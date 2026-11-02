using OpenWigiDash.DeviceBatteryInfo.GHub;

namespace DeviceBatteryInfo.Tests;

/// <summary>Mensagens reais do G HUB (capturadas num PC com G715, PRO X SUPERLIGHT 2 e C920; números de série trocados).</summary>
internal static class Fixtures
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    public static GHubMessage Message(string name) => GHubProtocol.Parse(Read(name))!;

    public const string KeyboardKey = "ghub:keyboard.g715.0.1111111111";
    public const string MouseKey = "ghub:mouse.pro_x_2_wireless_mouse.1.2222222222";

    /// <summary>A lista do fixture com os ids de sessão trocados, como o G HUB faz depois do standby.</summary>
    public static GHubMessage ListWithIds(string keyboardId, string mouseId) =>
        GHubProtocol.Parse(Read("devices-list.json")
            .Replace("dev00000006", keyboardId)
            .Replace("dev00000007", mouseId))!;

    public static GHubMessage Battery(string rawId, int percentage, bool charging = false, string path = GHubProtocol.BatteryStateChanged) =>
        GHubProtocol.Parse($$$"""
            {"msgId":"","verb":"BROADCAST","path":"{{{path}}}","origin":"backend","result":{"code":"SUCCESS","what":""},
             "payload":{"@type":"type.googleapis.com/logi.protocol.wireless.Battery","deviceId":"{{{rawId}}}","percentage":{{{percentage}}},"charging":{{{(charging ? "true" : "false")}}}}}
            """)!;

    public static GHubMessage DeviceState(string rawId, string state) =>
        GHubProtocol.Parse($$$"""
            {"msgId":"","verb":"BROADCAST","path":"/devices/state/changed","origin":"backend","payload":{"id":"{{{rawId}}}","state":"{{{state}}}"}}
            """)!;
}
