using System.IO;
using System.Runtime.InteropServices;
using osu.Framework;
using osu.Framework.Platform;

namespace OsuMappingHelper;

public static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [STAThread]
    public static void Main(string[] args)
    {

        // Parse command line arguments
        bool trainingMode = ParseTrainingMode(args);

        // Disable console output from osu!framework by redirecting to null stream
        // This prevents osu!framework from writing to console
        //var nullStream = new StreamWriter(Stream.Null) { AutoFlush = true };
        //Console.SetOut(nullStream);
        //Console.SetError(nullStream);

        using GameHost host = Host.GetSuitableDesktopHost("Companella!");
        using var game = new OsuMappingHelperGame(trainingMode);
        host.Run(game);
    }

    /// <summary>
    /// Parses command line arguments for --training flag.
    /// </summary>
    private static bool ParseTrainingMode(string[] args)
    {
        if (args == null || args.Length == 0)
            return false;

        foreach (var arg in args)
        {
            if (arg.Equals("--training", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-t", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
