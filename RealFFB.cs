using System;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using MelonLoader;
using PBSLoaderCommonUtils;
using UnityEngine;

[assembly: MelonInfo(typeof(RealFFB.RealFFBMod), "RealFFB", "0.2.0", "nzrn")]
[assembly: MelonGame(null, "Proton Bus Simulator")]

namespace RealFFB
{
    // Force feedback de verdade para volantes Logitech no Proton.
    // O jogo original só manda uma mola de centralização proporcional à velocidade
    // (MEPControlesBus.Update: LogiPlaySpringForce(0, 0, kmh/150*100, kmh/150*100)) e nada mais.
    // Módulos:
    //   Logi        chamadas diretas à DLL da SDK que o jogo carregou
    //   Onibus      acha o controlador VPP que esterça e o do motor
    //   Ffb         modelo de força (autoalinhamento, cáster, amortecimento, pista, motor, impactos)
    //   Calibracao  descobre o sentido da força constante no driver
    //   Painel      janela de ajustes ao vivo (Ctrl+Alt+F10)
    // Teclas: Ctrl+Alt+F10 painel, Ctrl+Alt+Shift+F10 telemetria em UserData/RealFFB_telemetria.csv.
    public class RealFFBMod : MelonMod
    {
        float _proxima;
        internal static bool BloqueioOk;

        public override void OnLateInitializeMelon() => UiIntegracao.Registrar();


        public override void OnInitializeMelon()
        {
            Cfg.Criar();
            BloquearMolaDoJogo();
        }

        // A chamada à SDK fica embutida no MEPControlesBus.Update; o prefixo no LogitechGSDK
        // pega todas as chamadas do jogo. As do mod vão direto à DLL e não passam por aqui.
        void BloquearMolaDoJogo()
        {
            try
            {
                var alvo = AccessTools.Method(typeof(Il2Cpp.LogitechGSDK), nameof(Il2Cpp.LogitechGSDK.LogiPlaySpringForce));
                HarmonyInstance.Patch(alvo, prefix: new HarmonyMethod(typeof(RealFFBMod), nameof(PrefixoMola)));
                BloqueioOk = true;
            }
            catch (Exception e)
            {
                Log.Aviso("Não deu para bloquear a mola do jogo (ela vai brigar com o mod): " + e.Message);
            }
        }

        static bool PrefixoMola(ref bool __result)
        {
            if (!Ffb.Assumiu) return true;
            __result = true;
            return false;
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            _proxima = 0f;
            Onibus.CenaMudou();
            Ffb.CenaMudou();
        }

        public override void OnUpdate()
        {
            if (Time.unscaledTime >= _proxima)
            {
                _proxima = Time.unscaledTime + 1f;
                Seguro(Onibus.Manter, "Onibus");
            }
            Painel.ManterCursor();
        }

        public override void OnLateUpdate()
        {
            Seguro(Ffb.Quadro, "FFB");
            Seguro(Telemetria.Quadro, "Telemetria");
        }

        public override void OnGUI() => Seguro(Painel.Desenhar, "Painel");

        public override void OnApplicationQuit()
        {
            Telemetria.Fechar();
            Ffb.Soltar();
        }

        internal static void Seguro(Action a, string onde)
        {
            try { a(); }
            catch (Exception e) { Log.Erro(onde, e); }
        }
    }

    internal static class Log
    {
        static readonly System.Collections.Generic.Dictionary<string, float> _ultimo = new();
        static readonly System.Collections.Generic.HashSet<string> _comPilha = new();
        internal static void Msg(string s) => Melon<RealFFBMod>.Logger.Msg(s);
        internal static void Aviso(string s) => Melon<RealFFBMod>.Logger.Warning(s);
        // Erros repetidos (todo frame) aparecem no máximo a cada 10 s; a pilha só na primeira vez.
        internal static void Erro(string onde, Exception e)
        {
            float agora = Time.realtimeSinceStartup;
            if (_ultimo.TryGetValue(onde, out float t) && agora - t < 10f) return;
            _ultimo[onde] = agora;
            bool primeiro = _comPilha.Add(onde);
            Melon<RealFFBMod>.Logger.Warning(primeiro ? $"{onde}: {e}" : $"{onde}: {e.GetType().Name}: {e.Message}");
        }
    }

    internal static class Cfg
    {
        internal const string Id = "RealFFB";
        // Nome do mod até a 0.2. As opções e os atalhos gravados com ele são trazidos uma vez para o nome novo.
        const string IdAntigo = "VolanteReal";

        internal static MelonPreferences_Category Cat;
        static MelonPreferences_Entry<bool> _migrado;
        internal static Atalho TeclaPainel, TeclaTelemetria;
        internal static MelonPreferences_Entry<bool> Ligado, Leds;
        internal static MelonPreferences_Entry<int> SinalConstante;
        internal static MelonPreferences_Entry<float> Forca, ForcaMinima, Curva, Suavizacao,
            Autoalinhamento, AceleracaoReferencia, TrilhaMecanica, PerdaNoLimite,
            Centralizacao, CentralizacaoParado, AmortecedorParado, AmortecedorAndando, AmortecedorSemMotor, GanhoSemMotor,
            Solavancos, Textura, Asfalto, VibracaoMotor, Impactos, ImpactoLimite, FaixaGiro;

        internal static void Criar()
        {
            var c = Cat = MelonPreferences.CreateCategory(Id, "RealFFB (force feedback Logitech)");
            _migrado = c.CreateEntry("MigradoDoVolanteReal", false, is_hidden: true);
            TeclaPainel = Atalhos.Registrar(Id, "Painel", "Painel do volante", "Ctrl+Alt+F10", Painel.Alternar, "Abre o painel de ajustes do force feedback");
            TeclaTelemetria = Atalhos.Registrar(Id, "Telemetria", "Telemetria do volante", "Ctrl+Alt+Shift+F10",
                () => RealFFBMod.Seguro(Telemetria.Alternar, "Telemetria"), "Liga e desliga a gravação de UserData/RealFFB_telemetria.csv");
            Ligado = c.CreateEntry("Ligado", true, description: "Liga o force feedback do mod (false = o do jogo)");
            SinalConstante = c.CreateEntry("SinalConstante", 0, description: "Sentido da força constante no seu driver: 1 ou -1. 0 = testar sozinho com o ônibus parado");

            Forca = c.CreateEntry("Forca", 1.0f, description: "Força geral (multiplica tudo)");
            ForcaMinima = c.CreateEntry("ForcaMinima", 0.05f, description: "Força mínima: vence a zona morta das engrenagens/correia (G29/G920 ~0,05-0,08; direct drive 0)");
            Curva = c.CreateEntry("Curva", 1.0f, description: "Curva da força: abaixo de 1 realça forças pequenas, acima de 1 realça as grandes");
            Suavizacao = c.CreateEntry("Suavizacao", 0.03f, description: "Filtro do autoalinhamento, em segundos (tira aspereza, aumenta atraso)");

            Autoalinhamento = c.CreateEntry("Autoalinhamento", 0.8f, description: "Torque que os pneus da frente fazem para voltar ao centro em curva");
            AceleracaoReferencia = c.CreateEntry("AceleracaoReferencia", 0.30f, description: "Aceleração lateral (g) que dá força cheia");
            TrilhaMecanica = c.CreateEntry("TrilhaMecanica", 0.35f, description: "Parte do autoalinhamento que vem do cáster e não some no limite do pneu (0..1)");
            PerdaNoLimite = c.CreateEntry("PerdaNoLimite", 1.0f, description: "Quanto o volante fica leve quando os pneus da frente chegam no limite (0 = nada)");

            Centralizacao = c.CreateEntry("Centralizacao", 0.18f, description: "Mola de centralização andando (cáster/pino-mestre)");
            CentralizacaoParado = c.CreateEntry("CentralizacaoParado", 0.05f, description: "Mola de centralização parado");
            AmortecedorParado = c.CreateEntry("AmortecedorParado", 0.30f, description: "Peso/arrasto do volante parado (pneu arrastando no chão)");
            AmortecedorAndando = c.CreateEntry("AmortecedorAndando", 0.10f, description: "Peso/arrasto andando (caixa de direção)");
            AmortecedorSemMotor = c.CreateEntry("AmortecedorSemMotor", 0.75f, description: "Peso do volante com o motor desligado (sem direção hidráulica)");
            GanhoSemMotor = c.CreateEntry("GanhoSemMotor", 1.6f, description: "Multiplica autoalinhamento e mola com o motor desligado");

            Solavancos = c.CreateEntry("Solavancos", 0.6f, description: "Tranco no volante quando só uma roda da frente pega um buraco/lombada");
            Textura = c.CreateEntry("Textura", 0.5f, description: "Vibração da pista medida pela suspensão (paralelepípedo, pista irregular)");
            Asfalto = c.CreateEntry("Asfalto", 0.03f, description: "Vibração fina constante do asfalto andando");
            VibracaoMotor = c.CreateEntry("VibracaoMotor", 0.04f, description: "Vibração do motor diesel no volante, mais forte parado");
            Impactos = c.CreateEntry("Impactos", 0.8f, description: "Trancos de batida, meio-fio e buraco forte");
            ImpactoLimite = c.CreateEntry("ImpactoLimite", 18f, description: "Aceleração brusca (m/s²) a partir da qual conta como impacto");

            FaixaGiro = c.CreateEntry("GrausVolante", 900f, description: "Ângulo total de giro do volante, de batente a batente (o G HUB costuma deixar travado em outro valor). 0 = não mexe");
            Leds = c.CreateEntry("Leds", true, description: "LEDs de rotação do volante (G29/G923) acompanham o motor");

            if (!_migrado.Value)
            {
                try { MigrarDoNomeAntigo(); }
                catch (Exception e) { Log.Aviso("Não deu para trazer as opções do VolanteReal: " + e.Message); }
            }
        }

        // Para ler o valor gravado no .cfg é preciso criar a entrada com o nome antigo: o MelonPreferences só
        // carrega do arquivo o que alguém registrou. Depois a categoria antiga sai do arquivo.
        static void MigrarDoNomeAntigo()
        {
            var antiga = MelonPreferences.CreateCategory(IdAntigo);
            int copiadas = 0;
            foreach (var nova in Cat.Entries)
            {
                if (nova == _migrado) continue;
                var velha = antiga.CreateEntry(nova.Identifier, nova.BoxedValue);
                if (!Equals(velha.BoxedValue, nova.BoxedValue)) { nova.BoxedValue = velha.BoxedValue; copiadas++; }
                antiga.DeleteEntry(nova.Identifier);
            }
            MelonPreferences.RemoveCategoryFromFile(Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "MelonPreferences.cfg"), IdAntigo);

            var atalhos = MelonPreferences.GetCategory(Atalhos.Categoria);
            foreach (var a in new[] { TeclaPainel, TeclaTelemetria })
            {
                string chaveAntiga = IdAntigo + "_" + a.Id;
                var velha = atalhos.CreateEntry(chaveAntiga, a.Combinacao.ToString());
                if (Combinacao.TentarLer(velha.Value, out var combinacao) && !combinacao.Equals(a.Combinacao)) { a.Definir(combinacao); copiadas++; }
                atalhos.DeleteEntry(chaveAntiga);
            }

            _migrado.Value = true;
            MelonPreferences.Save();
            if (copiadas > 0) Log.Msg($"{copiadas} opções trazidas do VolanteReal");
        }

        // Presets do painel. Ordem: Forca, ForcaMinima, Curva, Suavizacao, Autoalinhamento, AceleracaoReferencia,
        // TrilhaMecanica, PerdaNoLimite, Centralizacao, CentralizacaoParado, AmortecedorParado, AmortecedorAndando,
        // AmortecedorSemMotor, GanhoSemMotor, Solavancos, Textura, Asfalto, VibracaoMotor, Impactos.
        internal static readonly (string nome, float[] v)[] Presets =
        {
            ("Suave",    new[] { 0.7f, 0.04f, 1.1f, 0.05f, 0.6f, 0.35f, 0.40f, 0.7f, 0.14f, 0.04f, 0.22f, 0.08f, 0.60f, 1.4f, 0.3f, 0.25f, 0.01f, 0.02f, 0.4f }),
            ("Realista", new[] { 1.0f, 0.05f, 1.0f, 0.03f, 0.8f, 0.30f, 0.35f, 1.0f, 0.18f, 0.05f, 0.30f, 0.10f, 0.75f, 1.6f, 0.6f, 0.50f, 0.03f, 0.04f, 0.8f }),
            ("Forte",    new[] { 1.3f, 0.07f, 0.9f, 0.02f, 1.1f, 0.25f, 0.30f, 1.0f, 0.25f, 0.08f, 0.38f, 0.14f, 0.90f, 1.8f, 0.9f, 0.80f, 0.05f, 0.06f, 1.2f }),
        };

        internal static MelonPreferences_Entry<float>[] EntradasPreset => new[]
        {
            Forca, ForcaMinima, Curva, Suavizacao, Autoalinhamento, AceleracaoReferencia, TrilhaMecanica, PerdaNoLimite,
            Centralizacao, CentralizacaoParado, AmortecedorParado, AmortecedorAndando, AmortecedorSemMotor, GanhoSemMotor,
            Solavancos, Textura, Asfalto, VibracaoMotor, Impactos,
        };

        internal static void AplicarPreset(int i)
        {
            var e = EntradasPreset; var v = Presets[i].v;
            for (int k = 0; k < e.Length; k++) e[k].Value = v[k];
            MelonPreferences.Save();
            Log.Msg("Preset " + Presets[i].nome);
        }
    }

    // Ctrl+Alt+Shift+F10: uma linha por frame para calibrar o modelo com dados de verdade.
    internal static class Telemetria
    {
        static StreamWriter _w;
        static float _t0;

        internal static void Alternar()
        {
            if (_w != null) { Fechar(); Log.Msg("Telemetria parada"); return; }
            var arq = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, Cfg.Id + "_telemetria.csv");
            _w = new StreamWriter(arq, false, new UTF8Encoding(false));
            _w.WriteLine("t;kmh;autoalinha;solavanco;uso_pneu;carga_frente;saida;mola;amort;vibra;motor;volante_x");
            _t0 = Time.unscaledTime;
            Log.Msg("Telemetria gravando em " + arq);
        }

        internal static void Quadro()
        {
            if (_w == null || !Ffb.Assumiu) return;
            var ci = CultureInfo.InvariantCulture;
            _w.WriteLine(string.Join(";",
                (Time.unscaledTime - _t0).ToString("0.000", ci), Ffb.VelKmh.ToString("0.0", ci),
                Ffb.Autoalinha.ToString("0.000", ci), Ffb.Solavanco.ToString("0.000", ci), Ffb.Uso.ToString("0.00", ci),
                Ffb.CargaFrente.ToString("0", ci), Ffb.Saida.ToString("0.000", ci), Ffb.MolaCoef.ToString("0.00", ci),
                Ffb.Amort.ToString("0.00", ci), Ffb.Vibra.ToString("0.00", ci), Ffb.MotorLigado ? "1" : "0",
                Logi.PosicaoX().ToString(ci)));
        }

        internal static void Fechar()
        {
            _w?.Dispose();
            _w = null;
        }
    }
}
