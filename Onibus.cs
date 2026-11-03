using System;
using Il2Cpp;
using Il2CppMep;
using Il2CppVehiclePhysics;

namespace RealFFB
{
    // Ônibus do jogador, pelo SDK: o controlador VPP que tem as rodas que esterçam (Avancado.VppDirigido;
    // nos articulados é o da frente) e o que tem o motor (para saber se a direção hidráulica está
    // funcionando e para os LEDs de rotação).
    internal static class Onibus
    {
        internal static MEPControlesBus Ctrl => Avancado.Controles;
        internal static VPVehicleController Dirigido => Avancado.VppDirigido;
        internal static VPVehicleController Motor => Avancado.Vpp ?? Avancado.VppDirigido;
        // Por roda do Dirigido (índice do wheelState): esterça? lado (-1 esquerda, +1 direita).
        internal static bool[] Esterca = Array.Empty<bool>();
        internal static float[] Lado = Array.Empty<float>();
        static IntPtr _medido;
        static int _geracao = -1;

        // Força medir de novo (o número de rodas mudou).
        internal static void CenaMudou()
        {
            _medido = IntPtr.Zero; _geracao = -1;
            Esterca = Array.Empty<bool>(); Lado = Array.Empty<float>();
        }

        // Barato: chamado uma vez por segundo.
        internal static void Manter()
        {
            var d = Dirigido;
            if (d == null) { if (Esterca.Length > 0) CenaMudou(); return; }
            if (d.Pointer == _medido && _geracao == Veiculo.Geracao && Esterca.Length > 0) return;
            _medido = d.Pointer; _geracao = Veiculo.Geracao;

            var ws = d.wheelState;
            int n = ws.Length;
            Esterca = new bool[n]; Lado = new float[n];
            int quantas = 0;
            for (int i = 0; i < n; i++)
            {
                var w = ws[i];
                Esterca[i] = w.steerable;
                Lado[i] = d.GetWheelLocalPosition(w.wheelCol).x < 0f ? -1f : 1f;
                if (w.steerable) quantas++;
            }
            Log.Msg($"Ônibus: {Veiculo.Raiz?.name}, {quantas} roda(s) que esterçam em {d.name}, motor em {(Motor != null ? Motor.name : "?")}");
        }

        // Canal de dados do VPP (Channel.Vehicle = 1). Rotação vem multiplicada por 1000.
        internal static bool MotorFuncionando
        {
            get
            {
                if (Motor == null) return true;
                return Motor.data.Get(1, VehicleData.EngineWorking) != 0;
            }
        }

        internal static float Rpm => Motor == null ? 0f : Motor.data.Get(1, VehicleData.EngineRpm) / 1000f;

        internal static void FaixaRpm(out float lenta, out float maxima)
        {
            lenta = 650f; maxima = 2800f;
            if (Motor == null || Motor.m_engine == null) return;
            var s = Motor.m_engine.settings;
            lenta = s.idleRpm; maxima = s.maxRpm;
        }
    }
}
