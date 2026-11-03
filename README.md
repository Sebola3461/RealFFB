# RealFFB (experimental)

Force feedback novo para volantes Logitech no Proton Bus Simulator. O jogo original só aplica uma mola, que fica mais
dura com a velocidade. Este mod substitui essa mola por:

- **Autoalinhamento:** calculado pela força lateral dos pneus da frente. O volante fica leve quando eles chegam no
  limite.
- **Volante em movimento:** mola de centralização e amortecimento conforme a velocidade. Com o motor desligado, o
  volante fica pesado, como sem direção hidráulica.
- **Irregularidades e impactos:** vêm da suspensão e da carroceria.
- **Textura da pista e motor:** vibração da pista e da marcha lenta do motor.
- **LEDs de rotação**, nos volantes que têm.
- **Ângulo de giro do volante:** o padrão é 900°.

Requer o [PBSLoader](https://github.com/Sebola3461/pbsu-loader) (PBSLoaderCommonUtils e PBSLoaderCommonInterface).

## Compatibilidade

Testado só num **G25 com o Logitech Gaming Software 5.10** (antigo). Em outros modelos Logitech (G27, G29, G920, G923)
e com o G HUB, o mod deve funcionar pelo SDK da Logitech, mas ainda não foi testado. Volantes de outras marcas não
recebem force feedback do jogo, nem com o mod.

Na primeira vez, o mod descobre sozinho o sentido da força, com o ônibus parado. Se o volante puxar para o lado errado,
ajuste `SinalConstante` para `1` ou `-1`.

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
| `GrausVolante` | Ângulo de giro, de batente a batente. 0 deixa o que o software da Logitech definiu |
| `SinalConstante` | Sentido da força no seu driver. 0 testa sozinho |

## Vindo do VolanteReal

Este mod se chamava VolanteReal. Na primeira vez que o RealFFB abre, ele traz as opções da categoria `[VolanteReal]` e
os atalhos `VolanteReal_*`, e apaga os antigos. Tire a `VolanteReal.dll` da pasta `Mods`: com as duas DLLs, dois mods
brigam pelo volante.

## Reportar um problema

Abra uma issue com o modelo do volante, o software da Logitech instalado, o `MelonLoader/Latest.log` e a telemetria.

## Licença

[GPL-3.0](LICENSE). Este mod não é afiliado à MEP / Proton Bus Simulator nem à Logitech.
