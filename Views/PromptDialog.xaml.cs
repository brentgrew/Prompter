using System;
using System.Windows;
using System.Windows.Controls;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Views
{
    public partial class PromptDialog : Window
    {
        public string PromptTitle { get; private set; } = string.Empty;
        public string PromptContent { get; private set; } = string.Empty;

        public PromptDialog(PromptItem? existingPrompt = null)
        {
            InitializeComponent();
            ThemeService.Instance.ApplyWindowTheme(this);

            if (existingPrompt != null)
            {
                txtDialogHeader.Text = "Edit Prompt";
                txtTitle.Text = existingPrompt.Title;
                txtContent.Text = existingPrompt.Content;
            }
            else
            {
                txtDialogHeader.Text = "Add New Prompt";
            }

            Loaded += (s, e) =>
            {
                txtTitle.Focus();
                if (!string.IsNullOrEmpty(txtTitle.Text))
                {
                    txtTitle.SelectAll();
                }
                UpdateStats();
            };
        }

        private void TxtTitle_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtError.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(txtTitle.Text))
            {
                txtError.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtContent_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateStats();
        }

        private void UpdateStats()
        {
            var text = txtContent.Text ?? string.Empty;
            var chars = text.Length;
            var words = string.IsNullOrWhiteSpace(text) ? 0 : text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            txtStats.Text = $"{words} words, {chars} characters";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var title = txtTitle.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                txtError.Text = "A prompt title is required.";
                txtError.Visibility = Visibility.Visible;
                txtTitle.Focus();
                return;
            }

            PromptTitle = title;
            PromptContent = txtContent.Text ?? string.Empty;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
