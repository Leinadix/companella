using System.Runtime.InteropServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osuTK;
using osuTK.Graphics;
using Companella.Models.Session;

namespace Companella.Components.Charts;

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
    private const float TrendLineThickness = 3f;
    private const int MovingAverageWindow = 15;

    // Default range values
    public const float DefaultAccuracyMin = 80f;
    public const float DefaultAccuracyMax = 100f;
    public const float DefaultMsdMin = 0f;
    public const float DefaultMsdMax = 40f;

    // Zoom and pan constants
    private const float MinZoom = 1f;
    private const float MaxZoom = 60f;
    private const float ZoomStep = 0.15f;

    // Performance: max points to render (downsample if more)
    private const int MaxVisiblePoints = 500;

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
    private Container _trendLinesContainer = null!;
    private Container _regressionLinesContainer = null!;
    private Container _accuracyPointsContainer = null!;
    private Container _accuracyLinesContainer = null!;
    private Container _gridContainer = null!;
    private Container _labelsContainer = null!;
    private Container _legendContainer = null!;
    private SpriteText _noDataText = null!;

    private SkillsTrendResult? _trendData;
    private float _msdMin = DefaultMsdMin;
    private float _msdMax = DefaultMsdMax;
    private float _accMin = DefaultAccuracyMin;
    private float _accMax = DefaultAccuracyMax;
    private DateTime _startTime;
    private DateTime _endTime;
    
    // User-defined range overrides (null = auto-calculate)
    private float? _userMsdMin;
    private float? _userMsdMax;
    private float? _userAccMin;
    private float? _userAccMax;

    // Sorted plays for index-based positioning (even distribution on X-axis)
    private List<SkillsPlayData> _sortedPlays = new();

    // Zoom and pan state
    private float _zoomLevel = 1f;
    private float _viewOffset = 0f; // 0 = start, 1-viewWidth = end
    private bool _isDragging;
    private Vector2 _dragCenterScreen; // Screen position to reset cursor to during drag
    
    // Buffered rendering state
    private float _renderedCenterOffset; // Center of the rendered range in data space
    private float _renderedViewOffset; // View offset when last rendered (for calculating visual translation)

    // Skillset filtering
    private HashSet<string> _selectedSkillsets = new();
    private string? _hoveredSkillset;
    private Dictionary<string, List<Drawable>> _skillsetDrawables = new();

    // Injected dependencies
    [Resolved]
    private GameHost _host { get; set; } = null!;
    
    // Windows API for cursor repositioning
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
    
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

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
                Masking = true, // Clip content that extends beyond bounds
                Children = new Drawable[]
                {
                    _gridContainer = new Container { RelativeSizeAxes = Axes.Both },
                    _regressionLinesContainer = new Container { RelativeSizeAxes = Axes.Both },
                    _trendLinesContainer = new Container { RelativeSizeAxes = Axes.Both },
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
        // Reset zoom and pan when new data is loaded
        _zoomLevel = 1f;
        _viewOffset = 0f;
        Redraw();
    }

    /// <summary>
    /// Clears the chart.
    /// </summary>
    public void Clear()
    {
        _trendData = null;
        _zoomLevel = 1f;
        _viewOffset = 0f;
        Redraw();
    }

    /// <summary>
    /// Sets the display ranges for MSD and Accuracy axes.
    /// Pass null for any parameter to use auto-calculated values.
    /// </summary>
    public void SetRanges(float? msdMin, float? msdMax, float? accMin, float? accMax)
    {
        _userMsdMin = msdMin;
        _userMsdMax = msdMax;
        _userAccMin = accMin;
        _userAccMax = accMax;
        Redraw();
    }

    /// <summary>
    /// Resets zoom and pan to default.
    /// </summary>
    public void ResetView()
    {
        _zoomLevel = 1f;
        _viewOffset = 0f;
        Redraw();
    }

    protected override bool OnScroll(ScrollEvent e)
    {
        if (_sortedPlays.Count == 0) return base.OnScroll(e);

        // Get mouse position relative to chart area
        var mousePos = e.MousePosition;
        var chartBounds = _chartArea.DrawRectangle;
        
        if (!chartBounds.Contains(mousePos))
            return base.OnScroll(e);

        // Calculate relative X position in the chart (0-1)
        float relativeX = (mousePos.X - chartBounds.Left) / chartBounds.Width;
        relativeX = Math.Clamp(relativeX, 0, 1);

        // Calculate the raw data position under the mouse before zoom
        float viewWidth = 1f / _zoomLevel;
        float rawXUnderMouse = relativeX * viewWidth + _viewOffset;

        // Apply zoom
        if (e.ScrollDelta.Y > 0)
            _zoomLevel = Math.Min(MaxZoom, _zoomLevel * (1 + ZoomStep));
        else
            _zoomLevel = Math.Max(MinZoom, _zoomLevel * (1 - ZoomStep));

        // Adjust offset to keep the same data point under the mouse
        float newViewWidth = 1f / _zoomLevel;
        _viewOffset = rawXUnderMouse - relativeX * newViewWidth;

        // Clamp offset to valid range
        ClampViewOffset();

        Redraw();
        return true;
    }

    protected override bool OnDragStart(DragStartEvent e)
    {
        if (e.Button != osuTK.Input.MouseButton.Left)
            return base.OnDragStart(e);

        var chartBounds = _chartArea.DrawRectangle;
        if (!chartBounds.Contains(e.MousePosition))
            return base.OnDragStart(e);

        _isDragging = true;
        
        // Store the screen position to reset cursor to (center of chart area)
        // ToScreenSpace gives window-relative coords, need to add window position for absolute screen coords
        var chartCenterLocal = _chartArea.ToScreenSpace(new Vector2(chartBounds.Width / 2, chartBounds.Height / 2));
        if (_host.Window != null)
        {
            var windowPos = _host.Window.Position;
            _dragCenterScreen = new Vector2(windowPos.X + chartCenterLocal.X, windowPos.Y + chartCenterLocal.Y);
        }
        else
        {
            _dragCenterScreen = chartCenterLocal;
        }
        
        // Hide and confine cursor during drag
        if (_host.Window != null)
            _host.Window.CursorState |= CursorState.Hidden | CursorState.Confined;
        
        // Move cursor to center initially
        SetCursorPos((int)_dragCenterScreen.X, (int)_dragCenterScreen.Y);
        
        return true;
    }

    protected override void OnDrag(DragEvent e)
    {
        if (!_isDragging) return;

        // Get current cursor position
        if (!GetCursorPos(out POINT cursorPos))
            return;
        
        // Calculate delta from center
        float deltaX = cursorPos.X - _dragCenterScreen.X;
        
        // Reset cursor back to center for infinite dragging
        SetCursorPos((int)_dragCenterScreen.X, (int)_dragCenterScreen.Y);
        
        // Skip if no movement
        if (Math.Abs(deltaX) < 0.5f) return;

        // Use DrawSize for local coordinate calculations (DrawRectangle is screen space)
        float chartWidth = _pointsContainer.DrawSize.X;
        float viewWidth = 1f / _zoomLevel;
        
        // Convert pixel delta to data units (deltaX is in screen pixels, so scale appropriately)
        float scaleFactor = chartWidth / _chartArea.DrawRectangle.Width;
        float localDeltaX = deltaX * scaleFactor;
        float deltaData = localDeltaX / chartWidth * viewWidth * -1;
        
        // Calculate new offset (but don't apply yet)
        float newViewOffset = _viewOffset + deltaData;
        float maxOffset = Math.Max(0, 1f - viewWidth);
        newViewOffset = Math.Clamp(newViewOffset, 0, maxOffset);
        
        // Calculate how far we'd be from rendered center with new offset
        float newViewCenter = newViewOffset + viewWidth / 2f;
        float distanceFromRenderCenter = Math.Abs(newViewCenter - _renderedCenterOffset);
        float distancePercent = distanceFromRenderCenter / viewWidth;
        
        // Apply the new offset first
        _viewOffset = newViewOffset;
        
        // If we've dragged beyond 80% of visible width from render center, re-render
        // (we have 150% buffer on each side, so this leaves 70% margin)
        if (distancePercent >= 0.8f)
        {
            // Re-render at current position - this sets _renderedViewOffset = _viewOffset
            Redraw();
        }
        
        // Always calculate and apply visual offset (difference from where we rendered)
        // After re-render: _renderedViewOffset = _viewOffset, so offset = 0
        // Without re-render: offset = accumulated movement since last render
        float offsetFromRender = _viewOffset - _renderedViewOffset;
        float visualOffsetPx = -offsetFromRender / viewWidth * chartWidth;
        
        _pointsContainer.X = visualOffsetPx;
        _linesContainer.X = visualOffsetPx;
        _trendLinesContainer.X = visualOffsetPx;
        _regressionLinesContainer.X = visualOffsetPx;
        _accuracyPointsContainer.X = visualOffsetPx;
        _accuracyLinesContainer.X = visualOffsetPx;
    }

    protected override void OnDragEnd(DragEndEvent e)
    {
        if (_isDragging)
        {
            // Restore cursor state
            if (_host.Window != null)
                _host.Window.CursorState &= ~(CursorState.Hidden | CursorState.Confined);
            
            // Redraw at final position
            Redraw();
            
            // Reset container offsets (after Redraw, _renderedViewOffset = _viewOffset, so offset should be 0)
            _pointsContainer.X = 0;
            _linesContainer.X = 0;
            _trendLinesContainer.X = 0;
            _regressionLinesContainer.X = 0;
            _accuracyPointsContainer.X = 0;
            _accuracyLinesContainer.X = 0;
        }
        _isDragging = false;
        base.OnDragEnd(e);
    }

    private void ClampViewOffset()
    {
        float viewWidth = 1f / _zoomLevel;
        float maxOffset = Math.Max(0, 1f - viewWidth);
        _viewOffset = Math.Clamp(_viewOffset, 0, maxOffset);
    }

    /// <summary>
    /// Sets the skillset filter. Only selected skillsets will be shown at full opacity.
    /// Pass null or empty set to show all skillsets.
    /// </summary>
    public void SetSkillsetFilter(HashSet<string>? selectedSkillsets)
    {
        _selectedSkillsets = selectedSkillsets ?? new HashSet<string>();
        UpdateSkillsetVisibility();
    }

    /// <summary>
    /// Sets the hovered skillset. When set, only this skillset is shown at full opacity.
    /// Pass null to clear hover state and restore based on selection.
    /// </summary>
    public void SetHoveredSkillset(string? skillset)
    {
        _hoveredSkillset = skillset?.ToLowerInvariant();
        UpdateSkillsetVisibility();
    }

    private void UpdateSkillsetVisibility(bool animate = true)
    {
        double duration = animate ? 100 : 0;
        
        foreach (var (key, drawables) in _skillsetDrawables)
        {
            if (drawables.Count == 0) continue;
            
            var container = drawables[0];
            float targetAlpha;

            // Check if this is an accuracy container (format: _acc_{skillset})
            if (key.StartsWith("_acc_"))
            {
                var skillset = key.Substring(5); // Remove "_acc_" prefix
                targetAlpha = GetSkillsetAlpha(skillset);
            }
            else
            {
                // Regular skillset container
                targetAlpha = GetSkillsetAlpha(key);
            }
            
            // Fade the container and all its children directly
            container.FadeTo(targetAlpha, duration);
            
            // Also fade all children explicitly (for nested elements like SkillsLineSegment)
            if (container is Container<Drawable> c)
            {
                foreach (var child in c.Children)
                {
                    child.FadeTo(targetAlpha, duration);
                }
            }
        }
    }

    private float GetSkillsetAlpha(string skillset)
    {
        // If there's a hovered skillset, only that one is visible
        if (_hoveredSkillset != null)
        {
            return skillset == _hoveredSkillset ? 1f : 0.1f;
        }

        // If there are selected skillsets, only those are visible
        if (_selectedSkillsets.Count > 0)
        {
            return _selectedSkillsets.Contains(skillset) ? 1f : 0.1f;
        }

        // No filter - show all
        return 1f;
    }

    private void Redraw()
    {
        _pointsContainer.Clear();
        _linesContainer.Clear();
        _trendLinesContainer.Clear();
        _regressionLinesContainer.Clear();
        _accuracyPointsContainer.Clear();
        _accuracyLinesContainer.Clear();
        _gridContainer.Clear();
        _labelsContainer.Clear();
        _legendContainer.Clear();
        _skillsetDrawables.Clear();

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

        // Set render center and view offset (for buffered rendering and visual translation)
        float viewWidth = 1f / _zoomLevel;
        _renderedCenterOffset = _viewOffset + viewWidth / 2f;
        _renderedViewOffset = _viewOffset;

        CalculateRanges();
        DrawGrid();
        DrawAxisLabels();
        DrawLegend();
        DrawMsdData();
        DrawTrendLines();
        DrawAccuracyData();
        
        // Apply current visibility state immediately (no animation during redraw)
        UpdateSkillsetVisibility(animate: false);
    }

    private void CalculateRanges()
    {
        _sortedPlays.Clear();

        if (_trendData == null || _trendData.Plays.Count == 0)
        {
            _msdMin = _userMsdMin ?? DefaultMsdMin;
            _msdMax = _userMsdMax ?? DefaultMsdMax;
            _accMin = _userAccMin ?? DefaultAccuracyMin;
            _accMax = _userAccMax ?? DefaultAccuracyMax;
            _startTime = DateTime.UtcNow.AddDays(-7);
            _endTime = DateTime.UtcNow;
            return;
        }

        // Sort plays by time for index-based positioning
        _sortedPlays = _trendData.Plays.OrderBy(p => p.PlayedAt).ToList();

        // MSD range: use user values if set, otherwise auto-calculate
        if (_userMsdMin.HasValue && _userMsdMax.HasValue)
        {
            _msdMin = _userMsdMin.Value;
            _msdMax = _userMsdMax.Value;
        }
        else
        {
            var maxMsd = Math.Min(50, _trendData.Plays.Max(p => p.HighestMsdValue));
            var minMsd = Math.Max(0, _trendData.Plays.Min(p => p.HighestMsdValue));

            var msdRange = maxMsd - minMsd;
            if (msdRange < 5) msdRange = 5;

            _msdMin = _userMsdMin ?? Math.Max(0, minMsd - msdRange * 0.1f);
            _msdMax = _userMsdMax ?? (maxMsd + msdRange * 0.1f);

            if (_msdMax - _msdMin < 5)
            {
                _msdMax = _msdMin + 5;
            }
        }

        // Accuracy range: use user values if set, otherwise use defaults
        _accMin = _userAccMin ?? DefaultAccuracyMin;
        _accMax = _userAccMax ?? DefaultAccuracyMax;

        _startTime = _trendData.StartTime;
        _endTime = _trendData.EndTime;

        if (_endTime <= _startTime)
        {
            _endTime = _startTime.AddDays(1);
        }
    }

    /// <summary>
    /// Gets the X position (0-1) for a play at the given index using even distribution.
    /// Accounts for zoom and pan.
    /// </summary>
    private float GetXPositionByIndex(int index)
    {
        if (_sortedPlays.Count <= 1) return 0.5f;
        
        // Raw position (0-1) based on index
        float rawX = (float)index / (_sortedPlays.Count - 1);
        
        // Apply zoom and pan transformation
        float viewWidth = 1f / _zoomLevel;
        float visibleX = (rawX - _viewOffset) / viewWidth;
        
        return visibleX;
    }

    /// <summary>
    /// Gets the raw X position (0-1) without zoom/pan for a play index.
    /// </summary>
    private float GetRawXPositionByIndex(int index)
    {
        if (_sortedPlays.Count <= 1) return 0.5f;
        return (float)index / (_sortedPlays.Count - 1);
    }

    /// <summary>
    /// Gets the play index corresponding to a visible X position (0-1).
    /// </summary>
    private int GetIndexAtVisibleX(float visibleX)
    {
        if (_sortedPlays.Count <= 1) return 0;
        
        float viewWidth = 1f / _zoomLevel;
        float rawX = visibleX * viewWidth + _viewOffset;
        
        int index = (int)Math.Round(rawX * (_sortedPlays.Count - 1));
        return Math.Clamp(index, 0, _sortedPlays.Count - 1);
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
            float value = _accMax - (_accMax - _accMin) * (i / 4f);
            float y = ChartPaddingTop + (DrawHeight - ChartPaddingTop - ChartPaddingBottom) * (i / 4f);

            _labelsContainer.Add(new SpriteText
            {
                Text = $"{value:F1}%",
                Font = new FontUsage("", 16),
                Colour = Color4.White,
                Position = new Vector2(DrawWidth - 5, y - 5),
                Origin = Anchor.CentreRight
            });
        }

        // Time axis labels (bottom) - show dates at evenly spaced positions based on visible range
        int timeLines = 4;
        var timeRange = _endTime - _startTime;
        var format = timeRange.TotalDays > 365 ? "MMM yy" : "MMM dd yy";
        
        for (int i = 0; i <= timeLines; i++)
        {
            float x = ChartPaddingLeft + (DrawWidth - ChartPaddingLeft - ChartPaddingRight) * (i / (float)timeLines);

            DateTime time;
            if (_sortedPlays.Count > 0)
            {
                // Get the play index at this visible position (accounting for zoom/pan)
                int playIndex = GetIndexAtVisibleX(i / (float)timeLines);
                time = _sortedPlays[playIndex].PlayedAt;
            }
            else
            {
                // Fallback to linear time distribution if no plays
                time = _startTime.AddTicks((long)(timeRange.Ticks * (i / (float)timeLines)));
            }

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

    private void DrawMsdData()
    {
        if (_trendData == null || _sortedPlays.Count == 0) return;

        int totalCount = _sortedPlays.Count;
        float viewWidth = 1f / _zoomLevel;

        // Calculate buffered render range: 3x visible width centered on _renderedCenterOffset
        float renderStart = Math.Max(0, _renderedCenterOffset - 1.5f * viewWidth);
        float renderEnd = Math.Min(1f, _renderedCenterOffset + 1.5f * viewWidth);
        
        // Convert to index range
        int startIdx = (int)Math.Floor(renderStart * (totalCount - 1));
        int endIdx = (int)Math.Ceiling(renderEnd * (totalCount - 1));
        startIdx = Math.Max(0, startIdx);
        endIdx = Math.Min(totalCount - 1, endIdx);
        int renderCount = endIdx - startIdx + 1;

        // Calculate downsampling step based on render range and zoom
        int targetPoints = (int)(MaxVisiblePoints * Math.Max(1f, _zoomLevel));
        int step = Math.Max(1, renderCount / targetPoints);

        var pointsBySkillset = new Dictionary<string, List<Vector2>>();

        // Render only the buffered range
        for (int i = startIdx; i <= endIdx; i += step)
        {
            var play = _sortedPlays[i];
            var x = GetXPositionByIndex(i);
            var y = 1f - (play.HighestMsdValue - _msdMin) / (_msdMax - _msdMin);

            // Only clamp Y, not X - allows points to exist off-screen for smooth panning
            y = Math.Clamp(y, 0, 1);

            var skillset = play.DominantSkillset.ToLowerInvariant();
            var color = SkillsetColors.GetValueOrDefault(skillset, SkillsetColors["unknown"]);

            // Draw point - use skillset container for efficient filtering
            var skillsetContainer = GetOrCreateSkillsetContainer(skillset);
            skillsetContainer.Add(new Circle
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

        // Draw connecting lines per skillset (skip lines for performance with many points)
        bool drawLines = renderCount / step <= MaxVisiblePoints * 2;
        if (drawLines)
        {
            foreach (var (skillset, points) in pointsBySkillset)
            {
                if (points.Count < 2) continue;

                var skillsetContainer = GetOrCreateSkillsetContainer(skillset);
                var color = SkillsetColors.GetValueOrDefault(skillset, SkillsetColors["unknown"]);
                for (int i = 1; i < points.Count; i++)
                {
                    skillsetContainer.Add(new SkillsLineSegment(points[i - 1], points[i], color, LineThickness * 0.7f));
                }
            }
        }
    }

    private Container GetOrCreateSkillsetContainer(string skillset)
    {
        if (!_skillsetDrawables.ContainsKey(skillset))
        {
            var container = new Container 
            { 
                RelativeSizeAxes = Axes.Both,
                Alpha = GetSkillsetAlpha(skillset) // Set initial alpha based on current filter
            };
            _skillsetDrawables[skillset] = new List<Drawable> { container };
            _pointsContainer.Add(container);
        }
        return (Container)_skillsetDrawables[skillset][0];
    }

    private void DrawTrendLines()
    {
        if (_trendData == null || _sortedPlays.Count < 3) return;

        int totalCount = _sortedPlays.Count;
        float viewWidth = 1f / _zoomLevel;

        // Calculate buffered render range: 3x visible width centered on _renderedCenterOffset
        float renderStart = Math.Max(0, _renderedCenterOffset - 1.5f * viewWidth);
        float renderEnd = Math.Min(1f, _renderedCenterOffset + 1.5f * viewWidth);
        
        // Convert to index range
        int startIdx = (int)Math.Floor(renderStart * (totalCount - 1));
        int endIdx = (int)Math.Ceiling(renderEnd * (totalCount - 1));
        startIdx = Math.Max(0, startIdx);
        endIdx = Math.Min(totalCount - 1, endIdx);

        // Group plays by skillset with their indices (only within buffered range)
        var playsBySkillset = new Dictionary<string, List<(int index, float msd)>>();
        
        for (int i = startIdx; i <= endIdx; i++)
        {
            var play = _sortedPlays[i];
            var skillset = play.DominantSkillset.ToLowerInvariant();
            
            if (!playsBySkillset.ContainsKey(skillset))
                playsBySkillset[skillset] = new List<(int, float)>();
            
            playsBySkillset[skillset].Add((i, play.HighestMsdValue));
        }

        foreach (var (skillset, plays) in playsBySkillset)
        {
            if (plays.Count < 2) continue;

            var color = SkillsetColors.GetValueOrDefault(skillset, SkillsetColors["unknown"]);
            
            // Draw moving average trend line
            DrawMovingAverageLine(plays, color, skillset);
            
            // Draw linear regression line
            if (plays.Count >= 3)
            {
                DrawRegressionLine(plays, color, skillset);
            }
        }
    }

    private void DrawMovingAverageLine(List<(int index, float msd)> plays, Color4 color, string skillset)
    {
        if (plays.Count < 2) return;

        // Downsample for trend line calculation too
        int step = Math.Max(1, plays.Count / 100);
        
        var movingAvgPoints = new List<Vector2>();
        int windowSize = Math.Min(MovingAverageWindow, Math.Max(3, plays.Count / 5));

        for (int i = 0; i < plays.Count; i += step)
        {
            // Calculate moving average centered on current point
            int start = Math.Max(0, i - windowSize / 2);
            int end = Math.Min(plays.Count - 1, i + windowSize / 2);
            
            float sum = 0;
            int count = 0;
            for (int j = start; j <= end; j++)
            {
                sum += plays[j].msd;
                count++;
            }
            float avgMsd = sum / count;

            var x = GetXPositionByIndex(plays[i].index);
            var y = 1f - (avgMsd - _msdMin) / (_msdMax - _msdMin);
            
            // Only clamp Y, not X
            y = Math.Clamp(y, 0, 1);
            movingAvgPoints.Add(new Vector2(x, y));
        }

        // Get skillset container and draw trend line
        var skillsetContainer = GetOrCreateSkillsetContainer(skillset);
        var trendColor = new Color4(color.R, color.G, color.B, 0.7f);
        for (int i = 1; i < movingAvgPoints.Count; i++)
        {
            skillsetContainer.Add(new SkillsLineSegment(movingAvgPoints[i - 1], movingAvgPoints[i], trendColor, TrendLineThickness));
        }
    }

    private void DrawRegressionLine(List<(int index, float msd)> plays, Color4 color, string skillset)
    {
        if (plays.Count < 3) return;

        // Calculate linear regression using least squares
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        int n = plays.Count;

        for (int i = 0; i < n; i++)
        {
            double x = plays[i].index;
            double y = plays[i].msd;
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        double denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 0.0001) return; // Avoid division by zero

        double slope = (n * sumXY - sumX * sumY) / denominator;
        double intercept = (sumY - slope * sumX) / n;

        // Calculate start and end points of regression line
        int startIndex = plays[0].index;
        int endIndex = plays[^1].index;

        float startMsd = (float)(slope * startIndex + intercept);
        float endMsd = (float)(slope * endIndex + intercept);

        float x1 = GetXPositionByIndex(startIndex);
        float y1 = 1f - (startMsd - _msdMin) / (_msdMax - _msdMin);
        float x2 = GetXPositionByIndex(endIndex);
        float y2 = 1f - (endMsd - _msdMin) / (_msdMax - _msdMin);

        // Only clamp Y, not X - allows line to extend off-screen for smooth panning
        y1 = Math.Clamp(y1, 0, 1);
        y2 = Math.Clamp(y2, 0, 1);

        // Draw dashed regression line into skillset container
        var skillsetContainer = GetOrCreateSkillsetContainer(skillset);
        var regressionColor = new Color4(color.R, color.G, color.B, 0.5f);
        DrawDashedLine(skillsetContainer, new Vector2(x1, y1), new Vector2(x2, y2), regressionColor, LineThickness * 1.5f);
    }

    private void DrawDashedLine(Container container, Vector2 from, Vector2 to, Color4 color, float thickness)
    {
        const float dashLength = 0.02f;
        const float gapLength = 0.01f;
        
        var direction = to - from;
        var totalLength = direction.Length;
        if (totalLength < 0.001f) return;
        
        direction /= totalLength;
        
        float currentPos = 0;
        bool isDash = true;

        while (currentPos < totalLength)
        {
            float segmentLength = isDash ? dashLength : gapLength;
            float endPos = Math.Min(currentPos + segmentLength, totalLength);
            
            if (isDash)
            {
                var segmentStart = from + direction * currentPos;
                var segmentEnd = from + direction * endPos;
                container.Add(new SkillsLineSegment(segmentStart, segmentEnd, color, thickness));
            }
            
            currentPos = endPos;
            isDash = !isDash;
        }
    }

    private void DrawAccuracyData()
    {
        if (_trendData == null || _sortedPlays.Count == 0) return;

        int totalCount = _sortedPlays.Count;
        float viewWidth = 1f / _zoomLevel;

        // Calculate buffered render range: 3x visible width centered on _renderedCenterOffset
        float renderStart = Math.Max(0, _renderedCenterOffset - 1.5f * viewWidth);
        float renderEnd = Math.Min(1f, _renderedCenterOffset + 1.5f * viewWidth);
        
        // Convert to index range
        int startIdx = (int)Math.Floor(renderStart * (totalCount - 1));
        int endIdx = (int)Math.Ceiling(renderEnd * (totalCount - 1));
        startIdx = Math.Max(0, startIdx);
        endIdx = Math.Min(totalCount - 1, endIdx);
        int renderCount = endIdx - startIdx + 1;

        // Calculate downsampling step based on render range and zoom
        int targetPoints = (int)(MaxVisiblePoints * Math.Max(1f, _zoomLevel));
        int step = Math.Max(1, renderCount / targetPoints);

        // Build accuracy points per skillset (based on play's dominant skillset)
        var skillsetAccPoints = new Dictionary<string, List<(int index, Vector2 point)>>();

        for (int i = startIdx; i <= endIdx; i += step)
        {
            var play = _sortedPlays[i];
            var x = GetXPositionByIndex(i);
            var normalizedAcc = (float)Math.Clamp(play.Accuracy, _accMin, _accMax);
            var y = 1f - (normalizedAcc - _accMin) / (_accMax - _accMin);

            // Only clamp Y, not X - allows points to exist off-screen for smooth panning
            y = Math.Clamp(y, 0, 1);

            var point = new Vector2(x, y);

            // Add this accuracy point to the play's dominant skillset
            var skillset = play.DominantSkillset.ToLowerInvariant();
            if (!skillsetAccPoints.ContainsKey(skillset))
                skillsetAccPoints[skillset] = new List<(int, Vector2)>();
            skillsetAccPoints[skillset].Add((i, point));
        }

        // Create accuracy container for each skillset
        foreach (var (skillset, points) in skillsetAccPoints)
        {
            var accContainer = new Container 
            { 
                RelativeSizeAxes = Axes.Both,
                Alpha = GetSkillsetAlpha(skillset) // Set initial alpha based on current filter
            };
            
            // Add to the skillset's drawable list (create accuracy key)
            var accKey = $"_acc_{skillset}";
            _skillsetDrawables[accKey] = new List<Drawable> { accContainer };
            _accuracyPointsContainer.Add(accContainer);

            var baseColor = SkillsetColors.GetValueOrDefault(skillset, SkillsetColors["unknown"]);
            // Use a lighter/whiter version of the skillset color for accuracy
            var accColor = new Color4(
                (byte)Math.Min(255, baseColor.R * 255 * 0.7f + 80),
                (byte)Math.Min(255, baseColor.G * 255 * 0.7f + 80),
                (byte)Math.Min(255, baseColor.B * 255 * 0.7f + 80),
                180);

            // Draw points
            foreach (var (_, point) in points)
            {
                accContainer.Add(new Circle
                {
                    Size = new Vector2(PointRadius * 1.5f),
                    RelativePositionAxes = Axes.Both,
                    Position = point,
                    Origin = Anchor.Centre,
                    Colour = accColor
                });
            }

            // Draw connecting lines (skip for very large datasets)
            if (points.Count <= MaxVisiblePoints * 2)
            {
                var lineColor = new Color4(accColor.R, accColor.G, accColor.B, (byte)120);
                for (int i = 1; i < points.Count; i++)
                {
                    accContainer.Add(new SkillsLineSegment(points[i - 1].point, points[i].point, lineColor, LineThickness * 0.5f));
                }
            }
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
        private Box? _lineBox;
        private bool _initialized;

        public SkillsLineSegment(Vector2 from, Vector2 to, Color4 color, float thickness)
        {
            _from = from;
            _to = to;
            _color = color;
            _thickness = thickness;
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            _lineBox = new Box
            {
                Height = _thickness,
                Origin = Anchor.CentreLeft,
                Colour = _color
            };
            AddInternal(_lineBox);
        }

        protected override void Update()
        {
            base.Update();

            if (_lineBox == null) return;

            // Only recalculate if size changed
            var actualFrom = new Vector2(_from.X * DrawWidth, _from.Y * DrawHeight);
            var actualTo = new Vector2(_to.X * DrawWidth, _to.Y * DrawHeight);

            var diff = actualTo - actualFrom;
            var length = diff.Length;
            var angle = MathF.Atan2(diff.Y, diff.X);

            _lineBox.Width = length;
            _lineBox.Position = actualFrom;
            _lineBox.Rotation = MathHelper.RadiansToDegrees(angle);
            
            // Sync box alpha with parent alpha
            _lineBox.Alpha = Alpha;
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
