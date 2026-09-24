using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Prompter.ViewModels;

namespace Prompter.Services
{
    public class SystemTrayService : IDisposable
    {
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 101;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0x5042; // "PB" for Pause/Break

        // Tray icon flags
        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int NIF_INFO = 0x00000010;

        private const int NIIF_INFO = 0x00000001;

        // Windows messages for tray
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        // Hotkey constants
        private const uint VK_PAUSE = 0x13; // Pause/Break key
        private const uint MOD_NOREPEAT = 0x4000;

        // Window display constants
        private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, IntPtr lpBits);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;

        private readonly Window _window;
        private readonly MainViewModel _viewModel;
        private IntPtr _hWnd;
        private HwndSource? _hwndSource;
        private IntPtr _hIcon = IntPtr.Zero;
        private bool _isIconAdded;
        private bool _hasShownBalloon;
        private ContextMenu? _trayContextMenu;

        public event Action? ExitRequested;

        public SystemTrayService(Window window, MainViewModel viewModel)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        public void Initialize(IntPtr hWnd)
        {
            _hWnd = hWnd;
            _hwndSource = HwndSource.FromHwnd(_hWnd);
            _hwndSource?.AddHook(HwndHook);

            _hIcon = LoadTrayIcon();
            AddTrayIcon();
            RegisterGlobalHotkey();
            CreateContextMenu();
        }

        private static IntPtr LoadTrayIcon()
        {
            try
            {
                // 1. Try loading directly from prompter.ico in current/base directory
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(baseDir, "prompter.ico"),
                    Path.Combine(Environment.CurrentDirectory, "prompter.ico"),
                    "prompter.ico"
                };

                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        IntPtr hIcon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                        if (hIcon != IntPtr.Zero)
                        {
                            return hIcon;
                        }
                    }
                }

                // 2. Try extracting from running executable
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    uint count = ExtractIconEx(exePath, 0, out _, out IntPtr smallIcon, 1);
                    if (count > 0 && smallIcon != IntPtr.Zero)
                    {
                        return smallIcon;
                    }
                }
            }
            catch { }

            // 3. Fallback to procedurally generated icon
            return GenerateAppIcon();
        }

        private void AddTrayIcon()
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "Prompter - AI Prompt Manager (Press Pause/Break to show)"
            };

            _isIconAdded = Shell_NotifyIconW(NIM_ADD, ref nid);
        }

        private void RegisterGlobalHotkey()
        {
            // Register Pause/Break key globally
            RegisterHotKey(_hWnd, HOTKEY_ID, MOD_NOREPEAT, VK_PAUSE);
        }

        private void UnregisterGlobalHotkey()
        {
            UnregisterHotKey(_hWnd, HOTKEY_ID);
        }

        public void ShowNotification(string title, string message)
        {
            if (!_isIconAdded) return;

            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = 1,
                uFlags = NIF_INFO,
                szInfo = message,
                szInfoTitle = title,
                dwInfoFlags = NIIF_INFO
            };

            Shell_NotifyIconW(NIM_MODIFY, ref nid);
        }

        public void ShowFirstCloseToTrayNotification()
        {
            if (!_hasShownBalloon)
            {
                _hasShownBalloon = true;
                ShowNotification("Prompter is running in system tray", "Prompter has been minimized to the system tray. Click the tray icon or press Pause/Break to bring Prompter up, or right-click to exit.");
            }
        }

        public void ShowFirstMinimizeNotification() => ShowFirstCloseToTrayNotification();

        public void BringWindowToFront()
        {
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            ShowWindow(_hWnd, SW_RESTORE);
            SetForegroundWindow(_hWnd);

            // Pop window to front
            _window.Topmost = true;
            _window.Topmost = false;
            _window.Activate();
            _window.Focus();
        }

        private void CreateContextMenu()
        {
            _trayContextMenu = new ContextMenu();

            var miShow = new MenuItem
            {
                Header = "⚡ Show Prompter (Pause/Break)",
                FontWeight = FontWeights.Bold
            };
            miShow.Click += (s, e) => BringWindowToFront();
            _trayContextMenu.Items.Add(miShow);

            var miLockAll = new MenuItem
            {
                Header = "🔒 Lock All Protected Folders"
            };
            miLockAll.Click += (s, e) =>
            {
                _viewModel.LockAllFoldersCommand.Execute(null);
            };
            _trayContextMenu.Items.Add(miLockAll);

            _trayContextMenu.Items.Add(new Separator());

            var miExit = new MenuItem
            {
                Header = "❌ Exit Prompter"
            };
            miExit.Click += (s, e) =>
            {
                ExitRequested?.Invoke();
            };
            _trayContextMenu.Items.Add(miExit);
        }

        private void ShowTrayContextMenu()
        {
            if (_trayContextMenu == null) return;

            if (GetCursorPos(out var pt))
            {
                // Set foreground window before TrackPopupMenu/ContextMenu to allow clicking outside to dismiss
                SetForegroundWindow(_hWnd);
                _trayContextMenu.Placement = PlacementMode.AbsolutePoint;
                _trayContextMenu.HorizontalOffset = pt.X;
                _trayContextMenu.VerticalOffset = pt.Y;
                _trayContextMenu.IsOpen = true;
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                // Global Pause/Break key pressed
                BringWindowToFront();
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == WM_TRAYICON)
            {
                var eventCode = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                switch (eventCode)
                {
                    case WM_LBUTTONUP:
                    case WM_LBUTTONDBLCLK:
                        BringWindowToFront();
                        handled = true;
                        break;

                    case WM_RBUTTONUP:
                    case WM_CONTEXTMENU:
                        ShowTrayContextMenu();
                        handled = true;
                        break;
                }
            }

            return IntPtr.Zero;
        }

        private static IntPtr GenerateAppIcon()
        {
            const int width = 16;
            const int height = 16;
            var colorPixels = new uint[width * height];
            var maskPixels = new byte[width * height / 8];

            const uint blue = 0xFF2563EB; // #2563EB
            const uint white = 0xFFFFFFFF;
            const uint trans = 0x00000000;

            // Pattern for a 16x16 icon with rounded blue badge and white lightning bolt
            string[] pattern =
            {
                "................",
                "..############..",
                ".##############.",
                ".#######WW#####.",
                ".######WWW#####.",
                ".#####WWWW#####.",
                ".####WWWWW#####.",
                ".###WWWWWWWW###.",
                ".######WWWW####.",
                ".######WWW#####.",
                ".######WW######.",
                ".######W#######.",
                ".##############.",
                "..############..",
                "................",
                "................"
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    char c = pattern[y][x];

                    if (c == '.')
                    {
                        colorPixels[index] = trans;
                        int maskByte = index / 8;
                        int maskBit = 7 - (index % 8);
                        maskPixels[maskByte] |= (byte)(1 << maskBit); // 1 = transparent
                    }
                    else if (c == 'W')
                    {
                        colorPixels[index] = white;
                    }
                    else // '#'
                    {
                        colorPixels[index] = blue;
                    }
                }
            }

            GCHandle colorHandle = GCHandle.Alloc(colorPixels, GCHandleType.Pinned);
            GCHandle maskHandle = GCHandle.Alloc(maskPixels, GCHandleType.Pinned);

            IntPtr hbmColor = CreateBitmap(width, height, 1, 32, colorHandle.AddrOfPinnedObject());
            IntPtr hbmMask = CreateBitmap(width, height, 1, 1, maskHandle.AddrOfPinnedObject());

            colorHandle.Free();
            maskHandle.Free();

            var iconInfo = new ICONINFO
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = hbmMask,
                hbmColor = hbmColor
            };

            IntPtr hIcon = CreateIconIndirect(ref iconInfo);

            DeleteObject(hbmColor);
            DeleteObject(hbmMask);

            return hIcon;
        }

        public void Dispose()
        {
            UnregisterGlobalHotkey();

            if (_isIconAdded)
            {
                var nid = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hWnd,
                    uID = 1
                };
                Shell_NotifyIconW(NIM_DELETE, ref nid);
                _isIconAdded = false;
            }

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(HwndHook);
                _hwndSource = null;
            }

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }
    }
}
