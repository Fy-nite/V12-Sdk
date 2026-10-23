using V12.Pak;

namespace V12.Sdk;

/// <summary>Packs a game directory into a .v12pak archive (tar.gz with manifest.json).</summary>
public static class V12PakPackager
{
    /// <summary>
    /// Pack a game directory into a .v12pak file. Uses the directory's own
    /// <c>manifest.json</c> when present, otherwise generates one by scanning
    /// the layout (worlds/, assets/, paks/, scripts/).
    /// </summary>
    public static void PackGame(string gameDir, string outputPath)
        => V12PakBuilder.Build(gameDir, outputPath);

    /// <summary>Pack a game directory using exactly the supplied manifest.</summary>
    public static void PackGame(string gameDir, string outputPath, V12PakManifest manifest)
        => V12PakBuilder.Build(gameDir, outputPath, manifest);

    /// <summary>Pack a game directory using the manifest at <paramref name="manifestPath"/>.</summary>
    public static void PackGame(string gameDir, string outputPath, string manifestPath)
        => V12PakBuilder.Build(gameDir, outputPath, manifestPath);
}
