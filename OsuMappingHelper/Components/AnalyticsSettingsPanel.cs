using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Services;

namespace OsuMappingHelper.Components;

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
                        IsChecked = SettingsService.Settings.SendAnalytics
                    },
                    new SpriteText
                    {
                        Text = "Helps improve the app. No personal data is collected.",
                        Font = new FontUsage("", 16),
                        Colour = new Color4(120, 120, 120, 255)
                    }
                }
            }
        };

        _analyticsCheckbox.CheckedChanged += OnAnalyticsChanged;
        
        // Apply current setting to the analytics service
        AptabaseService.IsEnabled = SettingsService.Settings.SendAnalytics;
    }

    private void OnAnalyticsChanged(bool isChecked)
    {
        SettingsService.Settings.SendAnalytics = isChecked;
        AptabaseService.IsEnabled = isChecked;
        SaveSettings();
    }

    private void SaveSettings()
    {
        Task.Run(async () => await SettingsService.SaveAsync());
    }
}
