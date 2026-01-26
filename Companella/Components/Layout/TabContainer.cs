using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Layout;

/// <summary>
/// A tab container with animated tab switching, osu!lazer style.
/// </summary>
public partial class TabContainer : CompositeDrawable
{
	private readonly string[] _tabNames;
	private readonly Container[] _tabContents;
	private int _selectedIndex;

	private Container _tabHeaderContainer = null!;
	private Container _contentContainer = null!;
	private Box _indicator = null!;
	private TabButton[] _tabButtons = null!;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _inactiveColor = new(150, 150, 150, 255);

	public event Action<int>? TabChanged;

	public TabContainer(string[] tabNames, Container[] tabContents)
	{
		if (tabNames.Length != tabContents.Length)
			throw new ArgumentException("Tab names and contents must have the same length");

		_tabNames = tabNames;
		_tabContents = tabContents;
		_selectedIndex = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		_tabButtons = new TabButton[_tabNames.Length];

		InternalChildren = new Drawable[]
		{
			new GridContainer
			{
				RelativeSizeAxes = Axes.Both,
				RowDimensions = new[]
				{
					new Dimension(GridSizeMode.Absolute, 40), // Tab header
					new Dimension(GridSizeMode.Relative, 1f) // Content
				},
				Content = new[]
				{
					new Drawable[]
					{
						// Tab header bar
						_tabHeaderContainer = new Container
						{
							RelativeSizeAxes = Axes.Both,
							Children = new Drawable[]
							{
								new Box
								{
									RelativeSizeAxes = Axes.Both,
									Colour = new Color4(30, 30, 35, 255)
								},
								new FillFlowContainer
								{
									RelativeSizeAxes = Axes.Both,
									Direction = FillDirection.Horizontal,
									Children = CreateTabButtons()
								},
								// Animated underline indicator
								_indicator = new Box
								{
									Height = 3,
									Width = 0, // Will be set after layout
									Colour = _accentColor,
									Anchor = Anchor.BottomLeft,
									Origin = Anchor.BottomLeft
								}
							}
						}
					},
					new Drawable[]
					{
						// Content container
						_contentContainer = new Container
						{
							RelativeSizeAxes = Axes.Both
						}
					}
				}
			}
		};

		// Add all tab contents (initially hidden)
		foreach (var content in _tabContents)
		{
			content.RelativeSizeAxes = Axes.Both;
			content.Alpha = 0;
			_contentContainer.Add(content);
		}

		// Show initial tab
		if (_tabContents.Length > 0) _tabContents[0].Alpha = 1;

		// Schedule indicator positioning after layout
		Schedule(() => UpdateIndicator(false));
	}

	private Drawable[] CreateTabButtons()
	{
		var buttons = new Drawable[_tabNames.Length];
		var relativeWidth = 1f / _tabNames.Length;

		for (var i = 0; i < _tabNames.Length; i++)
		{
			var index = i; // Capture for closure
			var button = new TabButton(_tabNames[i], i == _selectedIndex, _accentColor, _inactiveColor)
			{
				RelativeSizeAxes = Axes.Both,
				Width = relativeWidth
			};
			button.Clicked += () => SelectTab(index);
			_tabButtons[i] = button;
			buttons[i] = button;
		}

		return buttons;
	}

	public void SelectTab(int index)
	{
		if (index < 0 || index >= _tabContents.Length || index == _selectedIndex)
			return;

		// Fade out current tab
		_tabContents[_selectedIndex].FadeOut(150, Easing.OutQuad);
		_tabButtons[_selectedIndex].SetSelected(false);

		_selectedIndex = index;

		// Fade in new tab
		_tabContents[_selectedIndex].FadeIn(150, Easing.OutQuad);
		_tabButtons[_selectedIndex].SetSelected(true);

		// Animate indicator
		UpdateIndicator(true);

		TabChanged?.Invoke(_selectedIndex);
	}

	private void UpdateIndicator(bool animate)
	{
		if (_tabButtons == null || _tabButtons.Length == 0)
			return;

		// Calculate relative position based on container width
		var relativeWidth = 1f / _tabButtons.Length;
		var containerWidth = _tabHeaderContainer.DrawWidth;
		var targetWidth = containerWidth * relativeWidth;
		var targetX = _selectedIndex * targetWidth;

		if (animate)
		{
			_indicator.MoveTo(new Vector2(targetX, 0), 200, Easing.OutQuad);
			_indicator.ResizeWidthTo(targetWidth, 200, Easing.OutQuad);
		}
		else
		{
			_indicator.X = targetX;
			_indicator.Width = targetWidth;
		}
	}

	public int SelectedIndex => _selectedIndex;

	/// <summary>
	/// Individual tab button.
	/// </summary>
	private partial class TabButton : CompositeDrawable
	{
		private readonly string _text;
		private bool _isSelected;
		private readonly Color4 _accentColor;
		private readonly Color4 _inactiveColor;

		private SpriteText _label = null!;
		private Box _hoverOverlay = null!;

		public event Action? Clicked;

		public TabButton(string text, bool isSelected, Color4 accentColor, Color4 inactiveColor)
		{
			_text = text;
			_isSelected = isSelected;
			_accentColor = accentColor;
			_inactiveColor = inactiveColor;
		}

		[BackgroundDependencyLoader]
		private void load()
		{
			InternalChildren = new Drawable[]
			{
				_hoverOverlay = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Colour = Color4.White,
					Alpha = 0
				},
				_label = new SpriteText
				{
					Text = _text,
					Font = new FontUsage("", 19, _isSelected ? "Bold" : ""),
					Colour = _isSelected ? _accentColor : _inactiveColor,
					Anchor = Anchor.Centre,
					Origin = Anchor.Centre
				}
			};
		}

		public void SetSelected(bool selected)
		{
			_isSelected = selected;
			_label.FadeColour(_isSelected ? _accentColor : _inactiveColor, 150);
			_label.Font = new FontUsage("", 19, _isSelected ? "Bold" : "");
		}

		protected override bool OnHover(HoverEvent e)
		{
			_hoverOverlay.FadeTo(0.05f, 100);
			if (!_isSelected)
				_label.FadeColour(Color4.White, 100);
			return base.OnHover(e);
		}

		protected override void OnHoverLost(HoverLostEvent e)
		{
			_hoverOverlay.FadeTo(0, 100);
			if (!_isSelected)
				_label.FadeColour(_inactiveColor, 100);
			base.OnHoverLost(e);
		}

		protected override bool OnClick(ClickEvent e)
		{
			Clicked?.Invoke();
			return true;
		}
	}
}
