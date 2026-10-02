using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Prompter.Services;

namespace Prompter.Views
{
    public partial class AddModelDialog : Window
    {
        private readonly LocalAiService _localAiService;
        private bool _isGgufMode;
        private CancellationTokenSource? _pullCts;

        public bool ModelAdded { get; private set; }
        public string? AddedModelName { get; private set; }

        public AddModelDialog(LocalAiService? localAiService = null)
        {
            InitializeComponent();
            _localAiService = localAiService ?? LocalAiService.Instance;
            SourceInitialized += (s, e) => ThemeService.Instance.ApplyWindowTheme(this);
        }

        private void TabPull_Click(object sender, RoutedEventArgs e)
        {
            _isGgufMode = false;
            panelPull.Visibility = Visibility.Visible;
            panelGguf.Visibility = Visibility.Collapsed;

            btnTabPull.Background = (Brush)FindResource("AccentPrimaryBrush");
            btnTabPull.Foreground = Brushes.White;
            btnTabGguf.Background = Brushes.Transparent;
            btnTabGguf.Foreground = (Brush)FindResource("TextMutedBrush");

            btnAction.Content = "📥 Download Model";
        }

        private void TabGguf_Click(object sender, RoutedEventArgs e)
        {
            _isGgufMode = true;
            panelPull.Visibility = Visibility.Collapsed;
            panelGguf.Visibility = Visibility.Visible;

            btnTabGguf.Background = (Brush)FindResource("AccentPrimaryBrush");
            btnTabGguf.Foreground = Brushes.White;
            btnTabPull.Background = Brushes.Transparent;
            btnTabPull.Foreground = (Brush)FindResource("TextMutedBrush");

            btnAction.Content = "➕ Register Model";
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string modelName)
            {
                txtModelName.Text = modelName;
            }
        }

        private void BrowseGguf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select GGUF Model File",
                Filter = "GGUF Model Files (*.gguf)|*.gguf|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (dlg.ShowDialog() == true)
            {
                txtGgufPath.Text = dlg.FileName;
                var baseName = Path.GetFileNameWithoutExtension(dlg.FileName)
                    .ToLowerInvariant()
                    .Replace(" ", "-")
                    .Replace("_", "-");
                txtGgufModelName.Text = baseName;
            }
        }

        private async void Action_Click(object sender, RoutedEventArgs e)
        {
            if (_isGgufMode)
            {
                await RegisterGgufAsync();
            }
            else
            {
                await PullOllamaModelAsync();
            }
        }

        private async Task RegisterGgufAsync()
        {
            var filePath = txtGgufPath.Text?.Trim();
            var modelName = txtGgufModelName.Text?.Trim();

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("Please select a valid .gguf model file on disk.", "Missing File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(modelName))
            {
                MessageBox.Show("Please provide a name for this model.", "Missing Model Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnAction.IsEnabled = false;
            btnCancel.IsEnabled = false;

            var success = await _localAiService.RegisterGgufModelAsync(modelName, filePath);
            if (success)
            {
                ModelAdded = true;
                AddedModelName = modelName;
                DialogResult = true;
                Close();
            }
            else
            {
                btnAction.IsEnabled = true;
                btnCancel.IsEnabled = true;
                MessageBox.Show("Failed to register GGUF model with Ollama. Make sure Ollama is running.", "Registration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task PullOllamaModelAsync()
        {
            var modelName = txtModelName.Text?.Trim();
            if (string.IsNullOrEmpty(modelName))
            {
                MessageBox.Show("Please enter a valid model name (e.g. gemma:2b, llama3.2).", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnAction.IsEnabled = false;
            prgPull.Visibility = Visibility.Visible;
            prgPull.IsIndeterminate = true;
            _pullCts = new CancellationTokenSource();

            var success = await _localAiService.PullModelAsync(
                modelName,
                status =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        txtStatus.Text = status;
                        if (status.Contains("%"))
                        {
                            prgPull.IsIndeterminate = false;
                        }
                    });
                },
                _pullCts.Token);

            btnAction.IsEnabled = true;
            prgPull.Visibility = Visibility.Collapsed;

            if (success)
            {
                ModelAdded = true;
                AddedModelName = modelName;
                var isCloud = modelName.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) || modelName.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase);
                txtStatus.Text = isCloud ? $"✓ Successfully connected cloud model \"{modelName}\"!" : $"✓ Successfully downloaded \"{modelName}\"!";
                DialogResult = true;
                Close();
            }
            else
            {
                txtStatus.Text = "⚠️ Download failed or was interrupted.";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _pullCts?.Cancel();
            DialogResult = false;
            Close();
        }
    }
}
