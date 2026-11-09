using System.Xml;
using System.Xml.Linq;

namespace V12.Sdk.Links;

public enum LinkSeverity
{
    Info,
    Warning,
    Error,
}

public sealed class LinkDiagnostic
{
    public LinkSeverity Severity { get; init; } = LinkSeverity.Warning;
    public string File { get; init; } = "";
    public int Line { get; init; }
    public string ElementPath { get; init; } = "";
    public string ComponentType { get; init; } = "";
    public string Property { get; init; } = "";
    public string Selector { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class LinkCheckResult
{
    public List<LinkDiagnostic> Diagnostics { get; } = new();
    public int FilesChecked { get; set; }
    public int RefsChecked { get; set; }
    public int RefsFixed { get; set; }

    public int Errors => Diagnostics.Count(d => d.Severity == LinkSeverity.Error);
    public int Warnings => Diagnostics.Count(d => d.Severity == LinkSeverity.Warning);
}

public sealed class RefPropertySchema
{
    public string Property { get; init; } = "";
    public string Target { get; init; } = "element";
    public string[] Accepts { get; init; } = Array.Empty<string>();
    public string? AcceptsInterface { get; init; }
    public bool Required { get; init; } = true;
}

public sealed class ComponentRefSchema
{
    public string Type { get; init; } = "";
    public Dictionary<string, RefPropertySchema> Refs { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ComponentSchema
{
    public Dictionary<string, ComponentRefSchema> Components { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ComponentSchema Load(string path)
    {
        var document = XDocument.Load(path);
        var schema = new ComponentSchema();
        var root = document.Root ?? throw new InvalidDataException("Schema has no root element.");

        foreach (var componentNode in root.Descendants().Where(e => e.Name.LocalName == "Component"))
        {
            var type = (string?)componentNode.Attribute("type") ?? "";
            if (string.IsNullOrWhiteSpace(type)) continue;

            var component = new ComponentRefSchema { Type = type };
            foreach (var refNode in componentNode.Elements().Where(e => e.Name.LocalName == "Ref"))
            {
                var property = (string?)refNode.Attribute("property") ?? "";
                if (string.IsNullOrWhiteSpace(property)) continue;

                var accepts = ((string?)refNode.Attribute("accepts") ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                component.Refs[property] = new RefPropertySchema
                {
                    Property = property,
                    Target = (string?)refNode.Attribute("target") ?? "element",
                    Accepts = accepts,
                    AcceptsInterface = (string?)refNode.Attribute("acceptsInterface"),
                    Required = !string.Equals((string?)refNode.Attribute("required"), "false", StringComparison.OrdinalIgnoreCase),
                };
            }

            if (component.Refs.Count > 0)
                schema.Components[type] = component;
        }

        return schema;
    }
}

/// <summary>
/// XML-only WorldML reference checker. It never loads the game assembly; when a
/// components.schema.xml is supplied it uses the declared ref properties, and
/// otherwise falls back to attributes whose names end in "Ref".
/// </summary>
public sealed class WorldMLinkChecker
{
    private const string RefSuffix = "Ref";
    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".v12", "node_modules", "packages",
    };

    private readonly ComponentSchema? _schema;
    private readonly TemplateMap? _externalTemplates;
    private readonly bool _fix;
    private readonly bool _dryRun;

    public WorldMLinkChecker(
        ComponentSchema? schema,
        bool fix = false,
        bool dryRun = false,
        TemplateMap? externalTemplates = null)
    {
        _schema = schema;
        _fix = fix;
        _dryRun = dryRun;
        _externalTemplates = externalTemplates;
    }

    public LinkCheckResult CheckDirectory(string root)
    {
        var result = new LinkCheckResult();
        foreach (var file in Discover(root))
            CheckFile(file, result);
        return result;
    }

    public LinkCheckResult CheckFile(string path)
    {
        var result = new LinkCheckResult();
        CheckFile(path, result);
        return result;
    }

    private static IEnumerable<string> Discover(string root)
    {
        if (File.Exists(root))
        {
            if (IsWorldFile(root)) yield return Path.GetFullPath(root);
            yield break;
        }

        if (!Directory.Exists(root)) yield break;

        foreach (var file in Directory.EnumerateFiles(root, "*.worldml", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)))
        {
            if (IsInSkippedDirectory(root, file)) continue;
            if (IsWorldFile(file)) yield return Path.GetFullPath(file);
        }
    }

    private static bool IsInSkippedDirectory(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => SkipDirectories.Contains(p));
    }

    private static bool IsWorldFile(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                return string.Equals(reader.LocalName, "World", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(reader.LocalName, "Element", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Not an XML file we can check.
        }
        return false;
    }

    private void CheckFile(string path, LinkCheckResult result)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            result.Diagnostics.Add(new LinkDiagnostic
            {
                Severity = LinkSeverity.Error,
                File = path,
                Message = $"Could not parse XML: {ex.Message}",
            });
            return;
        }

        result.FilesChecked++;
        var templates = TemplateMap.FromDocument(document);
        if (_externalTemplates != null) templates.Merge(_externalTemplates);
        var index = WorldIndex.Build(document, templates);
        ReportDuplicateUids(path, index, result);
        bool changed = false;

        foreach (var element in index.Elements)
        {
            foreach (var component in element.Components)
            {
                foreach (var reference in FindRefs(component))
                {
                    result.RefsChecked++;
                    CheckReference(path, index, element, component, reference, result, ref changed);
                }
            }
        }

        if (changed && !_dryRun)
        {
            try
            {
                document.Save(path);
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new LinkDiagnostic
                {
                    Severity = LinkSeverity.Error,
                    File = path,
                    Message = $"Could not write fixes: {ex.Message}",
                });
            }
        }
    }

    private static void ReportDuplicateUids(string path, WorldIndex index, LinkCheckResult result)
    {
        var pinned = new List<(string Uid, XObject Node, string Where)>();
        foreach (var element in index.Elements)
        {
            if (!string.IsNullOrWhiteSpace(element.Uid))
                pinned.Add((element.Uid!, element.Node, index.PathOf(element)));
            foreach (var component in element.Components)
            {
                var uid = (string?)component.Node.Attribute("uid");
                if (!string.IsNullOrWhiteSpace(uid))
                    pinned.Add((uid!, component.Node, $"{index.PathOf(element)} [{component.SimpleTypeName}]"));
            }
        }

        foreach (var group in pinned.GroupBy(p => p.Uid, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            foreach (var item in group)
            {
                result.Diagnostics.Add(new LinkDiagnostic
                {
                    Severity = LinkSeverity.Error,
                    File = path,
                    Line = LineOf(item.Node),
                    ElementPath = item.Where,
                    Message = $"Duplicate uid '{group.Key}' is pinned by {group.Count()} objects.",
                });
            }
        }
    }

    private IEnumerable<RefAttribute> FindRefs(ComponentInfo component)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_schema != null && _schema.Components.TryGetValue(component.SimpleTypeName, out var schema))
        {
            foreach (var property in schema.Refs.Values)
            {
                seen.Add(property.Property);
                var attribute = component.Node.Attributes()
                    .FirstOrDefault(a => string.Equals(a.Name.LocalName, property.Property, StringComparison.OrdinalIgnoreCase));
                if (attribute == null && property.Required)
                {
                    yield return new RefAttribute
                    {
                        Name = property.Property,
                        Value = "",
                        Schema = property,
                    };
                    continue;
                }

                if (attribute == null) continue;
                yield return new RefAttribute
                {
                    Name = attribute.Name.LocalName,
                    Value = attribute.Value,
                    Attribute = attribute,
                    Schema = property,
                };
            }
            yield break;
        }

        foreach (var attribute in component.Node.Attributes())
        {
            if (!attribute.Name.LocalName.EndsWith(RefSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(attribute.Name.LocalName)) continue;

            yield return new RefAttribute
            {
                Name = attribute.Name.LocalName,
                Value = attribute.Value,
                Attribute = attribute,
                Schema = new RefPropertySchema
                {
                    Property = attribute.Name.LocalName,
                    Target = attribute.Value.Contains('#') ? "component" : "element",
                    Required = false,
                },
            };
        }
    }

    private void CheckReference(
        string path,
        WorldIndex index,
        ElementInfo element,
        ComponentInfo component,
        RefAttribute reference,
        LinkCheckResult result,
        ref bool changed)
    {
        var selector = reference.Value?.Trim() ?? "";
        var diagnosticBase = new
        {
            File = path,
            Line = LineOf((XObject?)reference.Attribute ?? component.Node),
            ElementPath = index.PathOf(element),
            ComponentType = component.TypeName,
            Property = reference.Name,
            Selector = selector,
        };

        if (string.IsNullOrWhiteSpace(selector))
        {
            if (reference.Schema.Required)
            {
                result.Diagnostics.Add(new LinkDiagnostic
                {
                    Severity = LinkSeverity.Error,
                    File = diagnosticBase.File,
                    Line = diagnosticBase.Line,
                    ElementPath = diagnosticBase.ElementPath,
                    ComponentType = diagnosticBase.ComponentType,
                    Property = diagnosticBase.Property,
                    Message = "Required reference is empty.",
                });
            }
            return;
        }

        var (elementSelector, componentSelector) = SplitComponent(selector);
        var elementMatches = index.ResolveElements(elementSelector);

        if (elementMatches.Count == 0)
        {
            result.Diagnostics.Add(new LinkDiagnostic
            {
                Severity = LinkSeverity.Error,
                File = diagnosticBase.File,
                Line = diagnosticBase.Line,
                ElementPath = diagnosticBase.ElementPath,
                ComponentType = diagnosticBase.ComponentType,
                Property = diagnosticBase.Property,
                Selector = selector,
                Message = $"Target element '{elementSelector}' was not found.",
            });
            return;
        }

        if (elementMatches.Count > 1)
        {
            result.Diagnostics.Add(new LinkDiagnostic
            {
                Severity = LinkSeverity.Error,
                File = diagnosticBase.File,
                Line = diagnosticBase.Line,
                ElementPath = diagnosticBase.ElementPath,
                ComponentType = diagnosticBase.ComponentType,
                Property = diagnosticBase.Property,
                Selector = selector,
                Message = $"Target element '{elementSelector}' is ambiguous ({elementMatches.Count} matches). Use a longer path or an index.",
            });
            return;
        }

        var targetElement = elementMatches[0];
        bool wantsComponent = string.Equals(reference.Schema.Target, "component", StringComparison.OrdinalIgnoreCase);

        if (!wantsComponent)
        {
            if (!string.IsNullOrEmpty(componentSelector))
            {
                result.Diagnostics.Add(new LinkDiagnostic
                {
                    Severity = LinkSeverity.Error,
                    File = diagnosticBase.File,
                    Line = diagnosticBase.Line,
                    ElementPath = diagnosticBase.ElementPath,
                    ComponentType = diagnosticBase.ComponentType,
                    Property = diagnosticBase.Property,
                    Selector = selector,
                    Message = $"Element reference cannot include a component qualifier ('#{componentSelector}').",
                });
                return;
            }

            if (!AcceptsElement(reference.Schema, targetElement))
            {
                result.Diagnostics.Add(new LinkDiagnostic
                {
                    Severity = LinkSeverity.Error,
                    File = diagnosticBase.File,
                    Line = diagnosticBase.Line,
                    ElementPath = diagnosticBase.ElementPath,
                    ComponentType = diagnosticBase.ComponentType,
                    Property = diagnosticBase.Property,
                    Selector = selector,
                    Message = $"Target element '{targetElement.Name}' does not have a required component ({string.Join(", ", reference.Schema.Accepts)}).",
                });
                return;
            }

            if (_fix)
                CanonicalizeElement(index, element, reference, targetElement, ref changed);
            return;
        }

        if (string.IsNullOrEmpty(componentSelector))
        {
            result.Diagnostics.Add(new LinkDiagnostic
            {
                Severity = LinkSeverity.Error,
                File = diagnosticBase.File,
                Line = diagnosticBase.Line,
                ElementPath = diagnosticBase.ElementPath,
                ComponentType = diagnosticBase.ComponentType,
                Property = diagnosticBase.Property,
                Selector = selector,
                Message = "Component reference is missing its '#Component' qualifier.",
            });
            return;
        }

        var componentMatches = index.ResolveComponents(targetElement, componentSelector);
        if (componentMatches.Count == 0)
        {
            result.Diagnostics.Add(new LinkDiagnostic
            {
                Severity = LinkSeverity.Error,
                File = diagnosticBase.File,
                Line = diagnosticBase.Line,
                ElementPath = diagnosticBase.ElementPath,
                ComponentType = diagnosticBase.ComponentType,
                Property = diagnosticBase.Property,
                Selector = selector,
                Message = $"Component '{componentSelector}' was not found on '{targetElement.Name}'.",
            });
            return;
        }

        if (componentMatches.Count > 1)
        {
            result.Diagnostics.Add(new LinkDiagnostic
            {
                Severity = LinkSeverity.Error,
                File = diagnosticBase.File,
                Line = diagnosticBase.Line,
                ElementPath = diagnosticBase.ElementPath,
                ComponentType = diagnosticBase.ComponentType,
                Property = diagnosticBase.Property,
                Selector = selector,
                Message = $"Component qualifier '{componentSelector}' is ambiguous ({componentMatches.Count} matches). Add an index.",
            });
            return;
        }

        var targetComponent = componentMatches[0];
        if (!AcceptsComponent(reference.Schema, targetComponent))
        {
            result.Diagnostics.Add(new LinkDiagnostic
            {
                Severity = LinkSeverity.Error,
                File = diagnosticBase.File,
                Line = diagnosticBase.Line,
                ElementPath = diagnosticBase.ElementPath,
                ComponentType = diagnosticBase.ComponentType,
                Property = diagnosticBase.Property,
                Selector = selector,
                Message = $"Component '{targetComponent.TypeName}' is not accepted ({string.Join(", ", reference.Schema.Accepts)}).",
            });
            return;
        }

        if (_fix)
            CanonicalizeComponent(index, targetElement, targetComponent, reference, ref changed);
    }

    private static bool AcceptsElement(RefPropertySchema schema, ElementInfo target)
        => schema.Accepts.Length == 0
           || target.Components.Any(c => schema.Accepts.Contains(c.SimpleTypeName, StringComparer.OrdinalIgnoreCase));

    private static bool AcceptsComponent(RefPropertySchema schema, ComponentInfo target)
        => schema.Accepts.Length == 0
           || schema.Accepts.Contains(target.SimpleTypeName, StringComparer.OrdinalIgnoreCase);

    private void CanonicalizeElement(WorldIndex index, ElementInfo owner, RefAttribute reference, ElementInfo target, ref bool changed)
    {
        var canonical = index.CanonicalElementSelector(target);
        if (string.Equals(canonical, reference.Value, StringComparison.Ordinal)) return;
        if (reference.Attribute == null) return;

        reference.Attribute.Value = canonical;
        changed = true;
    }

    private void CanonicalizeComponent(WorldIndex index, ElementInfo owner, ComponentInfo target, RefAttribute reference, ref bool changed)
    {
        var canonical = index.CanonicalComponentSelector(target);
        if (string.Equals(canonical, reference.Value, StringComparison.Ordinal)) return;
        if (reference.Attribute == null) return;

        reference.Attribute.Value = canonical;
        changed = true;
    }

    private static (string Element, string? Component) SplitComponent(string selector)
    {
        int index = selector.IndexOf('#');
        return index < 0
            ? (selector, null)
            : (selector[..index], selector[(index + 1)..]);
    }

    private static int LineOf(XObject? node)
    {
        if (node is IXmlLineInfo info && info.HasLineInfo()) return info.LineNumber;
        if (node is XElement element && element.Document?.Root is IXmlLineInfo rootInfo && rootInfo.HasLineInfo())
            return rootInfo.LineNumber;
        return 0;
    }

    private sealed class RefAttribute
    {
        public string Name = "";
        public string Value = "";
        public XAttribute? Attribute;
        public RefPropertySchema Schema = new();
    }
}

// ── Virtual world index ─────────────────────────────────────────────────

internal sealed class ElementInfo
{
    public string Name = "";
    public string? Uid;
    public ElementInfo? Parent;
    public XElement Node = null!;
    public List<ElementInfo> Children = new();
    public List<ComponentInfo> Components = new();
    public int SiblingIndex;
}

internal sealed class ComponentInfo
{
    public string TypeName = "";
    public string? Name;
    public XElement Node = null!;
    public int TypeIndex;

    /// <summary>Simple CLR-style name used by the schema and qualifiers.</summary>
    public string SimpleTypeName
        => TypeName.Contains('.') ? TypeName[(TypeName.LastIndexOf('.') + 1)..] : TypeName;
}

public sealed class TemplateMap
{
    private readonly Dictionary<string, XElement> _templates = new(StringComparer.OrdinalIgnoreCase);

    public static TemplateMap FromDocument(XDocument document)
    {
        var map = new TemplateMap();
        var nodes = document.Root?.DescendantsAndSelf() ?? Enumerable.Empty<XElement>();
        foreach (var node in nodes.Where(e => e.Name.LocalName == "Template"))
        {
            var name = (string?)node.Attribute("name");
            if (!string.IsNullOrWhiteSpace(name))
                map._templates[name!] = node;
        }
        return map;
    }

    public static void LoadDirectory(TemplateMap map, string directory)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var file in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                var document = XDocument.Load(file);
                map.Merge(FromDocument(document));
            }
            catch
            {
                // A malformed template file is reported by the runtime loader;
                // the link checker skips it rather than failing the scan.
            }
        }
    }

    public void Merge(TemplateMap other)
    {
        foreach (var pair in other._templates)
            _templates[pair.Key] = pair.Value;
    }

    public XElement? Get(string name)
        => _templates.TryGetValue(name, out var template) ? template : null;
}

internal sealed class WorldIndex
{
    private readonly List<ElementInfo> _roots = new();
    private readonly List<ElementInfo> _all = new();
    private readonly TemplateMap _templates;

    private WorldIndex(TemplateMap templates)
    {
        _templates = templates;
    }

    public IReadOnlyList<ElementInfo> Elements => _all;

    public static WorldIndex Build(XDocument document, TemplateMap templates)
    {
        var index = new WorldIndex(templates);
        var root = document.Root;
        if (root == null) return index;

        if (string.Equals(root.Name.LocalName, "Element", StringComparison.OrdinalIgnoreCase))
        {
            index.AddElement(root, null);
            return index;
        }

        foreach (var child in root.Elements())
        {
            if (string.Equals(child.Name.LocalName, "Element", StringComparison.OrdinalIgnoreCase))
                index.AddElement(child, null);
            else if (string.Equals(child.Name.LocalName, "Instance", StringComparison.OrdinalIgnoreCase))
                index.ExpandInstance(child, null);
        }

        return index;
    }

    private void AddElement(XElement node, ElementInfo? parent)
    {
        var info = new ElementInfo
        {
            Name = (string?)node.Attribute("name") ?? "",
            Uid = (string?)node.Attribute("uid"),
            Parent = parent,
            Node = node,
        };

        var siblings = parent?.Children ?? _roots;
        info.SiblingIndex = siblings.Count(s =>
            string.Equals(s.Name, info.Name, StringComparison.OrdinalIgnoreCase)) + 1;

        siblings.Add(info);
        _all.Add(info);

        foreach (var child in node.Elements())
        {
            var local = child.Name.LocalName;
            if (string.Equals(local, "Element", StringComparison.OrdinalIgnoreCase))
                AddElement(child, info);
            else if (string.Equals(local, "Instance", StringComparison.OrdinalIgnoreCase))
                ExpandInstance(child, info);
            else if (IsComponentNode(child))
                AddComponent(child, info);
        }

        var templateName = (string?)node.Attribute("template");
        if (!string.IsNullOrWhiteSpace(templateName))
            ExpandTemplate(templateName!, node, info, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private void ExpandInstance(XElement instance, ElementInfo? parent)
    {
        var templateName = (string?)instance.Attribute("template");
        if (!string.IsNullOrWhiteSpace(templateName))
            ExpandTemplate(templateName!, instance, parent, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private void ExpandTemplate(string templateName, XElement source, ElementInfo? parent, HashSet<string> stack)
    {
        if (!stack.Add(templateName)) return;
        var template = _templates.Get(templateName);
        if (template == null) return;

        foreach (var child in template.Elements())
        {
            var clone = new XElement(child);
            ApplyInterpolation(clone, source);
            if (string.Equals(clone.Name.LocalName, "Element", StringComparison.OrdinalIgnoreCase))
                AddElement(clone, parent);
            else if (IsComponentNode(clone) && parent != null)
                AddComponent(clone, parent);
        }

        stack.Remove(templateName);
    }

    private static void ApplyInterpolation(XElement node, XElement source)
    {
        var overrides = source.Attributes()
            .Where(a => !a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase)
                     && !a.Name.LocalName.Equals("description", StringComparison.OrdinalIgnoreCase)
                     && !a.Name.LocalName.Equals("template", StringComparison.OrdinalIgnoreCase)
                     && !a.Name.LocalName.Equals("uid", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.OrdinalIgnoreCase);

        void Walk(XElement element)
        {
            foreach (var attribute in element.Attributes())
                attribute.Value = Interpolate(attribute.Value, overrides);

            foreach (var text in element.Nodes().OfType<XText>())
                text.Value = Interpolate(text.Value, overrides);

            foreach (var child in element.Elements())
                Walk(child);
        }

        Walk(node);
    }

    private static string Interpolate(string value, Dictionary<string, string> overrides)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("${")) return value;
        return System.Text.RegularExpressions.Regex.Replace(value, @"\$\{(\w+)\}", match =>
            overrides.TryGetValue(match.Groups[1].Value, out var replacement) ? replacement : "0");
    }

    private static bool IsComponentNode(XElement node)
        => string.Equals(node.Name.LocalName, "Component", StringComparison.OrdinalIgnoreCase)
           || node.Name.LocalName.EndsWith("Component", StringComparison.OrdinalIgnoreCase);

    private static void AddComponent(XElement node, ElementInfo owner)
    {
        var typeName = (string?)node.Attribute("type")
                       ?? (string?)node.Attribute("typename")
                       ?? node.Elements().FirstOrDefault(e => e.Name.LocalName == "Type")?.Value
                       ?? node.Name.LocalName;
        if (string.IsNullOrWhiteSpace(typeName)) return;

        var info = new ComponentInfo
        {
            TypeName = typeName.Trim(),
            Name = (string?)node.Attribute("name"),
            Node = node,
        };
        info.TypeIndex = owner.Components.Count(c =>
            string.Equals(c.TypeName, info.TypeName, StringComparison.OrdinalIgnoreCase)) + 1;
        owner.Components.Add(info);
    }

    // ── Resolution ──────────────────────────────────────────────────────

    public List<ElementInfo> ResolveElements(string selector)
    {
        selector = selector?.Trim() ?? "";
        if (selector.Length == 0) return new List<ElementInfo>();

        if (selector[0] == '@')
        {
            var uid = selector[1..];
            return _all.Where(e => string.Equals(e.Uid, uid, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var segments = selector.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseSegment)
            .ToArray();
        if (segments.Length == 0) return new List<ElementInfo>();

        var (lastName, lastIndex) = segments[^1];
        var candidates = _all
            .Where(e => string.Equals(e.Name, lastName, StringComparison.OrdinalIgnoreCase))
            .Where(e => !lastIndex.HasValue || e.SiblingIndex == lastIndex.Value)
            .Where(e => MatchesSuffix(e, segments))
            .ToList();

        return candidates;
    }

    public List<ComponentInfo> ResolveComponents(ElementInfo element, string qualifier)
    {
        qualifier = qualifier.Trim();
        if (qualifier.Length == 0) return new List<ComponentInfo>();

        if (qualifier[0] == '@')
        {
            var uid = qualifier[1..];
            return element.Components
                .Where(c => string.Equals((string?)c.Node.Attribute("uid"), uid, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var (name, index) = ParseSegment(qualifier);
        var byName = element.Components
            .Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byName.Count > 0)
            return index.HasValue ? byName.Where((_, i) => i + 1 == index.Value).ToList() : byName;

        var byType = element.Components
            .Where(c => string.Equals(c.SimpleTypeName, name, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(c.TypeName, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return index.HasValue ? byType.Where((_, i) => i + 1 == index.Value).ToList() : byType;
    }

    public string CanonicalElementSelector(ElementInfo element)
    {
        // A unique name is the shortest, most readable selector.
        if (!string.IsNullOrWhiteSpace(element.Name)
            && _all.Count(e => string.Equals(e.Name, element.Name, StringComparison.OrdinalIgnoreCase)) == 1)
            return Escape(element.Name);

        // Otherwise use the shortest suffix path that still resolves uniquely.
        var parts = new List<string>();
        for (var current = element; current != null; current = current.Parent)
        {
            var part = Escape(current.Name);
            if (CountSameNameSiblings(current) > 1)
                part += $"[{current.SiblingIndex}]";
            parts.Insert(0, part);

            var candidate = string.Join('/', parts);
            if (ResolveElements(candidate).Count == 1)
                return candidate;
        }

        return string.Join('/', parts);
    }

    public string CanonicalComponentSelector(ComponentInfo component)
    {
        var element = _all.First(e => e.Components.Contains(component));
        var elementSelector = CanonicalElementSelector(element);

        if (!string.IsNullOrWhiteSpace(component.Name))
            return $"{elementSelector}#{Escape(component.Name!)}";

        var sameType = element.Components.Count(c =>
            string.Equals(c.SimpleTypeName, component.SimpleTypeName, StringComparison.OrdinalIgnoreCase));
        var suffix = sameType > 1 ? $"[{component.TypeIndex}]" : "";
        return $"{elementSelector}#{component.SimpleTypeName}{suffix}";
    }

    public string PathOf(ElementInfo element)
    {
        var parts = new List<string>();
        for (var current = element; current != null; current = current.Parent)
            parts.Insert(0, string.IsNullOrWhiteSpace(current.Name) ? "(unnamed)" : current.Name);
        return string.Join(" / ", parts);
    }

    private int CountSameNameSiblings(ElementInfo element)
    {
        var siblings = element.Parent?.Children ?? _roots;
        return siblings.Count(s => string.Equals(s.Name, element.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesSuffix(ElementInfo element, (string Name, int? Index)[] segments)
    {
        var current = element;
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (current == null) return false;
            if (!string.Equals(current.Name, segments[i].Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (segments[i].Index.HasValue && current.SiblingIndex != segments[i].Index.Value) return false;
            current = current.Parent;
        }
        return true;
    }

    private static (string Name, int? Index) ParseSegment(string segment)
    {
        if (segment.Length == 0) return ("", null);

        // Parse the index on the escaped text: a literal '[' in a name is
        // encoded as %5B, so a raw '[' can only be an index bracket.
        if (segment[^1] != ']') return (Unescape(segment), null);

        int open = segment.LastIndexOf('[');
        if (open < 0 || open == segment.Length - 2) return (Unescape(segment), null);
        if (!int.TryParse(segment[(open + 1)..^1], out var index) || index < 1)
            return (Unescape(segment), null);
        return (Unescape(segment[..open]), index);
    }

    private static string Escape(string value)
        => value.Replace("%", "%25").Replace("/", "%2F").Replace("#", "%23")
            .Replace("@", "%40").Replace("[", "%5B").Replace("]", "%5D");

    private static string Unescape(string value)
        => value.Replace("%5B", "[").Replace("%5D", "]").Replace("%40", "@")
            .Replace("%23", "#").Replace("%2F", "/").Replace("%25", "%");
}
