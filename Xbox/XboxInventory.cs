using System.Security.Cryptography;
using System.Text;
using OpenWigiDash.DeviceBatteryInfo.Battery;

namespace OpenWigiDash.DeviceBatteryInfo.Xbox;

/// <summary>
/// Leituras do Windows.Gaming.Input viram aparelhos de bateria. Controle que sumiu (desligou) continua na lista
/// como desligado, com a última leitura, até o plugin fechar.
/// </summary>
internal sealed class XboxInventory
{
    public const string KeyPrefix = "xbox:";
    private const int StatusNotPresent = 0;
    private const int StatusCharging = 3;

    private readonly Dictionary<string, BatteryDevice> _known = new(StringComparer.Ordinal);

    /// <summary>Chave gravada: um resumo do NonRoamableId (que tem caracteres estranhos e não cabe bem num JSON).</summary>
    public static string KeyOf(string nonRoamableId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(nonRoamableId.TrimEnd('\0')));
        return KeyPrefix + Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    /// <summary>Percentual do relatório; <c>null</c> quando o controle não tem bateria (cabo) ou não informou.</summary>
    public static int? PercentageOf(XboxControllerReading reading)
    {
        if (reading.Status == StatusNotPresent)
            return null;
        if (reading is not { RemainingMilliwattHours: { } remaining, FullChargeMilliwattHours: { } full } || full <= 0)
            return null;
        return Math.Clamp((int)Math.Round(remaining * 100.0 / full), 0, 100);
    }

    public IReadOnlyList<BatteryDevice> Update(IReadOnlyList<XboxControllerReading> readings, DateTimeOffset now)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reading in readings)
        {
            var key = KeyOf(reading.Id);
            if (!seen.Add(key))
                continue;
            var percentage = PercentageOf(reading);
            _known.TryGetValue(key, out var previous);
            _known[key] = new BatteryDevice(
                key,
                string.IsNullOrWhiteSpace(reading.Name) ? "Controle Xbox" : reading.Name.Trim(),
                DeviceKind.Other,
                percentage ?? previous?.Percentage,
                reading.Status == StatusCharging,
                Connected: true,
                percentage is null ? previous?.UpdatedAt : now);
        }
        foreach (var (key, device) in _known.ToList())
        {
            if (!seen.Contains(key) && device.Connected)
                _known[key] = device with { Connected = false, Charging = false };
        }
        return Numbered(_known.Values);
    }

    /// <summary>Dois controles iguais têm o mesmo nome: o segundo vira "Nome #2" (pela ordem da chave, que não muda).</summary>
    private static List<BatteryDevice> Numbered(IEnumerable<BatteryDevice> devices)
    {
        var result = new List<BatteryDevice>();
        foreach (var group in devices.GroupBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var ordered = group.OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
            for (var i = 0; i < ordered.Count; i++)
                result.Add(i == 0 ? ordered[i] : ordered[i] with { Name = $"{ordered[i].Name} #{i + 1}" });
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }
}
