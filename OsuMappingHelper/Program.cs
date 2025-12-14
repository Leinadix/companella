using osu.Framework;
using osu.Framework.Platform;

namespace OsuMappingHelper;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        using GameHost host = Host.GetSuitableDesktopHost("OsuMappingHelper");
        using var game = new OsuMappingHelperGame();
        host.Run(game);
    }
}
