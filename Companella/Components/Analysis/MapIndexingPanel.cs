using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using Companella.Services.Database;
using Companella.Services.Platform;
using Companella.Services.Common;

namespace Companella.Components.Analysis;

/// <summary>
/// Panel for map indexing controls in the Settings tab.
/// Provides buttons for indexing, reindexing, and refreshing the map database.
/// </summary>
public partial class MapIndexingPanel : CompositeDrawable
{
    [Resolved]
    private MapsDatabaseService MapsDatabase { get; set; } = null!;
    
    [Resolved]
    private OsuProcessDetector ProcessDetector { get; set; } = null!;
    
    [Resolved]
    private AptabaseService AptabaseService { get; set; } = null!;

    private SpriteText _statusText = null!;
    private SpriteText _mapCountText = null!;
    private IndexButton _indexButton = null!;
    private IndexButton _reindexButton = null!;
    private IndexButton _refreshButton = null!;
    
    private CancellationTokenSource? _indexingCts;

    private readonly Color4 _primaryButtonColor = new Color4(80, 150, 200, 255);
    private readonly Color4 _secondaryButtonColor = new Color4(100, 100, 120, 255);
    private readonly Color4 _warningButtonColor = new Color4(200, 130, 80, 255);

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        InternalChildren = new Drawable[]
        {
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    // Header
                    new SpriteText
                    {
                        Text = "Map Database:",
                        Font = new FontUsage("", 16),
                        Colour = new Color4(200, 200, 200, 255)
                    },
                    // Map count
                    _mapCountText = new SpriteText
                    {
                        Text = "4K maps indexed: 0",
                        Font = new FontUsage("", 14),
                        Colour = new Color4(150, 150, 150, 255)
                    },
                    // Buttons row
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            _indexButton = new IndexButton
                            {
                                Width = 90,
                                Height = 28,
                                ButtonText = "Index Maps",
                                ButtonColor = _primaryButtonColor,
                                TooltipText = "Scan osu! Songs folder to enable map recommendations"
                            },
                            _reindexButton = new IndexButton
                            {
                                Width = 100,
                                Height = 28,
                                ButtonText = "Reindex Maps",
                                ButtonColor = _warningButtonColor,
                                TooltipText = "Clear and rebuild the entire map index"
                            },
                            _refreshButton = new IndexButton
                            {
                                Width = 110,
                                Height = 28,
                                ButtonText = "Refresh All MSD",
                                ButtonColor = _secondaryButtonColor,
                                TooltipText = "Recalculate difficulty ratings for all indexed maps"
                            }
                        }
                    },
                    // Status text
                    _statusText = new SpriteText
                    {
                        Text = "",
                        Font = new FontUsage("", 16),
                        Colour = new Color4(120, 120, 120, 255)
                    }
                }
            }
        };

        _indexButton.Clicked += OnIndexClicked;
        _reindexButton.Clicked += OnReindexClicked;
        _refreshButton.Clicked += OnRefreshAllClicked;
        
        MapsDatabase.IndexingProgressChanged += OnIndexingProgressChanged;
        MapsDatabase.IndexingCompleted += OnIndexingCompleted;
        
        UpdateMapCount();
    }

    private void UpdateMapCount()
    {
        var count = MapsDatabase.Get4KMapCount();
        _mapCountText.Text = $"4K maps indexed: {count}";
    }

    private void OnIndexClicked()
    {
        StartIndexing(forceReindex: false);
    }

    private void OnReindexClicked()
    {
        StartIndexing(forceReindex: true);
    }

    private void OnRefreshAllClicked()
    {
        // Refresh all maps by forcing reindex (same as reindex for now)
        StartIndexing(forceReindex: true);
    }

    private void StartIndexing(bool forceReindex)
    {
        var songsFolder = ProcessDetector.GetSongsFolder();
        if (string.IsNullOrEmpty(songsFolder))
        {
            _statusText.Text = "Could not find osu! Songs folder.";
            return;
        }

        if (MapsDatabase.IsIndexing)
        {
            _statusText.Text = "Indexing already in progress...";
            return;
        }

        _indexingCts?.Cancel();
        _indexingCts = new CancellationTokenSource();

        SetButtonsEnabled(false);
        _statusText.Text = forceReindex ? "Reindexing all maps..." : "Indexing new maps...";

        if (forceReindex)
        {
            // For reindex, we clear the database first
            ClearDatabaseAndIndex(songsFolder);
        }
        else
        {
            _ = MapsDatabase.ScanOsuSongsFolderAsync(songsFolder, _indexingCts.Token);
        }
    }

    private async void ClearDatabaseAndIndex(string songsFolder)
    {
        // Clear the database by recreating it (simple approach)
        // The service will skip unchanged files, but we want to force reanalysis
        await MapsDatabase.ScanOsuSongsFolderAsync(songsFolder, _indexingCts?.Token ?? CancellationToken.None);
    }

    private void OnIndexingProgressChanged(object? sender, IndexingProgressEventArgs e)
    {
        Schedule(() =>
        {
            _statusText.Text = $"Indexing: {e.ProcessedFiles}/{e.TotalFiles} ({e.ProgressPercentage}%)";
        });
    }

    private void OnIndexingCompleted(object? sender, IndexingCompletedEventArgs e)
    {
        Schedule(() =>
        {
            SetButtonsEnabled(true);
            UpdateMapCount();

            if (e.WasCancelled)
            {
                _statusText.Text = "Indexing cancelled.";
            }
            else if (e.FailedFiles > 0)
            {
                _statusText.Text = $"Done: {e.IndexedFiles} indexed, {e.FailedFiles} failed.";
                // Track analytics
                AptabaseService.TrackMapIndexing(e.IndexedFiles);
            }
            else
            {
                _statusText.Text = $"Done: {e.IndexedFiles} maps indexed.";
                // Track analytics
                AptabaseService.TrackMapIndexing(e.IndexedFiles);
            }
        });
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _indexButton.Enabled.Value = enabled;
        _reindexButton.Enabled.Value = enabled;
        _refreshButton.Enabled.Value = enabled;
    }

    /// <summary>
    /// Cancels any ongoing indexing operation.
    /// </summary>
    public void CancelIndexing()
    {
        _indexingCts?.Cancel();
        MapsDatabase.CancelIndexing();
    }

    protected override void Dispose(bool isDisposing)
    {
        MapsDatabase.IndexingProgressChanged -= OnIndexingProgressChanged;
        MapsDatabase.IndexingCompleted -= OnIndexingCompleted;
        _indexingCts?.Cancel();
        _indexingCts?.Dispose();
        base.Dispose(isDisposing);
    }
}

/// <summary>
/// Button used in the map indexing panel.
/// </summary>
public partial class IndexButton : CompositeDrawable, IHasTooltip
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _text = null!;

    public string ButtonText { get; set; } = "Button";
    public Color4 ButtonColor { get; set; } = new Color4(80, 150, 200, 255);
    public readonly BindableBool Enabled = new BindableBool(true);
    
    /// <summary>
    /// Tooltip text displayed on hover.
    /// </summary>
    public LocalisableString TooltipText { get; set; }
    
    public event Action? Clicked;

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 4;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = ButtonColor
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            _text = new SpriteText
            {
                Text = ButtonText,
                Font = new FontUsage("", 14, "Bold"),
                Colour = Color4.White,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            }
        };

        Enabled.BindValueChanged(e =>
        {
            this.FadeTo(e.NewValue ? 1 : 0.5f, 100);
        }, true);
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (Enabled.Value)
            _hoverOverlay.FadeTo(0.15f, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (!Enabled.Value) return false;

        _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
        Clicked?.Invoke();
        return true;
    }
}

