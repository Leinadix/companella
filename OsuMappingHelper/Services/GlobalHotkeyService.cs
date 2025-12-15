using System.Runtime.InteropServices;
using System.Text;

namespace OsuMappingHelper.Services;

/// <summary>
/// Service for registering and handling global hotkeys.
/// Uses Windows API to register system-wide hotkeys.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern uint VkKeyScan(char ch);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NONE = 0x0000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private IntPtr _windowHandle;
    private int _hotkeyId = 9000; // Arbitrary ID
    private bool _isRegistered;
    private string? _currentKeybind;
    private MessageOnlyWindow? _messageWindow;

    /// <summary>
    /// Event raised when the registered hotkey is pressed.
    /// </summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>
    /// Initializes the service with a window handle.
    /// If handle is IntPtr.Zero, creates a message-only window.
    /// </summary>
    public void Initialize(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            // Create message-only window for hotkey processing
            _messageWindow = new MessageOnlyWindow();
            _messageWindow.HotkeyReceived += (sender, id) =>
            {
                Console.WriteLine($"[Hotkey] HotkeyReceived event fired: id={id}, expected={_hotkeyId}");
                if (id == _hotkeyId)
                {
                    Console.WriteLine("[Hotkey] Invoking HotkeyPressed event");
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                }
            };
            _windowHandle = _messageWindow.Handle;
            Console.WriteLine($"[Hotkey] Initialized with message-only window handle: 0x{_windowHandle:X}");
        }
        else
        {
            _windowHandle = windowHandle;
            Console.WriteLine($"[Hotkey] Initialized with provided window handle: 0x{_windowHandle:X}");
        }
    }

    /// <summary>
    /// Registers a hotkey from a string format like "Ctrl+OemPlus" or "Alt+F1".
    /// </summary>
    public bool RegisterHotkey(string keybind)
    {
        if (_messageWindow == null)
        {
            Console.WriteLine("[Hotkey] Message window not initialized");
            return false;
        }

        // Unregister previous hotkey if any
        if (_isRegistered)
        {
            UnregisterHotkey();
        }

        try
        {
            var (modifiers, virtualKey) = ParseKeybind(keybind);
            Console.WriteLine($"[Hotkey] Parsed keybind '{keybind}': modifiers=0x{modifiers:X}, virtualKey=0x{virtualKey:X} ('{(char)virtualKey}')");
            
            if (virtualKey == 0)
            {
                Console.WriteLine($"[Hotkey] Failed to parse keybind: {keybind}");
                return false;
            }

            // Register on the message thread
            if (_messageWindow.RegisterHotkeyAsync(_hotkeyId, modifiers, virtualKey))
            {
                _isRegistered = true;
                _currentKeybind = keybind;
                Console.WriteLine($"[Hotkey] Successfully registered hotkey: {keybind} (id={_hotkeyId})");
                return true;
            }
            else
            {
                Console.WriteLine($"[Hotkey] Failed to register hotkey: {keybind}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hotkey] Error registering hotkey: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Unregisters the current hotkey.
    /// </summary>
    public void UnregisterHotkey()
    {
        if (_isRegistered && _messageWindow != null)
        {
            _messageWindow.UnregisterHotkeyAsync(_hotkeyId);
            _isRegistered = false;
            _currentKeybind = null;
            Console.WriteLine("[Hotkey] Unregistered hotkey");
        }
    }

    /// <summary>
    /// Processes Windows messages. Call this from your window's message loop.
    /// </summary>
    public void ProcessMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Parses a keybind string into modifiers and virtual key code.
    /// Format: "Ctrl+OemPlus", "Alt+Shift+F1", "Ctrl+=", etc.
    /// </summary>
    private (uint modifiers, uint virtualKey) ParseKeybind(string keybind)
    {
        uint modifiers = MOD_NONE;
        uint virtualKey = 0;

        var parts = keybind.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        foreach (var part in parts)
        {
            var partUpper = part.ToUpperInvariant();
            
            // Parse modifiers
            if (partUpper == "CTRL" || partUpper == "CONTROL")
            {
                modifiers |= MOD_CONTROL;
            }
            else if (partUpper == "ALT")
            {
                modifiers |= MOD_ALT;
            }
            else if (partUpper == "SHIFT")
            {
                modifiers |= MOD_SHIFT;
            }
            else if (partUpper == "WIN" || partUpper == "WINDOWS")
            {
                modifiers |= MOD_WIN;
            }
            else
            {
                // Parse the actual key
                virtualKey = ParseKey(part);
            }
        }

        return (modifiers, virtualKey);
    }

    /// <summary>
    /// Parses a key string into a virtual key code.
    /// </summary>
    private uint ParseKey(string key)
    {
        // Handle special keys
        var keyUpper = key.ToUpperInvariant();
        
        // OemPlus is the = key on most keyboards
        if (keyUpper == "OEMPLUS" || keyUpper == "=" || keyUpper == "EQUALS")
        {
            return 0xBB; // VK_OEM_PLUS
        }

        // Handle function keys
        if (keyUpper.StartsWith("F") && keyUpper.Length > 1)
        {
            if (int.TryParse(keyUpper.Substring(1), out var fNum) && fNum >= 1 && fNum <= 24)
            {
                return (uint)(0x70 + fNum - 1); // VK_F1 = 0x70
            }
        }

        // Handle single character keys (A-Z, 0-9)
        if (keyUpper.Length == 1)
        {
            var ch = keyUpper[0];
            if (ch >= 'A' && ch <= 'Z')
            {
                return (uint)ch;
            }
            if (ch >= '0' && ch <= '9')
            {
                return (uint)ch;
            }
            
            // Try to get virtual key from character
            var vk = VkKeyScan(ch);
            if (vk != 0xFFFF)
            {
                return vk & 0xFF;
            }
        }

        // Handle other common keys
        return keyUpper switch
        {
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "BACKSPACE" => 0x08,
            "DELETE" => 0x2E,
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            _ => 0
        };
    }

    public void Dispose()
    {
        UnregisterHotkey();
        _messageWindow?.Dispose();
        _messageWindow = null;
    }
}
