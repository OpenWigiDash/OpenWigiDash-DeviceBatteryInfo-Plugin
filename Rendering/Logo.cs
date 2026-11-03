using System.Drawing;
using System.Runtime.InteropServices;

namespace OpenWigiDash.DeviceBatteryInfo.Rendering;

/// <summary>Logos das fontes, embutidos no plugin (Assets).</summary>
internal static class Logo
{
    public static Bitmap? Load(string resourceName)
    {
        try
        {
            using var stream = typeof(Logo).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return null;
            using var loaded = new Bitmap(stream);
            return new Bitmap(loaded);
        }
        catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException or TypeInitializationException or ExternalException)
        {
            return null; // sem GDI+ (testes fora do Windows): fica a letra
        }
    }
}
