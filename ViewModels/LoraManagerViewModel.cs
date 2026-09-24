using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.ViewModels
{
    public class LoraManagerViewModel : INotifyPropertyChanged
    {
        private readonly SwarmUiService _swarmUiService;
        private readonly LoraSecurityService _securityService;
        private string _searchText = string.Empty;
        private string _selectedFolder = "All Folders";
        private LoraModelInfo? _selectedAvailableLora;
        private LoraModelInfo? _selectedActiveLora;
        private bool _isLoading;
        private string _statusMessage = string.Empty;

        public ObservableCollection<LoraModelInfo> AvailableLoras { get; } = new();
        public ObservableCollection<LoraModelInfo> ActiveLoras { get; } = new();
        public ObservableCollection<string> Folders { get; } = new();

        public ICollectionView AvailableLorasView { get; }

        public RelayCommand<LoraModelInfo> AddLoraCommand { get; }
        public RelayCommand<LoraModelInfo> RemoveLoraCommand { get; }
        public RelayCommand ClearAllActiveCommand { get; }
        public RelayCommand RefreshCommand { get; }

        public RelayCommand UnlockNsfwCommand { get; }
        public RelayCommand LockNsfwCommand { get; }
        public RelayCommand ChangeNsfwPasswordCommand { get; }

        public bool IsNsfwUnlocked => _securityService.IsUnlocked;
        public bool IsNsfwLocked => !_securityService.IsUnlocked;
        public string NsfwLockIcon => IsNsfwUnlocked ? "🔓" : "🔒";
        public string NsfwLockStatusText => IsNsfwUnlocked ? "NSFW Unlocked" : "NSFW Locked";
        public string NsfwLockTooltip => IsNsfwUnlocked
            ? "NSFW LoRAs are currently visible. Click to lock."
            : "NSFW LoRAs are password protected. Click to enter password and unlock.";

        public LoraManagerViewModel(
            IEnumerable<LoraModelInfo>? currentActiveLoras = null,
            SwarmUiService? swarmUiService = null,
            LoraSecurityService? securityService = null)
        {
            _swarmUiService = swarmUiService ?? SwarmUiService.Instance;
            _securityService = securityService ?? LoraSecurityService.Instance;

            AvailableLorasView = CollectionViewSource.GetDefaultView(AvailableLoras);
            AvailableLorasView.Filter = FilterAvailableLora;

            // Group by Folder for folder organization
            AvailableLorasView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LoraModelInfo.Folder)));

            // Commands
            AddLoraCommand = new RelayCommand<LoraModelInfo>(
                lora => AddLora(lora ?? SelectedAvailableLora),
                _ => (SelectedAvailableLora != null || AvailableLoras.Count > 0));

            RemoveLoraCommand = new RelayCommand<LoraModelInfo>(
                lora => RemoveLora(lora ?? SelectedActiveLora),
                _ => (SelectedActiveLora != null || ActiveLoras.Count > 0));

            ClearAllActiveCommand = new RelayCommand(ClearAllActive, () => ActiveLoras.Count > 0);
            RefreshCommand = new RelayCommand(async () => await LoadLorasAsync());

            UnlockNsfwCommand = new RelayCommand(ExecuteUnlockNsfw);
            LockNsfwCommand = new RelayCommand(ExecuteLockNsfw);
            ChangeNsfwPasswordCommand = new RelayCommand(ExecuteChangeNsfwPassword);

            _securityService.LockStateChanged += _ => NotifyLockStateChanged();

            ActiveLoras.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(ActiveCount));
                OnPropertyChanged(nameof(HasActiveLoras));
                OnPropertyChanged(nameof(ActiveCountSummary));
                ClearAllActiveCommand.RaiseCanExecuteChanged();
                RemoveLoraCommand.RaiseCanExecuteChanged();
            };

            // Copy initial active LoRAs
            if (currentActiveLoras != null)
            {
                foreach (var item in currentActiveLoras)
                {
                    ActiveLoras.Add(new LoraModelInfo
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Title = item.Title,
                        FilePath = item.FilePath,
                        RelativePath = item.RelativePath,
                        Folder = item.Folder,
                        Category = item.Category,
                        SubCategory = item.SubCategory,
                        Architecture = item.Architecture,
                        Strength = item.Strength,
                        SizeBytes = item.SizeBytes,
                        TriggerPhrase = item.TriggerPhrase,
                        PreviewImageUrl = item.PreviewImageUrl,
                        Tags = new List<string>(item.Tags)
                    });
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    AvailableLorasView.Refresh();
                    OnPropertyChanged(nameof(FilteredAvailableCount));
                }
            }
        }

        public string SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (_selectedFolder != value)
                {
                    // If user selects locked NSFW folder from dropdown, prompt for password
                    if (!string.IsNullOrEmpty(value) &&
                        (value.StartsWith("NSFW", StringComparison.OrdinalIgnoreCase) ||
                         value.Contains("NSFW (Locked)", StringComparison.OrdinalIgnoreCase)) &&
                        !_securityService.IsUnlocked)
                    {
                        var unlocked = PromptUnlockNsfw();
                        if (!unlocked)
                        {
                            OnPropertyChanged();
                            return;
                        }
                    }

                    _selectedFolder = value;
                    OnPropertyChanged();
                    AvailableLorasView.Refresh();
                    OnPropertyChanged(nameof(FilteredAvailableCount));
                }
            }
        }

        public LoraModelInfo? SelectedAvailableLora
        {
            get => _selectedAvailableLora;
            set
            {
                if (_selectedAvailableLora != value)
                {
                    _selectedAvailableLora = value;
                    OnPropertyChanged();
                    AddLoraCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public LoraModelInfo? SelectedActiveLora
        {
            get => _selectedActiveLora;
            set
            {
                if (_selectedActiveLora != value)
                {
                    _selectedActiveLora = value;
                    OnPropertyChanged();
                    RemoveLoraCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public int TotalAvailableCount => AvailableLoras.Count;
        public int FilteredAvailableCount => AvailableLorasView.Cast<object>().Count();
        public int ActiveCount => ActiveLoras.Count;
        public bool HasActiveLoras => ActiveLoras.Count > 0;
        public string ActiveCountSummary => $"{ActiveLoras.Count} Active LoRA{(ActiveLoras.Count == 1 ? "" : "s")}";

        public async Task LoadLorasAsync()
        {
            IsLoading = true;
            StatusMessage = "Scanning LoRA models from SwarmUI...";
            try
            {
                var loras = await _swarmUiService.ListLorasAsync();

                AvailableLoras.Clear();
                foreach (var lora in loras)
                {
                    AvailableLoras.Add(lora);
                }

                RebuildFoldersList();
                AvailableLorasView.Refresh();

                StatusMessage = $"Found {AvailableLoras.Count} LoRA models.";
                OnPropertyChanged(nameof(TotalAvailableCount));
                OnPropertyChanged(nameof(FilteredAvailableCount));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load LoRAs: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void RebuildFoldersList()
        {
            var folderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasNsfw = false;

            foreach (var l in AvailableLoras)
            {
                if (!string.IsNullOrEmpty(l.Folder) && !l.Folder.Equals("Root", StringComparison.OrdinalIgnoreCase))
                {
                    if (l.IsNsfw)
                    {
                        hasNsfw = true;
                        if (_securityService.IsUnlocked)
                        {
                            folderSet.Add(l.Folder);
                        }
                    }
                    else
                    {
                        folderSet.Add(l.Folder);
                    }
                }
            }

            var previous = _selectedFolder;
            Folders.Clear();
            Folders.Add("All Folders");

            if (hasNsfw && !_securityService.IsUnlocked)
            {
                Folders.Add("🔒 NSFW (Locked)");
            }

            foreach (var f in folderSet.OrderBy(x => x))
            {
                Folders.Add(f);
            }

            if (Folders.Contains(previous))
            {
                _selectedFolder = previous;
            }
            else
            {
                _selectedFolder = "All Folders";
            }
            OnPropertyChanged(nameof(SelectedFolder));
        }

        public void AddLora(LoraModelInfo? lora)
        {
            if (lora == null) return;

            // NSFW lock verification
            if (lora.IsNsfw && !_securityService.IsUnlocked)
            {
                if (!PromptUnlockNsfw())
                    return;
            }

            // Check if already in ActiveLoras
            var existing = ActiveLoras.FirstOrDefault(l =>
                l.RelativePath.Equals(lora.RelativePath, StringComparison.OrdinalIgnoreCase) ||
                l.Id.Equals(lora.Id, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                SelectedActiveLora = existing;
                return;
            }

            var activeCopy = new LoraModelInfo
            {
                Id = lora.Id,
                Name = lora.Name,
                Title = lora.Title,
                FilePath = lora.FilePath,
                RelativePath = lora.RelativePath,
                Folder = lora.Folder,
                Category = lora.Category,
                SubCategory = lora.SubCategory,
                Architecture = lora.Architecture,
                Strength = 1.0, // Default 1.0 as requested
                SizeBytes = lora.SizeBytes,
                TriggerPhrase = lora.TriggerPhrase,
                PreviewImageUrl = lora.PreviewImageUrl,
                Tags = new List<string>(lora.Tags)
            };

            ActiveLoras.Add(activeCopy);
            SelectedActiveLora = activeCopy;
        }

        public void RemoveLora(LoraModelInfo? lora)
        {
            if (lora == null) return;
            ActiveLoras.Remove(lora);
            if (SelectedActiveLora == lora)
            {
                SelectedActiveLora = ActiveLoras.LastOrDefault();
            }
        }

        public void ClearAllActive()
        {
            ActiveLoras.Clear();
            SelectedActiveLora = null;
        }

        public bool PromptUnlockNsfw()
        {
            if (_securityService.IsUnlocked) return true;

            var dlg = new Views.PasswordDialog(
                "Unlock NSFW LoRA Folder",
                "Enter password to unlock and access NSFW LoRA models:",
                pwd =>
                {
                    var success = _securityService.TryUnlock(pwd, out var err);
                    return (success, err);
                })
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            var result = dlg.ShowDialog() == true;
            if (result)
            {
                StatusMessage = "🔓 NSFW LoRAs unlocked for this session.";
                NotifyLockStateChanged();
            }
            return result;
        }

        private void ExecuteUnlockNsfw()
        {
            PromptUnlockNsfw();
        }

        private void ExecuteLockNsfw()
        {
            _securityService.Lock();
            if (SelectedFolder.StartsWith("NSFW", StringComparison.OrdinalIgnoreCase))
            {
                SelectedFolder = "All Folders";
            }
            StatusMessage = "🔒 NSFW LoRA folder locked.";
            NotifyLockStateChanged();
        }

        private void ExecuteChangeNsfwPassword()
        {
            var dlg = new Views.ChangePasswordDialog(
                "NSFW LoRA Folder Password",
                _securityService.PasswordSalt,
                _securityService.PasswordHash)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.NewPassword))
            {
                _securityService.SetPassword(dlg.NewPassword);
                StatusMessage = "✓ NSFW LoRA password updated successfully.";
                NotifyLockStateChanged();
            }
        }

        private void NotifyLockStateChanged()
        {
            OnPropertyChanged(nameof(IsNsfwUnlocked));
            OnPropertyChanged(nameof(IsNsfwLocked));
            OnPropertyChanged(nameof(NsfwLockIcon));
            OnPropertyChanged(nameof(NsfwLockStatusText));
            OnPropertyChanged(nameof(NsfwLockTooltip));
            RebuildFoldersList();
            AvailableLorasView.Refresh();
            OnPropertyChanged(nameof(FilteredAvailableCount));
        }

        private bool FilterAvailableLora(object obj)
        {
            if (obj is not LoraModelInfo lora) return false;

            // NSFW lock check: completely hide NSFW LoRAs while locked
            if (lora.IsNsfw && !_securityService.IsUnlocked)
            {
                return false;
            }

            // Folder filter
            if (!string.IsNullOrEmpty(SelectedFolder) &&
                !SelectedFolder.Equals("All Folders", StringComparison.OrdinalIgnoreCase))
            {
                if (!lora.Folder.Equals(SelectedFolder, StringComparison.OrdinalIgnoreCase) &&
                    !lora.Folder.StartsWith(SelectedFolder + "/", StringComparison.OrdinalIgnoreCase) &&
                    !lora.Category.Equals(SelectedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Search query filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var query = SearchText.Trim();
                var nameMatch = lora.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                var titleMatch = lora.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
                var fileMatch = lora.FileName.Contains(query, StringComparison.OrdinalIgnoreCase);
                var relMatch = lora.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase);
                var folderMatch = lora.Folder.Contains(query, StringComparison.OrdinalIgnoreCase);
                var triggerMatch = lora.TriggerPhrase != null && lora.TriggerPhrase.Contains(query, StringComparison.OrdinalIgnoreCase);
                var archMatch = lora.Architecture.Contains(query, StringComparison.OrdinalIgnoreCase);

                if (!nameMatch && !titleMatch && !fileMatch && !relMatch && !folderMatch && !triggerMatch && !archMatch)
                {
                    return false;
                }
            }

            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
