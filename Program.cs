using V12.Sdk.Commands;

// vcsdk <command> [options]
if (args.Length == 0)
{
    Console.WriteLine("V12 SDK for Contract — build and package games");
    Console.WriteLine();
    Console.WriteLine("Usage: vcsdk <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  build [path]            Compile contracts and pack .v12pak");
    Console.WriteLine("  serve [path]            Watch mode — rebuild on file changes");
    Console.WriteLine("  install <package>       Install a .coi package from Purr or local path");
    Console.WriteLine();
    Console.WriteLine("Scaffold a project with: ccl new <name>");
    return 1;
}

var cmd = args[0].TrimStart('-');
switch (cmd)
{
    case "build":     return BuildCommand.Handle(args.Skip(1).ToArray());
    case "serve":     return ServeCommand.Handle(args.Skip(1).ToArray());
    case "install":   return InstallCommand.Handle(args.Skip(1).ToArray());
    default:
        Console.Error.WriteLine($"Error: unknown command '{args[0]}'.");
        return 1;
}
