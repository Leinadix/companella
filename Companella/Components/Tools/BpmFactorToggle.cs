using Companella.Models.Difficulty;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Tools;

/// <summary>
/// A triple-toggle for selecting BPM factor (0.5x, 1x, 2x).
/// </summary>
public partial class BpmFactorToggle : CompositeDrawable
{
	private BpmFactor _currentFactor = BpmFactor.Normal;
	private ToggleOption _halfOption = null!;
	private ToggleOption _normalOption = null!;
	private ToggleOption _doubleOption = null!;
	private bool _isEnabled = true;

	public event Action<BpmFactor>? FactorChanged;

	/// <summary>
	/// Gets or sets the current BPM factor.
	/// </summary>
	public BpmFactor CurrentFactor
	{
		get => _currentFactor;
		set
		{
			if (_currentFactor == value)
				return;
			_currentFactor = value;
			UpdateSelection();
			FactorChanged?.Invoke(_currentFactor);
		}
	}

	/// <summary>
	/// Gets or sets whether the toggle is enabled.
	/// </summary>
	public bool Enabled
	{
		get => _isEnabled;
		set
		{
			_isEnabled = value;
			if (_halfOption != null)
			{
				_halfOption.Enabled = value;
				_normalOption.Enabled = value;
				_doubleOption.Enabled = value;
			}
		}
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 5;

		InternalChildren = new Drawable[]
		{
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(30, 30, 38, 255)
			},
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.Both,
				Direction = FillDirection.Horizontal,
				Spacing = new Vector2(2, 0),
				Padding = new MarginPadding(2),
				Children = new Drawable[]
				{
					_halfOption = new ToggleOption(BpmFactor.Half)
					{
						RelativeSizeAxes = Axes.Y,
						Width = 40,
						TooltipText = "Halve detected BPM (use if detection doubled the BPM)"
					},
					_normalOption = new ToggleOption(BpmFactor.Normal)
					{
						RelativeSizeAxes = Axes.Y,
						Width = 40,
						TooltipText = "Use detected BPM as-is"
					},
					_doubleOption = new ToggleOption(BpmFactor.Double)
					{
						RelativeSizeAxes = Axes.Y,
						Width = 40,
						TooltipText = "Double detected BPM (use if detection halved the BPM)"
					}
				}
			}
		};

		_halfOption.Selected += OnOptionSelected;
		_normalOption.Selected += OnOptionSelected;
		_doubleOption.Selected += OnOptionSelected;

		// Set initial selection
		UpdateSelection();
	}

	private void OnOptionSelected(BpmFactor factor)
	{
		if (!_isEnabled)
			return;
		CurrentFactor = factor;
	}

	private void UpdateSelection()
	{
		if (_halfOption == null)
			return;

		_halfOption.IsSelected = _currentFactor == BpmFactor.Half;
		_normalOption.IsSelected = _currentFactor == BpmFactor.Normal;
		_doubleOption.IsSelected = _currentFactor == BpmFactor.Double;
	}

	/// <summary>
	/// Individual toggle option button.
	/// </summary>
	private partial class ToggleOption : CompositeDrawable, IHasTooltip
	{
		private readonly BpmFactor _factor;
		private Box _background = null!;
		private Box _hoverOverlay = null!;
		private bool _isSelected;
		private bool _isEnabled = true;

		private readonly Color4 _normalColor = new(50, 50, 60, 255);
		private readonly Color4 _selectedColor = new(255, 102, 170, 255);
		private readonly Color4 _hoverColor = new(70, 70, 85, 255);
		private readonly Color4 _disabledColor = new(40, 40, 48, 255);

		/// <summary>
		/// Tooltip text displayed on hover.
		/// </summary>
		public LocalisableString TooltipText { get; set; }

		public event Action<BpmFactor>? Selected;

		public ToggleOption(BpmFactor factor)
		{
			_factor = factor;
		}

		public bool IsSelected
		{
			get => _isSelected;
			set
			{
				_isSelected = value;
				UpdateAppearance();
			}
		}

		public bool Enabled
		{
			get => _isEnabled;
			set
			{
				_isEnabled = value;
				UpdateAppearance();
			}
		}

		[BackgroundDependencyLoader]
		private void load()
		{
			Masking = true;
			CornerRadius = 3;

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
				new Container
				{
					RelativeSizeAxes = Axes.Both,
					Child = new SpriteText
					{
						Anchor = Anchor.Centre,
						Origin = Anchor.Centre,
						Text = _factor.GetLabel(),
						Font = new FontUsage("", 16, "Bold"),
						Colour = Color4.White
					}
				}
			};

			UpdateAppearance();
		}

		private void UpdateAppearance()
		{
			if (_background == null)
				return;

			if (!_isEnabled)
				_background.FadeColour(_disabledColor, 100);
			else if (_isSelected)
				_background.FadeColour(_selectedColor, 100);
			else
				_background.FadeColour(_normalColor, 100);
		}

		protected override bool OnHover(HoverEvent e)
		{
			if (_isEnabled && !_isSelected)
			{
				_background.FadeColour(_hoverColor, 100);
				_hoverOverlay.FadeTo(0.05f, 100);
			}

			return base.OnHover(e);
		}

		protected override void OnHoverLost(HoverLostEvent e)
		{
			_hoverOverlay.FadeTo(0, 100);
			UpdateAppearance();
			base.OnHoverLost(e);
		}

		protected override bool OnClick(ClickEvent e)
		{
			if (_isEnabled && !_isSelected)
			{
				Selected?.Invoke(_factor);
				_hoverOverlay.FadeTo(0.2f, 50).Then().FadeTo(0, 100);
			}

			return true;
		}
	}
}
