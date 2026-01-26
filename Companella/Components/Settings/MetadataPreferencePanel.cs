using Companella.Components.Session;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for configuring metadata display preference (romanized vs unicode).
/// </summary>
public partial class MetadataPreferencePanel : CompositeDrawable
{
	[Resolved] private UserSettingsService SettingsService { get; set; } = null!;

	private SettingsCheckbox _romanizedCheckbox = null!;

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
						Text = "Metadata Display:",
						Font = new FontUsage("", 16),
						Colour = new Color4(200, 200, 200, 255)
					},
					_romanizedCheckbox = new SettingsCheckbox
					{
						LabelText = "Prefer romanized metadata",
						IsChecked = SettingsService.Settings.PreferRomanizedMetadata,
						TooltipText = "Show romanized (ASCII) titles and artists instead of unicode"
					},
					new SpriteText
					{
						Text = "When enabled, shows 'Hitorigoto' instead of 'ひとりごと'",
						Font = new FontUsage("", 12),
						Colour = new Color4(120, 120, 120, 255)
					}
				}
			}
		};

		_romanizedCheckbox.CheckedChanged += OnPreferenceChanged;
	}

	private void OnPreferenceChanged(bool isChecked)
	{
		SettingsService.Settings.PreferRomanizedMetadata = isChecked;
		SaveSettings();
	}

	private void SaveSettings()
	{
		Task.Run(async () => await SettingsService.SaveAsync());
	}
}
