using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Session;

/// <summary>
/// A month-based activity heatmap showing session activity over time.
/// Displays all years with data as separate rows, from earliest to current year.
/// </summary>
public partial class SessionActivityHeatmap : CompositeDrawable
{
	/// <summary>
	/// The currently selected month.
	/// </summary>
	public Bindable<DateTime?> SelectedDate { get; } = new();

	private Dictionary<DateTime, int> _sessionCounts = new();
	private FillFlowContainer _yearsContainer = null!;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _backgroundColor = new(30, 30, 35, 255);

	// Intensity colors (low to high) - subtle pink gradient
	public static readonly Color4[] IntensityColors = new[]
	{
		new Color4(45, 45, 50, 255), // 0 sessions - empty
		new Color4(90, 55, 70, 255), // 1-2 sessions - very light
		new Color4(150, 70, 100, 255), // 3-5 sessions - light
		new Color4(210, 85, 135, 255), // 6-10 sessions - medium
		new Color4(255, 102, 170, 255) // 11+ sessions - full accent
	};

	private const float _cellWidth = 30f;
	private const float _cellHeight = 20f;
	private const float _cellSpacing = 2f;
	private const float _yearLabelWidth = 36f;
	private const float _rowSpacing = 3f;

	public SessionActivityHeatmap()
	{
		AutoSizeAxes = Axes.Both;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 6;

		InternalChildren = new Drawable[]
		{
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = _backgroundColor
			},
			new FillFlowContainer
			{
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Vertical,
				Padding = new MarginPadding(8),
				Spacing = new Vector2(0, 4),
				Children = new Drawable[]
				{
					// Month headers
					CreateMonthHeaders(),
					// Years container (each year is a row)
					_yearsContainer = new FillFlowContainer
					{
						AutoSizeAxes = Axes.Both,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, _rowSpacing)
					},
					// Legend
					CreateLegend()
				}
			}
		};

		RefreshHeatmap();
	}

	/// <summary>
	/// Sets the session data for the heatmap.
	/// </summary>
	public void SetSessionData(Dictionary<DateTime, int> sessionCounts)
	{
		_sessionCounts = sessionCounts;
		RefreshHeatmap();
	}

	/// <summary>
	/// Sets the dates with sessions (each date counts as 1).
	/// </summary>
	public void SetDatesWithSessions(IEnumerable<DateTime> dates)
	{
		_sessionCounts = dates
			.Select(d => d.Date)
			.GroupBy(d => d)
			.ToDictionary(g => g.Key, g => g.Count());
		RefreshHeatmap();
	}

	/// <summary>
	/// Creates the month header row.
	/// </summary>
	private static FillFlowContainer CreateMonthHeaders()
	{
		var monthNames = new[] { "J", "F", "M", "A", "M", "J", "J", "A", "S", "O", "N", "D" };

		var container = new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(_cellSpacing, 0)
		};

		// Year label spacer
		container.Add(new Container { Width = _yearLabelWidth });

		// Month labels
		foreach (var month in monthNames)
			container.Add(new Container
			{
				Size = new Vector2(_cellWidth, 14),
				Child = new SpriteText
				{
					Text = month,
					Font = new FontUsage("", 11),
					Colour = new Color4(100, 100, 110, 255),
					Anchor = Anchor.Centre,
					Origin = Anchor.Centre
				}
			});

		return container;
	}

	/// <summary>
	/// Gets session count for a specific month.
	/// </summary>
	private int GetMonthSessionCount(int year, int month)
	{
		return _sessionCounts
			.Where(kvp => kvp.Key.Year == year && kvp.Key.Month == month)
			.Sum(kvp => kvp.Value);
	}

	/// <summary>
	/// Creates the legend showing intensity levels.
	/// </summary>
	private static FillFlowContainer CreateLegend()
	{
		return new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(4, 0),
			Margin = new MarginPadding { Top = 4, Left = _yearLabelWidth },
			Children = new Drawable[]
			{
				new SpriteText
				{
					Text = "Less",
					Font = new FontUsage("", 10),
					Colour = new Color4(100, 100, 110, 255),
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft
				},
				CreateLegendCell(0),
				CreateLegendCell(1),
				CreateLegendCell(2),
				CreateLegendCell(3),
				CreateLegendCell(4),
				new SpriteText
				{
					Text = "More",
					Font = new FontUsage("", 10),
					Colour = new Color4(100, 100, 110, 255),
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft
				}
			}
		};
	}

	public static Drawable CreateLegendCell(int level)
	{
		return new Container
		{
			Size = new Vector2(10, 10),
			Masking = true,
			CornerRadius = 2,
			Child = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = IntensityColors[level]
			}
		};
	}

	/// <summary>
	/// Refreshes the heatmap display.
	/// </summary>
	private void RefreshHeatmap()
	{
		_yearsContainer.Clear();

		var today = DateTime.Today;
		var currentYear = today.Year;

		// Find the earliest year with data, default to current year if no data
		// Filter out invalid dates (year must be >= 1 and <= 9999)
		var validDates = _sessionCounts.Keys.Where(d => d.Year >= 1 && d.Year <= 9999).ToList();
		var earliestYear = validDates.Count > 0
			? validDates.Min(d => d.Year)
			: currentYear;

		// Ensure earliest year is reasonable (not too far in the past, at least 2000)
		earliestYear = Math.Max(earliestYear, 2000);

		// Create a row for each year from earliest to current
		for (var year = earliestYear; year <= currentYear; year++) _yearsContainer.Add(CreateYearRow(year, today));
	}

	/// <summary>
	/// Creates a single year row with all 12 months.
	/// </summary>
	private FillFlowContainer CreateYearRow(int year, DateTime today)
	{
		var container = new FillFlowContainer
		{
			AutoSizeAxes = Axes.Both,
			Direction = FillDirection.Horizontal,
			Spacing = new Vector2(_cellSpacing, 0)
		};

		// Year label
		container.Add(new Container
		{
			Size = new Vector2(_yearLabelWidth, _cellHeight),
			Child = new SpriteText
			{
				Text = year.ToString(CultureInfo.InvariantCulture),
				Font = new FontUsage("", 12, year == today.Year ? "Bold" : ""),
				Colour = year == today.Year ? _accentColor : new Color4(140, 140, 150, 255),
				Anchor = Anchor.CentreLeft,
				Origin = Anchor.CentreLeft
			}
		});

		// Month cells
		for (var month = 1; month <= 12; month++)
		{
			// Capture loop variables for closure
			var capturedYear = year;
			var capturedMonth = month;

			var sessionCount = GetMonthSessionCount(year, month);
			var isSelected = SelectedDate.Value.HasValue &&
							 SelectedDate.Value.Value.Year == year &&
							 SelectedDate.Value.Value.Month == month;
			var isCurrentMonth = today.Year == year && today.Month == month;
			var isFutureMonth = year > today.Year || (year == today.Year && month > today.Month);

			var cell = new MonthCell(
				year,
				month,
				sessionCount,
				isSelected,
				isCurrentMonth,
				isFutureMonth,
				GetIntensityColor(sessionCount),
				_accentColor)
			{
				Size = new Vector2(_cellWidth, _cellHeight),
				Action = () => OnMonthClicked(capturedYear, capturedMonth)
			};

			container.Add(cell);
		}

		return container;
	}

	/// <summary>
	/// Gets the intensity color for a session count.
	/// </summary>
	private static Color4 GetIntensityColor(int count)
	{
		if (count == 0)
			return IntensityColors[0];
		if (count <= 2)
			return IntensityColors[1];
		if (count <= 5)
			return IntensityColors[2];
		if (count <= 10)
			return IntensityColors[3];
		return IntensityColors[4];
	}

	/// <summary>
	/// Called when a month is clicked.
	/// </summary>
	private void OnMonthClicked(int year, int month)
	{
		// Validate parameters
		if (year < 1 || year > 9999 || month < 1 || month > 12)
			return;

		// Check if clicking the same month to deselect
		if (SelectedDate.Value.HasValue &&
			SelectedDate.Value.Value.Year == year &&
			SelectedDate.Value.Value.Month == month)
			SelectedDate.Value = null;
		else
			SelectedDate.Value = new DateTime(year, month, 1);

		RefreshHeatmap();
	}
}

/// <summary>
/// A single month cell in the activity heatmap with tooltip.
/// </summary>
public partial class MonthCell : CompositeDrawable, IHasTooltip
{
	private Box _background = null!;
	private Box _hoverOverlay = null!;

	private readonly int _year;
	private readonly int _month;
	private readonly int _sessionCount;
	private readonly bool _isSelected;
	private readonly bool _isCurrentMonth;
	private readonly bool _isFutureMonth;
	private readonly Color4 _color;
	private readonly Color4 _accentColor;

	private static readonly string[] _monthNames =
		{ "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

	public Action? Action { get; set; }

	public LocalisableString TooltipText =>
		$"{_monthNames[_month - 1]} {_year}: {_sessionCount} session{(_sessionCount != 1 ? "s" : "")}";

	public MonthCell(int year, int month, int sessionCount, bool isSelected, bool isCurrentMonth, bool isFutureMonth,
		Color4 color, Color4 accentColor)
	{
		_year = year;
		_month = month;
		_sessionCount = sessionCount;
		_isSelected = isSelected;
		_isCurrentMonth = isCurrentMonth;
		_isFutureMonth = isFutureMonth;
		_color = color;
		_accentColor = accentColor;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 3;

		var displayColor = _isFutureMonth ? new Color4(35, 35, 40, 255) : _color;

		InternalChildren = new Drawable[]
		{
			_background = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = displayColor
			},
			_hoverOverlay = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = Color4.White,
				Alpha = 0
			}
		};

		// Selection indicator
		if (_isSelected)
			AddInternal(new Container
			{
				RelativeSizeAxes = Axes.Both,
				Masking = true,
				CornerRadius = 3,
				BorderColour = Color4.White,
				BorderThickness = 2f,
				Child = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Alpha = 0
				}
			});
		// Current month indicator (subtle)
		else if (_isCurrentMonth)
			AddInternal(new Container
			{
				RelativeSizeAxes = Axes.Both,
				Masking = true,
				CornerRadius = 3,
				BorderColour = _accentColor,
				BorderThickness = 1.5f,
				Child = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Alpha = 0
				}
			});
	}

	protected override bool OnHover(HoverEvent e)
	{
		if (!_isFutureMonth)
			_hoverOverlay.FadeTo(0.15f, 60, Easing.OutQuint);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_hoverOverlay.FadeTo(0, 100, Easing.OutQuint);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		if (_isFutureMonth)
			return false;

		_hoverOverlay.FadeTo(0.3f, 30, Easing.OutQuint).Then().FadeTo(0.15f, 80, Easing.OutQuint);
		Action?.Invoke();
		return true;
	}
}
