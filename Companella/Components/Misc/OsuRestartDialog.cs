using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using Companella.Models.Application;
using Companella.Services.Common;
using Companella.Components.Tools;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// A confirmation dialog for osu! restart with command line arguments support.
/// </summary>
public partial class OsuRestartDialog : CompositeDrawable
{
    [Resolved]
    private UserSettingsService UserSettingsService { get; set; } = null!;

    private Container _dialogContainer = null!;
    private SpriteText _titleText = null!;
    private TextFlowContainer _messageText = null!;
    private StyledTextBox _argsTextBox = null!;
    private OsuRestartPresetDropdown _presetDropdown = null!;
    private ConfirmationDialogButton _editPresetsButton = null!;
    private ConfirmationDialogButton _confirmButton = null!;
    private ConfirmationDialogButton _cancelButton = null!;
    private ConfirmationDialogButton? _skipButton;
    private FillFlowContainer _buttonContainer = null!;
    private OsuRestartPresetEditDialog? _presetEditDialog;

    private List<OsuRestartPreset> _presets = new();
    private OsuRestartPreset? _selectedPreset;

    /// <summary>
    /// Event raised when the user confirms the action.
    /// Provides the command line arguments to use.
    /// </summary>
    public event Action<string>? Confirmed;

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

    public OsuRestartDialog()
    {
        RelativeSizeAxes = Axes.Both;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        LoadPresets();

        InternalChildren = new Drawable[]
        {
            // Dim background
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0, 0, 0, 220)
            },
            // Dialog container with shadow - no masking to allow dropdown to overflow
            _dialogContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(500, 340),
                Children = new Drawable[]
                {
                    // Background with masking for rounded corners
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
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
                            }
                        }
                    },
                    // Content container - outside masking so dropdown can overflow
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding(24),
                        Children = new Drawable[]
                        {
                            // Top content (title, message, args)
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 14),
                                Children = new Drawable[]
                                {
                                    // Title
                                    _titleText = new SpriteText
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Text = "Restart osu!",
                                        Font = new FontUsage("", 22, "Bold"),
                                        Colour = _accentColor
                                    },
                                    // Message with proper wrapping
                                    _messageText = new TextFlowContainer(s => 
                                    {
                                        s.Font = new FontUsage("", 14);
                                        s.Colour = new Color4(200, 200, 200, 255);
                                    })
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        TextAnchor = Anchor.TopCentre,
                                        Text = "osu! will be restarted."
                                    },
                                    // Preset row - Depth = -1 ensures dropdown menu renders on top of siblings
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 6),
                                        Depth = -1,
                                        Children = new Drawable[]
                                        {
                                            new SpriteText
                                            {
                                                Text = "Preset",
                                                Font = new FontUsage("", 14),
                                                Colour = new Color4(160, 160, 160, 255)
                                            },
                                            new Container
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                Height = 32,
                                                Children = new Drawable[]
                                                {
                                                    _presetDropdown = new OsuRestartPresetDropdown
                                                    {
                                                        RelativeSizeAxes = Axes.X,
                                                        Width = 0.88f,
                                                        Items = _presets
                                                    },
                                                    _editPresetsButton = new ConfirmationDialogButton("Edit")
                                                    {
                                                        Anchor = Anchor.CentreRight,
                                                        Origin = Anchor.CentreRight,
                                                        Size = new Vector2(38, 32),
                                                        BackgroundColour = new Color4(70, 70, 80, 255)
                                                    }
                                                }
                                            }
                                        }
                                    },
                                    // Arguments row
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 6),
                                        Depth = 1, // Render behind preset row
                                        Children = new Drawable[]
                                        {
                                            new SpriteText
                                            {
                                                Text = "Command Line Arguments",
                                                Font = new FontUsage("", 14),
                                                Colour = new Color4(160, 160, 160, 255)
                                            },
                                            _argsTextBox = new StyledTextBox
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                Height = 36,
                                                PlaceholderText = "e.g., -devserver mamesosu.net"
                                            }
                                        }
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
                                    _confirmButton = new ConfirmationDialogButton("Restart")
                                    {
                                        Size = new Vector2(110, 40),
                                        BackgroundColour = _dangerColor
                                    }
                                }
                            }
                        }
                    }
                }
            },
            // Preset edit dialog (hidden by default)
            _presetEditDialog = new OsuRestartPresetEditDialog()
        };

        _cancelButton.Clicked += OnCancelClicked;
        _confirmButton.Clicked += OnConfirmClicked;
        _editPresetsButton.Clicked += OnEditPresetsClicked;
        _presetDropdown.Current.ValueChanged += OnPresetChanged;
        _presetEditDialog.PresetSaved += OnPresetSaved;

        // Select first preset by default
        if (_presets.Count > 0)
        {
            _presetDropdown.Current.Value = _presets[0];
        }
    }

    private void LoadPresets()
    {
        var savedPresets = UserSettingsService.Settings.OsuRestartPresets;
        if (savedPresets != null && savedPresets.Count > 0)
        {
            _presets = savedPresets.ToList();
        }
        else
        {
            _presets = OsuRestartPreset.GetDefaults();
        }
    }

    private void SavePresets()
    {
        UserSettingsService.Settings.OsuRestartPresets = new List<OsuRestartPreset>(_presets);
        Task.Run(async () => await UserSettingsService.SaveAsync());
    }

    private void OnPresetChanged(ValueChangedEvent<OsuRestartPreset?> e)
    {
        _selectedPreset = e.NewValue;
        if (_selectedPreset != null)
        {
            _argsTextBox.Text = _selectedPreset.Arguments;
        }
    }

    private void OnEditPresetsClicked()
    {
        // Show edit dialog for the selected preset
        var index = _selectedPreset != null ? _presets.IndexOf(_selectedPreset) : 0;
        if (index >= 0 && index < _presets.Count)
        {
            _presetEditDialog?.Show(index, _presets[index]);
        }
        else if (_presets.Count > 0)
        {
            _presetEditDialog?.Show(0, _presets[0]);
        }
    }

    private void OnPresetSaved(int index, OsuRestartPreset preset)
    {
        if (index >= 0 && index < _presets.Count)
        {
            _presets[index] = preset;
        }
        else
        {
            _presets.Add(preset);
        }

        SavePresets();
        RefreshDropdown();
    }

    private void RefreshDropdown()
    {
        var currentSelection = _selectedPreset;
        _presetDropdown.Items = _presets;

        // Try to keep the same selection
        if (currentSelection != null)
        {
            var matchingPreset = _presets.FirstOrDefault(p => p.Name == currentSelection.Name);
            if (matchingPreset != null)
            {
                _presetDropdown.Current.Value = matchingPreset;
            }
            else if (_presets.Count > 0)
            {
                _presetDropdown.Current.Value = _presets[0];
            }
        }
        else if (_presets.Count > 0)
        {
            _presetDropdown.Current.Value = _presets[0];
        }
    }

    /// <summary>
    /// Shows the restart dialog with the specified title and message.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message (supports multi-line)</param>
    /// <param name="showSkip">Whether to show a Skip button</param>
    public void Show(string title, string message, bool showSkip = false)
    {
        _titleText.Text = title;
        _messageText.Text = message;

        // Reload presets in case they changed
        LoadPresets();
        RefreshDropdown();

        // Handle skip button
        // Remove all buttons first
        if (_buttonContainer.Contains(_cancelButton))
            _buttonContainer.Remove(_cancelButton, false);
        if (_skipButton != null && _buttonContainer.Contains(_skipButton))
            _buttonContainer.Remove(_skipButton, false);
        if (_buttonContainer.Contains(_confirmButton))
            _buttonContainer.Remove(_confirmButton, false);

        // Create or remove skip button
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

        // Re-add in correct order: Cancel, Skip (if shown), Restart
        _buttonContainer.Add(_cancelButton);
        if (showSkip && _skipButton != null)
        {
            _buttonContainer.Add(_skipButton);
        }
        _buttonContainer.Add(_confirmButton);

        // Adjust dialog size based on content
        var estimatedHeight = 340f;
        if (message.Length > 100)
            estimatedHeight = 370f;
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

    /// <summary>
    /// Gets the currently entered command line arguments.
    /// </summary>
    public string GetArguments()
    {
        return _argsTextBox.Text?.Trim() ?? "";
    }

    private void OnCancelClicked()
    {
        Hide();
    }

    private void OnConfirmClicked()
    {
        var arguments = GetArguments();
        Confirmed?.Invoke(arguments);
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
/// Dropdown for selecting osu! restart presets.
/// </summary>
public partial class OsuRestartPresetDropdown : BasicDropdown<OsuRestartPreset?>
{
    public OsuRestartPresetDropdown()
    {
        AutoSizeAxes = Axes.None;
    }

    protected override LocalisableString GenerateItemText(OsuRestartPreset? item)
    {
        if (item == null)
            return "Select preset...";
        
        if (string.IsNullOrEmpty(item.Arguments))
            return $"{item.Name} (no args)";
        
        return $"{item.Name}";
    }
}
