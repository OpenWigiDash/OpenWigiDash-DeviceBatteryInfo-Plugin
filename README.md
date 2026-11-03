# Device Battery Info para o WigiDash (OpenWigiDashManager)

Plugin de bateria para o painel WigiDash da G.SKILL, no
[OpenWigiDashManager](https://github.com/OpenWigiDash/OpenWigiDashManager) (.NET 10, SDK 3.0). Mostra a bateria de
mouse, teclado e headset sem fio num tile, como o "G Hub Device" do Stream Deck, sem os defeitos dele.

## Widgets

| Widget | O que faz |
| --- | --- |
| G HUB Battery | Bateria de um aparelho Logitech (mouse, teclado, headset) lida do G HUB |
| Bluetooth Battery | Bateria de um aparelho Bluetooth pareado (fone, mouse, teclado, celular), a que o Windows mostra em Configurações > Bluetooth |
| Xbox Battery | Bateria de um controle Xbox no adaptador sem fio ou no cabo |

Cada fonte de bateria tem o seu widget, com o mesmo desenho de tile. Ver [docs/PLANO.md](docs/PLANO.md).

Tamanhos: Mini (meia célula) e Small (o 1x1 da grade antiga).

## G HUB Battery

O tile mostra o nome do aparelho em cima, a pilha com 5 barras e o percentual embaixo.

- **Verde**: 85% ou mais. **Dourado**: no meio. **Vermelho**: no nível de alerta ou abaixo (20% por padrão). **Verde com raio**: carregando.
- **Cinza com "Zz"** (no canto de baixo, à esquerda): o aparelho está dormindo ou fora de alcance. O percentual é a última leitura.
- **"?" Não encontrado**: o aparelho escolhido não está no G HUB agora.
- **"!" G HUB fechado**: o G HUB não está aberto. O tile volta sozinho quando ele abrir.
- Toque no tile pede as leituras de novo na hora.

Opções: aparelho, texto de cima (vazio = nome do aparelho), cor de fundo, nível de alerta e o selo "G" no canto.

Precisa do [Logitech G HUB](https://www.logitechg.com/innovation/g-hub.html) aberto (ele roda na bandeja). O
plugin lê o mesmo serviço local que o próprio G HUB usa (`ws://127.0.0.1:9010`), sem instalar nada.

### O que mudou em relação ao "G Hub Device" do Stream Deck

| Problema no Stream Deck | Causa | Aqui |
| --- | --- | --- |
| Fica "desconectado" depois que o aparelho volta do standby | Só atualizava com evento de mudança de bateria; não perguntava de novo | Pergunta a cada minuto, quando a tela acorda, ao tocar no tile e quando o G HUB avisa que o aparelho ligou |
| Widget troca de aparelho sozinho, ou todos mostram o teclado | Gravava o id de sessão do G HUB (`dev00000006`), que muda quando o G HUB reinicia ou o aparelho reconecta; widget sem aparelho caía no primeiro da lista | Grava o `deviceSignature` (tipo, modelo e número de série); sem aparelho gravado, pede para escolher; aparelho sumido vira "não encontrado", nunca outro |
| Tenta reconectar a cada 5 s para sempre, com o G HUB fechado | Socket antigo ficava aberto sem dono | Fecha o socket antes de abrir outro e espaça as tentativas até 1 min |

## Bluetooth Battery

Mesmo tile do G HUB Battery, com o selo do Bluetooth no canto. Lê a propriedade de bateria que o Windows guarda para
cada aparelho pareado (`DEVPKEY_Bluetooth_Battery`), a mesma de Configurações > Bluetooth, sem falar com o aparelho.

- O aparelho fica gravado pelo endereço Bluetooth, que não muda quando ele desliga ou o PC reinicia.
- Um celular aparece quando está conectado ao PC (por exemplo, pelo Vincular ao Celular, que mantém o viva-voz aberto).
- O Windows guarda a última leitura de um aparelho desligado: o tile mostra essa leitura em cinza, com "Zz".
- O Windows não diz se o aparelho está carregando, então o tile não mostra o raio.
- A lista é lida de novo a cada 30 s, quando a tela acorda e ao tocar no tile. Log em
  `%LOCALAPPDATA%\OpenWigiDash\WidgetData\DeviceBatteryInfo\bluetooth.log`.

## Xbox Battery

Mesmo tile, com o selo de controle no canto. Lê o relatório de bateria do Windows.Gaming.Input, a mesma API que os
jogos usam, chamada direto (sem levar a biblioteca de WinRT do .NET para dentro do plugin).

- O controle fica gravado pelo id fixo que o Windows dá a ele, nunca pela posição 1 a 4 do XInput, que muda
  conforme a ordem em que os controles ligam.
- Controle desligado continua no tile em cinza, com "Zz" e a última leitura.
- Carregando (bateria recarregável no cabo) aparece com o raio verde.
- Pareado por Bluetooth, o controle aparece no Bluetooth Battery.
- Log em `%LOCALAPPDATA%\OpenWigiDash\WidgetData\DeviceBatteryInfo\xbox.log`.

## Requisitos

- Windows 10 ou 11.
- [OpenWigiDashManager](https://github.com/OpenWigiDash/OpenWigiDashManager) instalado pelo
  `OpenWigiDash-Setup.exe` da [release](https://github.com/OpenWigiDash/OpenWigiDashManager/releases) dele.
- Logitech G HUB, para o widget G HUB Battery.

## Desenvolvimento

```powershell
dotnet test tests/DeviceBatteryInfo.Tests
.\deploy.ps1 -RestartManager   # publica em %LOCALAPPDATA%\OpenWigiDash\Widgets\OpenWigiDash.DeviceBatteryInfo
```

Para compilar contra o SDK do repo do Manager clonado ao lado: `-p:UseLocalOpenWigiSdk=true`.

Publicação: workflow **Release Widget** (`release.yml`) na `main`, com a versão sem o "v".
