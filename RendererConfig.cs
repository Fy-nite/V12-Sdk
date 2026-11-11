using System.Text.Json;

namespace V12.Sdk;

/// <summary>Reads the renderer.json written next to a published renderer host.</summary>
sealed class RendererConfig
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public int SchemaVersion { get; init; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Distribution { get; init; } = "";
    public bool SingleFile { get; init; }
    public string RuntimeIdentifier { get; init; } = "";
    public bool SelfContained { get; init; }
    public string Executable { get; init; } = "";
    public string[] Arguments { get; init; } = Array.Empty<string>();
    public string SourceProject { get; init; } = "";
    public string GamePakMode { get; init; } = "";

    /// <summary>
    /// Directory holding this renderer build. For the current layout that is
    /// <c>~/.purr/renderers/&lt;id&gt;/&lt;rid&gt;</c>; the legacy layout uses
    /// <c>~/.purr/renderers/&lt;id&gt;</c>.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Directory { get; set; } = "";

    /// <summary>
    /// The <c>&lt;id&gt;</c> folder this build was installed under. Kept separate
    /// from <see cref="Id"/> because a per-RID install's own folder is the RID.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string InstallId { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayRid => string.IsNullOrWhiteSpace(RuntimeIdentifier) ? "any" : RuntimeIdentifier;

    /// <summary>
    /// Loads a renderer config from a distribution directory.
    /// <paramref name="installId"/> is the parent <c>&lt;id&gt;</c> folder for a
    /// per-RID layout; when omitted the directory name is used (legacy layout).
    /// </summary>
    public static RendererConfig? Load(string directory, string? installId = null)
    {
        var path = Path.Combine(directory, "renderer.json");
        if (!File.Exists(path)) return null;

        try
        {
            var config = JsonSerializer.Deserialize<RendererConfig>(File.ReadAllText(path), JsonOptions);
            if (config == null) return null;

            config.Directory = directory;
            config.InstallId = string.IsNullOrWhiteSpace(installId)
                ? Path.GetFileName(directory)
                : installId;

            if (string.IsNullOrWhiteSpace(config.Id)) config.Id = config.InstallId;
            return string.IsNullOrWhiteSpace(config.Executable) ? null : config;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
