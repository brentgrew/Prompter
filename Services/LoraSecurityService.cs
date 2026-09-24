using System;
using System.IO;
using System.Text.Json;

namespace Prompter.Services
{
    public class LoraSecurityConfigDto
    {
        public bool IsProtected { get; set; } = true;
        public string? PasswordSalt { get; set; }
        public string? PasswordHash { get; set; }
        public bool HasCustomPassword { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class LoraSecurityService
    {
        private static LoraSecurityService? _instance;
        public static LoraSecurityService Instance => _instance ??= new LoraSecurityService();

        public const string DefaultPassword = "password123";
        private readonly string _settingsFilePath;
        private readonly object _lock = new();

        public bool IsProtected { get; private set; } = true;
        public bool IsUnlocked { get; private set; } = false;
        public string? PasswordSalt { get; private set; }
        public string? PasswordHash { get; private set; }
        public bool HasCustomPassword { get; private set; }

        public event Action<bool>? LockStateChanged;

        public LoraSecurityService(string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath))
            {
                _settingsFilePath = customPath;
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var folder = Path.Combine(appData, "Prompter");
                Directory.CreateDirectory(folder);
                _settingsFilePath = Path.Combine(folder, "lora_security.json");
            }

            Load();
        }

        public void Load()
        {
            lock (_lock)
            {
                if (File.Exists(_settingsFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(_settingsFilePath);
                        var dto = JsonSerializer.Deserialize<LoraSecurityConfigDto>(json);
                        if (dto != null)
                        {
                            IsProtected = dto.IsProtected;
                            PasswordSalt = dto.PasswordSalt;
                            PasswordHash = dto.PasswordHash;
                            HasCustomPassword = dto.HasCustomPassword;
                            return;
                        }
                    }
                    catch
                    {
                        // Fallback to default
                    }
                }

                // First run: Initialize default password protection
                var salt = CryptoService.GenerateSaltBase64();
                var hash = CryptoService.HashPassword(DefaultPassword, salt);
                IsProtected = true;
                PasswordSalt = salt;
                PasswordHash = hash;
                HasCustomPassword = false;
                SaveInternal();
            }
        }

        public bool TryUnlock(string password, out string? errorMessage)
        {
            errorMessage = null;

            if (!IsProtected)
            {
                IsUnlocked = true;
                LockStateChanged?.Invoke(true);
                return true;
            }

            if (string.IsNullOrEmpty(PasswordSalt) || string.IsNullOrEmpty(PasswordHash))
            {
                errorMessage = "Security configuration is missing.";
                return false;
            }

            if (!CryptoService.VerifyPassword(password, PasswordSalt, PasswordHash))
            {
                errorMessage = "Incorrect password. Please try again.";
                return false;
            }

            IsUnlocked = true;
            LockStateChanged?.Invoke(true);
            return true;
        }

        public void Lock()
        {
            IsUnlocked = false;
            LockStateChanged?.Invoke(false);
        }

        public void SetPassword(string newPassword)
        {
            lock (_lock)
            {
                var salt = CryptoService.GenerateSaltBase64();
                var hash = CryptoService.HashPassword(newPassword, salt);

                IsProtected = true;
                PasswordSalt = salt;
                PasswordHash = hash;
                HasCustomPassword = true;
                IsUnlocked = true;

                SaveInternal();
                LockStateChanged?.Invoke(true);
            }
        }

        public bool ChangePassword(string currentPassword, string newPassword, out string? errorMessage)
        {
            errorMessage = null;

            if (IsProtected && !string.IsNullOrEmpty(PasswordSalt) && !string.IsNullOrEmpty(PasswordHash))
            {
                if (!CryptoService.VerifyPassword(currentPassword, PasswordSalt, PasswordHash))
                {
                    errorMessage = "Current password is incorrect.";
                    return false;
                }
            }

            SetPassword(newPassword);
            return true;
        }

        private void SaveInternal()
        {
            var dto = new LoraSecurityConfigDto
            {
                IsProtected = IsProtected,
                PasswordSalt = PasswordSalt,
                PasswordHash = PasswordHash,
                HasCustomPassword = HasCustomPassword,
                UpdatedAt = DateTime.UtcNow
            };

            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            var tempFile = _settingsFilePath + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _settingsFilePath, overwrite: true);
        }
    }
}
