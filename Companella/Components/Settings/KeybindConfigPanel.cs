using System.Linq;
using System.Threading.Tasks;
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
using osuTK.Input;
using Companella.Services.Common;
using Companella.Services.Platform;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for configuring the toggle visibility keybind.
/// </summary>
public partial class KeybindConfigPanel : CompositeDrawable
{
    [Resolved]
    private UserSettingsService SettingsService { get; set; } = null!;

    [Resolved]
    private GlobalHotkeyService HotkeyService { get; set; } = null!;

    private SpriteText _labelText = null!;
    private KeybindButton _keybindButton = null!;
    private bool _isRecording;
    private List<Key> _pressedKeys = new();

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _normalColor = new Color4(60, 60, 70, 255);
    private readonly Color4 _hoverColor = new Color4(80, 80, 90, 255);
    private readonly Color4 _recordingColor = new Color4(255, 150, 150, 255);

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

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
                    _labelText = new SpriteText
                    {
                        Text = "Toggle Visibility Keybind:",
                        Font = new FontUsage("", 16),
                        Colour = new Color4(200, 200, 200, 255)
                    },
                    _keybindButton = new KeybindButton
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 32,
                        Masking = true,
                        CornerRadius = 4,
                        Action = OnKeybindButtonClicked,
                        NormalColor = _normalColor,
                        HoverColor = _hoverColor,
                        RecordingColor = _recordingColor,
                        KeybindText = FormatKeybind(SettingsService.Settings.ToggleVisibilityKeybind),
                        TooltipText = "Click to set a new hotkey for toggling overlay visibility",
                        OnKeyDownCallback = OnKeybindKeyDown,
                        OnKeyUpCallback = OnKeybindKeyUp
                    }
                }
            }
        };
    }

    private bool OnKeybindKeyDown(KeyDownEvent e)
    {
        if (_isRecording)
        {
            if (!_pressedKeys.Contains(e.Key))
            {
                _pressedKeys.Add(e.Key);
                UpdateKeybindDisplay();
            }
            return true; // Consume the event
        }
        return false;
    }

    private void OnKeybindKeyUp(KeyUpEvent e)
    {
        if (_isRecording)
        {
            if (_pressedKeys.Contains(e.Key))
            {
                _pressedKeys.Remove(e.Key);
            }

            // If no modifier keys are pressed, finish recording
            var hasModifier = _pressedKeys.Any(k => k == Key.LControl || k == Key.RControl ||
                                                     k == Key.LAlt || k == Key.RAlt ||
                                                     k == Key.LShift || k == Key.RShift ||
                                                     k == Key.LWin || k == Key.RWin);

            if (!hasModifier && _pressedKeys.Count == 0)
            {
                FinishRecording();
            }
            else
            {
                UpdateKeybindDisplay();
            }
        }
    }

    private void OnKeybindButtonClicked()
    {
        if (_isRecording)
        {
            FinishRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        _isRecording = true;
        _pressedKeys.Clear();
        _keybindButton.SetRecording(true);
        _keybindButton.SetKeybindText("Press keys...");
        
        // Grab keyboard focus so we receive key events
        GetContainingFocusManager()?.ChangeFocus(_keybindButton);
    }

    private void FinishRecording()
    {
        _isRecording = false;

        if (_pressedKeys.Count > 0)
        {
            var keybind = BuildKeybindString(_pressedKeys);
            SettingsService.Settings.ToggleVisibilityKeybind = keybind;
            
            // Re-register hotkey
            HotkeyService.RegisterHotkey(keybind);
            
            // Save settings
            Task.Run(async () => await SettingsService.SaveAsync());

            _keybindButton.SetKeybindText(FormatKeybind(keybind));
        }
        else
        {
            _keybindButton.SetKeybindText(FormatKeybind(SettingsService.Settings.ToggleVisibilityKeybind));
        }

        _keybindButton.SetRecording(false);
        _pressedKeys.Clear();
    }

    private void UpdateKeybindDisplay()
    {
        if (_pressedKeys.Count > 0)
        {
            _keybindButton.SetKeybindText(FormatKeybind(BuildKeybindString(_pressedKeys)));
        }
    }

    private string BuildKeybindString(List<Key> keys)
    {
        var parts = new List<string>();
        var hasShift = keys.Contains(Key.LShift) || keys.Contains(Key.RShift);

        // Add the main key (first non-modifier)
        var mainKey = keys.FirstOrDefault(k => k != Key.LControl && k != Key.RControl &&
                                                k != Key.LAlt && k != Key.RAlt &&
                                                k != Key.LShift && k != Key.RShift &&
                                                k != Key.LWin && k != Key.RWin);

        // Check if Shift is part of the key (e.g., Shift+Plus = =) or a modifier
        bool shiftIsModifier = hasShift && (mainKey == Key.Unknown || mainKey != Key.Plus);

        // Add modifiers first
        if (keys.Contains(Key.LControl) || keys.Contains(Key.RControl))
            parts.Add("Ctrl");
        if (keys.Contains(Key.LAlt) || keys.Contains(Key.RAlt))
            parts.Add("Alt");
        if (shiftIsModifier)
            parts.Add("Shift");
        if (keys.Contains(Key.LWin) || keys.Contains(Key.RWin))
            parts.Add("Win");

        // Add the main key
        if (mainKey != Key.Unknown)
        {
            parts.Add(KeyToKeybindString(mainKey));
        }

        return string.Join("+", parts);
    }

    private string KeyToKeybindString(Key key)
    {
        // Handle special keys
        return key switch
        {
            Key.Plus => "OemPlus", // = key (when Shift is held) or numpad +
            Key.Minus => "OemMinus", // - key
            Key.BracketLeft => "OemOpenBrackets",
            Key.BracketRight => "OemCloseBrackets",
            Key.Semicolon => "OemSemicolon",
            Key.Quote => "OemQuotes",
            Key.Comma => "OemComma",
            Key.Period => "OemPeriod",
            Key.Slash => "OemQuestion",
            Key.BackSlash => "OemPipe",
            Key.Tilde => "OemTilde",
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Escape => "Escape",
            Key.BackSpace => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            _ => key.ToString()
        };
    }

    private string FormatKeybind(string keybind)
    {
        // Format for display (e.g., "Ctrl+OemPlus" -> "CTRL + =")
        var parts = keybind.Split('+');
        var formatted = parts.Select(part =>
        {
            return part.Trim() switch
            {
                "OemPlus" => "=",
                "OemMinus" => "-",
                "OemOpenBrackets" => "[",
                "OemCloseBrackets" => "]",
                "OemSemicolon" => ";",
                "OemQuotes" => "'",
                "OemComma" => ",",
                "OemPeriod" => ".",
                "OemQuestion" => "/",
                "OemPipe" => "\\",
                "OemTilde" => "~",
                _ => part.ToUpper()
            };
        });

        return string.Join(" + ", formatted);
    }

    /// <summary>
    /// Custom button that can receive keyboard input for keybind recording.
    /// </summary>
    private partial class KeybindButton : ClickableContainer, IHasTooltip
    {
        private Box _background = null!;
        private SpriteText _keybindText = null!;
        private bool _isRecording;
        
        public Color4 NormalColor { get; set; }
        public Color4 HoverColor { get; set; }
        public Color4 RecordingColor { get; set; }
        public string KeybindText { get; set; } = "";
        public Func<KeyDownEvent, bool>? OnKeyDownCallback { get; set; }
        
        /// <summary>
        /// Tooltip text displayed on hover.
        /// </summary>
        public LocalisableString TooltipText { get; set; }
        public Action<KeyUpEvent>? OnKeyUpCallback { get; set; }

        // Override to accept keyboard focus for keybind recording
        public override bool AcceptsFocus => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                _background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = NormalColor
                },
                _keybindText = new SpriteText
                {
                    Text = KeybindText,
                    Font = new FontUsage("", 15),
                    Colour = Color4.White,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                }
            };
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (_isRecording && OnKeyDownCallback != null)
            {
                return OnKeyDownCallback(e);
            }
            return base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyUpEvent e)
        {
            if (_isRecording)
            {
                OnKeyUpCallback?.Invoke(e);
            }
            base.OnKeyUp(e);
        }

        protected override void OnFocus(FocusEvent e)
        {
            base.OnFocus(e);
            if (_isRecording)
            {
                _background.Colour = RecordingColor;
            }
        }

        protected override void OnFocusLost(FocusLostEvent e)
        {
            base.OnFocusLost(e);
            // If we lose focus while recording, stop recording
            if (_isRecording)
            {
                // Don't automatically stop - let the parent handle it
            }
        }

        public void SetRecording(bool recording)
        {
            _isRecording = recording;
            _background.Colour = recording ? RecordingColor : NormalColor;
            _keybindText.Colour = recording ? new Color4(50, 50, 60, 255) : Color4.White;
        }

        public void SetKeybindText(string text)
        {
            _keybindText.Text = text;
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!_isRecording)
            {
                _background.Colour = HoverColor;
            }
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (!_isRecording)
            {
                _background.Colour = NormalColor;
            }
            base.OnHoverLost(e);
        }
    }
}
