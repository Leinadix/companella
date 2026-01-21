using Companella.Models.Application;

namespace Companella.Services.Common;

/// <summary>
/// Service that manages the first-launch tutorial state and provides tutorial step definitions.
/// </summary>
public class TutorialService
{
    private readonly List<TutorialStep> _tutorialSteps;
    private int _currentStepIndex;

    /// <summary>
    /// Gets the current step index.
    /// </summary>
    public int CurrentStepIndex => _currentStepIndex;

    /// <summary>
    /// Gets the total number of steps.
    /// </summary>
    public int TotalSteps => _tutorialSteps.Count;

    /// <summary>
    /// Gets the current tutorial step.
    /// </summary>
    public TutorialStep? CurrentStep => 
        _currentStepIndex >= 0 && _currentStepIndex < _tutorialSteps.Count 
            ? _tutorialSteps[_currentStepIndex] 
            : null;

    /// <summary>
    /// Whether there is a next step available.
    /// </summary>
    public bool HasNextStep => _currentStepIndex < _tutorialSteps.Count - 1;

    /// <summary>
    /// Whether there is a previous step available.
    /// </summary>
    public bool HasPreviousStep => _currentStepIndex > 0;

    /// <summary>
    /// Whether the tutorial is on the last step.
    /// </summary>
    public bool IsLastStep => _currentStepIndex == _tutorialSteps.Count - 1;

    /// <summary>
    /// Event raised when a step change requires switching the main tab.
    /// Parameter is the main tab index (0=Gameplay, 1=Mapping, 2=Settings).
    /// </summary>
    public event Action<int>? MainTabSwitchRequested;

    /// <summary>
    /// Event raised when a step change requires switching the split tab within a main tab.
    /// Parameters are (mainTabIndex, splitTabIndex).
    /// </summary>
    public event Action<int, int>? SplitTabSwitchRequested;

    /// <summary>
    /// Creates a new TutorialService with predefined tutorial steps.
    /// </summary>
    public TutorialService()
    {
        _tutorialSteps = CreateTutorialSteps();
        _currentStepIndex = 0;
    }

    /// <summary>
    /// Gets all tutorial steps.
    /// </summary>
    public IReadOnlyList<TutorialStep> GetTutorialSteps() => _tutorialSteps.AsReadOnly();

    /// <summary>
    /// Advances to the next tutorial step.
    /// </summary>
    /// <returns>True if advanced successfully, false if already at the end.</returns>
    public bool NextStep()
    {
        if (!HasNextStep)
            return false;

        _currentStepIndex++;
        RequestTabSwitchForCurrentStep();
        return true;
    }

    /// <summary>
    /// Goes back to the previous tutorial step.
    /// </summary>
    /// <returns>True if went back successfully, false if already at the beginning.</returns>
    public bool PreviousStep()
    {
        if (!HasPreviousStep)
            return false;

        _currentStepIndex--;
        RequestTabSwitchForCurrentStep();
        return true;
    }

    /// <summary>
    /// Resets the tutorial to the first step.
    /// </summary>
    public void Reset()
    {
        _currentStepIndex = 0;
    }

    /// <summary>
    /// Requests tab switches for the current step if needed.
    /// </summary>
    private void RequestTabSwitchForCurrentStep()
    {
        var step = CurrentStep;
        if (step == null) return;

        // Request main tab switch if needed
        if (step.MainTabIndex >= 0)
        {
            MainTabSwitchRequested?.Invoke(step.MainTabIndex);
        }

        // Request split tab switch if needed
        if (step.SplitTabIndex >= 0 && step.MainTabIndex >= 0)
        {
            SplitTabSwitchRequested?.Invoke(step.MainTabIndex, step.SplitTabIndex);
        }
    }

    /// <summary>
    /// Creates the predefined list of tutorial steps.
    /// </summary>
    private static List<TutorialStep> CreateTutorialSteps()
    {
        return new List<TutorialStep>
        {
            // Step 1: Welcome (centered)
            new TutorialStep(
                title: "Welcome to Companella!",
                description: "This quick tutorial will show you around the main features of the application. " +
                             "Companella is a companion tool for osu!mania that helps you improve your gameplay " +
                             "with rate changing, session tracking, skills analysis, and more.\n\n" +
                             "Click 'Next' to continue, or 'Skip' to close this tutorial.",
                dialogPosition: TutorialDialogPosition.Center
            ),

            // Step 2: Map Info Display
            new TutorialStep(
                title: "Map Information",
                description: "At the top of the screen, you can see details about the beatmap you're currently " +
                             "viewing in osu!. This includes the song title, artist, difficulty name, and MSD " +
                             "(MinaCalc) difficulty ratings broken down by skillsets like Stream, Jumpstream, " +
                             "Handstream, and more.\n\n" +
                             "Make sure osu! is running to see your current map here!",
                mainTabIndex: 0,
                splitTabIndex: 0,
                dialogPosition: TutorialDialogPosition.Bottom
            ),

            // Step 3: Rate Changer
            new TutorialStep(
                title: "Rate Changer",
                description: "The Rate Changer creates speed-modified versions of beatmaps. " +
                             "Select a rate (like 1.1x or 0.9x) and click 'Apply' to create a new difficulty.\n\n" +
                             "The naming format (e.g. '[Song] 1.1x') can be customized using the format field. " +
                             "Use [[name]] for the original difficulty name and [[rate]] for the rate value.\n\n" +
                             "Toggle 'Pitch Adjust' to control whether audio pitch changes with speed.",
                mainTabIndex: 0,
                splitTabIndex: 0,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 4: Mod Tools
            new TutorialStep(
                title: "Mod Tools",
                description: "The Mods panel applies transformations to beatmaps for practice purposes. " +
                             "Available mods include note randomization, column swaps, and other pattern modifications.\n\n" +
                             "Each mod creates a new difficulty file - your original map stays unchanged. " +
                             "Check the mod descriptions to see what each one does before applying.",
                mainTabIndex: 0,
                splitTabIndex: 1,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 5: Session Tracking
            new TutorialStep(
                title: "Session Tracking",
                description: "Companella tracks your play sessions when osu! is running. " +
                             "You can see your recent plays, accuracy, and practice duration.\n\n" +
                             "Click 'Start Session' to begin tracking. Your plays will be recorded " +
                             "and you can review your progress over time in the session history!",
                mainTabIndex: 0,
                splitTabIndex: 2,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 6: Skills Analysis
            new TutorialStep(
                title: "Skills Analysis",
                description: "The Skills Analysis panel shows your performance trends over time. " +
                             "It tracks improvement across different skillsets and recommends " +
                             "maps that match your current skill level.\n\n" +
                             "You need to 'Index Maps' first to scan your beatmaps before " +
                             "recommendations can work. This only needs to be done once!",
                mainTabIndex: 0,
                splitTabIndex: 3,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 7: Session Planner
            new TutorialStep(
                title: "Session Planner",
                description: "The Session Planner helps organize your practice sessions. " +
                             "Based on your skill trends, it suggests what to focus on.\n\n" +
                             "This works best after you've tracked some sessions and indexed your maps!",
                mainTabIndex: 0,
                splitTabIndex: 4,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 8: Timing Tools
            new TutorialStep(
                title: "Mapping Tools - Timing",
                description: "The Timing Tools help fix beatmap timing issues:\n\n" +
                             "- BPM Analysis: Attempts to detect BPM (experimental - may not be accurate for complex songs)\n" +
                             "- Normalize SV: Cleans up scroll velocity changes to a consistent speed\n" +
                             "- Offset: Shifts all notes by a fixed amount in milliseconds\n\n" +
                             "Note: Always backup your maps before using these tools!",
                mainTabIndex: 1,
                splitTabIndex: 0,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 9: Bulk Rate Changer
            new TutorialStep(
                title: "Bulk Rate Changer",
                description: "Creates multiple rate versions of a map at once. Instead of making rates " +
                             "one by one, generate a whole range (e.g., 0.8x to 1.3x) with one click.\n\n" +
                             "Configure the naming format to tag your difficulties. Use [[name]] for the " +
                             "original name and [[rate]] for the rate value (e.g. '[[name]] [[rate]]x').\n\n" +
                             "You can save custom presets for your preferred rate ranges!",
                mainTabIndex: 1,
                splitTabIndex: 1,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 10: Marathon Creator
            new TutorialStep(
                title: "Marathon Creator",
                description: "Combines multiple maps into one long marathon map.\n\n" +
                             "How to use:\n" +
                             "1. Select a map in osu! and click 'Add Song' to add it to the list\n" +
                             "2. Repeat for each map you want to include\n" +
                             "3. Arrange the order and set break times between maps\n" +
                             "4. Click 'Create Marathon' to generate the combined map\n\n" +
                             "Great for stamina training or creating themed compilations!",
                mainTabIndex: 1,
                splitTabIndex: 2,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 11: Settings - Overlay Mode
            new TutorialStep(
                title: "Settings - Overlay Mode",
                description: "Overlay Mode attaches Companella to your osu! window. When enabled, " +
                             "the app will follow osu! and position itself beside the game window.\n\n" +
                             "You can adjust the overlay offset to fine-tune the positioning. " +
                             "Use the keybind (default: Alt+Q) to quickly toggle visibility!",
                mainTabIndex: 2,
                splitTabIndex: -1,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 12: Settings - Keybinds
            new TutorialStep(
                title: "Settings - Keybinds",
                description: "Configure keyboard shortcuts for quick access to features.\n\n" +
                             "The toggle visibility keybind lets you show/hide Companella instantly " +
                             "without alt-tabbing. Set this to a key combination that doesn't " +
                             "conflict with osu! or other applications.",
                mainTabIndex: 2,
                splitTabIndex: -1,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 13: Settings - UI Scale & Other
            new TutorialStep(
                title: "Settings - Display & Privacy",
                description: "Additional settings to customize your experience:\n\n" +
                             "- UI Scale: Adjust the interface size (useful for high-DPI displays)\n" +
                             "- Metadata: Choose between romanized or unicode song titles\n" +
                             "- MinaCalc Version: Select which calculator version to use for MSD\n" +
                             "- Analytics: Optional anonymous usage data to help improve Companella",
                mainTabIndex: 2,
                splitTabIndex: -1,
                dialogPosition: TutorialDialogPosition.Top
            ),

            // Step 14: Quick Setup & Completion
            new TutorialStep(
                title: "Quick Setup",
                description: "Before you start, let's set up session tracking!\n\n" +
                             "IMPORTANT: Make sure osu! is running before clicking Quick Setup.\n\n" +
                             "Click 'Quick Setup' below to automatically:\n" +
                             "- Index your beatmaps (for recommendations)\n" +
                             "- Import your existing scores\n" +
                             "- Find and link missing replay files\n\n" +
                             "This may take a few minutes depending on your library size. " +
                             "You can also do this later from the Tutorial tab.",
                mainTabIndex: 0,
                splitTabIndex: 3,
                dialogPosition: TutorialDialogPosition.Center,
                showQuickSetup: true
            )
        };
    }
}
