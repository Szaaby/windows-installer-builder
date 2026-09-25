using System.Reflection;

namespace InstallerBuilder.Core;

/// <summary>A telepítés közben futó szkriptek (a Resources mappából, beépítve).</summary>
public static class EmbeddedScripts
{
    public static string Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Hiányzó beépített fájl: {name}");
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
