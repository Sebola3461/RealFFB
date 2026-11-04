using System;
using UnityEngine;

namespace RealFFB
{
    // O force feedback em si. Roda no LateUpdate, depois do MEPControlesBus.Update (que chama o
    // LogiUpdate da SDK). Monta a força a partir da física do VPP:
    //
    //   constante  = autoalinhamento dos pneus da frente + solavancos      (calculada todo frame)
    //   mola       = cáster/pino-mestre: puxa para o centro, cresce com a velocidade   (no volante)
    //   amortecedor= arrasto do pneu parado e da caixa hidráulica; pesado sem motor     (no volante)
    //   superfície = textura da pista ou vibração do motor em marcha lenta              (no volante)
    //   colisões   = trancos da SDK quando a carroceria sofre um impacto
    //
    // Autoalinhamento: cada roda que esterça gera -Fy * (trilha mecânica + trilha pneumática).
    // A trilha pneumática encolhe conforme o pneu se aproxima do pico de escorregamento lateral
    // (GetWheelPeakSlip) e fica levemente negativa além dele: o volante "fica leve" antes de o
    // ônibus sair de frente, como num pneu de verdade. Normalizado pela carga do eixo dianteiro,
    // então 1.0 = aceleração lateral de referência (Cfg.AceleracaoReferencia, em g).
    internal static class Ffb
    {
        // true enquanto o mod é dono do FFB; o Harmony só bloqueia a mola do jogo nesse estado.
        internal static bool Assumiu;

        // Valores ao vivo para o painel e a telemetria.
        internal static float VelKmh, Autoalinha, Solavanco, Uso, Saida, Clip, Amort, MolaCoef, Vibra, CargaFrente, Atrito, VelVolante;
        internal static bool MotorLigado, Pausado;
        internal static string Estado = "aguardando o jogo";

        static float _satFiltrado, _cargaRef, _clipMedia;
        static float[] _compAnt = Array.Empty<float>(), _compBaixa = Array.Empty<float>();
        static float _texturaRms;
        static Vector3 _velAnt, _acelMedia;
        static bool _temVelAnt;
        static float _proximoImpacto;
        static float _sinalDirecao = 1f;
        static int _votosDirecao;
        // Orientação aprendida da física, sem depender de convenção do VPP:
        //   _esterçoDireita: +1 se steerAngle positivo vira o ônibus para a direita
        //   _sinalModelo:    +1 se o autoalinhamento calculado já se opõe ao esterço (como deve)
        static float _estercoDireita = 1f, _sinalModelo = 1f;
        static int _votosEsterco, _votosModelo;
        static float _proximaFaixa;
        internal static int FaixaAtual;

        // A SDK da Logitech só fala com o volante com a janela em foco: no Alt+Tab o driver descarta os efeitos,
        // mas o cache do Logi acha que eles continuam tocando. Na volta, tudo é reenviado algumas vezes, porque o
        // Profiler/G HUB reaplica o perfil do jogo logo depois de ele ganhar foco.
        static bool _semFoco;
        static readonly float[] Reenvios = { 0f, 0.5f, 1.5f };
        static float _focoVoltou = -1f;
        static int _reenvio = Reenvios.Length;

        static int _xAnt;
        static bool _temXAnt;

        internal static void CenaMudou()
        {
            Soltar();
            _compAnt = Array.Empty<float>(); _compBaixa = Array.Empty<float>();
            _temVelAnt = false; _cargaRef = 0f; _satFiltrado = 0f; _temXAnt = false;
            Calibracao.CenaMudou();
        }

        internal static void Soltar()
        {
            if (Assumiu)
            {
                try { Logi.PararTudo(); } catch { }
                Assumiu = false;
                _hidEnviado = -1; _faixaPedida = -1;
            }
        }

        internal static void Quadro()
        {
            if (!Cfg.Ligado.Value) { Soltar(); Estado = "desligado (ligue no painel)"; return; }
            if (!Logi.Carregar()) { Soltar(); Estado = "SDK da Logitech ainda não carregada pelo jogo"; return; }
            if (Onibus.Ctrl == null || Onibus.Dirigido == null) { Soltar(); Estado = "sem ônibus (menu)"; return; }
            if (!Logi.Conectado()) { Soltar(); Estado = "volante Logitech não conectado"; return; }
            if (!Assumiu)
            {
                Assumiu = true;
                Logi.PararTudo();   // apaga a mola que o jogo deixou ligada
                Log.Msg(Logi.TemFfb() ? "Volante conectado: o mod assumiu o force feedback" : "Volante conectado, mas a SDK diz que ele não tem force feedback");
            }

            if (!Application.isFocused)
            {
                _semFoco = true; _temXAnt = false;
                Estado = "jogo sem foco (volta quando a janela voltar)";
                return;
            }
            // Um quadro de mais de meio segundo também conta: o jogo pode ter ficado congelado em segundo plano.
            if (_semFoco || Time.unscaledDeltaTime > 0.5f)
            {
                _semFoco = false;
                _focoVoltou = Time.unscaledTime; _reenvio = 0;
                _hidEnviado = -1; _proximaFaixa = 0f;
            }
            if (_reenvio < Reenvios.Length && Time.unscaledTime - _focoVoltou >= Reenvios[_reenvio])
            {
                Logi.PararTudo();
                _reenvio++;
            }

            AplicarFaixaGiro();

            if (Calibracao.Quadro()) { Estado = "testando a direção da força..."; return; }

            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            Pausado = Time.timeScale < 0.01f;
            if (Pausado)
            {
                // Jogo parado: só uma mola leve, sem nada que dependa da física congelada.
                Logi.Constante(0); Logi.PararSuperficie();
                Logi.Mola(0, 30, 25); Logi.Amortecedor(20);
                Estado = "pausado";
                return;
            }

            var v = Onibus.Dirigido;
            var ws = v.wheelState;
            int n = ws.Length;
            if (n != Onibus.Esterca.Length) { Onibus.CenaMudou(); return; }
            if (_compAnt.Length != n) { _compAnt = new float[n]; _compBaixa = new float[n]; for (int i = 0; i < n; i++) _compAnt[i] = ws[i].suspensionCompression; }

            float vel = Mathf.Abs(v.speed);
            VelKmh = vel * 3.6f;
            MotorLigado = Onibus.MotorFuncionando;
            float semMotor = MotorLigado ? 1f : Cfg.GanhoSemMotor.Value;

            // --- pneus da frente ---
            float trilhaMec = Cfg.TrilhaMecanica.Value, trilhaPneu = 1f - trilhaMec, perda = Cfg.PerdaNoLimite.Value;
            float sat = 0f, carga = 0f, usoMax = 0f, bumpAssim = 0f, hfQuad = 0f, angSoma = 0f;
            int noChao = 0, esterca = 0;
            var direita = v.transform.right;
            for (int i = 0; i < n; i++)
            {
                if (!Onibus.Esterca[i]) continue;
                var w = ws[i];
                float comp = w.suspensionCompression;
                float compVel = (comp - _compAnt[i]) / dt;
                _compAnt[i] = comp;
                // Passa-alta: o que sobra depois de tirar a rolagem/arfagem lenta da carroceria.
                _compBaixa[i] += (compVel - _compBaixa[i]) * (1f - Mathf.Exp(-dt / 0.12f));
                float hf = compVel - _compBaixa[i];
                hfQuad += hf * hf;
                bumpAssim += Onibus.Lado[i] * hf;

                angSoma += w.steerAngle; esterca++;
                if (!w.grounded) continue;
                noChao++;
                // Força lateral que o pneu faz na carroceria, projetada no "direita" do ônibus
                // (tireForce.x multiplica hit.sidewaysDir no VPP; a projeção tira a dúvida do sentido).
                var hit = w.hit;
                float fy = Vector3.Dot(hit.sidewaysDir, direita) * w.tireForce.x;
                float fz = Mathf.Max(hit.force, 0f);
                float pico = Mathf.Max(Mathf.Abs(v.GetWheelPeakSlip(i).x), 0.05f);
                float uso = Mathf.Abs(w.tireSlip.x) / pico;
                usoMax = Mathf.Max(usoMax, uso);
                float fp = 1f - perda * Mathf.Min(uso, 1f);
                if (uso > 1f) fp -= 0.25f * perda * Mathf.Min(uso - 1f, 1f);
                sat += -fy * (trilhaMec + trilhaPneu * fp);
                carga += fz;
            }
            Uso = usoMax;
            CargaFrente = carga;
            // Carga de referência do eixo: média longa (as transferências de peso entram e saem).
            if (noChao > 0)
            {
                if (_cargaRef <= 0f) _cargaRef = carga;
                else _cargaRef += (carga - _cargaRef) * (1f - Mathf.Exp(-dt / 4f));
            }
            float ang = esterca > 0 ? angSoma / esterca : 0f;
            AprenderEsterco(v, ang);
            // sat está no referencial "direita do ônibus"; passa para o do ângulo de esterço.
            sat *= _estercoDireita;
            AprenderModelo(sat, ang, usoMax);
            sat *= _sinalModelo;
            float satN = _cargaRef > 1f ? sat / (_cargaRef * Mathf.Max(Cfg.AceleracaoReferencia.Value, 0.05f)) : 0f;
            // Abaixo de uns 15 km/h a força lateral do VPP oscila e chega atrasada; num ônibus de verdade o
            // autoalinhamento manobrando é pequeno, quem segura o volante é o atrito e o amortecimento.
            satN *= Mathf.Clamp01((VelKmh - 3f) / 12f);
            float tau = Cfg.Suavizacao.Value;
            _satFiltrado = tau <= 0.001f ? satN : _satFiltrado + (satN - _satFiltrado) * (1f - Mathf.Exp(-dt / tau));
            Autoalinha = _satFiltrado * Cfg.Autoalinhamento.Value * semMotor;
            // Direção hidráulica: a válvula deixa passar para as mãos só uma parte da força da caixa, e o resto a
            // bomba assume. Por isso o volante não fica pesado em curva fechada nem manobrando, e perto do centro
            // a força continua igual. Sem motor não há bomba e chega tudo.
            float limite = Cfg.LimiteHidraulica.Value;
            if (MotorLigado && limite > 0.01f && limite < 1f) Autoalinha = limite * (float)Math.Tanh(Autoalinha / limite);

            // Solavanco de um lado só: a roda que sobe é empurrada para trás e puxa o volante
            // para o lado dela (raio de rolagem positivo).
            Solavanco = _estercoDireita * Mathf.Clamp(bumpAssim / 0.6f, -1.5f, 1.5f) * Cfg.Solavancos.Value * Mathf.Clamp01(VelKmh / 5f);

            // --- direção da força no volante ---
            AprenderDirecao(ws, n);
            float alvo = (Autoalinha + Solavanco) * _sinalDirecao * Cfg.Forca.Value;
            float s = Mathf.Sign(alvo), a = Mathf.Abs(alvo);
            a = Mathf.Pow(a, Cfg.Curva.Value);
            // A força mínima entra numa rampa suave: um degrau perto do zero vira ganho alto no centro, e o
            // volante solto passa a ir e voltar em volta dele.
            float min = Cfg.ForcaMinima.Value;
            if (min > 0f) a = min * Suave(a / 0.06f) + (1f - min) * a;

            // Atrito seco da caixa de direção e do pivô, contra o giro do volante. A tanh troca o sinal
            // devagar perto de parado, senão o atrito sozinho vibra a cada quadro.
            MedirVolante(dt);
            Atrito = -Cfg.Atrito.Value * semMotor * Cfg.Forca.Value * (float)Math.Tanh(VelVolante / 25f);
            float total = s * a + Atrito;
            bool clip = Mathf.Abs(total) > 1f;
            _clipMedia += ((clip ? 1f : 0f) - _clipMedia) * (1f - Mathf.Exp(-dt / 2f));
            Clip = _clipMedia;
            Saida = Mathf.Clamp(total, -1f, 1f);
            int sinal = Cfg.SinalConstante.Value;
            if (sinal == 0) Logi.Constante(0);   // direção ainda desconhecida: não empurra nada
            else Logi.Constante(Mathf.RoundToInt(Saida * 100f * sinal));

            // --- mola e amortecedor (no próprio volante, sem latência) ---
            float rampa = Mathf.Clamp01(VelKmh / 30f);
            MolaCoef = Mathf.Lerp(Cfg.CentralizacaoParado.Value, Cfg.Centralizacao.Value, rampa) * semMotor * Cfg.Forca.Value;
            int coef = Mathf.RoundToInt(Mathf.Clamp01(MolaCoef) * 100f);
            Logi.Mola(0, Mathf.Min(100, Mathf.RoundToInt(coef * 1.5f)), coef);
            Amort = Mathf.Lerp(Cfg.AmortecedorParado.Value, Cfg.AmortecedorAndando.Value, Mathf.Clamp01(VelKmh / 40f));
            if (!MotorLigado) Amort = Mathf.Max(Amort, Cfg.AmortecedorSemMotor.Value);
            Logi.Amortecedor(Mathf.RoundToInt(Mathf.Clamp01(Amort) * 100f));

            Superficie(dt, hfQuad);
            Impactos(v, dt);
            Leds();
            Estado = sinal == 0 ? "ativo, esperando o teste de direção (pare o ônibus)" : "ativo";
        }

        static float Suave(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // Velocidade do volante em graus por segundo, no sentido do eixo X da SDK.
        static void MedirVolante(float dt)
        {
            int x = Logi.PosicaoX();
            if (!_temXAnt) { _xAnt = x; _temXAnt = true; VelVolante = 0f; return; }
            float graus = FaixaAtual > 0 ? FaixaAtual : Mathf.Max(Cfg.FaixaGiro.Value, 200f);
            float vel = (x - _xAnt) / 65536f * graus / dt;
            _xAnt = x;
            VelVolante += (vel - VelVolante) * (1f - Mathf.Exp(-dt / 0.03f));
        }

        // Textura da pista (mede a agitação da suspensão) ou vibração do motor.
        static void Superficie(float dt, float hfQuad)
        {
            _texturaRms += (Mathf.Sqrt(hfQuad) - _texturaRms) * (1f - Mathf.Exp(-dt / 0.25f));
            float pista = Mathf.Clamp01(_texturaRms / 0.4f) * Cfg.Textura.Value * Mathf.Clamp01(VelKmh / 10f);
            float asfalto = Cfg.Asfalto.Value * Mathf.Clamp01((VelKmh - 5f) / 50f);
            float estrada = Mathf.Max(pista, asfalto);
            float rpm = Onibus.Rpm;
            // O diesel treme mais em marcha lenta (coxins perto da ressonância, explosões espaçadas); acelerando,
            // a vibração fica mais fina e fraca: um quinto no dobro da lenta.
            Onibus.FaixaRpm(out float lenta, out _);
            float acimaDaLenta = Mathf.Clamp01((rpm - lenta) / Mathf.Max(lenta, 300f));
            float motor = MotorLigado && rpm > 100f ? Cfg.VibracaoMotor.Value * Mathf.Lerp(1f, 0.2f, acimaDaLenta) : 0f;
            float mag; int periodo;
            if (motor >= estrada)
            {
                // Diesel de 6 cilindros: 3 explosões por volta.
                mag = motor; periodo = Mathf.RoundToInt(60000f / (Mathf.Max(rpm, 300f) * 3f));
            }
            else
            {
                mag = estrada; periodo = Mathf.RoundToInt(Mathf.Lerp(70f, 25f, Mathf.Clamp01(VelKmh / 80f)));
            }
            mag *= Cfg.Forca.Value;
            Vibra = mag;
            Logi.Superficie(Mathf.RoundToInt(Mathf.Clamp01(mag) * 100f), periodo);
        }

        // Pico de aceleração da carroceria muito acima da média = batida, meio-fio, buraco.
        static void Impactos(Il2Cpp.VPVehicleController v, float dt)
        {
            var rb = v.cachedRigidbody;
            if (rb == null || Cfg.Impactos.Value <= 0f) { _temVelAnt = false; return; }
            var vl = rb.transform.InverseTransformDirection(rb.velocity);
            if (!_temVelAnt) { _velAnt = vl; _acelMedia = Vector3.zero; _temVelAnt = true; return; }
            var dv = vl - _velAnt;
            _velAnt = vl;
            if (dv.magnitude > 12f) { _acelMedia = Vector3.zero; return; }   // teletransporte / reset
            var ac = dv / dt;
            var pico = ac - _acelMedia;
            _acelMedia += (ac - _acelMedia) * (1f - Mathf.Exp(-dt / 0.3f));
            if (Time.unscaledTime < _proximoImpacto) return;
            float lim = Cfg.ImpactoLimite.Value;
            int sinal = Cfg.SinalConstante.Value == 0 ? 1 : Cfg.SinalConstante.Value;
            if (Mathf.Abs(pico.x) > lim)
            {
                // Batida do lado direito acelera a carroceria para a esquerda e joga o volante.
                int mag = Mathf.RoundToInt(Mathf.Clamp01(Mathf.Abs(pico.x) / (lim * 3f)) * 100f * Cfg.Impactos.Value);
                Logi.ColisaoLateral(Math.Sign(pico.x) * (int)_estercoDireita * (int)_sinalDirecao * sinal * Math.Min(mag, 100));
                _proximoImpacto = Time.unscaledTime + 0.25f;
            }
            else if (pico.z < -lim || Mathf.Abs(pico.y) > lim * 1.5f)
            {
                float m = Mathf.Max(-pico.z, Mathf.Abs(pico.y) / 1.5f);
                Logi.ColisaoFrontal(Math.Min(100, Mathf.RoundToInt(Mathf.Clamp01(m / (lim * 3f)) * 100f * Cfg.Impactos.Value)));
                _proximoImpacto = Time.unscaledTime + 0.25f;
            }
        }

        // O sinal do ângulo de esterço do VPP em relação ao eixo do volante (normalmente +1, mas
        // a opção "inverter volante" do jogo troca). Aprendido do próprio movimento.
        static void AprenderDirecao(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppVehiclePhysics.VehicleBase.WheelState> ws, int n)
        {
            int x = Logi.PosicaoX();
            if (Mathf.Abs(x) < 4000) return;
            for (int i = 0; i < n; i++)
            {
                if (!Onibus.Esterca[i]) continue;
                float ang = ws[i].steerAngle;
                if (Mathf.Abs(ang) < 1.5f) return;
                int voto = Math.Sign(ang) * Math.Sign(x);
                _votosDirecao = Math.Clamp(_votosDirecao + voto, -30, 30);
                float novo = _votosDirecao >= 0 ? 1f : -1f;
                if (novo != _sinalDirecao && Math.Abs(_votosDirecao) >= 10)
                {
                    _sinalDirecao = novo;
                    Log.Msg(novo > 0 ? "Esterço segue o volante (sinal +)" : "Esterço invertido em relação ao volante (sinal -)");
                }
                return;
            }
        }

        // Andando para frente, esterço positivo que gira o ônibus para a direita => +1.
        static void AprenderEsterco(Il2Cpp.VPVehicleController v, float ang)
        {
            if (Mathf.Abs(ang) < 2f) return;
            var rb = v.cachedRigidbody;
            if (rb == null) return;
            var t = rb.transform;
            float frente = Vector3.Dot(rb.velocity, t.forward);
            float guinada = Vector3.Dot(rb.angularVelocity, t.up);   // + = horário visto de cima = direita
            if (frente < 3f || Mathf.Abs(guinada) < 0.05f) return;
            Votar(ref _votosEsterco, ref _estercoDireita, Math.Sign(ang) * Math.Sign(guinada), 40,
                "Esterço do VPP: positivo vira para a direita", "Esterço do VPP: positivo vira para a esquerda");
        }

        // Em curva normal (pneu longe do limite) o autoalinhamento sempre se opõe ao esterço.
        static void AprenderModelo(float sat, float ang, float uso)
        {
            if (VelKmh < 15f || Mathf.Abs(ang) < 1.5f || uso > 0.7f || _cargaRef <= 1f) return;
            if (Mathf.Abs(sat) < 0.03f * _cargaRef * Cfg.AceleracaoReferencia.Value) return;
            Votar(ref _votosModelo, ref _sinalModelo, -Math.Sign(sat) * Math.Sign(ang), 40,
                "Autoalinhamento: sentido confirmado pela física", "Autoalinhamento: sentido corrigido pela física (estava invertido)");
        }

        static void Votar(ref int votos, ref float sinal, int voto, int limite, string msgPos, string msgNeg)
        {
            votos = Math.Clamp(votos + voto, -limite - 20, limite + 20);
            float novo = votos >= limite ? 1f : votos <= -limite ? -1f : sinal;
            if (novo == sinal) return;
            sinal = novo;
            Log.Msg(novo > 0 ? msgPos : msgNeg);
        }

        // O G HUB/LGS costuma deixar o volante travado em outra faixa (e às vezes volta para ela).
        // Primeiro tenta pela SDK; se ela não sabe a faixa (LGS 5.10 antigo, só Profiler),
        // manda o comando nativo direto pelo USB. Reenvia por 20 s depois de assumir, porque o
        // Profiler reaplica o perfil quando o jogo ganha foco.
        static int _faixaPedida = -1, _hidEnviado = -1;
        static float _hidReenviarAte;
        static void AplicarFaixaGiro()
        {
            if (Time.unscaledTime < _proximaFaixa) return;
            _proximaFaixa = Time.unscaledTime + 2f;
            int f = Mathf.RoundToInt(Cfg.FaixaGiro.Value);
            if (f < 40) { FaixaAtual = Logi.FaixaGiro(); return; }
            int sdk = Logi.FaixaGiro();
            if (sdk > 0)
            {
                FaixaAtual = sdk;
                if (sdk == f) return;
                Logi.DefinirFaixaGiro(f);
                FaixaAtual = Logi.FaixaGiro();
                if (f != _faixaPedida) Log.Msg($"Ângulo do volante pela SDK: {sdk}° -> {FaixaAtual}°");
                _faixaPedida = f;
                if (FaixaAtual == f) return;
            }
            bool novo = f != _hidEnviado;
            if (!novo && Time.unscaledTime > _hidReenviarAte) return;
            if (novo) _hidReenviarAte = Time.unscaledTime + 20f;
            bool ok = Hid.DefinirFaixa(f);
            _hidEnviado = f;
            if (ok) FaixaAtual = f;
            if (novo) Log.Msg(ok ? $"Ângulo do volante pelo USB ({Hid.Modelo}): {f}°"
                               : "Não deu para mudar o ângulo: nem a SDK nem o USB aceitaram (volante em modo de compatibilidade?)");
        }

        static void Leds()
        {
            if (!Cfg.Leds.Value) return;
            Onibus.FaixaRpm(out float lenta, out float max);
            Logi.Leds(Onibus.Rpm, lenta + (max - lenta) * 0.45f, max * 0.93f);
        }
    }

    // Descobre para que lado o volante gira com força constante positiva (depende do driver).
    // Roda sozinho uma vez com o ônibus parado, ou pelo botão do painel: empurra +, depois −
    // pelo dobro do tempo e + de novo (volta perto de onde estava). Só vale se os dois
    // movimentos forem opostos e nenhum frame travar no meio (no carregamento da partida um
    // frame de segundos estraga a medida).
    internal static class Calibracao
    {
        const int Forca = 35;
        const float Toque = 0.22f;
        static int _fase = -1;
        static float _t;
        static int _x0, _x1, _x2;
        static int _tentativas;
        static float _esperaAte;
        internal static string Resultado = "";

        internal static void CenaMudou() { _fase = -1; _esperaAte = Time.unscaledTime + 8f; }

        internal static void Pedir()
        {
            _fase = 0; _t = Time.unscaledTime;
            Resultado = "Solte o volante...";
        }

        static void Falhou(string motivo)
        {
            Logi.Constante(0); _fase = -1;
            _esperaAte = Time.unscaledTime + 20f;
            Resultado = motivo;
            Log.Aviso("Teste de direção: " + motivo);
        }

        // true enquanto o teste está ocupando o volante.
        internal static bool Quadro()
        {
            if (_fase < 0)
            {
                if (Cfg.SinalConstante.Value != 0 || _tentativas >= 3 || Time.unscaledTime < _esperaAte) return false;
                if (Ffb.VelKmh > 0.5f || Time.unscaledDeltaTime > 0.05f) return false;
                _tentativas++;
                Log.Msg("Testando para que lado o volante gira com força positiva (três toques curtos)...");
                Pedir();
            }
            if (_fase > 0 && Time.unscaledDeltaTime > 0.08f) { Falhou("O jogo travou durante o teste. Clique em Testar direção de novo."); return false; }
            float t = Time.unscaledTime - _t;
            switch (_fase)
            {
                case 0:
                    Logi.PararTudo();
                    if (t < 0.6f) return true;
                    _x0 = Logi.PosicaoX(); _fase = 1; _t = Time.unscaledTime;
                    Logi.Constante(Forca);
                    return true;
                case 1:
                    if (t < Toque) return true;
                    _x1 = Logi.PosicaoX(); _fase = 2; _t = Time.unscaledTime;
                    Logi.Constante(-Forca);
                    return true;
                case 2:
                    if (t < Toque * 2f) return true;
                    _x2 = Logi.PosicaoX(); _fase = 3; _t = Time.unscaledTime;
                    Logi.Constante(Forca);
                    return true;
                case 3:
                    if (t < Toque) return true;
                    Logi.Constante(0); _fase = -1;
                    int d1 = _x1 - _x0, d2 = _x2 - _x1;
                    if (Math.Abs(d1) < 600 || Math.Abs(d2) < 600)
                    {
                        Falhou($"O volante quase não mexeu ({d1}, {d2}). Solte as mãos e clique em Testar direção.");
                        return false;
                    }
                    if (Math.Sign(d1) == Math.Sign(d2))
                    {
                        Falhou($"Movimentos não bateram ({d1}, {d2}): alguém segurou o volante? Teste de novo.");
                        return false;
                    }
                    Cfg.SinalConstante.Value = d1 > 0 ? 1 : -1;
                    MelonLoader.MelonPreferences.Save();
                    Resultado = d1 > 0 ? "OK: força positiva gira para a direita" : "OK: força positiva gira para a esquerda";
                    Log.Msg($"{Resultado} ({d1}, {d2})");
                    return false;
            }
            return false;
        }
    }
}
