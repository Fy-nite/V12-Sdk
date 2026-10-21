using V12.Pak;

namespace V12.Sdk;

/// <summary>Packs a game directory into a .v12pak archive (tar.gz with manifest.json).</summary>
public static class V12PakPackager
{
    /// <summary>
    /// Pack a game directory into a .v12pak file. Delegates to V12.Pak so the
    /// archive and manifest are written by the same code the engine loads with.
    /// </summary>
    public static void PackGame(string gameDir, string outputPath)
    {
        using var writer = new V12PakWriter(outputPath);
        var manifest = writer.PackFromDirectory(gameDir);
        writer.Finalize(manifest);
    }
}
