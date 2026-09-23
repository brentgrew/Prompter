using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Prompter.Services
{
    public enum AppTheme
    {
        Light,
        Dark
    }

    public class ThemeService
    {
        private static ThemeService? _instance;
        public static ThemeService Instance => _instance ??= new ThemeService();

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private readonly string _settingsFilePath;
        private AppTheme _currentTheme = AppTheme.Dark;

        public AppTheme CurrentTheme
        {
            get => _currentTheme;
            private set
            {
                if (_currentTheme != value)
                {
                    _currentTheme = value;
                    ThemeChanged?.Invoke(_currentTheme);
                }
            }
        }

        public event Action<AppTheme>? ThemeChanged;

        public ThemeService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, "Prompter");
            Directory.CreateDirectory(folder);
            _settingsFilePath = Path.Combine(folder, "theme.json");
        }

        public void Initialize()
        {
            var loadedTheme = LoadTheme();
            ApplyTheme(loadedTheme);
        }

        public void ToggleTheme()
        {
            var nextTheme = _currentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            ApplyTheme(nextTheme);
            SaveTheme(nextTheme);
        }

        public void ApplyWindowTheme(Window window)
        {
            if (window == null) return;

            var helper = new WindowInteropHelper(window);
            if (helper.Handle != IntPtr.Zero)
            {
                UpdateTitleBarTheme(helper.Handle, CurrentTheme == AppTheme.Dark);
            }
            else
            {
                window.SourceInitialized += (s, e) =>
                {
                    var h = new WindowInteropHelper(window).Handle;
                    if (h != IntPtr.Zero)
                    {
                        UpdateTitleBarTheme(h, CurrentTheme == AppTheme.Dark);
                    }
                };
            }
        }

        public static void UpdateTitleBarTheme(IntPtr hwnd, bool isDark)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int useDark = isDark ? 1 : 0;
                int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
                if (hr != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));
                }
            }
            catch
            {
                // Silently ignore if dwmapi is unsupported or fails on older OS
            }
        }

        public void ApplyTheme(AppTheme theme)
        {
            CurrentTheme = theme;
            if (Application.Current == null) return;
            var resources = Application.Current.Resources;

            if (theme == AppTheme.Dark)
            {
                SetResource(resources, "WindowBackgroundBrush", ColorFromHex("#090D16"));
                SetResource(resources, "WindowForegroundBrush", ColorFromHex("#F1F5F9"));
                SetResource(resources, "TopBarBackgroundBrush", ColorFromHex("#0B0F19"));
                SetResource(resources, "SidebarBackgroundBrush", ColorFromHex("#0F172A"));
                SetResource(resources, "CenterListBackgroundBrush", ColorFromHex("#111827"));
                SetResource(resources, "ContentAreaBackgroundBrush", ColorFromHex("#0F172A"));

                // Folder Cards & Icons (Lighter in Dark Mode with high contrast icons)
                SetResource(resources, "FolderCardBackgroundBrush", ColorFromHex("#1E293B"));
                SetResource(resources, "FolderCardBorderBrush", ColorFromHex("#334155"));
                SetResource(resources, "FolderCardHoverBrush", ColorFromHex("#273549"));
                SetResource(resources, "FolderCardSelectedBrush", ColorFromHex("#2563EB"));
                SetResource(resources, "FolderCardSelectedBorderBrush", ColorFromHex("#60A5FA"));
                SetResource(resources, "FolderIconBrush", ColorFromHex("#FBBF24"));
                SetResource(resources, "LockIconBrush", ColorFromHex("#F87171"));
                SetResource(resources, "UnlockIconBrush", ColorFromHex("#34D399"));
                SetResource(resources, "FolderBadgeBackgroundBrush", ColorFromHex("#0F172A"));
                SetResource(resources, "FolderBadgeForegroundBrush", ColorFromHex("#94A3B8"));

                SetResource(resources, "CardBackgroundBrush", ColorFromHex("#1E293B"));
                SetResource(resources, "CardHoverBackgroundBrush", ColorFromHex("#273549"));
                SetResource(resources, "CardSelectedBackgroundBrush", ColorFromHex("#1E3A8A"));
                SetResource(resources, "CardSelectedBorderBrush", ColorFromHex("#3B82F6"));

                SetResource(resources, "BorderBrush", ColorFromHex("#1E293B"));
                SetResource(resources, "SubtleBorderBrush", ColorFromHex("#334155"));
                SetResource(resources, "SeparatorBrush", ColorFromHex("#1E293B"));

                SetResource(resources, "TextHeadingBrush", ColorFromHex("#F8FAFC"));
                SetResource(resources, "TextBodyBrush", ColorFromHex("#E2E8F0"));
                SetResource(resources, "TextMutedBrush", ColorFromHex("#94A3B8"));
                SetResource(resources, "TextSubtleBrush", ColorFromHex("#64748B"));

                SetResource(resources, "InputBackgroundBrush", ColorFromHex("#0B0F19"));
                SetResource(resources, "InputForegroundBrush", ColorFromHex("#F8FAFC"));
                SetResource(resources, "InputBorderBrush", ColorFromHex("#334155"));

                SetResource(resources, "AccentPrimaryBrush", ColorFromHex("#3B82F6"));
                SetResource(resources, "AccentHoverBrush", ColorFromHex("#2563EB"));
                SetResource(resources, "AccentButtonForegroundBrush", ColorFromHex("#FFFFFF"));

                SetResource(resources, "OutlineButtonBackgroundBrush", ColorFromHex("#1E293B"));
                SetResource(resources, "OutlineButtonForegroundBrush", ColorFromHex("#E2E8F0"));
                SetResource(resources, "OutlineButtonBorderBrush", ColorFromHex("#334155"));

                SetResource(resources, "QuickCopyBackgroundBrush", ColorFromHex("#1E293B"));
                SetResource(resources, "QuickCopyForegroundBrush", ColorFromHex("#60A5FA"));
                SetResource(resources, "QuickCopyBorderBrush", ColorFromHex("#2563EB"));
                SetResource(resources, "QuickCopyHoverBackgroundBrush", ColorFromHex("#2563EB33"));

                SetResource(resources, "BadgeBackgroundBrush", ColorFromHex("#1E293B"));
                SetResource(resources, "BadgeForegroundBrush", ColorFromHex("#94A3B8"));

                SetResource(resources, "StatsBarBackgroundBrush", ColorFromHex("#111827"));
                SetResource(resources, "StatsBarBorderBrush", ColorFromHex("#1E293B"));

                SetResource(resources, "DangerBackgroundBrush", ColorFromHex("#450A0A"));
                SetResource(resources, "DangerBorderBrush", ColorFromHex("#7F1D1D"));
                SetResource(resources, "DangerForegroundBrush", ColorFromHex("#F87171"));

                SetResource(resources, "ToastBackgroundBrush", ColorFromHex("#0B0F19"));
                SetResource(resources, "ToastBorderBrush", ColorFromHex("#3B82F6"));

                SetResource(resources, "DialogCardBackgroundBrush", ColorFromHex("#1E293B"));

                // Chat Bot Resources
                SetResource(resources, "ChatBackgroundBrush", ColorFromHex("#090D16"));
                SetResource(resources, "ChatHeaderBackgroundBrush", ColorFromHex("#0E1526"));
                SetResource(resources, "ChatSplitterBrush", ColorFromHex("#1E293B"));
                SetResource(resources, "ChatSplitterGripBrush", ColorFromHex("#475569"));
                SetResource(resources, "ChatBubbleUserBackgroundBrush", ColorFromHex("#1D4ED8"));
                SetResource(resources, "ChatBubbleUserForegroundBrush", ColorFromHex("#FFFFFF"));
                SetResource(resources, "ChatBubbleAssistantBackgroundBrush", ColorFromHex("#1E293B"));
                SetResource(resources, "ChatBubbleAssistantForegroundBrush", ColorFromHex("#F1F5F9"));
                SetResource(resources, "ChatBubbleAssistantBorderBrush", ColorFromHex("#334155"));
                SetResource(resources, "ChatInputBackgroundBrush", ColorFromHex("#0F172A"));
                SetResource(resources, "ChatInputBorderBrush", ColorFromHex("#334155"));
                SetResource(resources, "ChatInputForegroundBrush", ColorFromHex("#F8FAFC"));
                SetResource(resources, "ChatThinkingBackgroundBrush", ColorFromHex("#141D2E"));
                SetResource(resources, "ChatThinkingBorderBrush", ColorFromHex("#293548"));
                SetResource(resources, "ChatThinkingForegroundBrush", ColorFromHex("#94A3B8"));
            }
            else
            {
                SetResource(resources, "WindowBackgroundBrush", ColorFromHex("#F1F5F9"));
                SetResource(resources, "WindowForegroundBrush", ColorFromHex("#0F172A"));
                SetResource(resources, "TopBarBackgroundBrush", ColorFromHex("#0F172A"));
                SetResource(resources, "SidebarBackgroundBrush", ColorFromHex("#FFFFFF"));
                SetResource(resources, "CenterListBackgroundBrush", ColorFromHex("#FAFAFA"));
                SetResource(resources, "ContentAreaBackgroundBrush", ColorFromHex("#FFFFFF"));

                // Folder Cards & Icons
                SetResource(resources, "FolderCardBackgroundBrush", ColorFromHex("#F8FAFC"));
                SetResource(resources, "FolderCardBorderBrush", ColorFromHex("#E2E8F0"));
                SetResource(resources, "FolderCardHoverBrush", ColorFromHex("#F1F5F9"));
                SetResource(resources, "FolderCardSelectedBrush", ColorFromHex("#EFF6FF"));
                SetResource(resources, "FolderCardSelectedBorderBrush", ColorFromHex("#2563EB"));
                SetResource(resources, "FolderIconBrush", ColorFromHex("#D97706"));
                SetResource(resources, "LockIconBrush", ColorFromHex("#DC2626"));
                SetResource(resources, "UnlockIconBrush", ColorFromHex("#059669"));
                SetResource(resources, "FolderBadgeBackgroundBrush", ColorFromHex("#E2E8F0"));
                SetResource(resources, "FolderBadgeForegroundBrush", ColorFromHex("#475569"));

                SetResource(resources, "CardBackgroundBrush", ColorFromHex("#FFFFFF"));
                SetResource(resources, "CardHoverBackgroundBrush", ColorFromHex("#F8FAFC"));
                SetResource(resources, "CardSelectedBackgroundBrush", ColorFromHex("#EFF6FF"));
                SetResource(resources, "CardSelectedBorderBrush", ColorFromHex("#2563EB"));

                SetResource(resources, "BorderBrush", ColorFromHex("#E2E8F0"));
                SetResource(resources, "SubtleBorderBrush", ColorFromHex("#CBD5E1"));
                SetResource(resources, "SeparatorBrush", ColorFromHex("#F1F5F9"));

                SetResource(resources, "TextHeadingBrush", ColorFromHex("#0F172A"));
                SetResource(resources, "TextBodyBrush", ColorFromHex("#1E293B"));
                SetResource(resources, "TextMutedBrush", ColorFromHex("#64748B"));
                SetResource(resources, "TextSubtleBrush", ColorFromHex("#94A3B8"));

                SetResource(resources, "InputBackgroundBrush", ColorFromHex("#F8FAFC"));
                SetResource(resources, "InputForegroundBrush", ColorFromHex("#0F172A"));
                SetResource(resources, "InputBorderBrush", ColorFromHex("#CBD5E1"));

                SetResource(resources, "AccentPrimaryBrush", ColorFromHex("#2563EB"));
                SetResource(resources, "AccentHoverBrush", ColorFromHex("#1D4ED8"));
                SetResource(resources, "AccentButtonForegroundBrush", ColorFromHex("#FFFFFF"));

                SetResource(resources, "OutlineButtonBackgroundBrush", ColorFromHex("#FFFFFF"));
                SetResource(resources, "OutlineButtonForegroundBrush", ColorFromHex("#334155"));
                SetResource(resources, "OutlineButtonBorderBrush", ColorFromHex("#CBD5E1"));

                SetResource(resources, "QuickCopyBackgroundBrush", ColorFromHex("#EEF2FF"));
                SetResource(resources, "QuickCopyForegroundBrush", ColorFromHex("#3B82F6"));
                SetResource(resources, "QuickCopyBorderBrush", ColorFromHex("#C7D2FE"));
                SetResource(resources, "QuickCopyHoverBackgroundBrush", ColorFromHex("#DBEAFE"));

                SetResource(resources, "BadgeBackgroundBrush", ColorFromHex("#F1F5F9"));
                SetResource(resources, "BadgeForegroundBrush", ColorFromHex("#64748B"));

                SetResource(resources, "StatsBarBackgroundBrush", ColorFromHex("#F8FAFC"));
                SetResource(resources, "StatsBarBorderBrush", ColorFromHex("#E2E8F0"));

                SetResource(resources, "DangerBackgroundBrush", ColorFromHex("#FEF2F2"));
                SetResource(resources, "DangerBorderBrush", ColorFromHex("#FECACA"));
                SetResource(resources, "DangerForegroundBrush", ColorFromHex("#DC2626"));

                SetResource(resources, "ToastBackgroundBrush", ColorFromHex("#0F172A"));
                SetResource(resources, "ToastBorderBrush", ColorFromHex("#2563EB"));

                SetResource(resources, "DialogCardBackgroundBrush", ColorFromHex("#F1F5F9"));

                // Chat Bot Resources
                SetResource(resources, "ChatBackgroundBrush", ColorFromHex("#F8FAFC"));
                SetResource(resources, "ChatHeaderBackgroundBrush", ColorFromHex("#FFFFFF"));
                SetResource(resources, "ChatSplitterBrush", ColorFromHex("#E2E8F0"));
                SetResource(resources, "ChatSplitterGripBrush", ColorFromHex("#94A3B8"));
                SetResource(resources, "ChatBubbleUserBackgroundBrush", ColorFromHex("#2563EB"));
                SetResource(resources, "ChatBubbleUserForegroundBrush", ColorFromHex("#FFFFFF"));
                SetResource(resources, "ChatBubbleAssistantBackgroundBrush", ColorFromHex("#FFFFFF"));
                SetResource(resources, "ChatBubbleAssistantForegroundBrush", ColorFromHex("#0F172A"));
                SetResource(resources, "ChatBubbleAssistantBorderBrush", ColorFromHex("#E2E8F0"));
                SetResource(resources, "ChatInputBackgroundBrush", ColorFromHex("#FFFFFF"));
                SetResource(resources, "ChatInputBorderBrush", ColorFromHex("#CBD5E1"));
                SetResource(resources, "ChatInputForegroundBrush", ColorFromHex("#0F172A"));
                SetResource(resources, "ChatThinkingBackgroundBrush", ColorFromHex("#F1F5F9"));
                SetResource(resources, "ChatThinkingBorderBrush", ColorFromHex("#E2E8F0"));
                SetResource(resources, "ChatThinkingForegroundBrush", ColorFromHex("#64748B"));
            }

            // Dynamically update native title bars for all active windows
            foreach (Window win in Application.Current.Windows)
            {
                var h = new WindowInteropHelper(win).Handle;
                if (h != IntPtr.Zero)
                {
                    UpdateTitleBarTheme(h, theme == AppTheme.Dark);
                }
            }
        }

        private static void SetResource(ResourceDictionary resources, string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        private static Color ColorFromHex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private AppTheme LoadTheme()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("theme", out var themeElem))
                    {
                        var themeStr = themeElem.GetString();
                        if (Enum.TryParse<AppTheme>(themeStr, ignoreCase: true, out var theme))
                        {
                            return theme;
                        }
                    }
                }
            }
            catch { }

            return AppTheme.Dark;
        }

        private void SaveTheme(AppTheme theme)
        {
            try
            {
                var json = JsonSerializer.Serialize(new { theme = theme.ToString() });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch { }
        }
    }
}
