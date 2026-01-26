using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Companella.Analyzers.Attributes;
using Companella.Services.Common;

namespace Companella.Services.Platform;

/// <summary>
/// Creates a message-only window on a dedicated thread for receiving Windows messages (like WM_HOTKEY).
/// This is needed because osu!Framework doesn't expose the message loop directly.
/// </summary>
[WinApiContext]
public class MessageOnlyWindow : IDisposable
{
	private const int WM_HOTKEY = 0x0312;
	private const int WM_QUIT = 0x0012;
	private const int WM_USER = 0x0400;
	private const int WM_REGISTER_HOTKEY = WM_USER + 1;
	private const int WM_UNREGISTER_HOTKEY = WM_USER + 2;

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr CreateWindowEx(
		uint dwExStyle, string lpClassName, string lpWindowName,
		uint dwStyle, int x, int y, int nWidth, int nHeight,
		IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool DestroyWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	private static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll")]
	private static extern IntPtr DispatchMessage(ref MSG lpMsg);

	[DllImport("user32.dll")]
	private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam,
		IntPtr lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	private const int GWL_WNDPROC = -4;
	private static readonly IntPtr HWND_MESSAGE = new(-3);

	[StructLayout(LayoutKind.Sequential)]
	private struct MSG
	{
		public IntPtr WndHandle;
		public uint Message;
		public IntPtr WParam;
		public IntPtr LParam;
		public uint Time;
		public int PtX;
		public int PtY;
	}

	private struct HotkeyRequest
	{
		public int Id;
		public uint Modifiers;
		public uint VirtualKey;
		public TaskCompletionSource<bool> Completion;
	}

	private IntPtr _handle;
	private Thread? _messageThread;
	private uint _messageThreadId;
	private volatile bool _isRunning;
	private WndProcDelegate? _wndProcDelegate;
	private IntPtr _originalWndProc;
	private readonly ManualResetEvent _handleCreated = new(false);
	private readonly ConcurrentQueue<HotkeyRequest> _pendingRegistrations = new();
	private readonly ConcurrentQueue<int> _pendingUnregistrations = new();

	/// <summary>
	/// Event raised when a hotkey message is received.
	/// </summary>
	public event EventHandler<int>? HotkeyReceived;

	/// <summary>
	/// Gets the window handle.
	/// </summary>
	public IntPtr Handle => _handle;

	public MessageOnlyWindow()
	{
		_isRunning = true;
		_messageThread = new Thread(MessageLoop)
		{
			Name = "HotkeyMessageLoop",
			IsBackground = true
		};
		_messageThread.SetApartmentState(ApartmentState.STA);
		_messageThread.Start();

		// Wait for handle to be created (with timeout)
		if (!_handleCreated.WaitOne(5000))
			Logger.Info("[MessageOnlyWindow] Timeout waiting for window handle creation");
	}

	/// <summary>
	/// Registers a hotkey on the message thread.
	/// </summary>
	public bool RegisterHotkeyAsync(int id, uint modifiers, uint virtualKey)
	{
		if (_handle == IntPtr.Zero)
		{
			Logger.Info("[MessageOnlyWindow] Cannot register hotkey - window not created");
			return false;
		}

		var tcs = new TaskCompletionSource<bool>();
		_pendingRegistrations.Enqueue(new HotkeyRequest
		{
			Id = id,
			Modifiers = modifiers,
			VirtualKey = virtualKey,
			Completion = tcs
		});

		// Post message to trigger processing
		PostMessage(_handle, WM_REGISTER_HOTKEY, IntPtr.Zero, IntPtr.Zero);

		// Wait for result with timeout
		if (tcs.Task.Wait(1000)) return tcs.Task.Result;

		Logger.Info("[MessageOnlyWindow] Timeout waiting for hotkey registration");
		return false;
	}

	/// <summary>
	/// Unregisters a hotkey on the message thread.
	/// </summary>
	public void UnregisterHotkeyAsync(int id)
	{
		if (_handle == IntPtr.Zero)
			return;

		_pendingUnregistrations.Enqueue(id);
		PostMessage(_handle, WM_UNREGISTER_HOTKEY, IntPtr.Zero, IntPtr.Zero);
	}

	private void MessageLoop()
	{
		_messageThreadId = GetCurrentThreadId();

		try
		{
			// Create a message-only window using the built-in "Static" class
			_handle = CreateWindowEx(
				0,
				"Static",
				"CompanellaHotkeyWindow",
				0,
				0, 0, 0, 0,
				HWND_MESSAGE,
				IntPtr.Zero,
				IntPtr.Zero,
				IntPtr.Zero);

			if (_handle == IntPtr.Zero)
			{
				var error = Marshal.GetLastWin32Error();
				Logger.Info($"[MessageOnlyWindow] Failed to create window: {error}");
				_handleCreated.Set();
				return;
			}

			Logger.Info($"[MessageOnlyWindow] Created message-only window: 0x{_handle:X}");

			// Subclass the window to intercept messages
			_wndProcDelegate = WndProc;
			var newWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
			_originalWndProc = SetWindowLongPtr(_handle, GWL_WNDPROC, newWndProc);

			_handleCreated.Set();

			// Message loop
			MSG msg;
			int result;
			while (_isRunning && (result = GetMessage(out msg, IntPtr.Zero, 0, 0)) != 0)
			{
				if (result == -1)
				{
					Logger.Info($"[MessageOnlyWindow] GetMessage error: {Marshal.GetLastWin32Error()}");
					break;
				}

				if (msg.Message == WM_QUIT)
					break;

				TranslateMessage(ref msg);
				DispatchMessage(ref msg);
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[MessageOnlyWindow] Exception in message loop: {ex.Message}");
		}
		finally
		{
			if (_handle != IntPtr.Zero)
			{
				DestroyWindow(_handle);
				_handle = IntPtr.Zero;
			}

			_handleCreated.Set();
		}
	}

	private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == WM_HOTKEY)
		{
			Logger.Info($"[MessageOnlyWindow] WM_HOTKEY in WndProc: id={wParam.ToInt32()}");
			HotkeyReceived?.Invoke(this, wParam.ToInt32());
		}
		else if (msg == WM_REGISTER_HOTKEY)
		{
			ProcessPendingRegistrations();
		}
		else if (msg == WM_UNREGISTER_HOTKEY)
		{
			ProcessPendingUnregistrations();
		}

		if (_originalWndProc != IntPtr.Zero) return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);

		return IntPtr.Zero;
	}

	private void ProcessPendingRegistrations()
	{
		while (_pendingRegistrations.TryDequeue(out var request))
		{
			// First try to unregister in case it was left over from a crash
			UnregisterHotKey(_handle, request.Id);

			var success = RegisterHotKey(_handle, request.Id, request.Modifiers, request.VirtualKey);
			if (!success)
			{
				var error = Marshal.GetLastWin32Error();
				Logger.Info($"[MessageOnlyWindow] RegisterHotKey failed: error={error}");
			}
			else
			{
				Logger.Info($"[MessageOnlyWindow] RegisterHotKey succeeded: id={request.Id}");
			}

			request.Completion.TrySetResult(success);
		}
	}

	private void ProcessPendingUnregistrations()
	{
		while (_pendingUnregistrations.TryDequeue(out var id))
		{
			UnregisterHotKey(_handle, id);
			Logger.Info($"[MessageOnlyWindow] UnregisterHotKey: id={id}");
		}
	}

	public void Dispose()
	{
		_isRunning = false;

		if (_messageThreadId != 0) PostThreadMessage(_messageThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

		_messageThread?.Join(1000);
		_handleCreated.Dispose();

		GC.SuppressFinalize(this);
	}
}
