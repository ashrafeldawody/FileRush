using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileRush.App.Infrastructure;

public sealed class ClipboardMonitor : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly HwndSource _source;
    private readonly Action<string> _onText;
    private string? _lastText;
    private bool _listening;

    public ClipboardMonitor(Window window, Action<string> onText)
    {
        _onText = onText;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle)!;
        _source.AddHook(WndProc);
        Start();
    }

    public void Start()
    {
        if (_listening) return;
        _listening = AddClipboardFormatListener(_source.Handle);
        try
        {
            _lastText = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
        }
        catch
        {
        }
    }

    public void Stop()
    {
        if (!_listening) return;
        RemoveClipboardFormatListener(_source.Handle);
        _listening = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate && _listening)
        {
            string? text = null;
            try
            {
                if (System.Windows.Clipboard.ContainsText()) text = System.Windows.Clipboard.GetText();
            }
            catch
            {
            }
            if (!string.IsNullOrWhiteSpace(text) && text != _lastText)
            {
                _lastText = text;
                _onText(text);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
