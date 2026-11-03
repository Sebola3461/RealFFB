using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RealFFB
{
    // Ângulo de giro direto pelo USB, para quando a SDK não consegue (o LGS 5.10 antigo, só com
    // o Profiler, não responde a LogiSetOperatingRange). É o comando nativo dos Logitech com
    // motor de engrenagem, o mesmo que o hid-lg4ff do Linux manda: F8 81 <graus lo> <graus hi>.
    // Só vale para quem está no modo nativo (o G25 em modo "Driving Force EX" fica em 200°).
    internal static class Hid
    {
        // PIDs do modo nativo que aceitam F8 81.
        static readonly (string pid, string nome)[] Modelos =
        {
            ("c299", "G25"), ("c29b", "G27"), ("c29a", "Driving Force GT"), ("c24f", "G29 (modo PS3)"), ("c260", "G29"),
        };

        internal static string Modelo = "";

        // Retorna true se achou um volante compatível e o comando foi aceito.
        internal static bool DefinirFaixa(int graus)
        {
            graus = Math.Clamp(graus, 40, 900);
            foreach (var caminho in Caminhos())
            {
                string c = caminho.ToLowerInvariant();
                if (!c.Contains("vid_046d")) continue;
                string nome = null;
                foreach (var m in Modelos) if (c.Contains("pid_" + m.pid)) { nome = m.nome; break; }
                if (nome == null) continue;

                using var h = CreateFile(caminho, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (h.IsInvalid) continue;
                int tam = TamanhoSaida(h);
                if (tam < 8) continue;   // a coleção do volante tem relatório de saída de 7 bytes + id
                var buf = new byte[tam];
                buf[0] = 0; buf[1] = 0xF8; buf[2] = 0x81; buf[3] = (byte)(graus & 0xFF); buf[4] = (byte)(graus >> 8);
                bool ok = WriteFile(h, buf, buf.Length, out int escrito, IntPtr.Zero) && escrito == buf.Length;
                if (!ok) ok = HidD_SetOutputReport(h, buf, buf.Length);
                if (!ok) continue;
                Modelo = nome;
                return true;
            }
            return false;
        }

        static int TamanhoSaida(SafeFileHandle h)
        {
            if (!HidD_GetPreparsedData(h, out IntPtr pd)) return 0;
            try { return HidP_GetCaps(pd, out HidpCaps caps) == HidpStatusSuccess ? caps.OutputReportByteLength : 0; }
            finally { HidD_FreePreparsedData(pd); }
        }

        static System.Collections.Generic.IEnumerable<string> Caminhos()
        {
            HidD_GetHidGuid(out Guid guid);
            IntPtr lista = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (lista == new IntPtr(-1)) yield break;
            try
            {
                var dado = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                for (int i = 0; SetupDiEnumDeviceInterfaces(lista, IntPtr.Zero, ref guid, i, ref dado); i++)
                {
                    SetupDiGetDeviceInterfaceDetail(lista, ref dado, IntPtr.Zero, 0, out int precisa, IntPtr.Zero);
                    if (precisa <= 0) continue;
                    IntPtr det = Marshal.AllocHGlobal(precisa);
                    string caminho = null;
                    try
                    {
                        Marshal.WriteInt32(det, 8);   // cbSize de SP_DEVICE_INTERFACE_DETAIL_DATA_W em x64
                        if (SetupDiGetDeviceInterfaceDetail(lista, ref dado, det, precisa, out _, IntPtr.Zero))
                            caminho = Marshal.PtrToStringUni(det + 4);
                    }
                    finally { Marshal.FreeHGlobal(det); }
                    if (caminho != null) yield return caminho;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(lista); }
        }

        const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;
        const int DigcfPresent = 2, DigcfDeviceInterface = 0x10, HidpStatusSuccess = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        struct SpDeviceInterfaceData { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HidpCaps
        {
            public short Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public short[] Reserved;
            public short NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pd);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pd);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pd, out HidpCaps caps);
        [DllImport("hid.dll")] static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerador, IntPtr janela, int flags);
        [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr lista, IntPtr info, ref Guid g, int i, ref SpDeviceInterfaceData d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr lista, ref SpDeviceInterfaceData d, IntPtr det, int tam, out int precisa, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr lista);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string nome, uint acesso, uint compart, IntPtr seg, uint criacao, uint flags, IntPtr modelo);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, byte[] buf, int len, out int escrito, IntPtr ov);
    }
}
