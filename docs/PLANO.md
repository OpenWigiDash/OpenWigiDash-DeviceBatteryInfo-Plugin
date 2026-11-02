# Plano do plugin Device Battery Info

Bateria dos aparelhos do PC no WigiDash, no OpenWigiDashManager (SDK 3.0, `OpenWigiDash.Sdk` 3.0.0-preview.1).
Pacote no nuget.org: `OpenWigiDash.DeviceBatteryInfo`.

## Widgets

Um widget por fonte, todos com o mesmo tile (`Rendering/BatteryRenderer.cs`) e a mesma instância
(`Widgets/BatteryInstance.cs`). Cada fonte implementa `IBatterySource`.

| Ordem | Widget | Fonte | Estado |
| --- | --- | --- | --- |
| 1 | G HUB Battery | Websocket local do G HUB | Primeira versão; falta teste no PC |
| 2 | Bluetooth Battery | Propriedade de bateria do Windows (`{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`) | Depois |
| 3 | Xbox Battery | XInput (4 níveis) e, se der, Windows.Gaming.Input (percentual) | Depois |
| - | JBL Quantum | Protocolo próprio do QuantumENGINE, sem bateria no Windows | Em espera |

## G HUB

### Protocolo

O lghub_agent escuta em `ws://127.0.0.1:9010` (subprotocolo `json`, `Origin: file://`). Não é documentado; é o
que o app do G HUB usa. Capturado num PC com G715, PRO X SUPERLIGHT 2 e C920 (fixtures em
`tests/DeviceBatteryInfo.Tests/Fixtures`, com os números de série trocados).

- Ao conectar, o servidor manda `{"verb":"OPTIONS","path":"/"}` sozinho.
- `GET /devices/list`: `payload.deviceInfos[]` com `id` (de sessão), `state` (`ACTIVE`...), `deviceType`,
  `displayName`, `extendedDisplayName`, `deviceModel`, `deviceBaseModel`, `deviceUnitId`, `deviceSignature` e
  `capabilities.hasBatteryStatus`.
- `GET /battery/{id}/state`: `payload` com `deviceId`, `percentage`, `charging`, `mileage` (horas) e outros. Sem
  bateria (webcam), `result.code` é `NO_SUCH_PATH` e não vem payload.
- `SUBSCRIBE /battery/state/changed` e `/devices/state/changed`: não têm resposta. O formato dos eventos ainda não
  foi visto ao vivo; o plugin aceita o payload de bateria no primeiro e `{id, state}` no segundo, como o
  LGSTrayBattery.

### Identidade do aparelho

- `id` (`dev00000006`) é um contador do G HUB: muda quando ele reinicia e quando o aparelho volta do standby.
- O widget grava `deviceSignature` (`KEYBOARD.g715.0.<serial>`), que tem o número de série. Sem ele, modelo base e
  `deviceUnitId`; por último, o nome. Dois aparelhos com a mesma chave ganham `#2`.
- Se a chave não está na lista, procura pelo nome, só se um único aparelho tem aquele nome. Senão, "não
  encontrado". Nunca o primeiro da lista.

### Atualização

- Lista a cada 60 s (e, com ela, a bateria de cada aparelho ligado).
- Na hora: quando a tela do WigiDash acorda, ao tocar no tile, ao abrir as opções e quando chega uma leitura de um
  id desconhecido (o G HUB trocou os ids).
- Resposta sem leitura (`NO_SUCH_PATH`, payload vazio) ou `state` diferente de `ACTIVE`: aparelho dormindo, com a
  última leitura.
- G HUB fechado: todos dormindo, tile diz "G HUB fechado"; novas tentativas em 2, 5, 10, 30 e 60 s.

## A testar no PC

- Deixar mouse e teclado dormirem e acordarem: o tile sai do "Zz" sozinho.
- Fechar e abrir o G HUB, e reiniciar o PC: cada widget continua no seu aparelho.
- Colocar o mouse para carregar: raio verde.
- Confirmar o formato dos eventos `/battery/state/changed` e `/devices/state/changed` no log.
