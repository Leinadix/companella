using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Services;

namespace OsuMappingHelper.Components;

/// <summary>
/// Panel for configuring overlay mode (window follows osu! vs independent window).
/// </summary>
public partial class OverlayModePanel : CompositeDrawable
{
    [Resolved]
    private UserSettingsService SettingsService { get; set; } = null!;

    [Resolved]
    private OsuWindowOverlayService OverlayService { get; set; } = null!;

    private SettingsCheckbox _overlayModeCheckbox = null!;

    /// <summary>
    /// Event raised when overlay mode setting changes.
    /// </summary>
    public event Action<bool>? OverlayModeChanged;

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
                        Text = "Overlay Mode:",
                        Font = new FontUsage("", 16),
                        Colour = new Color4(200, 200, 200, 255)
                    },
                    _overlayModeCheckbox = new SettingsCheckbox
                    {
                        LabelText = "Attach window to osu! as overlay",
                        IsChecked = SettingsService.Settings.OverlayMode
                    },
                    new SpriteText
                    {
                        Text = "When enabled, window follows osu! and hides when osu! loses focus.",
                        Font = new FontUsage("", 13),
                        Colour = new Color4(140, 140, 140, 255)
                    },
                    new SpriteText
                    {
                        Text = "When disabled, window stays independent and always visible.",
                        Font = new FontUsage("", 13),
                        Colour = new Color4(140, 140, 140, 255)
                    }
                }
            }
        };

        _overlayModeCheckbox.CheckedChanged += OnOverlayModeChanged;
    }

    private void OnOverlayModeChanged(bool isChecked)
    {
        SettingsService.Settings.OverlayMode = isChecked;
        SaveSettings();
        
        // Request immediate overlay mode change
        OverlayService.RequestOverlayModeChange(isChecked);
        
        OverlayModeChanged?.Invoke(isChecked);
    }

    private void SaveSettings()
    {
        Task.Run(async () => await SettingsService.SaveAsync());
    }
}
