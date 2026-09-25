using System.Text.Encodings.Web;
using System.Text.Json;

namespace InstallerBuilder.Core;

/// <summary>A profil mentése / betöltése JSON-ba.</summary>
public static class ProfileStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(BuildProfile profile) => JsonSerializer.Serialize(profile, Options);

    public static BuildProfile Deserialize(string json) =>
        JsonSerializer.Deserialize<BuildProfile>(json, Options) ?? new BuildProfile();

    public static void Save(BuildProfile profile, string path) => File.WriteAllText(path, Serialize(profile));

    public static BuildProfile Load(string path) => Deserialize(File.ReadAllText(path));
}
