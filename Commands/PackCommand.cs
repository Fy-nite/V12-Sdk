using System.Reflection;
using System.Xml.Linq;
using V12.Pak;

namespace V12.Sdk.Commands;

/// <summary>
/// Builds a .NET gameplay project with <c>dotnet publish</c> and packs the
/// resulting assembly into a <c>.v12pak</c> as a precompiled gamepak. This is
/// the dotnet counterpart to <see cref="BuildCommand"/> (which compiles
/// Contract sources with <c>ccl</c>).
/// </summary>
static class PackCommand
{
    public static int Handle(string[] args)
    {
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            ShowHelp();
            return 0;
        }

        return Execute(args);
    }

    /// <summary>
    /// Runs the pack flow. <paramref name="pathOverride"/> lets callers that
    /// already resolved the project directory (e.g. <c>vcsdk build</c>) pass it
    /// without re-parsing the original argument list.
    /// </summary>
    internal static int Execute(string[] args, string? pathOverride = null)
    {
        var output = GetOpt(args, "--output", "-o");
        var configuration = GetOpt(args, "--configuration") ?? "Release";
        var manifestPath = GetOpt(args, "--manifest", "-m");
        var rendererId = GetOpt(args, "--renderer", "-r");
        var copyTarget = GetOpt(args, "--copy-target", "-c");
        var noRenderer = args.Contains("--no-renderer");
        var liveScripts = args.Contains("--live-scripts");
        var noRestore = args.Contains("--no-restore");
        var strict = args.Contains("--strict");

        var renderer = noRenderer ? null : Purr.FindRenderer(rendererId);
        // Publish for the host's RID so only that platform's native assets are
        // staged (a pak is loaded into one renderer build).
        var runtime = GetOpt(args, "--runtime") ?? renderer?.RuntimeIdentifier;

        var path = pathOverride ?? ParsePositional(args) ?? ".";
        path = Path.GetFullPath(path);

        var projectPath = FindProject(path);
        if (projectPath == null)
        {
            ConOut.WriteError(
                $"Error: no .csproj found in '{path}'.\n" +
                "Usage: vcsdk pack <path-to-project-or-dir> [options]");
            return 1;
        }

        var gameDir = Path.GetDirectoryName(projectPath)!;
        var assemblyName = ReadAssemblyName(projectPath);
        var projectName = string.IsNullOrWhiteSpace(assemblyName)
            ? Path.GetFileNameWithoutExtension(projectPath)
            : assemblyName;

        var outputType = ReadOutputType(projectPath);
        if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase))
        {
            ConOut.WriteError(
                "[V12.Sdk] Warning: this project builds an executable with its own entry point. " +
                "The generic renderer starts IV12Gamepack types, not Program.Main; convert the project " +
                "to a gamepak library (see `vcsdk new-dotnet`).");
        }

        string? manifestSource = null;
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            manifestSource = Path.IsPathRooted(manifestPath) ? manifestPath : Path.GetFullPath(manifestPath);
            if (!File.Exists(manifestSource))
            {
                ConOut.WriteError($"Error: manifest '{manifestSource}' not found.");
                return 1;
            }
        }
        else
        {
            manifestSource = FindFile(gameDir, "manifest.json");
        }

        V12PakManifest? sourceManifest = null;
        if (manifestSource != null)
        {
            sourceManifest = LoadManifest(manifestSource);
            if (sourceManifest == null) return 1;
        }

        var stageRoot = Path.Combine(gameDir, "obj", "vcsdk-pack");
        var stage = Path.Combine(stageRoot, "stage");
        var publish = Path.Combine(stageRoot, "publish");

        ConOut.WriteLine($"[V12.Sdk] Packing .NET project '{Path.GetFileName(projectPath)}'...");
        SafeDelete(stage);
        SafeDelete(publish);
        Directory.CreateDirectory(stageRoot);
        Directory.CreateDirectory(stage);

        if (liveScripts)
        {
            ConOut.WriteLine("[V12.Sdk] Live scripts: packing .cs sources (compiled by the renderer at load time).");
        }
        else
        {
            var runtimeLabel = string.IsNullOrWhiteSpace(runtime) ? "" : $" {runtime}";
            ConOut.WriteLine($"[V12.Sdk] dotnet publish ({configuration}{runtimeLabel})...");
            var publishArgs =
                $"publish \"{projectPath}\" -c {configuration} -o \"{publish}\" --nologo --no-self-contained" +
                (string.IsNullOrWhiteSpace(runtime) ? "" : $" -r {runtime}") +
                (noRestore ? " --no-restore" : "");
            var exit = PwshUtil.RunProcess("dotnet", publishArgs, gameDir);
            if (exit != 0)
            {
                ConOut.WriteError("Error: dotnet publish failed.");
                return 1;
            }
        }

        CopyContent(gameDir, stage, liveScripts, sourceManifest);
        if (sourceManifest != null && manifestSource != null)
            File.Copy(manifestSource, Path.Combine(stage, "manifest.json"), true);

        if (!liveScripts)
        {
            if (!StageAssemblies(projectPath, publish, stage, projectName))
                return 1;
            if (!CheckEngineVersion(publish, renderer, strict))
                return 1;
        }

        var outPak = $"{projectName}.v12pak";
        var outPath = !string.IsNullOrEmpty(output)
            ? Path.IsPathRooted(output) ? output : Path.Combine(gameDir, output)
            : Path.Join(gameDir, "bin", outPak);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        ConOut.WriteLine($"[V12.Sdk] Packing v12pak → '{outPath}'");
        try
        {
            if (sourceManifest != null)
            {
                if (liveScripts)
                {
                    sourceManifest.CSharpScripts.Clear();
                    AddCSharpScripts(stage, sourceManifest);
                }
                else
                {
                    sourceManifest.CSharpScripts.Clear();
                    AddPak(sourceManifest, projectName);
                }

                V12PakPackager.PackGame(stage, outPath, sourceManifest);
            }
            else
            {
                V12PakPackager.PackGame(stage, outPath);
            }
        }
        catch (IOException ex)
        {
            ConOut.WriteError(
                $"Error: could not write the pak - it may be open in a running renderer/game. " +
                $"Close it and retry.\n  {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            ConOut.WriteError($"Error: {ex.Message}");
            return 1;
        }

        var resolvedRenderer = noRenderer ? "none" : rendererId;
        var size = new FileInfo(outPath).Length;
        ConOut.WriteLine($"[V12.Sdk] Built {outPak} ({FormatBytes(size)})");

        if (!string.IsNullOrWhiteSpace(copyTarget))
        {
            Directory.CreateDirectory(copyTarget);
            var destination = Path.Combine(copyTarget, outPak);
            File.Copy(outPath, destination, true);
            RendererDeployment.CopyToOutput(destination, resolvedRenderer);
            ConOut.WriteLine($"[V12.Sdk] Copied to '{copyTarget}/'");
        }
        else
        {
            RendererDeployment.CopyToOutput(outPath, resolvedRenderer);
        }

        return 0;
    }

    /// <summary>Finds the project file to pack: a .csproj path, or the single/first .csproj in a directory.</summary>
    internal static string? FindProject(string path)
    {
        if (File.Exists(path))
            return path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(path) : null;

        if (!Directory.Exists(path)) return null;

        var projects = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
        if (projects.Length == 0) return null;
        if (projects.Length == 1) return projects[0];

        var preferred = projects.FirstOrDefault(p =>
            string.Equals(Path.GetFileNameWithoutExtension(p), Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), StringComparison.OrdinalIgnoreCase));
        if (preferred != null) return preferred;

        ConOut.WriteError(
            $"[V12.Sdk] Warning: multiple .csproj files in '{path}'; using '{Path.GetFileName(projects[0])}'.");
        return projects[0];
    }

    static bool StageAssemblies(string projectPath, string publish, string stage, string assemblyName)
    {
        var projectDll = Path.Combine(publish, assemblyName + ".dll");
        if (!File.Exists(projectDll))
        {
            var found = Directory.Exists(publish)
                ? string.Join(", ", Directory.GetFiles(publish, "*.dll").Select(Path.GetFileName))
                : "(publish directory missing)";
            ConOut.WriteError(
                $"Error: expected '{assemblyName}.dll' in the publish output but did not find it.\n" +
                $"  publish: {publish}\n  DLLs: {found}");
            return false;
        }

        var paksDir = Path.Combine(stage, "paks");
        var binDir = Path.Combine(stage, "bin");
        Directory.CreateDirectory(paksDir);
        Directory.CreateDirectory(binDir);

        File.Copy(projectDll, Path.Combine(paksDir, Path.GetFileName(projectDll)), true);
        CopyPdb(projectDll, paksDir);

        // First-party project references are gameplay code too: pack them in
        // paks/ so their components/gamepaks are registered. Third-party NuGet
        // dependencies go to bin/ (load only, no registration).
        var projectReferences = ReadProjectReferenceNames(projectPath);

        foreach (var dll in Directory.GetFiles(publish, "*.dll"))
        {
            var simpleName = Path.GetFileNameWithoutExtension(dll);
            if (string.Equals(simpleName, assemblyName, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsEngineAssembly(simpleName) || IsFrameworkAssembly(simpleName)) continue;

            var destination = projectReferences.Contains(simpleName) ? paksDir : binDir;
            File.Copy(dll, Path.Combine(destination, Path.GetFileName(dll)), true);
            CopyPdb(dll, destination);
        }

        var runtimes = Path.Combine(publish, "runtimes");
        if (Directory.Exists(runtimes))
            CopyDirectory(runtimes, Path.Combine(binDir, "runtimes"), _ => true);

        return true;
    }

    static void CopyContent(string gameDir, string stage, bool liveScripts, V12PakManifest? manifest)
    {
        foreach (var name in new[] { "worlds", "assets", "dependencies", "paks" })
        {
            var source = FindDirectory(gameDir, name);
            if (source != null)
                CopyDirectory(source, Path.Combine(stage, name), _ => true);
        }

        var scripts = FindDirectory(gameDir, "scripts");
        var stagedScripts = Path.Combine(stage, "scripts");

        if (liveScripts)
        {
            // Live mode compiles the whole project at load time, not just
            // scripts/: the gamepak entry point usually lives at the root.
            foreach (var file in Directory.EnumerateFiles(gameDir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildPath(gameDir, file)) continue;
                var target = Path.Combine(stagedScripts, Path.GetRelativePath(gameDir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }

            // Non-C# script payloads (.ct/.lua/...) keep their archive layout.
            if (scripts != null)
            {
                foreach (var file in Directory.EnumerateFiles(scripts, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase)) continue;
                    var target = Path.Combine(stage, Path.GetRelativePath(gameDir, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, true);
                }
            }
        }
        else if (scripts != null)
        {
            CopyDirectory(scripts, stagedScripts,
                file => !string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase));
        }

        // A project manifest may point outside the canonical folders (for
        // example Scenes/Main.worldml). Stage everything it references so the
        // builder's validation passes and the archive matches the manifest.
        if (manifest != null)
            CopyManifestContent(gameDir, stage, manifest, liveScripts);
    }

    /// <summary>
    /// Copy every path a project manifest references into the stage, preserving
    /// its archive path. Runs in addition to the canonical-folder scan because
    /// projects often keep worlds under Scenes/ and assets elsewhere.
    /// </summary>
    static void CopyManifestContent(string gameDir, string stage, V12PakManifest manifest, bool liveScripts)
    {
        void CopyFile(string? relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return;
            var source = ResolvePath(gameDir, relative);
            if (source == null || !File.Exists(source)) return;

            var target = Path.Combine(stage, relative.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
        }

        void CopyDir(string? relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return;
            var source = ResolvePath(gameDir, relative);
            if (source == null || !Directory.Exists(source)) return;

            var target = Path.Combine(stage, relative.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
            CopyDirectory(source, target, _ => true);
        }

        foreach (var world in manifest.Worlds)
        {
            CopyFile(world.Entry);
            CopyDir(world.TemplatesDir);
        }

        foreach (var asset in manifest.Assets.Values) CopyFile(asset);
        foreach (var pak in manifest.Paks) CopyFile(pak);
        foreach (var script in manifest.Scripts) CopyFile(script);
        if (liveScripts)
            foreach (var script in manifest.CSharpScripts) CopyFile(script);
        foreach (var dependency in manifest.Dependencies) CopyFile(dependency);
    }

    static V12PakManifest? LoadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return null;

        try
        {
            return V12PakBuilder.LoadManifest(manifestPath);
        }
        catch (Exception ex)
        {
            ConOut.WriteError($"Error: could not read manifest '{manifestPath}': {ex.Message}");
            return null;
        }
    }

    static void AddPak(V12PakManifest manifest, string assemblyName)
    {
        var path = $"paks/{assemblyName}.dll";
        if (!manifest.Paks.Any(p => string.Equals(p.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase)))
            manifest.Paks.Add(path);
    }

    static void AddCSharpScripts(string stage, V12PakManifest manifest)
    {
        var scriptsDir = Path.Combine(stage, "scripts");
        if (!Directory.Exists(scriptsDir)) return;

        foreach (var file in Directory.EnumerateFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(stage, file).Replace('\\', '/');
            if (!manifest.CSharpScripts.Any(s => string.Equals(s.Replace('\\', '/'), rel, StringComparison.OrdinalIgnoreCase)))
                manifest.CSharpScripts.Add(rel);
        }
    }

    static HashSet<string> ReadProjectReferenceNames(string projectPath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var doc = XDocument.Load(projectPath);
            var dir = Path.GetDirectoryName(projectPath)!;
            foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            {
                var include = (string?)element.Attribute("Include");
                if (string.IsNullOrWhiteSpace(include)) continue;

                var reference = Path.GetFullPath(Path.Combine(dir, include));
                var name = File.Exists(reference) ? ReadAssemblyName(reference) : null;
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                else names.Add(Path.GetFileNameWithoutExtension(reference));
            }
        }
        catch
        {
            // Best effort: a malformed project file will fail dotnet publish anyway.
        }
        return names;
    }

    static string? ReadOutputType(string projectPath)
    {
        try
        {
            var doc = XDocument.Load(projectPath);
            return doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "OutputType")?.Value?.Trim();
        }
        catch
        {
            return null;
        }
    }

    static string? ResolvePath(string root, string relative)
    {
        var current = root;
        foreach (var segment in relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;

            var candidate = Path.Combine(current, segment);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                current = candidate;
                continue;
            }

            if (!Directory.Exists(current)) return null;
            var match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(p => string.Equals(Path.GetFileName(p), segment, StringComparison.OrdinalIgnoreCase));
            if (match == null) return null;
            current = match;
        }

        return File.Exists(current) || Directory.Exists(current) ? current : null;
    }

    static string ReadAssemblyName(string projectPath)
    {
        try
        {
            var doc = XDocument.Load(projectPath);
            var value = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value;
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        catch
        {
            // Fall through to the file name.
        }
        return Path.GetFileNameWithoutExtension(projectPath);
    }

    static bool CheckEngineVersion(string publish, RendererConfig? renderer, bool strict)
    {
        if (renderer == null || string.IsNullOrWhiteSpace(renderer.Version)) return true;

        var engineDll = new[] { "V12.Basic.dll", "V12.dll" }
            .Select(name => Path.Combine(publish, name))
            .FirstOrDefault(File.Exists);
        if (engineDll == null) return true;

        string projectVersion;
        try { projectVersion = NormalizeVersion(AssemblyName.GetAssemblyName(engineDll).Version?.ToString()); }
        catch { return true; }

        var rendererVersion = NormalizeVersion(renderer.Version);
        if (string.Equals(projectVersion, rendererVersion, StringComparison.OrdinalIgnoreCase)) return true;

        var message =
            $"game project references V12 v{projectVersion} but renderer '{renderer.Id}' ships v{rendererVersion}. " +
            "A mismatch can break component type identity at load time.";
        if (strict)
        {
            ConOut.WriteError($"Error: {message}");
            return false;
        }
        ConOut.WriteError($"[V12.Sdk] Warning: {message}");
        return true;
    }

    static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "";
        var core = version.Split('-', '+')[0];
        if (!Version.TryParse(core, out var parsed)) return core;

        if (parsed.Build >= 0) return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
        if (parsed.Minor >= 0) return $"{parsed.Major}.{parsed.Minor}";
        return parsed.Major.ToString();
    }

    static bool IsEngineAssembly(string simpleName)
        => string.Equals(simpleName, "V12", StringComparison.OrdinalIgnoreCase)
           || simpleName.StartsWith("V12.", StringComparison.OrdinalIgnoreCase);

    static bool IsFrameworkAssembly(string simpleName)
        => string.Equals(simpleName, "netstandard", StringComparison.OrdinalIgnoreCase)
           || string.Equals(simpleName, "mscorlib", StringComparison.OrdinalIgnoreCase)
           || string.Equals(simpleName, "WindowsBase", StringComparison.OrdinalIgnoreCase)
           || simpleName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
           || string.Equals(simpleName, "System", StringComparison.OrdinalIgnoreCase)
           || simpleName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);

    static void CopyPdb(string dll, string destinationDirectory)
    {
        var pdb = Path.ChangeExtension(dll, ".pdb");
        if (File.Exists(pdb))
            File.Copy(pdb, Path.Combine(destinationDirectory, Path.GetFileName(pdb)), true);
    }

    static void CopyDirectory(string source, string destination, Func<string, bool> include)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            if (!include(file)) continue;
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    static bool IsBuildPath(string root, string file)
    {
        var parts = Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase)
                              || p.Equals("obj", StringComparison.OrdinalIgnoreCase)
                              || p.Equals(".git", StringComparison.OrdinalIgnoreCase)
                              || p.Equals(".v12", StringComparison.OrdinalIgnoreCase)
                              || p.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    static string? FindDirectory(string root, string name)
    {
        var exact = Path.Combine(root, name);
        if (Directory.Exists(exact)) return exact;

        return Directory.EnumerateDirectories(root)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
    }

    static string? FindFile(string root, string name)
    {
        var exact = Path.Combine(root, name);
        if (File.Exists(exact)) return exact;

        return Directory.EnumerateFiles(root)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
    }

    static void SafeDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch { /* best effort; publish/build will overwrite */ }
    }

    static string? GetOpt(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (names.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                return args[++i];
        }
        return null;
    }

    static string? ParsePositional(string[] args)
    {
        var valueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--output", "-o", "--configuration", "--runtime", "--manifest", "-m",
            "--renderer", "-r", "--copy-target", "-c"
        };

        for (int i = 0; i < args.Length; i++)
        {
            if (valueOptions.Contains(args[i])) { i++; continue; }
            if (args[i].StartsWith("-")) continue;
            return args[i];
        }
        return null;
    }

    static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:F1}{units[index]}";
    }

    static void ShowHelp()
    {
        ConOut.WriteLine("""
vcsdk pack [path] [options]

Builds a .NET gameplay project with `dotnet publish` and packs the resulting
assembly as a precompiled gamepak inside a .v12pak. The generic V12 renderer
loads it from paks/ (no runtime Roslyn compile).

Arguments:
  [path]                    Project file or project directory (default: .)

Options:
  -o, --output <path>       Output .v12pak path (default: bin/<name>.v12pak)
  -m, --manifest <path>     Use this manifest.json instead of auto-detecting
      --configuration <c>   dotnet build configuration (default: Release)
      --runtime <rid>       Publish for this runtime identifier (default: the
                            installed renderer's RID, else portable)
  -r, --renderer <id>       Installed renderer host to copy (default: monogame)
      --no-renderer         Do not copy a renderer host beside the pak
  -c, --copy-target <dir>   Copy the built pak (and renderer) into this directory
      --live-scripts        Pack scripts/*.cs and compile them at load time
                            instead of publishing a precompiled DLL
      --no-restore          Skip dotnet restore during publish
      --strict              Fail when the project's V12 version differs from the
                            installed renderer's
  -h, --help                Show help

Examples:
  vcsdk pack
  vcsdk pack ./MyGame -o ./dist/MyGame.v12pak
  vcsdk pack ./MyGame --live-scripts
  vcsdk pack ./MyGame -r monogame --strict
""");
    }
}
