using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using OsuMappingHelper.Screens;
using OsuMappingHelper.Services;

namespace OsuMappingHelper;

/// <summary>
/// Main game class for the osu! Mapping Helper application.
/// </summary>
public partial class OsuMappingHelperGame : Game
{
    private ScreenStack _screenStack = null!;
    private DependencyContainer _dependencies = null!;
    private MainScreen _mainScreen = null!;

    // Services
    private OsuProcessDetector _processDetector = null!;
    private OsuFileParser _fileParser = null!;
    private OsuFileWriter _fileWriter = null!;
    private AudioExtractor _audioExtractor = null!;
    private TimingPointConverter _timingConverter = null!;

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        _dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        // Create and register services
        _processDetector = new OsuProcessDetector();
        _fileParser = new OsuFileParser();
        _fileWriter = new OsuFileWriter();
        _audioExtractor = new AudioExtractor();
        _timingConverter = new TimingPointConverter();

        _dependencies.CacheAs(_processDetector);
        _dependencies.CacheAs(_fileParser);
        _dependencies.CacheAs(_fileWriter);
        _dependencies.CacheAs(_audioExtractor);
        _dependencies.CacheAs(_timingConverter);

        return _dependencies;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // Add screen stack
        _screenStack = new ScreenStack
        {
            RelativeSizeAxes = Axes.Both
        };

        Add(_screenStack);

        // Push main screen
        _mainScreen = new MainScreen();
        _screenStack.Push(_mainScreen);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        
        // Set window properties after load is complete
        if (Window != null)
        {
            Window.Title = "osu! Mapping Helper";
            
            // Subscribe to file drop events
            Window.DragDrop += OnWindowFileDrop;
        }
    }

    private void OnWindowFileDrop(string file)
    {
        if (file.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
        {
            Schedule(() => _mainScreen?.HandleFileDrop(file));
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (Window != null)
        {
            Window.DragDrop -= OnWindowFileDrop;
        }
        _processDetector?.Dispose();
        base.Dispose(isDisposing);
    }
}
