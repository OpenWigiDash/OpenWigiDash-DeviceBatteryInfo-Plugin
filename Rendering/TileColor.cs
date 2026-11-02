using System.Drawing;
using System.Globalization;

namespace OpenWigiDash.DeviceBatteryInfo.Rendering;

/// <summary>Cor de fundo escolhida pelo usuário, salva como "#RRGGBB". Vazio = o padrão do widget.</summary>
public static class TileColor
{
    /// <summary>Sugestões mostradas nas configurações (o resto vem do seletor de cor).</summary>
    public static IReadOnlyList<(string Name, string Hex)> Presets { get; } =
    [
        ("Preto", "#000000"),
        ("Grafite", "#2B2D31"),
        ("Azul", "#5865F2"),
        ("Azul-escuro", "#1E3A8A"),
        ("Verde", "#23A55A"),
        ("Roxo", "#7C3AED"),
        ("Vermelho", "#DA373C"),
        ("Laranja", "#F0932B"),
        ("Rosa", "#EB459E"),
    ];

    public static Color? Parse(string? text)
    {
        var hex = text?.Trim().TrimStart('#');
        if (hex is not { Length: 6 } || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return null;
        return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    public static string? Normalize(string? text) => Parse(text) is { } color ? ToHex(color) : null;

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
