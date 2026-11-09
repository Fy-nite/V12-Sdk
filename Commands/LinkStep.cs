namespace V12.Sdk.Commands;

/// <summary>
/// Shared pre-pack link pass. Generates a temporary ref schema from the built
/// assemblies and runs <c>vcsdk links</c> against the content that is about to
/// be packed.
/// </summary>
internal static class LinkStep
{
    public static bool IsValidMode(string? mode)
        => string.IsNullOrWhiteSpace(mode)
           || mode.Equals("off", StringComparison.OrdinalIgnoreCase)
           || mode.Equals("check", StringComparison.OrdinalIgnoreCase)
           || mode.Equals("fix", StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs the link step. Returns false when a strict check failed.</summary>
    public static bool Run(
        string contentDir,
        string assemblyDir,
        string? manifestPath,
        string mode,
        bool strict)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode.Equals("off", StringComparison.OrdinalIgnoreCase))
            return true;

        string? schemaPath = Path.Combine(
            Path.GetTempPath(), $"v12-links-{Guid.NewGuid():N}.schema.xml");

        try
        {
            var schemaExit = SchemaCommand.Handle(new[]
            {
                assemblyDir,
                "--assemblies", assemblyDir,
                "--output", schemaPath,
                "--quiet",
            });
            if (schemaExit != 0)
            {
                ConOut.WriteLine("[V12.Sdk] Link check continuing without a schema (existence-only validation).");
                schemaPath = null;
            }

            var args = new List<string> { contentDir };
            if (schemaPath != null)
            {
                args.Add("--schema");
                args.Add(schemaPath);
            }
            if (mode.Equals("fix", StringComparison.OrdinalIgnoreCase))
                args.Add("--fix");
            if (strict)
                args.Add("--strict");
            if (!string.IsNullOrWhiteSpace(manifestPath))
            {
                args.Add("--manifest");
                args.Add(manifestPath);
            }

            var exit = LinksCommand.Handle(args.ToArray());
            return strict ? exit == 0 : true;
        }
        finally
        {
            if (schemaPath != null)
            {
                try { File.Delete(schemaPath); } catch { }
            }
        }
    }
}
