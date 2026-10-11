using System.Diagnostics;

namespace V12.Sdk;

static class PwshUtil
{
    public static int RunProcess(string exe, string args, string cwd)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var p = Process.Start(psi)!;
        Console.Write(p.StandardOutput.ReadToEnd());
        Console.Error.Write(p.StandardError.ReadToEnd());
        p.WaitForExit();
        return p.ExitCode;
    }
}
