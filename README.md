# RealFFB (experimental)

Force feedback novo para volantes Logitech no Proton Bus Simulator. O jogo original só aplica uma mola, que fica mais
dura com a velocidade. Este mod substitui essa mola por:

- **Autoalinhamento:** calculado pela força lateral dos pneus da frente. O volante fica leve quando eles chegam no
  limite.
- **Direção hidráulica:** com o motor ligado, a força nas mãos tem um teto, e o volante não fica pesado em curva
  fechada nem manobrando. Com o motor desligado não há assistência e ele pesa de verdade.
- **Atrito e amortecimento:** o volante não volta sozinho por qualquer força e não fica indo e voltando quando você
  solta.
- **Volante em movimento:** mola de centralização e amortecimento conforme a velocidade. Com o motor desligado, o
  volante fica pesado, como sem direção hidráulica.
- **Irregularidades e impactos:** vêm da suspensão e da carroceria.
- **Textura da pista e motor:** vibração da pista e do diesel, mais forte em marcha lenta e mais fraca acelerando.
- **LEDs de rotação**, nos volantes que têm.
- **Ângulo de giro do volante:** o padrão é 900°.

Requer o [PBSLoader](https://github.com/Sebola3461/pbsu-loader) (PBSLoaderCommonUtils e PBSLoaderCommonInterface).

## Compatibilidade

Testado só num **G25 com o Logitech Gaming Software 5.10** (antigo). Em outros modelos Logitech (G27, G29, G920, G923)
e com o G HUB, o mod deve funcionar pelo SDK da Logitech, mas ainda não foi testado. Volantes de outras marcas não
recebem force feedback do jogo, nem com o mod.

Na primeira vez, o mod descobre sozinho o sentido da força, com o ônibus parado e o volante solto. Se o volante puxar
para o lado errado, use **INVERTER** no painel.

O mod procura sozinho o volante Logitech entre os controles ligados. Se você tem outros controles (Arduino, pedaleira,
manche) e ele escolher errado, troque no botão **CONTROLE** do painel. O jogo original sempre usa o primeiro controle,
então com outro controle ligado antes do volante o force feedback do próprio jogo não chega nele.

## Teclas

| Tecla | Ação |
|---|---|
| Ctrl+Alt+F10 | Abre o painel de ajustes, com os presets Suave, Realista e Forte |
| Ctrl+Alt+Shift+F10 | Grava `UserData/RealFFB_telemetria.csv` |

As duas ficam na página **Atalhos** da PBSLoaderCommonInterface (ou em `[Atalhos]` no `.cfg`).

## Opções

As opções ficam na categoria `[RealFFB]` e no menu **Mods** da PBSLoaderCommonInterface:

| Opção | Efeito |
|---|---|
| `Ligado` | `false` devolve o force feedback do jogo |
| `Forca` | Força geral |
| `ForcaMinima` | Vence a folga das engrenagens e correias. Em G29 e G920, use de 0,05 a 0,08 |
| `Autoalinhamento` / `Centralizacao` | Força de cada efeito |
| `LimiteHidraulica` | Teto da força do autoalinhamento com o motor ligado. 1 tira o limite |
| `Atrito` / `AmortecedorAndando` | Seguram o volante. Suba se ele ficar indo e voltando ao soltar |
| `Antecipacao` | Compensa o atraso da física do jogo e corta o vai-e-vem que não para. Alto demais deixa o volante nervoso em pista ruim |
| `VibracaoMotor` | Vibração do motor em marcha lenta |
| `Controle` | -1 procura o volante sozinho; 0 a 3 escolhe o controle |
| `GrausVolante` | Ângulo de giro, de batente a batente. 0 deixa o que o software da Logitech definiu |
| `SinalConstante` | Sentido da força no seu driver. 0 testa sozinho |

## Reportar um problema

Abra uma issue com o modelo do volante, o software da Logitech instalado, o `MelonLoader/Latest.log` e a telemetria.

## Licença

[GPL-3.0](LICENSE). Este mod não é afiliado à MEP / Proton Bus Simulator nem à Logitech.
