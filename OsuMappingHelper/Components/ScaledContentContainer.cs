using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;

namespace OsuMappingHelper.Components;

/// <summary>
/// A container that applies a uniform scale transformation to all its children.
/// This allows for global UI scaling without affecting relative margins and paddings.
/// The container maintains a reference design size and scales content based on UIScale.
/// Both the window size AND internal content scale together.
/// </summary>
public partial class ScaledContentContainer : Container
{
    /// <summary>
    /// The reference design width (base resolution the UI was designed for).
    /// </summary>
    public float ReferenceWidth { get; set; } = 620f;

    /// <summary>
    /// The reference design height (base resolution the UI was designed for).
    /// </summary>
    public float ReferenceHeight { get; set; } = 810f;

    /// <summary>
    /// Bindable for the current UI scale factor.
    /// </summary>
    public BindableFloat UIScaleBindable { get; } = new BindableFloat(1.0f)
    {
        MinValue = 0.5f,
        MaxValue = 2.0f,
        Precision = 0.01f
    };

    /// <summary>
    /// Gets or sets the current UI scale factor.
    /// </summary>
    public float UIScale
    {
        get => UIScaleBindable.Value;
        set => UIScaleBindable.Value = value;
    }

    private readonly Container _scaledContent;
    private float _lastAppliedScale = 0f;

    public ScaledContentContainer()
    {
        RelativeSizeAxes = Axes.Both;

        // Create inner container that will be scaled
        // Content is designed for reference dimensions
        _scaledContent = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre
        };
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // Set size based on reference dimensions
        _scaledContent.Size = new Vector2(ReferenceWidth, ReferenceHeight);

        InternalChild = _scaledContent;

        // Subscribe to scale changes to update internal scaling
        UIScaleBindable.BindValueChanged(OnScaleChanged, true);
    }

    protected override void Update()
    {
        base.Update();

        // Apply scale directly from UIScale value
        ApplyScale();
    }

    private void ApplyScale()
    {
        if (_scaledContent == null) return;

        // Use the UIScale value directly for internal scaling
        var targetScale = UIScaleBindable.Value;

        // Only update if scale changed significantly
        if (Math.Abs(targetScale - _lastAppliedScale) > 0.001f)
        {
            _lastAppliedScale = targetScale;
            _scaledContent.Scale = new Vector2(targetScale);
        }
    }

    private void OnScaleChanged(ValueChangedEvent<float> e)
    {
        _lastAppliedScale = 0; // Force recalculation
        ApplyScale();
    }

    /// <summary>
    /// Adds a drawable to the scaled content area.
    /// </summary>
    public new void Add(Drawable drawable)
    {
        _scaledContent.Add(drawable);
    }

    /// <summary>
    /// Removes a drawable from the scaled content area.
    /// </summary>
    public new bool Remove(Drawable drawable, bool disposeImmediately)
    {
        return _scaledContent.Remove(drawable, disposeImmediately);
    }

    /// <summary>
    /// Clears all children from the scaled content area.
    /// </summary>
    public new void Clear(bool disposeChildren = true)
    {
        _scaledContent.Clear(disposeChildren);
    }
}
