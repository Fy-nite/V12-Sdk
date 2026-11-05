namespace V12.Sdk;

/// <summary>
/// Copies an installed renderer next to a packed pak, renaming the executable
/// to the pak's name so the host picks up the sibling .v12pak automatically.
/// Single-file hosts copy one file; folder-based hosts (Godot/Nova) copy their
/// whole distribution (data_* folder and native DLLs).
/// </summary>
static class RendererDeployment
{
    public static void CopyToOutput(string pakPath, string? rendererId)
    {
        if (string.Equals(rendererId, "none", StringComparison.OrdinalIgnoreCase)) return;

        var renderer = Purr.FindRenderer(rendererId);
        if (renderer == null)
        {
            var message = string.IsNullOrWhiteSpace(rendererId)
                ? $"No renderer installed in '{Purr.RenderersDir}'. Run tools/build-renderers.ps1 to install one."
                : $"Renderer '{rendererId}' was not found in '{Purr.RenderersDir}'.";
            ConOut.WriteError($"[V12.Sdk] Warning: {message} Skipping renderer copy.");
            return;
        }

        var sourceExe = Path.Combine(renderer.Directory, renderer.Executable);
        if (!File.Exists(sourceExe))
        {
            ConOut.WriteError($"[V12.Sdk] Warning: renderer '{renderer.Id}' executable not found: {sourceExe}");
            return;
        }

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(pakPath))!;
        var outputName = Path.GetFileNameWithoutExtension(pakPath) + Path.GetExtension(renderer.Executable);
        var destination = Path.Combine(outputDir, outputName);

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

            ConOut.WriteLine($"[V12.Sdk] Renderer: {renderer.Id} → {Path.GetFileName(destination)}");
        }
        catch (Exception ex)
        {
            ConOut.WriteError($"[V12.Sdk] Warning: could not copy renderer host: {ex.Message}");
        }
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
