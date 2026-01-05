using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Services;

namespace OsuMappingHelper.Components;

/// <summary>
/// Settings panel for selecting the MinaCalc version to use for MSD calculations.
/// </summary>
public partial class MinaCalcVersionPanel : CompositeDrawable
{
    [Resolved]
    private UserSettingsService UserSettingsService { get; set; } = null!;

    private MinaCalcVersionDropdown _versionDropdown = null!;
    private SpriteText _descriptionText = null!;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _backgroundColor = new Color4(40, 40, 45, 255);

    [BackgroundDependencyLoader]
    private void load()
    {
        AutoSizeAxes = Axes.Y;
        Masking = true;
        CornerRadius = 8;

        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = _backgroundColor
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding(12),
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    // Header
                    new SpriteText
                    {
                        Text = "MinaCalc Version",
                        Font = new FontUsage("", 19, "Bold"),
                        Colour = _accentColor
                    },
                    // Description
                    _descriptionText = new SpriteText
                    {
                        Text = "Select which MinaCalc version to use for MSD calculations",
                        Font = new FontUsage("", 15),
                        Colour = new Color4(160, 160, 160, 255)
                    },
                    // Dropdown
                    _versionDropdown = new MinaCalcVersionDropdown
                    {
                        Width = 200,
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft
                    },
                    // Version info
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 4),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = "5.15 - 4K/6K/7K support, chordjack and stream adjustments",
                                Font = new FontUsage("", 13),
                                Colour = new Color4(120, 120, 120, 255)
                            },
                            new SpriteText
                            {
                                Text = "5.05 - 4K only, legacy version",
                                Font = new FontUsage("", 13),
                                Colour = new Color4(120, 120, 120, 255)
                            }
                        }
                    }
                }
            }
        };

        // Initialize dropdown with available versions
        _versionDropdown.Items = new[] { "515", "505" };
        
        // Set current value from settings
        var currentVersion = UserSettingsService.Settings.MinaCalcVersion;
        if (currentVersion != "515" && currentVersion != "505")
            currentVersion = "515"; // Default fallback
        
        _versionDropdown.Current.Value = currentVersion;
        ToolPaths.SelectedMinaCalcVersion = currentVersion;

        // Bind to changes
        _versionDropdown.Current.BindValueChanged(OnVersionChanged);
    }

    private void OnVersionChanged(ValueChangedEvent<string> e)
    {
        // Update ToolPaths
        ToolPaths.SelectedMinaCalcVersion = e.NewValue;
        
        // Save to settings
        UserSettingsService.Settings.MinaCalcVersion = e.NewValue;
        Task.Run(async () => await UserSettingsService.SaveAsync());
        
        Console.WriteLine($"[MinaCalcVersionPanel] Changed to MinaCalc {(e.NewValue == "515" ? "5.15" : "5.05")}");
    }
}

/// <summary>
/// Dropdown for selecting MinaCalc version.
/// </summary>
public partial class MinaCalcVersionDropdown : BasicDropdown<string>
{
    protected override LocalisableString GenerateItemText(string item)
    {
        return item switch
        {
            "515" => "MinaCalc 5.15 (Latest)",
            "505" => "MinaCalc 5.05 (Legacy)",
            _ => item
        };
    }
}

