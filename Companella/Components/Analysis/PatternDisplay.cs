using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using Companella.Models.Beatmap;
using Companella.Services.Analysis;
using Companella.Models.Difficulty;
using Companella.Models.Training;
using Companella.Services.Common;

namespace Companella.Components.Analysis;

/// <summary>
/// Displays top 5 pattern types sorted by their pattern-specific MSD,
/// plus the dan classification below (using YAVSRG difficulty).
/// </summary>
public partial class PatternDisplay : CompositeDrawable
{
    private SpriteText _titleText = null!;
    private SpriteText _loadingText = null!;
    private SpriteText _errorText = null!;
    private FillFlowContainer _rowsContainer = null!;
    private Container _classifierContainer = null!;
    private SpriteText _classifierLabel = null!;
    private SpriteText _classifierValue = null!;
    private SpriteText _classifierDetail = null!;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _valueColor = new Color4(230, 230, 230, 255);

    // Store pattern result for display
    private PatternAnalysisResult? _currentPatternResult;
    private List<TopPattern>? _currentTopPatterns;
    private SkillsetScores? _pendingMsdScores;
    private OsuFile? _currentOsuFile;
    private float _currentRate = 1.0f;

    [Resolved(canBeNull: true)]
    private DanConfigurationService? DanConfigService { get; set; }

    // Pattern type colors (matching common rhythm game conventions)
    private static readonly Dictionary<PatternType, Color4> PatternColors = new()
    {
        { PatternType.Trill, new Color4(100, 200, 255, 255) },     // Light blue
        { PatternType.Jack, new Color4(255, 100, 100, 255) },      // Red
        { PatternType.Minijack, new Color4(255, 150, 150, 255) },  // Light red
        { PatternType.Stream, new Color4(100, 180, 255, 255) },    // Blue
        { PatternType.Jump, new Color4(100, 220, 100, 255) },      // Green
        { PatternType.Hand, new Color4(255, 180, 100, 255) },      // Orange
        { PatternType.Quad, new Color4(255, 220, 100, 255) },      // Yellow
        { PatternType.Jumpstream, new Color4(100, 220, 100, 255) },// Green
        { PatternType.Handstream, new Color4(255, 180, 100, 255) },// Orange
        { PatternType.Chordjack, new Color4(255, 220, 100, 255) }, // Yellow
        { PatternType.Roll, new Color4(180, 100, 255, 255) },      // Purple
        { PatternType.Bracket, new Color4(100, 220, 220, 255) },   // Cyan
        { PatternType.Jumptrill, new Color4(220, 100, 220, 255) }  // Magenta
    };

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            // Title
            _titleText = new SpriteText
            {
                Text = "",
                Font = new FontUsage("", 19, "Bold"),
                Colour = _accentColor,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft
            },
            // Loading text
            _loadingText = new SpriteText
            {
                Text = "Analyzing...",
                Font = new FontUsage("", 17),
                Colour = new Color4(160, 160, 160, 255),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Alpha = 0
            },
            // Error text
            _errorText = new SpriteText
            {
                Text = "",
                Font = new FontUsage("", 15),
                Colour = new Color4(255, 100, 100, 255),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Alpha = 0
            },
            // Container for top 5 pattern rows (with top padding to push down)
            _rowsContainer = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 3),
                Padding = new MarginPadding { Top = 22 },
                Alpha = 0
            },
            // Classifier display - anchored to bottom right
            _classifierContainer = new Container
            {
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Alpha = 0,
                Child = new HoverableClassifierBox
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 4,
                    Children = new Drawable[]
                    {
                        // Background
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(45, 42, 55, 255)
                        },
                        // Content
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Padding = new MarginPadding { Horizontal = 8, Vertical = 5 },
                            Spacing = new Vector2(6, 0),
                            Children = new Drawable[]
                            {
                                _classifierLabel = new SpriteText
                                {
                                    Text = "Dan (BETA):",
                                    Font = new FontUsage("", 16),
                                    Colour = new Color4(140, 140, 140, 255),
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft
                                },
                                _classifierValue = new SpriteText
                                {
                                    Text = "?",
                                    Font = new FontUsage("", 18, "Bold"),
                                    Colour = _accentColor,
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft
                                },
                                _classifierDetail = new SpriteText
                                {
                                    Text = "",
                                    Font = new FontUsage("", 17),
                                    Colour = new Color4(120, 120, 120, 255),
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Alpha = 0 // Hidden by default, shown on hover
                                }
                            }
                        }
                    },
                    DetailText = _classifierDetail
                }
            }
        };
    }

    /// <summary>
    /// Shows loading state.
    /// </summary>
    public void ShowLoading()
    {
        _loadingText.FadeTo(1, 100);
        _errorText.FadeTo(0, 100);
        _rowsContainer.FadeTo(0, 100);
        _classifierContainer.FadeTo(0, 100);
    }

    /// <summary>
    /// Shows error message.
    /// </summary>
    public void ShowError(string message)
    {
        _loadingText.FadeTo(0, 100);
        _errorText.Text = message;
        _errorText.FadeTo(1, 100);
        _rowsContainer.FadeTo(0, 100);
        _classifierContainer.FadeTo(0, 100);
    }

    /// <summary>
    /// Clears the display.
    /// </summary>
    public void Clear()
    {
        _loadingText.FadeTo(0, 100);
        _errorText.FadeTo(0, 100);
        _rowsContainer.FadeTo(0, 100);
        _classifierContainer.FadeTo(0, 100);
        _rowsContainer.Clear();
        _titleText.Text = "";
        _currentPatternResult = null;
        _currentTopPatterns = null;
        _pendingMsdScores = null;
        _currentOsuFile = null;
        _currentRate = 1.0f;
        _classifierValue.Text = "?";
        _classifierDetail.Text = "";
    }

    /// <summary>
    /// Sets the pattern analysis result to display.
    /// If MSD scores are already available, shows top 5 patterns sorted by MSD.
    /// Otherwise shows patterns by percentage until MSD arrives.
    /// </summary>
    public void SetPatternResult(PatternAnalysisResult result, OsuFile osuFile)
    {
        _loadingText.FadeTo(0, 100);
        _errorText.FadeTo(0, 100);
        _rowsContainer.Clear();

        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "Analysis failed");
            return;
        }

        _currentPatternResult = result;
        _currentOsuFile = osuFile;

        // Check if MSD is already available (handles race condition)
        if (_pendingMsdScores != null)
        {
            // MSD arrived before patterns, sort by MSD now
            DisplayPatternsSortedByMsd(result, _pendingMsdScores);
            // Trigger classification now that both patterns and MSD are ready
            TriggerClassification();
            _pendingMsdScores = null;
        }
        else
        {
            // No MSD yet, show patterns sorted by percentage temporarily
            // Exclude Jump, Quad, and Hand patterns
            var topPatterns = result.GetTopPatterns()
                .Where(p => p.Type != PatternType.Jump && p.Type != PatternType.Quad && p.Type != PatternType.Hand)
                .Take(5)
                .ToList();
            _currentTopPatterns = topPatterns;

            if (topPatterns.Count == 0)
            {
                _titleText.Text = "";
                _rowsContainer.FadeTo(1, 200);
                return;
            }

            _titleText.Text = "";

            // Add top pattern rows (max 5, sorted by percentage for now)
            foreach (var pattern in topPatterns)
            {
                var color = PatternColors.GetValueOrDefault(pattern.Type, _valueColor);
                _rowsContainer.Add(new TopPatternRow(pattern, color, null, null));
            }

            _rowsContainer.FadeTo(1, 200);
            
            // Don't classify yet - wait for MSD scores to arrive for accurate pattern ranking
            // Classification will be triggered when SetMsdScores is called
        }
    }

    /// <summary>
    /// Sets the MSD scores and triggers re-sorting of patterns.
    /// Call this after MSD analysis completes.
    /// Handles race condition: if patterns aren't ready yet, stores scores for later.
    /// </summary>
    /// <param name="scores">MSD skillset scores.</param>
    /// <param name="rate">Rate multiplier (1.0 = normal, 1.5 = DT, 0.75 = HT).</param>
    public void SetMsdScores(SkillsetScores scores, float rate = 1.0f)
    {
        // Always store the scores and rate for classification
        _pendingMsdScores = scores;
        _currentRate = rate;

        if (_currentPatternResult == null)
        {
            // Patterns haven't arrived yet, scores stored for when they do
            return;
        }

        // Both are ready - re-sort patterns by MSD
        _rowsContainer.Clear();
        DisplayPatternsSortedByMsd(_currentPatternResult, scores);
        
        // Trigger classification now that patterns are sorted by MSD
        TriggerClassification();
    }

    /// <summary>
    /// Triggers classification using the current top patterns and OsuFile.
    /// Only classifies if both are available and MSD-sorted patterns exist.
    /// </summary>
    private void TriggerClassification()
    {
        if (_currentOsuFile == null || _currentTopPatterns == null || _currentTopPatterns.Count == 0)
        {
            return;
        }

        // Capture local copies BEFORE Task.Run to avoid race conditions
        var msdScores = _pendingMsdScores;
        var osuFile = _currentOsuFile;
        var rate = _currentRate;

        // Show "calculating" state
        _classifierValue.Text = "...";
        _classifierDetail.Text = "Calculating...";
        _classifierContainer.FadeTo(1, 200);
        
        // Calculate YAVSRG and classify in background
        Task.Run(() =>
        {
            try
            {
                // Allow classification if either ONNX model OR dans.json is loaded
                if (DanConfigService == null || (!DanConfigService.IsModelLoaded && !DanConfigService.IsLoaded))
                {
                    Schedule(() =>
                    {
                        _classifierValue.Text = "?";
                        _classifierDetail.Text = "No model or config";
                    });
                    return;
                }

                var result = DanConfigService.ClassifyMap(msdScores, osuFile, rate);

                Schedule(() =>
                {
                    UpdateClassificationDisplay(result);
                });
            }
            catch (Exception ex)
            {
                Logger.Info($"[PatternDisplay] Classification failed: {ex.Message}");
                Schedule(() =>
                {
                    _classifierValue.Text = "?";
                    _classifierDetail.Text = "Error";
                });
            }
        });
    }

    /// <summary>
    /// Displays patterns sorted by their pattern-specific MSD (highest first).
    /// </summary>
    private void DisplayPatternsSortedByMsd(PatternAnalysisResult result, SkillsetScores scores)
    {
        // Get all patterns from the result, excluding Jump, Quad, and Hand
        var allPatterns = result.GetAllPatternsSorted()
            .Where(p => p.Type != PatternType.Jump && p.Type != PatternType.Quad && p.Type != PatternType.Hand)
            .ToList();

        if (allPatterns.Count == 0)
        {
            _titleText.Text = "";
            _rowsContainer.FadeTo(1, 200);
            return;
        }

        // Sort patterns by their pattern-specific MSD (highest first)
        var patternsByMsd = allPatterns
            .Select(p => new
            {
                Pattern = p,
                Msd = PatternToMsdMapper.GetMsdForPattern(p.Type, scores),
                MsdName = PatternToMsdMapper.GetMsdNameForPattern(p.Type)
            })
            .Where(p => p.Msd > 0) // Only show patterns with MSD > 0
            .OrderByDescending(p => p.Msd)
            .Take(5)
            .ToList();

        _currentTopPatterns = patternsByMsd.Select(p => p.Pattern).ToList();
        _titleText.Text = "";

        // Add top pattern rows sorted by MSD
        foreach (var item in patternsByMsd)
        {
            var color = PatternColors.GetValueOrDefault(item.Pattern.Type, _valueColor);
            _rowsContainer.Add(new TopPatternRow(item.Pattern, color, item.Msd, item.MsdName));
        }

        _rowsContainer.FadeTo(1, 200);
    }

    /// <summary>
    /// Updates the classification display with the result.
    /// </summary>
    private void UpdateClassificationDisplay(DanClassificationResult result)
    {
        // Update display
        _classifierValue.Text = result.DisplayName;
        
        // Build detail string showing raw model output and skillset
        var details = new List<string>();
        
        // Show raw model output if available (from ONNX model)
        if (result.RawModelOutput.HasValue)
        {
            details.Add($"Raw: {result.RawModelOutput.Value:F2}");
        }
        
        _classifierDetail.Text = string.Join(" | ", details);

        // Color based on confidence
        _classifierValue.Colour = result.Confidence > 0.7 
            ? _accentColor 
            : new Color4(200, 180, 100, 255);

        _classifierContainer.FadeTo(1, 200);
    }

    /// <summary>
    /// A compact row showing pattern type, BPM, and MSD (or percentage if no MSD).
    /// Format: "[color] Type @ BPM  MSD XX.X" or "[color] Type @ BPM  XX%"
    /// </summary>
    private partial class TopPatternRow : CompositeDrawable
    {
        public TopPatternRow(TopPattern pattern, Color4 color, double? msd = null, string? msdName = null)
        {
            RelativeSizeAxes = Axes.X;
            Height = 18;

            var secondaryColor = new Color4(160, 160, 160, 255);

            // Display MSD if available, otherwise percentage
            string rightText;
            if (msd.HasValue && msd.Value > 0)
            {
                rightText = msdName != null ? $"{msdName} {msd:F1}" : $"MSD {msd:F1}";
            }
            else
            {
                rightText = pattern.PercentageDisplay;
            }

            InternalChildren = new Drawable[]
            {
                // Background
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(35, 33, 43, 255),
                    Alpha = 0.4f
                },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = 2, Right = 4 },
                    ColumnDimensions = new[]
                    {
                        new Dimension(GridSizeMode.Relative, 0.6f),  // Type @ BPM
                        new Dimension(GridSizeMode.Relative, 0.4f)   // MSD or Percentage
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            // Left: Type @ BPM with color indicator
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(4, 0),
                                Children = new Drawable[]
                                {
                                    // Color indicator bar
                                    new Container
                                    {
                                        Size = new Vector2(3, 14),
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Masking = true,
                                        CornerRadius = 1,
                                        Child = new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Colour = color
                                        }
                                    },
                                    // Pattern name
                                    new SpriteText
                                    {
                                        Text = pattern.ShortName,
                                        Font = new FontUsage("", 16),
                                        Colour = color,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft
                                    },
                                    // @ BPM (if has BPM)
                                    new SpriteText
                                    {
                                        Text = pattern.Bpm > 0 ? $"@ {pattern.Bpm:F0}" : "",
                                        Font = new FontUsage("", 15),
                                        Colour = new Color4(200, 200, 200, 255),
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft
                                    }
                                }
                            },
                            // Right: MSD or Percentage
                            new SpriteText
                            {
                                Text = rightText,
                                Font = new FontUsage("", 15),
                                Colour = secondaryColor,
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight
                            }
                        }
                    }
                }
            };
        }
    }

    /// <summary>
    /// A container that shows/hides detail text on hover.
    /// </summary>
    private partial class HoverableClassifierBox : Container
    {
        /// <summary>
        /// The detail text to show/hide on hover.
        /// </summary>
        public SpriteText? DetailText { get; set; }

        protected override bool OnHover(HoverEvent e)
        {
            DetailText?.FadeTo(1, 150);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            DetailText?.FadeTo(0, 150);
            base.OnHoverLost(e);
        }
    }
}
