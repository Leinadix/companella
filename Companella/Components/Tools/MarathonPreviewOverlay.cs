using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using Companella.Models.Application;

namespace Companella.Components.Tools;

/// <summary>
/// Interactive overlay for the marathon background preview.
/// Handles click to select shards, drag to pan, and scroll to zoom.
/// </summary>
public partial class MarathonPreviewOverlay : CompositeDrawable
{
    // Preview dimensions (matches GenerateBackgroundPreviewAsync)
    private const int PreviewWidth = 480;
    private const int PreviewHeight = 270;
    
    // Original background dimensions from MarathonCreatorService
    private const float BgWidth = 1920f;
    private const float BgHeight = 1080f;
    private const float InnerRadius = 80f;
    private const float OuterRadius = 1100f;
    
    // Scaled dimensions for preview
    private float ScaleX => DrawWidth / BgWidth;
    private float ScaleY => DrawHeight / BgHeight;
    private float CenterX => DrawWidth / 2f;
    private float CenterY => DrawHeight / 2f;
    private float ScaledInnerRadius => InnerRadius * ScaleX;
    private float ScaledOuterRadius => OuterRadius * ScaleX;
    
    // Pan/zoom sensitivity
    private const float PanSensitivity = 0.01f;
    private const float ZoomSensitivity = 0.1f;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 2.0f;
    
    // State
    private List<MarathonEntry> _entries = new();
    private MarathonEntry? _selectedEntry;
    private int _selectedShardIndex = -1;
    private int _hoveredShardIndex = -1;
    private bool _isDragging;
    private Vector2 _dragStartPosition;
    private float _dragStartPanX;
    private float _dragStartPanY;
    
    // Visual feedback
    private Container _selectionHighlight = null!;
    private Container _hoverHighlight = null!;
    private SpriteText _infoText = null!;
    
    /// <summary>
    /// Event raised when a shard's pan values change.
    /// </summary>
    public event Action<MarathonEntry, float, float>? PanChanged;
    
    /// <summary>
    /// Event raised when a shard's zoom value changes.
    /// </summary>
    public event Action<MarathonEntry, float>? ZoomChanged;
    
    /// <summary>
    /// Event raised when a shard is selected.
    /// </summary>
    public event Action<MarathonEntry?>? SelectionChanged;

    public MarathonPreviewOverlay()
    {
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            _hoverHighlight = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0
            },
            _selectionHighlight = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0
            },
            _infoText = new SpriteText
            {
                Font = new FontUsage("", 11),
                Colour = new Color4(200, 200, 200, 255),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Padding = new MarginPadding(4),
                Alpha = 0
            }
        };
    }

    /// <summary>
    /// Updates the list of entries used for shard calculation.
    /// </summary>
    public void SetEntries(List<MarathonEntry> entries)
    {
        _entries = entries;
        
        // Check if selected entry is still valid
        if (_selectedEntry != null && !GetMapEntries().Contains(_selectedEntry))
        {
            ClearSelection();
        }
        
        UpdateSelectionHighlight();
    }

    /// <summary>
    /// Gets map entries only (excludes pauses).
    /// </summary>
    private List<MarathonEntry> GetMapEntries()
    {
        return _entries.Where(e => !e.IsPause && e.OsuFile != null).ToList();
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        _selectedEntry = null;
        _selectedShardIndex = -1;
        _selectionHighlight.FadeTo(0, 100);
        _infoText.FadeTo(0, 100);
        SelectionChanged?.Invoke(null);
    }

    /// <summary>
    /// Gets the shard index at the given position, or -1 if not on a shard.
    /// </summary>
    private int GetShardIndexFromPosition(Vector2 position)
    {
        var mapEntries = GetMapEntries();
        if (mapEntries.Count == 0)
            return -1;

        // Calculate position relative to center
        float dx = position.X - CenterX;
        float dy = position.Y - CenterY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        
        // Check if within the shard ring (between inner and outer radius)
        if (distance < ScaledInnerRadius || distance > ScaledOuterRadius)
            return -1;
        
        // Calculate angle in degrees (atan2 returns radians, 0 = right, positive = counter-clockwise)
        float angleRad = MathF.Atan2(dy, dx);
        float angleDeg = angleRad * 180f / MathF.PI;
        
        // Calculate shard parameters (matching MarathonCreatorService)
        float anglePerShard = 360f / mapEntries.Count;
        float startAngle = -90f - (anglePerShard / 2f);
        
        // Normalize angle to be relative to start angle
        float normalizedAngle = angleDeg - startAngle;
        
        // Normalize to 0-360 range
        while (normalizedAngle < 0) normalizedAngle += 360f;
        while (normalizedAngle >= 360f) normalizedAngle -= 360f;
        
        // Calculate shard index
        int shardIndex = (int)(normalizedAngle / anglePerShard);
        
        // Clamp to valid range
        shardIndex = Math.Clamp(shardIndex, 0, mapEntries.Count - 1);
        
        return shardIndex;
    }

    /// <summary>
    /// Gets the MarathonEntry for a given shard index.
    /// </summary>
    private MarathonEntry? GetEntryFromShardIndex(int shardIndex)
    {
        var mapEntries = GetMapEntries();
        if (shardIndex < 0 || shardIndex >= mapEntries.Count)
            return null;
        return mapEntries[shardIndex];
    }

    /// <summary>
    /// Updates the visual selection highlight.
    /// </summary>
    private void UpdateSelectionHighlight()
    {
        if (_selectedEntry == null || _selectedShardIndex < 0)
        {
            _selectionHighlight.FadeTo(0, 100);
            _infoText.FadeTo(0, 100);
            return;
        }
        
        // Show info text with current zoom/pan values
        var truncatedTitle = _selectedEntry.Title.Length > 20 
            ? _selectedEntry.Title[..17] + "..." 
            : _selectedEntry.Title;
        _infoText.Text = $"{truncatedTitle} | Zoom:{_selectedEntry.BackgroundZoom:F2} Pan:({_selectedEntry.BackgroundPanX:F2}, {_selectedEntry.BackgroundPanY:F2})";
        _infoText.FadeTo(1, 100);
        
        // Update highlight
        _selectionHighlight.Clear();
        
        var mapEntries = GetMapEntries();
        if (mapEntries.Count == 0) return;
        
        float anglePerShard = 360f / mapEntries.Count;
        float startAngle = -90f - (anglePerShard / 2f);
        float shardStartAngle = startAngle + (_selectedShardIndex * anglePerShard);
        float shardMidAngle = shardStartAngle + (anglePerShard / 2f);
        float midRad = shardMidAngle * MathF.PI / 180f;
        // Position indicator closer to center (15% from inner radius towards outer)
        float midRadius = ScaledInnerRadius + (ScaledOuterRadius - ScaledInnerRadius) * 0.15f;
        
        // Position indicator near inner edge of shard
        float indicatorX = CenterX + midRadius * MathF.Cos(midRad);
        float indicatorY = CenterY + midRadius * MathF.Sin(midRad);
        
        // Draw a selection indicator (ring with glow effect)
        var indicator = new CircularContainer
        {
            Size = new Vector2(24),
            Position = new Vector2(indicatorX, indicatorY),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Masking = true,
            BorderThickness = 3,
            BorderColour = new Color4(255, 102, 170, 255),
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(255, 102, 170, 80)
                }
            }
        };
        
        _selectionHighlight.Add(indicator);
        
        // Add outer glow ring
        _selectionHighlight.Add(new CircularContainer
        {
            Size = new Vector2(32),
            Position = new Vector2(indicatorX, indicatorY),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Masking = true,
            BorderThickness = 2,
            BorderColour = new Color4(255, 102, 170, 100),
            Alpha = 0.5f,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0
                }
            }
        });
        
        _selectionHighlight.FadeTo(1, 100);
    }

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        var position = e.MousePosition;
        int shardIndex = GetShardIndexFromPosition(position);
        
        if (shardIndex >= 0)
        {
            var entry = GetEntryFromShardIndex(shardIndex);
            if (entry != null)
            {
                _selectedEntry = entry;
                _selectedShardIndex = shardIndex;
                _isDragging = true;
                _dragStartPosition = position;
                _dragStartPanX = entry.BackgroundPanX;
                _dragStartPanY = entry.BackgroundPanY;
                
                UpdateSelectionHighlight();
                SelectionChanged?.Invoke(entry);
                
                return true;
            }
        }
        
        return base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseUpEvent e)
    {
        _isDragging = false;
        base.OnMouseUp(e);
    }

    protected override bool OnDragStart(DragStartEvent e)
    {
        if (_selectedEntry != null && _isDragging)
        {
            return true;
        }
        return base.OnDragStart(e);
    }

    protected override void OnDrag(DragEvent e)
    {
        if (_selectedEntry != null && _isDragging)
        {
            // Calculate pan delta based on drag distance
            Vector2 dragDelta = e.MousePosition - _dragStartPosition;
            
            // Drag moves the center point on the source image
            // Drag right = center point moves right = show more of right side of image
            float panDeltaX = dragDelta.X * PanSensitivity;
            float panDeltaY = dragDelta.Y * PanSensitivity;
            
            // Apply delta to starting pan values (no clamping)
            float newPanX = _dragStartPanX + panDeltaX;
            float newPanY = _dragStartPanY + panDeltaY;
            
            // Update entry and raise event
            _selectedEntry.BackgroundPanX = newPanX;
            _selectedEntry.BackgroundPanY = newPanY;
            
            UpdateSelectionHighlight();
            PanChanged?.Invoke(_selectedEntry, newPanX, newPanY);
        }
        
        base.OnDrag(e);
    }

    protected override void OnDragEnd(DragEndEvent e)
    {
        _isDragging = false;
        base.OnDragEnd(e);
    }

    protected override bool OnScroll(ScrollEvent e)
    {
        // Check if mouse is over a shard
        var position = e.MousePosition;
        int shardIndex = GetShardIndexFromPosition(position);
        
        MarathonEntry? targetEntry = null;
        
        if (shardIndex >= 0)
        {
            targetEntry = GetEntryFromShardIndex(shardIndex);
            
            // If scrolling over a different shard, select it
            if (targetEntry != null && targetEntry != _selectedEntry)
            {
                _selectedEntry = targetEntry;
                _selectedShardIndex = shardIndex;
                SelectionChanged?.Invoke(targetEntry);
            }
        }
        else if (_selectedEntry != null)
        {
            // Use currently selected entry if mouse is not over any shard
            targetEntry = _selectedEntry;
        }
        
        if (targetEntry != null)
        {
            // Calculate zoom delta
            float scrollDelta = e.ScrollDelta.Y;
            float zoomMultiplier = 1f + (scrollDelta * ZoomSensitivity);
            
            // Apply zoom with exponential scaling for smooth feel
            float newZoom = targetEntry.BackgroundZoom * zoomMultiplier;
            newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
            
            // Update entry and raise event
            targetEntry.BackgroundZoom = newZoom;
            
            UpdateSelectionHighlight();
            ZoomChanged?.Invoke(targetEntry, newZoom);
            
            return true;
        }
        
        return base.OnScroll(e);
    }

    protected override bool OnHover(HoverEvent e)
    {
        return true; // Accept hover to receive mouse move events
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoveredShardIndex = -1;
        _hoverHighlight.FadeTo(0, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnMouseMove(MouseMoveEvent e)
    {
        if (!_isDragging)
        {
            var position = e.MousePosition;
            int shardIndex = GetShardIndexFromPosition(position);
            
            if (shardIndex != _hoveredShardIndex)
            {
                _hoveredShardIndex = shardIndex;
                UpdateHoverHighlight();
            }
        }
        
        return base.OnMouseMove(e);
    }

    /// <summary>
    /// Updates the hover highlight to show which shard the user is hovering over.
    /// </summary>
    private void UpdateHoverHighlight()
    {
        // Don't show hover highlight for already selected shard
        if (_hoveredShardIndex < 0 || _hoveredShardIndex == _selectedShardIndex)
        {
            _hoverHighlight.FadeTo(0, 100);
            return;
        }
        
        _hoverHighlight.Clear();
        
        var mapEntries = GetMapEntries();
        if (mapEntries.Count == 0) return;
        
        float anglePerShard = 360f / mapEntries.Count;
        float startAngle = -90f - (anglePerShard / 2f);
        float shardStartAngle = startAngle + (_hoveredShardIndex * anglePerShard);
        float shardMidAngle = shardStartAngle + (anglePerShard / 2f);
        float midRad = shardMidAngle * MathF.PI / 180f;
        // Position indicator closer to center (15% from inner radius towards outer)
        float midRadius = ScaledInnerRadius + (ScaledOuterRadius - ScaledInnerRadius) * 0.15f;
        
        // Position indicator near inner edge of shard
        float indicatorX = CenterX + midRadius * MathF.Cos(midRad);
        float indicatorY = CenterY + midRadius * MathF.Sin(midRad);
        
        // Draw a subtle hover indicator
        _hoverHighlight.Add(new CircularContainer
        {
            Size = new Vector2(18),
            Position = new Vector2(indicatorX, indicatorY),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Masking = true,
            BorderThickness = 2,
            BorderColour = new Color4(255, 255, 255, 150),
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(255, 255, 255, 40)
                }
            }
        });
        
        _hoverHighlight.FadeTo(1, 100);
    }
}
