using V12.Sdk;
using V12.Sdk.Commands;

static void ShowHelp()
{
    ConOut.WriteLine("V12 SDK for Contract — build and package games");
    ConOut.WriteLine();
    ConOut.WriteLine("Usage: vcsdk <command> [options]");
    ConOut.WriteLine();
    ConOut.WriteLine("Commands:");
    ConOut.WriteLine("  new contract --name <n> Scaffold a Contract game project");
    ConOut.WriteLine("  new net --name <n>      Scaffold a .NET game pak");
    ConOut.WriteLine("  new renderer --name <n> Scaffold a V12 renderer library");
    ConOut.WriteLine("  build [path]            Compile Contract sources and pack .v12pak");
    ConOut.WriteLine("  pack [path]             Publish a .NET project and pack a gamepak .v12pak");
    ConOut.WriteLine("  schema [path]           Generate components.schema.xml (ref-only metadata)");
    ConOut.WriteLine("  links [path]            Check or fix WorldML reference selectors");
    ConOut.WriteLine("  serve [path]            Watch mode - rebuild on file changes");
    ConOut.WriteLine("  install <package>       Install a .coi package from Purr or local path");
    ConOut.WriteLine("  sync [options]          Sync Purr packages from V12-Monorepo (git pull + dotnet pack)");
    ConOut.WriteLine("  doctor                  Diagnose Purr home, feeds, and tools");
    ConOut.WriteLine();
    ConOut.WriteLine("Options:");
    ConOut.WriteLine("  -h, --help              Show help for a command");
    ConOut.WriteLine("  --color                 Force colored output");
    ConOut.WriteLine("  --no-color              Disable colored output");
    ConOut.WriteLine();
    ConOut.WriteLine("build [path] [options]");
    ConOut.WriteLine("  [path]                  Path to game project root (default: .)");
    ConOut.WriteLine("  -o, --output <path>     Output .v12pak path");
    ConOut.WriteLine("  -m, --manifest <path>   Use this manifest instead of auto-detecting");
    ConOut.WriteLine("  -x, --compiler <exe>    Contract compiler (default: ccl)");
    ConOut.WriteLine("  -t, --template <dir>    Expand a template before building");
    ConOut.WriteLine("  -r, --renderer <id>     Installed renderer host to copy (default: monogame)");
    ConOut.WriteLine("  --no-renderer           Do not copy a renderer host beside the pak");
    ConOut.WriteLine("  --runtime <rid>         Target RID(s), comma-separated (default: host RID)");
    ConOut.WriteLine("  --links <mode>          Reference check: off, check, fix (default: check)");
    ConOut.WriteLine("  --strict-links          Fail when any reference is unresolved or ambiguous");
    ConOut.WriteLine();
    ConOut.WriteLine("pack [path] [options]");
    ConOut.WriteLine("  [path]                  Project file or project directory (default: .)");
    ConOut.WriteLine("  -o, --output <path>     Output .v12pak path");
    ConOut.WriteLine("  -m, --manifest <path>   Use this manifest instead of auto-detecting");
    ConOut.WriteLine("      --configuration <c> dotnet build configuration (default: Release)");
    ConOut.WriteLine("      --runtime <rid>     Target RID(s), comma-separated (default: host RID)");
    ConOut.WriteLine("  -r, --renderer <id>     Installed renderer host to copy (default: monogame)");
    ConOut.WriteLine("      --no-renderer       Do not copy a renderer host beside the pak");
    ConOut.WriteLine("  -c, --copy-target <dir> Copy built pak into this directory");
    ConOut.WriteLine("      --live-scripts      Pack .cs sources; compile at load time");
    ConOut.WriteLine("      --no-restore        Skip dotnet restore during publish");
    ConOut.WriteLine("      --links <mode>      Reference check: off, check, fix (default: check)");
    ConOut.WriteLine("      --strict-links      Fail when any reference is unresolved or ambiguous");
    ConOut.WriteLine("      --strict            Fail on V12 version mismatch with the renderer");
    ConOut.WriteLine();
    ConOut.WriteLine("serve [path] [options]");
    ConOut.WriteLine("  [path]                  Path to game project root (default: .)");
    ConOut.WriteLine("  -o, --output <path>     Output .v12pak path");
    ConOut.WriteLine("  -m, --manifest <path>   Use this manifest instead of auto-detecting");
    ConOut.WriteLine("  -x, --compiler <exe>    Contract compiler (default: ccl)");
    ConOut.WriteLine("  -t, --template <dir>    Expand a template on start");
    ConOut.WriteLine("  -i, --interval <ms>     Watch poll interval (default: 500)");
    ConOut.WriteLine("  -c, --copy-target <dir> Copy built pak to this directory");
    ConOut.WriteLine("  -r, --renderer <id>     Installed renderer host to copy (default: monogame)");
    ConOut.WriteLine("  --no-renderer           Do not copy a renderer host beside the pak");
    ConOut.WriteLine("  --runtime <rid>         Target RID (default: host RID)");
    ConOut.WriteLine();
    ConOut.WriteLine("install <package> [options]");
    ConOut.WriteLine("  <package>               Package name (registry) or path to .coi file");
    ConOut.WriteLine("  -x, --compiler <exe>    Contract compiler (default: ccl)");
    ConOut.WriteLine();
    ConOut.WriteLine("new <kind> --name <name> [options]");
    ConOut.WriteLine("  <kind>                  contract, net, or renderer");
    ConOut.WriteLine("  -n, --name <name>       Project name (required)");
    ConOut.WriteLine("  -o, --output <dir>      Output directory (default: ./<name>/)");
    ConOut.WriteLine("  --type <exe|lib>        Contract project type (contract only)");
    ConOut.WriteLine("  -b, --backend <type>    none, monogame, or godot (renderer only)");
    ConOut.WriteLine("  --id <id>               renderer.json id (renderer only)");
    ConOut.WriteLine();
    ConOut.WriteLine("Run 'vcsdk <command> --help' for more details.");
}

// Accept `-renderer` as `--renderer` (and other single-dash long options).
args = ArgParser.NormalizeLongOptions(args);

// Global color override, stripped before command parsing.
if (args.Contains("--no-color")) ConOut.SetColor(false);
else if (args.Contains("--color")) ConOut.SetColor(true);
if (args.Contains("--no-color") || args.Contains("--color"))
    args = args.Where(a => a is not "--no-color" and not "--color").ToArray();

// vcsdk <command> [options]
if (args.Length == 0 || args is ["-h"] or ["--help"] or ["help"])
{
    ShowHelp();
    return args.Length == 0 ? 1 : 0;
}

// Top-level --help / -h before a command (e.g. vcsdk --help, vcsdk -h)
var first = args[0];
if (first is "-h" or "--help")
{
    ShowHelp();
    return 0;
}

var cmd = args[0].TrimStart('-');

if (cmd is "new-dotnet" or "new-renderer")
{
    var replacement = cmd == "new-dotnet" ? "new net" : "new renderer";
    ConOut.WriteError(
        $"Error: 'vcsdk {cmd}' was replaced by 'vcsdk {replacement} --name <name>'. " +
        "Run 'vcsdk new --help'.");
    return 1;
}

switch (cmd)
{
    case "build":          return BuildCommand.Handle(args.Skip(1).ToArray());
    case "pack":           return PackCommand.Handle(args.Skip(1).ToArray());
    case "schema":         return SchemaCommand.Handle(args.Skip(1).ToArray());
    case "links":          return LinksCommand.Handle(args.Skip(1).ToArray());
    case "serve":          return ServeCommand.Handle(args.Skip(1).ToArray());
    case "install":        return InstallCommand.Handle(args.Skip(1).ToArray());
    case "new":            return NewCommand.Handle(args.Skip(1).ToArray());
    case "sync":           return SyncCommand.Handle(args.Skip(1).ToArray());
    case "doctor":         return DoctorCommand.Handle(args.Skip(1).ToArray());
    case "purr":           return DoctorCommand.Handle(args.Skip(1).ToArray());
    case "help":
        if (args.Length > 1)
        {
            var sub = args[1].TrimStart('-');
            switch (sub)
            {
                case "build":      return BuildCommand.Handle(new[] { "--help" });
                case "pack":       return PackCommand.Handle(new[] { "--help" });
                case "schema":     return SchemaCommand.Handle(new[] { "--help" });
                case "links":      return LinksCommand.Handle(new[] { "--help" });
                case "serve":      return ServeCommand.Handle(new[] { "--help" });
                case "install":    return InstallCommand.Handle(new[] { "--help" });
                case "new":        return NewCommand.Handle(new[] { "--help" });
                case "sync":       return SyncCommand.Handle(new[] { "--help" });
                case "doctor":     return DoctorCommand.Handle(new[] { "--help" });
                default: break;
            }
        }
        ShowHelp();
        return 0;
    default:
        ConOut.WriteError($"Error: unknown command '{args[0]}'. Run 'vcsdk --help' for usage.");
        return 1;
}
