using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using Companella.Models.Application;
using Companella.Components.Tools;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// Dialog for editing an osu! restart preset.
/// </summary>
public partial class OsuRestartPresetEditDialog : CompositeDrawable
{
    private Container _dialogContainer = null!;
    private SpriteText _titleText = null!;
    private StyledTextBox _nameTextBox = null!;
    private StyledTextBox _argumentsTextBox = null!;
    private PresetDialogButton _saveButton = null!;
    private PresetDialogButton _cancelButton = null!;
    private SpriteText _errorText = null!;

    private OsuRestartPreset? _preset;
    private int _presetIndex;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _dialogBgColor = new Color4(25, 25, 30, 255);
    private readonly Color4 _dialogBorderColor = new Color4(60, 60, 70, 255);

    /// <summary>
    /// Event raised when the preset is saved.
    /// </summary>
    public event Action<int, OsuRestartPreset>? PresetSaved;

    /// <summary>
    /// Event raised when the dialog is closed.
    /// </summary>
    public event Action? Closed;

    public OsuRestartPresetEditDialog()
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
                Colour = new Color4(0, 0, 0, 220)
            },
            // Dialog container with shadow
            _dialogContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(400, 260),
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
                    // Content
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(24),
                        Spacing = new Vector2(0, 16),
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
                            CreateLabeledInput("Preset Name", out _nameTextBox, "e.g., Bancho, Mames"),
                            // Arguments input
                            CreateLabeledInput("Command Line Arguments", out _argumentsTextBox, "e.g., -devserver mamesosu.net"),
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
                                Spacing = new Vector2(12, 0),
                                Margin = new MarginPadding { Top = 8 },
                                Children = new Drawable[]
                                {
                                    _cancelButton = new PresetDialogButton("Cancel")
                                    {
                                        Size = new Vector2(110, 38),
                                        BackgroundColour = new Color4(80, 80, 85, 255)
                                    },
                                    _saveButton = new PresetDialogButton("Save")
                                    {
                                        Size = new Vector2(110, 38),
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

    private Container CreateLabeledInput(string label, out StyledTextBox textBox, string placeholder = "")
    {
        textBox = new StyledTextBox
        {
            RelativeSizeAxes = Axes.X,
            Height = 36,
            PlaceholderText = placeholder
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
                Spacing = new Vector2(0, 6),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = label,
                        Font = new FontUsage("", 14),
                        Colour = new Color4(160, 160, 160, 255)
                    },
                    textBox
                }
            }
        };
    }

    /// <summary>
    /// Shows the dialog for editing a preset.
    /// </summary>
    public void Show(int presetIndex, OsuRestartPreset preset)
    {
        _presetIndex = presetIndex;
        _preset = preset;

        // Populate fields
        _nameTextBox.Text = preset.Name;
        _argumentsTextBox.Text = preset.Arguments;

        _errorText.Alpha = 0;
        _titleText.Text = presetIndex >= 0 ? $"Edit Preset" : "Add Preset";

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

        // Arguments can be empty (for plain Bancho start)
        var arguments = _argumentsTextBox.Text.Trim();

        // Create updated preset
        var updatedPreset = new OsuRestartPreset(name, arguments);
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
