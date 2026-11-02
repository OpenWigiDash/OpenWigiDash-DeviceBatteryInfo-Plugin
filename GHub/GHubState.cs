using System.Text.Json;
using OpenWigiDash.DeviceBatteryInfo.Battery;

namespace OpenWigiDash.DeviceBatteryInfo.GHub;

/// <summary>O que fazer depois de aplicar uma mensagem.</summary>
/// <param name="Changed">Algo que o tile mostra mudou.</param>
/// <param name="BatteryRequests">Ids de sessão cuja bateria precisa ser pedida ("/battery/{id}/state").</param>
/// <param name="NeedsList">Apareceu um id que não conhecemos (o G HUB trocou os ids): pedir "/devices/list" de novo.</param>
internal sealed record GHubUpdate(bool Changed, IReadOnlyList<string> BatteryRequests, bool NeedsList)
{
    public static readonly GHubUpdate None = new(false, [], false);
}

/// <summary>
/// Os aparelhos que o G HUB mostrou nesta execução, pela chave estável. Sem rede: recebe as mensagens já lidas e diz
/// o que pedir em seguida, para dar para testar. Thread-safe.
/// </summary>
/// <remarks>
/// O G HUB troca o id de sessão (devNNNNNNNN) quando reinicia e quando o aparelho volta do standby. Por isso o mapa
/// id → chave é refeito a cada lista, e uma leitura de um id desconhecido pede a lista de novo em vez de ser
/// descartada (era assim que o plugin do Stream Deck ficava preso em "desconectado").
/// </remarks>
internal sealed class GHubState(TimeProvider time)
{
    private sealed class Entry(GHubDeviceInfo info)
    {
        public GHubDeviceInfo Info = info;
        public int? Percentage;
        public bool Charging;
        public bool Connected;
        public DateTimeOffset? UpdatedAt;

        public BatteryDevice ToDevice() =>
            new(Info.Key, Info.Name, Info.Kind, Percentage, Charging, Connected, UpdatedAt);
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keyByRawId = new(StringComparer.Ordinal);
    private IReadOnlyList<BatteryDevice> _snapshot = [];

    /// <summary>Aparelhos com bateria, na ordem em que apareceram.</summary>
    public IReadOnlyList<BatteryDevice> Devices
    {
        get
        {
            lock (_lock)
                return _snapshot;
        }
    }

    /// <summary>Aplica uma mensagem do G HUB.</summary>
    public GHubUpdate Apply(GHubMessage message)
    {
        if (message.Path == GHubProtocol.DevicesList)
            return message.Payload is { } list ? ApplyDeviceList(GHubProtocol.ParseDeviceList(list)) : GHubUpdate.None;

        if (message.Path == GHubProtocol.DeviceStateChanged)
            return message.Payload is { } payload ? ApplyDeviceState(payload) : GHubUpdate.None;

        if (message.Path == GHubProtocol.BatteryStateChanged)
        {
            return message.Payload is { } payload && GHubProtocol.ParseBattery(payload) is { RawId: { } id } battery
                ? ApplyBattery(id, battery)
                : GHubUpdate.None;
        }

        if (GHubProtocol.BatteryPathId(message.Path) is { } rawId)
        {
            // Resposta ao GET: sem leitura (NO_SUCH_PATH, payload vazio) = o aparelho não está respondendo agora.
            var success = message.ResultCode is null || string.Equals(message.ResultCode, "SUCCESS", StringComparison.OrdinalIgnoreCase);
            return success && message.Payload is { } payload && GHubProtocol.ParseBattery(payload) is { } battery
                ? ApplyBattery(battery.RawId ?? rawId, battery)
                : ApplyNoBattery(rawId);
        }

        return GHubUpdate.None;
    }

    internal GHubUpdate ApplyDeviceList(IReadOnlyList<GHubDeviceInfo> devices)
    {
        lock (_lock)
        {
            var changed = false;
            var requests = new List<string>();
            var present = new HashSet<string>(StringComparer.Ordinal);
            _keyByRawId.Clear();
            foreach (var info in devices)
            {
                _keyByRawId[info.RawId] = info.Key;
                if (!info.HasBattery)
                    continue;
                present.Add(info.Key);
                if (!_byKey.TryGetValue(info.Key, out var entry))
                {
                    _byKey[info.Key] = entry = new Entry(info);
                    changed = true;
                }
                else if (entry.Info != info)
                {
                    changed |= entry.Info.Name != info.Name;
                    entry.Info = info;
                }
                if (info.Active == false)
                    changed |= SetDisconnected(entry);
                else
                    requests.Add(info.RawId);
            }
            // Quem sumiu da lista continua conhecido (o widget mostra a última leitura), só que desligado.
            foreach (var (key, entry) in _byKey)
            {
                if (!present.Contains(key))
                    changed |= SetDisconnected(entry);
            }
            if (changed)
                Publish();
            return new GHubUpdate(changed, requests, false);
        }
    }

    internal GHubUpdate ApplyBattery(string rawId, GHubBattery battery)
    {
        lock (_lock)
        {
            if (!_keyByRawId.TryGetValue(rawId, out var key) || !_byKey.TryGetValue(key, out var entry))
                return new GHubUpdate(false, [], NeedsList: true);
            var changed = entry.Percentage != battery.Percentage || entry.Charging != battery.Charging || !entry.Connected;
            entry.Percentage = battery.Percentage;
            entry.Charging = battery.Charging;
            entry.Connected = true;
            entry.UpdatedAt = time.GetUtcNow();
            if (changed)
                Publish();
            return new GHubUpdate(changed, [], false);
        }
    }

    internal GHubUpdate ApplyNoBattery(string rawId)
    {
        lock (_lock)
        {
            if (!_keyByRawId.TryGetValue(rawId, out var key) || !_byKey.TryGetValue(key, out var entry))
                return GHubUpdate.None; // webcam e outros sem bateria também caem aqui
            var changed = SetDisconnected(entry);
            if (changed)
                Publish();
            return new GHubUpdate(changed, [], false);
        }
    }

    private GHubUpdate ApplyDeviceState(JsonElement payload)
    {
        if (GHubProtocol.ParseDeviceState(payload) is not { } state)
            return GHubUpdate.None;
        if (!state.Active)
            return ApplyNoBattery(state.RawId);
        // Ligou: pode ter vindo com id novo. A lista refaz o mapa e pede a bateria de todos.
        lock (_lock)
        {
            return _keyByRawId.ContainsKey(state.RawId)
                ? new GHubUpdate(false, [state.RawId], NeedsList: false)
                : new GHubUpdate(false, [], NeedsList: true);
        }
    }

    /// <summary>O G HUB fechou: todos ficam desligados, com a última leitura.</summary>
    public bool MarkAllDisconnected()
    {
        lock (_lock)
        {
            var changed = false;
            foreach (var entry in _byKey.Values)
                changed |= SetDisconnected(entry);
            _keyByRawId.Clear();
            if (changed)
                Publish();
            return changed;
        }
    }

    private static bool SetDisconnected(Entry entry)
    {
        if (!entry.Connected)
            return false;
        entry.Connected = false;
        entry.Charging = false;
        return true;
    }

    private void Publish() => _snapshot = _byKey.Values.Select(e => e.ToDevice()).ToList();
}
