using V12.Sdk.Commands;

static void ShowHelp()
{
    Console.WriteLine("V12 SDK for Contract — build and package games");
    Console.WriteLine();
    Console.WriteLine("Usage: vcsdk <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  new <name>              Scaffold a new Contract game project");
    Console.WriteLine("  new-dotnet <name>       Scaffold a new C#/.NET SDK game project");
    Console.WriteLine("  build [path]            Compile contracts/dotnet and pack .v12pak");
    Console.WriteLine("  serve [path]            Watch mode - rebuild on file changes");
    Console.WriteLine("  install <package>       Install a .coi package from Purr or local path");
    Console.WriteLine("  sync [options]          Sync Purr packages from V12-Monorepo (git pull + dotnet pack)");
    Console.WriteLine("  doctor                  Diagnose Purr home, feeds, and tools");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -h, --help              Show help for a command");
    Console.WriteLine();
    Console.WriteLine("build [path] [options]");
    Console.WriteLine("  [path]                  Path to game project root (default: .)");
    Console.WriteLine("  -o, --output <path>     Output .v12pak path");
    Console.WriteLine("  -m, --manifest <path>   Use this manifest instead of auto-detecting");
    Console.WriteLine("  -x, --compiler <exe>    Contract compiler (default: ccl)");
    Console.WriteLine("  -t, --template <dir>    Expand a template before building");
    Console.WriteLine();
    Console.WriteLine("serve [path] [options]");
    Console.WriteLine("  [path]                  Path to game project root (default: .)");
    Console.WriteLine("  -o, --output <path>     Output .v12pak path");
    Console.WriteLine("  -m, --manifest <path>   Use this manifest instead of auto-detecting");
    Console.WriteLine("  -x, --compiler <exe>    Contract compiler (default: ccl)");
    Console.WriteLine("  -t, --template <dir>    Expand a template on start");
    Console.WriteLine("  -i, --interval <ms>     Watch poll interval (default: 500)");
    Console.WriteLine("  -c, --copy-target <dir> Copy built pak to this directory");
    Console.WriteLine();
    Console.WriteLine("install <package> [options]");
    Console.WriteLine("  <package>               Package name (registry) or path to .coi file");
    Console.WriteLine("  -x, --compiler <exe>    Contract compiler (default: ccl)");
    Console.WriteLine();
    Console.WriteLine("new <name>");
    Console.WriteLine("  <name>                  Project name");
    Console.WriteLine();
    Console.WriteLine("new-dotnet <name> [options]");
    Console.WriteLine("  <name>                  Project name");
    Console.WriteLine("  -o, --output <dir>      Output directory (default: ./<name>/)");
    Console.WriteLine("  -b, --backend <type>    Target backend: none, monoGame, or godot (default: none)");
    Console.WriteLine();
    Console.WriteLine("Run 'vcsdk <command> --help' for more details.");
}

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
switch (cmd)
{
    case "build":          return BuildCommand.Handle(args.Skip(1).ToArray());
    case "serve":          return ServeCommand.Handle(args.Skip(1).ToArray());
    case "install":        return InstallCommand.Handle(args.Skip(1).ToArray());
    case "new":            return NewCommand.Handle(args.Skip(1).ToArray());
    case "new-dotnet":     return DotnetNewCommand.Handle(args.Skip(1).ToArray());
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
                case "serve":      return ServeCommand.Handle(new[] { "--help" });
                case "install":    return InstallCommand.Handle(new[] { "--help" });
                case "new":        return NewCommand.Handle(new[] { "--help" });
                case "new-dotnet": return DotnetNewCommand.Handle(new[] { "--help" });
                case "sync":       return SyncCommand.Handle(new[] { "--help" });
                case "doctor":     return DoctorCommand.Handle(new[] { "--help" });
                default: break;
            }
        }
        ShowHelp();
        return 0;
    default:
        Console.Error.WriteLine($"Error: unknown command '{args[0]}'. Run 'vcsdk --help' for usage.");
        return 1;
}
