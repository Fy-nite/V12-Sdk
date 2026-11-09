using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace V12.Sdk.Commands;

/// <summary>
/// Generates components.schema.xml, the ref-only component metadata consumed by
/// <c>vcsdk links</c>. Reflection is metadata-only (<see cref="MetadataLoadContext"/>),
/// so loading a game assembly never executes module initializers or game code.
/// </summary>
static class SchemaCommand
{
    public static int Handle(string[] args)
    {
        if (args.Contains("-h") || args.Contains("--help"))
        {
            ShowHelp();
            return 0;
        }

        var output = GetOpt(args, "--output", "-o");
        var check = args.Contains("--check");
        var quiet = args.Contains("--quiet");
        var assemblies = GetOpts(args, "--assemblies", "-a");
        var path = ArgParser.GetPositional(args, 0) ?? ".";
        path = Path.GetFullPath(path);

        if (!Directory.Exists(path) && !File.Exists(path))
        {
            ConOut.WriteError($"Error: path '{path}' not found.");
            return 1;
        }

        var root = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        var outputPath = string.IsNullOrWhiteSpace(output)
            ? Path.Combine(root, "components.schema.xml")
            : (Path.IsPathRooted(output) ? output : Path.GetFullPath(output));

        var model = BuildModel(path, assemblies, out var loadWarnings);
        foreach (var warning in loadWarnings)
            ConOut.WriteLine($"[V12.Sdk] Warning: {warning}");

        if (!model.FoundComponents)
        {
            ConOut.WriteError(
                "[V12.Sdk] Error: no IComponent types found. Pass --assemblies <dir> with " +
                "the engine and game output, or build the project first.");
            return 1;
        }

        if (model.Components.Count == 0 && !quiet)
            ConOut.WriteLine("[V12.Sdk] No reference properties were found; writing an empty schema.");

        var document = BuildDocument(model);
        if (check)
        {
            if (!File.Exists(outputPath))
            {
                ConOut.WriteError($"Error: schema '{outputPath}' does not exist.");
                return 1;
            }

            try
            {
                var existing = XDocument.Load(outputPath);
                if (XNode.DeepEquals(existing.Root, document.Root))
                {
                    ConOut.WriteLine($"[V12.Sdk] Schema is up to date ({model.Components.Count} components, {model.RefCount} refs).");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                ConOut.WriteError($"Error: could not read '{outputPath}': {ex.Message}");
                return 1;
            }

            ConOut.WriteError(
                $"[V12.Sdk] Error: schema is stale. Run 'vcsdk schema {path}' to regenerate '{outputPath}'.");
            return 1;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            document.Save(outputPath);
        }
        catch (Exception ex)
        {
            ConOut.WriteError($"Error: could not write '{outputPath}': {ex.Message}");
            return 1;
        }

        if (!quiet)
        {
            ConOut.WriteLine(
                $"[V12.Sdk] Wrote {model.Components.Count} component(s), {model.RefCount} reference(s) " +
                $"to '{outputPath}'.");
        }
        return 0;
    }

    // ── Model ────────────────────────────────────────────────────────────

    private sealed class SchemaModel
    {
        public string EngineVersion = "";
        public bool FoundComponents;
        public List<ComponentModel> Components = new();
        public int RefCount => Components.Sum(c => c.Refs.Count);
    }

    private sealed class ComponentModel
    {
        public string Type = "";
        public string FullName = "";
        public string Assembly = "";
        public string? Category;
        public string? DisplayName;
        public List<RefModel> Refs = new();
    }

    private sealed class RefModel
    {
        public string Property = "";
        public string Target = "element";
        public List<string> Accepts = new();
        public string? AcceptsInterface;
        public bool Required = true;
        public string? Description;
    }

    private static SchemaModel BuildModel(string path, List<string> explicitAssemblies, out List<string> warnings)
    {
        warnings = new List<string>();
        var dirs = new List<string>();

        void AddDir(string? dir)
        {
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                dirs.Add(Path.GetFullPath(dir));
        }

        void AddAny(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (Directory.Exists(value)) AddDir(value);
            else if (File.Exists(value)) dirs.Add(Path.GetDirectoryName(Path.GetFullPath(value))!);
        }

        // Runtime assemblies first (System.*), then explicit/project output,
        // then the SDK's own directory (V12.dll / V12.Basic.dll).
        AddDir(RuntimeEnvironment.GetRuntimeDirectory());
        if (explicitAssemblies.Count > 0)
        {
            foreach (var value in explicitAssemblies) AddAny(value);
        }
        else
        {
            AddAny(path);
            foreach (var sub in new[]
                     {
                         "obj/vcsdk-pack/publish",
                         "bin/Release/net10.0",
                         "bin/Debug/net10.0",
                         "bin/Release",
                         "bin/Debug",
                         "paks",
                     })
                AddDir(Path.Combine(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!, sub));
        }
        AddDir(AppContext.BaseDirectory);

        // One path per file name; earlier directories win.
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
            {
                var name = Path.GetFileName(dll);
                if (!byName.ContainsKey(name)) byName[name] = dll;
            }
        }

        if (byName.Count == 0)
        {
            warnings.Add("No assemblies found. Pass --assemblies <dir>.");
            return new SchemaModel();
        }

        var resolver = new PathAssemblyResolver(byName.Values);
        using var context = new MetadataLoadContext(resolver, "System.Private.CoreLib");

        var loaded = new List<Assembly>();
        foreach (var file in byName.Values)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // Framework/runtime assemblies cannot add components and are a lot
            // of metadata to walk, so skip the obvious noise.
            if (name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
                || name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
                || name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase))
                continue;

            // Native shims in the runtime directory are not managed assemblies.
            try
            {
                using var stream = File.OpenRead(file);
                using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
                if (!pe.HasMetadata) continue;
            }
            catch { continue; }

            try { loaded.Add(context.LoadFromAssemblyPath(file)); }
            catch (Exception ex) { warnings.Add($"Could not load '{Path.GetFileName(file)}': {ex.Message}"); }
        }

        if (Environment.GetEnvironmentVariable("V12_SDK_DEBUG") == "1")
        {
            foreach (var assembly in loaded)
                ConOut.WriteLine($"[schema] loaded {assembly.GetName().Name} " +
                    $"({SafeGetTypes(assembly).Count()} types)");
            foreach (var candidate in loaded.SelectMany(SafeGetTypes).Where(t => t.Name == "IComponent"))
                ConOut.WriteLine($"[schema] IComponent candidate: {candidate.FullName} in {candidate.Assembly.GetName().Name}");
        }

        var iComponent = loaded
            .SelectMany(SafeGetTypes)
            .FirstOrDefault(t => t.FullName == "V12.Core.Core.Interfaces.IComponent");

        if (iComponent == null)
        {
            warnings.Add("IComponent was not found. Is V12.dll next to the tool or in --assemblies?");
            return new SchemaModel();
        }

        var allTypes = loaded.SelectMany(SafeGetTypes)
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .ToList();

        var componentTypes = allTypes
            .Where(t => Implements(t, iComponent))
            .ToList();

        var model = new SchemaModel
        {
            FoundComponents = componentTypes.Count > 0,
            EngineVersion = loaded.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, "V12", StringComparison.OrdinalIgnoreCase))
                ?.GetName().Version?.ToString() ?? "",
        };

        foreach (var type in componentTypes)
        {
            var component = new ComponentModel
            {
                Type = type.Name,
                FullName = type.FullName ?? type.Name,
                Assembly = type.Assembly.GetName().Name ?? "",
            };

            var entry = FindAttribute(type, "V12.Components.ComponentEntryAttribute");
            if (entry != null)
            {
                component.Category = entry.ConstructorArguments.ElementAtOrDefault(0).Value as string;
                component.DisplayName = entry.ConstructorArguments.ElementAtOrDefault(1).Value as string;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var refKind = RefKind(property.PropertyType);
                if (refKind == null) continue;

                var reference = new RefModel
                {
                    Property = property.Name,
                    Target = refKind,
                };

                var attribute = FindAttribute(property, "V12.Core.References.ReferenceTargetAttribute");
                Type? acceptsComponent = null;
                Type? acceptsInterface = null;

                if (attribute != null)
                {
                    foreach (var arg in attribute.NamedArguments)
                    {
                        switch (arg.MemberName)
                        {
                            case "Kind":
                                reference.Target = Convert.ToInt32(arg.TypedValue.Value) == 1
                                    ? "component" : "element";
                                break;
                            case "AcceptsComponent":
                                acceptsComponent = arg.TypedValue.Value as Type;
                                break;
                            case "AcceptsInterface":
                                acceptsInterface = arg.TypedValue.Value as Type;
                                break;
                            case "Required":
                                reference.Required = arg.TypedValue.Value is bool required && required;
                                break;
                            case "Description":
                                reference.Description = arg.TypedValue.Value as string;
                                break;
                        }
                    }
                }

                if (acceptsComponent != null)
                    reference.Accepts.Add(acceptsComponent.Name);

                if (acceptsInterface != null)
                {
                    reference.AcceptsInterface = acceptsInterface.Name;
                    foreach (var candidate in componentTypes)
                    {
                        if (candidate.GetInterfaces().Any(i =>
                                string.Equals(i.FullName, acceptsInterface.FullName, StringComparison.Ordinal)))
                            reference.Accepts.Add(candidate.Name);
                    }
                }

                reference.Accepts = reference.Accepts
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                component.Refs.Add(reference);
            }

            if (component.Refs.Count > 0)
                model.Components.Add(component);
        }

        model.Components = model.Components
            .OrderBy(c => c.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return model;
    }

    private static XDocument BuildDocument(SchemaModel model)
    {
        return new XDocument(
            new XElement("ComponentSchema",
                new XAttribute("version", "1"),
                new XAttribute("engine", model.EngineVersion),
                new XElement("Components",
                    model.Components.Select(component =>
                        new XElement("Component",
                            new XAttribute("type", component.Type),
                            new XAttribute("fullName", component.FullName),
                            new XAttribute("assembly", component.Assembly),
                            Optional("category", component.Category),
                            Optional("displayName", component.DisplayName),
                            component.Refs.Select(reference =>
                                new XElement("Ref",
                                    new XAttribute("property", reference.Property),
                                    new XAttribute("target", reference.Target),
                                    Optional("accepts", reference.Accepts.Count == 0
                                        ? null : string.Join(",", reference.Accepts)),
                                    Optional("acceptsInterface", reference.AcceptsInterface),
                                    new XAttribute("required", reference.Required ? "true" : "false"),
                                    Optional("description", reference.Description))))))));
    }

    private static XAttribute? Optional(string name, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value!);

    private static bool Implements(Type type, Type interfaceType)
        => type.GetInterfaces().Any(i =>
            string.Equals(i.FullName, interfaceType.FullName, StringComparison.Ordinal));

    private static string? RefKind(Type type)
        => type.FullName switch
        {
            "V12.Core.References.ElementRef" => "element",
            "V12.Core.References.ComponentRef" => "component",
            _ => null,
        };

    private static CustomAttributeData? FindAttribute(MemberInfo member, string fullName)
    {
        try
        {
            return member.GetCustomAttributesData()
                .FirstOrDefault(a => string.Equals(a.AttributeType.FullName, fullName, StringComparison.Ordinal));
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
        catch { return Array.Empty<Type>(); }
    }

    // ── CLI ──────────────────────────────────────────────────────────────

    private static string? GetOpt(string[] args, params string[] names)
        => ArgParser.GetOption(args, names);

    private static List<string> GetOpts(string[] args, params string[] names)
        => ArgParser.GetOptions(args, names);

    private static void ShowHelp()
    {
        ConOut.WriteLine("""
vcsdk schema [path] [options]

Generates components.schema.xml, the ref-only component metadata consumed by
`vcsdk links` for CI validation of hand-authored WorldML. The schema lists every
IComponent type that exposes an ElementRef / ComponentRef property, the target
kind, and the component types that property accepts.

Reflection is metadata-only: game assemblies are inspected but never executed.

Arguments:
  [path]                    Project directory or build output to inspect (default: .)

Options:
  -a, --assemblies <dir>    Assembly directory or comma-separated list. Repeatable.
                            Default: project output (obj/vcsdk-pack/publish, bin/, paks/)
  -o, --output <path>       Schema path (default: <project>/components.schema.xml)
      --check               Fail if the checked-in schema differs from generated output
  -h, --help                Show help

Examples:
  vcsdk schema ./MyGame
  vcsdk schema ./MyGame --assemblies ./MyGame/obj/vcsdk-pack/publish
  vcsdk schema ./MyGame --check
""");
    }
}
