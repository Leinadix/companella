using Companella.Models.Application;
using Companella.Services.Common;
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

namespace Companella.Components.Settings;

/// <summary>
/// Settings panel for selecting the rice dan calculator (Companella ONNX vs Daniel).
/// </summary>
public partial class RiceDanCalculatorPanel : CompositeDrawable
{
	[Resolved] private UserSettingsService UserSettingsService { get; set; } = null!;

	private RiceDanCalculatorDropdown _calculatorDropdown = null!;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _backgroundColor = new(40, 40, 45, 255);

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
					new SpriteText
					{
						Text = "Rice Dan Calculator",
						Font = new FontUsage("", 19, "Bold"),
						Colour = _accentColor
					},
					new SpriteText
					{
						Text = "Choose how 4K rice dan is estimated. Daniel falls back to Companella ONNX below Alpha.",
						Font = new FontUsage("", 15),
						Colour = new Color4(160, 160, 160, 255)
					},
					_calculatorDropdown = new RiceDanCalculatorDropdown
					{
						Width = 280,
						Anchor = Anchor.TopLeft,
						Origin = Anchor.TopLeft
					},
					new SpriteText
					{
						Text = "LN dan always uses the Companella Sunny-based estimator.",
						Font = new FontUsage("", 13),
						Colour = new Color4(120, 120, 120, 255)
					},
					new SpriteText
					{
						Text = "Daniel algorithm © 2026 TheBagelOfMan (MIT) — github.com/TheBagelOfMan/Daniel",
						Font = new FontUsage("", 12),
						Colour = new Color4(100, 100, 100, 255)
					}
				}
			}
		};

		_calculatorDropdown.Items = Enum.GetValues<RiceDanCalculatorMode>();
		_calculatorDropdown.Current.Value = UserSettingsService.Settings.RiceDanCalculator;
		_calculatorDropdown.Current.BindValueChanged(OnCalculatorChanged);
	}

	private void OnCalculatorChanged(ValueChangedEvent<RiceDanCalculatorMode> e)
	{
		UserSettingsService.Settings.RiceDanCalculator = e.NewValue;
		Task.Run(async () => await UserSettingsService.SaveAsync());
		Logger.Info($"[RiceDanCalculatorPanel] Changed to {e.NewValue}");
	}
}

/// <summary>
/// Dropdown for rice dan calculator selection.
/// </summary>
public partial class RiceDanCalculatorDropdown : BasicDropdown<RiceDanCalculatorMode>
{
	protected override LocalisableString GenerateItemText(RiceDanCalculatorMode item)
	{
		return item switch
		{
			RiceDanCalculatorMode.CompanellaOnnx => "Companella ONNX",
			RiceDanCalculatorMode.Daniel => "Daniel (Alpha+ fallback to ONNX)",
			_ => item.ToString()
		};
	}
}
