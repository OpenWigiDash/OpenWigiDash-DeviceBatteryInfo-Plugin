using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenWigi.Sdk;

namespace OpenWigiDash.DeviceBatteryInfo.Rendering;

/// <summary>Em que situação o aparelho está, do ponto de vista do tile.</summary>
public enum BatteryTileState
{
    /// <summary>Ligado, com leitura nova.</summary>
    Online,
    /// <summary>Dormindo ou fora de alcance: mostra a última leitura apagada, com "Zz".</summary>
    Asleep,
    /// <summary>O aparelho gravado não está na lista (desligado há muito tempo, ou o G HUB ainda não o viu).</summary>
    NotFound,
    /// <summary>O programa da fonte (G HUB) está fechado.</summary>
    SourceOffline,
    /// <summary>Nenhum aparelho escolhido ainda.</summary>
    NotConfigured,
}

/// <summary>O que o tile de bateria mostra.</summary>
/// <param name="Title">Texto de cima (nome do aparelho ou o título escolhido); vazio = sem texto.</param>
/// <param name="Percentage">Leitura (a última, se não está online); <c>null</c> = desconhecida.</param>
/// <param name="Detail">Texto de baixo no lugar do percentual (ex.: "G HUB fechado").</param>
public sealed record BatteryVisual(string? Title, int? Percentage, bool Charging, BatteryTileState State, string? Detail = null)
{
    /// <summary>Abaixo disso (inclusive) a bateria fica vermelha.</summary>
    public int LowLevel { get; init; } = 20;

    /// <summary>Cor de fundo escolhida; <c>null</c> = a padrão.</summary>
    public Color? Background { get; init; }

    /// <summary>Selo da fonte no canto (ex.: "G" do G HUB); <c>null</c> = sem selo.</summary>
    public SourceBadge? Badge { get; init; }
}

/// <summary>Selo da fonte no canto superior direito: uma imagem (logo) ou, sem ela, letras num quadrado colorido.</summary>
public sealed record SourceBadge(string Text, Color Color)
{
    /// <summary>Logo redondo da fonte (ex.: o "G" do G HUB). Desenhado por inteiro, sem o quadrado.</summary>
    public Bitmap? Image { get; init; }
}

/// <summary>
/// Desenha o tile de bateria: título em cima, a pilha com 5 barras no meio e o percentual embaixo, como o "G Hub
/// Device" do Stream Deck. A cor diz o estado: verde quase cheia (85% ou mais) ou carregando, dourado no meio,
/// vermelho com pouca bateria e cinza
/// quando o aparelho não está respondendo.
/// </summary>
public static class BatteryRenderer
{
    public static readonly Color DefaultBackground = Color.FromArgb(0x14, 0x17, 0x2B);
    public static readonly Color NormalColor = Color.FromArgb(0xE8, 0xB9, 0x23);
    public static readonly Color LowColor = Color.FromArgb(0xE5, 0x48, 0x4D);
    public static readonly Color ChargingColor = Color.FromArgb(0x3B, 0xC4, 0x6B);
    /// <summary>Daqui para cima (inclusive) a pilha fica verde: quase cheia.</summary>
    public const int HighLevel = 85;
    public static readonly Color HighColor = Color.FromArgb(0x3B, 0xC4, 0x6B);
    public static readonly Color DimColor = Color.FromArgb(0x6E, 0x72, 0x80);
    private static readonly Color DimText = Color.FromArgb(0x9A, 0x9E, 0xAA);

    /// <summary>Barras da pilha.</summary>
    public const int Segments = 5;

    /// <summary>Barras acesas para um percentual: 1 a 20% = 1 barra, 81 a 100% = 5; 0% = nenhuma.</summary>
    public static int LitSegments(int percentage) =>
        percentage <= 0 ? 0 : Math.Min(Segments, (int)Math.Ceiling(Math.Min(percentage, 100) / (100.0 / Segments)));

    /// <summary>Cor da pilha para o estado e o percentual.</summary>
    public static Color AccentFor(BatteryVisual visual)
    {
        if (visual.State != BatteryTileState.Online)
            return DimColor;
        if (visual.Charging)
            return ChargingColor;
        return visual.Percentage switch
        {
            { } p when p <= visual.LowLevel => LowColor,
            { } p when p >= HighLevel => HighColor,
            _ => NormalColor,
        };
    }

    /// <summary>Texto de baixo: o detalhe, ou o percentual ("54%"), ou "--%".</summary>
    public static string BottomText(BatteryVisual visual) =>
        !string.IsNullOrWhiteSpace(visual.Detail) ? visual.Detail!
        : visual.Percentage is { } p ? $"{p}%" : "--%";

    public static WidgetFrame Render(PixelSize size, WidgetTheme theme, BatteryVisual visual)
    {
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(visual.Background ?? DefaultBackground);

            float w = size.Width, h = size.Height;
            var hasTitle = !string.IsNullOrWhiteSpace(visual.Title);
            var titleBand = hasTitle ? h * 0.24f : 0f;
            var bottomBand = h * 0.27f;
            var middle = new RectangleF(0, titleBand, w, h - titleBand - bottomBand);

            if (hasTitle)
            {
                // Com selo, o título encolhe dos dois lados para continuar centrado sem passar por baixo dele.
                var inset = visual.Badge is null ? 3f : BadgeSide(size) + 4f;
                DrawText(g, theme, visual.Title!, new RectangleF(inset, h * 0.03f, w - 2 * inset, titleBand), Color.White, bold: false);
            }

            var accent = AccentFor(visual);
            var body = BatteryBody(middle);
            DrawBattery(g, body, accent, visual);

            var bottomColor = visual.State == BatteryTileState.Online ? Color.White : DimText;
            var small = !string.IsNullOrWhiteSpace(visual.Detail);
            var bottom = new RectangleF(3, h - bottomBand, w - 6, bottomBand - h * 0.04f);
            DrawText(g, theme, BottomText(visual), small ? RectangleF.Inflate(bottom, 0, -bottom.Height * 0.12f) : bottom,
                bottomColor, bold: !small);

            if (visual.Badge is { } badge)
            {
                DrawBadge(g, size, badge);
                // Com selo, o "Zz" fica logo abaixo dele, no canto, longe do título e do selo.
                if (visual.State == BatteryTileState.Asleep)
                {
                    var rect = BadgeRect(size);
                    DrawMark(g, "Zz", new RectangleF(rect.X - rect.Width * 0.1f, rect.Bottom + rect.Height * 0.04f,
                        rect.Width * 1.2f, rect.Height * 0.62f), DimText);
                }
            }
        }
        return ToFrame(bitmap);
    }

    /// <summary>Retângulo do corpo da pilha (sem o polo), centrado na área do meio, na proporção 2,1 : 1.</summary>
    internal static RectangleF BatteryBody(RectangleF area)
    {
        var height = Math.Min(area.Height * 0.86f, area.Width * 0.62f / 2.1f);
        var width = height * 2.1f;
        var nub = height * 0.14f;
        return new RectangleF(area.X + (area.Width - width - nub) / 2f, area.Y + (area.Height - height) / 2f, width, height);
    }

    private static void DrawBattery(Graphics g, RectangleF body, Color accent, BatteryVisual visual)
    {
        var stroke = Math.Max(1.5f, body.Height * 0.09f);
        var radius = body.Height * 0.16f;

        // Polo à direita.
        var nubWidth = body.Height * 0.14f;
        var nub = new RectangleF(body.Right + stroke * 0.2f, body.Y + body.Height * 0.3f, nubWidth, body.Height * 0.4f);
        using (var nubPath = Rounded(nub, nubWidth * 0.4f))
        using (var brush = new SolidBrush(accent))
            g.FillPath(brush, nubPath);

        var outline = RectangleF.Inflate(body, -stroke / 2f, -stroke / 2f);
        using (var path = Rounded(outline, radius))
        using (var pen = new Pen(accent, stroke))
            g.DrawPath(pen, path);

        // Barras.
        var inner = RectangleF.Inflate(body, -stroke * 1.9f, -stroke * 1.9f);
        var gap = Math.Max(1f, inner.Width * 0.035f);
        var segment = (inner.Width - gap * (Segments - 1)) / Segments;
        var lit = visual.Percentage is { } p ? LitSegments(p) : 0;
        using (var on = new SolidBrush(accent))
        using (var off = new SolidBrush(Color.FromArgb(40, accent)))
        {
            for (var i = 0; i < Segments; i++)
            {
                var r = new RectangleF(inner.X + i * (segment + gap), inner.Y, segment, inner.Height);
                g.FillRectangle(i < lit ? on : off, r);
            }
        }

        switch (visual.State)
        {
            case BatteryTileState.Online when visual.Charging:
                DrawBolt(g, body);
                break;
            case BatteryTileState.Asleep when visual.Badge is null:
                DrawMark(g, "Zz", new RectangleF(body.Right - body.Height * 0.55f, body.Y - body.Height * 0.62f,
                    body.Height * 0.9f, body.Height * 0.62f), DimText);
                break;
            case BatteryTileState.NotFound:
            case BatteryTileState.NotConfigured:
                DrawMark(g, "?", body, Color.White);
                break;
            case BatteryTileState.SourceOffline:
                DrawMark(g, "!", body, Color.White);
                break;
        }
    }

    /// <summary>Raio de carregando, branco com contorno escuro, no meio da pilha.</summary>
    private static void DrawBolt(Graphics g, RectangleF body)
    {
        var cx = body.X + body.Width / 2f;
        var top = body.Y + body.Height * 0.08f;
        var hgt = body.Height * 0.84f;
        var wid = hgt * 0.62f;
        PointF[] points =
        [
            new(cx + wid * 0.10f, top),
            new(cx - wid * 0.45f, top + hgt * 0.56f),
            new(cx - wid * 0.02f, top + hgt * 0.56f),
            new(cx - wid * 0.12f, top + hgt),
            new(cx + wid * 0.45f, top + hgt * 0.40f),
            new(cx + wid * 0.02f, top + hgt * 0.40f),
        ];
        using var path = new GraphicsPath();
        path.AddPolygon(points);
        using var pen = new Pen(Color.FromArgb(20, 20, 20), Math.Max(1.5f, hgt * 0.1f)) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, path);
        using var fill = new SolidBrush(Color.White);
        g.FillPath(fill, path);
    }

    private static void DrawMark(Graphics g, string text, RectangleF box, Color color)
    {
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var family = new FontFamily(GenericFontFamilies.SansSerif);
        using var path = new GraphicsPath();
        path.AddString(text, family, (int)FontStyle.Bold, box.Height * 0.78f, box, format);
        using var pen = new Pen(Color.FromArgb(20, 20, 20), Math.Max(2f, box.Height * 0.12f)) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, path);
        using var fill = new SolidBrush(color);
        g.FillPath(fill, path);
    }

    private static void DrawText(Graphics g, WidgetTheme theme, string text, RectangleF box, Color color, bool bold)
    {
        using var family = ResolveFamily(bold ? theme.SecondaryFontFamily : theme.PrimaryFontFamily);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        var style = bold ? FontStyle.Bold : FontStyle.Regular;
        var emSize = Math.Max(9f, box.Height * 0.82f);
        using (var probe = new Font(family, emSize, style, GraphicsUnit.Pixel))
        {
            var measured = g.MeasureString(text, probe).Width;
            if (measured > box.Width && measured > 0)
                emSize = Math.Max(8f, emSize * box.Width / measured);
        }
        using var font = new Font(family, emSize, style, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, box, format);
    }

    /// <summary>Lado do selo: 28% do lado menor do tile (no Small, ~40 px; no Mini, ~19 px).</summary>
    internal static float BadgeSide(PixelSize size) => Math.Max(14f, Math.Min(size.Width, size.Height) * 0.28f);

    /// <summary>Onde o selo fica: canto superior direito.</summary>
    internal static RectangleF BadgeRect(PixelSize size)
    {
        var side = BadgeSide(size);
        var margin = Math.Max(2f, side * 0.08f);
        return new RectangleF(size.Width - side - margin, margin, side, side);
    }

    private static void DrawBadge(Graphics g, PixelSize size, SourceBadge badge)
    {
        var rect = BadgeRect(size);
        var side = rect.Width;
        if (badge.Image is { } image)
        {
            lock (image)
            {
                var previous = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, rect);
                g.InterpolationMode = previous;
            }
            // Anel fino claro: o círculo preto do logo some no fundo escuro sem ele.
            using var ring = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1f, side * 0.035f));
            g.DrawEllipse(ring, RectangleF.Inflate(rect, -side * 0.02f, -side * 0.02f));
            return;
        }
        using (var path = Rounded(rect, side * 0.25f))
        using (var brush = new SolidBrush(badge.Color))
            g.FillPath(brush, path);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var family = new FontFamily(GenericFontFamilies.SansSerif);
        using var font = new Font(family, side * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.White);
        g.DrawString(badge.Text, font, text, rect, format);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static FontFamily ResolveFamily(string name)
    {
        try
        {
            return new FontFamily(name);
        }
        catch (ArgumentException)
        {
            return new FontFamily(GenericFontFamilies.SansSerif);
        }
    }

    /// <summary>Bitmap para quadro Bgra32 (o mesmo dos outros plugins; candidato ao SDK).</summary>
    internal static WidgetFrame ToFrame(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new WidgetFrame(new PixelSize(bitmap.Width, bitmap.Height), FramePixelFormat.Bgra32, pixels, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
