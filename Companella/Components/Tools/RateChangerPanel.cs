using System.Globalization;
using Companella.Components.Session;
using Companella.Services.Tools;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using TextBox = osu.Framework.Graphics.UserInterface.TextBox;

namespace Companella.Components.Tools;

/// <summary>
/// Modern panel for changing beatmap playback rate.
/// </summary>
public partial class RateChangerPanel : CompositeDrawable
{
	private StyledTextBox _rateTextBox = null!;
	private StyledTextBox _targetBpmTextBox = null!;
	private StyledTextBox _formatTextBox = null!;
	private ModernButton _applyButton = null!;
	private SpriteText _previewText = null!;
	private SpriteText _currentBpmLabel = null!;
	private FillFlowContainer _quickRateButtons = null!;
	private SettingsCheckbox _pitchAdjustCheckbox = null!;
	private BasicSliderBar<double> _odSlider = null!;
	private BasicSliderBar<double> _hpSlider = null!;
	private SpriteText _odValueText = null!;
	private SpriteText _hpValueText = null!;
	private LockButton _odLockButton = null!;
	private LockButton _hpLockButton = null!;

	public event Action<double, string, bool, double, double>? ApplyRateClicked;
	public event Action<string>? FormatChanged;
	public event Action<double, string>? PreviewRequested;
	public event Action<bool>? PitchAdjustChanged;

	private double _currentRate = 1.0;
	private double _currentMapBpm = 120.0;
	private double _targetBpm = 120.0;
	private bool _pitchAdjust = true;
	private double _currentOd = 8.0;
	private double _currentHp = 8.0;
	private bool _odLocked;
	private bool _hpLocked;
	private string _currentFormat = RateChanger.DefaultNameFormat;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);

	public RateChangerPanel()
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
				Spacing = new Vector2(0, 12),
				Children = new Drawable[]
				{
					// Pitch Adjust Checkbox
					_pitchAdjustCheckbox = new SettingsCheckbox
					{
						LabelText = "Change Pitch",
						IsChecked = true,
						TooltipText = "When unchecked, preserves original pitch (nightcore-style)"
					},
					// OD/HP Sliders Section
					CreateSection("Difficulty Settings", new Drawable[]
					{
						// OD Slider Row
						new Container
						{
							RelativeSizeAxes = Axes.X,
							Height = 28,
							Children = new Drawable[]
							{
								new SpriteText
								{
									Text = "OD",
									Font = new FontUsage("", 15),
									Colour = new Color4(140, 140, 140, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Width = 25
								},
								new Container
								{
									RelativeSizeAxes = Axes.X,
									Height = 20,
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Padding = new MarginPadding { Left = 30, Right = 80 },
									Child = _odSlider = new BasicSliderBar<double>
									{
										RelativeSizeAxes = Axes.X,
										Height = 20,
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft,
										Current = new BindableDouble(8.0)
											{ MinValue = 0, MaxValue = 10, Precision = 0.1 },
										BackgroundColour = new Color4(40, 40, 45, 255),
										SelectionColour = _accentColor
									}
								},
								_odValueText = new SpriteText
								{
									Text = "8.0",
									Font = new FontUsage("", 15),
									Colour = new Color4(200, 200, 200, 255),
									Anchor = Anchor.CentreRight,
									Origin = Anchor.CentreRight,
									Margin = new MarginPadding { Right = 35 }
								},
								_odLockButton = new LockButton
								{
									Size = new Vector2(24, 24),
									Anchor = Anchor.CentreRight,
									Origin = Anchor.CentreRight,
									TooltipText = "Lock OD value when changing maps"
								}
							}
						},
						// HP Slider Row
						new Container
						{
							RelativeSizeAxes = Axes.X,
							Height = 28,
							Margin = new MarginPadding { Top = 4 },
							Children = new Drawable[]
							{
								new SpriteText
								{
									Text = "HP",
									Font = new FontUsage("", 15),
									Colour = new Color4(140, 140, 140, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Width = 25
								},
								new Container
								{
									RelativeSizeAxes = Axes.X,
									Height = 20,
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Padding = new MarginPadding { Left = 30, Right = 80 },
									Child = _hpSlider = new BasicSliderBar<double>
									{
										RelativeSizeAxes = Axes.X,
										Height = 20,
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft,
										Current = new BindableDouble(8.0)
											{ MinValue = 0, MaxValue = 10, Precision = 0.1 },
										BackgroundColour = new Color4(40, 40, 45, 255),
										SelectionColour = _accentColor
									}
								},
								_hpValueText = new SpriteText
								{
									Text = "8.0",
									Font = new FontUsage("", 15),
									Colour = new Color4(200, 200, 200, 255),
									Anchor = Anchor.CentreRight,
									Origin = Anchor.CentreRight,
									Margin = new MarginPadding { Right = 35 }
								},
								_hpLockButton = new LockButton
								{
									Size = new Vector2(24, 24),
									Anchor = Anchor.CentreRight,
									Origin = Anchor.CentreRight,
									TooltipText = "Lock HP value when changing maps"
								}
							}
						}
					}),
					// Rate Selection Section
					CreateSection("Rate", new Drawable[]
					{
						// Quick rate buttons
						_quickRateButtons = new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Horizontal,
							Spacing = new Vector2(6, 6),
							Children = CreateQuickRateButtons()
						},
						// Custom rate input + Target BPM input on same row
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Horizontal,
							Spacing = new Vector2(8, 0),
							Margin = new MarginPadding { Top = 8 },
							Children = new Drawable[]
							{
								new SpriteText
								{
									Text = "Custom:",
									Font = new FontUsage("", 16),
									Colour = new Color4(140, 140, 140, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft
								},
								_rateTextBox = new StyledTextBox
								{
									Size = new Vector2(70, 32),
									PlaceholderText = "1.0"
								},
								new SpriteText
								{
									Text = "x",
									Font = new FontUsage("", 17),
									Colour = new Color4(100, 100, 100, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft
								},
								new SpriteText
								{
									Text = "Target BPM:",
									Font = new FontUsage("", 16),
									Colour = new Color4(140, 140, 140, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Margin = new MarginPadding { Left = 12 }
								},
								_targetBpmTextBox = new StyledTextBox
								{
									Size = new Vector2(70, 32),
									PlaceholderText = "120"
								},
								_currentBpmLabel = new SpriteText
								{
									Text = "(Current: --)",
									Font = new FontUsage("", 14),
									Colour = new Color4(100, 100, 100, 255),
									Anchor = Anchor.CentreLeft,
									Origin = Anchor.CentreLeft,
									Margin = new MarginPadding { Left = 4 }
								}
							}
						}
					}),
					// Format Section
					CreateSection("Difficulty Name Format", new Drawable[]
					{
						_formatTextBox = new StyledTextBox
						{
							RelativeSizeAxes = Axes.X,
							Height = 32,
							PlaceholderText = "[[name]] [[rate]]"
						},
						new SpriteText
						{
							Text = "Available: [[name]] [[rate]] [[bpm]] [[od]] [[hp]] [[msd]]",
							Font = new FontUsage("", 17),
							Colour = new Color4(90, 90, 90, 255),
							Margin = new MarginPadding { Top = 4 }
						}
					}),
					// Preview Section
					new Container
					{
						RelativeSizeAxes = Axes.X,
						AutoSizeAxes = Axes.Y,
						Children = new Drawable[]
						{
							new FillFlowContainer
							{
								RelativeSizeAxes = Axes.X,
								AutoSizeAxes = Axes.Y,
								Direction = FillDirection.Horizontal,
								Spacing = new Vector2(8, 0),
								Children = new Drawable[]
								{
									new SpriteText
									{
										Text = "Preview:",
										Font = new FontUsage("", 15),
										Colour = new Color4(120, 120, 120, 255),
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft
									},
									_previewText = new SpriteText
									{
										Text = "...",
										Font = new FontUsage("", 15),
										Colour = _accentColor,
										Anchor = Anchor.CentreLeft,
										Origin = Anchor.CentreLeft
									}
								}
							}
						}
					},
					// Apply Button
					_applyButton = new ModernButton("Create Rate-Changed Beatmap")
					{
						RelativeSizeAxes = Axes.X,
						Height = 40,
						Enabled = false,
						TooltipText = "Create a new difficulty with modified audio speed"
					}
				}
			}
		};

		// Set initial values
		_rateTextBox.Text = "1.0";
		_targetBpmTextBox.Text = "120";
		_formatTextBox.Text = _currentFormat;

		// Wire up events - use value change for immediate feedback
		_rateTextBox.Current.BindValueChanged(_ => OnRateTextChanged());
		_targetBpmTextBox.Current.BindValueChanged(_ => OnTargetBpmTextChanged());
		_formatTextBox.Current.BindValueChanged(_ => OnFormatTextChanged());
		_applyButton.Clicked += OnApplyClicked;
		_pitchAdjustCheckbox.CheckedChanged += OnPitchAdjustChanged;

		// OD/HP slider events
		_odSlider.Current.ValueChanged += e => OnOdSliderChanged(e.NewValue);
		_hpSlider.Current.ValueChanged += e => OnHpSliderChanged(e.NewValue);
		_odLockButton.LockChanged += OnOdLockChanged;
		_hpLockButton.LockChanged += OnHpLockChanged;
	}

	private void OnPitchAdjustChanged(bool isChecked)
	{
		_pitchAdjust = isChecked;
		PitchAdjustChanged?.Invoke(isChecked);
	}

	private void OnOdSliderChanged(double value)
	{
		_currentOd = value;
		_odValueText.Text = value.ToString("0.0", CultureInfo.InvariantCulture);
	}

	private void OnHpSliderChanged(double value)
	{
		_currentHp = value;
		_hpValueText.Text = value.ToString("0.0", CultureInfo.InvariantCulture);
	}

	private void OnOdLockChanged(bool isLocked)
	{
		_odLocked = isLocked;
		_odSlider.Alpha = isLocked ? 0.5f : 1.0f;
	}

	private void OnHpLockChanged(bool isLocked)
	{
		_hpLocked = isLocked;
		_hpSlider.Alpha = isLocked ? 0.5f : 1.0f;
	}

	private Drawable[] CreateQuickRateButtons()
	{
		var rates = new[] { 0.5, 0.6, 0.7, 0.8, 0.9, 1.1, 1.2, 1.3, 1.4 };
		var buttons = new List<Drawable>();

		foreach (var rate in rates)
			buttons.Add(new QuickRateButton(rate, rate == 1.0, _accentColor)
			{
				Size = new Vector2(42, 28),
				Action = () => SetRate(rate),
				TooltipText = $"Create a {rate:0.0#}x speed version"
			});

		return buttons.ToArray();
	}

	private static Container CreateSection(string title, Drawable[] content)
	{
		return new Container
		{
			RelativeSizeAxes = Axes.X,
			AutoSizeAxes = Axes.Y,
			Children = new Drawable[]
			{
				new FillFlowContainer
				{
					RelativeSizeAxes = Axes.X,
					AutoSizeAxes = Axes.Y,
					Direction = FillDirection.Vertical,
					Spacing = new Vector2(0, 6),
					Children = new Drawable[]
					{
						new SpriteText
						{
							Text = title,
							Font = new FontUsage("", 15, "Bold"),
							Colour = new Color4(180, 180, 180, 255)
						},
						new FillFlowContainer
						{
							RelativeSizeAxes = Axes.X,
							AutoSizeAxes = Axes.Y,
							Direction = FillDirection.Vertical,
							Children = content
						}
					}
				}
			}
		};
	}

	/// <summary>
	/// Sets the rate value programmatically.
	/// </summary>
	public void SetRate(double rate)
	{
		_currentRate = rate;
		_rateTextBox.Text = rate.ToString("0.0#", CultureInfo.InvariantCulture);

		// Update target BPM based on new rate
		if (_currentMapBpm > 0)
		{
			_targetBpm = _currentMapBpm * _currentRate;
			_targetBpmTextBox.Text = _targetBpm.ToString("0.#", CultureInfo.InvariantCulture);
		}

		UpdateQuickRateButtonSelection(rate);
		UpdatePreview();
	}

	private void UpdateQuickRateButtonSelection(double selectedRate)
	{
		foreach (var child in _quickRateButtons.Children)
			if (child is QuickRateButton button)
				button.SetSelected(Math.Abs(button.Rate - selectedRate) < 0.001);
	}

	private void OnRateTextChanged()
	{
		if (double.TryParse(_rateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
		{
			_currentRate = Math.Clamp(value, 0.1, 5.0);

			// Update target BPM based on new rate
			if (_currentMapBpm > 0)
			{
				_targetBpm = _currentMapBpm * _currentRate;
				_targetBpmTextBox.Text = _targetBpm.ToString("0.#", CultureInfo.InvariantCulture);
			}

			UpdateQuickRateButtonSelection(_currentRate);
			UpdatePreview();
		}
	}

	private void OnRateTextCommit(TextBox sender, bool newText)
	{
		if (double.TryParse(sender.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
		{
			_currentRate = Math.Clamp(value, 0.1, 5.0);
			sender.Text = _currentRate.ToString("0.0#", CultureInfo.InvariantCulture);
		}
		else
		{
			sender.Text = _currentRate.ToString("0.0#", CultureInfo.InvariantCulture);
		}

		// Update target BPM based on new rate
		if (_currentMapBpm > 0)
		{
			_targetBpm = _currentMapBpm * _currentRate;
			_targetBpmTextBox.Text = _targetBpm.ToString("0.#", CultureInfo.InvariantCulture);
		}

		UpdateQuickRateButtonSelection(_currentRate);
		UpdatePreview();
	}

	private void OnTargetBpmTextChanged()
	{
		if (double.TryParse(_targetBpmTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
		{
			_targetBpm = Math.Clamp(value, 10, 1000);

			// Calculate rate from target BPM and update rate textbox
			if (_currentMapBpm > 0)
			{
				_currentRate = Math.Clamp(_targetBpm / _currentMapBpm, 0.1, 5.0);
				_rateTextBox.Text = _currentRate.ToString("0.0#", CultureInfo.InvariantCulture);
				UpdateQuickRateButtonSelection(_currentRate);
			}

			UpdatePreview();
		}
	}

	private void OnTargetBpmTextCommit(TextBox sender, bool newText)
	{
		if (double.TryParse(sender.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
		{
			_targetBpm = Math.Clamp(value, 10, 1000);
			sender.Text = _targetBpm.ToString("0.#", CultureInfo.InvariantCulture);

			// Calculate rate from target BPM and update rate textbox
			if (_currentMapBpm > 0)
			{
				_currentRate = Math.Clamp(_targetBpm / _currentMapBpm, 0.1, 5.0);
				_rateTextBox.Text = _currentRate.ToString("0.0#", CultureInfo.InvariantCulture);
				UpdateQuickRateButtonSelection(_currentRate);
			}
		}
		else
		{
			sender.Text = _targetBpm.ToString("0.#", CultureInfo.InvariantCulture);
		}

		UpdatePreview();
	}

	/// <summary>
	/// Sets the current map's BPM for target BPM calculations.
	/// </summary>
	public void SetCurrentMapBpm(double bpm)
	{
		_currentMapBpm = bpm > 0 ? bpm : 120;
		_currentBpmLabel.Text = $"(Current: {_currentMapBpm:0.#})";

		// Update target BPM based on current rate
		_targetBpm = _currentMapBpm * _currentRate;
		_targetBpmTextBox.Text = _targetBpm.ToString("0.#", CultureInfo.InvariantCulture);

		UpdatePreview();
	}

	private void OnFormatTextChanged()
	{
		_currentFormat = _formatTextBox.Text;
		if (!string.IsNullOrWhiteSpace(_currentFormat))
		{
			UpdatePreview();
			FormatChanged?.Invoke(_currentFormat);
		}
	}

	private void OnFormatTextCommit(TextBox sender, bool newText)
	{
		_currentFormat = sender.Text;
		if (string.IsNullOrWhiteSpace(_currentFormat))
		{
			_currentFormat = RateChanger.DefaultNameFormat;
			sender.Text = _currentFormat;
		}

		UpdatePreview();
		FormatChanged?.Invoke(_currentFormat);
	}

	private void UpdatePreview()
	{
		PreviewRequested?.Invoke(_currentRate, _currentFormat);
	}

	private void OnApplyClicked()
	{
		ApplyRateClicked?.Invoke(_currentRate, _currentFormat, _pitchAdjust, _currentOd, _currentHp);
	}

	/// <summary>
	/// Gets or sets whether pitch is adjusted with rate.
	/// </summary>
	public bool PitchAdjust
	{
		get => _pitchAdjust;
		set
		{
			if (_pitchAdjust == value)
				return;
			_pitchAdjust = value;
			if (_pitchAdjustCheckbox != null) _pitchAdjustCheckbox.IsChecked = value;
		}
	}

	public void SetPreviewText(string text)
	{
		_previewText.Text = text;
	}

	public void SetEnabled(bool enabled)
	{
		_applyButton.Enabled = enabled;
	}

	public void SetFormat(string format)
	{
		if (string.IsNullOrWhiteSpace(format))
			format = RateChanger.DefaultNameFormat;

		_currentFormat = format;
		_formatTextBox.Text = format;
	}

	public string CurrentFormat => _currentFormat;

	/// <summary>
	/// Updates OD/HP values from the current map (unless locked).
	/// </summary>
	public void SetMapDifficultyValues(double od, double hp)
	{
		if (!_odLocked)
		{
			_currentOd = od;
			_odSlider.Current.Value = od;
			_odValueText.Text = od.ToString("0.0", CultureInfo.InvariantCulture);
		}

		if (!_hpLocked)
		{
			_currentHp = hp;
			_hpSlider.Current.Value = hp;
			_hpValueText.Text = hp.ToString("0.0", CultureInfo.InvariantCulture);
		}
	}
}

/// <summary>
/// Quick rate selection button with selection state.
/// </summary>
public partial class QuickRateButton : CompositeDrawable, IHasTooltip
{
	public double Rate { get; }
	public Action? Action { get; set; }

	/// <summary>
	/// Tooltip text displayed on hover.
	/// </summary>
	public LocalisableString TooltipText { get; set; }

	private bool _isSelected;
	private Box _background = null!;
	private Box _hoverOverlay = null!;
	private SpriteText _label = null!;
	private readonly Color4 _accentColor;

	private readonly Color4 _normalBg = new(45, 45, 50, 255);
	private readonly Color4 _selectedBg = new(255, 102, 170, 255);
	private readonly Color4 _hoverBg = new(60, 60, 65, 255);

	public QuickRateButton(double rate, bool isSelected, Color4 accentColor)
	{
		Rate = rate;
		_isSelected = isSelected;
		_accentColor = accentColor;
	}

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
				Colour = _isSelected ? _selectedBg : _normalBg
			},
			_hoverOverlay = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = Color4.White,
				Alpha = 0
			},
			_label = new SpriteText
			{
				Text = $"{Rate:0.0#}x",
				Font = new FontUsage("", 12, _isSelected ? "Bold" : ""),
				Colour = _isSelected ? Color4.White : new Color4(180, 180, 180, 255),
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre
			}
		};
	}

	public void SetSelected(bool selected)
	{
		_isSelected = selected;
		_background.FadeColour(_isSelected ? _selectedBg : _normalBg, 150);
		_label.FadeColour(_isSelected ? Color4.White : new Color4(180, 180, 180, 255), 150);
		_label.Font = new FontUsage("", 12, _isSelected ? "Bold" : "");
	}

	protected override bool OnHover(HoverEvent e)
	{
		if (!_isSelected)
		{
			_hoverOverlay.FadeTo(0.1f, 100);
			_background.FadeColour(_hoverBg, 100);
		}

		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_hoverOverlay.FadeTo(0, 100);
		if (!_isSelected)
			_background.FadeColour(_normalBg, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		Action?.Invoke();
		return true;
	}
}

/// <summary>
/// Modern styled text box with clean appearance.
/// </summary>
public partial class StyledTextBox : BasicTextBox
{
	// Use test font for Unicode character support (Greek letters, symbols)
	private static readonly FontUsage _unicodeFont = new("Noto-Basic", 16);

	public StyledTextBox()
	{
		CornerRadius = 4;
		BackgroundFocused = new Color4(50, 50, 55, 255);
		BackgroundUnfocused = new Color4(35, 35, 40, 255);
		Masking = true;
	}

	protected override SpriteText CreatePlaceholder()
	{
		return new SpriteText
		{
			Font = _unicodeFont,
			Colour = new Color4(80, 80, 80, 255)
		};
	}

	protected override Drawable GetDrawableCharacter(char c)
	{
		return new SpriteText
		{
			Text = c.ToString(),
			Font = _unicodeFont,
			Colour = Color4.White
		};
	}
}

/// <summary>
/// Modern action button with clean styling.
/// </summary>
public partial class ModernButton : CompositeDrawable, IHasTooltip
{
	private readonly string _text;
	private Box _background = null!;
	private Box _hoverOverlay = null!;
	private SpriteText _label = null!;
	private bool _isEnabled = true;

	private Color4 _enabledColor = new(255, 102, 170, 255);
	private Color4 _disabledColor = new(60, 60, 65, 255);

	/// <summary>
	/// Tooltip text displayed on hover.
	/// </summary>
	public LocalisableString TooltipText { get; set; }

	public event Action? Clicked;

	public bool Enabled
	{
		get => _isEnabled;
		set
		{
			_isEnabled = value;
			if (_background != null)
			{
				_background.FadeColour(_isEnabled ? _enabledColor : _disabledColor, 150);
				_label.FadeColour(_isEnabled ? Color4.White : new Color4(100, 100, 100, 255), 150);
			}
		}
	}

	public ModernButton(string text, Color4? enabledColor = null, Color4? disabledColor = null)
	{
		_text = text;
		_enabledColor = enabledColor ?? _enabledColor;
		_disabledColor = disabledColor ?? _disabledColor;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 6;

		InternalChildren = new Drawable[]
		{
			_background = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = _isEnabled ? _enabledColor : _disabledColor
			},
			_hoverOverlay = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = Color4.White,
				Alpha = 0
			},
			_label = new SpriteText
			{
				Text = _text,
				Font = new FontUsage("", 17, "Bold"),
				Colour = _isEnabled ? Color4.White : new Color4(100, 100, 100, 255),
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre
			}
		};
	}

	protected override bool OnHover(HoverEvent e)
	{
		if (_isEnabled)
			_hoverOverlay.FadeTo(0.15f, 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_hoverOverlay.FadeTo(0, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		if (_isEnabled)
		{
			Clicked?.Invoke();
			_hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
		}

		return true;
	}
}

/// <summary>
/// Lock button for OD/HP sliders.
/// </summary>
public partial class LockButton : CompositeDrawable, IHasTooltip
{
	public event Action<bool>? LockChanged;
	public LocalisableString TooltipText { get; set; }

	private bool _isLocked;
	private Box _background = null!;
	private SpriteText _icon = null!;

	private readonly Color4 _unlockedColor = new(50, 50, 55, 255);
	private readonly Color4 _lockedColor = new(255, 102, 170, 255);

	public bool IsLocked
	{
		get => _isLocked;
		set
		{
			if (_isLocked == value)
				return;
			_isLocked = value;
			UpdateVisuals();
			LockChanged?.Invoke(_isLocked);
		}
	}

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
				Colour = _unlockedColor
			},
			_icon = new SpriteText
			{
				Text = "U",
				Font = new FontUsage("", 12, "Bold"),
				Colour = new Color4(120, 120, 120, 255),
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre
			}
		};
	}

	private void UpdateVisuals()
	{
		_background.FadeColour(_isLocked ? _lockedColor : _unlockedColor, 150);
		_icon.Text = _isLocked ? "L" : "U";
		_icon.FadeColour(_isLocked ? Color4.White : new Color4(120, 120, 120, 255), 150);
	}

	protected override bool OnHover(HoverEvent e)
	{
		_background.FadeColour(_isLocked ? _lockedColor.Lighten(0.1f) : new Color4(65, 65, 70, 255), 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_background.FadeColour(_isLocked ? _lockedColor : _unlockedColor, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		IsLocked = !IsLocked;
		return true;
	}
}
