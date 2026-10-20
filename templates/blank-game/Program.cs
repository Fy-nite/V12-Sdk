// {name} - created with V12 SDK
// .NET SDK game project

using System.Diagnostics;
using Serilog;
using V12.Core;

namespace {name-to-lower}
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GameRoot.ConfigureLogging(Serilog.Events.LogEventLevel.Debug);

            var game = new GameRoot();
            game.Initialize();

            // TODO: Load worlds/gamepaks here:
            //   game.CreateWorld("MainScene", "HotReload");
            //   game.LoadGamepacks(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gamepaks"));

            Console.WriteLine($"V12 game '{nameof(BlankGame)}' starting...");
            Console.WriteLine("Press Ctrl+C to exit.");

            var stopwatch = Stopwatch.StartNew();
            double lastTick = 0;
            const double tickInterval = 1.0 / 60.0;

            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Escape)
                        break;
                }

                double elapsed = stopwatch.Elapsed.TotalSeconds - lastTick;
                lastTick = stopwatch.Elapsed.TotalSeconds;

                if (elapsed >= tickInterval)
                {
                    game.V12Tick((float)elapsed);
                }
                else
                {
                    Thread.Sleep(1);
                }
            }

            game.Shutdown();
            stopwatch.Stop();
            Console.WriteLine($"Game exited after {stopwatch.ElapsedMilliseconds} ms.");
        }
    }
}
