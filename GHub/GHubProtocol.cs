using System.Text.Json;
using System.Text.RegularExpressions;
using OpenWigiDash.DeviceBatteryInfo.Battery;

namespace OpenWigiDash.DeviceBatteryInfo.GHub;

/// <summary>Uma mensagem do websocket do G HUB (resposta a um pedido ou evento de uma assinatura).</summary>
/// <param name="ResultCode">"SUCCESS" ou o código de erro; <c>null</c> quando a mensagem não traz "result".</param>
/// <param name="Payload">Cópia do "payload", ou <c>null</c> quando não veio (aparelho dormindo, por exemplo).</param>
internal sealed record GHubMessage(string? MsgId, string? Verb, string? Path, string? ResultCode, JsonElement? Payload);

/// <summary>Um aparelho de "/devices/list".</summary>
/// <param name="RawId">Id da sessão do G HUB ("dev00000006"). Muda quando o G HUB reinicia ou o aparelho volta do standby.</param>
/// <param name="Key">Chave estável do aparelho, para gravar no widget.</param>
/// <param name="Active"><c>false</c> quando a lista diz que o aparelho não está ligado; <c>null</c> quando não diz nada.</param>
internal sealed record GHubDeviceInfo(string RawId, string Key, string Name, DeviceKind Kind, bool HasBattery, bool? Active);

/// <summary>Leitura de "/battery/{id}/state" ou do evento "/battery/state/changed".</summary>
internal sealed record GHubBattery(string? RawId, int Percentage, bool Charging);

/// <summary>
/// O protocolo do lghub_agent em ws://127.0.0.1:9010 (subprotocolo "json"). Não é documentado pela Logitech: é o
/// mesmo que o próprio app do G HUB usa, e o que o plugin "G Hub Device" do Stream Deck e o LGSTrayBattery usam.
/// Cada mensagem é um JSON com msgId, verb (GET, SUBSCRIBE), path e, na volta, result e payload.
/// </summary>
internal static partial class GHubProtocol
{
    public const string DevicesList = "/devices/list";
    public const string DeviceStateChanged = "/devices/state/changed";
    public const string BatteryStateChanged = "/battery/state/changed";

    public static string BatteryState(string rawId) => $"/battery/{rawId}/state";

    public static string Request(string verb, string path, string msgId = "") =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["msgId"] = msgId, ["verb"] = verb, ["path"] = path });

    [GeneratedRegex(@"^/battery/(?<id>[^/]+)/state$")]
    private static partial Regex BatteryStatePath();

    /// <summary>O id do aparelho em "/battery/{id}/state", ou <c>null</c> se o caminho é outro.</summary>
    public static string? BatteryPathId(string? path)
    {
        if (path is null)
            return null;
        var match = BatteryStatePath().Match(path);
        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>Lê uma mensagem; <c>null</c> se não for um JSON com objeto na raiz.</summary>
    public static GHubMessage? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            string? code = null;
            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
                code = String(result, "code");
            JsonElement? payload = root.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object
                ? p.Clone()
                : null;
            return new GHubMessage(String(root, "msgId"), String(root, "verb"), String(root, "path"), code, payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Os aparelhos de "/devices/list" (payload.deviceInfos). Quem não tem id fica de fora. Aparelhos iguais (dois
    /// mouses do mesmo modelo) ganham "#2", "#3" na chave, na ordem em que a lista vem.
    /// </summary>
    public static IReadOnlyList<GHubDeviceInfo> ParseDeviceList(JsonElement payload)
    {
        if (!payload.TryGetProperty("deviceInfos", out var infos) || infos.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<GHubDeviceInfo>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var info in infos.EnumerateArray())
        {
            if (ParseDevice(info) is not { } device)
                continue;
            var count = seen[device.Key] = seen.GetValueOrDefault(device.Key) + 1;
            list.Add(count == 1 ? device : device with { Key = $"{device.Key}#{count}" });
        }
        return list;
    }

    /// <summary>Um deviceInfo (da lista ou do evento de aparelho ligado), ou <c>null</c> se não tem id.</summary>
    public static GHubDeviceInfo? ParseDevice(JsonElement info)
    {
        if (info.ValueKind != JsonValueKind.Object || String(info, "id") is not { Length: > 0 } rawId)
            return null;
        var name = FirstString(info, "displayName", "extendedDisplayName", "deviceModel") ?? rawId;
        var hasBattery = info.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object
            && caps.TryGetProperty("hasBatteryStatus", out var b) && b.ValueKind == JsonValueKind.True;
        bool? active = String(info, "state") is { } state ? IsActive(state) : null;
        return new GHubDeviceInfo(rawId, StableKey(info, name), name, KindOf(String(info, "deviceType")), hasBattery, active);
    }

    /// <summary>
    /// Chave que não muda entre sessões do G HUB. A primeira opção é o deviceSignature
    /// ("KEYBOARD.g715.0.2575552764": tipo, modelo base, extensão e número de série), que separa até dois aparelhos
    /// do mesmo modelo. Sem ele, o modelo e, por último, o nome. O "id" (devNNNNNNNN) é um contador da sessão e não
    /// serve: era ele que o plugin do Stream Deck gravava.
    /// </summary>
    internal static string StableKey(JsonElement info, string name)
    {
        var key = FirstString(info, "deviceSignature")
            ?? (FirstString(info, "deviceBaseModel", "deviceModel") is { } model
                ? model + (FirstString(info, "deviceUnitId", "unitId") is { } unit ? "." + unit : "")
                : name);
        return "ghub:" + key.Trim().ToLowerInvariant();
    }

    /// <summary>Leitura de bateria; <c>null</c> se o payload não traz o percentual.</summary>
    public static GHubBattery? ParseBattery(JsonElement payload)
    {
        if (!payload.TryGetProperty("percentage", out var p) || !p.TryGetDouble(out var percentage))
            return null;
        var charging = payload.TryGetProperty("charging", out var c) && c.ValueKind == JsonValueKind.True;
        return new GHubBattery(String(payload, "deviceId"), (int)Math.Clamp(Math.Round(percentage), 0, 100), charging);
    }

    /// <summary>"/devices/state/changed": id e se ficou ligado. <c>null</c> se não dá para saber.</summary>
    public static (string RawId, bool Active)? ParseDeviceState(JsonElement payload)
    {
        if (String(payload, "id") is not { Length: > 0 } rawId || String(payload, "state") is not { } state)
            return null;
        return (rawId, IsActive(state));
    }

    /// <summary>"ACTIVE" é ligado; "NOT_CONNECTED", "ABSENT" e o resto, não.</summary>
    internal static bool IsActive(string state) => string.Equals(state, "active", StringComparison.OrdinalIgnoreCase);

    internal static DeviceKind KindOf(string? deviceType) => deviceType?.ToLowerInvariant() switch
    {
        "mouse" => DeviceKind.Mouse,
        "keyboard" => DeviceKind.Keyboard,
        "headset" => DeviceKind.Headset,
        "speaker" => DeviceKind.Speaker,
        null or "" => DeviceKind.Unknown,
        _ => DeviceKind.Other,
    };

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (String(element, name) is { } value && !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }
}
