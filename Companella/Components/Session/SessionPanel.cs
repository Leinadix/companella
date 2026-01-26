using Companella.Models.Session;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Session;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Session;

/// <summary>
/// Unified panel for live session tracking and session history viewing.
/// </summary>
public partial class SessionPanel : CompositeDrawable
{
	[Resolved] private SessionTrackerService TrackerService { get; set; } = null!;

	[Resolved] private SessionDatabaseService DatabaseService { get; set; } = null!;

	[Resolved] private OsuFileParser FileParser { get; set; } = null!;

	[Resolved] private ReplayFileWatcherService ReplayWatcherService { get; set; } = null!;

	private SessionModeToggle _modeToggle = null!;
	private SessionToggleButton _toggleButton = null!;
	private SpriteText _statsText = null!;
	private SpriteText _durationText = null!;
	private SessionActivityHeatmap _activityHeatmap = null!;
	private SessionDropdown _sessionDropdown = null!;
	private FindReplaysButton _findReplaysButton = null!;
	private FillFlowContainer _playsListContainer = null!;
	private CapturedScrollContainer _scrollContainer = null!;
	private Container _liveControlsContainer = null!;
	private Container _historyControlsContainer = null!;
	private SpriteText _noPlaysText = null!;

	private readonly Bindable<SessionMode> _currentMode = new(SessionMode.Live);
	private StoredSession? _selectedSession;
	private List<StoredSession> _sessionsForDay = new();
	private readonly Dictionary<long, SessionScoreRow> _playRows = new();

	private readonly Color4 _accentColor = new(255, 102, 170, 255);

	/// <summary>
	/// Event raised when a replay analysis is requested.
	/// </summary>
	public event Action<StoredSessionPlay>? ReplayAnalysisRequested;

	/// <summary>
	/// Event raised when a beatmap analysis is requested (right-click on score).
	/// </summary>
	public event Action<StoredSessionPlay>? BeatmapAnalysisRequested;

	public SessionPanel()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChildren = new Drawable[]
		{
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Vertical,
				Spacing = new Vector2(0, 10),
				Children = new Drawable[]
				{
					// Header with title and mode toggle
					new Container
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Children = new Drawable[]
						{
							new SpriteText
							{
								Text = "Session",
								Font = new FontUsage("", 19, "Bold"),
								Colour = new Color4(180, 180, 180, 255),
								Anchor = Anchor.CentreLeft,
								Origin = Anchor.CentreLeft
							},
							_modeToggle = new SessionModeToggle
							{
								Anchor = Anchor.CentreRight,
								Origin = Anchor.CentreRight
							}
						}
					},
					// Live session controls
					_liveControlsContainer = new Container
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Child = new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Horizontal,
							Spacing = new Vector2(12, 0),
							Children = new Drawable[]
							{
								_toggleButton = new SessionToggleButton
								{
									Size = new Vector2(120, 36),
									TooltipText = "Track your plays and view progress over time"
								},
								new FillFlowContainer
								{
									AutoSizeAxes = Axes.Both,
									Direction = FillDirection.Vertical,
									Spacing = new Vector2(0, 2),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Children = new Drawable[]
									{
										_statsText = new SpriteText
										{
											Text = "Plays: 0",
											Font = new FontUsage("", 17),
											Colour = new Color4(160, 160, 160, 255)
										},
										_durationText = new SpriteText
										{
											Text = "Duration: 00:00:00",
											Font = new FontUsage("", 17),
											Colour = new Color4(120, 120, 120, 255)
										}
									}
								}
							}
						}
					},
					// History controls - depth -1 to render dropdown on top of plays list
					_historyControlsContainer = new Container
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Alpha = 0,
						Depth = -1,
						Child = new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Vertical,
							Spacing = new Vector2(0, 10),
							Children = new Drawable[]
							{
								// Activity heatmap
								_activityHeatmap = new SessionActivityHeatmap(),
								// Session selection row
								new FillFlowContainer
								{
									AutoSizeAxes = Axes.Both,
									Direction = FillDirection.Horizontal,
									Spacing = new Vector2(12, 0),
									Children = new Drawable[]
									{
										new FillFlowContainer
										{
											AutoSizeAxes = Axes.Y,
											Width = 280,
											Direction = FillDirection.Vertical,
											Spacing = new Vector2(0, 4),
											Children = new Drawable[]
											{
												new SpriteText
												{
													Text = "Session:",
													Font = new FontUsage("", 16),
													Colour = new Color4(120, 120, 130, 255)
												},
												_sessionDropdown = new SessionDropdown
												{
													RelativeSizeAxes = Axes.X
												}
											}
										},
										_findReplaysButton = new FindReplaysButton
										{
											Width = 120,
											Height = 28,
											Margin = new MarginPadding
												{ Top = 16 }, // Align with dropdown (skip label height)
											Action = OnFindReplaysClicked
										}
									}
								}
							}
						}
					},
					// Plays list wrapper - contains scroll and renders behind dropdown
					new Container
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Masking = true,
						Margin = new MarginPadding { Top = 8 },
						Children = new Drawable[]
						{
							_scrollContainer = new CapturedScrollContainer
							{
								RelativeSizeAxes = Axes.X,
								Height = 400,
								Child = _playsListContainer = new FillFlowContainer
								{
									RelativeSizeAxes = Axes.X,
									AutoSizeAxes = Axes.Y,
									Direction = FillDirection.Vertical,
									Spacing = new Vector2(0, 4)
								}
							}
						}
					},
					// No plays message
					_noPlaysText = new SpriteText
					{
						Text = "No plays yet. Start a session to record plays!",
						Font = new FontUsage("", 16),
						Colour = new Color4(120, 120, 120, 255),
						Alpha = 0
					}
				}
			}
		};

		// Wire up events
		_modeToggle.Current.BindTo(_currentMode);
		_currentMode.BindValueChanged(OnModeChanged, true);

		_toggleButton.Clicked += OnToggleClicked;
		_activityHeatmap.SelectedDate.BindValueChanged(OnDateSelected);
		_sessionDropdown.Current.ValueChanged += e => OnSessionSelected(e.NewValue);

		TrackerService.PlayRecorded += OnPlayRecorded;
		TrackerService.SessionStarted += OnSessionStarted;
		TrackerService.SessionStopped += OnSessionStopped;

		// Initialize
		UpdateButtonState();
		UpdateStats();
		LoadDatesWithSessions();

		// Auto-select current month
		_activityHeatmap.SelectedDate.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
	}

	protected override void Update()
	{
		base.Update();

		// Update duration display if tracking
		if (_currentMode.Value == SessionMode.Live && TrackerService.IsTracking)
			_durationText.Text = $"Duration: {TrackerService.SessionDuration:hh\\:mm\\:ss}";
	}

	/// <summary>
	/// Called when the mode is changed.
	/// </summary>
	private void OnModeChanged(ValueChangedEvent<SessionMode> e)
	{
		if (e.NewValue == SessionMode.Live)
		{
			_liveControlsContainer.FadeTo(1, 200);
			_historyControlsContainer.FadeTo(0, 200);
			RefreshLivePlays();
		}
		else
		{
			_liveControlsContainer.FadeTo(0, 200);
			_historyControlsContainer.FadeTo(1, 200);
			LoadDatesWithSessions();
			RefreshHistoryPlays();
		}
	}

	/// <summary>
	/// Called when a month is selected in the heatmap.
	/// </summary>
	private void OnDateSelected(ValueChangedEvent<DateTime?> e)
	{
		if (e.NewValue == null)
		{
			_sessionsForDay.Clear();
			_sessionDropdown.Items = _sessionsForDay;
			_selectedSession = null;
			RefreshHistoryPlays();
			return;
		}

		// Validate date
		var selectedMonth = e.NewValue.Value;
		if (selectedMonth.Year < 1 || selectedMonth.Year > 9999 ||
			selectedMonth.Month < 1 || selectedMonth.Month > 12)
			return;

		// Load sessions for the selected month
		var startOfMonth = new DateTime(selectedMonth.Year, selectedMonth.Month, 1);
		var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);
		_sessionsForDay = DatabaseService.GetSessionsInRange(startOfMonth, endOfMonth);
		_sessionDropdown.Items = _sessionsForDay;

		if (_sessionsForDay.Count > 0)
		{
			_sessionDropdown.Current.Value = _sessionsForDay[0];
		}
		else
		{
			_selectedSession = null;
			RefreshHistoryPlays();
		}
	}

	/// <summary>
	/// Called when a session is selected in the dropdown.
	/// </summary>
	private void OnSessionSelected(StoredSession? session)
	{
		if (session == null)
		{
			_selectedSession = null;
			RefreshHistoryPlays();
			return;
		}

		// Load full session with plays
		_selectedSession = DatabaseService.GetSessionById(session.Id);
		RefreshHistoryPlays();
	}

	/// <summary>
	/// Loads dates that have sessions for the calendar.
	/// </summary>
	private void LoadDatesWithSessions()
	{
		var sessionCounts = DatabaseService.GetSessionCountsPerDay();
		_activityHeatmap.SetSessionData(sessionCounts);
	}

	/// <summary>
	/// Refreshes the plays list for live session mode.
	/// </summary>
	private void RefreshLivePlays()
	{
		_playsListContainer.Clear();
		_playRows.Clear();

		var plays = TrackerService.Plays;

		if (plays.Count == 0)
		{
			_noPlaysText.FadeTo(1, 200);
			_noPlaysText.Text = TrackerService.IsTracking
				? "Session active. Complete a play to see it here!"
				: "No plays yet. Start a session to record plays!";
			return;
		}

		_noPlaysText.FadeTo(0, 200);

		// Convert to StoredSessionPlay for display
		for (var i = 0; i < plays.Count; i++)
		{
			var play = plays[i];
			var storedPlay = new StoredSessionPlay
			{
				Id = i,
				BeatmapPath = play.BeatmapPath,
				BeatmapHash = play.BeatmapHash,
				Accuracy = play.Accuracy,
				Misses = play.Misses,
				PauseCount = play.PauseCount,
				Grade = play.Grade,
				Status = play.Status,
				SessionTime = play.SessionTime,
				RecordedAt = play.RecordedAt,
				HighestMsdValue = play.HighestMsdValue,
				DominantSkillset = play.DominantSkillset,
				ReplayHash = play.ReplayHash,
				ReplayPath = play.ReplayPath,
				Rate = play.Rate
			};

			var row = new SessionScoreRow(storedPlay, i, FileParser)
			{
				RelativeSizeAxes = Axes.X
			};
			row.ReplayRequested += OnReplayRequested;
			row.BeatmapRequested += OnBeatmapRequested;

			_playsListContainer.Add(row);
			_playRows[i] = row;
		}
	}

	/// <summary>
	/// Refreshes the plays list for history mode.
	/// </summary>
	private void RefreshHistoryPlays()
	{
		_playsListContainer.Clear();
		_playRows.Clear();

		if (_selectedSession == null || _selectedSession.Plays.Count == 0)
		{
			_noPlaysText.FadeTo(1, 200);
			_noPlaysText.Text = _selectedSession == null
				? "Select a date and session to view plays."
				: "No plays in this session.";
			return;
		}

		_noPlaysText.FadeTo(0, 200);

		for (var i = 0; i < _selectedSession.Plays.Count; i++)
		{
			var play = _selectedSession.Plays[i];
			var row = new SessionScoreRow(play, i, FileParser)
			{
				RelativeSizeAxes = Axes.X
			};
			row.ReplayRequested += OnReplayRequested;
			row.BeatmapRequested += OnBeatmapRequested;

			_playsListContainer.Add(row);
			_playRows[play.Id] = row;
		}
	}

	/// <summary>
	/// Called when the toggle button is clicked.
	/// </summary>
	private void OnToggleClicked()
	{
		if (TrackerService.IsTracking)
			TrackerService.StopSession();
		else
			TrackerService.StartSession();

		UpdateButtonState();
	}

	/// <summary>
	/// Called when a play is recorded.
	/// </summary>
	private void OnPlayRecorded(object? sender, SessionPlayResult play)
	{
		Schedule(() =>
		{
			if (_currentMode.Value == SessionMode.Live) RefreshLivePlays();

			UpdateStats();
		});
	}

	/// <summary>
	/// Called when a session starts.
	/// </summary>
	private void OnSessionStarted(object? sender, EventArgs e)
	{
		Schedule(() =>
		{
			if (_currentMode.Value == SessionMode.Live) RefreshLivePlays();

			UpdateButtonState();
			UpdateStats();
		});
	}

	/// <summary>
	/// Called when a session stops.
	/// </summary>
	private void OnSessionStopped(object? sender, EventArgs e)
	{
		Schedule(() =>
		{
			UpdateButtonState();
			LoadDatesWithSessions();
		});
	}

	/// <summary>
	/// Called when a replay is requested.
	/// </summary>
	private void OnReplayRequested(StoredSessionPlay play)
	{
		ReplayAnalysisRequested?.Invoke(play);
	}

	/// <summary>
	/// Called when a beatmap analysis is requested (right-click).
	/// </summary>
	private void OnBeatmapRequested(StoredSessionPlay play)
	{
		BeatmapAnalysisRequested?.Invoke(play);
	}

	/// <summary>
	/// Updates the button state.
	/// </summary>
	private void UpdateButtonState()
	{
		_toggleButton.SetTracking(TrackerService.IsTracking);
	}

	/// <summary>
	/// Updates the stats display.
	/// </summary>
	private void UpdateStats()
	{
		var playCount = TrackerService.PlayCount;
		_statsText.Text = $"Plays: {playCount}";

		if (TrackerService.IsTracking)
			_durationText.Text = $"Duration: {TrackerService.SessionDuration:hh\\:mm\\:ss}";
		else
			_durationText.Text = "Duration: 00:00:00";
	}

	/// <summary>
	/// Updates replay status for a specific play.
	/// </summary>
	public void UpdateReplayStatus(long playId, bool hasReplay)
	{
		if (_playRows.TryGetValue(playId, out var row)) Schedule(() => row.UpdateReplayStatus(hasReplay));
	}

	/// <summary>
	/// Called when the find replays button is clicked.
	/// </summary>
	private void OnFindReplaysClicked()
	{
		if (_selectedSession == null)
			return;

		_findReplaysButton.SetLoading(true);

		var sessionId = _selectedSession.Id;

		Task.Run(() =>
		{
			try
			{
				var found = ReplayWatcherService.FindReplaysForSession(sessionId,
					(matched, total) => { Schedule(() => _findReplaysButton.SetProgress($"{matched}/{total}")); });

				Schedule(() =>
				{
					_findReplaysButton.SetLoading(false);
					_findReplaysButton.SetProgress($"Found {found}");

					// Reload session data to show updated replay status
					_selectedSession = DatabaseService.GetSessionById(sessionId);
					RefreshHistoryPlays();
				});
			}
			catch (Exception ex)
			{
				Schedule(() =>
				{
					_findReplaysButton.SetLoading(false);
					_findReplaysButton.SetProgress("Error");
				});
				Logger.Info($"[SessionPanel] Error finding replays: {ex.Message}");
			}
		});
	}

	protected override void Dispose(bool isDisposing)
	{
		if (TrackerService != null)
		{
			TrackerService.PlayRecorded -= OnPlayRecorded;
			TrackerService.SessionStarted -= OnSessionStarted;
			TrackerService.SessionStopped -= OnSessionStopped;
		}

		base.Dispose(isDisposing);
	}
}

/// <summary>
/// Session mode enum.
/// </summary>
public enum SessionMode
{
	Live,
	History
}

/// <summary>
/// Toggle for switching between live and history modes.
/// </summary>
public partial class SessionModeToggle : CompositeDrawable
{
	public Bindable<SessionMode> Current { get; } = new(SessionMode.Live);

	private Box _liveBackground = null!;
	private Box _historyBackground = null!;
	private SpriteText _liveText = null!;
	private SpriteText _historyText = null!;

	private readonly Color4 _activeColor = new(255, 102, 170, 255);
	private readonly Color4 _inactiveColor = new(60, 60, 65, 255);
	private readonly Color4 _activeTextColor = Color4.White;
	private readonly Color4 _inactiveTextColor = new(140, 140, 140, 255);

	[BackgroundDependencyLoader]
	private void load()
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
					Colour = new Color4(40, 40, 45, 255)
				},
				new FillFlowContainer
				{
					AutoSizeAxes = Axes.Both,
					Direction = FillDirection.Horizontal,
					Spacing = new Vector2(2, 0),
					Padding = new MarginPadding(2),
					Children = new Drawable[]
					{
						new ClickableContainer
						{
							Size = new Vector2(70, 28),
							Masking = true,
							CornerRadius = 3,
							Action = () => Current.Value = SessionMode.Live,
							Children = new Drawable[]
							{
								_liveBackground = new Box
								{
									RelativeSizeAxes = Axes.Both,
									Colour = _activeColor
								},
								_liveText = new SpriteText
								{
									Text = "Live",
									Font = new FontUsage("", 15, "Bold"),
									Colour = _activeTextColor,
									Anchor = Anchor.Centre,
									Origin = Anchor.Centre
								}
							}
						},
						new ClickableContainer
						{
							Size = new Vector2(70, 28),
							Masking = true,
							CornerRadius = 3,
							Action = () => Current.Value = SessionMode.History,
							Children = new Drawable[]
							{
								_historyBackground = new Box
								{
									RelativeSizeAxes = Axes.Both,
									Colour = _inactiveColor
								},
								_historyText = new SpriteText
								{
									Text = "History",
									Font = new FontUsage("", 15, "Bold"),
									Colour = _inactiveTextColor,
									Anchor = Anchor.Centre,
									Origin = Anchor.Centre
								}
							}
						}
					}
				}
			}
		};

		Current.BindValueChanged(OnModeChanged, true);
	}

	private void OnModeChanged(ValueChangedEvent<SessionMode> e)
	{
		if (e.NewValue == SessionMode.Live)
		{
			_liveBackground.FadeColour(_activeColor, 200);
			_liveText.FadeColour(_activeTextColor, 200);
			_historyBackground.FadeColour(_inactiveColor, 200);
			_historyText.FadeColour(_inactiveTextColor, 200);
		}
		else
		{
			_liveBackground.FadeColour(_inactiveColor, 200);
			_liveText.FadeColour(_inactiveTextColor, 200);
			_historyBackground.FadeColour(_activeColor, 200);
			_historyText.FadeColour(_activeTextColor, 200);
		}
	}
}

/// <summary>
/// Clickable container for mode toggle buttons.
/// </summary>
public partial class ClickableContainer : Container
{
	public Action? Action { get; set; }

	protected override bool OnClick(ClickEvent e)
	{
		Action?.Invoke();
		return true;
	}
}

/// <summary>
/// Button for finding replays with loading indicator.
/// </summary>
public partial class FindReplaysButton : CompositeDrawable
{
	private Box _background = null!;
	private Box _hoverOverlay = null!;
	private SpriteText _label = null!;
	private SpriteText _progressText = null!;
	private LoadingSpinner _spinner = null!;
	private bool _isLoading;

	private readonly Color4 _normalColor = new(80, 80, 90, 255);
	private readonly Color4 _loadingColor = new(60, 60, 70, 255);

	public Action? Action { get; set; }

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 4;

		InternalChildren = new Drawable[]
		{
			_background = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = _normalColor
			},
			_hoverOverlay = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = Color4.White,
				Alpha = 0
			},
			new FillFlowContainer
			{
				AutoSizeAxes = Axes.Both,
				Direction = FillDirection.Horizontal,
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Spacing = new Vector2(6, 0),
				Children = new Drawable[]
				{
					_spinner = new LoadingSpinner
					{
						Size = new Vector2(14, 14),
						Alpha = 0
					},
					_label = new SpriteText
					{
						Text = "Find Replays",
						Font = new FontUsage("", 15, "Bold"),
						Colour = Color4.White,
						Anchor = Anchor.CentreLeft,
						Origin = Anchor.CentreLeft
					}
				}
			},
			_progressText = new SpriteText
			{
				Text = "",
				Font = new FontUsage("", 13),
				Colour = new Color4(180, 180, 180, 255),
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Alpha = 0
			}
		};
	}

	public void SetLoading(bool loading)
	{
		_isLoading = loading;

		if (loading)
		{
			_background.FadeColour(_loadingColor, 100);
			_spinner.FadeTo(1, 100);
			_label.FadeTo(0, 100);
			_progressText.FadeTo(0, 100);
		}
		else
		{
			_background.FadeColour(_normalColor, 100);
			_spinner.FadeTo(0, 100);
			_label.FadeTo(1, 100);
		}
	}

	public void SetProgress(string text)
	{
		_progressText.Text = text;
		if (!_isLoading && !string.IsNullOrEmpty(text)) _progressText.FadeTo(1, 100).Then().Delay(2000).FadeTo(0, 500);
	}

	protected override bool OnHover(HoverEvent e)
	{
		if (!_isLoading)
			_hoverOverlay.FadeTo(0.1f, 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_hoverOverlay.FadeTo(0, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		if (!_isLoading)
		{
			_hoverOverlay.FadeTo(0.2f, 50).Then().FadeTo(0.1f, 100);
			Action?.Invoke();
		}

		return true;
	}
}

/// <summary>
/// Simple loading spinner.
/// </summary>
public partial class LoadingSpinner : CompositeDrawable
{
	private Box _dot = null!;

	[BackgroundDependencyLoader]
	private void load()
	{
		InternalChild = _dot = new Box
		{
			RelativeSizeAxes = Axes.Both,
			Colour = new Color4(255, 102, 170, 255)
		};

		// Pulsing animation
		_dot.Loop(d => d
			.FadeTo(0.3f, 400)
			.Then()
			.FadeTo(1f, 400)
		);
	}
}

/// <summary>
/// Scroll container that captures scroll events but passes through at boundaries.
/// </summary>
public partial class CapturedScrollContainer : BasicScrollContainer
{
	protected override bool OnScroll(ScrollEvent e)
	{
		// Check if we're at a boundary and scrolling in that direction
		var scrollDelta = e.ScrollDelta.Y;
		var atTop = Current <= 0;
		var atBottom = Current >= ScrollableExtent;

		// If at top and scrolling up, or at bottom and scrolling down, let parent handle it
		if ((atTop && scrollDelta > 0) || (atBottom && scrollDelta < 0)) return false;

		// Otherwise handle it ourselves
		return base.OnScroll(e);
	}
}
