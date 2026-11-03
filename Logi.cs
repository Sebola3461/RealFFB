using System;
using System.Runtime.InteropServices;

namespace RealFFB
{
    // Chamadas diretas à DLL da Logitech que o jogo já carregou (mesma instância da SDK, então
    // o LogiUpdate que o jogo faz todo frame vale para nós também). Não passa pelo LogitechGSDK
    // do jogo, que fica livre para ser bloqueado pelo Harmony sem bloquear o mod.
    // Os efeitos são cacheados: mola, amortecedor e superfície só são reenviados quando mudam,
    // porque cada Play recria parâmetros no driver e reenviar todo frame dá "estalos".
    internal static unsafe class Logi
    {
        const string Dll = "LogitechSteeringWheelEnginesWrapper.dll";
        internal const int Indice = 0;

        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string nome);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr mod, string nome);

        static IntPtr _mod;
        static delegate* unmanaged<int, byte> _conectado, _temFfb, _pararConst, _pararMola, _pararAmort, _pararSup, _pararSoft;
        static delegate* unmanaged<int, int, byte> _const, _amort, _lateral, _frontal, _soft, _setFaixa;
        static delegate* unmanaged<int, int*, byte> _getFaixa;
        static delegate* unmanaged<int, int, int, int, byte> _mola, _sup;
        static delegate* unmanaged<int, IntPtr> _estado;
        static delegate* unmanaged<int, float, float, float, byte> _leds;

        internal static bool Carregado => _mod != IntPtr.Zero;

        // A DLL só aparece no processo depois da primeira chamada do jogo; tenta de novo até achar.
        internal static bool Carregar()
        {
            if (_mod != IntPtr.Zero) return true;
            var m = GetModuleHandleW(Dll);
            if (m == IntPtr.Zero) return false;
            IntPtr F(string n)
            {
                var p = GetProcAddress(m, n);
                if (p == IntPtr.Zero) throw new EntryPointNotFoundException(n);
                return p;
            }
            _conectado = (delegate* unmanaged<int, byte>)F("LogiIsConnected");
            _temFfb = (delegate* unmanaged<int, byte>)F("LogiHasForceFeedback");
            _const = (delegate* unmanaged<int, int, byte>)F("LogiPlayConstantForce");
            _pararConst = (delegate* unmanaged<int, byte>)F("LogiStopConstantForce");
            _mola = (delegate* unmanaged<int, int, int, int, byte>)F("LogiPlaySpringForce");
            _pararMola = (delegate* unmanaged<int, byte>)F("LogiStopSpringForce");
            _amort = (delegate* unmanaged<int, int, byte>)F("LogiPlayDamperForce");
            _pararAmort = (delegate* unmanaged<int, byte>)F("LogiStopDamperForce");
            _sup = (delegate* unmanaged<int, int, int, int, byte>)F("LogiPlaySurfaceEffect");
            _pararSup = (delegate* unmanaged<int, byte>)F("LogiStopSurfaceEffect");
            _lateral = (delegate* unmanaged<int, int, byte>)F("LogiPlaySideCollisionForce");
            _frontal = (delegate* unmanaged<int, int, byte>)F("LogiPlayFrontalCollisionForce");
            _soft = (delegate* unmanaged<int, int, byte>)F("LogiPlaySoftstopForce");
            _pararSoft = (delegate* unmanaged<int, byte>)F("LogiStopSoftstopForce");
            _setFaixa = (delegate* unmanaged<int, int, byte>)F("LogiSetOperatingRange");
            _getFaixa = (delegate* unmanaged<int, int*, byte>)F("LogiGetOperatingRange");
            _estado = (delegate* unmanaged<int, IntPtr>)F("LogiGetStateENGINES");
            _leds = (delegate* unmanaged<int, float, float, float, byte>)F("LogiPlayLeds");
            _mod = m;
            Log.Msg("SDK da Logitech encontrada no processo do jogo");
            return true;
        }

        internal static bool Conectado() => _conectado(Indice) != 0;
        internal static bool TemFfb() => _temFfb(Indice) != 0;

        // Posição do eixo do volante (-32768 esquerda .. 32767 direita).
        internal static int PosicaoX()
        {
            var p = _estado(Indice);
            return p == IntPtr.Zero ? 0 : Marshal.ReadInt32(p);
        }

        static int _ultConst = int.MinValue;
        internal static void Constante(int pct)
        {
            pct = Math.Clamp(pct, -100, 100);
            if (pct == _ultConst) return;
            _ultConst = pct;
            _const(Indice, pct);
        }

        static int _molaOff = int.MinValue, _molaSat, _molaCoef;
        internal static void Mola(int offset, int saturacao, int coef)
        {
            saturacao = Math.Clamp(saturacao, 0, 100); coef = Math.Clamp(coef, -100, 100);
            if (saturacao == 0 || coef == 0) { PararMola(); return; }
            if (offset == _molaOff && saturacao == _molaSat && coef == _molaCoef) return;
            _molaOff = offset; _molaSat = saturacao; _molaCoef = coef;
            _mola(Indice, offset, saturacao, coef);
        }
        internal static void PararMola()
        {
            if (_molaOff == int.MinValue) return;
            _molaOff = int.MinValue;
            _pararMola(Indice);
        }

        static int _ultAmort = int.MinValue;
        internal static void Amortecedor(int coef)
        {
            coef = Math.Clamp(coef, -100, 100);
            if (coef == _ultAmort) return;
            _ultAmort = coef;
            if (coef == 0) _pararAmort(Indice); else _amort(Indice, coef);
        }

        // Tipos da SDK: 0 senoide, 1 quadrada, 2 triangular.
        static int _supMag = 0, _supPer = 0;
        internal static void Superficie(int magnitude, int periodoMs)
        {
            magnitude = Math.Clamp(magnitude, 0, 100);
            periodoMs = Math.Clamp(periodoMs, 8, 500);
            if (magnitude == 0) { PararSuperficie(); return; }
            // Recriar a senoide reinicia a fase; só manda quando a mudança é perceptível.
            if (_supMag != 0 && Math.Abs(magnitude - _supMag) < 2 && Math.Abs(periodoMs - _supPer) < 4) return;
            _supMag = magnitude; _supPer = periodoMs;
            _sup(Indice, 0, magnitude, periodoMs);
        }
        internal static void PararSuperficie()
        {
            if (_supMag == 0) return;
            _supMag = 0;
            _pararSup(Indice);
        }

        internal static void ColisaoLateral(int pct) => _lateral(Indice, Math.Clamp(pct, -100, 100));
        internal static void ColisaoFrontal(int pct) => _frontal(Indice, Math.Clamp(pct, 0, 100));

        internal static int FaixaGiro()
        {
            int r = 0;
            return _getFaixa(Indice, &r) != 0 ? r : 0;
        }
        internal static void DefinirFaixaGiro(int graus) => _setFaixa(Indice, graus);

        internal static void Leds(float rpm, float primeiro, float vermelho) => _leds(Indice, rpm, primeiro, vermelho);

        // Para tudo que o mod toca e esquece o cache (o próximo Play sempre sai).
        internal static void PararTudo()
        {
            if (_mod == IntPtr.Zero) return;
            _pararConst(Indice); _ultConst = int.MinValue;
            _pararMola(Indice); _molaOff = int.MinValue;
            _pararAmort(Indice); _ultAmort = int.MinValue;
            _pararSup(Indice); _supMag = 0;
            _pararSoft(Indice);
        }
    }
}
