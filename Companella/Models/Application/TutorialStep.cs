namespace Companella.Models.Application;

/// <summary>
/// Position hint for the tutorial dialog.
/// </summary>
public enum TutorialDialogPosition
{
    /// <summary>Center of the screen (default for welcome/completion).</summary>
    Center,
    /// <summary>Top of the screen (when describing bottom content).</summary>
    Top,
    /// <summary>Bottom of the screen (when describing top content like map info).</summary>
    Bottom,
    /// <summary>Bottom-right area (when describing left sidebar content).</summary>
    BottomRight
}

/// <summary>
/// Represents a single step in the first-launch tutorial.
/// </summary>
public class TutorialStep
{
    /// <summary>
    /// The title displayed at the top of the tutorial step.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The description/explanation text for this step.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Main tab index to switch to before showing this step.
    /// -1 means no tab switch required.
    /// 0 = Gameplay, 1 = Mapping, 2 = Settings
    /// </summary>
    public int MainTabIndex { get; set; } = -1;

    /// <summary>
    /// Split tab index within the main tab to switch to.
    /// -1 means no split tab switch required.
    /// For Gameplay: 0=Rate Changer, 1=Mods, 2=Session, 3=Skills Analysis, 4=Session Planner
    /// For Mapping: 0=Timing Tools, 1=Bulk Rates, 2=Marathon
    /// </summary>
    public int SplitTabIndex { get; set; } = -1;

    /// <summary>
    /// Where the dialog should be positioned to not block the relevant UI.
    /// </summary>
    public TutorialDialogPosition DialogPosition { get; set; } = TutorialDialogPosition.Center;

    /// <summary>
    /// Whether to show the Quick Setup button on this step.
    /// Used for the final setup step.
    /// </summary>
    public bool ShowQuickSetup { get; set; } = false;

    /// <summary>
    /// Creates a new tutorial step.
    /// </summary>
    public TutorialStep()
    {
    }

    /// <summary>
    /// Creates a new tutorial step with the specified properties.
    /// </summary>
    public TutorialStep(string title, string description, int mainTabIndex = -1, int splitTabIndex = -1, 
        TutorialDialogPosition dialogPosition = TutorialDialogPosition.Center, bool showQuickSetup = false)
    {
        Title = title;
        Description = description;
        MainTabIndex = mainTabIndex;
        SplitTabIndex = splitTabIndex;
        DialogPosition = dialogPosition;
        ShowQuickSetup = showQuickSetup;
    }
}
