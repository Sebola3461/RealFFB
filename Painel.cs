using System;
using System.Collections.Generic;
using MelonLoader;
using PBSLoaderCommonUtils;
using UnityEngine;

namespace RealFFB
{
    // Painel de ajustes ao vivo (Ctrl+Alt+F10). IMGUI desenhado à mão, no visual do PBSLoaderCommonInterface:
    // painéis chapados escuros, destaque vermelho, seleção invertida em branco.
    // Os controles são próprios (trilho + preenchimento) em vez da skin padrão da Unity.
    internal static class Painel
    {
        internal static bool Aberto;
        static Vector2 _rolagem;
        static string _dica = "";
        static int _arrastando = -1;
        static bool _sujo;

        static Texture2D _fundo, _faixa, _trilho, _destaque, _branco, _alerta;
        static GUIStyle _titulo, _texto, _fraco, _fracoDireita, _valor, _secao, _botao, _botaoAtivo, _dicaEstilo;

        static readonly Color CorFundo = new(0.07f, 0.075f, 0.085f, 0.96f);
        static readonly Color CorFaixa = new(0.12f, 0.125f, 0.14f, 1f);
        static readonly Color CorTrilho = new(0.22f, 0.23f, 0.25f, 1f);
        static readonly Color CorDestaque = new(0.839f, 0f, 0.184f, 1f);   // #D6002F
        static readonly Color CorAlerta = new(1f, 0.72f, 0.1f, 1f);
        static readonly Color CorTexto = new(0.93f, 0.94f, 0.95f, 1f);
        static readonly Color CorFraco = new(0.62f, 0.64f, 0.68f, 1f);

        abstract class Item { internal string Rotulo, Dica; }
        sealed class Secao : Item { }
        sealed class Deslizante : Item
        {
            internal MelonPreferences_Entry<float> E; internal float Min, Max; internal Func<float, string> Fmt;
        }
        sealed class Chave : Item { internal MelonPreferences_Entry<bool> E; }

        static List<Item> _itens;

        static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
        static string Vezes(float v) => "x" + v.ToString("0.00");
        static string Ms(float v) => Mathf.RoundToInt(v * 1000f) + " ms";

        static void Montar()
        {
            Deslizante D(MelonPreferences_Entry<float> e, string r, float min, float max, Func<float, string> f) =>
                new Deslizante { E = e, Rotulo = r, Min = min, Max = max, Fmt = f, Dica = e.Description };
            Secao S(string r) => new Secao { Rotulo = r };
            _itens = new List<Item>
            {
                S("GERAL"),
                D(Cfg.FaixaGiro, "Ângulo do volante", 0f, 1080f, v => v < 40f ? "G HUB" : Mathf.RoundToInt(v) + "°"),
                D(Cfg.Forca, "Força geral", 0f, 2f, Pct),
                D(Cfg.ForcaMinima, "Força mínima", 0f, 0.2f, Pct),
                D(Cfg.Curva, "Curva", 0.5f, 2f, Vezes),
                D(Cfg.Suavizacao, "Suavização", 0f, 0.15f, Ms),
                D(Cfg.Antecipacao, "Antecipação", 0f, 0.15f, Ms),
                S("PNEUS DA FRENTE"),
                D(Cfg.Autoalinhamento, "Autoalinhamento", 0f, 2f, Pct),
                D(Cfg.AceleracaoReferencia, "Força cheia em", 0.1f, 0.6f, v => v.ToString("0.00") + " g"),
                D(Cfg.TrilhaMecanica, "Parte do cáster", 0f, 1f, Pct),
                D(Cfg.PerdaNoLimite, "Fica leve no limite", 0f, 1f, Pct),
                D(Cfg.LimiteHidraulica, "Limite da hidráulica", 0.2f, 1f, Pct),
                S("DIREÇÃO"),
                D(Cfg.Centralizacao, "Centralização andando", 0f, 0.6f, Pct),
                D(Cfg.CentralizacaoParado, "Centralização parado", 0f, 0.4f, Pct),
                D(Cfg.AmortecedorParado, "Peso parado", 0f, 1f, Pct),
                D(Cfg.AmortecedorAndando, "Peso andando", 0f, 0.6f, Pct),
                D(Cfg.AmortecedorSemMotor, "Peso sem motor", 0f, 1f, Pct),
                D(Cfg.GanhoSemMotor, "Força sem motor", 1f, 3f, Vezes),
                D(Cfg.Atrito, "Atrito", 0f, 0.3f, Pct),
                S("PISTA E MOTOR"),
                D(Cfg.Solavancos, "Solavancos", 0f, 2f, Pct),
                D(Cfg.Textura, "Textura da pista", 0f, 2f, Pct),
                D(Cfg.Asfalto, "Vibração do asfalto", 0f, 0.15f, Pct),
                D(Cfg.VibracaoMotor, "Motor em marcha lenta", 0f, 0.3f, Pct),
                D(Cfg.Impactos, "Impactos", 0f, 2f, Pct),
                D(Cfg.ImpactoLimite, "Sensibilidade a impacto", 5f, 50f, v => v.ToString("0") + " m/s²"),
                S("VOLANTE"),
                new Chave { E = Cfg.Leds, Rotulo = "LEDs de rotação", Dica = Cfg.Leds.Description },
            };
        }

        internal static void Alternar()
        {
            Aberto = !Aberto;
            if (!Aberto && _sujo) { MelonPreferences.Save(); _sujo = false; }
        }

        // A cada frame: cursor solto enquanto o painel está aberto (quem decide o cursor é o PBSLoaderCommonUtils).
        internal static void ManterCursor() => Mouse.Soltar(Cfg.Id, Aberto);

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        static GUIStyle Estilo(int tam, Color cor, FontStyle f = FontStyle.Normal, TextAnchor al = TextAnchor.MiddleLeft)
        {
            var s = new GUIStyle();
            s.fontSize = tam; s.fontStyle = f; s.alignment = al;
            s.normal.textColor = cor;
            s.wordWrap = false; s.clipping = TextClipping.Clip;
            return s;
        }

        static void Preparar()
        {
            if (_fundo != null) return;   // texturas morrem junto com a cena às vezes; recria
            _fundo = Tex(CorFundo); _faixa = Tex(CorFaixa); _trilho = Tex(CorTrilho);
            _destaque = Tex(CorDestaque); _branco = Tex(Color.white); _alerta = Tex(CorAlerta);
            _titulo = Estilo(20, CorTexto, FontStyle.BoldAndItalic);
            _texto = Estilo(13, CorTexto);
            _fraco = Estilo(12, CorFraco);
            _fracoDireita = Estilo(12, CorFraco, FontStyle.Normal, TextAnchor.MiddleRight);
            _valor = Estilo(13, CorTexto, FontStyle.Bold, TextAnchor.MiddleRight);
            _secao = Estilo(12, CorDestaque, FontStyle.Bold);
            _dicaEstilo = Estilo(12, CorFraco); _dicaEstilo.wordWrap = true; _dicaEstilo.alignment = TextAnchor.UpperLeft;
            _botao = Estilo(12, CorTexto, FontStyle.Bold, TextAnchor.MiddleCenter);
            _botao.normal.background = _trilho; _botao.hover.background = _destaque; _botao.hover.textColor = Color.white;
            _botaoAtivo = Estilo(12, Color.black, FontStyle.Bold, TextAnchor.MiddleCenter);
            _botaoAtivo.normal.background = _branco; _botaoAtivo.hover.background = _branco; _botaoAtivo.hover.textColor = Color.black;
            if (_itens == null) Montar();
        }

        static void Caixa(Rect r, Texture2D t) => GUI.DrawTexture(r, t);

        internal static void Desenhar()
        {
            if (!Aberto) return;
            Preparar();
            var ev = Event.current;
            float w = 500f, h = Mathf.Min(Screen.height - 48f, 880f);
            var janela = new Rect(24f, 24f, w, h);
            Caixa(janela, _fundo);
            Caixa(new Rect(janela.x, janela.y, 4f, h), _destaque);

            float x = janela.x + 20f, y = janela.y + 14f, cw = w - 40f;
            GUI.Label(new Rect(x, y, cw, 26f), "REALFFB", _titulo);
            GUI.Label(new Rect(x, y, cw, 26f), Cfg.TeclaPainel.Combinacao.Legivel() + " fecha", _fracoDireita);
            y += 28f;
            GUI.Label(new Rect(x, y, cw, 18f), "Force feedback · " + Ffb.Estado + (Ffb.FaixaAtual > 0 ? $"   ·   volante em {Ffb.FaixaAtual}°" : ""), _fraco);
            y += 24f;

            y = Medidor(x, y, cw);

            // Presets + ações
            float bw = (cw - 3 * 6f) / 4f;
            for (int i = 0; i < Cfg.Presets.Length; i++)
                if (GUI.Button(new Rect(x + i * (bw + 6f), y, bw, 28f), Cfg.Presets[i].nome.ToUpperInvariant(), _botao))
                    Cfg.AplicarPreset(i);
            bool lig = Cfg.Ligado.Value;
            if (GUI.Button(new Rect(x + 3 * (bw + 6f), y, bw, 28f), lig ? "LIGADO" : "DESLIGADO", lig ? _botaoAtivo : _botao))
            { Cfg.Ligado.Value = !lig; _sujo = true; }
            y += 34f;
            float hw = (cw - 6f) / 2f;
            if (GUI.Button(new Rect(x, y, hw, 26f), "TESTAR DIREÇÃO", _botao)) Calibracao.Pedir();
            string sentido = Cfg.SinalConstante.Value == 0 ? "SENTIDO: ?" : Cfg.SinalConstante.Value > 0 ? "SENTIDO: +" : "SENTIDO: −";
            if (GUI.Button(new Rect(x + hw + 6f, y, hw, 26f), "INVERTER FORÇA  (" + sentido + ")", _botao))
                Cfg.InverterForca();
            y += 30f;
            if (GUI.Button(new Rect(x, y, cw, 26f), "CONTROLE:  " + TextoControle(), _botao)) ProximoControle();
            y += 30f;
            if (Calibracao.Resultado.Length > 0) { GUI.Label(new Rect(x, y, cw, 18f), Calibracao.Resultado, _fraco); y += 20f; }

            // Lista rolável
            float dicaH = 54f;
            var area = new Rect(x - 4f, y + 4f, cw + 8f, janela.yMax - y - dicaH - 18f);
            float linha = 30f, altura = 0f;
            foreach (var it in _itens) altura += it is Secao ? 32f : linha;
            _dica = "";
            _rolagem = GUI.BeginScrollView(area, _rolagem, new Rect(0f, 0f, cw - 10f, altura));
            float ly = 0f, lw = cw - 14f;
            for (int i = 0; i < _itens.Count; i++)
            {
                var it = _itens[i];
                if (it is Secao)
                {
                    GUI.Label(new Rect(4f, ly + 10f, lw, 18f), it.Rotulo, _secao);
                    Caixa(new Rect(4f, ly + 29f, lw, 1f), _trilho);
                    ly += 32f; continue;
                }
                var r = new Rect(4f, ly, lw, linha);
                bool sobre = r.Contains(ev.mousePosition);
                if (sobre) { Caixa(r, _faixa); _dica = it.Dica; }
                GUI.Label(new Rect(r.x + 6f, r.y, 170f, linha), it.Rotulo, _texto);
                if (it is Deslizante d) Controle(i, d, new Rect(r.x + 180f, r.y + 11f, lw - 250f, 8f), new Rect(r.xMax - 64f, r.y, 58f, linha), ev);
                else if (it is Chave c)
                {
                    var b = new Rect(r.xMax - 64f, r.y + 5f, 58f, 20f);
                    if (GUI.Button(b, c.E.Value ? "SIM" : "NÃO", c.E.Value ? _botaoAtivo : _botao)) { c.E.Value = !c.E.Value; _sujo = true; }
                }
                ly += linha;
            }
            GUI.EndScrollView();

            var dr = new Rect(x, janela.yMax - dicaH - 10f, cw, dicaH);
            Caixa(new Rect(x, dr.y - 6f, cw, 1f), _trilho);
            GUI.Label(dr, !string.IsNullOrEmpty(_dica) ? _dica : "Passe o mouse num ajuste para ver o que ele faz. Tudo vale na hora e é salvo ao fechar.", _dicaEstilo);
        }

        static string TextoControle()
        {
            string atual = Logi.Indice >= 0 ? $"{Logi.Indice} · {Logi.Nome}" : "nenhum";
            return Cfg.Controle.Value < 0 ? "AUTOMÁTICO  (" + atual + ")" : atual;
        }

        // Automático, depois cada controle conectado em ordem, e volta ao automático.
        static void ProximoControle()
        {
            int v = Cfg.Controle.Value;
            do v = v + 1 >= Logi.MaxControles ? -1 : v + 1;
            while (v >= 0 && !Logi.ControleConectado(v));
            Cfg.Controle.Value = v;
            _sujo = true;
        }

        // Força saindo agora, centrada no zero; vira amarela quando satura (clip).
        static float Medidor(float x, float y, float cw)
        {
            var barra = new Rect(x, y, cw, 14f);
            Caixa(barra, _trilho);
            float meio = barra.x + barra.width / 2f;
            float s = Mathf.Clamp(Ffb.Saida, -1f, 1f);
            var cheia = s >= 0 ? new Rect(meio, barra.y, barra.width / 2f * s, barra.height) : new Rect(meio + barra.width / 2f * s, barra.y, -barra.width / 2f * s, barra.height);
            Caixa(cheia, Ffb.Clip > 0.05f ? _alerta : _destaque);
            Caixa(new Rect(meio - 1f, barra.y - 3f, 2f, barra.height + 6f), _branco);
            y += 18f;
            string info = $"{Mathf.RoundToInt(Ffb.Saida * 100f)}%   ·   clip {Mathf.RoundToInt(Ffb.Clip * 100f)}%   ·   pneu {Ffb.Uso:0.00}   ·   {Ffb.VelKmh:0} km/h   ·   {(Ffb.MotorLigado ? "motor ligado" : "motor desligado")}";
            GUI.Label(new Rect(x, y, cw, 18f), info, _fraco);
            return y + 26f;
        }

        static void Controle(int id, Deslizante d, Rect trilho, Rect rotuloValor, Event ev)
        {
            float t = Mathf.InverseLerp(d.Min, d.Max, d.E.Value);
            Caixa(trilho, _trilho);
            Caixa(new Rect(trilho.x, trilho.y, trilho.width * t, trilho.height), _destaque);
            Caixa(new Rect(trilho.x + trilho.width * t - 3f, trilho.y - 5f, 6f, trilho.height + 10f), _branco);
            GUI.Label(rotuloValor, d.Fmt(d.E.Value), _valor);

            var alvo = new Rect(trilho.x - 6f, trilho.y - 10f, trilho.width + 12f, trilho.height + 20f);
            var tipo = ev.type;
            if (tipo == EventType.MouseDown && ev.button == 0 && alvo.Contains(ev.mousePosition)) { _arrastando = id; ev.Use(); }
            if (_arrastando == id && (tipo == EventType.MouseDrag || tipo == EventType.MouseDown))
            {
                float nt = Mathf.Clamp01((ev.mousePosition.x - trilho.x) / trilho.width);
                float v = Mathf.Lerp(d.Min, d.Max, nt);
                float passo = (d.Max - d.Min) >= 100f ? 10f : (d.Max - d.Min) / 200f;
                d.E.Value = Mathf.Round(v / passo) * passo;
                _sujo = true;
                ev.Use();
            }
            if (tipo == EventType.MouseUp && _arrastando == id) { _arrastando = -1; ev.Use(); }
        }
    }
}
