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
    public string Directory { get; set; } = "";

    public static RendererConfig? Load(string directory)
    {
        var path = Path.Combine(directory, "renderer.json");
        if (!File.Exists(path)) return null;

        try
        {
            var config = JsonSerializer.Deserialize<RendererConfig>(File.ReadAllText(path), JsonOptions);
            if (config == null) return null;

            config.Directory = directory;
            if (string.IsNullOrWhiteSpace(config.Id)) config.Id = Path.GetFileName(directory);
            return string.IsNullOrWhiteSpace(config.Executable) ? null : config;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
