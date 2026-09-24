using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Prompter.Models;
using Prompter.Services;
using Prompter.ViewModels;

namespace Prompter.Views
{
    public partial class LoraManagerDialog : Window
    {
        public LoraManagerViewModel ViewModel { get; }

        public LoraManagerDialog(
            IEnumerable<LoraModelInfo>? currentActiveLoras = null,
            SwarmUiService? swarmUiService = null,
            LoraSecurityService? securityService = null,
            LocalModelInfo? currentModel = null)
        {
            InitializeComponent();
            ViewModel = new LoraManagerViewModel(currentActiveLoras, swarmUiService, securityService, currentModel);
            DataContext = ViewModel;

            Loaded += async (s, e) =>
            {
                await ViewModel.LoadLorasAsync();
            };
        }

        private void SetStrengthPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn &&
                btn.DataContext is LoraModelInfo lora &&
                btn.Tag is string tagStr &&
                double.TryParse(tagStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                lora.Strength = val;
            }
        }

        private void AvailableLora_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.SelectedAvailableLora != null)
            {
                ViewModel.AddLora(ViewModel.SelectedAvailableLora);
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
