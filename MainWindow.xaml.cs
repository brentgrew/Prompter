using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Prompter.Models;
using Prompter.Services;
using Prompter.ViewModels;

namespace Prompter
{
    public partial class MainWindow : Window
    {
        private SystemTrayService? _trayService;
        private bool _isExplicitExit;

        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            SourceInitialized += MainWindow_SourceInitialized;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;

            // Auto-scroll chat conversation when new tokens/messages arrive
            ViewModel.ChatMessages.CollectionChanged += (s, e) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (lstChatMessages.Items.Count > 0)
                    {
                        lstChatMessages.ScrollIntoView(lstChatMessages.Items[lstChatMessages.Items.Count - 1]);
                    }
                });
            };
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            ThemeService.Instance.ApplyWindowTheme(this);
            var helper = new WindowInteropHelper(this);
            _trayService = new SystemTrayService(this, ViewModel);
            _trayService.Initialize(helper.Handle);
            _trayService.ExitRequested += OnExitRequested;

            var source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (App.ActivateWindowMessage != 0 && (uint)msg == App.ActivateWindowMessage)
            {
                RestoreAndBringToFront();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void RestoreAndBringToFront()
        {
            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                _trayService?.ShowFirstMinimizeNotification();
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                // Minimize to tray instead of quitting
                e.Cancel = true;
                Hide();
                _trayService?.ShowFirstMinimizeNotification();
                return;
            }

            ViewModel.SaveVaultOnExit();
            _trayService?.Dispose();
        }

        private void OnExitRequested()
        {
            _isExplicitExit = true;
            Close();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PromptItem prompt)
            {
                ViewModel.CopyPromptToClipboard(prompt);
                e.Handled = true;
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PromptItem prompt)
            {
                ViewModel.DeletePrompt(prompt);
                e.Handled = true;
            }
        }

        private void ChatCopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ChatMessage message)
            {
                ViewModel.CopyChatMessage(message);
                e.Handled = true;
            }
        }

        private void OpenImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string path)
            {
                ViewModel.OpenImageFile(path);
                e.Handled = true;
            }
        }

        private void CopyImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string path)
            {
                ViewModel.CopyImageToClipboard(path);
                e.Handled = true;
            }
        }

        private void ShowInFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string path)
            {
                ViewModel.ShowImageInFolder(path);
                e.Handled = true;
            }
        }

        private void TxtChatInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Allow Shift+Enter to create a newline
                    return;
                }

                // Plain Enter sends the message
                e.Handled = true;
                if (ViewModel.SendChatMessageCommand.CanExecute(null))
                {
                    ViewModel.SendChatMessageCommand.Execute(null);
                }
            }
        }
    }
}