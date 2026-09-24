using System;
using System.Windows;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Views
{
    public partial class PasswordDialog : Window
    {
        private readonly PromptFolder? _folder;
        private readonly StorageService? _storageService;
        private readonly Func<string, (bool success, string? error)>? _customValidator;

        public PasswordDialog(PromptFolder folder, StorageService storageService)
        {
            InitializeComponent();
            ThemeService.Instance.ApplyWindowTheme(this);
            _folder = folder;
            _storageService = storageService;

            txtPrompt.Text = $"Enter the password to access \"{_folder.Name}\":";

            Loaded += (s, e) => txtPassword.Focus();
        }

        public PasswordDialog(string title, string promptMessage, Func<string, (bool success, string? error)> validator)
        {
            InitializeComponent();
            ThemeService.Instance.ApplyWindowTheme(this);
            Title = title;
            txtPrompt.Text = promptMessage;
            _customValidator = validator;

            Loaded += (s, e) => txtPassword.Focus();
        }

        private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (txtError.Visibility == Visibility.Visible)
            {
                txtError.Visibility = Visibility.Collapsed;
            }
        }

        private void UnlockButton_Click(object sender, RoutedEventArgs e)
        {
            var password = txtPassword.Password;
            if (string.IsNullOrEmpty(password))
            {
                txtError.Text = "Please enter a password.";
                txtError.Visibility = Visibility.Visible;
                return;
            }

            if (_customValidator != null)
            {
                var (success, error) = _customValidator(password);
                if (success)
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    txtError.Text = error ?? "Incorrect password. Please try again.";
                    txtError.Visibility = Visibility.Visible;
                    txtPassword.SelectAll();
                    txtPassword.Focus();
                }
                return;
            }

            string? errorMessage = null;
            if (_folder != null && _storageService != null && _storageService.TryUnlockFolder(_folder, password, out errorMessage))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                txtError.Text = errorMessage ?? "Incorrect password. Please try again.";
                txtError.Visibility = Visibility.Visible;
                txtPassword.SelectAll();
                txtPassword.Focus();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
