using Companella.Components.Misc;
using Companella.Services.Beatmap;
using Companella.Services.Platform;
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
/// Panel for migrating scores from Companella session copies to their original maps.
/// </summary>
public partial class ScoreMigrationPanel : CompositeDrawable
{
	[Resolved] private ScoreMigrationService MigrationService { get; set; } = null!;

	[Resolved] private OsuCollectionService CollectionService { get; set; } = null!;

	[Resolved] private OsuProcessDetector ProcessDetector { get; set; } = null!;

	private ConfirmationDialog? _confirmationDialog;

	private MigrationButton _migrateButton = null!;
	private MigrationButton _cleanupButton = null!;
	private SpriteText _statusText = null!;
	private SpriteText _resultText = null!;
	private bool _isWorking;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _dangerColor = new(255, 80, 80, 255);

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
						Text = "Score Migration",
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
						Text = "Move scores from session practice copies to their original beatmaps. " +
							   "This lets you keep your practice scores on the real maps."
					},
					// Info
					new TextFlowContainer(s =>
					{
						s.Font = new FontUsage("", 13);
						s.Colour = new Color4(255, 180, 100, 255);
					})
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Text =
							"osu! will restart to save scores, close for migration, then reopen. A backup is created."
					},
					// Buttons and status
					new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 6),
						Margin = new MarginPadding { Top = 4 },
						Children = new Drawable[]
						{
							// Button row
							new FillFlowContainer
							{
								AutoSizeAxes = Axes.Both,
								Direction = FillDirection.Horizontal,
								Spacing = new Vector2(8, 0),
								Children = new Drawable[]
								{
									_migrateButton = new MigrationButton
									{
										Text = "Migrate Scores",
										Width = 140,
										Height = 32,
										Action = OnMigrateClicked
									},
									_cleanupButton = new MigrationButton
									{
										Text = "Delete Session Maps",
										Width = 160,
										Height = 32,
										Action = OnCleanupClicked,
										IsDanger = true
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

	private void SetButtonsEnabled(bool enabled)
	{
		_migrateButton.Enabled.Value = enabled;
		_cleanupButton.Enabled.Value = enabled;
	}

	private void OnMigrateClicked()
	{
		if (_isWorking)
			return;

		// Check if osu! is running and show confirmation
		if (ProcessDetector.IsOsuRunning)
			ShowConfirmationDialog(
				"Force Close osu!?",
				"osu! is currently running. It will be force closed to migrate scores. Continue?",
				() => PerformMigration()
			);
		else
			PerformMigration();
	}

	private void PerformMigration()
	{
		_isWorking = true;
		SetButtonsEnabled(false);
		_statusText.Alpha = 1;
		_statusText.Text = "Restarting osu! to save scores, then migrating...";
		_resultText.Alpha = 0;

		Task.Run(() =>
		{
			var result = MigrationService.MigrateSessionScores();

			Schedule(() =>
			{
				_isWorking = false;
				SetButtonsEnabled(true);
				_statusText.Alpha = 0;

				if (result.Success)
				{
					if (result.ScoresMigrated > 0)
					{
						_resultText.Text =
							$"Migrated {result.ScoresMigrated} scores from {result.MigratedMaps.Count} maps (osu! restarted)";
						_resultText.Colour = new Color4(100, 200, 100, 255);
						_resultText.Alpha = 1;
					}
					else if (result.SessionMapsFound == 0)
					{
						_resultText.Text = "No session copy beatmaps found";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
					else
					{
						_resultText.Text = $"Found {result.SessionMapsFound} session maps, but no scores to migrate";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
				}
				else
				{
					_resultText.Text = $"Migration failed: {result.Error}";
					_resultText.Colour = new Color4(255, 100, 100, 255);
					_resultText.Alpha = 1;
				}
			});
		});
	}

	private void OnCleanupClicked()
	{
		if (_isWorking)
			return;

		// Check if osu! is running and show confirmation
		if (ProcessDetector.IsOsuRunning)
			ShowConfirmationDialog(
				"Force Close osu!?",
				"osu! is currently running. It will be force closed to cleanup session maps. Continue?",
				() => PerformCleanup()
			);
		else
			PerformCleanup();
	}

	private void PerformCleanup()
	{
		_isWorking = true;
		SetButtonsEnabled(false);
		_statusText.Alpha = 1;
		_statusText.Text = "Restarting osu! to save scores, migrating, then deleting...";
		_resultText.Alpha = 0;

		Task.Run(() =>
		{
			var result = MigrationService.CleanupSessionMaps();

			Schedule(() =>
			{
				_isWorking = false;
				SetButtonsEnabled(true);
				_statusText.Alpha = 0;

				if (result.Success)
				{
					if (result.FilesDeleted > 0)
					{
						var migrationInfo = result.MigrationResult?.ScoresMigrated > 0
							? $" ({result.MigrationResult.ScoresMigrated} scores migrated)"
							: "";
						_resultText.Text =
							$"Deleted {result.FilesDeleted} session maps{migrationInfo} - osu! restarted";
						_resultText.Colour = new Color4(100, 200, 100, 255);
						_resultText.Alpha = 1;

						if (result.FilesFailed > 0) _resultText.Text += $" ({result.FilesFailed} failed)";
					}
					else
					{
						_resultText.Text = "No session maps found to delete";
						_resultText.Colour = new Color4(160, 160, 160, 255);
						_resultText.Alpha = 1;
					}
				}
				else
				{
					_resultText.Text = $"Cleanup failed: {result.Error}";
					_resultText.Colour = new Color4(255, 100, 100, 255);
					_resultText.Alpha = 1;
				}
			});
		});
	}

	private void ShowConfirmationDialog(string title, string message, Action onConfirm)
	{
		if (_confirmationDialog == null)
		{
			_confirmationDialog = new ConfirmationDialog();
			AddInternal(_confirmationDialog);
		}

		_confirmationDialog.Confirmed -= onConfirm;
		_confirmationDialog.Confirmed += onConfirm;
		_confirmationDialog.Show(title, message, true);
	}
}

/// <summary>
/// Styled button for migration action.
/// </summary>
public partial class MigrationButton : CompositeDrawable
{
	private Box _background = null!;
	private Box _hoverOverlay = null!;
	private SpriteText _text = null!;

	public string Text { get; set; } = "Button";
	public Action? Action { get; set; }
	public bool IsDanger { get; set; }

	public readonly Bindable<bool> Enabled = new(true);

	private readonly Color4 _normalColor = new(60, 60, 70, 255);
	private readonly Color4 _dangerColor = new(100, 50, 50, 255);
	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _dangerAccentColor = new(255, 80, 80, 255);
	private readonly Color4 _disabledColor = new(45, 45, 55, 255);

	private Color4 BaseColor => IsDanger ? _dangerColor : _normalColor;
	private Color4 AccentColor => IsDanger ? _dangerAccentColor : _accentColor;

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
				Colour = BaseColor
			},
			_hoverOverlay = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = AccentColor,
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
			_background.FadeColour(e.NewValue ? BaseColor : _disabledColor, 100);
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
