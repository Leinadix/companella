using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using Companella.Services.Common;
using Companella.Components.Session;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for configuring analytics/privacy settings.
/// Allows users to opt-out of anonymous usage tracking (GDPR compliant).
/// </summary>
public partial class AnalyticsSettingsPanel : CompositeDrawable
{
    [Resolved]
    private UserSettingsService SettingsService { get; set; } = null!;

    [Resolved]
    private AptabaseService AptabaseService { get; set; } = null!;

    private SettingsCheckbox _analyticsCheckbox = null!;
    private SettingsCheckbox _danTrainingCheckbox = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

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
                        Text = "Privacy:",
                        Font = new FontUsage("", 16),
                        Colour = new Color4(200, 200, 200, 255)
                    },
                    _analyticsCheckbox = new SettingsCheckbox
                    {
                        LabelText = "Send anonymous usage data",
                        IsChecked = SettingsService.Settings.SendAnalytics,
                        TooltipText = "Help improve Companella by sending anonymous usage data"
                    },
                    new SpriteText
                    {
                        Text = "Helps improve the app. No personal data is collected.",
                        Font = new FontUsage("", 14),
                        Colour = new Color4(120, 120, 120, 255)
                    },
                    new Container { Height = 8 }, // Spacer
                    _danTrainingCheckbox = new SettingsCheckbox
                    {
                        LabelText = "Participate in Dan Training",
                        IsChecked = SettingsService.Settings.ParticipateDanTraining,
                        TooltipText = "Help improve dan classification by rating maps after completing them"
                    },
                    new SpriteText
                    {
                        Text = "Shows a dan rating dialog after completing new maps.",
                        Font = new FontUsage("", 14),
                        Colour = new Color4(120, 120, 120, 255)
                    }
                }
            }
        };

        _analyticsCheckbox.CheckedChanged += OnAnalyticsChanged;
        _danTrainingCheckbox.CheckedChanged += OnDanTrainingChanged;
        
        // Apply current setting to the analytics service
        AptabaseService.IsEnabled = SettingsService.Settings.SendAnalytics;
    }

    private void OnAnalyticsChanged(bool isChecked)
    {
        SettingsService.Settings.SendAnalytics = isChecked;
        AptabaseService.IsEnabled = isChecked;
        SaveSettings();
    }

    private void OnDanTrainingChanged(bool isChecked)
    {
        SettingsService.Settings.ParticipateDanTraining = isChecked;
        SaveSettings();
    }

    private void SaveSettings()
    {
        Task.Run(async () => await SettingsService.SaveAsync());
    }
}
