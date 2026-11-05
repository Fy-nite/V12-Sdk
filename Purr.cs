using System.Text.Json;

namespace V12.Sdk;

/// <summary>
/// Resolves the Purr home directory (~/.purr) and its conventional layout:
/// <code>
/// ~/.purr/
///   packages/        # local NuGet feed (Finite.V12, V12.Basic, Nova, etc.)
///   bin/             # binaries/tools (ccl, vcsdk helpers)
///   renderers/
///     nova/          # Nova renderer distribution
/// </code>
/// Also provides MSBuild property helpers so `vcsdk build` can compile
/// dotnet projects against the Purr-provided libraries via `msbuild -p:...`.
/// </summary>
static class Purr
{
    public static string Home
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("PURR_HOME");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
                return Path.GetFullPath(env);

            // On Windows USERPROFILE, on Unix HOME
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? Directory.GetCurrentDirectory();

            return Path.Combine(home, ".purr");
        }
    }

    public static string PackagesDir => Path.Combine(Home, "packages");
    public static string BinDir => Path.Combine(Home, "bin");
    public static string RenderersDir => Path.Combine(Home, "renderers");
    public static string NovaRendererDir => Path.Combine(RenderersDir, "nova");

    public static void EnsureHome()
    {
        Directory.CreateDirectory(PackagesDir);
        Directory.CreateDirectory(BinDir);
        Directory.CreateDirectory(RenderersDir);
    }

    public static bool HasPackagesFeed => Directory.Exists(PackagesDir) && Directory.GetFiles(PackagesDir, "*.nupkg", SearchOption.TopDirectoryOnly).Length > 0;
    public static bool HasNovaRenderer => Directory.Exists(NovaRendererDir)
        && (File.Exists(Path.Combine(NovaRendererDir, "project.godot")) || Directory.Exists(Path.Combine(NovaRendererDir, "libs")));

    // -----------------------------------------------------------------------
    // MSBuild properties
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build the set of -p:... arguments that point a dotnet build at the
    /// Purr-provided libraries. Only adds a property if the target exists.
    /// </summary>
    public static string GetMsBuildArgs(string gameDir = null)
    {
        var args = new List<string>();

        // Always expose PurrHome so Directory.Build.targets can derive other paths
        args.Add($"-p:PurrHome=\"{Home}\"");

        // Check if the game has a local godot project; if not, use the Purr renderer
        bool hasLocalGodot = false;
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            hasLocalGodot = File.Exists(Path.Combine(gameDir, "godot", "project.godot"))
                         || File.Exists(Path.Combine(gameDir, "..", "godot", "project.godot"))
                         || Directory.GetFiles(gameDir, "*.csproj", SearchOption.TopDirectoryOnly)
                             .Any(f => File.ReadAllText(f).Contains("GodotProjectDir"));
        }

        // V12.Basic root - only set from Purr if the game has no local V12.Basic.
        // This keeps local dev (vendored V12.Basic) working while allowing
        // Purr-based games to build against the renderer checkout.
        bool hasLocalV12Basic = !string.IsNullOrWhiteSpace(gameDir) &&
            (File.Exists(Path.Combine(gameDir, "libs", "V12.Basic", "V12.Basic.csproj")) ||
             File.Exists(Path.Combine(gameDir, "..", "libs", "V12.Basic", "V12.Basic.csproj")) ||
             File.Exists(Path.Combine(gameDir, "godot", "libs", "Nova", "libs", "V12.Basic", "V12.Basic.csproj")) ||
             Directory.GetFiles(gameDir, "V12.Basic.csproj", SearchOption.AllDirectories).Length > 0);

        if (!hasLocalV12Basic && HasNovaRenderer)
        {
            var v12BasicCandidates = new[]
            {
                Path.Combine(NovaRendererDir, "libs", "V12.Basic", "V12.Basic.csproj"),
                Path.Combine(NovaRendererDir, "V12.Basic.csproj"),
                Path.Combine(Home, "..", "V12.Basic", "V12.Basic.csproj"),
            };

            foreach (var candidate in v12BasicCandidates)
            {
                if (File.Exists(candidate))
                {
                    var root = Path.GetDirectoryName(candidate)!;
                    // V12BasicRoot is the directory containing V12.Basic.csproj
                    args.Add($"-p:V12BasicRoot=\"{root}\"");
                    args.Add($"-p:V12EngineProject=\"{Path.Combine(root, "libs", "V12", "V12.csproj")}\"");
                    args.Add($"-p:V12BindingsProject=\"{Path.Combine(root, "libs", "V12.Bindings", "V12.Bindings.csproj")}\"");
                    break;
                }
            }

            // If no renderer checkout, try to find a V12.Basic package in the feed
            if (!args.Any(a => a.Contains("V12BasicRoot")))
            {
                var latestBasic = FindLatestPackageDir("Finite.V12.Basic");
                if (latestBasic != null)
                    args.Add($"-p:V12BasicRoot=\"{latestBasic}\"");
            }
        }

        // Godot project dir for host builds (EcoVR-like game with embedded renderer)
        // Only set if the game has no local godot project and Purr has a renderer.
        if (!hasLocalGodot && HasNovaRenderer && Directory.Exists(NovaRendererDir))
            args.Add($"-p:GodotProjectDir=\"{NovaRendererDir}\"");

        // Also expose Purr packages as a NuGet source via RestoreAdditionalProjectSources
        if (HasPackagesFeed)
            args.Add($"-p:RestoreAdditionalProjectSources=\"{PackagesDir}\"");

        return string.Join(" ", args);
    }

    public static string GetCclExe(string overrideCompiler = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideCompiler) && overrideCompiler != "ccl")
            return overrideCompiler;

        var purrCcl = FindTool("ccl");
        if (purrCcl != null) return purrCcl;

        var alt = FindTool("contract");
        if (alt != null) return alt;

        return overrideCompiler ?? "ccl";
    }

    /// <summary>
    /// Ensure the game directory has a nuget.config that adds the Purr feed.
    /// Returns true if it created/modified the file.
    /// </summary>
    public static bool EnsureNuGetConfig(string gameDir)
    {
        if (!HasPackagesFeed) return false;

        var configPath = Path.Combine(gameDir, "nuget.config");
        var feed = PackagesDir;

        if (File.Exists(configPath))
        {
            var text = File.ReadAllText(configPath);
            if (text.Contains(feed, StringComparison.OrdinalIgnoreCase))
                return false;
            // Simple: if it exists but doesn't contain the feed, we don't overwrite.
            // The user can add it manually. We could merge, but keep it simple.
            return false;
        }

        var xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <add key=""purr"" value=""{feed}"" />
    <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" />
  </packageSources>
</configuration>";
        File.WriteAllText(configPath, xml);
        Console.WriteLine($"[Purr] Created nuget.config with local feed: {feed}");
        return true;
    }

    public static string? FindTool(string name)
    {
        // Check Purr bin first
        var candidates = new[]
        {
            Path.Combine(BinDir, name),
            Path.Combine(BinDir, name + ".exe"),
            Path.Combine(BinDir, name + ".dll"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return null;
    }

    public static string GetNuGetSourcesArgs()
    {
        var sources = new List<string>();
        if (Directory.Exists(PackagesDir))
            sources.Add($"--source \"{PackagesDir}\"");

        // Always include nuget.org as fallback
        sources.Add("--source https://api.nuget.org/v3/index.json");
        return string.Join(" ", sources);
    }

    public static string[] GetNuGetFeedPaths()
    {
        var feeds = new List<string>();
        if (Directory.Exists(PackagesDir)) feeds.Add(PackagesDir);
        return feeds.ToArray();
    }

    public static string? FindLatestPackageDir(string packageId)
    {
        if (!Directory.Exists(PackagesDir)) return null;

        // NuGet feed layout: packages are .nupkg files, but some setups
        // extract to packages/<id>/<version>/content.
        // We support both: look for directory packages/<id>/<version>
        var idLower = packageId.ToLowerInvariant();
        var direct = Path.Combine(PackagesDir, packageId);
        if (Directory.Exists(direct))
        {
            var versions = Directory.GetDirectories(direct)
                .Select(d => new { Path = d, Version = TryParseVersion(Path.GetFileName(d)) })
                .Where(x => x.Version != null)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
            if (versions != null) return versions.Path;
        }

        // Also scan for .nupkg files
        var nupkgs = Directory.GetFiles(PackagesDir, $"{packageId}*.nupkg", SearchOption.TopDirectoryOnly);
        if (nupkgs.Length > 0)
        {
            var withVer = nupkgs.Select(p => new { Path = p, Version = TryParseVersionFromNupkg(p, packageId) })
                .Where(x => x.Version != null)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
            if (withVer != null)
            {
                // For nupkg feed, the msbuild -p should point to the unpacked content.
                // We return the packages dir itself and rely on NuGet restore;
                // the -p for V12BasicRoot is not needed for PackageReference.
                // Return null to indicate "feed only" case.
                return null;
            }
        }

        // Fallback: case-insensitive scan
        foreach (var dir in Directory.GetDirectories(PackagesDir))
        {
            if (string.Equals(Path.GetFileName(dir), packageId, StringComparison.OrdinalIgnoreCase))
            {
                var versions = Directory.GetDirectories(dir)
                    .Select(d => new { Path = d, Version = TryParseVersion(Path.GetFileName(d)) })
                    .Where(x => x.Version != null)
                    .OrderByDescending(x => x.Version)
                    .FirstOrDefault();
                if (versions != null) return versions.Path;
            }
        }

        return null;
    }

    private static Version? TryParseVersion(string s)
    {
        // Handle versions like "1.0.0", "1.0.0-beta.1"
        var dash = s.IndexOf('-');
        var core = dash >= 0 ? s.Substring(0, dash) : s;
        if (Version.TryParse(core, out var v)) return v;
        return null;
    }

    private static Version? TryParseVersionFromNupkg(string path, string packageId)
    {
        var file = Path.GetFileNameWithoutExtension(path); // e.g. Finite.V12.Basic.1.0.0
        var prefix = packageId + ".";
        if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var verStr = file.Substring(prefix.Length);
            return TryParseVersion(verStr);
        }
        return null;
    }
}
