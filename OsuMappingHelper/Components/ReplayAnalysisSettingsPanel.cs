using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Services;
using TextBox = osu.Framework.Graphics.UserInterface.TextBox;

namespace OsuMappingHelper.Components;

/// <summary>
/// Panel for configuring replay analysis window settings.
/// </summary>
public partial class ReplayAnalysisSettingsPanel : CompositeDrawable
{
    [Resolved]
    private UserSettingsService SettingsService { get; set; } = null!;

    private SettingsCheckbox _enabledCheckbox = null!;
    private BasicTextBox _widthTextBox = null!;
    private BasicTextBox _heightTextBox = null!;
    private BasicTextBox _xTextBox = null!;
    private BasicTextBox _yTextBox = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        var settings = SettingsService.Settings;

        InternalChildren = new Drawable[]
        {
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = "Replay Analysis Window:",
                        Font = new FontUsage("", 16),
                        Colour = new Color4(200, 200, 200, 255)
                    },
                    _enabledCheckbox = new SettingsCheckbox
                    {
                        LabelText = "Show replay analysis on results screen",
                        IsChecked = settings.ReplayAnalysisEnabled
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(12, 0),
                        Children = new Drawable[]
                        {
                            CreateLabeledInput("Width:", settings.ReplayAnalysisWidth.ToString(), out _widthTextBox, 70),
                            CreateLabeledInput("Height:", settings.ReplayAnalysisHeight.ToString(), out _heightTextBox, 70),
                        }
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(12, 0),
                        Children = new Drawable[]
                        {
                            CreateLabeledInput("X:", settings.ReplayAnalysisX.ToString(), out _xTextBox, 70),
                            CreateLabeledInput("Y:", settings.ReplayAnalysisY.ToString(), out _yTextBox, 70),
                        }
                    },
                    new SpriteText
                    {
                        Text = "Default size: 800x400 (8:4 aspect ratio)",
                        Font = new FontUsage("", 12),
                        Colour = new Color4(120, 120, 120, 255)
                    }
                }
            }
        };

        // Subscribe to events
        _enabledCheckbox.CheckedChanged += OnEnabledChanged;
        _widthTextBox.OnCommit += OnSizeChanged;
        _heightTextBox.OnCommit += OnSizeChanged;
        _xTextBox.OnCommit += OnPositionChanged;
        _yTextBox.OnCommit += OnPositionChanged;
    }

    private Drawable CreateLabeledInput(string label, string value, out BasicTextBox textBox, float inputWidth)
    {
        textBox = null!;
        
        var container = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(4, 0),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = label,
                    Font = new FontUsage("", 14),
                    Colour = new Color4(150, 150, 150, 255),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft
                },
                new Container
                {
                    Width = inputWidth,
                    Height = 28,
                    Masking = true,
                    CornerRadius = 4,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(35, 35, 40, 255)
                        },
                        textBox = new BasicTextBox
                        {
                            RelativeSizeAxes = Axes.Both,
                            Text = value,
                            CommitOnFocusLost = true
                        }
                    }
                }
            }
        };

        return container;
    }

    private void OnEnabledChanged(bool isChecked)
    {
        SettingsService.Settings.ReplayAnalysisEnabled = isChecked;
        SaveSettings();
    }

    private void OnSizeChanged(TextBox sender, bool newText)
    {
        if (int.TryParse(_widthTextBox.Text, out int width) && width > 100)
        {
            SettingsService.Settings.ReplayAnalysisWidth = width;
        }
        
        if (int.TryParse(_heightTextBox.Text, out int height) && height > 50)
        {
            SettingsService.Settings.ReplayAnalysisHeight = height;
        }
        
        SaveSettings();
    }

    private void OnPositionChanged(TextBox sender, bool newText)
    {
        if (int.TryParse(_xTextBox.Text, out int x))
        {
            SettingsService.Settings.ReplayAnalysisX = x;
        }
        
        if (int.TryParse(_yTextBox.Text, out int y))
        {
            SettingsService.Settings.ReplayAnalysisY = y;
        }
        
        SaveSettings();
    }

    private void SaveSettings()
    {
        Task.Run(async () => await SettingsService.SaveAsync());
    }
}

