using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace OsuMappingHelper.Components;

/// <summary>
/// A loading overlay with blur effect and osu!lazer style spinner.
/// </summary>
public partial class LoadingOverlay : CompositeDrawable
{
    private Container _content = null!;
    private Box _dimBox = null!;
    private LoadingSpinner _spinner = null!;
    private SpriteText _statusText = null!;

    public LoadingOverlay()
    {
        RelativeSizeAxes = Axes.Both;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            // Dim/blur background
            _dimBox = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0, 0, 0, 180)
            },
            // Content container
            _content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 20),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Children = new Drawable[]
                        {
                            _spinner = new LoadingSpinner
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Size = new Vector2(80)
                            },
                            _statusText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Font = new FontUsage("", 18),
                                Colour = Color4.White,
                                Text = "Loading..."
                            }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Shows the loading overlay with the specified status message.
    /// </summary>
    public void Show(string status)
    {
        _statusText.Text = status;
        _spinner.Show();
        this.FadeIn(200, Easing.OutQuint);
    }

    /// <summary>
    /// Updates the status message while loading.
    /// </summary>
    public void UpdateStatus(string status)
    {
        _statusText.Text = status;
    }

    /// <summary>
    /// Hides the loading overlay.
    /// </summary>
    public new void Hide()
    {
        _spinner.Hide();
        this.FadeOut(200, Easing.OutQuint);
    }
}

/// <summary>
/// osu!lazer style loading spinner with rotating rings.
/// </summary>
public partial class LoadingSpinner : CompositeDrawable
{
    private Container _mainRing = null!;
    private Container _innerRing = null!;
    private Container _outerRing = null!;
    private bool _isSpinning;

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            // Outer pulsing ring
            _outerRing = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Children = new Drawable[]
                {
                    new CircularContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Masking = true,
                        BorderThickness = 3,
                        BorderColour = new Color4(255, 102, 170, 100),
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.Transparent
                        }
                    }
                }
            },
            // Main spinning ring
            _mainRing = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Scale = new Vector2(0.7f),
                Children = new Drawable[]
                {
                    new CircularContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Masking = true,
                        BorderThickness = 4,
                        BorderColour = new Color4(255, 102, 170, 255),
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.Transparent
                        }
                    },
                    // Spinner accent (the "dot" that spins around)
                    new CircularContainer
                    {
                        Size = new Vector2(12),
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        Y = 4,
                        Masking = true,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(255, 102, 170, 255)
                        }
                    }
                }
            },
            // Inner ring (spins opposite direction)
            _innerRing = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Scale = new Vector2(0.4f),
                Children = new Drawable[]
                {
                    new CircularContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Masking = true,
                        BorderThickness = 3,
                        BorderColour = new Color4(255, 150, 200, 200),
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.Transparent
                        }
                    },
                    // Inner accent dot
                    new CircularContainer
                    {
                        Size = new Vector2(8),
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        Y = 2,
                        Masking = true,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(255, 150, 200, 255)
                        }
                    }
                }
            }
        };
    }

    public new void Show()
    {
        _isSpinning = true;
        StartSpinning();
    }

    public new void Hide()
    {
        _isSpinning = false;
    }

    private void StartSpinning()
    {
        if (!_isSpinning) return;

        // Main ring spins clockwise
        _mainRing.RotateTo(0).RotateTo(360, 1200, Easing.InOutSine).OnComplete(_ => StartSpinning());
        
        // Inner ring spins counter-clockwise faster
        _innerRing.RotateTo(0).RotateTo(-360, 800, Easing.InOutSine);
        
        // Outer ring pulses
        _outerRing.ScaleTo(1f).ScaleTo(1.1f, 600, Easing.InOutSine)
                  .Then().ScaleTo(1f, 600, Easing.InOutSine);
        
        _outerRing.FadeTo(0.3f).FadeTo(0.8f, 600, Easing.InOutSine)
                  .Then().FadeTo(0.3f, 600, Easing.InOutSine);
    }

    protected override void Update()
    {
        base.Update();
        
        // Continuously update rotation if spinning
        if (_isSpinning && _mainRing.Transforms.Count() == 0)
        {
            StartSpinning();
        }
    }
}
