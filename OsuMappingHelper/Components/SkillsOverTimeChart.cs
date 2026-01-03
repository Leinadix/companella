using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Models;

namespace OsuMappingHelper.Components;

/// <summary>
/// Chart displaying skill trends over time across multiple sessions.
/// Shows MSD trends per skillset + accuracy overlay with phase shift indicators.
/// </summary>
public partial class SkillsOverTimeChart : CompositeDrawable
{
    private const float ChartPaddingLeft = 45f;
    private const float ChartPaddingRight = 45f;
    private const float ChartPaddingTop = 25f;
    private const float ChartPaddingBottom = 30f;
    private const float PointRadius = 3f;
    private const float LineThickness = 2f;

    private const float AccuracyMin = 80f;
    private const float AccuracyMax = 100f;
    private const float DefaultMsdMin = 0f;
    private const float DefaultMsdMax = 40f;

    /// <summary>
    /// Skillset colors matching MsdChart.
    /// </summary>
    public static readonly Dictionary<string, Color4> SkillsetColors = new()
    {
        { "overall", new Color4(200, 200, 200, 255) },
        { "stream", new Color4(100, 180, 255, 255) },
        { "jumpstream", new Color4(100, 220, 100, 255) },
        { "handstream", new Color4(255, 180, 100, 255) },
        { "stamina", new Color4(180, 100, 255, 255) },
        { "jackspeed", new Color4(255, 100, 100, 255) },
        { "chordjack", new Color4(255, 220, 100, 255) },
        { "technical", new Color4(100, 220, 220, 255) },
        { "unknown", new Color4(150, 150, 150, 255) }
    };

    private Container _chartArea = null!;
    private Container _pointsContainer = null!;
    private Container _linesContainer = null!;
    private Container _accuracyPointsContainer = null!;
    private Container _accuracyLinesContainer = null!;
    private Container _phaseShiftsContainer = null!;
    private Container _gridContainer = null!;
    private Container _labelsContainer = null!;
    private Container _legendContainer = null!;
    private SpriteText _titleText = null!;
    private SpriteText _noDataText = null!;

    private SkillsTrendResult? _trendData;
    private float _msdMin = DefaultMsdMin;
    private float _msdMax = DefaultMsdMax;
    private DateTime _startTime;
    private DateTime _endTime;

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            // Background
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(30, 30, 35, 255)
            },
            // Title
            _titleText = new SpriteText
            {
                Text = "Skills Over Time",
                Font = new FontUsage("", 17, "Bold"),
                Colour = new Color4(255, 102, 170, 255),
                Position = new Vector2(10, 5)
            },
            // No data text
            _noDataText = new SpriteText
            {
                Text = "No data for selected period",
                Font = new FontUsage("", 16),
                Colour = new Color4(120, 120, 120, 255),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Alpha = 1
            },
            // Legend container
            _legendContainer = new Container
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(-10, 5),
                Alpha = 0
            },
            // Chart area container
            _chartArea = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding
                {
                    Left = ChartPaddingLeft,
                    Right = ChartPaddingRight,
                    Top = ChartPaddingTop,
                    Bottom = ChartPaddingBottom
                },
                Alpha = 0,
                Children = new Drawable[]
                {
                    _gridContainer = new Container { RelativeSizeAxes = Axes.Both },
                    _phaseShiftsContainer = new Container { RelativeSizeAxes = Axes.Both },
                    _linesContainer = new Container { RelativeSizeAxes = Axes.Both },
                    _accuracyLinesContainer = new Container { RelativeSizeAxes = Axes.Both },
                    _pointsContainer = new Container { RelativeSizeAxes = Axes.Both },
                    _accuracyPointsContainer = new Container { RelativeSizeAxes = Axes.Both }
                }
            },
            // Labels container
            _labelsContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0
            }
        };
    }

    /// <summary>
    /// Sets the trend data to display.
    /// </summary>
    public void SetTrendData(SkillsTrendResult? data)
    {
        _trendData = data;
        Redraw();
    }

    /// <summary>
    /// Clears the chart.
    /// </summary>
    public void Clear()
    {
        _trendData = null;
        Redraw();
    }

    private void Redraw()
    {
        _pointsContainer.Clear();
        _linesContainer.Clear();
        _accuracyPointsContainer.Clear();
        _accuracyLinesContainer.Clear();
        _phaseShiftsContainer.Clear();
        _gridContainer.Clear();
        _labelsContainer.Clear();
        _legendContainer.Clear();

        if (_trendData == null || _trendData.Plays.Count == 0)
        {
            _noDataText.FadeTo(1, 200);
            _chartArea.FadeTo(0, 200);
            _labelsContainer.FadeTo(0, 200);
            _legendContainer.FadeTo(0, 200);
            return;
        }

        _noDataText.FadeTo(0, 200);
        _chartArea.FadeTo(1, 200);
        _labelsContainer.FadeTo(1, 200);
        _legendContainer.FadeTo(1, 200);

        CalculateRanges();
        DrawGrid();
        DrawAxisLabels();
        DrawLegend();
        DrawPhaseShifts();
        DrawMsdData();
        DrawAccuracyData();

        // Update title
        var duration = _endTime - _startTime;
        var durationStr = duration.TotalDays > 30 
            ? $"{duration.TotalDays / 30:F1} months"
            : $"{duration.TotalDays:F0} days";
        _titleText.Text = $"Skills Over Time ({_trendData.Plays.Count} plays, {durationStr})";
    }

    private void CalculateRanges()
    {
        if (_trendData == null || _trendData.Plays.Count == 0)
        {
            _msdMin = DefaultMsdMin;
            _msdMax = DefaultMsdMax;
            _startTime = DateTime.UtcNow.AddDays(-7);
            _endTime = DateTime.UtcNow;
            return;
        }

        var maxMsd = _trendData.Plays.Max(p => p.HighestMsdValue);
        var minMsd = _trendData.Plays.Min(p => p.HighestMsdValue);

        var msdRange = maxMsd - minMsd;
        if (msdRange < 5) msdRange = 5;

        _msdMin = Math.Max(0, minMsd - msdRange * 0.1f);
        _msdMax = maxMsd + msdRange * 0.1f;

        if (_msdMax - _msdMin < 5)
        {
            _msdMax = _msdMin + 5;
        }

        _startTime = _trendData.StartTime;
        _endTime = _trendData.EndTime;

        if (_endTime <= _startTime)
        {
            _endTime = _startTime.AddDays(1);
        }
    }

    private void DrawGrid()
    {
        // Horizontal grid lines
        for (int i = 0; i <= 4; i++)
        {
            float y = i / 4f;
            _gridContainer.Add(new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 1,
                RelativePositionAxes = Axes.Y,
                Y = y,
                Colour = new Color4(50, 50, 55, 255)
            });
        }

        // Vertical grid lines (time-based)
        int timeLines = 4;
        for (int i = 0; i <= timeLines; i++)
        {
            float x = i / (float)timeLines;
            _gridContainer.Add(new Box
            {
                RelativeSizeAxes = Axes.Y,
                Width = 1,
                RelativePositionAxes = Axes.X,
                X = x,
                Colour = new Color4(50, 50, 55, 255)
            });
        }
    }

    private void DrawAxisLabels()
    {
        // MSD axis labels (left side)
        for (int i = 0; i <= 4; i++)
        {
            float value = _msdMin + (_msdMax - _msdMin) * (1 - i / 4f);
            float y = ChartPaddingTop + (DrawHeight - ChartPaddingTop - ChartPaddingBottom) * (i / 4f);

            _labelsContainer.Add(new SpriteText
            {
                Text = value.ToString("F1"),
                Font = new FontUsage("", 16),
                Colour = new Color4(150, 150, 150, 255),
                Position = new Vector2(5, y - 5),
                Origin = Anchor.CentreLeft
            });
        }

        // Accuracy axis labels (right side)
        for (int i = 0; i <= 4; i++)
        {
            float value = AccuracyMax - (AccuracyMax - AccuracyMin) * (i / 4f);
            float y = ChartPaddingTop + (DrawHeight - ChartPaddingTop - ChartPaddingBottom) * (i / 4f);

            _labelsContainer.Add(new SpriteText
            {
                Text = $"{value:F0}%",
                Font = new FontUsage("", 16),
                Colour = Color4.White,
                Position = new Vector2(DrawWidth - 5, y - 5),
                Origin = Anchor.CentreRight
            });
        }

        // Time axis labels (bottom)
        int timeLines = 4;
        var timeRange = _endTime - _startTime;
        
        for (int i = 0; i <= timeLines; i++)
        {
            var time = _startTime.AddTicks((long)(timeRange.Ticks * (i / (float)timeLines)));
            float x = ChartPaddingLeft + (DrawWidth - ChartPaddingLeft - ChartPaddingRight) * (i / (float)timeLines);

            var format = timeRange.TotalDays > 30 ? "MMM dd" : "MMM dd HH:mm";

            _labelsContainer.Add(new SpriteText
            {
                Text = time.ToLocalTime().ToString(format),
                Font = new FontUsage("", 12),
                Colour = new Color4(150, 150, 150, 255),
                Position = new Vector2(x, DrawHeight - 10),
                Origin = Anchor.TopCentre
            });
        }

        // Axis titles
        _labelsContainer.Add(new SpriteText
        {
            Text = "MSD",
            Font = new FontUsage("", 13, "Bold"),
            Colour = new Color4(255, 102, 170, 255),
            Position = new Vector2(5, ChartPaddingTop - 15),
            Origin = Anchor.BottomLeft
        });

        _labelsContainer.Add(new SpriteText
        {
            Text = "Acc",
            Font = new FontUsage("", 13, "Bold"),
            Colour = Color4.White,
            Position = new Vector2(DrawWidth - 5, ChartPaddingTop - 15),
            Origin = Anchor.BottomRight
        });
    }

    private void DrawLegend()
    {
        if (_trendData == null) return;

        // Get skillsets that appear in the data
        var skillsetsInData = _trendData.Plays
            .Select(p => p.DominantSkillset.ToLowerInvariant())
            .Distinct()
            .Where(s => SkillsetColors.ContainsKey(s))
            .Take(5) // Limit to 5 to save space
            .ToList();

        var flow = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(8, 0)
        };

        foreach (var skillset in skillsetsInData)
        {
            var color = SkillsetColors.GetValueOrDefault(skillset, SkillsetColors["unknown"]);
            flow.Add(new LegendItem(skillset, color));
        }

        // Add accuracy legend
        flow.Add(new LegendItem("acc", Color4.White));

        _legendContainer.Add(flow);
    }

    private void DrawPhaseShifts()
    {
        if (_trendData == null) return;

        var timeRange = (_endTime - _startTime).TotalMilliseconds;
        if (timeRange <= 0) return;

        foreach (var shift in _trendData.PhaseShifts)
        {
            var x = (float)((shift.Time - _startTime).TotalMilliseconds / timeRange);
            x = Math.Clamp(x, 0, 1);

            var color = shift.Type switch
            {
                PhaseShiftType.Breakthrough => new Color4(100, 255, 100, 80),
                PhaseShiftType.Plateau => new Color4(255, 255, 100, 80),
                PhaseShiftType.Decline => new Color4(255, 100, 100, 80),
                PhaseShiftType.Recovery => new Color4(100, 200, 255, 80),
                _ => new Color4(150, 150, 150, 80)
            };

            _phaseShiftsContainer.Add(new Box
            {
                RelativeSizeAxes = Axes.Y,
                Width = 3,
                RelativePositionAxes = Axes.X,
                X = x,
                Colour = color,
                Origin = Anchor.TopCentre
            });
        }
    }

    private void DrawMsdData()
    {
        if (_trendData == null || _trendData.Plays.Count == 0) return;

        var timeRange = (_endTime - _startTime).TotalMilliseconds;
        if (timeRange <= 0) return;

        var sortedPlays = _trendData.Plays.OrderBy(p => p.PlayedAt).ToList();
        var pointsBySkillset = new Dictionary<string, List<Vector2>>();

        foreach (var play in sortedPlays)
        {
            var x = (float)((play.PlayedAt - _startTime).TotalMilliseconds / timeRange);
            var y = 1f - (play.HighestMsdValue - _msdMin) / (_msdMax - _msdMin);

            x = Math.Clamp(x, 0, 1);
            y = Math.Clamp(y, 0, 1);

            var skillset = play.DominantSkillset.ToLowerInvariant();
            var color = SkillsetColors.GetValueOrDefault(skillset, SkillsetColors["unknown"]);

            // Draw point
            _pointsContainer.Add(new Circle
            {
                Size = new Vector2(PointRadius * 2),
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(x, y),
                Origin = Anchor.Centre,
                Colour = color
            });

            // Track for line drawing
            if (!pointsBySkillset.ContainsKey(skillset))
                pointsBySkillset[skillset] = new List<Vector2>();
            pointsBySkillset[skillset].Add(new Vector2(x, y));
        }

        // Draw connecting lines per skillset
        foreach (var (skillset, points) in pointsBySkillset)
        {
            if (points.Count < 2) continue;

            var color = SkillsetColors.GetValueOrDefault(skillset, SkillsetColors["unknown"]);
            for (int i = 1; i < points.Count; i++)
            {
                DrawLine(_linesContainer, points[i - 1], points[i], color, LineThickness * 0.7f);
            }
        }
    }

    private void DrawAccuracyData()
    {
        if (_trendData == null || _trendData.Plays.Count == 0) return;

        var timeRange = (_endTime - _startTime).TotalMilliseconds;
        if (timeRange <= 0) return;

        var sortedPlays = _trendData.Plays.OrderBy(p => p.PlayedAt).ToList();
        Vector2? previousPoint = null;

        foreach (var play in sortedPlays)
        {
            var x = (float)((play.PlayedAt - _startTime).TotalMilliseconds / timeRange);
            var normalizedAcc = (float)Math.Clamp(play.Accuracy, AccuracyMin, AccuracyMax);
            var y = 1f - (normalizedAcc - AccuracyMin) / (AccuracyMax - AccuracyMin);

            x = Math.Clamp(x, 0, 1);
            y = Math.Clamp(y, 0, 1);

            // Draw small white point for accuracy
            _accuracyPointsContainer.Add(new Circle
            {
                Size = new Vector2(PointRadius * 1.5f),
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(x, y),
                Origin = Anchor.Centre,
                Colour = new Color4(255, 255, 255, 180)
            });

            var currentPoint = new Vector2(x, y);
            if (previousPoint.HasValue)
            {
                DrawLine(_accuracyLinesContainer, previousPoint.Value, currentPoint, new Color4(255, 255, 255, 120), LineThickness * 0.5f);
            }
            previousPoint = currentPoint;
        }
    }

    private void DrawLine(Container container, Vector2 from, Vector2 to, Color4 color, float thickness)
    {
        container.Add(new SkillsLineSegment(from, to, color, thickness));
    }

    private partial class SkillsLineSegment : CompositeDrawable
    {
        private readonly Vector2 _from;
        private readonly Vector2 _to;
        private readonly Color4 _color;
        private readonly float _thickness;

        public SkillsLineSegment(Vector2 from, Vector2 to, Color4 color, float thickness)
        {
            _from = from;
            _to = to;
            _color = color;
            _thickness = thickness;
            RelativeSizeAxes = Axes.Both;
        }

        protected override void Update()
        {
            base.Update();

            ClearInternal();

            var actualFrom = new Vector2(_from.X * DrawWidth, _from.Y * DrawHeight);
            var actualTo = new Vector2(_to.X * DrawWidth, _to.Y * DrawHeight);

            var diff = actualTo - actualFrom;
            var length = diff.Length;
            var angle = MathF.Atan2(diff.Y, diff.X);

            AddInternal(new Box
            {
                Width = length,
                Height = _thickness,
                Position = actualFrom,
                Origin = Anchor.CentreLeft,
                Rotation = MathHelper.RadiansToDegrees(angle),
                Colour = _color
            });
        }
    }

    private partial class LegendItem : CompositeDrawable
    {
        public LegendItem(string label, Color4 color)
        {
            AutoSizeAxes = Axes.Both;

            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(3, 0),
                Children = new Drawable[]
                {
                    new Circle
                    {
                        Size = new Vector2(8),
                        Colour = color,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft
                    },
                    new SpriteText
                    {
                        Text = label,
                        Font = new FontUsage("", 12),
                        Colour = new Color4(180, 180, 180, 255),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft
                    }
                }
            };
        }
    }
}
