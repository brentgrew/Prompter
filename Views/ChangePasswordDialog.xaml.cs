using System;
using System.Windows;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Views
{
    public partial class ChangePasswordDialog : Window
    {
        private readonly PromptFolder? _folder;
        private readonly StorageService? _storageService;
        private readonly string? _customSalt;
        private readonly string? _customHash;

        public bool RemoveProtectionSelected { get; private set; }
        public string? NewPassword { get; private set; }

        public ChangePasswordDialog(PromptFolder folder, StorageService storageService)
        {
            InitializeComponent();
            ThemeService.Instance.ApplyWindowTheme(this);
            _folder = folder;
            _storageService = storageService;

            if (_folder.IsPasswordProtected)
            {
                txtHeader.Text = $"Security Settings: {_folder.Name}";
                pnlModeSelect.Visibility = Visibility.Visible;
                pnlCurrentPassword.Visibility = Visibility.Visible;
                lblNewPassword.Text = "New Password *";
            }
            else
            {
                txtHeader.Text = $"Add Password: {_folder.Name}";
                pnlModeSelect.Visibility = Visibility.Collapsed;
                pnlCurrentPassword.Visibility = Visibility.Collapsed;
                lblNewPassword.Text = "Set Password *";
            }

            Loaded += (s, e) =>
            {
                if (_folder.IsPasswordProtected)
                {
                    txtCurrentPassword.Focus();
                }
                else
                {
                    txtNewPassword.Focus();
                }
            };
        }

        public ChangePasswordDialog(string headerTitle, string? currentSalt, string? currentHash)
        {
            InitializeComponent();
            ThemeService.Instance.ApplyWindowTheme(this);
            _customSalt = currentSalt;
            _customHash = currentHash;

            txtHeader.Text = headerTitle;
            pnlModeSelect.Visibility = Visibility.Collapsed;
            var isProtected = !string.IsNullOrEmpty(_customSalt) && !string.IsNullOrEmpty(_customHash);
            pnlCurrentPassword.Visibility = isProtected ? Visibility.Visible : Visibility.Collapsed;
            lblNewPassword.Text = isProtected ? "New Password *" : "Set Password *";

            Loaded += (s, e) =>
            {
                if (isProtected) txtCurrentPassword.Focus();
                else txtNewPassword.Focus();
            };
        }

        private void RbMode_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlNewPassword == null) return;
            var isChanging = rbChangePassword.IsChecked == true;
            pnlNewPassword.Visibility = isChanging ? Visibility.Visible : Visibility.Collapsed;
            btnApply.Content = isChanging ? "Update Password" : "Remove Password";
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            txtError.Visibility = Visibility.Collapsed;

            // Handle custom password verification (e.g. for LoRA security)
            if (!string.IsNullOrEmpty(_customSalt) && !string.IsNullOrEmpty(_customHash))
            {
                var currentPwd = txtCurrentPassword.Password;
                if (string.IsNullOrEmpty(currentPwd))
                {
                    ShowError("Please enter the current password.");
                    txtCurrentPassword.Focus();
                    return;
                }

                if (!CryptoService.VerifyPassword(currentPwd, _customSalt, _customHash))
                {
                    ShowError("Current password is incorrect.");
                    txtCurrentPassword.SelectAll();
                    txtCurrentPassword.Focus();
                    return;
                }
            }
            // If folder is currently password-protected, verify current password
            else if (_folder != null && _folder.IsPasswordProtected)
            {
                var currentPwd = txtCurrentPassword.Password;
                if (string.IsNullOrEmpty(currentPwd))
                {
                    ShowError("Please enter the current folder password.");
                    txtCurrentPassword.Focus();
                    return;
                }

                if (!CryptoService.VerifyPassword(currentPwd, _folder.PasswordSalt!, _folder.PasswordHash!))
                {
                    ShowError("Current password is incorrect.");
                    txtCurrentPassword.SelectAll();
                    txtCurrentPassword.Focus();
                    return;
                }

                // If user selected to remove protection
                if (rbRemovePassword.IsChecked == true)
                {
                    RemoveProtectionSelected = true;
                    DialogResult = true;
                    Close();
                    return;
                }
            }

            // Setting or changing password
            var newPwd = txtNewPassword.Password;
            var confirmPwd = txtConfirmPassword.Password;

            if (string.IsNullOrEmpty(newPwd))
            {
                ShowError("Please enter a new password.");
                txtNewPassword.Focus();
                return;
            }

            if (newPwd.Length < 3)
            {
                ShowError("Password must be at least 3 characters.");
                txtNewPassword.Focus();
                return;
            }

            if (newPwd != confirmPwd)
            {
                ShowError("Passwords do not match.");
                txtConfirmPassword.SelectAll();
                txtConfirmPassword.Focus();
                return;
            }

            NewPassword = newPwd;
            RemoveProtectionSelected = false;
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
