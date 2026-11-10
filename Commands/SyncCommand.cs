using System.Xml.Linq;

namespace V12.Sdk.Commands;

static class SyncCommand
{
    /// <summary>
    /// The modules that ship as NuGet packages today. Kept explicit so the
    /// package ids and pack order are stable, and so a standalone checkout
    /// silently skips anything that isn't present. New root-level
    /// <c>Finite.V12.*</c> modules are discovered automatically afterwards.
    /// </summary>
    private static readonly (string ProjectPath, string PackageId)[] KnownModules =
    {
        // Contract runtime chain. Finite.V12 declares a Contract.Runtime
        // dependency, so these must be in the feed for a plain PackageReference
        // restore to succeed.
        ("Contract/libs/ObjektRT.Core/ObjektRT.Core.csproj", "ObjektRT.Core"),
        ("Contract/libs/ObjektRT.Stdlib/ObjektRT.Stdlib.csproj", "ObjektRT.Stdlib"),
        ("Contract/libs/Objekt-RT/src/ObjectRT.Abstractions/ObjectRT.Abstractions.csproj", "ObjectRT.Abstractions"),
        ("Contract/libs/Objekt-RT/src/ObjectRT.VM/ObjectRT.VM.csproj", "ObjectRT.VM"),
        ("Contract/libs/Objekt-RT/src/ObjectRT.Runtime/ObjectRT.Runtime.csproj", "ObjectRT.Runtime"),
        ("Contract/Contract.Compiler.Abstractions/Contract.Compiler.Abstractions.csproj", "Contract.Compiler.Abstractions"),
        ("Contract/Contract.Compiler/Contract.Compiler.csproj", "Contract.Compiler"),
        ("Contract/Contract.Runtime/Contract.Runtime.csproj", "Contract.Runtime"),

        // Engine modules.
        ("V12-Engine/V12.csproj", "Finite.V12"),
        ("V12.Rendering/V12.Rendering.csproj", "Finite.V12.Rendering"),
        ("V12.Audio/V12.Audio.csproj", "Finite.V12.Audio"),
        ("V12.Audio.OwnAudio/V12.Audio.OwnAudio.csproj", "Finite.V12.Audio.OwnAudio"),
        ("V12.Basic/V12.Basic.csproj", "Finite.V12.Basic"),
        ("V12.Paper/V12.Paper.csproj", "Finite.V12.Paper"),
        ("V12.Editor/V12.Editor.csproj", "Finite.V12.Editor"),
        ("V12.Pak/V12.Pak.csproj", "Finite.V12.Pak"),
    };

    public static int Handle(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help"))
        {
            ShowHelp();
            return 0;
        }

        var monorepoOpt = GetOpt(args, "--monorepo", "--url");
        var refOpt = GetOpt(args, "--ref", "--branch") ?? "main";
        var versionOpt = GetOpt(args, "--version", "-v");
        var outputOpt = GetOpt(args, "--output", "-o");
        var force = args.Contains("--force") || args.Contains("-f");
        var noBuild = args.Contains("--no-build");
        var verbose = args.Contains("--verbose");
        var dev = args.Contains("--dev") || args.Contains("-d");

        // Resolve version
        var version = versionOpt ?? TryReadV12VersionFromMonorepo() ?? "1.0.0";

        Purr.EnsureHome();
        var packagesDir = !string.IsNullOrWhiteSpace(outputOpt)
            ? Path.GetFullPath(outputOpt)
            : Purr.PackagesDir;

        Directory.CreateDirectory(packagesDir);

        // Resolve monorepo source: --monorepo > env > local dev > remote default
        var envUrl = Environment.GetEnvironmentVariable("V12_MONOREPO_URL");
        var defaultUrl = "https://github.com/Fy-nite/V12.git";
        var monorepoUrl = monorepoOpt ?? envUrl ?? defaultUrl;

        // Prefer a local checkout when run from inside one, but never assume a
        // machine path. --monorepo and V12_MONOREPO_URL always win.
        if (monorepoUrl == defaultUrl)
        {
            var localMonorepo = Purr.FindMonorepo();
            if (localMonorepo != null)
            {
                ConOut.WriteLine($"[sync] Using local monorepo at {localMonorepo} (override with --monorepo <url>)");
                monorepoUrl = localMonorepo;
            }
        }

        string? monorepoDir = null;
        var isLocalPath = Directory.Exists(monorepoUrl) || File.Exists(monorepoUrl);

        if (isLocalPath)
        {
            // Local path — use directly, no clone
            monorepoDir = Path.GetFullPath(monorepoUrl);
            ConOut.WriteLine($"[sync] Using local monorepo: {monorepoDir}");
            if (!File.Exists(Path.Combine(monorepoDir, ".gitmodules")) && !Directory.Exists(Path.Combine(monorepoDir, "V12-Engine")))
                ConOut.WriteLine($"[sync] Warning: {monorepoDir} doesn't look like V12-Monorepo (missing .gitmodules/V12-Engine)");
        }
        else
        {
            // Remote URL — clone/pull to Purr src cache
            var srcDir = Path.Combine(Purr.Home, "src");
            Directory.CreateDirectory(srcDir);
            monorepoDir = Path.Combine(srcDir, "V12-Monorepo");
            // If PURR_HOME cache already has it, pull; else clone
            if (Directory.Exists(Path.Combine(monorepoDir, ".git")))
            {
                ConOut.WriteLine($"[sync] Updating cached monorepo at {monorepoDir} (ref: {refOpt})...");
                var pull = PwshUtil.RunProcess("git", $"fetch origin", monorepoDir);
                if (pull != 0 && !force) return pull;
                // Try to checkout ref, fall back to main/master
                PwshUtil.RunProcess("git", $"checkout {refOpt}", monorepoDir);
                PwshUtil.RunProcess("git", $"pull --ff-only", monorepoDir);
                PwshUtil.RunProcess("git", $"submodule update --init --recursive", monorepoDir);
            }
            else
            {
                ConOut.WriteLine($"[sync] Cloning {monorepoUrl} -> {monorepoDir}...");
                // Ensure parent doesn't have stale non-git dir
                if (Directory.Exists(monorepoDir)) Directory.Delete(monorepoDir, true);
                var cloneArgs = $"clone --recurse-submodules --depth 1 --branch {refOpt} \"{monorepoUrl}\" \"{monorepoDir}\"";
                var exit = PwshUtil.RunProcess("git", cloneArgs, srcDir);
                if (exit != 0)
                {
                    // Try without --branch (maybe ref is commit) or without recurse
                    ConOut.WriteLine($"[sync] Clone with --branch {refOpt} failed, trying without branch...");
                    exit = PwshUtil.RunProcess("git", $"clone --recurse-submodules \"{monorepoUrl}\" \"{monorepoDir}\"", srcDir);
                    if (exit != 0) return exit;
                    PwshUtil.RunProcess("git", $"checkout {refOpt}", monorepoDir);
                    PwshUtil.RunProcess("git", $"submodule update --init --recursive", monorepoDir);
                }
            }
        }

        // Also try to read version from monorepo's Directory.Build.props if not specified
        if (string.IsNullOrWhiteSpace(versionOpt))
        {
            var monorepoVersion = TryReadV12Version(monorepoDir!);
            if (!string.IsNullOrWhiteSpace(monorepoVersion)) version = monorepoVersion!;
        }

        // --dev stamps a unique prerelease version so NuGet never reuses a cached
        // extraction of an older build with the same version.
        if (dev) version = MakeDevVersion(version);

        var modules = DiscoverModules(monorepoDir!);
        if (modules.Count == 0)
        {
            ConOut.WriteError($"[sync] Error: no packable V12 modules found under '{monorepoDir}'.");
            return 1;
        }

        ConOut.WriteLine($"[sync] Packing {modules.Count} module(s) v{version} -> {packagesDir}");
        ConOut.WriteLine($"       {string.Join(", ", modules.Select(m => m.PackageId))}");

        var verbosity = verbose ? "minimal" : "quiet";
        var packed = 0;
        foreach (var module in modules)
        {
            ConOut.WriteLine($"[sync] Packing {module.PackageId} ({Path.GetFileName(module.ProjectPath)})...");
            var args2 = $"pack \"{module.ProjectPath}\" -c Release -o \"{packagesDir}\" -p:Version={version} -p:GeneratePackageOnBuild=false -p:DebugSymbols=true -p:DebugType=portable --nologo -v {verbosity}";
            if (noBuild) args2 += " --no-build";
            var exit = PwshUtil.RunProcess("dotnet", args2, Path.GetDirectoryName(module.ProjectPath)!);
            if (exit != 0)
            {
                ConOut.WriteError($"[sync] dotnet pack failed for {module.ProjectPath} (exit {exit})");
                if (!force) return exit;
            }
            else packed++;
        }

        // Publish the install metadata every scaffolded project imports. This
        // is what makes ~/.purr the single source of truth for the engine
        // version and package feed.
        Purr.WriteV12Props(version, packagesDir);
        ConOut.WriteLine($"[sync] SDK props: {Purr.V12PropsPath}");

        // Drop the packed versions from the NuGet global cache so the next restore
        // extracts the fresh assemblies even when the version is unchanged.
        var nugetCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");
        var suffix = $".{version}.nupkg";
        foreach (var nupkg in Directory.GetFiles(packagesDir, $"*{suffix}"))
        {
            var fileName = Path.GetFileName(nupkg);
            if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var id = fileName.Substring(0, fileName.Length - suffix.Length).ToLowerInvariant();
            var cache = Path.Combine(nugetCache, id, version);
            try
            {
                if (Directory.Exists(cache))
                {
                    Directory.Delete(cache, true);
                    ConOut.WriteLine($"[sync] Cleared NuGet cache: {id}/{version}");
                }
            }
            catch { }
        }

        // Optional mirror for existing local feeds (set V12_LEGACY_FEED).
        var legacy = Environment.GetEnvironmentVariable("V12_LEGACY_FEED");
        if (!string.IsNullOrWhiteSpace(legacy)
            && Directory.Exists(legacy)
            && packagesDir != Path.GetFullPath(legacy))
        {
            foreach (var f in Directory.GetFiles(packagesDir, "Finite.V12*.nupkg"))
            {
                try { File.Copy(f, Path.Combine(legacy, Path.GetFileName(f)), true); } catch { }
            }
            ConOut.WriteLine($"[sync] Mirrored to {legacy}");
        }

        ConOut.WriteLine($"\n[sync] Done. Packed {packed} package(s) v{version} -> {packagesDir}");
        ConOut.WriteLine($"[sync] Feed ready. Try: vcsdk build");
        if (Directory.GetFiles(packagesDir, "*.nupkg").Length == 0)
        {
            ConOut.WriteError($"[sync] Warning: no nupkgs in {packagesDir}");
            return 1;
        }
        foreach (var f in Directory.GetFiles(packagesDir, "*.nupkg").OrderByDescending(f => new FileInfo(f).LastWriteTime).Take(5))
            ConOut.WriteLine($"  {Path.GetFileName(f)}");

        return 0;
    }

    /// <summary>
    /// Builds the pack list from the known modules plus any additional
    /// root-level <c>Finite.V12.*</c> module with its own csproj. Discovery is
    /// deliberately shallow: samples, renderer hosts and nested libraries are
    /// not engine packages and must not be packed here.
    /// </summary>
    static List<(string ProjectPath, string PackageId)> DiscoverModules(string monorepoDir)
    {
        var root = Path.GetFullPath(monorepoDir);
        var modules = new List<(string ProjectPath, string PackageId)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string projectPath, string? packageId)
        {
            var fullPath = Path.GetFullPath(Path.Combine(root, projectPath));
            if (!File.Exists(fullPath) || !seen.Add(fullPath)) return;

            var id = string.IsNullOrWhiteSpace(packageId) ? ReadPackageId(fullPath) : packageId;
            if (string.IsNullOrWhiteSpace(id)) return;

            // The SDK tool has its own version and pack flow.
            if (id.Equals("Finite.V12.Sdk", StringComparison.OrdinalIgnoreCase)) return;

            modules.Add((fullPath, id!));
        }

        foreach (var (projectPath, packageId) in KnownModules)
        {
            if (!File.Exists(Path.Combine(root, projectPath)))
            {
                ConOut.WriteLine($"[sync] Skip missing {projectPath}");
                continue;
            }
            Add(projectPath, packageId);
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.')) continue;

            foreach (var project in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
            {
                var id = ReadPackageId(project);
                if (id != null && id.StartsWith("Finite.V12", StringComparison.OrdinalIgnoreCase))
                    Add(project, id);
            }
        }

        return modules;
    }

    /// <summary>Reads &lt;PackageId&gt; (falling back to &lt;AssemblyName&gt;) from a project file.</summary>
    static string? ReadPackageId(string projectPath)
    {
        try
        {
            var doc = XDocument.Load(projectPath);
            var id = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "PackageId")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(id)) return id;

            var assemblyName = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value?.Trim();
            return string.IsNullOrWhiteSpace(assemblyName)
                ? Path.GetFileNameWithoutExtension(projectPath)
                : assemblyName;
        }
        catch
        {
            return null;
        }
    }

    static string MakeDevVersion(string baseVersion)
    {
        // Strip any existing prerelease suffix so the result stays valid semver.
        var dash = baseVersion.IndexOf('-');
        var core = dash >= 0 ? baseVersion.Substring(0, dash) : baseVersion;
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        return $"{core}-dev.{stamp}";
    }

    static string? TryReadV12VersionFromMonorepo()
    {
        var local = Purr.FindMonorepo();
        return local != null ? TryReadV12Version(local) : null;
    }

    static string? TryReadV12Version(string monorepoDir)
    {
        try
        {
            var props = Path.Combine(monorepoDir, "Directory.Build.props");
            if (!File.Exists(props)) return null;
            var text = File.ReadAllText(props);
            var m = System.Text.RegularExpressions.Regex.Match(text, @"<V12Version>([^<]+)</V12Version>");
            if (m.Success) return m.Groups[1].Value.Trim();
        }
        catch { }
        return null;
    }

    static string? GetOpt(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length; i++)
            if (names.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                return args[++i];
        return null;
    }

    static void ShowHelp()
    {
        ConOut.WriteLine("""
vcsdk sync [options]

Sync Purr packages from the V12-Monorepo into ~/.purr/packages, then write
~/.purr/sdk/V12.props (engine version + feed) for scaffolded projects.
Clones/pulls the monorepo and runs dotnet pack for every V12 module: the
Contract runtime chain (Contract.Runtime, Contract.Compiler, ObjectRT.*,
ObjektRT.*), Finite.V12, Finite.V12.Basic, Finite.V12.Pak,
Finite.V12.Rendering, Finite.V12.Audio, Finite.V12.Audio.OwnAudio,
Finite.V12.Paper, Finite.V12.Editor, plus any new root-level Finite.V12.* module.

Options:
  --monorepo <url|path>  Monorepo git URL or local path (default: https://github.com/Fy-nite/V12.git, or the nearest local checkout when run inside one)
  --ref <branch>         Git ref to sync (default: main)
  --version <ver>        Package version to pack (default: V12Version from Directory.Build.props, else 1.0.0)
  --dev, -d              Append a unique -dev.<timestamp> prerelease suffix
  -o, --output <path>    Output packages dir (default: ~/.purr/packages or $PURR_HOME/packages)
  --force                Continue on error, overwrite existing
  --no-build             Pass --no-build to dotnet pack
  --verbose              Verbose pack output
  -h, --help             Show help

Env:
  V12_MONOREPO_URL       Override default monorepo URL
  V12_LEGACY_FEED        Optional folder to mirror Finite.V12*.nupkg into
  PURR_HOME              Override ~/.purr location

Examples:
  vcsdk sync
  vcsdk sync --dev
  vcsdk sync --monorepo ..\V12-Monorepo --version 1.0.1
  vcsdk sync --ref main --verbose
  PURR_HOME=/path/to/purr vcsdk sync
""");
    }
}
