using System.Diagnostics;
using System.Xml.Linq;

namespace V12.Sdk;

/// <summary>
/// Copies an installed renderer next to a packed pak, renaming the executable
/// to the pak's name so the host picks up the sibling .v12pak automatically.
/// The renderer build is selected by runtime identifier, so a Windows host is
/// never copied for a Linux/macOS build (or vice versa).
/// Single-file hosts copy one file; folder-based hosts (Godot/Nova) copy their
/// whole per-RID distribution. macOS <c>.app</c> bundles get bundle-aware
/// deployment: the bundle is renamed to the game, CFBundleExecutable and any
/// external .pck are updated, and the pak is placed in Contents/MacOS where
/// Nova's executable-sibling lookup finds it.
/// </summary>
static class RendererDeployment
{
    public static void CopyToOutput(string pakPath, string? rendererId, string? runtimeIdentifier = null)
    {
        if (string.Equals(rendererId, "none", StringComparison.OrdinalIgnoreCase)) return;

        var rid = string.IsNullOrWhiteSpace(runtimeIdentifier)
            ? Purr.HostRuntimeIdentifier
            : runtimeIdentifier.Trim();

        var renderer = Purr.FindRenderer(rendererId, rid);
        if (renderer == null)
        {
            ConOut.WriteError($"[V12.Sdk] Warning: {MissingRendererMessage(rendererId, rid)} Skipping renderer copy.");
            return;
        }

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(pakPath))!;

        // macOS exports are bundles, not a single executable. They need their
        // own path: copying/renaming the inner binary alone breaks Info.plist,
        // the .pck lookup and executable-sibling pak discovery.
        var bundle = ResolveMacBundle(renderer);
        if (bundle != null)
        {
            DeployMacBundle(pakPath, renderer, bundle, outputDir);
            return;
        }

        var sourceExe = Path.Combine(renderer.Directory, renderer.Executable);
        if (!File.Exists(sourceExe))
        {
            ConOut.WriteError(
                $"[V12.Sdk] Warning: renderer '{renderer.Id}' ({renderer.DisplayRid}) executable not found: {sourceExe}");
            return;
        }

        var outputName = Path.GetFileNameWithoutExtension(pakPath) + HostExecutableSuffix(renderer.Executable);
        var destination = Path.Combine(outputDir, outputName);

        if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(pakPath), StringComparison.OrdinalIgnoreCase))
        {
            ConOut.WriteError(
                "[V12.Sdk] Warning: the renderer host would overwrite the pak (the output has no .v12pak extension). " +
                "Use -o <name>.v12pak or a directory.");
            return;
        }

        try
        {
            // Single-file hosts (MonoGame) are just the executable. Folder-based
            // hosts (the Godot/Nova export) need their data_* and native DLLs
            // alongside the renamed executable.
            if (!renderer.SingleFile)
                CopyDistribution(renderer.Directory, outputDir, sourceExe);

            if (!string.Equals(Path.GetFullPath(sourceExe), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                File.Copy(sourceExe, destination, true);

            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(destination, File.GetUnixFileMode(sourceExe)); }
                catch { }
            }

            ConOut.WriteLine($"[V12.Sdk] Renderer: {renderer.Id} ({renderer.DisplayRid}) → {Path.GetFileName(destination)}");
        }
        catch (Exception ex)
        {
            ConOut.WriteError($"[V12.Sdk] Warning: could not copy renderer host: {ex.Message}");
        }
    }

    // ── macOS app bundles ────────────────────────────────────────────────

    /// <summary>
    /// Deploys a <c>.app</c> bundle as <c>&lt;game&gt;.app</c>, renames the
    /// inner executable and keeps Info.plist in sync, renames an external
    /// <c>.pck</c> when the export did not embed it, and copies the pak into
    /// <c>Contents/MacOS</c> beside the executable.
    /// </summary>
    static void DeployMacBundle(string pakPath, RendererConfig renderer, string sourceBundle, string outputDir)
    {
        var gameName = Path.GetFileNameWithoutExtension(pakPath);
        var destinationBundle = Path.Combine(outputDir, gameName + ".app");
        var sameBundle = PathsEqual(sourceBundle, destinationBundle);

        if (!sameBundle && IsUnder(destinationBundle, sourceBundle))
        {
            ConOut.WriteError(
                $"[V12.Sdk] Warning: refusing to copy the renderer bundle into itself: {destinationBundle}");
            return;
        }

        try
        {
            if (!sameBundle)
            {
                if (Directory.Exists(destinationBundle))
                    Directory.Delete(destinationBundle, true);

                CopyDirectory(sourceBundle, destinationBundle);
            }

            var contents = Path.Combine(destinationBundle, "Contents");
            var macOs = Path.Combine(contents, "MacOS");
            var infoPlist = Path.Combine(contents, "Info.plist");

            var oldExeName = ReadPlistString(infoPlist, "CFBundleExecutable");
            if (string.IsNullOrWhiteSpace(oldExeName) || !File.Exists(Path.Combine(macOs, oldExeName)))
                oldExeName = FindMacOsExecutable(macOs) ?? oldExeName ?? "";

            var plistUpdated = TrySetPlistString(infoPlist, "CFBundleExecutable", gameName);
            if (plistUpdated)
            {
                // Cosmetic, but keeps the menu bar/About panel in step with the
                // renamed bundle. Only updates keys the export already wrote.
                TryUpdateExistingPlistStrings(infoPlist,
                    ("CFBundleName", gameName),
                    ("CFBundleDisplayName", gameName));
            }

            var hostName = oldExeName;

            if (plistUpdated
                && !string.IsNullOrWhiteSpace(gameName)
                && !string.Equals(oldExeName, gameName, StringComparison.OrdinalIgnoreCase))
            {
                var sourceExe = Path.Combine(macOs, oldExeName);
                var destinationExe = Path.Combine(macOs, gameName);

                if (File.Exists(sourceExe))
                {
                    try
                    {
                        if (File.Exists(destinationExe)) File.Delete(destinationExe);
                        File.Move(sourceExe, destinationExe);
                    }
                    catch
                    {
                        // Keep Info.plist consistent with the file that still exists.
                        TrySetPlistString(infoPlist, "CFBundleExecutable", oldExeName);
                        throw;
                    }

                    RenamePck(destinationBundle, oldExeName, gameName);
                    hostName = gameName;
                }
            }
            else if (!plistUpdated)
            {
                ConOut.WriteError(
                    "[V12.Sdk] Warning: could not update Info.plist; keeping the bundle's inner " +
                    $"executable name '{hostName}'. The pak will be named after it.");
            }

            if (string.IsNullOrWhiteSpace(hostName))
                hostName = gameName;

            var innerPak = Path.Combine(macOs, hostName + ".v12pak");
            File.Copy(pakPath, innerPak, true);

            // Editing the bundle invalidates any existing signature. Re-sign
            // ad-hoc so the app still launches locally; distributors should
            // re-sign with their own identity afterwards.
            TryAdhocSign(destinationBundle);

            ConOut.WriteLine(
                $"[V12.Sdk] Renderer: {renderer.Id} ({renderer.DisplayRid}) → " +
                $"{Path.GetFileName(destinationBundle)}/Contents/MacOS/{hostName} " +
                $"(pak: {Path.GetFileName(innerPak)})");
        }
        catch (Exception ex)
        {
            ConOut.WriteError($"[V12.Sdk] Warning: could not deploy macOS bundle: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds the .app bundle for a renderer. Supports the three common install
    /// shapes: the install directory is the bundle, the install directory
    /// contains one bundle, or renderer.json points inside the bundle.
    /// </summary>
    static string? ResolveMacBundle(RendererConfig renderer)
    {
        var directory = renderer.Directory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        if (directory.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(Path.Combine(directory, "Contents")))
            return directory;

        var executable = renderer.Executable.Replace('\\', '/');
        var marker = executable.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            var bundleRelative = executable[..(marker + 4)].Replace('/', Path.DirectorySeparatorChar);
            var candidate = Path.Combine(directory, bundleRelative);
            if (Directory.Exists(Path.Combine(candidate, "Contents")))
                return candidate;
        }

        foreach (var candidate in Directory.EnumerateDirectories(directory, "*.app", SearchOption.TopDirectoryOnly))
        {
            if (Directory.Exists(Path.Combine(candidate, "Contents")))
                return candidate;
        }

        return null;
    }

    static string? FindMacOsExecutable(string macOsDirectory)
    {
        if (!Directory.Exists(macOsDirectory)) return null;

        var files = Directory.GetFiles(macOsDirectory);
        return files
                   .Select(Path.GetFileName)
                   .FirstOrDefault(name => string.IsNullOrEmpty(Path.GetExtension(name)))
               ?? files.Select(Path.GetFileName).FirstOrDefault();
    }

    static string? ReadPlistString(string plistPath, string key)
    {
        if (!File.Exists(plistPath)) return null;

        try
        {
            var dict = XDocument.Load(plistPath).Root?.Element("dict");
            if (dict == null) return null;

            string? currentKey = null;
            foreach (var child in dict.Elements())
            {
                if (child.Name.LocalName == "key")
                {
                    currentKey = child.Value;
                    continue;
                }

                if (string.Equals(currentKey, key, StringComparison.Ordinal))
                    return child.Value;

                currentKey = null;
            }
        }
        catch
        {
            // Binary or malformed plist; the caller falls back to the MacOS dir.
        }

        return null;
    }

    /// <summary>
    /// Sets a string in an XML .app plist. Returns false when the plist is
    /// binary/unreadable, so the caller can avoid renaming the executable.
    /// </summary>
    static bool TrySetPlistString(string plistPath, string key, string value)
    {
        if (!File.Exists(plistPath)) return true;

        try
        {
            var document = XDocument.Load(plistPath);
            var dict = document.Root?.Element("dict");
            if (dict == null) return false;

            var keyElement = dict.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "key"
                                     && string.Equals(e.Value, key, StringComparison.Ordinal));

            if (keyElement == null)
            {
                dict.Add(new XElement("key", key), new XElement("string", value));
            }
            else
            {
                var valueElement = keyElement.ElementsAfterSelf().FirstOrDefault();
                if (valueElement != null && valueElement.Name.LocalName != "key")
                    valueElement.Value = value;
                else
                    keyElement.AddAfterSelf(new XElement("string", value));
            }

            document.Save(plistPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Updates existing top-level plist string keys, leaving absent keys alone.</summary>
    static void TryUpdateExistingPlistStrings(string plistPath, params (string Key, string Value)[] values)
    {
        if (!File.Exists(plistPath)) return;

        try
        {
            var document = XDocument.Load(plistPath);
            var dict = document.Root?.Element("dict");
            if (dict == null) return;

            var changed = false;
            string? currentKey = null;
            foreach (var child in dict.Elements())
            {
                if (child.Name.LocalName == "key")
                {
                    currentKey = child.Value;
                    continue;
                }

                if (currentKey != null)
                {
                    foreach (var (key, value) in values)
                    {
                        if (!string.Equals(currentKey, key, StringComparison.Ordinal)) continue;
                        child.Value = value;
                        changed = true;
                    }
                }

                currentKey = null;
            }

            if (changed) document.Save(plistPath);
        }
        catch
        {
            // Cosmetic update only; the executable/plist pairing is what matters.
        }
    }

    static void RenamePck(string bundle, string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName)
            || string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var directory in new[]
                 {
                     Path.Combine(bundle, "Contents", "Resources"),
                     Path.Combine(bundle, "Contents", "MacOS"),
                     bundle,
                 })
        {
            var oldPck = Path.Combine(directory, oldName + ".pck");
            if (!File.Exists(oldPck)) continue;

            var newPck = Path.Combine(directory, newName + ".pck");
            if (File.Exists(newPck) && !PathsEqual(oldPck, newPck))
                File.Delete(newPck);

            File.Move(oldPck, newPck);
        }
    }

    /// <summary>Best-effort ad-hoc signing for macOS after modifying a bundle.</summary>
    static void TryAdhocSign(string bundlePath)
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            var startInfo = new ProcessStartInfo("codesign")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("--force");
            startInfo.ArgumentList.Add("--deep");
            startInfo.ArgumentList.Add("--sign");
            startInfo.ArgumentList.Add("-");
            startInfo.ArgumentList.Add(bundlePath);

            using var process = Process.Start(startInfo);
            if (process == null) return;

            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
                ConOut.WriteLine("[V12.Sdk] Re-signed bundle ad-hoc (codesign --sign -).");
            else
                ConOut.WriteError($"[V12.Sdk] Warning: codesign failed ({process.ExitCode}): {error.Trim()}");
        }
        catch (Exception ex)
        {
            ConOut.WriteError($"[V12.Sdk] Warning: could not re-sign the bundle: {ex.Message}");
        }
    }

    static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    static bool PathsEqual(string first, string second)
    {
        var comparison = PathComparison;
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), comparison);
    }

    static bool IsUnder(string candidate, string parent)
    {
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(root, PathComparison);
    }

    static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    // ── common helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the host's real executable suffix. Only recognised suffixes are
    /// kept: an assembly-style name like <c>Pulsar.GL</c> must not be treated
    /// as having a <c>.GL</c> extension, or Linux/macOS paks get named
    /// <c>MyGame.GL</c>.
    /// </summary>
    static string HostExecutableSuffix(string executable)
    {
        foreach (var suffix in new[] { ".exe", ".cmd", ".bat" })
        {
            if (executable.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return executable[^suffix.Length..];
        }
        return "";
    }

    static string MissingRendererMessage(string? rendererId, string rid)
    {
        if (string.IsNullOrWhiteSpace(rendererId))
        {
            var available = Purr.LoadRenderers()
                .Select(Purr.DescribeRenderer)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var suffix = available.Length == 0 ? "" : $" Available: {string.Join(", ", available)}.";
            return $"no renderer is installed for '{rid}' in '{Purr.RenderersDir}'.{suffix}";
        }

        var variants = Purr.LoadRenderers()
            .Where(c => string.Equals(c.Id, rendererId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c.InstallId, rendererId, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.DisplayRid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (variants.Length == 0)
            return $"renderer '{rendererId}' was not found in '{Purr.RenderersDir}'.";

        var hint = string.Equals(variants[0], "any", StringComparison.OrdinalIgnoreCase)
            ? ""
            : $" Pass --runtime {variants[0]} to build for an installed renderer, or install the '{rid}' build.";
        return $"renderer '{rendererId}' has no '{rid}' build (installed: {string.Join(", ", variants)}).{hint}";
    }

    /// <summary>
    /// Mirrors a folder-based renderer distribution next to the pak, skipping
    /// the source executable (copied separately under the pak's name) and the
    /// renderer metadata file.
    /// </summary>
    static void CopyDistribution(string sourceDir, string destinationDir, string sourceExe)
    {
        var sourceExeFull = Path.GetFullPath(sourceExe);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            if (string.Equals(relative, "renderer.json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(Path.GetFullPath(file), sourceExeFull, StringComparison.OrdinalIgnoreCase))
                continue;

            var target = Path.Combine(destinationDir, relative);
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}
