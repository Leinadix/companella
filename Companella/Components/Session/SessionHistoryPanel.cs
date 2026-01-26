using System.Globalization;
using Companella.Components.Charts;
using Companella.Models.Session;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Session;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Session;

/// <summary>
/// Dropdown for selecting sessions.
/// </summary>
public partial class SessionDropdown : BasicDropdown<StoredSession?>
{
	public SessionDropdown()
	{
		// Override default AutoSizeAxes to allow manual width control
		AutoSizeAxes = Axes.None;
	}

	protected override LocalisableString GenerateItemText(StoredSession? item)
	{
		return item?.DisplayName ?? "Select a session...";
	}
}

/// <summary>
/// Small badge showing skillset count.
/// </summary>
public partial class SkillsetBadge : CompositeDrawable
{
	public SkillsetBadge(string skillset, int count, Color4 color)
	{
		AutoSizeAxes = Axes.Both;

		InternalChild = new Container
		{
			AutoSizeAxes = Axes.Both,
			Masking = true,
			CornerRadius = 4,
			Children = new Drawable[]
			{
				new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = new Color4(color.R, color.G, color.B, 0.3f)
				},
				new FillFlowContainer
				{
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Padding = new MarginPadding { Horizontal = 6, Vertical = 2 },
					Children = new Drawable[]
					{
						new SpriteText
						{
							Text = $"{skillset}: ",
							Font = new FontUsage("", 13),
							Colour = color
						},
						new SpriteText
						{
							Text = count.ToString(CultureInfo.InvariantCulture),
							Font = new FontUsage("", 13, "Bold"),
							Colour = color
						}
					}
				}
			}
		};
	}
}