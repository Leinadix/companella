using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Models;

namespace OsuMappingHelper.Components;

/// <summary>
/// Hit window timing system types.
/// </summary>
public enum HitWindowSystemType
{
    /// <summary>
    /// osu!mania timing windows (OD-based).
    /// </summary>
    OsuMania,
    
    /// <summary>
    /// StepMania/Etterna judge timing windows (J1-J8, Justice).
    /// </summary>
    StepMania
}

/// <summary>
/// Displays a timeline graph of timing deviations across a map.
/// X-axis: Song time (0 to map duration)
/// Y-axis: Deviation in milliseconds (negative=early, positive=late)
/// Supports column/key filtering and multiple hit window systems.
/// </summary>
public partial class TimingDeviationChart : CompositeDrawable
{
    private const float ChartPaddingLeft = 200f;  // Increased for distribution chart
    private const float ChartPaddingRight = 20f;
    private const float ChartPaddingTop = 50f;
    private const float ChartPaddingBottom = 50f;
    private const float DistributionChartWidth = 130f;
    private const float PointRadius = 3f;
    
    // Deviation range in ms (will be auto-scaled)
    private const float DefaultDeviationRange = 100f;
    
    // Colors for different judgements
    private static readonly Color4 ColorMax300 = new Color4(200, 200, 255, 255);   // Light blue (rainbow)
    private static readonly Color4 Color300 = new Color4(100, 180, 255, 255);      // Blue
    private static readonly Color4 Color200 = new Color4(100, 220, 100, 255);      // Green
    private static readonly Color4 Color100 = new Color4(255, 220, 100, 255);      // Yellow
    private static readonly Color4 Color50 = new Color4(255, 150, 80, 255);        // Orange
    private static readonly Color4 ColorMiss = new Color4(255, 80, 80, 255);       // Red
    
    // Colors for columns/keys (for key indicators)
    private static readonly Color4[] ColumnColors = new Color4[]
    {
        new Color4(255, 100, 100, 255),  // Column 0 - Red
        new Color4(100, 200, 255, 255),  // Column 1 - Cyan
        new Color4(255, 220, 100, 255),  // Column 2 - Yellow
        new Color4(100, 255, 150, 255),  // Column 3 - Green
        new Color4(200, 150, 255, 255),  // Column 4 - Purple
        new Color4(255, 180, 100, 255),  // Column 5 - Orange
        new Color4(150, 220, 255, 255),  // Column 6 - Light Blue
    };
    
    private Container _chartArea = null!;
    private Container _pointsContainer = null!;
    private Container _gridContainer = null!;
    private Container _labelsContainer = null!;
    private Container _keyIndicatorsContainer = null!;
    private Container _columnStatsContainer = null!;
    private Container _distributionContainer = null!;
    private SpriteText _titleText = null!;
    private SpriteText _statsText = null!;
    private SpriteText _noDataText = null!;
    private SpriteText _hitWindowText = null!;
    private SpriteText _accuracyText = null!;
    private Box _zeroLine = null!;
    
    private TimingAnalysisResult? _data;
    private float _deviationMax = DefaultDeviationRange;
    
    // Column/key filtering
    private bool[] _activeColumns = new bool[7];
    private int _keyCount = 4;
    
    // Hit window system
    private HitWindowSystemType _hitWindowSystemType = HitWindowSystemType.OsuMania;
    private int _maniaOD = 8; // osu!mania OD (0-10)
    private int _etternaJudge = 4; // Etterna Wife judge (1-9, with Justice as special)
    private bool _isManiaV2 = false; // osu!mania v1 vs v2 scoring
    
    /// <summary>
    /// Gets the current hit window system type.
    /// </summary>
    public HitWindowSystemType SystemType => _hitWindowSystemType;
    
    /// <summary>
    /// Gets the current osu!mania OD level (0-10).
    /// </summary>
    public int ManiaOD => _maniaOD;
    
    /// <summary>
    /// Gets the current Etterna Wife judge level (1-8, 9=Justice).
    /// </summary>
    public int EtternaJudge => _etternaJudge;
    
    /// <summary>
    /// Gets the number of keys/columns in the current data.
    /// </summary>
    public int KeyCount => _keyCount;
    
    /// <summary>
    /// Event raised when column filter changes.
    /// </summary>
    public event Action? ColumnFilterChanged;
    
    /// <summary>
    /// Event raised when hit window system changes.
    /// </summary>
    public event Action? HitWindowSystemChanged;
    
    /// <summary>
    /// Event raised when a full re-analysis is requested (e.g., when OD changes).
    /// Parameters: OD value (for osu!mania) or judge level (for Etterna)
    /// </summary>
    public event Action<double>? ReanalysisRequested;
    
    [BackgroundDependencyLoader]
    private void load()
    {
        // Initialize all columns as active
        for (int i = 0; i < _activeColumns.Length; i++)
            _activeColumns[i] = true;
        
        InternalChildren = new Drawable[]
        {
            // Semi-transparent background
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(20, 20, 25, 220)
            },
            // Title
            _titleText = new SpriteText
            {
                Text = "Timing Deviation Analysis",
                Font = new FontUsage("", 20, "Bold"),
                Colour = new Color4(255, 102, 170, 255),
                Position = new Vector2(12, 0)
            },
            // Hit window system indicator
            _hitWindowText = new SpriteText
            {
                Text = "[osu!mania]",
                Font = new FontUsage("", 12),
                Colour = new Color4(150, 150, 160, 255),
                Position = new Vector2(12, 20)
            },
            // Statistics text
            _statsText = new SpriteText
            {
                Text = "",
                Font = new FontUsage("", 14),
                Colour = new Color4(200, 200, 200, 255),
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-12, 10)
            },
            // Key indicators container (bottom)
            _keyIndicatorsContainer = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 30,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Padding = new MarginPadding { Left = ChartPaddingLeft, Right = ChartPaddingRight, Bottom = 8 }
            },
            // Column stats container (right side)
            _columnStatsContainer = new Container
            {
                Width = 120,
                RelativeSizeAxes = Axes.Y,
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Padding = new MarginPadding { Top = ChartPaddingTop, Bottom = ChartPaddingBottom, Right = 8 },
                Alpha = 0
            },
            // Distribution bar chart container (left side)
            _distributionContainer = new Container
            {
                Width = DistributionChartWidth,
                RelativeSizeAxes = Axes.Y,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(55, 0),
                Padding = new MarginPadding { Top = ChartPaddingTop, Bottom = ChartPaddingBottom },
                Alpha = 0
            },
            // Accuracy text (below title)
            _accuracyText = new SpriteText
            {
                Text = "",
                Font = new FontUsage("", 14, "Bold"),
                Colour = new Color4(100, 220, 100, 255),
                Position = new Vector2(55, 200)
            },
            // No data text
            _noDataText = new SpriteText
            {
                Text = "No timing data available",
                Font = new FontUsage("", 16),
                Colour = new Color4(120, 120, 120, 255),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Alpha = 1
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
                    // Grid lines
                    _gridContainer = new Container
                    {
                        RelativeSizeAxes = Axes.Both
                    },
                    // Zero line (perfect timing)
                    _zeroLine = new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 2,
                        RelativePositionAxes = Axes.Y,
                        Y = 0.5f, // Center (zero deviation)
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.CentreLeft,
                        Colour = new Color4(255, 255, 255, 180)
                    },
                    // Data points
                    _pointsContainer = new Container
                    {
                        RelativeSizeAxes = Axes.Both
                    }
                }
            },
            // Labels container (outside chart area)
            _labelsContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0
            }
        };
    }
    
    /// <summary>
    /// Sets the timing analysis data to display.
    /// </summary>
    public void SetData(TimingAnalysisResult data)
    {
        _data = data;
        
        // Detect key count from max column index
        if (_data != null && _data.Deviations.Count > 0)
        {
            _keyCount = _data.Deviations.Max(d => d.Column) + 1;
            _keyCount = Math.Clamp(_keyCount, 4, 7);
            
            // Initialize OD from beatmap
            _maniaOD = (int)Math.Round(_data.OverallDifficulty);
            _maniaOD = Math.Clamp(_maniaOD, 0, 10);
        }
        else
        {
            _keyCount = 4;
        }
        
        // Enable all columns for new data
        for (int i = 0; i < _activeColumns.Length; i++)
            _activeColumns[i] = true;
        
        Redraw();
    }
    
    /// <summary>
    /// Clears all data from the chart.
    /// </summary>
    public void Clear()
    {
        _data = null;
        Redraw();
    }
    
    /// <summary>
    /// Toggles the visibility of a column/key.
    /// </summary>
    /// <param name="column">Column index (0-based).</param>
    public void ToggleColumn(int column)
    {
        if (column < 0 || column >= _keyCount) return;
        
        _activeColumns[column] = !_activeColumns[column];
        Console.WriteLine($"[Chart] Column {column + 1} {(_activeColumns[column] ? "enabled" : "disabled")}");
        
        ColumnFilterChanged?.Invoke();
        Redraw();
    }
    
    /// <summary>
    /// Checks if a column is currently active/visible.
    /// </summary>
    public bool IsColumnActive(int column)
    {
        if (column < 0 || column >= _activeColumns.Length) return false;
        return _activeColumns[column];
    }
    
    /// <summary>
    /// Gets the filtered deviations based on active columns.
    /// </summary>
    private List<TimingDeviation> GetFilteredDeviations()
    {
        if (_data == null) return new List<TimingDeviation>();
        
        return _data.Deviations
            .Where(d => d.Column >= 0 && d.Column < _activeColumns.Length && _activeColumns[d.Column])
            .ToList();
    }
    
    /// <summary>
    /// Toggles between osu!mania v1, v2, and StepMania hit window systems.
    /// Cycle: v1 -> v2 -> StepMania -> v1
    /// </summary>
    public void ToggleSystemType()
    {
        if (_hitWindowSystemType == HitWindowSystemType.OsuMania)
        {
            if (!_isManiaV2)
            {
                // v1 -> v2
                _isManiaV2 = true;
            }
            else
            {
                // v2 -> StepMania
                _hitWindowSystemType = HitWindowSystemType.StepMania;
                _isManiaV2 = false;
            }
        }
        else
        {
            // StepMania -> v1
            _hitWindowSystemType = HitWindowSystemType.OsuMania;
            _isManiaV2 = false;
        }
        
        Console.WriteLine($"[Chart] Switched to {GetHitWindowSystemName()}");
        RecalculateJudgements();
        HitWindowSystemChanged?.Invoke();
        Redraw();
    }
    
    /// <summary>
    /// Adjusts the current level (OD or Judge) by the given delta.
    /// </summary>
    /// <param name="delta">Direction to adjust: -1 for left/decrease, +1 for right/increase.</param>
    public void AdjustLevel(int delta)
    {
        if (_hitWindowSystemType == HitWindowSystemType.OsuMania)
        {
            _maniaOD = Math.Clamp(_maniaOD + delta, 0, 10);
            Console.WriteLine($"[Chart] OD set to {_maniaOD}, requesting full re-analysis");
            
            // Request full re-analysis with new OD (if handler is registered)
            if (ReanalysisRequested != null)
            {
                ReanalysisRequested.Invoke(_maniaOD);
                HitWindowSystemChanged?.Invoke();
                // Data will be updated by the handler calling SetData again
                return;
            }
            
            // Fallback to local recalculation if no handler
            Console.WriteLine($"[Chart] No re-analysis handler, using local recalculation");
        }
        else
        {
            // Etterna Wife: 1-8 and 9=Justice - use local recalculation
            _etternaJudge = Math.Clamp(_etternaJudge + delta, 1, 9);
            Console.WriteLine($"[Chart] Judge set to {GetEtternaJudgeName(_etternaJudge)}");
        }
        
        RecalculateJudgements();
        HitWindowSystemChanged?.Invoke();
        Redraw();
    }
    
    /// <summary>
    /// Gets the display name for StepMania/Etterna judge level.
    /// </summary>
    private static string GetEtternaJudgeName(int judge)
    {
        return judge switch
        {
            1 => "J1",
            2 => "J2",
            3 => "J3",
            4 => "J4",
            5 => "J5",
            6 => "J6",
            7 => "J7",
            8 => "J8",
            9 => "Justice",
            _ => $"J{judge}"
        };
    }
    
    /// <summary>
    /// Recalculates judgements based on current hit window settings.
    /// </summary>
    private void RecalculateJudgements()
    {
        if (_data == null) return;
        
        foreach (var deviation in _data.Deviations)
        {
            // Don't recalculate judgements for notes that were never hit
            // (notes where no keypress was matched at all - these are always Miss)
            if (deviation.WasNeverHit)
            {
                deviation.Judgement = ManiaJudgement.Miss;
                continue;
            }
            
            if (_hitWindowSystemType == HitWindowSystemType.OsuMania)
            {
                // For LNs with stored tail deviation, use proper combined calculation
                if (deviation.IsLongNote && deviation.TailDeviation.HasValue)
                {
                    deviation.Judgement = TimingDeviation.GetLNJudgementFromDeviations(
                        deviation.Deviation,
                        deviation.TailDeviation.Value,
                        _maniaOD
                    );
                }
                else
                {
                    // Regular note
                    double absDeviation = Math.Abs(deviation.Deviation);
                    deviation.Judgement = GetManiaJudgement(absDeviation, _maniaOD);
                }
            }
            else
            {
                // Etterna/StepMania - use simple deviation (no LN distinction)
                double absDeviation = Math.Abs(deviation.Deviation);
                deviation.Judgement = GetEtternaJudgement(absDeviation, _etternaJudge);
            }
        }
        
        _data.CalculateStatistics();
    }
    
    /// <summary>
    /// Gets judgement for osu!mania timing windows at a specific OD.
    /// </summary>
    private static ManiaJudgement GetManiaJudgement(double absDeviation, int od)
    {
        // osu!mania timing windows formula:
        // MAX/300g: 16ms (fixed)
        // 300: 64 - 3 * OD
        // 200: 97 - 3 * OD
        // 100: 127 - 3 * OD
        // 50: 151 - 3 * OD
        // Miss: 188 - 3 * OD
        
        double window300g = 16;
        double window300 = 64 - 3 * od;
        double window200 = 97 - 3 * od;
        double window100 = 127 - 3 * od;
        double window50 = 151 - 3 * od;
        
        if (absDeviation <= window300g) return ManiaJudgement.Max300;
        if (absDeviation <= window300) return ManiaJudgement.Hit300;
        if (absDeviation <= window200) return ManiaJudgement.Hit200;
        if (absDeviation <= window100) return ManiaJudgement.Hit100;
        if (absDeviation <= window50) return ManiaJudgement.Hit50;
        return ManiaJudgement.Miss;
    }
    
    /// <summary>
    /// Gets judgement for Etterna/StepMania timing windows at a specific judge level.
    /// Uses exact Wife judge timing windows.
    /// </summary>
    private static ManiaJudgement GetEtternaJudgement(double absDeviation, int judge)
    {
        // StepMania/Etterna timing windows by judge level (in ms)
        // Values from official StepMania documentation
        double marvelous, perfect, great, good, boo;
        
        switch (judge)
        {
            case 1: // Judge 1 (most lenient)
                marvelous = 33; perfect = 68; great = 135; good = 203; boo = 270;
                break;
            case 2: // Judge 2
                marvelous = 29; perfect = 60; great = 120; good = 180; boo = 239;
                break;
            case 3: // Judge 3
                marvelous = 26; perfect = 52; great = 104; good = 157; boo = 209;
                break;
            case 4: // Judge 4 (default)
                marvelous = 22; perfect = 45; great = 90; good = 135; boo = 180;
                break;
            case 5: // Judge 5
                marvelous = 18; perfect = 38; great = 76; good = 113; boo = 151;
                break;
            case 6: // Judge 6
                marvelous = 15; perfect = 30; great = 59; good = 89; boo = 119;
                break;
            case 7: // Judge 7
                marvelous = 11; perfect = 23; great = 45; good = 68; boo = 90;
                break;
            case 8: // Judge 8
                marvelous = 7; perfect = 15; great = 30; good = 45; boo = 59;
                break;
            case 9: // Justice (hardest)
                marvelous = 4; perfect = 9; great = 18; good = 27; boo = 36;
                break;
            default: // Default to Judge 4
                marvelous = 22; perfect = 45; great = 90; good = 135; boo = 180;
                break;
        }
        
        if (absDeviation <= marvelous) return ManiaJudgement.Max300;  // Marvelous
        if (absDeviation <= perfect) return ManiaJudgement.Hit300;   // Perfect
        if (absDeviation <= great) return ManiaJudgement.Hit200;     // Great
        if (absDeviation <= good) return ManiaJudgement.Hit100;      // Good
        if (absDeviation <= boo) return ManiaJudgement.Hit50;        // Boo
        return ManiaJudgement.Miss;
    }
    
    /// <summary>
    /// Redraws the entire chart.
    /// </summary>
    private void Redraw()
    {
        _pointsContainer.Clear();
        _gridContainer.Clear();
        _labelsContainer.Clear();
        _keyIndicatorsContainer.Clear();
        _columnStatsContainer.Clear();
        _distributionContainer.Clear();
        
        if (_data == null || !_data.Success || _data.Deviations.Count == 0)
        {
            _noDataText.FadeTo(1, 200);
            _chartArea.FadeTo(0, 200);
            _labelsContainer.FadeTo(0, 200);
            _columnStatsContainer.FadeTo(0, 200);
            _distributionContainer.FadeTo(0, 200);
            _statsText.Text = "";
            _hitWindowText.Text = "";
            _accuracyText.Text = "";
            return;
        }
        
        _noDataText.FadeTo(0, 200);
        _chartArea.FadeTo(1, 200);
        _labelsContainer.FadeTo(1, 200);
        _columnStatsContainer.FadeTo(1, 200);
        _distributionContainer.FadeTo(1, 200);
        
        // Update hit window indicator
        _hitWindowText.Text = $"[{GetHitWindowSystemName()}] A/D or Arrows to adjust, Tab to switch system";
        
        // Calculate deviation range (auto-scale)
        CalculateRange();
        
        // Update stats text
        UpdateStatsText();
        
        // Draw distribution chart (left side)
        DrawDistributionChart();
        
        // Draw grid and axes
        DrawGrid();
        DrawAxisLabels();
        
        // Draw key indicators
        DrawKeyIndicators();
        
        // Draw column stats
        DrawColumnStats();
        
        // Draw data points
        DrawDataPoints();
    }
    
    /// <summary>
    /// Gets the display name for the current hit window system with level.
    /// </summary>
    public string GetHitWindowSystemName()
    {
        if (_hitWindowSystemType == HitWindowSystemType.OsuMania)
        {
            string version = _isManiaV2 ? "v2" : "v1";
            return $"osu!mania {version} OD{_maniaOD}";
        }
        else
        {
            return $"StepMania {GetEtternaJudgeName(_etternaJudge)}";
        }
    }
    
    /// <summary>
    /// Calculates the deviation range for Y-axis scaling based on the miss window of the current hit window system.
    /// </summary>
    private void CalculateRange()
    {
        // Scale to the miss window of the current hit window system
        _deviationMax = GetMissWindow();
    }
    
    /// <summary>
    /// Gets the miss window (boo/50 window) for the current hit window system.
    /// </summary>
    private float GetMissWindow()
    {
        if (_hitWindowSystemType == HitWindowSystemType.OsuMania)
        {
            // osu!mania: 50 window = 151 - 3 * OD
            return 151f - 3f * _maniaOD;
        }
        else
        {
            // StepMania: Boo window by judge level
            return _etternaJudge switch
            {
                1 => 270f,  // J1
                2 => 239f,  // J2
                3 => 209f,  // J3
                4 => 180f,  // J4
                5 => 151f,  // J5
                6 => 119f,  // J6
                7 => 90f,   // J7
                8 => 59f,   // J8
                9 => 36f,   // Justice
                _ => 180f   // Default to J4
            };
        }
    }
    
    /// <summary>
    /// Gets the timing windows for the current hit window system.
    /// Returns (marvelous, perfect, great, good, boo) windows.
    /// </summary>
    private (float marvelous, float perfect, float great, float good, float boo) GetCurrentTimingWindows()
    {
        if (_hitWindowSystemType == HitWindowSystemType.OsuMania)
        {
            // osu!mania timing windows
            return (
                marvelous: 16f,
                perfect: 64f - 3f * _maniaOD,
                great: 97f - 3f * _maniaOD,
                good: 127f - 3f * _maniaOD,
                boo: 151f - 3f * _maniaOD
            );
        }
        else
        {
            // StepMania timing windows by judge level
            return _etternaJudge switch
            {
                1 => (33f, 68f, 135f, 203f, 270f),
                2 => (29f, 60f, 120f, 180f, 239f),
                3 => (26f, 52f, 104f, 157f, 209f),
                4 => (22f, 45f, 90f, 135f, 180f),
                5 => (18f, 38f, 76f, 113f, 151f),
                6 => (15f, 30f, 59f, 89f, 119f),
                7 => (11f, 23f, 45f, 68f, 90f),
                8 => (7f, 15f, 30f, 45f, 59f),
                9 => (4f, 9f, 18f, 27f, 36f),
                _ => (22f, 45f, 90f, 135f, 180f)
            };
        }
    }
    
    /// <summary>
    /// Draws the judgement distribution bar chart on the left side.
    /// </summary>
    private void DrawDistributionChart()
    {
        if (_data == null) return;
        
        var filtered = GetFilteredDeviations();
        if (filtered.Count == 0) return;
        
        // Count judgements
        var counts = new Dictionary<ManiaJudgement, int>
        {
            { ManiaJudgement.Max300, 0 },
            { ManiaJudgement.Hit300, 0 },
            { ManiaJudgement.Hit200, 0 },
            { ManiaJudgement.Hit100, 0 },
            { ManiaJudgement.Hit50, 0 },
            { ManiaJudgement.Miss, 0 }
        };
        
        foreach (var deviation in filtered)
        {
            counts[deviation.Judgement]++;
        }
        
        int totalHits = filtered.Count;
        int maxCount = counts.Values.Max();
        if (maxCount == 0) maxCount = 1;
        
        // Calculate accuracy based on current system
        double accuracy = CalculateAccuracy(counts, totalHits);
        _accuracyText.Text = $"Accuracy: {accuracy:F2}%";
        _accuracyText.Colour = GetAccuracyColor(accuracy);
        
        // Draw header
        _distributionContainer.Add(new SpriteText
        {
            Text = "Distribution",
            Font = new FontUsage("", 12, "Bold"),
            Colour = new Color4(180, 180, 190, 255),
            Position = new Vector2(0, -15)
        });
        
        // Draw bars
        var judgements = new[]
        {
            (ManiaJudgement.Max300, "MAX", ColorMax300),
            (ManiaJudgement.Hit300, "300", Color300),
            (ManiaJudgement.Hit200, "200", Color200),
            (ManiaJudgement.Hit100, "100", Color100),
            (ManiaJudgement.Hit50, "50", Color50),
            (ManiaJudgement.Miss, "Miss", ColorMiss)
        };
        
        float barHeight = 18f;
        float barSpacing = 4f;
        float labelWidth = 35f;
        float countWidth = 40f;
        float maxBarWidth = DistributionChartWidth - labelWidth - countWidth - 10f;
        float yOffset = 5f;
        
        foreach (var (judgement, label, color) in judgements)
        {
            int count = counts[judgement];
            float barWidth = maxCount > 0 ? (count / (float)maxCount) * maxBarWidth : 0;
            float percentage = totalHits > 0 ? (count / (float)totalHits) * 100f : 0;
            
            // Label
            _distributionContainer.Add(new SpriteText
            {
                Text = label,
                Font = new FontUsage("", 11),
                Colour = color,
                Position = new Vector2(0, yOffset + 2),
                Origin = Anchor.TopLeft
            });
            
            // Bar background
            _distributionContainer.Add(new Box
            {
                Size = new Vector2(maxBarWidth, barHeight - 4),
                Position = new Vector2(labelWidth, yOffset + 2),
                Colour = new Color4(30, 30, 35, 255)
            });
            
            // Bar fill
            if (barWidth > 0)
            {
                _distributionContainer.Add(new Box
                {
                    Size = new Vector2(Math.Max(barWidth, 2), barHeight - 4),
                    Position = new Vector2(labelWidth, yOffset + 2),
                    Colour = new Color4(color.R, color.G, color.B, 200)
                });
            }
            
            // Count/percentage
            _distributionContainer.Add(new SpriteText
            {
                Text = $"{count}",
                Font = new FontUsage("", 10),
                Colour = new Color4(200, 200, 200, 255),
                Position = new Vector2(labelWidth + maxBarWidth + 4, yOffset + 3),
                Origin = Anchor.TopLeft
            });
            
            yOffset += barHeight + barSpacing;
        }
        
        // Draw total
        yOffset += 5f;
        _distributionContainer.Add(new SpriteText
        {
            Text = $"Total: {totalHits}",
            Font = new FontUsage("", 11),
            Colour = new Color4(150, 150, 160, 255),
            Position = new Vector2(0, yOffset)
        });
    }
    
    /// <summary>
    /// Calculates accuracy based on the current hit window system.
    /// </summary>
    private double CalculateAccuracy(Dictionary<ManiaJudgement, int> counts, int totalHits)
    {
        if (totalHits == 0) return 0;
        
        if (_hitWindowSystemType == HitWindowSystemType.OsuMania)
        {
            if (_isManiaV2)
            {
                // osu!mania v2 (ScoreV2) accuracy:
                // MAX (300g) is worth 305, 300 is worth 300
                // Formula: (300g * 305 + 300 * 300 + 200 * 200 + 100 * 100 + 50 * 50) / (total * 305)
                double score = counts[ManiaJudgement.Max300] * 305.0
                             + counts[ManiaJudgement.Hit300] * 300.0
                             + counts[ManiaJudgement.Hit200] * 200.0
                             + counts[ManiaJudgement.Hit100] * 100.0
                             + counts[ManiaJudgement.Hit50] * 50.0;
                return (score / (totalHits * 305.0)) * 100.0;
            }
            else
            {
                // osu!mania v1 accuracy:
                // MAX and 300 are both worth 300 (no distinction)
                // Formula: (MAX * 300 + 300 * 300 + 200 * 200 + 100 * 100 + 50 * 50) / (total * 300)
                double score = counts[ManiaJudgement.Max300] * 300.0
                             + counts[ManiaJudgement.Hit300] * 300.0
                             + counts[ManiaJudgement.Hit200] * 200.0
                             + counts[ManiaJudgement.Hit100] * 100.0
                             + counts[ManiaJudgement.Hit50] * 50.0;
                return (score / (totalHits * 300.0)) * 100.0;
            }
        }
        else
        {
            // Wife scoring (Etterna/StepMania)
            // Wife3 scoring: each judgement has a specific wife points value
            // Marvelous = 100%, Perfect = ~99.3%, Great = ~66%, Good = ~33%, Boo = ~0%, Miss = -10%
            // Simplified version:
            double wifeScore = counts[ManiaJudgement.Max300] * 100.0   // Marvelous
                             + counts[ManiaJudgement.Hit300] * 99.0    // Perfect
                             + counts[ManiaJudgement.Hit200] * 66.0    // Great
                             + counts[ManiaJudgement.Hit100] * 33.0    // Good
                             + counts[ManiaJudgement.Hit50] * 0.0      // Boo
                             + counts[ManiaJudgement.Miss] * -10.0;    // Miss (penalty)
            
            // Normalize to 0-100 scale
            double maxScore = totalHits * 100.0;
            return Math.Max(0, (wifeScore / maxScore) * 100.0);
        }
    }
    
    /// <summary>
    /// Gets a color representing the accuracy value (green = good, red = bad).
    /// </summary>
    private static Color4 GetAccuracyColor(double accuracy)
    {
        if (accuracy >= 99) return new Color4(200, 200, 255, 255);  // Light blue (SS)
        if (accuracy >= 95) return new Color4(100, 220, 100, 255);  // Green (S)
        if (accuracy >= 90) return new Color4(180, 220, 100, 255);  // Yellow-green (A)
        if (accuracy >= 80) return new Color4(255, 220, 100, 255);  // Yellow (B)
        if (accuracy >= 70) return new Color4(255, 150, 80, 255);   // Orange (C)
        return new Color4(255, 100, 100, 255);                       // Red (D)
    }
    
    /// <summary>
    /// Updates the statistics text display based on filtered data.
    /// </summary>
    private void UpdateStatsText()
    {
        if (_data == null) return;
        
        var filtered = GetFilteredDeviations();
        
        if (filtered.Count == 0)
        {
            _statsText.Text = "No data for selected keys";
            return;
        }
        
        // Calculate stats for filtered data
        double meanDev = filtered.Average(d => d.Deviation);
        double sumSquaredDiff = filtered.Sum(d => Math.Pow(d.Deviation - meanDev, 2));
        double stdDev = Math.Sqrt(sumSquaredDiff / filtered.Count);
        double ur = stdDev * 10;
        
        var meanDirection = meanDev < 0 ? "early" : "late";
        _statsText.Text = $"UR: {ur:F2}  |  Mean: {Math.Abs(meanDev):F1}ms {meanDirection}  |  Hits: {filtered.Count}";
    }
    
    /// <summary>
    /// Draws the key indicator buttons at the bottom (clickable).
    /// </summary>
    private void DrawKeyIndicators()
    {
        float buttonWidth = 50;
        float buttonSpacing = 8;
        float totalWidth = _keyCount * buttonWidth + (_keyCount - 1) * buttonSpacing;
        float startX = (DrawWidth - ChartPaddingLeft - ChartPaddingRight - totalWidth) / 2;
        
        for (int i = 0; i < _keyCount; i++)
        {
            int column = i;
            bool isActive = _activeColumns[column];
            var color = isActive ? ColumnColors[column % ColumnColors.Length] : new Color4(60, 60, 70, 255);
            
            // Create clickable key button
            var keyButton = new ClickableKeyButton(column, isActive, color, () => ToggleColumn(column))
            {
                Size = new Vector2(buttonWidth, 24),
                Position = new Vector2(startX + i * (buttonWidth + buttonSpacing), 0)
            };
            
            _keyIndicatorsContainer.Add(keyButton);
        }
        
        // Add hint text
        _keyIndicatorsContainer.Add(new SpriteText
        {
            Text = "Click or press 1-" + _keyCount + " to toggle",
            Font = new FontUsage("", 11),
            Colour = new Color4(120, 120, 130, 255),
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            Position = new Vector2(0, 0)
        });
    }
    
    /// <summary>
    /// Clickable button for toggling column visibility.
    /// </summary>
    private partial class ClickableKeyButton : CompositeDrawable
    {
        private readonly int _column;
        private readonly bool _isActive;
        private readonly Color4 _color;
        private readonly Action _onClick;
        private Box _background = null!;
        
        public ClickableKeyButton(int column, bool isActive, Color4 color, Action onClick)
        {
            _column = column;
            _isActive = isActive;
            _color = color;
            _onClick = onClick;
        }
        
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
                    Colour = _isActive ? new Color4(_color.R, _color.G, _color.B, 180) : new Color4(40, 40, 50, 200)
                },
                new SpriteText
                {
                    Text = $"{_column + 1}",
                    Font = new FontUsage("", 14, "Bold"),
                    Colour = _isActive ? Color4.White : new Color4(100, 100, 100, 255),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                }
            };
        }
        
        protected override bool OnClick(ClickEvent e)
        {
            _onClick?.Invoke();
            return true;
        }
        
        protected override bool OnHover(HoverEvent e)
        {
            _background.FadeColour(_isActive 
                ? new Color4((byte)Math.Min(_color.R + 30, 255), (byte)Math.Min(_color.G + 30, 255), (byte)Math.Min(_color.B + 30, 255), 200)
                : new Color4(60, 60, 70, 200), 100);
            return true;
        }
        
        protected override void OnHoverLost(HoverLostEvent e)
        {
            _background.FadeColour(_isActive 
                ? new Color4(_color.R, _color.G, _color.B, 180) 
                : new Color4(40, 40, 50, 200), 100);
        }
    }
    
    /// <summary>
    /// Draws per-column statistics on the right side.
    /// </summary>
    private void DrawColumnStats()
    {
        if (_data == null) return;
        
        float yOffset = 0;
        float rowHeight = 22;
        
        // Header
        _columnStatsContainer.Add(new SpriteText
        {
            Text = "Per-Key Stats",
            Font = new FontUsage("", 12, "Bold"),
            Colour = new Color4(180, 180, 190, 255),
            Position = new Vector2(0, yOffset)
        });
        yOffset += rowHeight;
        
        for (int i = 0; i < _keyCount; i++)
        {
            var columnDeviations = _data.Deviations.Where(d => d.Column == i).ToList();
            bool isActive = _activeColumns[i];
            var color = isActive ? ColumnColors[i % ColumnColors.Length] : new Color4(80, 80, 80, 255);
            
            if (columnDeviations.Count == 0)
            {
                _columnStatsContainer.Add(new SpriteText
                {
                    Text = $"K{i + 1}: --",
                    Font = new FontUsage("", 11),
                    Colour = color,
                    Position = new Vector2(0, yOffset)
                });
            }
            else
            {
                double mean = columnDeviations.Average(d => d.Deviation);
                double sumSq = columnDeviations.Sum(d => Math.Pow(d.Deviation - mean, 2));
                double stdDev = Math.Sqrt(sumSq / columnDeviations.Count);
                double ur = stdDev * 10;
                
                _columnStatsContainer.Add(new SpriteText
                {
                    Text = $"K{i + 1}: {ur:F1} UR",
                    Font = new FontUsage("", 11),
                    Colour = color,
                    Position = new Vector2(0, yOffset)
                });
            }
            
            yOffset += rowHeight - 4;
        }
    }
    
    /// <summary>
    /// Draws grid lines on the chart including judgement boundary lines.
    /// </summary>
    private void DrawGrid()
    {
        // Get current timing windows for judgement boundaries
        var windows = GetCurrentTimingWindows();
        
        // Draw judgement boundary lines (horizontal)
        // Each line is drawn twice: above and below the zero line
        DrawJudgementLine(windows.marvelous, ColorMax300, "Marvelous");
        DrawJudgementLine(windows.perfect, Color300, "Perfect");
        DrawJudgementLine(windows.great, Color200, "Great");
        DrawJudgementLine(windows.good, Color100, "Good");
        DrawJudgementLine(windows.boo, Color50, "Boo");
        
        // Vertical grid lines (time divisions)
        if (_data != null && _data.MapDuration > 0)
        {
            int numDivisions = Math.Max(4, Math.Min(10, (int)(_data.MapDuration / 30000))); // One per ~30 seconds
            for (int i = 0; i <= numDivisions; i++)
            {
                float x = i / (float)numDivisions;
                _gridContainer.Add(new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 1,
                    RelativePositionAxes = Axes.X,
                    X = x,
                    Colour = new Color4(50, 50, 60, 255)
                });
            }
        }
    }
    
    /// <summary>
    /// Draws a pair of horizontal lines at +/- the given deviation value to mark a judgement boundary.
    /// </summary>
    private void DrawJudgementLine(float deviationMs, Color4 color, string label)
    {
        if (deviationMs > _deviationMax) return; // Don't draw if outside visible range
        
        float normalizedDev = deviationMs / _deviationMax;
        
        // Line above zero (early hits - negative deviation shown as positive Y)
        float yAbove = 0.5f - (normalizedDev * 0.5f);
        // Line below zero (late hits - positive deviation)
        float yBelow = 0.5f + (normalizedDev * 0.5f);
        
        // Draw both lines with semi-transparent color
        var lineColor = new Color4(color.R, color.G, color.B, 100);
        
        // Upper line (early boundary)
        _gridContainer.Add(new Box
        {
            RelativeSizeAxes = Axes.X,
            Height = 1,
            RelativePositionAxes = Axes.Y,
            Y = yAbove,
            Colour = lineColor
        });
        
        // Lower line (late boundary)
        _gridContainer.Add(new Box
        {
            RelativeSizeAxes = Axes.X,
            Height = 1,
            RelativePositionAxes = Axes.Y,
            Y = yBelow,
            Colour = lineColor
        });
        
        // Add small label on the right side for the upper line
        _gridContainer.Add(new SpriteText
        {
            Text = label,
            Font = new FontUsage("", 9),
            Colour = new Color4(color.R, color.G, color.B, 160),
            Anchor = Anchor.TopRight,
            Origin = Anchor.CentreRight,
            RelativePositionAxes = Axes.Y,
            Y = yAbove,
            Padding = new MarginPadding { Right = 4 }
        });
    }
    
    /// <summary>
    /// Draws axis labels.
    /// </summary>
    private void DrawAxisLabels()
    {
        // Y-axis labels (deviation in ms) - left side
        float[] labelLevels = { -1f, -0.5f, 0f, 0.5f, 1f };
        foreach (var level in labelLevels)
        {
            float y = ChartPaddingTop + (DrawHeight - ChartPaddingTop - ChartPaddingBottom) * (0.5f - level * 0.5f);
            float value = level * _deviationMax;
            
            string label = level == 0 ? "0" : $"{value:+0;-0}";
            
            _labelsContainer.Add(new SpriteText
            {
                Text = label,
                Font = new FontUsage("", 14),
                Colour = level == 0 ? Color4.White : new Color4(150, 150, 150, 255),
                Position = new Vector2(ChartPaddingLeft - 8, y),
                Origin = Anchor.CentreRight
            });
        }
        
        // Y-axis title
        _labelsContainer.Add(new SpriteText
        {
            Text = "ms",
            Font = new FontUsage("", 12),
            Colour = new Color4(150, 150, 150, 255),
            Position = new Vector2(8, ChartPaddingTop - 5),
            Origin = Anchor.BottomLeft
        });
        
        // "Early" and "Late" labels
        _labelsContainer.Add(new SpriteText
        {
            Text = "Early",
            Font = new FontUsage("", 11),
            Colour = new Color4(100, 180, 255, 200),
            Position = new Vector2(8, ChartPaddingTop + 20),
            Origin = Anchor.TopLeft
        });
        
        _labelsContainer.Add(new SpriteText
        {
            Text = "Late",
            Font = new FontUsage("", 11),
            Colour = new Color4(255, 150, 80, 200),
            Position = new Vector2(8, DrawHeight - ChartPaddingBottom - 20),
            Origin = Anchor.BottomLeft
        });
        
        // X-axis labels (time)
        if (_data != null && _data.MapDuration > 0)
        {
            int numDivisions = Math.Max(4, Math.Min(10, (int)(_data.MapDuration / 30000)));
            for (int i = 0; i <= numDivisions; i++)
            {
                float x = ChartPaddingLeft + (DrawWidth - ChartPaddingLeft - ChartPaddingRight) * (i / (float)numDivisions);
                var time = TimeSpan.FromMilliseconds(_data.MapDuration * (i / (float)numDivisions));
                
                _labelsContainer.Add(new SpriteText
                {
                    Text = time.ToString(@"m\:ss"),
                    Font = new FontUsage("", 12),
                    Colour = new Color4(150, 150, 150, 255),
                    Position = new Vector2(x, DrawHeight - ChartPaddingBottom + 8),
                    Origin = Anchor.TopCentre
                });
            }
        }
    }
    
    /// <summary>
    /// Draws the data points on the chart (filtered by active columns).
    /// </summary>
    private void DrawDataPoints()
    {
        if (_data == null || _data.MapDuration <= 0) return;
        
        var filtered = GetFilteredDeviations();
        
        foreach (var deviation in filtered)
        {
            // X position based on time
            float x = (float)(deviation.ExpectedTime / _data.MapDuration);
            x = Math.Clamp(x, 0, 1);
            
            // Y position based on deviation (inverted: negative at top, positive at bottom)
            // Clamp to range
            float normalizedDev = (float)Math.Clamp(deviation.Deviation, -_deviationMax, _deviationMax);
            float y = 0.5f + (normalizedDev / _deviationMax) * 0.5f;
            y = Math.Clamp(y, 0, 1);
            
            // Color based on judgement
            var color = GetJudgementColor(deviation.Judgement);
            
            // Draw point
            _pointsContainer.Add(new Circle
            {
                Size = new Vector2(PointRadius * 2),
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(x, y),
                Origin = Anchor.Centre,
                Colour = color,
                Alpha = deviation.Judgement == ManiaJudgement.Miss ? 0.7f : 1f
            });
        }
    }
    
    /// <summary>
    /// Gets the color for a judgement type.
    /// </summary>
    private static Color4 GetJudgementColor(ManiaJudgement judgement)
    {
        return judgement switch
        {
            ManiaJudgement.Max300 => ColorMax300,
            ManiaJudgement.Hit300 => Color300,
            ManiaJudgement.Hit200 => Color200,
            ManiaJudgement.Hit100 => Color100,
            ManiaJudgement.Hit50 => Color50,
            ManiaJudgement.Miss => ColorMiss,
            _ => Color300
        };
    }
}

