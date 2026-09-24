using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
            Closing += MainWindow_Closing;

            // Auto-scroll chat conversation smoothly when new tokens/messages arrive
            ViewModel.ChatMessages.CollectionChanged += (s, e) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var sv = FindChildScrollViewer(lstChatMessages);
                    if (sv != null)
                    {
                        sv.ScrollToBottom();
                    }
                    else if (lstChatMessages.Items.Count > 0)
                    {
                        lstChatMessages.ScrollIntoView(lstChatMessages.Items[lstChatMessages.Items.Count - 1]);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            };

            DataObject.AddPastingHandler(txtChatInput, TxtChatInput_Pasting);
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

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                // Close button minimizes Prompter to the system tray instead of quitting
                e.Cancel = true;
                Hide();
                _trayService?.ShowFirstCloseToTrayNotification();
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
            ChatMessage? message = null;
            if (sender is FrameworkElement elem)
            {
                if (elem.Tag is ChatMessage msg)
                    message = msg;
                else if (elem.DataContext is ChatMessage dcMsg)
                    message = dcMsg;
            }

            if (message != null)
            {
                ViewModel.CopyChatMessage(message);
                e.Handled = true;
            }
        }

        private void EditUserPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem)
            {
                var msg = elem.Tag as ChatMessage ?? elem.DataContext as ChatMessage;
                if (msg != null && msg.IsUser)
                {
                    msg.BeginEdit();
                    e.Handled = true;
                    return;
                }

                // Fallback for non-user messages (e.g. assistant image prompt)
                string? prompt = null;
                if (elem.Tag is string s)
                    prompt = s;
                else if (msg != null)
                    prompt = !string.IsNullOrWhiteSpace(msg.ImagePrompt) ? msg.ImagePrompt : msg.Content;

                if (!string.IsNullOrEmpty(prompt))
                {
                    ViewModel.LoadPromptForEditing(prompt);
                    txtChatInput.Focus();
                    txtChatInput.Select(txtChatInput.Text.Length, 0);
                    e.Handled = true;
                }
            }
        }

        private void EditPromptTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.IsVisible)
            {
                tb.Focus();
                tb.Select(tb.Text.Length, 0);
            }
        }

        private void EditPromptTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox tb && tb.IsVisible)
            {
                tb.Focus();
                tb.Select(tb.Text.Length, 0);
            }
        }

        private void CancelEditPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem)
            {
                var msg = elem.Tag as ChatMessage ?? elem.DataContext as ChatMessage;
                if (msg != null)
                {
                    msg.CancelEdit();
                    e.Handled = true;
                }
            }
        }

        private void SaveEditPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem)
            {
                var msg = elem.Tag as ChatMessage ?? elem.DataContext as ChatMessage;
                if (msg != null)
                {
                    _ = ViewModel.CommitEditUserPromptAsync(msg);
                    e.Handled = true;
                }
            }
        }

        private void CopyUserPrompt_Click(object sender, RoutedEventArgs e)
        {
            string? prompt = null;
            if (sender is FrameworkElement elem)
            {
                if (elem.Tag is string s)
                    prompt = s;
                else if (elem.Tag is ChatMessage msg)
                    prompt = !string.IsNullOrWhiteSpace(msg.ImagePrompt) ? msg.ImagePrompt : msg.Content;
                else if (elem.DataContext is ChatMessage dcMsg)
                    prompt = !string.IsNullOrWhiteSpace(dcMsg.ImagePrompt) ? dcMsg.ImagePrompt : dcMsg.Content;
            }

            if (!string.IsNullOrEmpty(prompt))
            {
                ViewModel.CopyTextToClipboard(prompt, "✓ Prompt copied to clipboard!");
                e.Handled = true;
            }
        }

        private void DeleteChatMessage_Click(object sender, RoutedEventArgs e)
        {
            ChatMessage? message = null;
            if (sender is FrameworkElement elem)
            {
                if (elem.Tag is ChatMessage msg)
                    message = msg;
                else if (elem.DataContext is ChatMessage dcMsg)
                    message = dcMsg;
            }

            if (message != null)
            {
                ViewModel.DeleteChatMessage(message);
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

        private void RegenerateImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is ChatMessage message)
            {
                var rawPrompt = !string.IsNullOrWhiteSpace(message.ImagePrompt)
                    ? message.ImagePrompt
                    : message.Content;

                var prompt = MainViewModel.CleanPrompt(rawPrompt);
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    ViewModel.TriggerImageRegeneration(prompt);
                    e.Handled = true;
                }
            }
        }

        private void DeleteImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is ChatMessage message)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to delete this generated image?\n\nThis will remove it from the chat and delete the file from your computer.",
                    "Confirm Delete Image",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    ViewModel.DeleteChatMessage(message);
                }
                e.Handled = true;
            }
        }

        private void ImagePreview_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is ChatMessage message && !string.IsNullOrEmpty(message.ImagePath))
            {
                ViewModel.OpenImageFile(message.ImagePath);
                e.Handled = true;
            }
        }

        private void CopySeed_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem)
            {
                long? seed = null;
                if (elem.Tag is long l) seed = l;
                else if (elem.Tag is string s && long.TryParse(s, out var parsed)) seed = parsed;

                if (seed.HasValue)
                {
                    Clipboard.SetText(seed.Value.ToString());
                    ViewModel.ShowStatus($"✓ Copied Seed {seed.Value} to clipboard!");
                    e.Handled = true;
                }
            }
        }

        private void UseSeed_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem)
            {
                long? seed = null;
                if (elem.Tag is long l) seed = l;
                else if (elem.Tag is string s && long.TryParse(s, out var parsed)) seed = parsed;

                if (seed.HasValue)
                {
                    ViewModel.ApplySeed(seed.Value);
                    e.Handled = true;
                }
            }
        }

        private void TxtChatInput_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                var rawText = e.DataObject.GetData(DataFormats.UnicodeText) as string;
                if (!string.IsNullOrEmpty(rawText))
                {
                    var cleaned = MainViewModel.StripLeadingPromptPrefix(rawText);
                    if (cleaned != rawText)
                    {
                        var dataObj = new DataObject();
                        dataObj.SetData(DataFormats.UnicodeText, cleaned);
                        e.DataObject = dataObj;
                    }
                }
            }
        }

        private static ScrollViewer? FindChildScrollViewer(DependencyObject depObj)
        {
            if (depObj == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is ScrollViewer sv) return sv;
                var childSv = FindChildScrollViewer(child);
                if (childSv != null) return childSv;
            }
            return null;
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