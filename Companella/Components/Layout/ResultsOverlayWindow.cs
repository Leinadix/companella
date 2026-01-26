using Companella.Components.Charts;
using Companella.Models.Application;
using Companella.Services.Common;
using Companella.Services.Screenshot;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK.Graphics;

namespace Companella.Components.Layout;

/// <summary>
/// A dedicated window for displaying timing deviation analysis.
/// Uses 8:4 (2:1) aspect ratio and fills the entire window.
/// Base resolution: 800x400 (scales with UIScale).
/// </summary>
public partial class ResultsOverlayWindow : CompositeDrawable
{
	/// <summary>
	/// Base width for 8:4 aspect ratio design.
	/// </summary>
	public const int BaseWidth = 800;

	/// <summary>
	/// Base height for 8:4 aspect ratio design.
	/// </summary>
	public const int BaseHeight = 400;

	private TimingDeviationChart _chart = null!;
	private Container _contentContainer = null!;
	private Container _chartContainer = null!; // Inner container for screenshot capture
	private CustomTitleBar _titleBar = null!;
	private SpriteText _loadingText = null!;
	private SpriteText _errorText = null!;

	private bool _isVisible;
	private TimingAnalysisResult? _currentData;

	/// <summary>
	/// Event raised when the overlay is closed by the user.
	/// </summary>
	public event EventHandler? CloseRequested;

	/// <summary>
	/// Event raised when a full re-analysis is requested (e.g., when OD changes).
	/// Parameter is the requested OD value.
	/// </summary>
	public event Action<double>? ReanalysisRequested;

	[Resolved] private osu.Framework.Platform.GameHost Host { get; set; } = null!;

	[Resolved] private IRenderer Renderer { get; set; } = null!;

	[BackgroundDependencyLoader]
	private void load()
	{
		RelativeSizeAxes = Axes.Both;
		Alpha = 0;

		// Background gradient
		var bgColor1 = new Color4(18, 18, 24, 255);
		var bgColor2 = new Color4(28, 28, 36, 255);

		InternalChildren = new Drawable[]
		{
			// Main background
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = bgColor1
			},
			// Subtle gradient overlay
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(40, 40, 55, 40)
			},
			// Title bar (reusable component with dragging support)
			_titleBar = new CustomTitleBar
			{
				Depth = -1 // Ensure it's on top
			},
			// Main content area
			_contentContainer = new Container
			{
				RelativeSizeAxes = Axes.Both,
				Padding = new MarginPadding { Top = 32, Left = 12, Right = 12, Bottom = 12 },
				Child = _chartContainer = new Container
				{
					RelativeSizeAxes = Axes.Both,
					Masking = true,
					CornerRadius = 8,
					Children = new Drawable[]
					{
						new Box
						{
							RelativeSizeAxes = Axes.Both,
							Colour = new Color4(22, 22, 28, 255)
						},
						// Timing deviation chart - fills the content area
						new Container
						{
							RelativeSizeAxes = Axes.Both,
							Padding = new MarginPadding(8),
							Child = _chart = new TimingDeviationChart
							{
								RelativeSizeAxes = Axes.Both
							}
						}
					}
				}
			},
			// Loading text (centered)
			_loadingText = new SpriteText
			{
				Text = "Analyzing replay...",
				Font = new FontUsage("", 18),
				Colour = new Color4(180, 180, 190, 255),
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Alpha = 0
			},
			// Error text (centered)
			_errorText = new SpriteText
			{
				Text = "",
				Font = new FontUsage("", 16),
				Colour = new Color4(255, 120, 120, 255),
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Alpha = 0
			}
		};

		// Wire up title bar events
		_titleBar.SetTitle("Replay Analysis");
		_titleBar.SetCloseAction(() => Hide());
		_titleBar.ScreenshotRequested += OnCameraClicked;

		// Wire up chart's re-analysis request to our event
		_chart.ReanalysisRequested += od => { ReanalysisRequested?.Invoke(od); };

		// Bell curve bounds are initialized to match the graph range when data is set
		// No need to load from settings - they reset to full range each time
	}

	/// <summary>
	/// Handles the camera button click - captures the chart content area (without title bar).
	/// </summary>
	private void OnCameraClicked()
	{
		Logger.Info("[ReplayAnalysis] Screenshot requested for chart content area");
		ScreenshotService.CaptureDrawable(_chartContainer, Host.Window);
	}

	/// <summary>
	/// Sets whether the window is draggable.
	/// </summary>
	public void SetDraggable(bool draggable)
	{
		_titleBar?.SetDraggable(draggable);
	}

	/// <summary>
	/// Shows the window with loading state.
	/// </summary>
	public void ShowLoading()
	{
		_isVisible = true;
		_chart.Clear();
		_titleBar.SetTitle("Replay Analysis - Loading...");
		_loadingText.FadeTo(1, 100);
		_errorText.FadeTo(0, 100);
		_contentContainer.FadeTo(0.3f, 100);
		this.FadeTo(1, 150);
	}

	/// <summary>
	/// Gets the current analysis data (for re-analysis).
	/// </summary>
	public TimingAnalysisResult? CurrentData => _currentData;

	/// <summary>
	/// Shows the window with analysis data.
	/// </summary>
	public void ShowData(TimingAnalysisResult data)
	{
		_currentData = data;
		_isVisible = true;
		_loadingText.FadeTo(0, 100);
		_errorText.FadeTo(0, 100);
		_contentContainer.FadeTo(1, 150);
		this.FadeTo(1, 150);

		// Schedule chart data update after layout is complete
		// Double-schedule ensures DrawWidth/DrawHeight are valid
		Schedule(() => Schedule(() =>
		{
			_chart.SetData(data);
			UpdateTitleWithStats();
		}));
	}

	/// <summary>
	/// Shows the window with an error message.
	/// </summary>
	public void ShowError(string message)
	{
		_isVisible = true;
		_titleBar.SetTitle("Replay Analysis - Error");
		_loadingText.FadeTo(0, 100);
		_errorText.Text = message;
		_errorText.FadeTo(1, 100);
		_contentContainer.FadeTo(0.3f, 100);
		this.FadeTo(1, 150);
	}

	/// <summary>
	/// Hides the window.
	/// </summary>
	public new void Hide()
	{
		_isVisible = false;
		this.FadeTo(0, 150);
		CloseRequested?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Whether the window is currently visible.
	/// </summary>
	public bool IsVisible => _isVisible;

	/// <summary>
	/// Handle keyboard input for chart controls.
	/// - 1-7: Toggle column visibility
	/// - Tab: Switch between osu!mania OD and StepMania Judge system
	/// - A/D or Left/Right: Adjust OD/Judge level
	/// - Escape: Close window
	/// </summary>
	protected override bool OnKeyDown(KeyDownEvent e)
	{
		if (!_isVisible)
			return base.OnKeyDown(e);

		// Escape to close
		if (e.Key == osuTK.Input.Key.Escape)
		{
			Hide();
			return true;
		}

		// Tab to toggle between osu!mania and StepMania
		if (e.Key == osuTK.Input.Key.Tab)
		{
			_chart.ToggleSystemType();
			UpdateTitleWithStats();
			return true;
		}

		// A/Left to decrease OD/Judge, D/Right to increase
		if (e.Key == osuTK.Input.Key.A || e.Key == osuTK.Input.Key.Left)
		{
			_chart.AdjustLevel(-1);
			UpdateTitleWithStats();
			return true;
		}

		if (e.Key == osuTK.Input.Key.D || e.Key == osuTK.Input.Key.Right)
		{
			_chart.AdjustLevel(1);
			UpdateTitleWithStats();
			return true;
		}

		// Number keys 1-7 to toggle columns
		int? column = e.Key switch
		{
			osuTK.Input.Key.Number1 => 0,
			osuTK.Input.Key.Number2 => 1,
			osuTK.Input.Key.Number3 => 2,
			osuTK.Input.Key.Number4 => 3,
			osuTK.Input.Key.Number5 => 4,
			osuTK.Input.Key.Number6 => 5,
			osuTK.Input.Key.Number7 => 6,
			// Also support numpad
			osuTK.Input.Key.Keypad1 => 0,
			osuTK.Input.Key.Keypad2 => 1,
			osuTK.Input.Key.Keypad3 => 2,
			osuTK.Input.Key.Keypad4 => 3,
			osuTK.Input.Key.Keypad5 => 4,
			osuTK.Input.Key.Keypad6 => 5,
			osuTK.Input.Key.Keypad7 => 6,
			_ => null
		};

		if (column.HasValue && column.Value < _chart.KeyCount)
		{
			_chart.ToggleColumn(column.Value);
			UpdateTitleWithStats();
			return true;
		}

		return base.OnKeyDown(e);
	}

	/// <summary>
	/// Updates the title bar with current filtered statistics.
	/// </summary>
	private void UpdateTitleWithStats()
	{
		var systemName = _chart.GetHitWindowSystemName();

		// Count active columns
		var activeCount = 0;
		for (var i = 0; i < _chart.KeyCount; i++)
			if (_chart.IsColumnActive(i))
				activeCount++;

		var filterInfo = activeCount < _chart.KeyCount ? $" ({activeCount}/{_chart.KeyCount} keys)" : "";
		_titleBar.SetTitle($"Replay Analysis [{systemName}]{filterInfo}");
	}
}
