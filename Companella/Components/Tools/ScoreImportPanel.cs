using Companella.Services.Session;
using Companella.Services.Tools;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// Panel for importing older scores from scores.db as Companella sessions.
/// Only imports scores that have corresponding .osr replay files.
/// </summary>
public partial class ScoreImportPanel : CompositeDrawable
{
	[Resolved] private ScoreImportService ImportService { get; set; } = null!;

	[Resolved] private ReplayFileWatcherService ReplayWatcherService { get; set; } = null!;

	private ImportButton _importButton = null!;
	private ImportButton _reimportReplaysButton = null!;
	private SpriteText _statusText = null!;
	private SpriteText _progressText = null!;
	private SpriteText _resultText = null!;
	private bool _isWorking;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);

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
					// Header
					new SpriteText
					{
						Text = "Score Import",
						Font = new FontUsage("", 17, "Bold"),
						Colour = new Color4(180, 180, 180, 255)
					},
					// Description
					new TextFlowContainer(s =>
					{
						s.Font = new FontUsage("", 14);
						s.Colour = new Color4(140, 140, 140, 255);
					})
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Text = "Import older scores from osu!'s scores.db as Companella sessions. " +
							   "Only imports osu!mania scores that have corresponding replay files (.osr). " +
							   "Scores are grouped by calendar day into sessions."
					},
					// Warning
					new TextFlowContainer(s =>
					{
						s.Font = new FontUsage("", 13);
						s.Colour = new Color4(255, 180, 100, 255);
					})
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Text = "Note: MSD calculation runs for each play, which may take a while for many scores."
					},
					// Button and status
					new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 6),
						Margin = new MarginPadding { Top = 4 },
						Children = new Drawable[]
						{
							// Buttons row
							new FillFlowContainer
							{
								AutoSizeAxes = Axes.Both,
								Direction = FillDirection.Horizontal,
								Spacing = new Vector2(8, 0),
								Children = new Drawable[]
								{
									_importButton = new ImportButton
									{
										Text = "Import Scores as Sessions",
										Width = 200,
										Height = 32,
										Action = OnImportClicked
									},
									_reimportReplaysButton = new ImportButton
									{
										Text = "Find Missing Replays",
										Width = 160,
										Height = 32,
										Action = OnFindMissingReplaysClicked
									}
								}
							},
							_statusText = new SpriteText
							{
								Text = "",
								Font = new FontUsage("", 14),
								Colour = new Color4(160, 160, 160, 255),
								Alpha = 0
							},
							_progressText = new SpriteText
							{
								Text = "",
								Font = new FontUsage("", 13),
								Colour = new Color4(140, 140, 140, 255),
								Alpha = 0
							},
							_resultText = new SpriteText
							{
								Text = "",
								Font = new FontUsage("", 15),
								Colour = _accentColor,
								Alpha = 0
							}
						}
					}
				}
			}
		};
	}

	private void OnImportClicked()
	{
		if (_isWorking)
			return;

		_isWorking = true;
		_importButton.Enabled.Value = false;
		_statusText.Alpha = 1;
		_statusText.Text = "Starting import...";
		_progressText.Alpha = 1;
		_progressText.Text = "";
		_resultText.Alpha = 0;

		Task.Run(() =>
		{
			var result = ImportService.ImportScoresAsSessions(progress =>
			{
				Schedule(() =>
				{
					_statusText.Text = progress.Stage;
					if (!string.IsNullOrEmpty(progress.CurrentItem))
						_progressText.Text = progress.CurrentItem;
					else if (progress.Total > 0) _progressText.Text = $"{progress.Current}/{progress.Total}";
				});
			});

			Schedule(() =>
			{
				_isWorking = false;
				_importButton.Enabled.Value = true;
				_statusText.Alpha = 0;
				_progressText.Alpha = 0;

				if (result.Success)
				{
					if (result.SessionsCreated > 0)
					{
						_resultText.Text =
							$"Imported {result.PlaysImported} plays into {result.SessionsCreated} sessions";
						_resultText.Colour = new Color4(100, 200, 100, 255);
						_resultText.Alpha = 1;
					}
					else if (result.ManiaScoresFound == 0)
					{
						_resultText.Text = "No osu!mania scores found in scores.db";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
					else if (result.ScoresWithReplays == 0)
					{
						_resultText.Text = $"Found {result.ManiaScoresFound} mania scores, but none have replay files";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
					else if (result.ScoresWithBeatmaps == 0)
					{
						_resultText.Text =
							$"Found {result.ScoresWithReplays} scores with replays, but could not find beatmaps";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
					else
					{
						_resultText.Text = "Import completed with no sessions created";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
				}
				else
				{
					_resultText.Text = $"Import failed: {result.Error}";
					_resultText.Colour = new Color4(255, 100, 100, 255);
					_resultText.Alpha = 1;
				}
			});
		});
	}

	private void OnFindMissingReplaysClicked()
	{
		if (_isWorking)
			return;

		_isWorking = true;
		_importButton.Enabled.Value = false;
		_reimportReplaysButton.Enabled.Value = false;
		_statusText.Alpha = 1;
		_statusText.Text = "Finding missing replays...";
		_progressText.Alpha = 1;
		_progressText.Text = "";
		_resultText.Alpha = 0;

		Task.Run(() =>
		{
			var foundCount = ReplayWatcherService.FindAllMissingReplays((matched, total) =>
			{
				Schedule(() => { _progressText.Text = $"Found {matched} of {total} checked..."; });
			});

			Schedule(() =>
			{
				_isWorking = false;
				_importButton.Enabled.Value = true;
				_reimportReplaysButton.Enabled.Value = true;
				_statusText.Alpha = 0;
				_progressText.Alpha = 0;

				if (foundCount > 0)
				{
					_resultText.Text = $"Found {foundCount} missing replays";
					_resultText.Colour = new Color4(100, 200, 100, 255);
				}
				else
				{
					_resultText.Text = "No missing replays found";
					_resultText.Colour = new Color4(160, 160, 160, 255);
				}

				_resultText.Alpha = 1;
			});
		});
	}
}

/// <summary>
/// Styled button for import action.
/// </summary>
public partial class ImportButton : CompositeDrawable
{
	private Box _background = null!;
	private Box _hoverOverlay = null!;
	private SpriteText _text = null!;

	public string Text { get; set; } = "Button";
	public Action? Action { get; set; }

	public readonly Bindable<bool> Enabled = new(true);

	private readonly Color4 _normalColor = new(60, 60, 70, 255);
	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _disabledColor = new(45, 45, 55, 255);

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
				Colour = _accentColor,
				Alpha = 0
			},
			_text = new SpriteText
			{
				Text = Text,
				Font = new FontUsage("", 15, "Bold"),
				Colour = Color4.White,
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre
			}
		};

		Enabled.BindValueChanged(e =>
		{
			_background.FadeColour(e.NewValue ? _normalColor : _disabledColor, 100);
			_text.FadeTo(e.NewValue ? 1 : 0.5f, 100);
		}, true);
	}

	protected override bool OnHover(HoverEvent e)
	{
		if (Enabled.Value)
			_hoverOverlay.FadeTo(0.2f, 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_hoverOverlay.FadeTo(0, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		if (!Enabled.Value)
			return false;

		_hoverOverlay.FadeTo(0.4f).Then().FadeTo(0.2f, 100);
		Action?.Invoke();
		return true;
	}
}
