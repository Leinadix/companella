using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using Companella.Models.Application;
using Companella.Components.Session;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Dialog for editing a bulk rate preset.
/// </summary>
public partial class PresetEditDialog : CompositeDrawable
{
    private Container _dialogContainer = null!;
    private SpriteText _titleText = null!;
    private StyledTextBox _nameTextBox = null!;
    private StyledTextBox _minRateTextBox = null!;
    private StyledTextBox _maxRateTextBox = null!;
    private StyledTextBox _stepTextBox = null!;
    private StyledTextBox _odTextBox = null!;
    private StyledTextBox _hpTextBox = null!;
    private SettingsCheckbox _excludeBaseRateCheckbox = null!;
    private PresetDialogButton _saveButton = null!;
    private PresetDialogButton _cancelButton = null!;
    private SpriteText _errorText = null!;

    private BulkRatePreset? _preset;
    private int _presetIndex;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    /// <summary>
    /// Event raised when the preset is saved.
    /// </summary>
    public event Action<int, BulkRatePreset>? PresetSaved;

    /// <summary>
    /// Event raised when the dialog is closed.
    /// </summary>
    public event Action? Closed;

    public PresetEditDialog()
    {
        RelativeSizeAxes = Axes.Both;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            // Dim background
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0, 0, 0, 200)
            },
            // Dialog container
            _dialogContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(340, 420),
                Masking = true,
                CornerRadius = 10,
                Children = new Drawable[]
                {
                    // Background
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(30, 30, 35, 255)
                    },
                    // Content
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(20),
                        Spacing = new Vector2(0, 12),
                        Children = new Drawable[]
                        {
                            // Title
                            _titleText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "Edit Preset",
                                Font = new FontUsage("", 22, "Bold"),
                                Colour = _accentColor
                            },
                            // Name input
                            CreateLabeledInput("Preset Name", out _nameTextBox),
                            // Rate range row
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Children = new Drawable[]
                                {
                                    CreateSmallLabeledInput("Min Rate", out _minRateTextBox, 90),
                                    CreateSmallLabeledInput("Max Rate", out _maxRateTextBox, 90),
                                    CreateSmallLabeledInput("Step", out _stepTextBox, 90)
                                }
                            },
                            // OD/HP row
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Children = new Drawable[]
                                {
                                    CreateSmallLabeledInput("OD (empty=map)", out _odTextBox, 140),
                                    CreateSmallLabeledInput("HP (empty=map)", out _hpTextBox, 140)
                                }
                            },
                            // Exclude base rate checkbox
                            _excludeBaseRateCheckbox = new SettingsCheckbox
                            {
                                LabelText = "Exclude Base Rate (1.0x)",
                                IsChecked = false
                            },
                            // Error text
                            _errorText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "",
                                Font = new FontUsage("", 14),
                                Colour = new Color4(255, 100, 100, 255),
                                Alpha = 0
                            },
                            // Button container
                            new FillFlowContainer
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Margin = new MarginPadding { Top = 10 },
                                Children = new Drawable[]
                                {
                                    _cancelButton = new PresetDialogButton("Cancel")
                                    {
                                        Size = new Vector2(100, 35),
                                        BackgroundColour = new Color4(80, 80, 85, 255)
                                    },
                                    _saveButton = new PresetDialogButton("Save")
                                    {
                                        Size = new Vector2(100, 35),
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
        _saveButton.Clicked += OnSaveClicked;
    }

    private Container CreateLabeledInput(string label, out StyledTextBox textBox)
    {
        textBox = new StyledTextBox
        {
            RelativeSizeAxes = Axes.X,
            Height = 32
        };

        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = label,
                        Font = new FontUsage("", 14),
                        Colour = new Color4(140, 140, 140, 255)
                    },
                    textBox
                }
            }
        };
    }

    private Container CreateSmallLabeledInput(string label, out StyledTextBox textBox, float width)
    {
        textBox = new StyledTextBox
        {
            Size = new Vector2(width, 32)
        };

        return new Container
        {
            AutoSizeAxes = Axes.Both,
            Child = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = label,
                        Font = new FontUsage("", 13),
                        Colour = new Color4(140, 140, 140, 255)
                    },
                    textBox
                }
            }
        };
    }

    /// <summary>
    /// Shows the dialog for editing a preset.
    /// </summary>
    public void Show(int presetIndex, BulkRatePreset preset)
    {
        _presetIndex = presetIndex;
        _preset = preset;

        // Populate fields
        _nameTextBox.Text = preset.Name;
        _minRateTextBox.Text = preset.MinRate.ToString("0.0#", CultureInfo.InvariantCulture);
        _maxRateTextBox.Text = preset.MaxRate.ToString("0.0#", CultureInfo.InvariantCulture);
        _stepTextBox.Text = preset.Step.ToString("0.0#", CultureInfo.InvariantCulture);
        _odTextBox.Text = preset.OD.HasValue ? preset.OD.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
        _hpTextBox.Text = preset.HP.HasValue ? preset.HP.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
        _excludeBaseRateCheckbox.IsChecked = preset.ExcludeBaseRate;

        _errorText.Alpha = 0;
        _titleText.Text = $"Edit Preset {presetIndex + 1}";

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

    private void OnSaveClicked()
    {
        // Validate inputs
        var name = _nameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowError("Name cannot be empty");
            return;
        }

        if (!double.TryParse(_minRateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minRate) ||
            minRate < 0.1 || minRate > 3.0)
        {
            ShowError("Min rate must be between 0.1 and 3.0");
            return;
        }

        if (!double.TryParse(_maxRateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxRate) ||
            maxRate < 0.1 || maxRate > 3.0)
        {
            ShowError("Max rate must be between 0.1 and 3.0");
            return;
        }

        if (maxRate < minRate)
        {
            ShowError("Max rate must be >= min rate");
            return;
        }

        if (!double.TryParse(_stepTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var step) ||
            step < 0.01 || step > 1.0)
        {
            ShowError("Step must be between 0.01 and 1.0");
            return;
        }

        // Parse OD (optional)
        double? od = null;
        if (!string.IsNullOrWhiteSpace(_odTextBox.Text))
        {
            if (!double.TryParse(_odTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var odValue) ||
                odValue < 0 || odValue > 10)
            {
                ShowError("OD must be between 0 and 10");
                return;
            }
            od = odValue;
        }

        // Parse HP (optional)
        double? hp = null;
        if (!string.IsNullOrWhiteSpace(_hpTextBox.Text))
        {
            if (!double.TryParse(_hpTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var hpValue) ||
                hpValue < 0 || hpValue > 10)
            {
                ShowError("HP must be between 0 and 10");
                return;
            }
            hp = hpValue;
        }

        // Create updated preset
        var updatedPreset = new BulkRatePreset(name, minRate, maxRate, step, od, hp, _excludeBaseRateCheckbox.IsChecked);
        PresetSaved?.Invoke(_presetIndex, updatedPreset);
        Hide();
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.FadeIn(100).Then().Delay(3000).FadeOut(200);
    }

    protected override bool OnClick(ClickEvent e)
    {
        // Prevent clicks from passing through
        return true;
    }
}

/// <summary>
/// A styled button for the preset dialog.
/// </summary>
public partial class PresetDialogButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _textSprite = null!;
    private readonly string _text;

    public Color4 BackgroundColour { get; set; } = new Color4(255, 102, 170, 255);

    public event Action? Clicked;

    public PresetDialogButton(string text)
    {
        _text = text;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 5;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = BackgroundColour
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
