using System.Globalization;
using Companella.Models.Session;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Charts;

/// <summary>
/// Displays a session tracking chart with MSD ratings (color-coded) and accuracy (white overlay).
/// X-axis: session time (hh:mm:ss), Y-axis: MSD rating (left) / Accuracy (right, 80-100% scaled).
/// </summary>
public partial class SessionChart : CompositeDrawable
{
	private const float _chartPaddingLeft = 45f;
	private const float _chartPaddingRight = 45f;
	private const float _chartPaddingTop = 25f;
	private const float _chartPaddingBottom = 30f;
	private const float _pointRadius = 4f;
	private const float _lineThickness = 2f;

	// Accuracy scaling: 80% = bottom, 100% = top
	private const float _accuracyMin = 80f;
	private const float _accuracyMax = 100f;

	// MSD scaling: auto-calculated based on data, default range
	private const float _defaultMsdMin = 0f;
	private const float _defaultMsdMax = 40f;

	// Skillset colors (same as MsdChart)
	private static readonly Dictionary<string, Color4> _skillsetColors = new()
	{
		{ "overall", new Color4(200, 200, 200, 255) },
		{ "stream", new Color4(100, 180, 255, 255) },
		{ "jumpstream", new Color4(100, 220, 100, 255) },
		{ "handstream", new Color4(255, 180, 100, 255) },
		{ "stamina", new Color4(180, 100, 255, 255) },
		{ "jackspeed", new Color4(255, 100, 100, 255) },
		{ "chordjack", new Color4(255, 220, 100, 255) },
		{ "technical", new Color4(100, 220, 220, 255) },
		{ "unknown", new Color4(150, 150, 150, 255) }
	};

	private Container _chartArea = null!;
	private Container _msdPointsContainer = null!;
	private Container _accuracyPointsContainer = null!;
	private Container _msdLinesContainer = null!;
	private Container _accuracyLinesContainer = null!;
	private Container _gridContainer = null!;
	private Container _labelsContainer = null!;
	private SpriteText _titleText = null!;
	private SpriteText _noDataText = null!;

	private List<SessionPlayResult> _data = new();
	private float _msdMin = _defaultMsdMin;
	private float _msdMax = _defaultMsdMax;
	private TimeSpan _maxSessionTime = TimeSpan.Zero;

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			// Background
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(30, 30, 35, 255)
			},
			// Title
			_titleText = new SpriteText
			{
				Text = "Session Progress",
				Font = new FontUsage("", 17, "Bold"),
				Colour = new Color4(255, 102, 170, 255),
				Position = new Vector2(10, 5)
			},
			// No data text
			_noDataText = new SpriteText
			{
				Text = "No plays recorded yet",
				Font = new FontUsage("", 16),
				Colour = new Color4(120, 120, 120, 255),
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Alpha = 1
			},
			// Chart area container
			_chartArea = new Container
			{
				RelativeSizeAxes = Axes.Both,
				Padding = new MarginPadding
				{
					Left = _chartPaddingLeft,
					Right = _chartPaddingRight,
					Top = _chartPaddingTop,
					Bottom = _chartPaddingBottom
				},
				Alpha = 0,
				Children = new Drawable[]
				{
					// Grid lines
					_gridContainer = new Container
					{
						RelativeSizeAxes = Axes.Both
					},
					// MSD lines (behind points)
					_msdLinesContainer = new Container
					{
						RelativeSizeAxes = Axes.Both
					},
					// Accuracy lines (behind points)
					_accuracyLinesContainer = new Container
					{
						RelativeSizeAxes = Axes.Both
					},
					// MSD points
					_msdPointsContainer = new Container
					{
						RelativeSizeAxes = Axes.Both
					},
					// Accuracy points
					_accuracyPointsContainer = new Container
					{
						RelativeSizeAxes = Axes.Both
					}
				}
			},
			// Labels container (outside chart area for axis labels)
			_labelsContainer = new Container
			{
				RelativeSizeAxes = Axes.Both,
				Alpha = 0
			}
		};
	}

	/// <summary>
	/// Updates the chart with new session data.
	/// </summary>
	public void SetSessionData(List<SessionPlayResult> plays)
	{
		_data = plays ?? new List<SessionPlayResult>();
		Redraw();
	}

	/// <summary>
	/// Adds a single play to the chart.
	/// </summary>
	public void AddPlay(SessionPlayResult play)
	{
		_data.Add(play);
		Redraw();
	}

	/// <summary>
	/// Clears all data from the chart.
	/// </summary>
	public void Clear()
	{
		_data.Clear();
		Redraw();
	}

	/// <summary>
	/// Redraws the entire chart.
	/// </summary>
	private void Redraw()
	{
		_msdPointsContainer.Clear();
		_accuracyPointsContainer.Clear();
		_msdLinesContainer.Clear();
		_accuracyLinesContainer.Clear();
		_gridContainer.Clear();
		_labelsContainer.Clear();

		if (_data.Count == 0)
		{
			_noDataText.FadeTo(1, 200);
			_chartArea.FadeTo(0, 200);
			_labelsContainer.FadeTo(0, 200);
			return;
		}

		_noDataText.FadeTo(0, 200);
		_chartArea.FadeTo(1, 200);
		_labelsContainer.FadeTo(1, 200);

		// Calculate ranges
		CalculateRanges();

		// Draw grid and axes
		DrawGrid();
		DrawAxisLabels();

		// Draw data
		DrawMsdData();
		DrawAccuracyData();
	}

	/// <summary>
	/// Calculates the min/max ranges for the axes.
	/// </summary>
	private void CalculateRanges()
	{
		if (_data.Count == 0)
		{
			_msdMin = _defaultMsdMin;
			_msdMax = _defaultMsdMax;
			_maxSessionTime = TimeSpan.Zero;
			return;
		}

		var maxMsd = _data.Max(p => p.HighestMsdValue);
		var minMsd = _data.Min(p => p.HighestMsdValue);

		// Add some padding to the MSD range
		var msdRange = maxMsd - minMsd;
		if (msdRange < 5)
			msdRange = 5;

		_msdMin = Math.Max(0, minMsd - msdRange * 0.1f);
		_msdMax = maxMsd + msdRange * 0.1f;

		// Ensure reasonable minimum range
		if (_msdMax - _msdMin < 5) _msdMax = _msdMin + 5;

		_maxSessionTime = _data.Max(p => p.SessionTime);

		// Ensure minimum time range of 1 minute
		if (_maxSessionTime < TimeSpan.FromMinutes(1)) _maxSessionTime = TimeSpan.FromMinutes(1);
	}

	/// <summary>
	/// Draws grid lines on the chart.
	/// </summary>
	private void DrawGrid()
	{
		// Horizontal grid lines (5 lines)
		for (var i = 0; i <= 4; i++)
		{
			var y = i / 4f;
			_gridContainer.Add(new Box
			{
				RelativeSizeAxes = Axes.X,
				Height = 1,
				RelativePositionAxes = Axes.Y,
				Y = y,
				Colour = new Color4(50, 50, 55, 255)
			});
		}

		// Vertical grid lines (based on time, up to 5 lines)
		var timeLines = Math.Min(5, Math.Max(2, (int)_maxSessionTime.TotalMinutes));
		for (var i = 0; i <= timeLines; i++)
		{
			var x = i / (float)timeLines;
			_gridContainer.Add(new Box
			{
				RelativeSizeAxes = Axes.Y,
				Width = 1,
				RelativePositionAxes = Axes.X,
				X = x,
				Colour = new Color4(50, 50, 55, 255)
			});
		}
	}

	/// <summary>
	/// Draws axis labels.
	/// </summary>
	private void DrawAxisLabels()
	{
		// MSD axis labels (left side)
		for (var i = 0; i <= 4; i++)
		{
			var value = _msdMin + (_msdMax - _msdMin) * (1 - i / 4f);
			var y = _chartPaddingTop + (DrawHeight - _chartPaddingTop - _chartPaddingBottom) * (i / 4f);

			_labelsContainer.Add(new SpriteText
			{
				Text = value.ToString("F1", CultureInfo.CurrentCulture),
				Font = new FontUsage("", 16),
				Colour = new Color4(150, 150, 150, 255),
				Position = new Vector2(5, y - 5),
				Origin = Anchor.CentreLeft
			});
		}

		// Accuracy axis labels (right side)
		for (var i = 0; i <= 4; i++)
		{
			var value = _accuracyMax - (_accuracyMax - _accuracyMin) * (i / 4f);
			var y = _chartPaddingTop + (DrawHeight - _chartPaddingTop - _chartPaddingBottom) * (i / 4f);

			_labelsContainer.Add(new SpriteText
			{
				Text = $"{value:F0}%",
				Font = new FontUsage("", 16),
				Colour = Color4.White,
				Position = new Vector2(DrawWidth - 5, y - 5),
				Origin = Anchor.CentreRight
			});
		}

		// Time axis labels (bottom)
		var timeLines = Math.Min(5, Math.Max(2, (int)_maxSessionTime.TotalMinutes));
		for (var i = 0; i <= timeLines; i++)
		{
			var time = TimeSpan.FromTicks((long)(_maxSessionTime.Ticks * (i / (float)timeLines)));
			var x = _chartPaddingLeft + (DrawWidth - _chartPaddingLeft - _chartPaddingRight) * (i / (float)timeLines);

			_labelsContainer.Add(new SpriteText
			{
				Text = time.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture),
				Font = new FontUsage("", 12),
				Colour = new Color4(150, 150, 150, 255),
				Position = new Vector2(x, DrawHeight - 10),
				Origin = Anchor.TopCentre
			});
		}

		// Axis titles
		_labelsContainer.Add(new SpriteText
		{
			Text = "MSD",
			Font = new FontUsage("", 13, "Bold"),
			Colour = new Color4(255, 102, 170, 255),
			Position = new Vector2(5, _chartPaddingTop - 15),
			Origin = Anchor.BottomLeft
		});

		_labelsContainer.Add(new SpriteText
		{
			Text = "Acc",
			Font = new FontUsage("", 13, "Bold"),
			Colour = Color4.White,
			Position = new Vector2(DrawWidth - 5, _chartPaddingTop - 15),
			Origin = Anchor.BottomRight
		});
	}

	/// <summary>
	/// Draws the MSD data points and lines.
	/// </summary>
	private void DrawMsdData()
	{
		if (_data.Count == 0)
			return;

		var sortedData = _data.OrderBy(p => p.SessionTime).ToList();
		Vector2? previousPoint = null;

		foreach (var play in sortedData)
		{
			var x = (float)(play.SessionTime.TotalMilliseconds / _maxSessionTime.TotalMilliseconds);
			var y = 1f - (play.HighestMsdValue - _msdMin) / (_msdMax - _msdMin);

			// Clamp to valid range
			x = Math.Clamp(x, 0, 1);
			y = Math.Clamp(y, 0, 1);

			var color = _skillsetColors.GetValueOrDefault(play.DominantSkillset.ToLowerInvariant(),
				_skillsetColors["unknown"]);

			// Draw point
			_msdPointsContainer.Add(new Circle
			{
				Size = new Vector2(_pointRadius * 2),
				RelativePositionAxes = Axes.Both,
				Position = new Vector2(x, y),
				Origin = Anchor.Centre,
				Colour = color
			});

			// Draw line to previous point
			var currentPoint = new Vector2(x, y);
			if (previousPoint.HasValue)
				DrawLine(_msdLinesContainer, previousPoint.Value, currentPoint, color, _lineThickness);

			previousPoint = currentPoint;
		}
	}

	/// <summary>
	/// Draws the accuracy data points and lines (white, scaled 80-100%).
	/// </summary>
	private void DrawAccuracyData()
	{
		if (_data.Count == 0)
			return;

		var sortedData = _data.OrderBy(p => p.SessionTime).ToList();
		Vector2? previousPoint = null;

		foreach (var play in sortedData)
		{
			var x = (float)(play.SessionTime.TotalMilliseconds / _maxSessionTime.TotalMilliseconds);

			// Scale accuracy: 80% = 1 (bottom), 100% = 0 (top)
			var normalizedAcc = (float)Math.Clamp(play.Accuracy, _accuracyMin, _accuracyMax);
			var y = 1f - (normalizedAcc - _accuracyMin) / (_accuracyMax - _accuracyMin);

			// Clamp to valid range
			x = Math.Clamp(x, 0, 1);
			y = Math.Clamp(y, 0, 1);

			// Draw point (white)
			_accuracyPointsContainer.Add(new Circle
			{
				Size = new Vector2(_pointRadius * 2),
				RelativePositionAxes = Axes.Both,
				Position = new Vector2(x, y),
				Origin = Anchor.Centre,
				Colour = Color4.White
			});

			// Draw line to previous point
			var currentPoint = new Vector2(x, y);
			if (previousPoint.HasValue)
				DrawLine(_accuracyLinesContainer, previousPoint.Value, currentPoint, Color4.White, _lineThickness);

			previousPoint = currentPoint;
		}
	}

	/// <summary>
	/// Draws a line between two relative points in the container.
	/// </summary>
	private static void DrawLine(Container container, Vector2 from, Vector2 to, Color4 color, float thickness)
	{
		// Calculate actual positions based on container size
		// We need to use a Path for proper line drawing
		container.Add(new LineSegment(from, to, color, thickness));
	}

	/// <summary>
	/// Simple line segment drawable using Box with rotation.
	/// </summary>
	private partial class LineSegment : CompositeDrawable
	{
		public LineSegment(Vector2 from, Vector2 to, Color4 color, float thickness)
		{
			RelativeSizeAxes = Axes.Both;

			// Use relative positions
			var fromPos = from;
			var toPos = to;

			InternalChild = new RelativeLineBox(fromPos, toPos, color, thickness);
		}
	}

	/// <summary>
	/// Box that draws a line between two relative points.
	/// </summary>
	private partial class RelativeLineBox : CompositeDrawable
	{
		private readonly Vector2 _from;
		private readonly Vector2 _to;
		private readonly Color4 _color;
		private readonly float _thickness;

		public RelativeLineBox(Vector2 from, Vector2 to, Color4 color, float thickness)
		{
			_from = from;
			_to = to;
			_color = color;
			_thickness = thickness;
			RelativeSizeAxes = Axes.Both;
		}

		protected override void Update()
		{
			base.Update();

			// Clear and redraw
			ClearInternal();

			var actualFrom = new Vector2(_from.X * DrawWidth, _from.Y * DrawHeight);
			var actualTo = new Vector2(_to.X * DrawWidth, _to.Y * DrawHeight);

			var diff = actualTo - actualFrom;
			var length = diff.Length;
			var angle = MathF.Atan2(diff.Y, diff.X);

			AddInternal(new Box
			{
				Width = length,
				Height = _thickness,
				Position = actualFrom,
				Origin = Anchor.CentreLeft,
				Rotation = MathHelper.RadiansToDegrees(angle),
				Colour = _color
			});
		}
	}
}
