using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Prompter.Models;
using Prompter.Services;
using Prompter.Views;

namespace Prompter.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly StorageService _storageService;
        private readonly LocalAiService _localAiService;
        private readonly SwarmUiService _swarmUiService;
        private readonly DispatcherTimer _statusTimer;

        private PromptFolder? _selectedFolder;
        private PromptItem? _selectedPrompt;
        private string _searchText = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isStatusVisible;
        private ICollectionView? _filteredPrompts;

        // Local AI Chat & Image Generation Fields
        private CancellationTokenSource? _chatCts;
        private LocalModelInfo? _selectedModel;
        private LocalModelInfo? _selectedChatModel;
        private LocalModelInfo? _selectedImageModel;
        private string _activeMode = "Chat"; // "Chat" or "Image"
        private string _chatInputText = string.Empty;
        private bool _isChatGenerating;
        private string _ollamaStatusText = "Checking Ollama...";
        private bool _isOllamaConnected;
        private string _swarmStatusText = "Checking SwarmUI...";
        private bool _isSwarmConnected;
        private string _activeTaskStatus = string.Empty;

        public MainViewModel()
        {
            _storageService = new StorageService();
            _localAiService = LocalAiService.Instance;
            _swarmUiService = SwarmUiService.Instance;
            Folders = _storageService.LoadVault();

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _statusTimer.Tick += (s, e) =>
            {
                _statusTimer.Stop();
                IsStatusVisible = false;
            };

            // Commands
            AddFolderCommand = new RelayCommand(ExecuteAddFolder);
            RenameFolderCommand = new RelayCommand(ExecuteRenameFolder, () => SelectedFolder != null);
            DeleteFolderCommand = new RelayCommand(ExecuteDeleteFolder, () => SelectedFolder != null);
            ToggleLockFolderCommand = new RelayCommand(ExecuteToggleLockFolder, () => SelectedFolder != null && SelectedFolder.IsPasswordProtected);
            LockAllFoldersCommand = new RelayCommand(ExecuteLockAllFolders);
            ChangeFolderPasswordCommand = new RelayCommand(ExecuteChangeFolderPassword, () => SelectedFolder != null);

            AddPromptCommand = new RelayCommand(ExecuteAddPrompt, () => SelectedFolder != null && SelectedFolder.CanAccessPrompts);
            DeletePromptCommand = new RelayCommand(ExecuteDeletePrompt, () => SelectedPrompt != null);
            CopyPromptCommand = new RelayCommand(ExecuteCopyPrompt);
            SaveCurrentPromptCommand = new RelayCommand(ExecuteSaveCurrentPrompt, () => SelectedPrompt != null);
            ToggleThemeCommand = new RelayCommand(ExecuteToggleTheme);

            // Local AI Chat & Image Commands
            SendChatMessageCommand = new RelayCommand(ExecuteSendChatMessage, () => !IsChatGenerating && !string.IsNullOrWhiteSpace(ChatInputText));
            StopGenerationCommand = new RelayCommand(ExecuteStopGeneration, () => IsChatGenerating);
            ClearChatCommand = new RelayCommand(ExecuteClearChat);
            ScanModelsCommand = new RelayCommand(async () => await ScanLocalModelsAsync());
            AddModelCommand = new RelayCommand(ExecuteAddModel);
            InsertActivePromptCommand = new RelayCommand(ExecuteInsertActivePrompt, () => SelectedPrompt != null && !string.IsNullOrWhiteSpace(SelectedPrompt.Content));
            StartOllamaCommand = new RelayCommand(ExecuteStartOllama);
            StartSwarmCommand = new RelayCommand(ExecuteStartSwarm);
            OpenSwarmOutputsCommand = new RelayCommand(ExecuteOpenSwarmOutputs);
            SwitchToChatModeCommand = new RelayCommand(() => ActiveMode = "Chat");
            SwitchToImageModeCommand = new RelayCommand(() => ActiveMode = "Image");
            ToggleModeCommand = new RelayCommand(() => ActiveMode = IsChatMode ? "Image" : "Chat");
            CopyImageToClipboardCommand = new RelayCommand(p => CopyImageToClipboard(p as string));
            OpenImageFileCommand = new RelayCommand(p => OpenImageFile(p as string));
            ShowImageInFolderCommand = new RelayCommand(p => ShowImageInFolder(p as string));
            SetDefaultModelCommand = new RelayCommand(p => ExecuteSetDefaultModel(p as LocalModelInfo));

            ThemeService.Instance.ThemeChanged += _ =>
            {
                OnPropertyChanged(nameof(ThemeToggleIcon));
                OnPropertyChanged(nameof(ThemeToggleText));
            };

            // Select initial folder
            if (Folders.Count > 0)
            {
                SelectedFolder = Folders[0];
            }

            // Welcome chat message
            ChatMessages.Add(new ChatMessage("Assistant", "👋 Welcome to Prompter Studio! Chat with local LLMs (Qwen, Gemma, Llama, DeepSeek) or generate images with SwarmUI (SDXL, Pony, DreamShaper) completely offline."));

            // Kick off async local model discovery
            _ = ScanLocalModelsAsync();
        }

        public ObservableCollection<PromptFolder> Folders { get; }
        public ObservableCollection<ChatMessage> ChatMessages { get; } = new();
        public ObservableCollection<LocalModelInfo> AvailableModels { get; } = new();
        public ObservableCollection<LocalModelInfo> AvailableChatModels { get; } = new();
        public ObservableCollection<LocalModelInfo> AvailableImageModels { get; } = new();

        public PromptFolder? SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (_selectedFolder != value)
                {
                    if (_selectedFolder != null)
                    {
                        _selectedFolder.IsSelected = false;
                    }

                    _selectedFolder = value;

                    if (_selectedFolder != null)
                    {
                        _selectedFolder.IsSelected = true;
                    }

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsFolderSelected));
                    OnPropertyChanged(nameof(IsFolderLocked));
                    OnPropertyChanged(nameof(CanAccessPrompts));
                    UpdatePromptsView();

                    // Auto-select first prompt in folder if available
                    SelectedPrompt = (_selectedFolder != null && _selectedFolder.CanAccessPrompts && _selectedFolder.Prompts.Count > 0)
                        ? _selectedFolder.Prompts[0]
                        : null;
                }
            }
        }

        public PromptItem? SelectedPrompt
        {
            get => _selectedPrompt;
            set
            {
                if (_selectedPrompt != value)
                {
                    _selectedPrompt = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPromptSelected));
                    InsertActivePromptCommand?.RaiseCanExecuteChanged();
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
                    _filteredPrompts?.Refresh();
                }
            }
        }

        public ICollectionView? FilteredPrompts
        {
            get => _filteredPrompts;
            private set
            {
                _filteredPrompts = value;
                OnPropertyChanged();
            }
        }

        public string ActiveMode
        {
            get => _activeMode;
            set
            {
                if (_activeMode != value)
                {
                    _activeMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsChatMode));
                    OnPropertyChanged(nameof(IsImageMode));
                    OnPropertyChanged(nameof(CurrentModel));
                    OnPropertyChanged(nameof(ActiveModelDisplayName));
                    OnPropertyChanged(nameof(ActiveModeIcon));
                    OnPropertyChanged(nameof(InputPlaceholderText));
                    // Update active model for binding
                    _selectedModel = IsImageMode ? SelectedImageModel : SelectedChatModel;
                    OnPropertyChanged(nameof(SelectedModel));
                }
            }
        }

        public bool IsChatMode => string.Equals(ActiveMode, "Chat", StringComparison.OrdinalIgnoreCase);
        public bool IsImageMode => string.Equals(ActiveMode, "Image", StringComparison.OrdinalIgnoreCase);
        public string ActiveModeIcon => IsImageMode ? "🎨" : "💬";

        public LocalModelInfo? SelectedChatModel
        {
            get => _selectedChatModel;
            set
            {
                if (_selectedChatModel != value)
                {
                    _selectedChatModel = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentModel));
                    OnPropertyChanged(nameof(ActiveModelDisplayName));
                    if (IsChatMode)
                    {
                        _selectedModel = value;
                        OnPropertyChanged(nameof(SelectedModel));
                        SaveLastSelectedModel(_selectedChatModel?.ModelId);
                    }
                }
            }
        }

        public LocalModelInfo? SelectedImageModel
        {
            get => _selectedImageModel;
            set
            {
                if (_selectedImageModel != value)
                {
                    _selectedImageModel = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentModel));
                    OnPropertyChanged(nameof(ActiveModelDisplayName));
                    if (IsImageMode)
                    {
                        _selectedModel = value;
                        OnPropertyChanged(nameof(SelectedModel));
                        SaveLastSelectedImageModel(_selectedImageModel?.ModelId);
                    }
                }
            }
        }

        public LocalModelInfo? CurrentModel => IsImageMode ? SelectedImageModel : (SelectedChatModel ?? SelectedModel);

        public string ActiveModelDisplayName => IsImageMode
            ? (SelectedImageModel?.DisplayName ?? "Select Image Model")
            : (SelectedChatModel?.DisplayName ?? SelectedModel?.DisplayName ?? "Select Chat Model");

        public LocalModelInfo? SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_selectedModel != value)
                {
                    _selectedModel = value;
                    OnPropertyChanged();
                    if (_selectedModel != null)
                    {
                        if (_selectedModel.IsImageModel)
                        {
                            SelectedImageModel = _selectedModel;
                            if (!IsImageMode) ActiveMode = "Image";
                        }
                        else
                        {
                            SelectedChatModel = _selectedModel;
                            if (!IsChatMode) ActiveMode = "Chat";
                        }
                    }
                    OnPropertyChanged(nameof(CurrentModel));
                    OnPropertyChanged(nameof(ActiveModelDisplayName));
                }
            }
        }

        public string ChatInputText
        {
            get => _chatInputText;
            set
            {
                if (_chatInputText != value)
                {
                    _chatInputText = value;
                    OnPropertyChanged();
                    SendChatMessageCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsChatGenerating
        {
            get => _isChatGenerating;
            set
            {
                if (_isChatGenerating != value)
                {
                    _isChatGenerating = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanSendChat));
                    OnPropertyChanged(nameof(HasActiveTask));
                    SendChatMessageCommand?.RaiseCanExecuteChanged();
                    StopGenerationCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanSendChat => !IsChatGenerating;

        public string ActiveTaskStatus
        {
            get => _activeTaskStatus;
            set
            {
                if (_activeTaskStatus != value)
                {
                    _activeTaskStatus = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasActiveTask));
                }
            }
        }

        public bool HasActiveTask => !string.IsNullOrWhiteSpace(_activeTaskStatus) || IsChatGenerating;

        public string InputPlaceholderText => IsImageMode
            ? "Describe image prompt for SwarmUI... (Enter to generate)"
            : "Ask local AI, prompt for image, or use active prompt... (Enter to send, Shift+Enter for new line)";

        public string OllamaStatusText
        {
            get => _ollamaStatusText;
            set
            {
                if (_ollamaStatusText != value)
                {
                    _ollamaStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsOllamaConnected
        {
            get => _isOllamaConnected;
            set
            {
                if (_isOllamaConnected != value)
                {
                    _isOllamaConnected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SwarmStatusText
        {
            get => _swarmStatusText;
            set
            {
                if (_swarmStatusText != value)
                {
                    _swarmStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSwarmConnected
        {
            get => _isSwarmConnected;
            set
            {
                if (_isSwarmConnected != value)
                {
                    _isSwarmConnected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsStatusVisible
        {
            get => _isStatusVisible;
            set { _isStatusVisible = value; OnPropertyChanged(); }
        }

        public bool IsFolderSelected => SelectedFolder != null;
        public bool IsPromptSelected => SelectedPrompt != null;
        public bool IsFolderLocked => SelectedFolder != null && SelectedFolder.IsPasswordProtected && !SelectedFolder.IsUnlocked;
        public bool CanAccessPrompts => SelectedFolder != null && SelectedFolder.CanAccessPrompts;

        #region Commands
        public RelayCommand AddFolderCommand { get; }
        public RelayCommand RenameFolderCommand { get; }
        public RelayCommand DeleteFolderCommand { get; }
        public RelayCommand ToggleLockFolderCommand { get; }
        public RelayCommand LockAllFoldersCommand { get; }
        public RelayCommand ChangeFolderPasswordCommand { get; }

        public RelayCommand AddPromptCommand { get; }
        public RelayCommand DeletePromptCommand { get; }
        public RelayCommand CopyPromptCommand { get; }
        public RelayCommand SaveCurrentPromptCommand { get; }
        public RelayCommand ToggleThemeCommand { get; }

        // Local AI Chat & Image Commands
        public RelayCommand SendChatMessageCommand { get; }
        public RelayCommand StopGenerationCommand { get; }
        public RelayCommand ClearChatCommand { get; }
        public RelayCommand ScanModelsCommand { get; }
        public RelayCommand AddModelCommand { get; }
        public RelayCommand InsertActivePromptCommand { get; }
        public RelayCommand StartOllamaCommand { get; }
        public RelayCommand StartSwarmCommand { get; }
        public RelayCommand OpenSwarmOutputsCommand { get; }
        public RelayCommand SwitchToChatModeCommand { get; }
        public RelayCommand SwitchToImageModeCommand { get; }
        public RelayCommand ToggleModeCommand { get; }
        public RelayCommand CopyImageToClipboardCommand { get; }
        public RelayCommand OpenImageFileCommand { get; }
        public RelayCommand ShowImageInFolderCommand { get; }
        public RelayCommand SetDefaultModelCommand { get; }

        public string ThemeToggleIcon => ThemeService.Instance.CurrentTheme == AppTheme.Dark ? "☀️" : "🌙";
        public string ThemeToggleText => ThemeService.Instance.CurrentTheme == AppTheme.Dark ? "Light" : "Dark";
        #endregion

        private void ExecuteToggleTheme()
        {
            ThemeService.Instance.ToggleTheme();
            ShowStatus(ThemeService.Instance.CurrentTheme == AppTheme.Dark ? "🌙 Dark theme activated" : "☀️ Light theme activated");
        }

        private void UpdatePromptsView()
        {
            if (SelectedFolder == null || !SelectedFolder.CanAccessPrompts)
            {
                FilteredPrompts = null;
                return;
            }

            var view = CollectionViewSource.GetDefaultView(SelectedFolder.Prompts);
            view.Filter = item =>
            {
                if (string.IsNullOrWhiteSpace(_searchText)) return true;
                if (item is PromptItem prompt)
                {
                    return prompt.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                           prompt.Content.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            };
            FilteredPrompts = view;
        }

        public void ShowStatus(string message)
        {
            StatusMessage = message;
            IsStatusVisible = true;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        public void CopyPromptToClipboard(PromptItem prompt)
        {
            if (prompt == null) return;
            try
            {
                Clipboard.SetText(prompt.Content ?? string.Empty);
                ShowStatus($"✓ Copied \"{prompt.DisplayTitle}\" to clipboard!");
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to copy: {ex.Message}");
            }
        }

        private void ExecuteCopyPrompt(object? parameter)
        {
            var prompt = parameter as PromptItem ?? SelectedPrompt;
            if (prompt != null)
            {
                CopyPromptToClipboard(prompt);
            }
        }

        private void ExecuteSaveCurrentPrompt()
        {
            if (SelectedPrompt != null)
            {
                SelectedPrompt.UpdatedAt = DateTime.Now;
                _storageService.SaveVault(Folders);
                ShowStatus("✓ Prompt saved successfully!");
            }
        }

        public void UnlockFolder(PromptFolder folder)
        {
            if (!folder.IsPasswordProtected || folder.IsUnlocked) return;

            var dialog = new PasswordDialog(folder, _storageService);
            SetDialogOwner(dialog);

            if (dialog.ShowDialog() == true)
            {
                OnPropertyChanged(nameof(IsFolderLocked));
                OnPropertyChanged(nameof(CanAccessPrompts));
                UpdatePromptsView();
                if (folder.Prompts.Count > 0)
                {
                    SelectedPrompt = folder.Prompts[0];
                }
                ShowStatus($"✓ \"{folder.Name}\" unlocked.");
            }
        }

        private void ExecuteToggleLockFolder(object? parameter)
        {
            var folder = parameter as PromptFolder ?? SelectedFolder;
            if (folder == null || !folder.IsPasswordProtected) return;

            if (folder.IsUnlocked)
            {
                // Lock it
                _storageService.SaveVault(Folders); // Ensure latest state is encrypted and saved
                folder.Lock();
                if (SelectedFolder == folder)
                {
                    SelectedPrompt = null;
                    UpdatePromptsView();
                    OnPropertyChanged(nameof(IsFolderLocked));
                    OnPropertyChanged(nameof(CanAccessPrompts));
                }
                ShowStatus($"🔒 \"{folder.Name}\" locked.");
            }
            else
            {
                UnlockFolder(folder);
            }
        }

        private void ExecuteLockAllFolders()
        {
            _storageService.SaveVault(Folders);
            int count = 0;
            foreach (var f in Folders)
            {
                if (f.IsPasswordProtected && f.IsUnlocked)
                {
                    f.Lock();
                    count++;
                }
            }

            if (SelectedFolder != null && SelectedFolder.IsPasswordProtected)
            {
                SelectedPrompt = null;
                UpdatePromptsView();
                OnPropertyChanged(nameof(IsFolderLocked));
                OnPropertyChanged(nameof(CanAccessPrompts));
            }

            ShowStatus(count > 0 ? $"🔒 Locked {count} protected folder(s)." : "All protected folders are already locked.");
        }

        private static void SetDialogOwner(Window dialog)
        {
            if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                dialog.Owner = Application.Current.MainWindow;
            }
        }

        private void ExecuteAddFolder()
        {
            var dialog = new FolderDialog();
            SetDialogOwner(dialog);

            if (dialog.ShowDialog() == true)
            {
                var newFolder = new PromptFolder(dialog.FolderName, dialog.IsPasswordProtected);

                if (dialog.IsPasswordProtected && !string.IsNullOrEmpty(dialog.Password))
                {
                    var salt = CryptoService.GenerateSaltBase64();
                    var hash = CryptoService.HashPassword(dialog.Password, salt);
                    newFolder.PasswordSalt = salt;
                    newFolder.PasswordHash = hash;
                    newFolder.SessionPassword = dialog.Password;
                    newFolder.IsUnlocked = true;
                }

                Folders.Add(newFolder);
                SelectedFolder = newFolder;
                _storageService.SaveVault(Folders);
                ShowStatus($"✓ Created folder \"{newFolder.Name}\".");
            }
        }

        private void ExecuteRenameFolder()
        {
            if (SelectedFolder == null) return;

            var dialog = new FolderDialog(SelectedFolder);
            SetDialogOwner(dialog);

            if (dialog.ShowDialog() == true)
            {
                SelectedFolder.Name = dialog.FolderName;
                _storageService.SaveVault(Folders);
                ShowStatus($"✓ Renamed folder to \"{SelectedFolder.Name}\".");
            }
        }

        private void ExecuteDeleteFolder()
        {
            if (SelectedFolder == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete folder \"{SelectedFolder.Name}\" and all of its prompts?\nThis action cannot be undone.",
                "Delete Folder",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var folderName = SelectedFolder.Name;
                var index = Folders.IndexOf(SelectedFolder);
                Folders.Remove(SelectedFolder);
                SelectedFolder = Folders.Count > 0 ? Folders[Math.Max(0, index - 1)] : null;
                _storageService.SaveVault(Folders);
                ShowStatus($"✓ Deleted folder \"{folderName}\".");
            }
        }

        private void ExecuteChangeFolderPassword()
        {
            if (SelectedFolder == null) return;

            if (SelectedFolder.IsPasswordProtected && !SelectedFolder.IsUnlocked)
            {
                // Must unlock first
                UnlockFolder(SelectedFolder);
                if (!SelectedFolder.IsUnlocked) return;
            }

            var dialog = new ChangePasswordDialog(SelectedFolder, _storageService);
            SetDialogOwner(dialog);

            if (dialog.ShowDialog() == true)
            {
                if (dialog.RemoveProtectionSelected)
                {
                    SelectedFolder.IsPasswordProtected = false;
                    SelectedFolder.PasswordSalt = null;
                    SelectedFolder.PasswordHash = null;
                    SelectedFolder.EncryptedPayload = null;
                    SelectedFolder.SessionPassword = null;
                    SelectedFolder.IsUnlocked = true;
                    _storageService.SaveVault(Folders);
                    ShowStatus($"✓ Removed password protection from \"{SelectedFolder.Name}\".");
                }
                else if (!string.IsNullOrEmpty(dialog.NewPassword))
                {
                    var salt = CryptoService.GenerateSaltBase64();
                    var hash = CryptoService.HashPassword(dialog.NewPassword, salt);
                    SelectedFolder.IsPasswordProtected = true;
                    SelectedFolder.PasswordSalt = salt;
                    SelectedFolder.PasswordHash = hash;
                    SelectedFolder.SessionPassword = dialog.NewPassword;
                    SelectedFolder.IsUnlocked = true;
                    _storageService.SaveVault(Folders);
                    ShowStatus($"✓ Password updated for \"{SelectedFolder.Name}\".");
                }
            }
        }

        private void ExecuteAddPrompt()
        {
            if (SelectedFolder == null || !SelectedFolder.CanAccessPrompts) return;

            var dialog = new PromptDialog();
            SetDialogOwner(dialog);

            if (dialog.ShowDialog() == true)
            {
                var newPrompt = new PromptItem(dialog.PromptTitle, dialog.PromptContent);
                SelectedFolder.Prompts.Insert(0, newPrompt);
                SelectedPrompt = newPrompt;
                _storageService.SaveVault(Folders);
                ShowStatus($"✓ Added prompt \"{newPrompt.DisplayTitle}\".");
            }
        }

        private void ExecuteDeletePrompt(object? parameter = null)
        {
            var prompt = parameter as PromptItem ?? SelectedPrompt;
            if (prompt != null)
            {
                DeletePrompt(prompt);
            }
        }

        public void DeletePrompt(PromptItem prompt)
        {
            if (SelectedFolder == null || prompt == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete prompt \"{prompt.DisplayTitle}\"?\n\nThis action cannot be undone.",
                "Delete Prompt Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var promptTitle = prompt.DisplayTitle;
                var index = SelectedFolder.Prompts.IndexOf(prompt);
                SelectedFolder.Prompts.Remove(prompt);

                if (SelectedPrompt == prompt)
                {
                    SelectedPrompt = SelectedFolder.Prompts.Count > 0
                        ? SelectedFolder.Prompts[Math.Clamp(index, 0, SelectedFolder.Prompts.Count - 1)]
                        : null;
                }

                _storageService.SaveVault(Folders);
                ShowStatus($"✓ Deleted prompt \"{promptTitle}\".");
            }
        }

        #region Local AI Chat & SwarmUI Image Methods
        public async Task ScanLocalModelsAsync()
        {
            OllamaStatusText = "Scanning Ollama...";
            SwarmStatusText = "Scanning SwarmUI...";

            // 1. Scan Ollama models
            var isOllamaRunning = await _localAiService.IsOllamaRunningAsync();
            IsOllamaConnected = isOllamaRunning;
            var chatModels = await _localAiService.ScanModelsAsync();

            // 2. Scan SwarmUI models (API + X:\SwarmUI\Models\Stable-Diffusion)
            var isSwarmRunning = await _swarmUiService.IsSwarmRunningAsync();
            IsSwarmConnected = isSwarmRunning;
            var imageModels = await _swarmUiService.ListModelsAsync();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                AvailableChatModels.Clear();
                foreach (var m in chatModels) AvailableChatModels.Add(m);

                AvailableImageModels.Clear();
                foreach (var m in imageModels) AvailableImageModels.Add(m);

                AvailableModels.Clear();
                foreach (var m in chatModels) AvailableModels.Add(m);
                foreach (var m in imageModels) AvailableModels.Add(m);

                // Restore default or last chat model
                var defaultChatModelId = LoadDefaultChatModel();
                var savedChatModelId = LoadLastSelectedModel();

                foreach (var m in AvailableChatModels)
                {
                    m.IsDefault = !string.IsNullOrEmpty(defaultChatModelId) &&
                                  (string.Equals(m.ModelId, defaultChatModelId, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(m.Name, defaultChatModelId, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrEmpty(defaultChatModelId))
                {
                    SelectedChatModel = AvailableChatModels.FirstOrDefault(m => m.IsDefault);
                }
                if (SelectedChatModel == null && !string.IsNullOrEmpty(savedChatModelId))
                {
                    SelectedChatModel = AvailableChatModels.FirstOrDefault(m => string.Equals(m.ModelId, savedChatModelId, StringComparison.OrdinalIgnoreCase)
                                                                             || string.Equals(m.Name, savedChatModelId, StringComparison.OrdinalIgnoreCase));
                }
                if (SelectedChatModel == null)
                {
                    SelectedChatModel = AvailableChatModels.FirstOrDefault(m => m.IsQwen)
                                     ?? AvailableChatModels.FirstOrDefault(m => m.IsGemma)
                                     ?? AvailableChatModels.FirstOrDefault();
                }

                // Restore default or last image model
                var defaultImageModelId = LoadDefaultImageModel();
                var savedImageModelId = LoadLastSelectedImageModel();

                foreach (var m in AvailableImageModels)
                {
                    m.IsDefault = !string.IsNullOrEmpty(defaultImageModelId) &&
                                  (string.Equals(m.ModelId, defaultImageModelId, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(m.Name, defaultImageModelId, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrEmpty(defaultImageModelId))
                {
                    SelectedImageModel = AvailableImageModels.FirstOrDefault(m => m.IsDefault);
                }
                if (SelectedImageModel == null && !string.IsNullOrEmpty(savedImageModelId))
                {
                    SelectedImageModel = AvailableImageModels.FirstOrDefault(m => string.Equals(m.ModelId, savedImageModelId, StringComparison.OrdinalIgnoreCase)
                                                                               || string.Equals(m.Name, savedImageModelId, StringComparison.OrdinalIgnoreCase));
                }
                if (SelectedImageModel == null)
                {
                    SelectedImageModel = AvailableImageModels.FirstOrDefault(m => m.IsSdxl)
                                      ?? AvailableImageModels.FirstOrDefault(m => m.IsSd15)
                                      ?? AvailableImageModels.FirstOrDefault();
                }

                SelectedModel = IsImageMode ? SelectedImageModel : SelectedChatModel;
            });

            if (isOllamaRunning)
            {
                OllamaStatusText = $"🟢 Ollama Online ({AvailableChatModels.Count} model{(AvailableChatModels.Count == 1 ? "" : "s")})";
            }
            else
            {
                OllamaStatusText = AvailableChatModels.Count > 0
                    ? $"🔴 Ollama Offline ({AvailableChatModels.Count} cached - Click to Start)"
                    : "🔴 Ollama Offline (Click to Start)";
            }

            if (isSwarmRunning)
            {
                SwarmStatusText = $"🟢 SwarmUI Online ({AvailableImageModels.Count} model{(AvailableImageModels.Count == 1 ? "" : "s")})";
            }
            else
            {
                SwarmStatusText = AvailableImageModels.Count > 0
                    ? $"🔴 SwarmUI Offline ({AvailableImageModels.Count} models on disk)"
                    : "🔴 SwarmUI Offline (port 7801)";
            }
        }

        private async void ExecuteSendChatMessage()
        {
            var text = ChatInputText?.Trim();
            if (string.IsNullOrEmpty(text) || IsChatGenerating) return;

            if (IsImageMode)
            {
                if (SelectedImageModel == null)
                {
                    ChatMessages.Add(new ChatMessage("Error", "⚠️ No SwarmUI image model selected. Please select a model from the side panel."));
                    return;
                }

                // Check SwarmUI server status
                if (!IsSwarmConnected)
                {
                    var isRunning = await _swarmUiService.IsSwarmRunningAsync();
                    if (!isRunning)
                    {
                        ChatMessages.Add(new ChatMessage("Error", "⚠️ SwarmUI is not responding on http://localhost:7801. Please make sure SwarmUI is running."));
                        return;
                    }
                    IsSwarmConnected = true;
                    SwarmStatusText = $"🟢 SwarmUI Online ({AvailableImageModels.Count} models)";
                }

                var cleanText = CleanPrompt(text);

                // Add user message to conversation
                var userMsg = new ChatMessage("User", cleanText);
                ChatMessages.Add(userMsg);
                ChatInputText = string.Empty;

                // Add assistant image loading bubble with throbber overlay
                var assistantMsg = new ChatMessage("Assistant", string.Empty, SelectedImageModel.Name)
                {
                    IsImageMessage = true,
                    IsImageLoading = true,
                    ImagePrompt = cleanText,
                    ImageDimensions = $"{SelectedImageModel.StandardWidth}×{SelectedImageModel.StandardHeight}",
                    ImageSteps = 50,
                    ImageCfg = 7.0
                };
                ChatMessages.Add(assistantMsg);

                IsChatGenerating = true;
                ActiveTaskStatus = $"Generating image with {SelectedImageModel.Name}...";
                _chatCts = new CancellationTokenSource();

                try
                {
                    var result = await _swarmUiService.GenerateImageAsync(cleanText, SelectedImageModel, _chatCts.Token);
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        assistantMsg.IsImageLoading = false;
                        if (result.Success)
                        {
                            assistantMsg.ImagePath = result.ImagePath;
                            assistantMsg.ImageUrl = result.ImageUrl;
                            assistantMsg.ImagePrompt = cleanText;
                            assistantMsg.ImageDimensions = $"{result.Width}×{result.Height}";
                            assistantMsg.ImageSteps = result.Steps;
                            assistantMsg.ImageCfg = result.Cfg;
                            assistantMsg.Content = cleanText;
                            ShowStatus("✓ SwarmUI image generated successfully!");
                        }
                        else
                        {
                            assistantMsg.Role = "Error";
                            assistantMsg.Content = $"⚠️ {result.ErrorMessage ?? "Image generation failed."}";
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        assistantMsg.IsImageLoading = false;
                        assistantMsg.Content = "*(Image generation canceled)*";
                    });
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        assistantMsg.IsImageLoading = false;
                        assistantMsg.Role = "Error";
                        assistantMsg.Content = $"⚠️ Error generating image: {ex.Message}";
                    });
                }
                finally
                {
                    IsChatGenerating = false;
                    ActiveTaskStatus = string.Empty;
                    _chatCts?.Dispose();
                    _chatCts = null;
                }
            }
            else
            {
                // Text LLM Chat with Ollama
                if (SelectedChatModel == null && SelectedModel == null)
                {
                    ChatMessages.Add(new ChatMessage("Error", "⚠️ No local model selected. Please select a model from the side panel."));
                    return;
                }

                var model = SelectedChatModel ?? SelectedModel!;

                // Ensure Ollama is running
                if (!IsOllamaConnected)
                {
                    OllamaStatusText = "🟡 Starting Ollama...";
                    var started = await _localAiService.EnsureOllamaRunningAsync();
                    if (started)
                    {
                        IsOllamaConnected = true;
                        OllamaStatusText = $"🟢 Ollama Online ({AvailableChatModels.Count} models)";
                    }
                    else
                    {
                        ChatMessages.Add(new ChatMessage("Error", "⚠️ Could not connect to Ollama on http://localhost:11434. Please verify that Ollama is installed and running."));
                        OllamaStatusText = "🔴 Ollama Offline (Click to Start)";
                        return;
                    }
                }

                // Add user message to conversation
                var userMsg = new ChatMessage("User", text);
                ChatMessages.Add(userMsg);
                ChatInputText = string.Empty;

                // Add empty assistant response bubble to stream tokens into
                var assistantMsg = new ChatMessage("Assistant", string.Empty, model.Name)
                {
                    IsGenerating = true
                };
                ChatMessages.Add(assistantMsg);

                IsChatGenerating = true;
                ActiveTaskStatus = $"Streaming tokens with {model.Name}...";
                _chatCts = new CancellationTokenSource();

                try
                {
                    await _localAiService.StreamChatAsync(
                        model.ModelId,
                        ChatMessages.Take(ChatMessages.Count - 1),
                        (chunk, isThinking) =>
                        {
                            Application.Current?.Dispatcher.InvokeAsync(() =>
                            {
                                if (isThinking)
                                {
                                    assistantMsg.ThinkingContent += chunk;
                                }
                                else
                                {
                                    assistantMsg.Content += chunk;
                                }
                            });
                        },
                        _chatCts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (string.IsNullOrWhiteSpace(assistantMsg.Content))
                    {
                        assistantMsg.Content = "*(Generation stopped)*";
                    }
                    else
                    {
                        assistantMsg.Content += "\n\n*(Generation stopped)*";
                    }
                }
                catch (Exception ex)
                {
                    assistantMsg.Role = "Error";
                    assistantMsg.Content = $"⚠️ Error generating response: {ex.Message}";
                }
                finally
                {
                    assistantMsg.IsGenerating = false;
                    IsChatGenerating = false;
                    ActiveTaskStatus = string.Empty;
                    _chatCts?.Dispose();
                    _chatCts = null;
                }
            }
        }

        private void ExecuteStopGeneration()
        {
            _chatCts?.Cancel();
        }

        public void TriggerImageRegeneration(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return;
            if (IsChatGenerating)
            {
                ShowStatus("⚠️ A generation task is already running. Please wait or stop it first.");
                return;
            }

            var clean = CleanPrompt(prompt);
            if (string.IsNullOrWhiteSpace(clean)) return;

            ActiveMode = "Image";
            ChatInputText = clean;
            ExecuteSendChatMessage();
        }

        public void DeleteChatMessage(ChatMessage msg)
        {
            if (msg == null) return;
            try
            {
                if (msg.IsImageMessage && !string.IsNullOrEmpty(msg.ImagePath) && File.Exists(msg.ImagePath))
                {
                    File.Delete(msg.ImagePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete image: {ex.Message}");
            }

            ChatMessages.Remove(msg);
            ShowStatus("🗑️ Message deleted");
        }

        private void ExecuteClearChat()
        {
            ChatMessages.Clear();
            ChatMessages.Add(new ChatMessage("Assistant", "🧹 Chat cleared! Select a model and start a new conversation."));
        }

        private void ExecuteInsertActivePrompt()
        {
            if (SelectedPrompt != null && !string.IsNullOrWhiteSpace(SelectedPrompt.Content))
            {
                var clean = CleanPrompt(SelectedPrompt.Content);
                if (string.IsNullOrWhiteSpace(ChatInputText))
                {
                    ChatInputText = clean;
                }
                else
                {
                    ChatInputText += "\n\n" + clean;
                }
                ShowStatus($"✓ Inserted prompt \"{SelectedPrompt.DisplayTitle}\" into chat input!");
            }
            else
            {
                ShowStatus("Select a prompt in the manager above to insert.");
            }
        }

        private async void ExecuteStartOllama()
        {
            OllamaStatusText = "🟡 Starting Ollama...";
            var started = await _localAiService.EnsureOllamaRunningAsync();
            await ScanLocalModelsAsync();
            if (started)
            {
                ShowStatus("✓ Ollama service is online!");
            }
            else
            {
                ShowStatus("⚠️ Failed to start Ollama automatically. Check if ollama.exe is installed.");
            }
        }

        private async void ExecuteAddModel()
        {
            var dialog = new AddModelDialog(_localAiService);
            SetDialogOwner(dialog);
            if (dialog.ShowDialog() == true && dialog.ModelAdded)
            {
                await ScanLocalModelsAsync();
                if (!string.IsNullOrEmpty(dialog.AddedModelName))
                {
                    var found = AvailableModels.FirstOrDefault(m => string.Equals(m.ModelId, dialog.AddedModelName, StringComparison.OrdinalIgnoreCase)
                                                                 || string.Equals(m.Name, dialog.AddedModelName, StringComparison.OrdinalIgnoreCase));
                    if (found != null)
                    {
                        SelectedModel = found;
                    }
                }
                ShowStatus("✓ Model added successfully!");
            }
        }

        private void SaveLastSelectedModel(string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prompter");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "last_model.txt"), modelId);
            }
            catch { }
        }

        private string? LoadLastSelectedModel()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prompter", "last_model.txt");
                if (File.Exists(path))
                {
                    return File.ReadAllText(path).Trim();
                }
            }
            catch { }
            return null;
        }

        public void CopyChatMessage(ChatMessage message)
        {
            if (message == null) return;
            var text = message.IsImageMessage && !string.IsNullOrWhiteSpace(message.ImagePrompt)
                ? message.ImagePrompt
                : message.Content;

            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                ShowStatus("✓ Message copied to clipboard!");
            }
            catch
            {
                ShowStatus("✓ Message copied to clipboard!");
            }
        }

        public void CopyTextToClipboard(string text, string successMessage = "✓ Copied to clipboard!")
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                ShowStatus(successMessage);
            }
            catch
            {
                ShowStatus(successMessage);
            }
        }

        public void LoadPromptForEditing(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var clean = CleanPrompt(text);
            ChatInputText = clean;
            ShowStatus("✏️ Prompt loaded into editor");
        }

        public static string CleanPrompt(string? prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
            var trimmed = prompt.Trim();

            // Strip markdown code fences if wrapped in ``` ... ```
            if (trimmed.StartsWith("```") && trimmed.EndsWith("```") && trimmed.Length > 6)
            {
                var firstLineEnd = trimmed.IndexOfAny(new[] { '\r', '\n' });
                var lastLineStart = trimmed.LastIndexOfAny(new[] { '\r', '\n' });
                if (firstLineEnd > 0 && lastLineStart > firstLineEnd)
                {
                    trimmed = trimmed.Substring(firstLineEnd, lastLineStart - firstLineEnd).Trim();
                }
            }

            string[] prefixes = new[]
            {
                "**prompt:**",
                "**prompt**:",
                "**prompt** -",
                "**prompt**-",
                "**image prompt:**",
                "**image prompt**:",
                "image prompt:",
                "positive prompt:",
                "**positive prompt:**",
                "prompt:",
                "prompt -"
            };

            bool stripped = true;
            while (stripped)
            {
                stripped = false;

                // Strip wrapping double or single quotes
                if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2) ||
                    (trimmed.StartsWith("“") && trimmed.EndsWith("”") && trimmed.Length >= 2) ||
                    (trimmed.StartsWith("'") && trimmed.EndsWith("'") && trimmed.Length >= 2))
                {
                    trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    stripped = true;
                }

                var lower = trimmed.ToLowerInvariant();
                foreach (var prefix in prefixes)
                {
                    if (lower.StartsWith(prefix))
                    {
                        trimmed = trimmed.Substring(prefix.Length).Trim();
                        stripped = true;
                        break;
                    }
                }
            }

            // Final check for wrapping quotes
            if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2) ||
                (trimmed.StartsWith("“") && trimmed.EndsWith("”") && trimmed.Length >= 2))
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
            }

            return trimmed;
        }

        private void ExecuteStartSwarm()
        {
            if (_swarmUiService.StartSwarmServer())
            {
                ShowStatus("✓ Starting SwarmUI server process...");
            }
            else
            {
                ShowStatus("⚠️ Could not find SwarmUI launcher script (e.g. launch-windows.bat).");
            }
        }

        private void ExecuteOpenSwarmOutputs()
        {
            try
            {
                if (Directory.Exists(_swarmUiService.SwarmOutputPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", _swarmUiService.SwarmOutputPath);
                }
                else
                {
                    ShowStatus($"SwarmUI output folder not found at {_swarmUiService.SwarmOutputPath}");
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to open outputs: {ex.Message}");
            }
        }

        public void CopyImageToClipboard(string? imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                ShowStatus("Image file not found on disk.");
                return;
            }

            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                Clipboard.SetImage(bitmap);
                ShowStatus("✓ Image copied to clipboard!");
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to copy image: {ex.Message}");
            }
        }

        public void OpenImageFile(string? imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                ShowStatus("Image file not found on disk.");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(imagePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not open image: {ex.Message}");
            }
        }

        public void ShowImageInFolder(string? imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                ShowStatus("Image file not found on disk.");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{imagePath}\"");
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not show in folder: {ex.Message}");
            }
        }

        private void SaveLastSelectedImageModel(string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prompter");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "last_image_model.txt"), modelId);
            }
            catch { }
        }

        private string? LoadLastSelectedImageModel()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prompter", "last_image_model.txt");
                if (File.Exists(path))
                {
                    return File.ReadAllText(path).Trim();
                }
            }
            catch { }
            return null;
        }
        public void ExecuteSetDefaultModel(LocalModelInfo? model)
        {
            model ??= CurrentModel;
            if (model == null) return;

            if (model.IsImageModel)
            {
                foreach (var m in AvailableImageModels)
                {
                    m.IsDefault = (m == model || string.Equals(m.ModelId, model.ModelId, StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(m.Name, model.Name, StringComparison.OrdinalIgnoreCase));
                }
                SaveDefaultImageModel(model.ModelId);
                SelectedImageModel = model;
                ShowStatus($"⭐ Default image model set to {model.Name}");
            }
            else
            {
                foreach (var m in AvailableChatModels)
                {
                    m.IsDefault = (m == model || string.Equals(m.ModelId, model.ModelId, StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(m.Name, model.Name, StringComparison.OrdinalIgnoreCase));
                }
                SaveDefaultChatModel(model.ModelId);
                SelectedChatModel = model;
                ShowStatus($"⭐ Default chat model set to {model.Name}");
            }

            OnPropertyChanged(nameof(CurrentModel));
            OnPropertyChanged(nameof(ActiveModelDisplayName));
        }

        private void SaveDefaultChatModel(string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prompter");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "default_chat_model.txt"), modelId);
            }
            catch { }
        }

        public string? LoadDefaultChatModel()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prompter", "default_chat_model.txt");
                if (File.Exists(path))
                {
                    return File.ReadAllText(path).Trim();
                }
            }
            catch { }
            return null;
        }

        private void SaveDefaultImageModel(string? modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prompter");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "default_image_model.txt"), modelId);
            }
            catch { }
        }

        public string? LoadDefaultImageModel()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prompter", "default_image_model.txt");
                if (File.Exists(path))
                {
                    return File.ReadAllText(path).Trim();
                }
            }
            catch { }
            return null;
        }
        #endregion

        public void SaveVaultOnExit()
        {
            _storageService.SaveVault(Folders);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
