namespace OpenWigiDash.DeviceBatteryInfo.Battery;

/// <summary>Que tipo de aparelho é; escolhe o nome padrão e, no futuro, o ícone.</summary>
public enum DeviceKind
{
    Unknown,
    Mouse,
    Keyboard,
    Headset,
    Speaker,
    Other,
}

/// <summary>Se a fonte (G HUB) está respondendo.</summary>
public enum SourceState
{
    /// <summary>Ainda tentando a primeira conexão.</summary>
    Connecting,
    Connected,
    /// <summary>O programa da fonte não está aberto (ou recusou a conexão).</summary>
    Unavailable,
}

/// <summary>
/// Um aparelho com bateria, como a fonte o conhece agora.
/// </summary>
/// <param name="Key">Chave estável (não muda quando o G HUB reinicia ou o aparelho dorme). É o que o widget grava.</param>
/// <param name="Name">Nome que o programa do fabricante mostra.</param>
/// <param name="Percentage">Última leitura, ou <c>null</c> se nunca chegou uma.</param>
/// <param name="Connected">O aparelho está ligado agora. Desligado = dormindo ou fora de alcance; a leitura é a última.</param>
/// <param name="UpdatedAt">Quando chegou a última leitura.</param>
public sealed record BatteryDevice(
    string Key,
    string Name,
    DeviceKind Kind,
    int? Percentage,
    bool Charging,
    bool Connected,
    DateTimeOffset? UpdatedAt);

/// <summary>De onde vem a bateria (G HUB, Bluetooth; Xbox depois).</summary>
public interface IBatterySource
{
    /// <summary>Id curto gravado junto com a chave do aparelho (ex.: "ghub").</summary>
    string Id { get; }

    /// <summary>Nome para a tela de opções.</summary>
    string DisplayName { get; }

    /// <summary>Nome do programa da fonte, curto, para o tile ("G HUB fechado").</summary>
    string ShortName { get; }

    SourceState State { get; }

    /// <summary>Por que a fonte está indisponível (mensagem técnica), ou <c>null</c>.</summary>
    string? LastError { get; }

    /// <summary>Aparelhos com bateria que a fonte já viu nesta sessão, ligados ou não.</summary>
    IReadOnlyList<BatteryDevice> Devices { get; }

    /// <summary>Lista ou leitura mudou. Pode vir de qualquer thread.</summary>
    event Action? Changed;

    /// <summary>Pede a lista e as leituras de novo (tela acordou, botão de atualizar).</summary>
    void Refresh();

    /// <summary>Um widget passou a usar a fonte. O primeiro abre a conexão.</summary>
    void Acquire();

    /// <summary>Um widget saiu. O último fecha a conexão.</summary>
    Task ReleaseAsync();

    /// <summary>Texto de baixo do tile quando a fonte está indisponível (sem compactar).</summary>
    string OfflineText => $"{ShortName} fechado";

    /// <summary>O mesmo, curto, para o tile Mini.</summary>
    string OfflineShortText => "Fechado";

    /// <summary>Aviso das opções quando a fonte está indisponível.</summary>
    string UnavailableMessage => $"O {DisplayName} não está aberto. O tile volta sozinho quando ele abrir.";

    /// <summary>Explica nas opções por que o vínculo não troca de aparelho.</summary>
    string BindingHelp => "Fica gravado por um número fixo do aparelho: não troca de aparelho sozinho.";

    /// <summary>Arquivo de log da fonte, para mostrar nas opções; <c>null</c> = sem log.</summary>
    string? LogPath => null;
}

/// <summary>O aparelho que um widget mostra, como fica gravado nas configurações.</summary>
/// <param name="Source">Id da fonte (<see cref="IBatterySource.Id"/>).</param>
/// <param name="Key">Chave estável do aparelho (<see cref="BatteryDevice.Key"/>).</param>
/// <param name="Name">Nome quando foi escolhido: para mostrar "não encontrado" e para achar de novo se a chave mudar.</param>
public sealed record DeviceBinding(string Source, string Key, string Name);

/// <summary>Acha o aparelho gravado na lista atual. Nunca troca por outro aparelho.</summary>
public static class DeviceResolver
{
    /// <summary>
    /// Pela chave; se ela não está na lista, pelo nome, mas só quando um único aparelho tem aquele nome. Sem
    /// achar, devolve <c>null</c> (o widget mostra "não encontrado"), nunca o primeiro da lista.
    /// </summary>
    public static BatteryDevice? Find(DeviceBinding? binding, IReadOnlyList<BatteryDevice> devices)
    {
        if (binding is null || string.IsNullOrEmpty(binding.Key))
            return null;
        foreach (var device in devices)
        {
            if (string.Equals(device.Key, binding.Key, StringComparison.Ordinal))
                return device;
        }
        if (string.IsNullOrWhiteSpace(binding.Name))
            return null;
        BatteryDevice? byName = null;
        foreach (var device in devices)
        {
            if (!string.Equals(device.Name, binding.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (byName is not null)
                return null; // dois com o mesmo nome: melhor "não encontrado" do que mostrar o errado
            byName = device;
        }
        return byName;
    }
}
