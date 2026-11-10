using System.Text.Json;

namespace V12.Sdk;

/// <summary>
/// Resolves the Purr home directory (~/.purr) and its conventional layout:
/// <code>
/// ~/.purr/
///   packages/        # local NuGet feed
///   bin/             # binaries/tools (compiler and SDK helpers)
///   renderers/
///     <id>/          # an installed renderer distribution
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
    public static string SdkDir => Path.Combine(Home, "sdk");
    public static string V12PropsPath => Path.Combine(SdkDir, "V12.props");
    public static string TemplatesDir => Path.Combine(Home, "templates");
    public static string NovaRendererDir => Path.Combine(RenderersDir, "nova");

    public static string RendererDir(string id) => Path.Combine(RenderersDir, id);

    /// <summary>
    /// Finds an installed renderer by folder or config id. With no id, prefers
    /// MonoGame (the current default) and otherwise uses the first renderer.
    /// </summary>
    public static RendererConfig? FindRenderer(string? id = null)
    {
        if (!Directory.Exists(RenderersDir)) return null;

        var configs = new List<RendererConfig>();
        foreach (var dir in Directory.GetDirectories(RenderersDir))
        {
            var config = RendererConfig.Load(dir);
            if (config != null) configs.Add(config);
        }

        if (configs.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(id))
        {
            return configs.FirstOrDefault(c =>
                string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(c.Directory), id, StringComparison.OrdinalIgnoreCase));
        }

        return configs.FirstOrDefault(c => string.Equals(c.Id, "monogame", StringComparison.OrdinalIgnoreCase))
            ?? configs[0];
    }

    public static void EnsureHome()
    {
        Directory.CreateDirectory(PackagesDir);
        Directory.CreateDirectory(BinDir);
        Directory.CreateDirectory(RenderersDir);
        Directory.CreateDirectory(SdkDir);
        Directory.CreateDirectory(TemplatesDir);
    }

    /// <summary>
    /// Writes <c>~/.purr/sdk/V12.props</c>, the single property file every
    /// scaffolded project imports. It carries the engine package version and
    /// adds the Purr package feed to restore. Run by <c>vcsdk sync</c>.
    /// </summary>
    public static void WriteV12Props(string version, string? packagesDir = null)
    {
        Directory.CreateDirectory(SdkDir);

        var resolved = string.IsNullOrWhiteSpace(packagesDir)
            ? PackagesDir
            : Path.GetFullPath(packagesDir);
        var isDefaultFeed = string.Equals(
            Path.GetFullPath(resolved).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(PackagesDir).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

        string feed = isDefaultFeed
            ? "$(PurrHome)/packages"
            : resolved.Replace('\\', '/');

        string xml =
            "<Project>\n" +
            "  <PropertyGroup>\n" +
            "    <PurrHome Condition=\"'$(PurrHome)' == '' and '$(PURR_HOME)' != ''\">$(PURR_HOME)</PurrHome>\n" +
            "    <PurrHome Condition=\"'$(PurrHome)' == '' and '$(USERPROFILE)' != ''\">$(USERPROFILE)/.purr</PurrHome>\n" +
            "    <PurrHome Condition=\"'$(PurrHome)' == '' and '$(HOME)' != ''\">$(HOME)/.purr</PurrHome>\n" +
            $"    <V12Version>{version}</V12Version>\n" +
            $"    <V12PackageFeed>{feed}</V12PackageFeed>\n" +
            "    <RestoreAdditionalProjectSources>$(RestoreAdditionalProjectSources);$(V12PackageFeed)</RestoreAdditionalProjectSources>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n";

        File.WriteAllText(V12PropsPath, xml, new System.Text.UTF8Encoding(false));
    }

    /// <summary>Reads the engine package version written by <c>vcsdk sync</c>, or null when not installed.</summary>
    public static string? ReadV12Version()
    {
        try
        {
            if (!File.Exists(V12PropsPath)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(V12PropsPath), "<V12Version>([^<]+)</V12Version>");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool HasPackagesFeed => Directory.Exists(PackagesDir) && Directory.GetFiles(PackagesDir, "*.nupkg", SearchOption.TopDirectoryOnly).Length > 0;
    public static bool HasNovaRenderer => Directory.Exists(NovaRendererDir)
        && (File.Exists(Path.Combine(NovaRendererDir, "project.godot")) || Directory.Exists(Path.Combine(NovaRendererDir, "libs")));

    /// <summary>
    /// Locates a local V12-Monorepo checkout without relying on a hardcoded
    /// machine path. Checks the current directory and its ancestors for the
    /// monorepo markers, then sibling "V12-Monorepo" folders, then the Purr
    /// source cache.
    /// </summary>
    public static string? FindMonorepo(string? startDirectory = null)
    {
        static bool LooksLikeMonorepo(string dir)
            => File.Exists(Path.Combine(dir, ".gitmodules"))
            && Directory.Exists(Path.Combine(dir, "V12-Engine"));

        var current = Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory());
        var info = new DirectoryInfo(current);
        for (int depth = 0; depth < 8 && info != null; depth++, info = info.Parent)
        {
            if (LooksLikeMonorepo(info.FullName)) return info.FullName;

            var sibling = Path.Combine(info.FullName, "V12-Monorepo");
            if (LooksLikeMonorepo(sibling)) return sibling;
        }

        var cached = Path.Combine(Home, "src", "V12-Monorepo");
        return LooksLikeMonorepo(cached) ? cached : null;
    }

    // -----------------------------------------------------------------------
    // MSBuild properties
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build the set of -p:... arguments that point a dotnet build at the
    /// Purr-provided libraries. Only adds a property if the target exists.
    /// </summary>
    public static string GetMsBuildArgs(string? gameDir = null)
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

        // Godot project dir for host builds (a game with an embedded renderer)
        // Only set if the game has no local godot project and Purr has a renderer.
        if (!hasLocalGodot && HasNovaRenderer && Directory.Exists(NovaRendererDir))
            args.Add($"-p:GodotProjectDir=\"{NovaRendererDir}\"");

        // Also expose Purr packages as a NuGet source via RestoreAdditionalProjectSources
        if (HasPackagesFeed)
            args.Add($"-p:RestoreAdditionalProjectSources=\"{PackagesDir}\"");

        return string.Join(" ", args);
    }

    public static string GetCclExe(string? overrideCompiler = null)
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
        ConOut.WriteLine($"[Purr] Created nuget.config with local feed: {feed}");
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
