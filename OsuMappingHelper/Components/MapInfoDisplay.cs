using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Models;
using OsuMappingHelper.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace OsuMappingHelper.Components;

/// <summary>
/// Displays comprehensive map metadata with the beatmap background image, MSD chart, and pattern analysis.
/// </summary>
public partial class MapInfoDisplay : CompositeDrawable
{
    private SpriteText _artistTitleText = null!;
    private SpriteText _mapperDiffText = null!;
    private SpriteText _sourceText = null!;
    private SpriteText _difficultyStatsText = null!;
    private SpriteText _timingStatsText = null!;
    private SpriteText _tagsText = null!;
    private SpriteText _beatmapIdText = null!;
    
    private double? _yavsrgDifficulty;
    
    // Background
    private Sprite _backgroundSprite = null!;
    private Box _backgroundDimOverlay = null!;

    // MSD Chart
    private MsdChart _msdChart = null!;
    private string? _currentBeatmapPath;
    private CancellationTokenSource? _msdCancellation;

    // Pattern Display
    private PatternDisplay _patternDisplay = null!;
    private CancellationTokenSource? _patternCancellation;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _labelColor = new Color4(160, 160, 160, 255);
    private readonly Color4 _valueColor = new Color4(230, 230, 230, 255);

    [Resolved]
    private IRenderer Renderer { get; set; } = null!;

    private string? _currentBackgroundPath;
    private OsuFile? _currentOsuFile;

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 8;

        InternalChildren = new Drawable[]
        {
            // Dark fallback background
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(30, 28, 38, 255)
            },
            // Beatmap background image
            _backgroundSprite = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Fill,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Alpha = 0
            },
            // Dim overlay for readability
            _backgroundDimOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0, 0, 0, 160),
                Alpha = 0
            },
            // Content - Two rows: info on top, MSD chart + patterns on bottom
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(12),
                Child = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions = new[]
                    {
                        new Dimension(GridSizeMode.Absolute, 95),  // Top row (map info)
                        new Dimension()                             // Bottom row (MSD chart + patterns, fills remaining)
                    },
                    Content = new[]
                    {
                        // Top row - Map info (two columns)
                        new Drawable[]
                        {
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ColumnDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.Relative, 0.6f),  // Left column (main info)
                                    new Dimension(GridSizeMode.Relative, 0.4f)   // Right column (stats)
                                },
                                Content = new[]
                                {
                                    new Drawable[]
                                    {
                                        // Left column - Main info
                                        new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, 4),
                                            Children = new Drawable[]
                                            {
                                                // Artist - Title (large)
                                                _artistTitleText = new SpriteText
                                                {
                                                    Text = "No map loaded",
                                                    Font = new FontUsage("", 23, "Bold"),
                                                    Colour = Color4.White,
                                                    Truncate = true,
                                                    RelativeSizeAxes = Axes.X
                                                },
                                                // Mapped by X [Difficulty]
                                                _mapperDiffText = new SpriteText
                                                {
                                                    Text = "",
                                                    Font = new FontUsage("", 17),
                                                    Colour = _valueColor,
                                                    Truncate = true,
                                                    RelativeSizeAxes = Axes.X
                                                },
                                                // Source
                                                _sourceText = new SpriteText
                                                {
                                                    Text = "",
                                                    Font = new FontUsage("", 15),
                                                    Colour = _labelColor,
                                                    Truncate = true,
                                                    RelativeSizeAxes = Axes.X
                                                },
                                                // Tags (truncated)
                                                _tagsText = new SpriteText
                                                {
                                                    Text = "",
                                                    Font = new FontUsage("", 13),
                                                    Colour = new Color4(120, 120, 120, 255),
                                                    Truncate = true,
                                                    RelativeSizeAxes = Axes.X
                                                }
                                            }
                                        },
                                        // Right column - Stats
                                        new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, 6),
                                            Padding = new MarginPadding { Left = 15 },
                                            Children = new Drawable[]
                                            {
                                                // Difficulty settings (CS, AR, OD, HP)
                                                _difficultyStatsText = new SpriteText
                                                {
                                                    Text = "",
                                                    Font = new FontUsage("", 16),
                                                    Colour = _accentColor
                                                },
                                                // Timing points stats
                                                _timingStatsText = new SpriteText
                                                {
                                                    Text = "",
                                                    Font = new FontUsage("", 15),
                                                    Colour = _valueColor
                                                },
                                                // Beatmap ID info
                                                _beatmapIdText = new SpriteText
                                                {
                                                    Text = "",
                                                    Font = new FontUsage("", 13),
                                                    Colour = _labelColor
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        // Bottom row - MSD Chart (left) + Pattern Display (right)
                        new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Top = 8 },
                                Child = new GridContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    ColumnDimensions = new[]
                                    {
                                        new Dimension(GridSizeMode.Relative, 0.6f),  // MSD Chart
                                        new Dimension(GridSizeMode.Relative, 0.4f)   // Pattern Display
                                    },
                                    Content = new[]
                                    {
                                        new Drawable[]
                                        {
                                            // MSD Chart
                                            _msdChart = new MsdChart
                                            {
                                                RelativeSizeAxes = Axes.Both
                                            },
                                            // Pattern Display
                                            new Container
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Padding = new MarginPadding { Left = 8 },
                                                Child = _patternDisplay = new PatternDisplay
                                                {
                                                    RelativeSizeAxes = Axes.Both
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            // Accent border at bottom
            new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 3,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Colour = _accentColor
            }
        };
    }

    public void SetMapInfo(OsuFile osuFile)
    {
        _currentOsuFile = osuFile;
        
        // Use romanized (ASCII) variants for display
        var artist = !string.IsNullOrEmpty(osuFile.Artist) ? osuFile.Artist : osuFile.ArtistUnicode;
        var title = !string.IsNullOrEmpty(osuFile.Title) ? osuFile.Title : osuFile.TitleUnicode;
        
        _artistTitleText.Text = $"{artist} - {title}";
        _mapperDiffText.Text = $"Mapped by {osuFile.Creator} [{osuFile.Version}]";
        
        // Source
        if (!string.IsNullOrEmpty(osuFile.Source))
            _sourceText.Text = $"Source: {osuFile.Source}";
        else
            _sourceText.Text = "";

        // Tags (truncated for display)
        if (!string.IsNullOrEmpty(osuFile.Tags))
        {
            var tags = osuFile.Tags.Length > 80 ? osuFile.Tags.Substring(0, 77) + "..." : osuFile.Tags;
            _tagsText.Text = $"Tags: {tags}";
        }
        else
        {
            _tagsText.Text = "";
        }

        // Reset YAVSRG difficulty (will be calculated asynchronously)
        _yavsrgDifficulty = null;
        
        // Difficulty settings
        UpdateDifficultyStats(osuFile);

        // Timing points stats
        var uninheritedCount = osuFile.TimingPoints.Count(tp => tp.Uninherited);
        var inheritedCount = osuFile.TimingPoints.Count(tp => !tp.Uninherited);
        
        // Get BPM range
        var bpms = osuFile.TimingPoints
            .Where(tp => tp.Uninherited && tp.BeatLength > 0)
            .Select(tp => tp.Bpm)
            .ToList();
        
        string bpmInfo = "No BPM";
        if (bpms.Count > 0)
        {
            var minBpm = bpms.Min();
            var maxBpm = bpms.Max();
            bpmInfo = Math.Abs(minBpm - maxBpm) < 0.1 
                ? $"{minBpm:F0} BPM" 
                : $"{minBpm:F0}-{maxBpm:F0} BPM";
        }
        
        _timingStatsText.Text = $"{bpmInfo}  |  {uninheritedCount} timing, {inheritedCount} SV points";

        // Beatmap ID info
        var idParts = new List<string>();
        if (osuFile.BeatmapSetID > 0)
            idParts.Add($"Set: {osuFile.BeatmapSetID}");
        if (osuFile.BeatmapID > 0)
            idParts.Add($"Map: {osuFile.BeatmapID}");
        _beatmapIdText.Text = idParts.Count > 0 ? string.Join("  |  ", idParts) : "Not submitted";

        // Load background image
        LoadBackground(osuFile.BackgroundFilePath);

        // Load MSD analysis for mania maps
        LoadMsdAnalysis(osuFile);

        // Load pattern analysis for mania maps
        LoadPatternAnalysis(osuFile);
        
        // Calculate YAVSRG difficulty for mania maps
        LoadYavsrgDifficulty(osuFile);
    }
    
    private void UpdateDifficultyStats(OsuFile osuFile)
    {
        var statsParts = new List<string>
        {
            osuFile.ModeName,
            $"OD {osuFile.OverallDifficulty:F1}",
            $"HP {osuFile.HPDrainRate:F1}"
        };
        
        // Add YAVSRG difficulty if available
        if (_yavsrgDifficulty.HasValue)
        {
            statsParts.Add($"{_yavsrgDifficulty.Value:F2}*");
        }
        
        _difficultyStatsText.Text = string.Join("  |  ", statsParts);
    }
    
    private void LoadYavsrgDifficulty(OsuFile osuFile)
    {
        // Only calculate for 4K mania maps
        if (osuFile.Mode != 3 || Math.Abs(osuFile.CircleSize - 4.0) > 0.1)
        {
            _yavsrgDifficulty = null;
            return;
        }
        
        // Calculate YAVSRG difficulty in background
        Task.Run(() =>
        {
            try
            {
                var difficultyService = new InterludeDifficultyService();
                var difficulty = difficultyService.CalculateDifficulty(osuFile, 1.0f);
                
                Schedule(() =>
                {
                    _yavsrgDifficulty = difficulty;
                    if (_currentOsuFile?.FilePath == osuFile.FilePath)
                    {
                        UpdateDifficultyStats(_currentOsuFile);
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[YAVSRG] Difficulty calculation failed: {ex.Message}");
                Schedule(() =>
                {
                    _yavsrgDifficulty = null;
                    if (_currentOsuFile?.FilePath == osuFile.FilePath)
                    {
                        UpdateDifficultyStats(_currentOsuFile);
                    }
                });
            }
        });
    }

    public void SetNoMap()
    {
        _currentOsuFile = null;
        _yavsrgDifficulty = null;
        
        _artistTitleText.Text = "No map loaded";
        _mapperDiffText.Text = "Select a beatmap or drop a .osu file";
        _sourceText.Text = "";
        _tagsText.Text = "";
        _difficultyStatsText.Text = "";
        _timingStatsText.Text = "";
        _beatmapIdText.Text = "";
        
        ClearBackground();
        ClearMsdAnalysis();
        ClearPatternAnalysis();
    }

    private void LoadMsdAnalysis(OsuFile osuFile)
    {
        // Cancel any pending MSD analysis
        _msdCancellation?.Cancel();
        _msdCancellation = new CancellationTokenSource();
        var token = _msdCancellation.Token;

        // Only analyze 4K mania maps (CS = 4.0 for 4K)
        if (osuFile.Mode != 3 || Math.Abs(osuFile.CircleSize - 4.0) > 0.1)
        {
            _msdChart.ShowError("MSD: 4K mania only");
            return;
        }

        // Check if msd-calculator exists
        if (!ToolPaths.MsdCalculatorExists)
        {
            _msdChart.ShowError("msd-calculator not found");
            return;
        }

        // Don't re-analyze the same beatmap
        if (_currentBeatmapPath == osuFile.FilePath)
            return;

        _currentBeatmapPath = osuFile.FilePath;
        _msdChart.ShowLoading();

        // Run analysis in background (use --rate 1.0 for faster results)
        Task.Run(async () =>
        {
            try
            {
                var analyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);
                var result = await analyzer.AnalyzeSingleRateAsync(osuFile.FilePath, 1.0f);

                if (token.IsCancellationRequested)
                    return;

                Schedule(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        _msdChart.SetSingleRateResult(result);
                        
                        // Pass full MSD scores to pattern display for sorting and classification
                        if (result?.Scores != null)
                        {
                            _patternDisplay.SetMsdScores(result.Scores);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MSD] Analysis failed: {ex.Message}");
                
                if (token.IsCancellationRequested)
                    return;

                Schedule(() =>
                {
                    if (!token.IsCancellationRequested)
                        _msdChart.ShowError($"Error: {ex.Message.Split('\n')[0]}");
                });
            }
        }, token);
    }

    private void ClearMsdAnalysis()
    {
        _msdCancellation?.Cancel();
        _currentBeatmapPath = null;
        _msdChart.Clear();
    }

    private void LoadPatternAnalysis(OsuFile osuFile)
    {
        // Cancel any pending pattern analysis
        _patternCancellation?.Cancel();
        _patternCancellation = new CancellationTokenSource();
        var token = _patternCancellation.Token;

        // Only analyze 4K mania maps
        if (osuFile.Mode != 3 || Math.Abs(osuFile.CircleSize - 4.0) > 0.1)
        {
            _patternDisplay.ShowError("4K mania only");
            return;
        }

        _patternDisplay.ShowLoading();

        // Run analysis in background
        Task.Run(() =>
        {
            try
            {
                var patternFinder = new PatternFinder(osuFile);
                var result = patternFinder.FindAllPatterns(osuFile);

                if (token.IsCancellationRequested)
                    return;

                Schedule(() =>
                {
                    if (!token.IsCancellationRequested)
                        _patternDisplay.SetPatternResult(result, osuFile);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Pattern] Analysis failed: {ex.Message}");
                
                if (token.IsCancellationRequested)
                    return;

                Schedule(() =>
                {
                    if (!token.IsCancellationRequested)
                        _patternDisplay.ShowError($"Error: {ex.Message.Split('\n')[0]}");
                });
            }
        }, token);
    }

    private void ClearPatternAnalysis()
    {
        _patternCancellation?.Cancel();
        _patternDisplay.Clear();
    }

    private void LoadBackground(string? backgroundPath)
    {
        if (string.IsNullOrEmpty(backgroundPath) || !File.Exists(backgroundPath))
        {
            ClearBackground();
            return;
        }

        // Don't reload if same background
        if (_currentBackgroundPath == backgroundPath)
            return;

        _currentBackgroundPath = backgroundPath;

        try
        {
            // Load texture from file using ImageSharp
            // Note: Do NOT dispose the image - TextureUpload takes ownership and disposes it
            using var stream = File.OpenRead(backgroundPath);
            var image = Image.Load<Rgba32>(stream);
            
            var texture = Renderer.CreateTexture(image.Width, image.Height);
            // TextureUpload takes ownership of the image and will dispose it after upload
            texture.SetData(new TextureUpload(image));

            Schedule(() =>
            {
                _backgroundSprite.Texture = texture;
                _backgroundSprite.FadeTo(1, 200);
                _backgroundDimOverlay.FadeTo(1, 200);
            });
        }
        catch
        {
            // Failed to load background, use default
            ClearBackground();
        }
    }

    private void ClearBackground()
    {
        _currentBackgroundPath = null;
        _backgroundSprite.FadeTo(0, 200);
        _backgroundDimOverlay.FadeTo(0, 200);
    }
}
