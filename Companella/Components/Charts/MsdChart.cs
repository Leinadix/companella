using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using Companella.Models.Difficulty;

namespace Companella.Components.Charts;

/// <summary>
/// Displays a vertical bar chart of MSD skillset scores.
/// Bars grow from bottom to top, with 40 being the max (fully filled).
/// </summary>
public partial class MsdChart : CompositeDrawable
{
    private const float MaxMsdValue = 40f;

    private FillFlowContainer _barsContainer = null!;
    private SpriteText _titleText = null!;
    private SpriteText _loadingText = null!;
    private SpriteText _errorText = null!;
    private bool _isLoading;

    /// <summary>
    /// Gets whether the chart is currently in loading state.
    /// </summary>
    public bool IsLoading => _isLoading;

    // Skillset colors matching Etterna's color scheme
    private static readonly Dictionary<string, Color4> SkillsetColors = new()
    {
        { "overall", new Color4(200, 200, 200, 255) },      // Gray
        { "stream", new Color4(100, 180, 255, 255) },       // Blue
        { "jumpstream", new Color4(100, 220, 100, 255) },   // Green
        { "handstream", new Color4(255, 180, 100, 255) },   // Orange
        { "stamina", new Color4(180, 100, 255, 255) },      // Purple
        { "jackspeed", new Color4(255, 100, 100, 255) },    // Red
        { "chordjack", new Color4(255, 220, 100, 255) },    // Yellow
        { "technical", new Color4(100, 220, 220, 255) }     // Cyan
    };

    // Short labels for the chart
    private static readonly Dictionary<string, string> SkillsetLabels = new()
    {
        { "overall", "OVR" },
        { "stream", "STR" },
        { "jumpstream", "JS" },
        { "handstream", "HS" },
        { "stamina", "STA" },
        { "jackspeed", "JCK" },
        { "chordjack", "CJ" },
        { "technical", "TEC" }
    };

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            // Title
            _titleText = new SpriteText
            {
                Text = "MSD Analysis",
                Font = new FontUsage("", 19, "Bold"),
                Colour = new Color4(255, 102, 170, 255),
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
                Font = new FontUsage("", 16),
                Colour = new Color4(255, 100, 100, 255),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Alpha = 0
            },
            // Bars container - horizontal flow for vertical bars
            _barsContainer = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Padding = new MarginPadding { Top = 18 },
                Alpha = 0
            }
        };
    }

    /// <summary>
    /// Shows loading state.
    /// </summary>
    public void ShowLoading()
    {
        _isLoading = true;
        _loadingText.FadeTo(1, 100);
        _errorText.FadeTo(0, 100);
        _barsContainer.FadeTo(0, 100);
    }

    /// <summary>
    /// Shows error message.
    /// </summary>
    public void ShowError(string message)
    {
        _isLoading = false;
        _loadingText.FadeTo(0, 100);
        _errorText.Text = message;
        _errorText.FadeTo(1, 100);
        _barsContainer.FadeTo(0, 100);
    }

    /// <summary>
    /// Clears the chart and hides it.
    /// </summary>
    public void Clear()
    {
        _isLoading = false;
        _loadingText.FadeTo(0, 100);
        _errorText.FadeTo(0, 100);
        _barsContainer.FadeTo(0, 100);
        _barsContainer.Clear();
        _titleText.Text = "MSD Analysis";
    }

    /// <summary>
    /// Sets the MSD scores to display.
    /// </summary>
    /// <param name="scores">Skillset scores to display.</param>
    /// <param name="rate">The rate these scores are for (for title display).</param>
    public void SetScores(SkillsetScores scores, float rate = 1.0f)
    {
        _isLoading = false;
        _loadingText.FadeTo(0, 100);
        _errorText.FadeTo(0, 100);

        // Show rate in title if it's not 1.0x (e.g. DT = 1.5x, HT = 0.75x)
        if (Math.Abs(rate - 1.0f) > 0.01f)
        {
            _titleText.Text = $"MSD @ {rate:F2}x";
        }
        else
        {
            _titleText.Text = "";
        }

        _barsContainer.Clear();

        // Get all skillset values
        var skillsets = new (string Name, float Value)[]
        {
            ("overall", scores.Overall),
            ("stream", scores.Stream),
            ("jumpstream", scores.Jumpstream),
            ("handstream", scores.Handstream),
            ("stamina", scores.Stamina),
            ("jackspeed", scores.Jackspeed),
            ("chordjack", scores.Chordjack),
            ("technical", scores.Technical)
        };

        foreach (var (name, value) in skillsets)
        {
            // Height percentage based on MaxMsdValue (40), clamped to 0-1
            var heightPercent = Math.Clamp(value / MaxMsdValue, 0f, 1f);
            var color = SkillsetColors.GetValueOrDefault(name, Color4.Gray);
            var label = SkillsetLabels.GetValueOrDefault(name, name);

            _barsContainer.Add(new VerticalSkillsetBar(label, value, heightPercent, color));
        }

        _barsContainer.FadeTo(1, 200);
    }

    /// <summary>
    /// Sets the MSD result from full analysis.
    /// </summary>
    public void SetMsdResult(MsdResult result)
    {
        // Show 1.0x rate by default
        var scores1x = result.GetScoresForRate(1.0f);
        if (scores1x != null)
        {
            SetScores(scores1x, 1.0f);
        }
        else if (result.Rates.Count > 0)
        {
            SetScores(result.Rates[0].Scores, result.Rates[0].Rate);
        }
        else
        {
            ShowError("No rate data available");
        }
    }

    /// <summary>
    /// Sets the single-rate MSD result.
    /// </summary>
    public void SetSingleRateResult(SingleRateMsdResult result)
    {
        SetScores(result.Scores, result.Rate);
    }

    /// <summary>
    /// Vertical skillset bar component (grows bottom to top).
    /// </summary>
    private partial class VerticalSkillsetBar : CompositeDrawable
    {
        public VerticalSkillsetBar(string label, float value, float heightPercent, Color4 color)
        {
            RelativeSizeAxes = Axes.Y;
            Width = 38;

            InternalChildren = new Drawable[]
            {
                // Bar area (most of the height)
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Bottom = 28 }, // Space for label and value
                    Child = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 4,
                        Children = new Drawable[]
                        {
                            // Background
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(40, 40, 50, 255)
                            },
                            // Filled bar (anchored at bottom, grows up)
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Height = heightPercent,
                                Anchor = Anchor.BottomLeft,
                                Origin = Anchor.BottomLeft,
                                Colour = color
                            }
                        }
                    }
                },
                // Value text (above label)
                new SpriteText
                {
                    Text = value.ToString("F1"),
                    Font = new FontUsage("", 15),
                    Colour = color,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Y = -14
                },
                // Label at bottom
                new SpriteText
                {
                    Text = label,
                    Font = new FontUsage("", 15),
                    Colour = new Color4(180, 180, 180, 255),
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre
                }
            };
        }
    }
}
