using System;
using System.Windows;
using System.Windows.Controls;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Views
{
    public partial class FolderDialog : Window
    {
        public string FolderName { get; private set; } = string.Empty;
        public bool IsPasswordProtected { get; private set; }
        public string? Password { get; private set; }

        public FolderDialog(PromptFolder? existingFolder = null)
        {
            InitializeComponent();
            ThemeService.Instance.ApplyWindowTheme(this);

            if (existingFolder != null)
            {
                txtDialogHeader.Text = "Rename Folder";
                txtFolderName.Text = existingFolder.Name;
                chkPasswordProtect.IsEnabled = false; // Existing protection changed through dedicated change password dialog
                chkPasswordProtect.IsChecked = existingFolder.IsPasswordProtected;
            }
            else
            {
                txtDialogHeader.Text = "Create New Folder";
            }

            Loaded += (s, e) =>
            {
                txtFolderName.Focus();
                if (!string.IsNullOrEmpty(txtFolderName.Text))
                {
                    txtFolderName.SelectAll();
                }
            };
        }

        private void ChkPasswordProtect_Changed(object sender, RoutedEventArgs e)
        {
            if (pnlPasswordFields == null) return;
            var isChecked = chkPasswordProtect.IsChecked == true;
            pnlPasswordFields.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtFolderName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtError.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(txtFolderName.Text))
            {
                txtError.Visibility = Visibility.Collapsed;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var name = txtFolderName.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError("Please enter a folder name.");
                txtFolderName.Focus();
                return;
            }

            if (chkPasswordProtect.IsChecked == true && chkPasswordProtect.IsEnabled)
            {
                var pwd = txtPassword.Password;
                var confirmPwd = txtConfirmPassword.Password;

                if (string.IsNullOrEmpty(pwd))
                {
                    ShowError("Please enter a password for this folder.");
                    txtPassword.Focus();
                    return;
                }

                if (pwd.Length < 3)
                {
                    ShowError("Password must be at least 3 characters long.");
                    txtPassword.Focus();
                    return;
                }

                if (pwd != confirmPwd)
                {
                    ShowError("Passwords do not match. Please verify.");
                    txtConfirmPassword.Focus();
                    return;
                }

                Password = pwd;
                IsPasswordProtected = true;
            }
            else
            {
                IsPasswordProtected = chkPasswordProtect.IsChecked == true;
            }

            FolderName = name;
            DialogResult = true;
            Close();
        }

        private void ShowError(string message)
        {
            txtError.Text = message;
            txtError.Visibility = Visibility.Visible;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
