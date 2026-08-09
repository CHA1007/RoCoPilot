using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RocoPilot.Capture;
using Wpf.Ui.Controls;

namespace RocoPilot.Shell.Dialogs;

public sealed partial class WindowPickerDialog : FluentWindow
{
    private sealed record PickItem(string Title, string ProcessName, ImageSource? Icon, CaptureWindow Window);

    private const int WmGetIcon = 0x007F;
    private const int IconBig = 1;
    private const int IconSmall = 0;
    private const int GclHIcon = -14;

    public CaptureWindow? Picked { get; private set; }

    public WindowPickerDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ClampToOwner();
            FindWindows();
        };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
    }

    private void ClampToOwner()
    {
        if (Owner is not { } owner)
        {
            return;
        }

        var width = Math.Min(Width, owner.ActualWidth);
        var height = Math.Min(Height, owner.ActualHeight);
        Width = width;
        Height = height;
        Left = owner.Left + (owner.ActualWidth - width) / 2;
        Top = owner.Top + (owner.ActualHeight - height) / 2;
    }

    private void FindWindows()
    {
        var self = Environment.ProcessId;
        var items = new List<PickItem>();
        foreach (var window in WindowFinder.ListAppWindows())
        {
            var processId = WindowFinder.GetProcessId(window.Handle);
            if (processId == self)
            {
                continue;
            }

            var (processName, exePath) = ProcessInfo(processId);
            items.Add(new PickItem(window.Title, processName, GrabIcon(window.Handle, exePath), window));
        }

        WindowList.ItemsSource = items
            .OrderByDescending(item => string.Equals(item.ProcessName, WindowFinder.GameProcessName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => item.Window.Handle)
            .ToList();
    }

    private static (string Name, string? ExePath) ProcessInfo(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            string? exePath = null;
            try
            {
                exePath = process.MainModule?.FileName;
            }
            catch
            {
            }

            return (process.ProcessName, exePath);
        }
        catch
        {
            return ("—", null);
        }
    }

    private static ImageSource? GrabIcon(IntPtr hwnd, string? exePath)
    {
        var icon = TryGrab(() => Interop.SendMessage(hwnd, WmGetIcon, IconBig, IntPtr.Zero));
        if (icon == IntPtr.Zero)
        {
            icon = TryGrab(() => Interop.SendMessage(hwnd, WmGetIcon, IconSmall, IntPtr.Zero));
        }

        if (icon == IntPtr.Zero)
        {
            icon = TryGrab(() => Interop.GetClassLongPtr(hwnd, GclHIcon));
        }

        if (icon == IntPtr.Zero && exePath is not null)
        {
            icon = TryGrab(() => ExtractFileIcon(exePath));
        }

        if (icon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr TryGrab(Func<IntPtr> grab)
    {
        try
        {
            return grab();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static IntPtr ExtractFileIcon(string exePath)
    {
        var info = new Interop.ShFileInfo();
        Interop.SHGetFileInfo(exePath, 0, ref info, (uint)Marshal.SizeOf<Interop.ShFileInfo>(), Interop.ShgfiIcon);
        return info.hIcon;
    }

    private void OnListSelected(object sender, SelectionChangedEventArgs e)
    {
        if (WindowList.SelectedItem is PickItem item)
        {
            Picked = item.Window;
            DialogResult = true;
        }
    }

    private static partial class Interop
    {
        public const uint ShgfiIcon = 0x100;

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        public static partial IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

        [LibraryImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        public static partial IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ShFileInfo
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }
    }
}
