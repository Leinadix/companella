using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Models;
using OsuMappingHelper.Services;

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
    // All layout values are relative (0-1 fractions)
    private const float LeftPanelWidth = 0.22f;      // Left bar chart takes 22% of width
    private const float RightPanelPadding = 0.01f;   // Small right padding
    private const float TopPadding = 0.12f;          // Top area for title (increased)
    private const float BottomPadding = 0.02f;       // Small bottom padding
    private const float BellCurveRelativeHeight = 0.42f;  // Bell curve takes 42% of height
    private const float KeyIndicatorRelativeHeight = 0.08f; // Key buttons take 8%
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
    
    // Time selection for bell curve (horizontal selection on scatter plot)
    private Container _bellCurveContainer = null!;
    private Container _timeSelectionContainer = null!;
    private Box _timeSelectionBox = null!;
    private float _timeSelectionStart = 0f;   // Normalized time (0-1), left edge
    private float _timeSelectionEnd = 1f;     // Normalized time (0-1), right edge
    private const float MinTimeSelectionRange = 0.05f; // Minimum 5% of map selected
    private const float DragHandleWidth = 30f; // Drag handle width in pixels (fixed size for consistent hitbox)
    
    /// <summary>
    /// Gets or sets the time selection start (0-1 normalized).
    /// </summary>
    public float TimeSelectionStart
    {
        get => _timeSelectionStart;
        set
        {
            _timeSelectionStart = Math.Clamp(value, 0f, 1f);
        }
    }
    
    /// <summary>
    /// Gets or sets the time selection end (0-1 normalized).
    /// </summary>
    public float TimeSelectionEnd
    {
        get => _timeSelectionEnd;
        set
        {
            _timeSelectionEnd = Math.Clamp(value, 0f, 1f);
        }
    }
    
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
    
    /// <summary>
    /// Event raised when time selection changes.
    /// </summary>
    public event Action? TimeSelectionChanged;
    
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
                Font = new FontUsage("", 21, "Bold"),
                Colour = new Color4(255, 102, 170, 255),
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(0.01f, 0.01f)
            },
            // Hit window system indicator
            _hitWindowText = new SpriteText
            {
                Text = "[osu!mania]",
                Font = new FontUsage("", 13),
                Colour = new Color4(150, 150, 160, 255),
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(0.01f, 0.06f)
            },
            // Statistics text
            _statsText = new SpriteText
            {
                Text = "",
                Font = new FontUsage("", 15),
                Colour = new Color4(200, 200, 200, 255),
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(-0.01f, 0.02f)
            },
            // Key indicators container (very bottom of screen) - relative sizing
            _keyIndicatorsContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = 1f - LeftPanelWidth - RightPanelPadding,
                Height = KeyIndicatorRelativeHeight,
                RelativePositionAxes = Axes.Both,
                X = LeftPanelWidth,
                Y = 1f - KeyIndicatorRelativeHeight,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft
            },
            // Column stats container (right side of scatter plot) - relative sizing
            _columnStatsContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.12f,
                Height = 1f - TopPadding - BellCurveRelativeHeight - KeyIndicatorRelativeHeight - BottomPadding,
                RelativePositionAxes = Axes.Both,
                X = 1f - 0.12f - RightPanelPadding,
                Y = TopPadding,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Alpha = 0
            },
            // Distribution bar chart container (left side) - relative sizing
            _distributionContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = LeftPanelWidth - 0.01f,
                Height = 1f - TopPadding - KeyIndicatorRelativeHeight - 0.02f,
                RelativePositionAxes = Axes.Both,
                X = 0.005f,
                Y = TopPadding,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Alpha = 0
            },
            // Accuracy text (bottom of left panel, above key indicators)
            _accuracyText = new SpriteText
            {
                Text = "",
                Font = new FontUsage("", 23, "Bold"),
                Colour = new Color4(100, 220, 100, 255),
                RelativePositionAxes = Axes.Both,
                X = 0.01f,
                Y = 1f - KeyIndicatorRelativeHeight - 0.01f,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.BottomLeft
            },
            // Bell curve container (large area below scatter plot) - relative sizing
            _bellCurveContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = 1f - LeftPanelWidth - RightPanelPadding,
                Height = BellCurveRelativeHeight,
                RelativePositionAxes = Axes.Both,
                X = LeftPanelWidth,
                Y = 1f - BellCurveRelativeHeight - KeyIndicatorRelativeHeight,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Alpha = 0
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
            // Chart area container (scatter plot - above bell curve) - relative sizing
            _chartArea = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = 1f - LeftPanelWidth - RightPanelPadding,
                Height = 1f - TopPadding - BellCurveRelativeHeight - KeyIndicatorRelativeHeight - BottomPadding,
                RelativePositionAxes = Axes.Both,
                X = LeftPanelWidth,
                Y = TopPadding,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Alpha = 0,
                Children = new Drawable[]
                {
                    // Time selection container (horizontal selection with draggable left/right edges)
                    _timeSelectionContainer = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        RelativePositionAxes = Axes.X,
                        Alpha = 0,
                        Children = new Drawable[]
                        {
                            // Background highlight
                            _timeSelectionBox = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(255, 102, 170, 25)
                            },
                            // Left edge handle (start time) - fixed pixel width
                            new DraggableTimeHandle(
                                isLeftEdge: true,
                                onDrag: (delta) => OnTimeEdgeDrag(true, delta),
                                onDragEnd: () => { RedrawSelectionDependentCharts(); TimeSelectionChanged?.Invoke(); }
                            )
                            {
                                RelativeSizeAxes = Axes.Y,
                                Width = DragHandleWidth,
                                Height = 1f,
                                Anchor = Anchor.TopLeft,
                                Origin = Anchor.TopLeft
                            },
                            // Right edge handle (end time) - fixed pixel width
                            new DraggableTimeHandle(
                                isLeftEdge: false,
                                onDrag: (delta) => OnTimeEdgeDrag(false, delta),
                                onDragEnd: () => { RedrawSelectionDependentCharts(); TimeSelectionChanged?.Invoke(); }
                            )
                            {
                                RelativeSizeAxes = Axes.Y,
                                Width = DragHandleWidth,
                                Height = 1f,
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight
                            }
                        }
                    },
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
            
            // Reset time selection to full range
            _timeSelectionStart = 0f;
            _timeSelectionEnd = 1f;
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
        Logger.Info($"[Chart] Column {column + 1} {(_activeColumns[column] ? "enabled" : "disabled")}");
        
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
        
        Logger.Info($"[Chart] Switched to {GetHitWindowSystemName()}");
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
            Logger.Info($"[Chart] OD set to {_maniaOD}, requesting full re-analysis");
            
            // Request full re-analysis with new OD (if handler is registered)
            if (ReanalysisRequested != null)
            {
                ReanalysisRequested.Invoke(_maniaOD);
                HitWindowSystemChanged?.Invoke();
                // Data will be updated by the handler calling SetData again
                return;
            }
            
            // Fallback to local recalculation if no handler
            Logger.Info($"[Chart] No re-analysis handler, using local recalculation");
        }
        else
        {
            // Etterna Wife: 1-8 and 9=Justice - use local recalculation
            _etternaJudge = Math.Clamp(_etternaJudge + delta, 1, 9);
            Logger.Info($"[Chart] Judge set to {GetEtternaJudgeName(_etternaJudge)}");
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
        _bellCurveContainer.Clear();
        
        if (_data == null || !_data.Success || _data.Deviations.Count == 0)
        {
            _noDataText.FadeTo(1, 200);
            _chartArea.FadeTo(0, 200);
            _labelsContainer.FadeTo(0, 200);
            _columnStatsContainer.FadeTo(0, 200);
            _distributionContainer.FadeTo(0, 200);
            _bellCurveContainer.FadeTo(0, 200);
            _timeSelectionContainer.FadeTo(0, 200);
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
        _bellCurveContainer.FadeTo(1, 200);
        
        // Update hit window indicator
        _hitWindowText.Text = $"[{GetHitWindowSystemName()}] A/D or Arrows to adjust, Tab to switch system";
        
        // Calculate deviation range (auto-scale)
        CalculateRange();
        
        // Update stats text
        UpdateStatsText();
        
        // Draw distribution chart (left side)
        DrawDistributionChart();
        
        // Draw time selection highlight on scatter plot
        DrawTimeSelection();
        
        // Draw bell curve (left side, below distribution) - uses time selection
        DrawBellCurve();
        
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
    /// Filters by time selection on the scatter plot.
    /// </summary>
    private void DrawDistributionChart()
    {
        if (_data == null || _data.MapDuration <= 0) return;
        
        var filtered = GetFilteredDeviations();
        if (filtered.Count == 0) return;
        
        // Filter by time selection (same as bell curve)
        double startTime = _timeSelectionStart * _data.MapDuration;
        double endTime = _timeSelectionEnd * _data.MapDuration;
        var selectedPoints = filtered.Where(d => d.ExpectedTime >= startTime && d.ExpectedTime <= endTime).ToList();
        
        // Count judgements from selected points only
        var counts = new Dictionary<ManiaJudgement, int>
        {
            { ManiaJudgement.Max300, 0 },
            { ManiaJudgement.Hit300, 0 },
            { ManiaJudgement.Hit200, 0 },
            { ManiaJudgement.Hit100, 0 },
            { ManiaJudgement.Hit50, 0 },
            { ManiaJudgement.Miss, 0 }
        };
        
        foreach (var deviation in selectedPoints)
        {
            counts[deviation.Judgement]++;
        }
        
        int totalHits = selectedPoints.Count;
        int maxCount = counts.Values.Max();
        if (maxCount == 0) maxCount = 1;
        
        // Calculate accuracy based on current system (for selected region only)
        double accuracy = CalculateAccuracy(counts, totalHits);
        _accuracyText.Text = totalHits > 0 ? $"{accuracy:F2}%" : "N/A";
        _accuracyText.Colour = GetAccuracyColor(accuracy);
        
        // Use container dimensions for relative layout
        float containerWidth = _distributionContainer.DrawWidth;
        float containerHeight = _distributionContainer.DrawHeight;
        
        // Draw header - positioned at top of container
        _distributionContainer.Add(new SpriteText
        {
            Text = "Distribution",
            Font = new FontUsage("", Math.Max(14, containerHeight * 0.045f), "Bold"),
            Colour = new Color4(180, 180, 190, 255),
            Position = new Vector2(0, 0)
        });
        
        // Draw bars - all sizes relative to container
        var judgements = new[]
        {
            (ManiaJudgement.Max300, "MAX", ColorMax300),
            (ManiaJudgement.Hit300, "300", Color300),
            (ManiaJudgement.Hit200, "200", Color200),
            (ManiaJudgement.Hit100, "100", Color100),
            (ManiaJudgement.Hit50, "50", Color50),
            (ManiaJudgement.Miss, "Miss", ColorMiss)
        };
        
        float labelWidthRel = 0.20f;  // 20% for labels
        float countWidthRel = 0.20f;  // 20% for count
        float barWidthRel = 1f - labelWidthRel - countWidthRel - 0.02f;  // Rest for bar
        
        float numBars = judgements.Length;
        float headerSpace = 0.07f;  // Space for header
        float totalBarArea = 0.80f;  // Use 80% of height for bars
        float barHeightRel = totalBarArea / numBars * 0.65f;  // Each bar
        float barSpacingRel = totalBarArea / numBars * 0.35f;  // Spacing
        float startY = headerSpace;  // Start after header
        
        float yOffset = startY;
        
        foreach (var (judgement, label, color) in judgements)
        {
            int count = counts[judgement];
            float barFillRel = maxCount > 0 ? (count / (float)maxCount) * barWidthRel : 0;
            
            float barHeight = barHeightRel * containerHeight;
            float yPos = yOffset * containerHeight;
            
            // Label
            _distributionContainer.Add(new SpriteText
            {
                Text = label,
                Font = new FontUsage("", Math.Max(13, containerHeight * 0.05f)),
                Colour = color,
                Position = new Vector2(0, yPos + barHeight * 0.3f),
                Origin = Anchor.TopLeft
            });
            
            // Bar background
            _distributionContainer.Add(new Box
            {
                Size = new Vector2(barWidthRel * containerWidth, barHeight * 0.8f),
                Position = new Vector2(labelWidthRel * containerWidth, yPos + barHeight * 0.1f),
                Colour = new Color4(30, 30, 35, 255)
            });
            
            // Bar fill
            if (barFillRel > 0)
            {
                _distributionContainer.Add(new Box
                {
                    Size = new Vector2(Math.Max(barFillRel * containerWidth, 2), barHeight * 0.8f),
                    Position = new Vector2(labelWidthRel * containerWidth, yPos + barHeight * 0.1f),
                    Colour = new Color4(color.R, color.G, color.B, 230)
                });
            }
            
            // Count
            _distributionContainer.Add(new SpriteText
            {
                Text = $"{count}",
                Font = new FontUsage("", Math.Max(12, containerHeight * 0.045f)),
                Colour = new Color4(200, 200, 200, 255),
                Position = new Vector2((labelWidthRel + barWidthRel + 0.02f) * containerWidth, yPos + barHeight * 0.3f),
                Origin = Anchor.TopLeft
            });
            
            yOffset += barHeightRel + barSpacingRel;
        }
        
        // Draw total at bottom
        _distributionContainer.Add(new SpriteText
        {
            Text = $"Total: {totalHits}",
            Font = new FontUsage("", Math.Max(13, containerHeight * 0.045f)),
            Colour = new Color4(150, 150, 160, 255),
            Position = new Vector2(0, (yOffset + 0.02f) * containerHeight)
        });
    }
    
    /// <summary>
    /// Draws the distribution curve based on actual timing deviation data.
    /// Uses kernel density estimation for a smooth curve that reflects the real data.
    /// Filters by time selection on the scatter plot.
    /// </summary>
    private void DrawBellCurve()
    {
        _bellCurveContainer.Clear();
        
        if (_data == null || _data.MapDuration <= 0) return;
        
        var filtered = GetFilteredDeviations();
        if (filtered.Count == 0) return;
        
        // Filter deviations by time selection (X-axis on scatter plot)
        double startTime = _timeSelectionStart * _data.MapDuration;
        double endTime = _timeSelectionEnd * _data.MapDuration;
        var selectedPoints = filtered.Where(d => d.ExpectedTime >= startTime && d.ExpectedTime <= endTime).ToList();
        
        if (selectedPoints.Count == 0) return;
        
        // Calculate mean and standard deviation for selected points
        double mean = selectedPoints.Average(d => d.Deviation);
        double stdDev = 0;
        if (selectedPoints.Count > 1)
        {
            double sumSquaredDiff = selectedPoints.Sum(d => Math.Pow(d.Deviation - mean, 2));
            stdDev = Math.Sqrt(sumSquaredDiff / selectedPoints.Count);
        }
        if (stdDev < 0.1) stdDev = 0.1;
        
        float containerWidth = _bellCurveContainer.DrawWidth;
        float containerHeight = _bellCurveContainer.DrawHeight;
        float headerPadding = 18f; // Space for header
        float footerPadding = 14f; // Space for labels at bottom
        float sidePadding = 10f;   // Side padding
        float chartWidth = containerWidth - sidePadding * 2;
        float chartHeight = containerHeight - headerPadding - footerPadding;
        float topBoost = 8f; // Extra space at top for visual scaling
        
        // Use the graph's deviation range for the bell curve X-axis
        float deviationMin = -_deviationMax;
        float deviationMax = _deviationMax;
        float boundsRange = deviationMax - deviationMin;
        
        // Background
        _bellCurveContainer.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = new Color4(25, 25, 30, 200)
        });
        
        // Header with selection info
        double selectionPercent = (_timeSelectionEnd - _timeSelectionStart) * 100;
        string headerText = selectionPercent < 99.9 
            ? $"Distribution (selection: {selectionPercent:F0}%)" 
            : "Deviation Distribution";
        _bellCurveContainer.Add(new SpriteText
        {
            Text = headerText,
            Font = new FontUsage("", 15, "Bold"),
            Colour = new Color4(180, 180, 190, 255),
            Position = new Vector2(5, 3)
        });
        
        // Use Kernel Density Estimation (KDE) to create a smooth curve from actual data
        int numPoints = 80;
        float[] density = new float[numPoints];
        
        // Bandwidth for KDE (Silverman's rule of thumb)
        double bandwidth = 1.06 * stdDev * Math.Pow(selectedPoints.Count, -0.2);
        if (bandwidth < 1) bandwidth = 1; // Minimum bandwidth of 1ms
        
        // Calculate density at each point using Gaussian kernel
        for (int i = 0; i < numPoints; i++)
        {
            float x = deviationMin + (i / (float)(numPoints - 1)) * boundsRange;
            double sum = 0;
            
            foreach (var dev in selectedPoints)
            {
                double u = (x - dev.Deviation) / bandwidth;
                sum += Math.Exp(-0.5 * u * u); // Gaussian kernel
            }
            
            density[i] = (float)(sum / (selectedPoints.Count * bandwidth));
        }
        
        // Find max for normalization
        float maxDensity = density.Max();
        if (maxDensity < 0.0001f) maxDensity = 0.0001f;
        
        // Draw filled area under curve (as vertical bars for smooth fill)
        // Bars are centered on their X position
        float barWidth = chartWidth / numPoints + 1;
        float curveAreaHeight = chartHeight - topBoost;
        float curveTop = headerPadding + topBoost;
        
        for (int i = 0; i < numPoints; i++)
        {
            float x = sidePadding + (i / (float)(numPoints - 1)) * chartWidth;
            float normalizedHeight = density[i] / maxDensity;
            float barHeight = normalizedHeight * curveAreaHeight;
            
            if (barHeight > 0.5f)
            {
                float devValue = deviationMin + (i / (float)(numPoints - 1)) * boundsRange;
                var color = GetDeviationColor(Math.Abs(devValue));
                
                // Draw filled bar - centered on X position, anchored to bottom of curve area
                _bellCurveContainer.Add(new Box
                {
                    Size = new Vector2(barWidth, barHeight),
                    Position = new Vector2(x, containerHeight - footerPadding),
                    Colour = new Color4(color.R, color.G, color.B, 100),
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.BottomCentre
                });
            }
        }
        
        // Draw curve line on top
        for (int i = 0; i < numPoints - 1; i++)
        {
            float x1 = sidePadding + (i / (float)(numPoints - 1)) * chartWidth;
            float x2 = sidePadding + ((i + 1) / (float)(numPoints - 1)) * chartWidth;
            // Y coordinates: bottom of curve area minus the normalized height
            float y1 = containerHeight - footerPadding - (density[i] / maxDensity) * curveAreaHeight;
            float y2 = containerHeight - footerPadding - (density[i + 1] / maxDensity) * curveAreaHeight;
            
            float devValue = deviationMin + ((i + 0.5f) / (float)(numPoints - 1)) * boundsRange;
            var color = GetDeviationColor(Math.Abs(devValue));
            
            float length = (float)Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            float angle = (float)Math.Atan2(y2 - y1, x2 - x1);
            
            _bellCurveContainer.Add(new Box
            {
                Size = new Vector2(length + 1, 2),
                Position = new Vector2(x1, y1),
                Rotation = (float)(angle * 180 / Math.PI),
                Origin = Anchor.CentreLeft,
                Anchor = Anchor.TopLeft,
                Colour = new Color4(color.R, color.G, color.B, 255)
            });
        }
        
        // Draw zero line
        float zeroX = sidePadding + ((0 - deviationMin) / boundsRange) * chartWidth;
        _bellCurveContainer.Add(new Box
        {
            Size = new Vector2(1, curveAreaHeight + 2),
            Position = new Vector2(zeroX, curveTop - 2),
            Colour = new Color4(100, 100, 100, 100),
            Origin = Anchor.TopCentre
        });
        
        // Draw mean line
        float meanX = sidePadding + ((float)(mean - deviationMin) / boundsRange) * chartWidth;
        if (meanX >= sidePadding && meanX <= sidePadding + chartWidth)
        {
            _bellCurveContainer.Add(new Box
            {
                Size = new Vector2(2, curveAreaHeight + 2),
                Position = new Vector2(meanX, curveTop - 2),
                Colour = new Color4(255, 255, 255, 180),
                Origin = Anchor.TopCentre
            });
            
            string meanLabel = mean >= 0 ? $"+{mean:F1}" : $"{mean:F1}";
            _bellCurveContainer.Add(new SpriteText
            {
                Text = meanLabel,
                Font = new FontUsage("", 12),
                Colour = new Color4(255, 255, 255, 220),
                Position = new Vector2(meanX, containerHeight - 4),
                Origin = Anchor.BottomCentre
            });
        }
        
        // Bounds labels (deviation range) - centered under the chart edges
        _bellCurveContainer.Add(new SpriteText
        {
            Text = $"{deviationMin:F0}ms",
            Font = new FontUsage("", 12),
            Colour = new Color4(150, 150, 160, 255),
            Position = new Vector2(sidePadding, containerHeight - 4),
            Origin = Anchor.BottomCentre
        });
        
        _bellCurveContainer.Add(new SpriteText
        {
            Text = $"+{deviationMax:F0}ms",
            Font = new FontUsage("", 12),
            Colour = new Color4(150, 150, 160, 255),
            Position = new Vector2(sidePadding + chartWidth, containerHeight - 4),
            Origin = Anchor.BottomCentre
        });
        
        // Stats text
        double urSelected = stdDev * 10;
        _bellCurveContainer.Add(new SpriteText
        {
            Text = $"Selected: {selectedPoints.Count} | UR: {urSelected:F1}",
            Font = new FontUsage("", 12),
            Colour = new Color4(150, 150, 160, 255),
            Position = new Vector2(containerWidth - 5, 4),
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight
        });
    }
    
    /// <summary>
    /// Gets a color based on deviation magnitude for the bell curve.
    /// </summary>
    private Color4 GetDeviationColor(float absDeviation)
    {
        var windows = GetCurrentTimingWindows();
        
        if (absDeviation <= windows.marvelous) return ColorMax300;
        if (absDeviation <= windows.perfect) return Color300;
        if (absDeviation <= windows.great) return Color200;
        if (absDeviation <= windows.good) return Color100;
        if (absDeviation <= windows.boo) return Color50;
        return ColorMiss;
    }
    
    /// <summary>
    /// Draws the time selection highlight on the scatter plot (horizontal selection).
    /// </summary>
    private void DrawTimeSelection()
    {
        if (_data == null) return;
        
        // Position and size based on time selection (X axis) - all relative
        // Minimum width ensures handles don't overlap (handles are fixed pixel size)
        float selectionWidth = Math.Max(0.05f, _timeSelectionEnd - _timeSelectionStart);
        
        _timeSelectionContainer.X = _timeSelectionStart;
        _timeSelectionContainer.Width = selectionWidth;
        _timeSelectionContainer.RelativeSizeAxes = Axes.Both;
        _timeSelectionContainer.RelativePositionAxes = Axes.X;
        _timeSelectionContainer.FadeTo(1, 100);
    }
    
    /// <summary>
    /// Redraws charts that depend on the time selection (distribution and bell curve).
    /// Called when time selection changes.
    /// </summary>
    private void RedrawSelectionDependentCharts()
    {
        _distributionContainer.Clear();
        DrawDistributionChart();
        DrawBellCurve();
    }
    
    /// <summary>
    /// Handles drag events from time edge handles on the scatter plot.
    /// </summary>
    private void OnTimeEdgeDrag(bool isLeftEdge, float deltaX)
    {
        float chartWidth = _chartArea.DrawWidth;
        if (chartWidth <= 0) return;
        
        // Convert pixel delta to normalized delta
        float normalizedDelta = deltaX / chartWidth;
        
        if (isLeftEdge)
        {
            // Left edge controls start time
            float newStart = _timeSelectionStart + normalizedDelta;
            // Ensure minimum range and stay within bounds
            if (newStart >= 0 && newStart < _timeSelectionEnd - MinTimeSelectionRange)
            {
                _timeSelectionStart = newStart;
                DrawTimeSelection();
            }
        }
        else
        {
            // Right edge controls end time
            float newEnd = _timeSelectionEnd + normalizedDelta;
            // Ensure minimum range and stay within bounds
            if (newEnd <= 1 && newEnd > _timeSelectionStart + MinTimeSelectionRange)
            {
                _timeSelectionEnd = newEnd;
                DrawTimeSelection();
            }
        }
    }
    
    /// <summary>
    /// Draggable vertical edge handle for adjusting time selection on the scatter plot.
    /// Drags horizontally to adjust the X position of the selection.
    /// </summary>
    private partial class DraggableTimeHandle : CompositeDrawable
    {
        private readonly bool _isLeftEdge;
        private readonly Action<float> _onDrag;
        private readonly Action _onDragEnd;
        private Box _background = null!;
        private Box _indicator = null!;
        
        public DraggableTimeHandle(bool isLeftEdge, Action<float> onDrag, Action onDragEnd)
        {
            _isLeftEdge = isLeftEdge;
            _onDrag = onDrag;
            _onDragEnd = onDragEnd;
        }
        
        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                _background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(255, 102, 170, 0) // Transparent by default
                },
                _indicator = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 4,
                    Anchor = _isLeftEdge ? Anchor.CentreLeft : Anchor.CentreRight,
                    Origin = Anchor.Centre,
                    Colour = new Color4(255, 102, 170, 200)
                }
            };
        }
        
        protected override bool OnHover(HoverEvent e)
        {
            _background.FadeColour(new Color4(255, 102, 170, 50), 100);
            _indicator.FadeColour(new Color4(255, 150, 200, 255), 100);
            _indicator.ResizeWidthTo(6, 100);
            return true;
        }
        
        protected override void OnHoverLost(HoverLostEvent e)
        {
            _background.FadeColour(new Color4(255, 102, 170, 0), 100);
            _indicator.FadeColour(new Color4(255, 102, 170, 200), 100);
            _indicator.ResizeWidthTo(4, 100);
        }
        
        protected override bool OnDragStart(DragStartEvent e)
        {
            return true;
        }
        
        protected override void OnDrag(DragEvent e)
        {
            _onDrag?.Invoke(e.Delta.X);
        }
        
        protected override void OnDragEnd(DragEndEvent e)
        {
            _onDragEnd?.Invoke();
        }
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
        float containerWidth = _keyIndicatorsContainer.DrawWidth;
        float containerHeight = _keyIndicatorsContainer.DrawHeight;
        
        // Button sizes relative to container
        float buttonWidthRel = 0.08f;  // Each button 8% of container width
        float buttonSpacingRel = 0.015f;  // 1.5% spacing
        float totalWidthRel = _keyCount * buttonWidthRel + (_keyCount - 1) * buttonSpacingRel;
        float startXRel = (1f - totalWidthRel) / 2;  // Center the buttons
        
        for (int i = 0; i < _keyCount; i++)
        {
            int column = i;
            bool isActive = _activeColumns[column];
            var color = isActive ? ColumnColors[column % ColumnColors.Length] : new Color4(60, 60, 70, 255);
            
            // Create clickable key button with relative positioning
            var keyButton = new ClickableKeyButton(column, isActive, color, () => ToggleColumn(column))
            {
                Size = new Vector2(buttonWidthRel * containerWidth, containerHeight * 0.7f),
                Position = new Vector2((startXRel + i * (buttonWidthRel + buttonSpacingRel)) * containerWidth, containerHeight * 0.15f)
            };
            
            _keyIndicatorsContainer.Add(keyButton);
        }
        
        // Add hint text
        _keyIndicatorsContainer.Add(new SpriteText
        {
            Text = "Click or press 1-" + _keyCount + " to toggle",
            Font = new FontUsage("", 14),
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
                    Font = new FontUsage("", 17, "Bold"),
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
        float rowHeight = 24;
        
        // Header
        _columnStatsContainer.Add(new SpriteText
        {
            Text = "Per-Key Stats",
            Font = new FontUsage("", 15, "Bold"),
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
                    Font = new FontUsage("", 14),
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
                    Font = new FontUsage("", 14),
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
    /// Draws axis labels using relative positioning.
    /// </summary>
    private void DrawAxisLabels()
    {
        // Calculate positions based on relative layout
        float chartTop = TopPadding;
        float chartBottom = 1f - BellCurveRelativeHeight - KeyIndicatorRelativeHeight - BottomPadding;
        float chartHeight = chartBottom - chartTop;
        float chartLeft = LeftPanelWidth;
        
        // Y-axis labels (deviation in ms) - positioned between left panel and scatter plot
        float[] labelLevels = { -1f, -0.5f, 0f, 0.5f, 1f };
        foreach (var level in labelLevels)
        {
            float relY = chartTop + chartHeight * (0.5f - level * 0.5f);
            float value = level * _deviationMax;
            
            string label = level == 0 ? "0" : $"{value:+0;-0}";
            
            _labelsContainer.Add(new SpriteText
            {
                Text = label,
                Font = new FontUsage("", 13),
                Colour = level == 0 ? Color4.White : new Color4(150, 150, 150, 255),
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(chartLeft - 0.005f, relY),
                Origin = Anchor.CentreRight
            });
        }
        
        // Y-axis title "ms" - top left of scatter plot
        _labelsContainer.Add(new SpriteText
        {
            Text = "ms",
            Font = new FontUsage("", 12),
            Colour = new Color4(150, 150, 150, 255),
            RelativePositionAxes = Axes.Both,
            Position = new Vector2(chartLeft + 0.005f, chartTop - 0.01f),
            Origin = Anchor.BottomLeft
        });
        
        // "Early" label - top left inside scatter area
        _labelsContainer.Add(new SpriteText
        {
            Text = "Early",
            Font = new FontUsage("", 12),
            Colour = new Color4(100, 180, 255, 180),
            RelativePositionAxes = Axes.Both,
            Position = new Vector2(chartLeft + 0.005f, chartTop + 0.01f),
            Origin = Anchor.TopLeft
        });
        
        // "Late" label - bottom left inside scatter area
        _labelsContainer.Add(new SpriteText
        {
            Text = "Late",
            Font = new FontUsage("", 12),
            Colour = new Color4(255, 150, 80, 180),
            RelativePositionAxes = Axes.Both,
            Position = new Vector2(chartLeft + 0.005f, chartBottom - 0.01f),
            Origin = Anchor.BottomLeft
        });
        
        // X-axis labels (time) - positioned between scatter plot and bell curve
        if (_data != null && _data.MapDuration > 0)
        {
            float chartWidth = 1f - LeftPanelWidth - RightPanelPadding;
            int numDivisions = Math.Max(4, Math.Min(10, (int)(_data.MapDuration / 30000)));
            for (int i = 0; i <= numDivisions; i++)
            {
                float relX = LeftPanelWidth + chartWidth * (i / (float)numDivisions);
                var time = TimeSpan.FromMilliseconds(_data.MapDuration * (i / (float)numDivisions));
                
                _labelsContainer.Add(new SpriteText
                {
                    Text = time.ToString(@"m\:ss"),
                    Font = new FontUsage("", 13),
                    Colour = new Color4(150, 150, 150, 255),
                    RelativePositionAxes = Axes.Both,
                    Position = new Vector2(relX, chartBottom + 0.01f),
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

