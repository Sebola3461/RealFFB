using System;
using MelonLoader;

[assembly: MelonAdditionalDependencies("PBSLoaderCommonUtils", "PBSLoaderCommonInterface")]

namespace RealFFB
{
    // Coloca o mod no menu da PBSLoaderCommonInterface (ações sem atalho de teclado e sliders com faixa). Ela é base do PBSLoader,
    // sempre instalada junto com a PBSLoaderCommonUtils.
    internal static class UiIntegracao
    {
        internal static void Registrar()
        {
            try { Fazer(); }
            catch (Exception e) { MelonLogger.Warning("Integração com a PBSLoaderCommonInterface falhou: " + e.Message); }
        }

        static void Fazer()
        {
            const string C = Cfg.Id;
            PBSLoaderCommonInterface.PainelMods.Nome(C, "Volante (force feedback)");
            PBSLoaderCommonInterface.PainelMods.Acao(C, "Ajustes do force feedback", Painel.Alternar, "Força, curva e efeitos do volante");
            PBSLoaderCommonInterface.PainelMods.Acao(C, "Inverter força", Cfg.InverterForca, "Para quando o volante gira sozinho para o lado errado ou foge do centro");
            PBSLoaderCommonInterface.PainelMods.Acao(C, "Telemetria", Telemetria.Alternar, "Liga ou desliga a gravação de telemetria");
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "Forca", 0f, 2f, 0.05f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "ForcaMinima", 0f, 0.2f, 0.01f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "Curva", 0.3f, 2.5f, 0.05f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "Suavizacao", 0f, 0.2f, 0.005f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "Antecipacao", 0f, 0.15f, 0.005f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "Autoalinhamento", 0f, 2f, 0.05f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "AceleracaoReferencia", 0.1f, 1f, 0.01f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "TrilhaMecanica", 0f, 1f, 0.05f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "PerdaNoLimite", 0f, 1f, 0.05f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "Atrito", 0f, 0.3f, 0.01f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "LimiteHidraulica", 0.2f, 1f, 0.05f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "AmortecedorAndando", 0f, 0.6f, 0.01f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "VibracaoMotor", 0f, 0.3f, 0.01f);
        }
    }
}
