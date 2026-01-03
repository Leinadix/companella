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
