using Companella.Components.Misc;
using Companella.Components.Session;
using Companella.Services.Common;
using Companella.Services.Platform;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using OsuTextBox = osu.Framework.Graphics.UserInterface.TextBox;
using osuTK;
using osuTK.Graphics;
using System.Threading;
using System.Windows.Forms;

namespace Companella.Components.Settings;

/// <summary>
/// Settings for automatic osu! directory detection vs manual path (Songs folder parent).
/// </summary>
public partial class OsuDirectorySettingsPanel : CompositeDrawable
{
	private const double _autoPathRefreshSeconds = 1.0;

	[Resolved] private UserSettingsService SettingsService { get; set; } = null!;

	[Resolved] private OsuProcessDetector ProcessDetector { get; set; } = null!;

	private SettingsCheckbox _autoDetectCheckbox = null!;
	private SpriteText _readOnlyPathText = null!;
	private BasicTextBox _manualPathTextBox = null!;
	private FunctionButton _browseButton = null!;
	private FillFlowContainer _autoPathRow = null!;
	private FillFlowContainer _manualPathRow = null!;

	private double _lastAutoPathRefresh;

	[BackgroundDependencyLoader]
	private void load()
	{
		RelativeSizeAxes = Axes.X;
		AutoSizeAxes = Axes.Y;

		var settings = SettingsService.Settings;

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
						Text = "osu! Songs Directory:",
						Font = new FontUsage("", 16),
						Colour = new Color4(200, 200, 200, 255)
					},
					_autoDetectCheckbox = new SettingsCheckbox
					{
						LabelText = "Automatically Detect osu! Songs Directory",
						IsChecked = settings.AutoDetectOsuDirectory,
						TooltipText =
							"When enabled, the osu! folder is found from the running game, cache, or default install location"
					},
					new SpriteText
					{
						Text = "osu! directory (contains Songs folder):",
						Font = new FontUsage("", 13),
						Colour = new Color4(160, 160, 160, 255)
					},
					_autoPathRow = new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Vertical,
						Spacing = new Vector2(0, 4),
						Children = new Drawable[]
						{
							_readOnlyPathText = new SpriteText
							{
								RelativeSizeAxes = Axes.X,
								Text = GetAutoPathDisplayText(),
								Font = new FontUsage("", 14),
								Colour = new Color4(160, 160, 160, 255),
								Alpha = 0.7f,
								Truncate = true
							}
						}
					},
					_manualPathRow = new FillFlowContainer
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Direction = FillDirection.Horizontal,
						Spacing = new Vector2(8, 0),
						Children = new Drawable[]
						{
							new Container
							{
								Height = 32,
								Width = 420,
								Masking = true,
								CornerRadius = 4,
								Children = new Drawable[]
								{
									new Box
									{
										RelativeSizeAxes = Axes.Both,
										Colour = new Color4(35, 35, 40, 255)
									},
									_manualPathTextBox = new BasicTextBox
									{
										RelativeSizeAxes = Axes.Both,
										Text = settings.CachedOsuDirectory ?? "",
										PlaceholderText = "Path to osu! installation folder",
										CommitOnFocusLost = true
									}
								}
							},
							_browseButton = new FunctionButton("Browse")
							{
								Width = 90,
								Height = 32,
								TooltipText = "Choose the folder that contains the Songs subfolder"
							}
						}
					}
				}
			}
		};

		_autoDetectCheckbox.CheckedChanged += OnAutoDetectChanged;
		_manualPathTextBox.OnCommit += OnManualPathCommit;
		_browseButton.Clicked += OnBrowseClicked;

		UpdatePathRowVisibility();
	}

	private string GetAutoPathDisplayText()
	{
		var path = ProcessDetector.GetOsuDirectory();
		return string.IsNullOrEmpty(path) ? "(not detected)" : path;
	}

	private void OnAutoDetectChanged(bool isChecked)
	{
		if (!isChecked && string.IsNullOrWhiteSpace(SettingsService.Settings.CachedOsuDirectory))
		{
			var detected = ProcessDetector.GetOsuDirectory();
			if (!string.IsNullOrEmpty(detected))
				SettingsService.Settings.CachedOsuDirectory = detected;
		}

		SettingsService.Settings.AutoDetectOsuDirectory = isChecked;
		SaveSettings();

		if (!isChecked)
			_manualPathTextBox.Text = SettingsService.Settings.CachedOsuDirectory ?? "";

		UpdatePathRowVisibility();
		if (isChecked)
			_readOnlyPathText.Text = GetAutoPathDisplayText();
	}

	private void OnManualPathCommit(OsuTextBox sender, bool newText)
	{
		if (SettingsService.Settings.AutoDetectOsuDirectory)
			return;

		var trimmed = (_manualPathTextBox.Text ?? "").Trim();
		SettingsService.Settings.CachedOsuDirectory = string.IsNullOrEmpty(trimmed) ? null : trimmed;
		SaveSettings();
	}

	private void OnBrowseClicked()
	{
		if (SettingsService.Settings.AutoDetectOsuDirectory)
			return;

		// WinForms folder dialogs must run on an STA thread. The osu!framework game thread is not suitable
		// for ShowDialog() (freeze/deadlock). Run the dialog on a dedicated STA thread and marshal back.
		var initialDir = SettingsService.Settings.CachedOsuDirectory;
		if (string.IsNullOrEmpty(initialDir) || !Directory.Exists(initialDir))
			initialDir = null;

		var thread = new Thread(() =>
		{
			string? selected = null;
			try
			{
				using var dialog = new FolderBrowserDialog
				{
					Description = "Select your osu! installation folder (the folder that contains Songs)",
					UseDescriptionForTitle = true
				};

				if (initialDir != null)
					dialog.InitialDirectory = initialDir;

				if (dialog.ShowDialog() == DialogResult.OK)
					selected = dialog.SelectedPath?.Trim();
			}
			catch (Exception ex)
			{
				Logger.Info($"[Settings] Folder browser failed: {ex.Message}");
			}

			var path = selected;
			Schedule(() => ApplyBrowseSelection(path));
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Name = "FolderBrowserDialog STA";
		thread.Start();
	}

	private void ApplyBrowseSelection(string? selected)
	{
		if (SettingsService.Settings.AutoDetectOsuDirectory)
			return;

		if (string.IsNullOrEmpty(selected))
			return;

		var songsPath = Path.Combine(selected, "Songs");
		if (!Directory.Exists(songsPath))
			Logger.Info(
				"[Settings] Selected osu! folder has no Songs subfolder; path saved anyway. User may need to fix the path.");

		SettingsService.Settings.CachedOsuDirectory = selected;
		_manualPathTextBox.Text = selected;
		SaveSettings();
	}

	private void UpdatePathRowVisibility()
	{
		var auto = SettingsService.Settings.AutoDetectOsuDirectory;
		_autoPathRow.Alpha = auto ? 1 : 0;
		_manualPathRow.Alpha = auto ? 0 : 1;

		if (auto)
			_readOnlyPathText.Text = GetAutoPathDisplayText();
		else
			_manualPathTextBox.Text = SettingsService.Settings.CachedOsuDirectory ?? "";
	}

	protected override void Update()
	{
		base.Update();

		if (!SettingsService.Settings.AutoDetectOsuDirectory)
			return;

		var now = Clock.CurrentTime;
		if (now - _lastAutoPathRefresh < _autoPathRefreshSeconds)
			return;

		_lastAutoPathRefresh = now;
		_readOnlyPathText.Text = GetAutoPathDisplayText();
	}

	private void SaveSettings()
	{
		Task.Run(async () => await SettingsService.SaveAsync());
	}
}
