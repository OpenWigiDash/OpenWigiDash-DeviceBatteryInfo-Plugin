using System.Text.RegularExpressions;
using OpenWigiDash.DeviceBatteryInfo.Battery;

namespace OpenWigiDash.DeviceBatteryInfo.Bluetooth;

/// <summary>Um nó de dispositivo Bluetooth do Gerenciador de Dispositivos, com o que interessa para a bateria.</summary>
/// <param name="InstanceId">Ex.: <c>BTHLE\DEV_D905CE8E965D\8&amp;138EAB69&amp;0&amp;D905CE8E965D</c>.</param>
/// <param name="Name">Nome amigável do nó (o do aparelho, ou o do perfil, como "Galaxy A55 5G Hands-Free HF").</param>
/// <param name="Battery">DEVPKEY_Bluetooth_Battery: o percentual que o Windows mostra em Configurações; <c>null</c> se o nó não tem.</param>
/// <param name="Disconnected">O Windows marca o nó como desconectado (DN_DEVICE_DISCONNECTED).</param>
internal sealed record BluetoothNode(string InstanceId, string? Name, int? Battery, bool Disconnected);

/// <summary>
/// Junta os nós de cada aparelho (um celular tem dezenas: um por perfil) pelo endereço Bluetooth, que não muda, e
/// devolve só os aparelhos que têm bateria.
/// </summary>
internal static partial class BluetoothInventory
{
    public const string KeyPrefix = "bt:";

    // Nó do aparelho em si: BTHENUM\DEV_<mac>\... (clássico) ou BTHLE\DEV_<mac>\... (Low Energy).
    [GeneratedRegex(@"^BTH(?:ENUM|LE)\\DEV_([0-9A-F]{12})\\", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceNode();

    // Perfil clássico: BTHENUM\{uuid}_VID...\8&...&<mac>_C00000000
    [GeneratedRegex(@"^BTHENUM\\\{[^}]+\}[^\\]*\\(?:[^\\]*&)?([0-9A-F]{12})_C[0-9A-F]+$", RegexOptions.IgnoreCase)]
    private static partial Regex ClassicProfile();

    // Serviço LE: BTHLEDEVICE\{uuid}_<mac>\... ou BTHLEDEVICE\{uuid}_DEV_VID&..._<mac>\...
    [GeneratedRegex(@"^BTHLEDEVICE\\\{[^}]+\}_(?:DEV_[^\\]*_)?([0-9A-F]{12})\\", RegexOptions.IgnoreCase)]
    private static partial Regex LeService();

    /// <summary>Endereço Bluetooth (12 dígitos hexa, minúsculos) do nó, ou <c>null</c> se não dá para saber.</summary>
    public static string? AddressOf(string instanceId)
    {
        foreach (var regex in (ReadOnlySpan<Regex>)[DeviceNode(), ClassicProfile(), LeService()])
        {
            var match = regex.Match(instanceId);
            if (match.Success)
                return match.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    public static bool IsDeviceNode(string instanceId) => DeviceNode().IsMatch(instanceId);

    /// <param name="nodes">Nós presentes (os que o Windows ainda conhece) de BTHENUM, BTHLE e BTHLEDEVICE.</param>
    /// <param name="now">Quando a leitura foi feita.</param>
    public static List<BatteryDevice> Build(IEnumerable<BluetoothNode> nodes, DateTimeOffset now)
    {
        var byAddress = new Dictionary<string, List<BluetoothNode>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (AddressOf(node.InstanceId) is not { } address)
                continue;
            if (!byAddress.TryGetValue(address, out var list))
                byAddress[address] = list = [];
            list.Add(node);
        }

        var devices = new List<BatteryDevice>();
        foreach (var (address, list) in byAddress)
        {
            // Bateria: o nó conectado ganha do desconectado (o celular tem um nó LE parado com leitura velha).
            var withBattery = list.Where(n => n.Battery is not null)
                .OrderBy(n => n.Disconnected)
                .ThenBy(n => IsDeviceNode(n.InstanceId) ? 0 : 1)
                .ToList();
            if (withBattery.Count == 0)
                continue;

            var deviceNodes = list.Where(n => IsDeviceNode(n.InstanceId)).ToList();
            // Ligado se algum nó do aparelho (clássico ou LE) não está desconectado. Sem nó do aparelho, vale o da bateria.
            var connected = deviceNodes.Count > 0 ? deviceNodes.Any(n => !n.Disconnected) : !withBattery[0].Disconnected;
            var name = deviceNodes.OrderBy(n => n.Disconnected).Select(n => n.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                       ?? CleanProfileName(withBattery[0].Name)
                       ?? FormatAddress(address);

            devices.Add(new BatteryDevice(
                KeyPrefix + address,
                name.Trim(),
                DeviceKind.Unknown,
                Math.Clamp(withBattery[0].Battery!.Value, 0, 100),
                Charging: false, // o Windows não diz se está carregando
                connected,
                now));
        }
        devices.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return devices;
    }

    private static readonly string[] ProfileSuffixes =
        [" Hands-Free HF", " Hands-Free AG", " Hands-Free", " A2DP SNK", " A2DP SRC", " Stereo", " Avrcp Transport"];

    private static string? CleanProfileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        foreach (var suffix in ProfileSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }
        return name;
    }

    private static string FormatAddress(string address) =>
        string.Join(":", Enumerable.Range(0, 6).Select(i => address.Substring(i * 2, 2).ToUpperInvariant()));
}
