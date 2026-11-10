using System.Text;

namespace V12.Sdk.Commands;

/// <summary>
/// Copies a template tree into a new project, replacing placeholders in file
/// contents and in relative paths. Templates live next to the tool, in the
/// source checkout, or in <c>~/.purr/templates</c> (which wins, so a user can
/// override a bundled template).
/// </summary>
static class TemplateScaffolder
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".config", ".worldml",
        ".xml", ".json", ".md", ".godot", ".tscn", ".cfg", ".manifest", ".txt",
    };

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".godot", "node_modules",
    };

    public static string Find(string name)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(Purr.TemplatesDir, name),
            Path.Combine(baseDir, "templates", name),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "templates", name)),
        };

        foreach (var candidate in candidates)
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);

        return Path.GetFullPath(candidates[1]);
    }

    public static void Copy(string templateDir, string destinationDir, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var source in Directory.GetFiles(templateDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(templateDir, source);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => IgnoredDirectories.Contains(part))) continue;

            foreach (var replacement in replacements)
                relative = relative.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);

            var target = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (TextExtensions.Contains(Path.GetExtension(source)))
            {
                var text = File.ReadAllText(source);
                foreach (var replacement in replacements)
                    text = text.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(target, text, new UTF8Encoding(false));
            }
            else
            {
                File.Copy(source, target, true);
            }
        }
    }

    public static string ToIdentifier(string name)
    {
        var builder = new StringBuilder(name.Length);
        bool capitalize = true;

        foreach (char ch in name)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                capitalize = true;
                continue;
            }

            builder.Append(capitalize ? char.ToUpperInvariant(ch) : ch);
            capitalize = false;
        }

        if (builder.Length == 0) builder.Append("Project");
        if (char.IsDigit(builder[0])) builder.Insert(0, '_');
        return builder.ToString();
    }

    public static void WriteReadme(string path, string content)
        => File.WriteAllText(path, content, new UTF8Encoding(false));

    public static bool TryGetOption(string[] args, out string? value, params string[] names)
    {
        value = ArgParser.GetOption(args, names);
        return !string.IsNullOrWhiteSpace(value);
    }
}
