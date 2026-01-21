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

namespace Companella.Components.Misc;

/// <summary>
/// A simple confirmation dialog for yes/no actions.
/// </summary>
public partial class ConfirmationDialog : CompositeDrawable
{
    private Container _dialogContainer = null!;
    private SpriteText _titleText = null!;
    private TextFlowContainer _messageText = null!;
    private ConfirmationDialogButton _confirmButton = null!;
    private ConfirmationDialogButton _cancelButton = null!;
    private ConfirmationDialogButton? _skipButton;
    private FillFlowContainer _buttonContainer = null!;

    /// <summary>
    /// Event raised when the user confirms the action.
    /// </summary>
    public event Action? Confirmed;

    /// <summary>
    /// Event raised when the user skips the action (optional).
    /// </summary>
    public event Action? Skipped;

    /// <summary>
    /// Event raised when the dialog is closed (confirmed, skipped, or cancelled).
    /// </summary>
    public event Action? Closed;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _dangerColor = new Color4(255, 80, 80, 255);
    private readonly Color4 _dialogBgColor = new Color4(25, 25, 30, 255);
    private readonly Color4 _dialogBorderColor = new Color4(60, 60, 70, 255);

    public ConfirmationDialog()
    {
        RelativeSizeAxes = Axes.Both;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            // Dim background with blur effect
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0, 0, 0, 220)
            },
            // Dialog container with shadow
            _dialogContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(480, 200),
                Masking = true,
                CornerRadius = 12,
                Children = new Drawable[]
                {
                    // Shadow layer
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(0, 0, 0, 100),
                        Margin = new MarginPadding(2)
                    },
                    // Main background
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = _dialogBgColor
                    },
                    // Border
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 12,
                        BorderColour = _dialogBorderColor,
                        BorderThickness = 1.5f,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                            AlwaysPresent = true
                        }
                    },
                    // Content container
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding(24),
                        Children = new Drawable[]
                        {
                            // Top content (title and message)
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 18),
                                Children = new Drawable[]
                                {
                                    // Title
                                    _titleText = new SpriteText
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Text = "Confirm Action",
                                        Font = new FontUsage("", 22, "Bold"),
                                        Colour = _accentColor
                                    },
                                    // Message with proper wrapping
                                    _messageText = new TextFlowContainer(s => 
                                    {
                                        s.Font = new FontUsage("", 15);
                                        s.Colour = new Color4(220, 220, 220, 255);
                                    })
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        TextAnchor = Anchor.TopCentre,
                                        Text = "Are you sure?"
                                    }
                                }
                            },
                            // Button container at bottom
                            _buttonContainer = new FillFlowContainer
                            {
                                Anchor = Anchor.BottomCentre,
                                Origin = Anchor.BottomCentre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Children = new Drawable[]
                                {
                                    _cancelButton = new ConfirmationDialogButton("Cancel")
                                    {
                                        Size = new Vector2(110, 40),
                                        BackgroundColour = new Color4(70, 70, 75, 255)
                                    },
                                    _confirmButton = new ConfirmationDialogButton("Confirm")
                                    {
                                        Size = new Vector2(110, 40),
                                        BackgroundColour = _accentColor
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        _cancelButton.Clicked += OnCancelClicked;
        _confirmButton.Clicked += OnConfirmClicked;
    }

    /// <summary>
    /// Shows the confirmation dialog with the specified title and message.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message (supports multi-line)</param>
    /// <param name="isDangerous">Whether this is a dangerous action (affects confirm button color)</param>
    /// <param name="showSkip">Whether to show a Skip button</param>
    /// <param name="skipAfterConfirm">If true, Skip button appears after Confirm (for startup restart). If false, Skip appears between Cancel and Confirm.</param>
    public void Show(string title, string message, bool isDangerous = false, bool showSkip = false, bool skipAfterConfirm = false)
    {
        _titleText.Text = title;
        _messageText.Text = message;
        _confirmButton.BackgroundColour = isDangerous ? _dangerColor : _accentColor;

        // Always ensure correct button order: Cancel, Skip (if shown), Confirm
        // Remove all buttons and re-add in correct order
        var buttonsToReorder = new List<Drawable>();
        
        // Remove Cancel
        if (_buttonContainer.Contains(_cancelButton))
        {
            _buttonContainer.Remove(_cancelButton, false);
            buttonsToReorder.Add(_cancelButton);
        }
        
        // Remove Skip if it exists
        if (_skipButton != null && _buttonContainer.Contains(_skipButton))
        {
            _buttonContainer.Remove(_skipButton, false);
        }
        
        // Remove Confirm
        if (_buttonContainer.Contains(_confirmButton))
        {
            _buttonContainer.Remove(_confirmButton, false);
            buttonsToReorder.Add(_confirmButton);
        }
        
        // Add or remove skip button
        if (showSkip && _skipButton == null)
        {
            _skipButton = new ConfirmationDialogButton("Skip")
            {
                Size = new Vector2(110, 40),
                BackgroundColour = new Color4(100, 100, 110, 255)
            };
            _skipButton.Clicked += OnSkipClicked;
        }
        else if (!showSkip && _skipButton != null)
        {
            _skipButton.Clicked -= OnSkipClicked;
            _skipButton = null;
        }
        
        // Re-add in correct order: Cancel, Skip (if shown), Confirm
        _buttonContainer.Add(_cancelButton);
        if (showSkip && _skipButton != null)
        {
            _buttonContainer.Add(_skipButton);
        }
        _buttonContainer.Add(_confirmButton);

        // Adjust dialog size based on content
        var estimatedHeight = 200f;
        if (message.Length > 80)
            estimatedHeight = 240f;
        if (showSkip)
            estimatedHeight += 10f;
        
        _dialogContainer.ResizeHeightTo(estimatedHeight, 0);

        // Show with animation
        this.FadeIn(200, Easing.OutQuint);
        _dialogContainer.ScaleTo(0.9f).ScaleTo(1f, 200, Easing.OutQuint);
    }

    /// <summary>
    /// Hides the dialog.
    /// </summary>
    public new void Hide()
    {
        this.FadeOut(200, Easing.OutQuint);
        Closed?.Invoke();
    }

    private void OnCancelClicked()
    {
        Hide();
    }

    private void OnConfirmClicked()
    {
        Confirmed?.Invoke();
        Hide();
    }

    private void OnSkipClicked()
    {
        Skipped?.Invoke();
        Hide();
    }

    protected override bool OnClick(ClickEvent e)
    {
        // Prevent clicks from passing through
        return true;
    }
}

/// <summary>
/// A styled button for the confirmation dialog.
/// </summary>
public partial class ConfirmationDialogButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _textSprite = null!;
    private readonly string _text;

    public Color4 BackgroundColour { get; set; } = new Color4(255, 102, 170, 255);

    public event Action? Clicked;

    public ConfirmationDialogButton(string text)
    {
        _text = text;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 6;

        InternalChildren = new Drawable[]
        {
            // Shadow
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0, 0, 0, 80),
                Margin = new MarginPadding(1)
            },
            // Main background
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = BackgroundColour
            },
            // Border
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                BorderColour = new Color4(255, 255, 255, 30),
                BorderThickness = 1f,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0,
                    AlwaysPresent = true
                }
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            _textSprite = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = _text,
                Font = new FontUsage("", 16, "Bold"),
                Colour = Color4.White
            }
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        _hoverOverlay.FadeTo(0.15f, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        Clicked?.Invoke();
        _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
        return true;
    }
}
