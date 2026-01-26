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
/// A split container with a sidebar navigation on the left and content on the right.
/// Designed for organizing multiple features within a tab.
/// </summary>
public partial class SplitTabContainer : CompositeDrawable
{
	private readonly SplitTabItem[] _items;
	private int _selectedIndex;

	private Container _sidebarContainer = null!;
	private Container _contentContainer = null!;
	private Box _selectionIndicator = null!;
	private SidebarButton[] _sidebarButtons = null!;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _inactiveColor = new(150, 150, 150, 255);
	private readonly Color4 _sidebarBgColor = new(25, 25, 30, 255);
	private readonly Color4 _contentBgColor = new(20, 20, 25, 255);

	private const float _sidebarWidth = 140f;
	private const float _buttonHeight = 36f;

	/// <summary>
	/// Event raised when the selected item changes.
	/// </summary>
	public event Action<int>? SelectionChanged;

	public SplitTabContainer(SplitTabItem[] items)
	{
		if (items == null || items.Length == 0)
			throw new ArgumentException("At least one item is required");

		_items = items;
		_selectedIndex = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		_sidebarButtons = new SidebarButton[_items.Length];

		InternalChildren = new Drawable[]
		{
			new GridContainer
			{
				RelativeSizeAxes = Axes.Both,
				ColumnDimensions = new[]
				{
					new Dimension(GridSizeMode.Absolute, _sidebarWidth),
					new Dimension()
				},
				Content = new[]
				{
					new Drawable[]
					{
						// Sidebar
						_sidebarContainer = new Container
						{
							RelativeSizeAxes = Axes.Both,
							Children = new Drawable[]
							{
								// Background
								new Box
								{
									RelativeSizeAxes = Axes.Both,
									Colour = _sidebarBgColor
								},
								// Right border
								new Box
								{
									Width = 1,
									RelativeSizeAxes = Axes.Y,
									Anchor = Anchor.TopRight,
									Origin = Anchor.TopRight,
									Colour = new Color4(50, 50, 55, 255)
								},
								// Selection indicator
								_selectionIndicator = new Box
								{
									Width = 3,
									Height = _buttonHeight,
									Colour = _accentColor,
									Anchor = Anchor.TopLeft,
									Origin = Anchor.TopLeft
								},
								// Scrollable button list
								new BasicScrollContainer
								{
									RelativeSizeAxes = Axes.Both,
									ClampExtension = 10,
									Child = new FillFlowContainer
									{
										RelativeSizeAxes = Axes.X,
										AutoSizeAxes = Axes.Y,
										Direction = FillDirection.Vertical,
										Padding = new MarginPadding { Top = 5, Bottom = 5 },
										Children = CreateSidebarButtons()
									}
								}
							}
						},
						// Content area
						new Container
						{
							RelativeSizeAxes = Axes.Both,
							Children = new Drawable[]
							{
								// Background
								new Box
								{
									RelativeSizeAxes = Axes.Both,
									Colour = _contentBgColor
								},
								// Scrollable content
								new BasicScrollContainer
								{
									RelativeSizeAxes = Axes.Both,
									ClampExtension = 20,
									ScrollbarVisible = true,
									Child = _contentContainer = new Container
									{
										RelativeSizeAxes = Axes.X,
										AutoSizeAxes = Axes.Y,
										Padding = new MarginPadding(10)
									}
								}
							}
						}
					}
				}
			}
		};

		// Add all content containers (initially hidden)
		foreach (var item in _items)
		{
			// Ensure the content respects relative X sizing
			item.Content.RelativeSizeAxes = Axes.X;

			var wrapper = new Container
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Alpha = 0,
				Child = item.Content
			};
			_contentContainer.Add(wrapper);
		}

		// Show initial content
		if (_contentContainer.Children.Count > 0) _contentContainer.Children[0].Alpha = 1;

		// Position indicator after layout
		Schedule(() => UpdateIndicator(false));
	}

	private Drawable[] CreateSidebarButtons()
	{
		var buttons = new Drawable[_items.Length];

		for (var i = 0; i < _items.Length; i++)
		{
			var index = i; // Capture for closure
			var button = new SidebarButton(_items[i].Name, i == _selectedIndex, _accentColor, _inactiveColor)
			{
				RelativeSizeAxes = Axes.X,
				Height = _buttonHeight
			};
			button.Clicked += () => SelectItem(index);
			_sidebarButtons[i] = button;
			buttons[i] = button;
		}

		return buttons;
	}

	/// <summary>
	/// Selects an item by index.
	/// </summary>
	public void SelectItem(int index)
	{
		if (index < 0 || index >= _items.Length || index == _selectedIndex)
			return;

		// Fade out current content
		_contentContainer.Children[_selectedIndex].FadeOut(150, Easing.OutQuad);
		_sidebarButtons[_selectedIndex].SetSelected(false);

		_selectedIndex = index;

		// Fade in new content
		_contentContainer.Children[_selectedIndex].FadeIn(150, Easing.OutQuad);
		_sidebarButtons[_selectedIndex].SetSelected(true);

		// Animate indicator
		UpdateIndicator(true);

		SelectionChanged?.Invoke(_selectedIndex);
	}

	private void UpdateIndicator(bool animate)
	{
		if (_sidebarButtons == null || _sidebarButtons.Length == 0)
			return;

		var targetY = 5 + _selectedIndex * _buttonHeight; // 5 is the top padding

		if (animate)
			_selectionIndicator.MoveTo(new Vector2(0, targetY), 200, Easing.OutQuad);
		else
			_selectionIndicator.Y = targetY;
	}

	/// <summary>
	/// Gets the currently selected index.
	/// </summary>
	public int SelectedIndex => _selectedIndex;

	/// <summary>
	/// Sidebar navigation button.
	/// </summary>
	private partial class SidebarButton : CompositeDrawable
	{
		private readonly string _text;
		private bool _isSelected;
		private readonly Color4 _accentColor;
		private readonly Color4 _inactiveColor;

		private SpriteText _label = null!;
		private Box _hoverOverlay = null!;

		public event Action? Clicked;

		public SidebarButton(string text, bool isSelected, Color4 accentColor, Color4 inactiveColor)
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
					Font = new FontUsage("", 17, _isSelected ? "Bold" : ""),
					Colour = _isSelected ? _accentColor : _inactiveColor,
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft,
					Padding = new MarginPadding { Left = 12 }
				}
			};
		}

		public void SetSelected(bool selected)
		{
			_isSelected = selected;
			_label.FadeColour(_isSelected ? _accentColor : _inactiveColor, 150);
			_label.Font = new FontUsage("", 17, _isSelected ? "Bold" : "");
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

/// <summary>
/// Represents an item in a SplitTabContainer.
/// </summary>
public class SplitTabItem
{
	/// <summary>
	/// The display name shown in the sidebar.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// The content to display when this item is selected.
	/// </summary>
	public Drawable Content { get; }

	public SplitTabItem(string name, Drawable content)
	{
		Name = name;
		Content = content;
	}
}
