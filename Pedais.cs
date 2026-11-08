using System;
using HarmonyLib;
using Il2Cpp;

namespace RealFFB
{
    // Embreagem dos volantes que não são G25/G27. O VPDeviceInputMepStatic do jogo (modelo "LogitechG27")
    // lê a embreagem sempre de rglSlider[1], com o pedal solto em +32767. O G29 manda o pedal em outro eixo;
    // o rglSlider[1] fica em 0 e o jogo vê a embreagem pela metade o tempo todo.
    // Aqui o eixo é descoberto sozinho: pedal solto fica num extremo (é assim que o jogo começa) e, ao ser
    // pisado, sai dele. Achado o eixo, a embreagem do jogo é recalculada depois do Update dele.
    internal static class Pedais
    {
        const int EixoDoJogo = 7;
        // lZ, lRx, lRy, rglSlider[0], rglSlider[1]. lX, lY e lRz são volante, acelerador e freio.
        static readonly int[] Candidatos = { 2, 3, 4, 6, 7 };
        static readonly string[] Nomes = { "lX", "lY", "lZ", "lRx", "lRy", "lRz", "Slider 0", "Slider 1" };
        const int Extremo = 30000, Pisado = 20000;

        // Por campo do DIJOYSTATE2: sinal do pedal solto (0 = ainda não visto num extremo) e se já foi pisado.
        static readonly int[] _solto = new int[8];
        static readonly bool[] _pisado = new bool[8];
        static int _eixo = -1, _indiceAnterior = -1;
        static bool _jogoServe;
        internal static string Estado = "";

        internal static void Instalar(HarmonyLib.Harmony harmony)
        {
            try
            {
                var alvo = AccessTools.Method(typeof(VPDeviceInputMepStatic), nameof(VPDeviceInputMepStatic.Update));
                harmony.Patch(alvo, postfix: new HarmonyMethod(typeof(Pedais), nameof(DepoisDoUpdate)));
            }
            catch (Exception e)
            {
                Log.Aviso("Não deu para corrigir a embreagem nem inverter o giro do volante: " + e.Message);
            }
        }

        static void DepoisDoUpdate(VPDeviceInputMepStatic __instance)
        {
            try
            {
                // O force feedback aprende o sentido do esterço pelo eixo físico, então se ajusta sozinho à inversão.
                if (Cfg.InverterGiro.Value) VPDeviceInputMepStatic.volanteSteering = -VPDeviceInputMepStatic.volanteSteering;
                Corrigir(__instance);
            }
            catch (Exception e) { Log.Erro("Embreagem", e); }
        }

        static void Corrigir(VPDeviceInputMepStatic entrada)
        {
            int escolha = Cfg.EixoEmbreagem.Value;
            if (escolha == 0 || !Logi.Carregado || Logi.Indice < 0) return;
            if (entrada.deviceModel != VPDeviceInputMepStatic.DeviceModel.LogitechG27) return;
            if (Logi.Indice != _indiceAnterior) Esquecer();

            int eixo = escolha > 0 ? Math.Clamp(escolha, 1, Nomes.Length - 1) : Descobrir();
            if (eixo < 0)
            {
                // Sem pedal achado, o jogo leria meia embreagem de um eixo vazio; solta até o pedal aparecer.
                if (!_jogoServe) VPDeviceInputMepStatic.volanteEmbreagem = 0f;
                return;
            }
            if (!Logi.LerEixo(eixo, out int v)) return;
            int solto = escolha > 0 && _solto[eixo] == 0 ? 1 : _solto[eixo];
            float pedal = solto > 0 ? 1f - (v + 32767f) / 65535f : (v + 32768f) / 65535f;
            VPDeviceInputMepStatic.volanteEmbreagem = Faixa(pedal, entrada.clutchRangeMin, entrada.clutchRangeMax);
        }

        // -1 enquanto não há pedal certo, e também quando o eixo do jogo já serve (aí o jogo cuida sozinho).
        static int Descobrir()
        {
            if (_jogoServe) return -1;
            foreach (int c in Candidatos)
            {
                if (!Logi.LerEixo(c, out int v)) return _eixo;
                if (_solto[c] == 0 && Math.Abs(v) >= Extremo) _solto[c] = Math.Sign(v);
                if (_solto[c] != 0 && Math.Abs(v - _solto[c] * 32767) >= Pisado) _pisado[c] = true;
            }
            if (_solto[EixoDoJogo] > 0)
            {
                _jogoServe = true;
                return -1;
            }
            if (_eixo >= 0) return _eixo;
            foreach (int c in Candidatos)
            {
                if (!_pisado[c]) continue;
                _eixo = c;
                Estado = Nomes[c] + (_solto[c] < 0 ? " (invertido)" : "");
                Log.Msg($"Embreagem achada no eixo {Estado} do controle {Logi.Indice} (o jogo lê do Slider 1)");
                break;
            }
            return _eixo;
        }

        static void Esquecer()
        {
            Array.Clear(_solto); Array.Clear(_pisado);
            _eixo = -1; _jogoServe = false; Estado = "";
            _indiceAnterior = Logi.Indice;
        }

        // Mesma conta do EffectivePedalRange do jogo.
        static float Faixa(float valor, float min, float max)
        {
            if (min >= max) return valor >= min ? 1f : 0f;
            return Math.Clamp((valor - min) / (max - min), 0f, 1f);
        }
    }
}
