using Companella.Models.Application;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// An interactive tutorial overlay that guides new users through the application.
/// Displays step-by-step instructions as a centered dialog.
/// </summary>
public partial class TutorialOverlay : CompositeDrawable
{
	private Container _dialogContainer = null!;
	private SpriteText _titleText = null!;
	private TextFlowContainer _descriptionText = null!;
	private SpriteText _progressText = null!;
	private TutorialButton _previousButton = null!;
	private TutorialButton _nextButton = null!;
	private TutorialButton _skipButton = null!;
	private TutorialButton _quickSetupButton = null!;
	private FillFlowContainer _buttonContainer = null!;

	private TutorialService _tutorialService = null!;

	/// <summary>
	/// Event raised when the tutorial is completed or skipped.
	/// </summary>
	public event Action? TutorialCompleted;

	/// <summary>
	/// Event raised when a main tab switch is required.
	/// Parameter is the main tab index (0=Gameplay, 1=Mapping, 2=Settings).
	/// </summary>
	public event Action<int>? MainTabSwitchRequested;

	/// <summary>
	/// Event raised when a split tab switch is required within a main tab.
	/// Parameters are (mainTabIndex, splitTabIndex).
	/// </summary>
	public event Action<int, int>? SplitTabSwitchRequested;

	/// <summary>
	/// Event raised when the Quick Setup button is clicked.
	/// Triggers: Index Maps, Import Scores, Find Missing Replays.
	/// </summary>
	public event Action? QuickSetupRequested;

	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _dialogBgColor = new(25, 25, 30, 250);
	private readonly Color4 _dialogBorderColor = new(80, 80, 90, 255);

	public TutorialOverlay()
	{
		RelativeSizeAxes = Axes.Both;
		Alpha = 0;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		_tutorialService = new TutorialService();
		_tutorialService.MainTabSwitchRequested += OnMainTabSwitchRequested;
		_tutorialService.SplitTabSwitchRequested += OnSplitTabSwitchRequested;

		InternalChildren = new Drawable[]
		{
			// Semi-transparent background overlay
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(0, 0, 0, 180)
			},
			// Dialog container
			_dialogContainer = new Container
			{
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Size = new Vector2(520, 140),
				Masking = true,
				CornerRadius = 12,
				Children = new Drawable[]
				{
					// Shadow
					new Box
					{
						RelativeSizeAxes = Axes.Both,
						Colour = new Color4(0, 0, 0, 100),
						Margin = new MarginPadding(2)
					},
					// Main background
					new Box
					{
						RelativeSizeAxes = Axes.Both,
						Colour = _dialogBgColor
					},
					// Border
					new Container
					{
						RelativeSizeAxes = Axes.Both,
						Masking = true,
						CornerRadius = 12,
						BorderColour = _dialogBorderColor,
						BorderThickness = 1.5f,
						Child = new Box
						{
							RelativeSizeAxes = Axes.Both,
							Alpha = 0,
							AlwaysPresent = true
						}
					},
					// Accent stripe at top
					new Box
					{
						RelativeSizeAxes = Axes.X,
						Height = 4,
						Colour = _accentColor
					},
					// Content container
					new Container
					{
						RelativeSizeAxes = Axes.Both,
						Padding = new MarginPadding(24),
						Children = new Drawable[]
						{
							// Title and progress
							new FillFlowContainer
							{
								RelativeSizeAxes = Axes.X,
								AutoSizeAxes = Axes.Y,
								Direction = FillDirection.Vertical,
								Spacing = new Vector2(0, 8),
								Children = new Drawable[]
								{
									// Progress indicator
									_progressText = new SpriteText
									{
										Anchor = Anchor.TopCentre,
										Origin = Anchor.TopCentre,
										Text = "Step 1 of 12",
										Font = new FontUsage("", 13),
										Colour = new Color4(150, 150, 150, 255)
									},
									// Title
									_titleText = new SpriteText
									{
										Anchor = Anchor.TopCentre,
										Origin = Anchor.TopCentre,
										Text = "Welcome to Companella!",
										Font = new FontUsage("", 24, "Bold"),
										Colour = _accentColor
									}
								}
							},
							// Description text
							_descriptionText = new TextFlowContainer(s =>
							{
								s.Font = new FontUsage("", 15);
								s.Colour = new Color4(220, 220, 220, 255);
							})
							{
								Anchor = Anchor.TopCentre,
								Origin = Anchor.TopCentre,
								RelativeSizeAxes = Axes.X,
								AutoSizeAxes = Axes.Y,
								Y = 70,
								TextAnchor = Anchor.TopLeft,
								Text = "Welcome!"
							},
							// Button container at bottom
							_buttonContainer = new FillFlowContainer
							{
								Anchor = Anchor.BottomCentre,
								Origin = Anchor.BottomCentre,
								AutoSizeAxes = Axes.Both,
								Direction = FillDirection.Horizontal,
								Spacing = new Vector2(10, 0),
								Children = new Drawable[]
								{
									_skipButton = new TutorialButton("Skip")
									{
										Size = new Vector2(90, 38),
										BackgroundColour = new Color4(70, 70, 75, 255)
									},
									_previousButton = new TutorialButton("Previous")
									{
										Size = new Vector2(100, 38),
										BackgroundColour = new Color4(80, 80, 90, 255)
									},
									_quickSetupButton = new TutorialButton("Quick Setup")
									{
										Size = new Vector2(120, 38),
										BackgroundColour = new Color4(80, 180, 80, 255),
										Alpha = 0
									},
									_nextButton = new TutorialButton("Next")
									{
										Size = new Vector2(100, 38),
										BackgroundColour = _accentColor
									}
								}
							}
						}
					}
				}
			}
		};

		_skipButton.Clicked += OnSkipClicked;
		_previousButton.Clicked += OnPreviousClicked;
		_nextButton.Clicked += OnNextClicked;
		_quickSetupButton.Clicked += OnQuickSetupClicked;
	}

	/// <summary>
	/// Shows the tutorial overlay, starting from the first step.
	/// </summary>
	public new void Show()
	{
		_tutorialService.Reset();
		UpdateCurrentStep();

		// Show with animation
		this.FadeIn(300, Easing.OutQuint);
		_dialogContainer.ScaleTo(0.9f).ScaleTo(1f, 300, Easing.OutQuint);
	}

	/// <summary>
	/// Hides the tutorial overlay.
	/// </summary>
	public new void Hide()
	{
		this.FadeOut(200, Easing.OutQuint);
	}

	private void UpdateCurrentStep()
	{
		var step = _tutorialService.CurrentStep;
		if (step == null)
			return;

		// Update progress text
		_progressText.Text = $"Step {_tutorialService.CurrentStepIndex + 1} of {_tutorialService.TotalSteps}";

		// Update title
		_titleText.Text = step.Title;

		// Update description
		_descriptionText.Text = step.Description;

		// Update button states
		_previousButton.Alpha = _tutorialService.HasPreviousStep ? 1f : 0.5f;
		_previousButton.Enabled = _tutorialService.HasPreviousStep;

		// Show/hide Quick Setup button based on step
		if (step.ShowQuickSetup)
		{
			_quickSetupButton.FadeIn(200);
			_nextButton.Text = "Skip Setup";
			_skipButton.Alpha = 0; // Hide skip button on final step
		}
		else
		{
			_quickSetupButton.FadeOut(200);
			_nextButton.Text = _tutorialService.IsLastStep ? "Finish" : "Next";
			_skipButton.Alpha = 1;
		}

		// Adjust dialog size based on content length
		var estimatedHeight = 300f;
		if (step.Description.Length > 200)
			estimatedHeight = 360f;
		if (step.Description.Length > 350)
			estimatedHeight = 420f;
		if (step.ShowQuickSetup)
			estimatedHeight = 400f; // Extra space for quick setup step

		_dialogContainer.ResizeHeightTo(estimatedHeight, 200, Easing.OutQuint);

		// Position the dialog based on the step's position hint
		PositionDialog(step.DialogPosition);
	}

	private void PositionDialog(TutorialDialogPosition position)
	{
		// Calculate target position based on the hint
		// The dialog uses Anchor.Centre and Origin.Centre, so we offset from center
		Vector2 targetPosition;

		switch (position)
		{
			case TutorialDialogPosition.Top:
				// Move dialog to upper area (offset from center upward)
				targetPosition = new Vector2(0, -180);
				break;

			case TutorialDialogPosition.Bottom:
				// Move dialog to lower area (offset from center downward)
				targetPosition = new Vector2(0, 150);
				break;

			case TutorialDialogPosition.BottomRight:
				// Move dialog to bottom-right area to not block left sidebar
				targetPosition = new Vector2(80, 120);
				break;

			case TutorialDialogPosition.Center:
			default:
				// Center position (no offset)
				targetPosition = Vector2.Zero;
				break;
		}

		// Animate to the new position
		_dialogContainer.MoveTo(targetPosition, 250, Easing.OutQuint);
	}

	private void OnSkipClicked()
	{
		Hide();
		TutorialCompleted?.Invoke();
	}

	private void OnPreviousClicked()
	{
		if (_tutorialService.PreviousStep()) UpdateCurrentStep();
	}

	private void OnNextClicked()
	{
		if (_tutorialService.IsLastStep)
		{
			// Tutorial complete (user chose to skip setup)
			Hide();
			TutorialCompleted?.Invoke();
		}
		else if (_tutorialService.NextStep())
		{
			UpdateCurrentStep();
		}
	}

	private void OnQuickSetupClicked()
	{
		// Trigger quick setup and complete tutorial
		Hide();
		QuickSetupRequested?.Invoke();
		TutorialCompleted?.Invoke();
	}

	private void OnMainTabSwitchRequested(int tabIndex)
	{
		MainTabSwitchRequested?.Invoke(tabIndex);
	}

	private void OnSplitTabSwitchRequested(int mainTabIndex, int splitTabIndex)
	{
		SplitTabSwitchRequested?.Invoke(mainTabIndex, splitTabIndex);
	}

	protected override bool OnClick(ClickEvent e)
	{
		// Prevent clicks from passing through
		return true;
	}
}

/// <summary>
/// A styled button for the tutorial overlay.
/// </summary>
public partial class TutorialButton : CompositeDrawable
{
	private Box _background = null!;
	private Box _hoverOverlay = null!;
	private SpriteText _textSprite = null!;
	private string _text;

	public Color4 BackgroundColour { get; set; } = new(255, 102, 170, 255);

	public bool Enabled { get; set; } = true;

	public string Text
	{
		get => _text;
		set
		{
			_text = value;
			if (_textSprite != null)
				_textSprite.Text = value;
		}
	}

	public event Action? Clicked;

	public TutorialButton(string text)
	{
		_text = text;
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		CornerRadius = 6;

		InternalChildren = new Drawable[]
		{
			// Shadow
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = new Color4(0, 0, 0, 80),
				Margin = new MarginPadding(1)
			},
			// Main background
			_background = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = BackgroundColour
			},
			// Border
			new Container
			{
				RelativeSizeAxes = Axes.Both,
				Masking = true,
				CornerRadius = 6,
				BorderColour = new Color4(255, 255, 255, 30),
				BorderThickness = 1f,
				Child = new Box
				{
					RelativeSizeAxes = Axes.Both,
					Alpha = 0,
					AlwaysPresent = true
				}
			},
			_hoverOverlay = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = Color4.White,
				Alpha = 0
			},
			_textSprite = new SpriteText
			{
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Text = _text,
				Font = new FontUsage("", 15, "Bold"),
				Colour = Color4.White
			}
		};
	}

	protected override bool OnHover(HoverEvent e)
	{
		if (Enabled)
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
		if (!Enabled)
			return true;

		Clicked?.Invoke();
		_hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
		return true;
	}
}
