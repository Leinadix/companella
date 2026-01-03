using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace OsuMappingHelper.Components;

/// <summary>
/// A text component that automatically scrolls (marquee effect) when the text overflows its container.
/// When text fits within the container, it displays normally without animation.
/// </summary>
public partial class MarqueeText : CompositeDrawable
{
    private SpriteText _innerText = null!;
    private Container _textContainer = null!;
    
    private bool _isAnimating;
    private float _scrollOffset;
    private MarqueeState _state = MarqueeState.Idle;
    private double _stateTime;
    private float _lastDrawWidth;
    
    // Animation configuration
    private const float SCROLL_SPEED = 50f; // Pixels per second
    private const double PAUSE_DURATION = 2000; // Milliseconds to pause at each end
    
    private string _text = string.Empty;
    private FontUsage _font = new FontUsage("", 15);
    private Color4 _textColour = Color4.White;
    
    /// <summary>
    /// The text to display.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            
            if (_innerText != null)
            {
                _innerText.Text = value;
                ResetAnimation();
            }
        }
    }
    
    /// <summary>
    /// The font to use for rendering.
    /// </summary>
    public FontUsage Font
    {
        get => _font;
        set
        {
            _font = value;
            if (_innerText != null)
            {
                _innerText.Font = value;
                ResetAnimation();
            }
        }
    }
    
    /// <summary>
    /// The colour of the text. Use this instead of Colour to set text color.
    /// </summary>
    public new Color4 Colour
    {
        get => _textColour;
        set
        {
            _textColour = value;
            if (_innerText != null)
                _innerText.Colour = value;
        }
    }
    
    private enum MarqueeState
    {
        Idle,           // Text fits, no animation needed
        PauseStart,     // Pausing at start position
        ScrollLeft,     // Scrolling left to show end of text
        PauseEnd,       // Pausing at end position
        ScrollRight     // Scrolling right back to start
    }
    
    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        
        InternalChildren = new Drawable[]
        {
            _textContainer = new Container
            {
                AutoSizeAxes = Axes.Both,
                Child = _innerText = new SpriteText
                {
                    Text = _text,
                    Font = _font,
                    Colour = _textColour
                }
            }
        };
    }
    
    protected override void LoadComplete()
    {
        base.LoadComplete();
        
        // Schedule to check overflow after layout
        Schedule(CheckOverflow);
    }
    
    protected override void Update()
    {
        base.Update();
        
        // Check if size changed and re-evaluate overflow
        if (Math.Abs(_lastDrawWidth - DrawWidth) > 0.1f)
        {
            _lastDrawWidth = DrawWidth;
            CheckOverflow();
        }
        
        if (_state == MarqueeState.Idle)
            return;
        
        var elapsed = Time.Elapsed;
        _stateTime += elapsed;
        
        switch (_state)
        {
            case MarqueeState.PauseStart:
                if (_stateTime >= PAUSE_DURATION)
                {
                    _state = MarqueeState.ScrollLeft;
                    _stateTime = 0;
                }
                break;
                
            case MarqueeState.ScrollLeft:
                {
                    var maxScroll = GetMaxScroll();
                    _scrollOffset += (float)(SCROLL_SPEED * elapsed / 1000.0);
                    
                    if (_scrollOffset >= maxScroll)
                    {
                        _scrollOffset = maxScroll;
                        _state = MarqueeState.PauseEnd;
                        _stateTime = 0;
                    }
                    
                    _textContainer.X = -_scrollOffset;
                }
                break;
                
            case MarqueeState.PauseEnd:
                if (_stateTime >= PAUSE_DURATION)
                {
                    _state = MarqueeState.ScrollRight;
                    _stateTime = 0;
                }
                break;
                
            case MarqueeState.ScrollRight:
                {
                    _scrollOffset -= (float)(SCROLL_SPEED * elapsed / 1000.0);
                    
                    if (_scrollOffset <= 0)
                    {
                        _scrollOffset = 0;
                        _state = MarqueeState.PauseStart;
                        _stateTime = 0;
                    }
                    
                    _textContainer.X = -_scrollOffset;
                }
                break;
        }
    }
    
    private float GetMaxScroll()
    {
        var textWidth = _innerText.DrawWidth;
        var containerWidth = DrawWidth;
        return Math.Max(0, textWidth - containerWidth);
    }
    
    private void CheckOverflow()
    {
        if (_innerText == null) return;
        
        var textWidth = _innerText.DrawWidth;
        var containerWidth = DrawWidth;
        
        if (textWidth > containerWidth && containerWidth > 0)
        {
            // Text overflows, start animation
            if (_state == MarqueeState.Idle)
            {
                _state = MarqueeState.PauseStart;
                _stateTime = 0;
                _scrollOffset = 0;
                _isAnimating = true;
            }
        }
        else
        {
            // Text fits, reset to idle
            _state = MarqueeState.Idle;
            _scrollOffset = 0;
            if (_textContainer != null)
                _textContainer.X = 0;
            _isAnimating = false;
        }
    }
    
    private void ResetAnimation()
    {
        _state = MarqueeState.Idle;
        _stateTime = 0;
        _scrollOffset = 0;
        
        if (_textContainer != null)
            _textContainer.X = 0;
        
        // Re-check overflow on next frame
        Schedule(CheckOverflow);
    }
    
    /// <summary>
    /// Gets whether the text is currently animating (overflowing).
    /// </summary>
    public bool IsAnimating => _isAnimating;
}
