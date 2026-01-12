using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using Companella.Mods.Parameters;

namespace Companella.Components.Tools;

/// <summary>
/// A slider control for mod parameters.
/// Displays a title, slider bar, and current value.
/// </summary>
public partial class ParameterSlider : CompositeDrawable, IHasTooltip
{
    private readonly IModParameter _parameter;
    private readonly Color4 _accentColor;

    private Box _sliderBackground = null!;
    private Box _sliderFill = null!;
    private Circle _sliderNub = null!;
    private SpriteText _valueText = null!;
    private Container _sliderContainer = null!;

    private bool _isDragging;

    public LocalisableString TooltipText => _parameter.Description;

    /// <summary>
    /// Event raised when the parameter value changes.
    /// </summary>
    public event Action<IModParameter>? ValueChanged;

    public ParameterSlider(IModParameter parameter, Color4 accentColor)
    {
        _parameter = parameter;
        _accentColor = accentColor;

        RelativeSizeAxes = Axes.X;
        Height = 48;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    // Title row with name and value
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 16,
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = _parameter.Name,
                                Font = new FontUsage("", 14),
                                Colour = Color4.White,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            _valueText = new SpriteText
                            {
                                Text = _parameter.GetDisplayValue(),
                                Font = new FontUsage("", 14, "Bold"),
                                Colour = _accentColor,
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight
                            }
                        }
                    },
                    // Slider bar
                    _sliderContainer = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 24,
                        Children = new Drawable[]
                        {
                            // Background track
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 6,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Masking = true,
                                CornerRadius = 3,
                                Children = new Drawable[]
                                {
                                    _sliderBackground = new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = new Color4(50, 50, 55, 255)
                                    },
                                    _sliderFill = new Box
                                    {
                                        RelativeSizeAxes = Axes.Y,
                                        Width = 0,
                                        Colour = _accentColor
                                    }
                                }
                            },
                            // Nub
                            _sliderNub = new Circle
                            {
                                Size = new Vector2(14, 14),
                                Colour = Color4.White,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.Centre,
                                X = 0
                            }
                        }
                    }
                }
            }
        };

        UpdateSliderPosition();
    }

    private void UpdateSliderPosition()
    {
        var normalized = _parameter.GetNormalizedValue();
        var sliderWidth = _sliderContainer.DrawWidth;
        
        // Clamp the nub position within bounds (accounting for nub radius)
        var nubRadius = 7;
        var usableWidth = sliderWidth - nubRadius * 2;
        var nubX = nubRadius + (float)(normalized * usableWidth);
        
        _sliderNub.X = nubX;
        _sliderFill.Width = nubX;
        _valueText.Text = _parameter.GetDisplayValue();
    }

    protected override void Update()
    {
        base.Update();
        
        // Update position in case the container resized
        if (!_isDragging)
        {
            UpdateSliderPosition();
        }
    }

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (_sliderContainer.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
        {
            _isDragging = true;
            HandleDrag(e.MousePosition);
            return true;
        }
        return base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseUpEvent e)
    {
        _isDragging = false;
        base.OnMouseUp(e);
    }

    protected override bool OnDragStart(DragStartEvent e) => _isDragging;

    protected override void OnDrag(DragEvent e)
    {
        if (_isDragging)
        {
            HandleDrag(e.MousePosition);
        }
    }

    private void HandleDrag(Vector2 mousePosition)
    {
        var sliderWidth = _sliderContainer.DrawWidth;
        var nubRadius = 7;
        var usableWidth = sliderWidth - nubRadius * 2;
        
        // Calculate normalized value from mouse position
        var localX = mousePosition.X - _sliderContainer.DrawPosition.X;
        var normalized = (localX - nubRadius) / usableWidth;
        normalized = Math.Clamp(normalized, 0, 1);
        
        _parameter.SetNormalizedValue(normalized);
        UpdateSliderPosition();
        ValueChanged?.Invoke(_parameter);
    }

    protected override bool OnHover(HoverEvent e)
    {
        _sliderNub.ScaleTo(1.2f, 100, Easing.OutQuad);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _sliderNub.ScaleTo(1f, 100, Easing.OutQuad);
        base.OnHoverLost(e);
    }

    /// <summary>
    /// Refreshes the display to match the current parameter value.
    /// </summary>
    public void Refresh()
    {
        UpdateSliderPosition();
    }
}
