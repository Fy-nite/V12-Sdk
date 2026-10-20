using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace V12.Sdk;

/// <summary>Packs a game directory into a .v12pak archive (tar.gz with manifest.json).</summary>
public static class V12PakPackager
{
    /// <summary>Pack a game directory into a .v12pak file.</summary>
    public static void PackGame(string gameDir, string outputPath)
    {
        var entries = new List<(string Path, byte[] Data)>();
        var normalizedRoot = Path.GetFullPath(gameDir);

        // Discover worlds
        foreach (var worldXml in Directory.EnumerateFiles(gameDir, "world.xml", SearchOption.AllDirectories))
        {
            var worldDir = Path.GetDirectoryName(worldXml)!;
            var rel = Normalize(worldDir.Substring(normalizedRoot.Length));
            if (!string.IsNullOrEmpty(rel))
                AddDir(entries, rel + "/", worldDir);
            else
                entries.Add(("world.xml", File.ReadAllBytes(worldXml)));
        }

        // Assets
        foreach (var atype in new[] { "meshes", "textures", "audio", "fonts" })
        {
            var adir = Path.Combine(gameDir, atype);
            if (Directory.Exists(adir)) AddDir(entries, "assets/" + atype + "/", adir);
        }

        var assetsRoot = Path.Combine(gameDir, "Assets");
        if (Directory.Exists(assetsRoot)) AddDir(entries, "Assets/", assetsRoot);

        // Scripts & DLLs
        var scriptsDir = Path.Combine(gameDir, "scripts");
        if (Directory.Exists(scriptsDir)) AddFilesMatching(entries, "scripts/", scriptsDir, "*.ct");
        
        var paksDir = Path.Combine(gameDir, "paks");
        if (Directory.Exists(paksDir)) AddFilesMatching(entries, "", paksDir, "*.dll");

        // Write tar.gz
        using var fs = File.Create(outputPath);
        using var gzip = new GZipStream(fs, CompressionLevel.Optimal);
        var bin = new BinaryWriter(gzip, Encoding.UTF8, true);

        // Manifest
        var manifest = BuildManifest(entries);
        var manifestJson = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        WriteEntry(bin, "manifest.json", manifestJson);

        // Entries
        foreach (var (path, data) in entries) WriteEntry(bin, path, data);
    }

    private static Dictionary<string, object> BuildManifest(List<(string, byte[])> entries)
    {
        var worlds = new List<object>();
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scripts = new List<string>();
        var dlls = new List<string>();

        foreach (var (path, _) in entries)
        {
            if (path.Contains("/world.xml"))
            {
                var parts = path.Split('/');
                worlds.Add(new { name = parts.Length > 0 ? parts[parts.Length - 2] : "default", entry = path });
            }
            if (path.StartsWith("assets/") || path.StartsWith("Assets/")) assets[path] = path;
            if (path.StartsWith("scripts/")) scripts.Add(path);
            if (path.EndsWith(".dll") && !path.Contains("/")) dlls.Add(path);
        }

        return new Dictionary<string, object>
        {
            ["version"] = 1, ["engine"] = "v12", ["worlds"] = worlds,
            ["assets"] = assets, ["scripts"] = scripts, ["paks"] = dlls
        };
    }

    private static void AddDir(List<(string, byte[])> e, string prefix, string dir)
    {
        foreach (var fp in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            e.Add((prefix + Normalize(fp.Substring(Path.GetFullPath(dir).Length)), File.ReadAllBytes(fp)));
    }

    private static void AddFilesMatching(List<(string, byte[])> e, string prefix, string dir, string pattern)
    {
        foreach (var fp in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
            e.Add((prefix + Normalize(fp.Substring(Path.GetFullPath(dir).Length)), File.ReadAllBytes(fp)));
    }

    private static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/');

    private static void WriteEntry(BinaryWriter bw, string name, byte[] data)
    {
        byte[] header = new byte[512];
        var nb = Encoding.UTF8.GetBytes(name);
        Array.Copy(nb, 0, header, 0, Math.Min(nb.Length, 100));
        var sizeStr = data.Length.ToString("x8").PadLeft(11, '0');
        Array.Copy(Encoding.ASCII.GetBytes(sizeStr), 0, header, 124, 11);
        header[156] = (byte)'0';
        bw.Write(header);

        int remaining = data.Length;
        while (remaining > 0)
        {
            var block = new byte[512];
            Array.Copy(data, data.Length - remaining, block, 0, Math.Min(512, remaining));
            bw.Write(block);
            remaining -= 512;
        }
    }
}
