using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Prompter.Models;
using Prompter.Services;
using Prompter.ViewModels;

namespace Prompter.Tests
{
    [TestClass]
    public class CryptoServiceTests
    {
        [TestMethod]
        public void TestPasswordHashingAndVerification()
        {
            var salt = CryptoService.GenerateSaltBase64();
            var password = "SuperSecretPassword123!";

            var hash = CryptoService.HashPassword(password, salt);
            Assert.IsFalse(string.IsNullOrEmpty(hash));

            // Verify with correct password
            Assert.IsTrue(CryptoService.VerifyPassword(password, salt, hash));

            // Verify with wrong password
            Assert.IsFalse(CryptoService.VerifyPassword("WrongPassword", salt, hash));

            // Verify with modified salt
            var otherSalt = CryptoService.GenerateSaltBase64();
            Assert.IsFalse(CryptoService.VerifyPassword(password, otherSalt, hash));
        }

        [TestMethod]
        public void TestEncryptionAndDecryptionRoundTrip()
        {
            var salt = CryptoService.GenerateSaltBase64();
            var password = "MyEncryptionKey#456";
            var plainText = "You are an expert AI prompt engineer. System prompt test with special chars: 🚀 <>&\"' \r\n Multiline!";

            var encryptedBase64 = CryptoService.Encrypt(plainText, password, salt);
            Assert.IsFalse(string.IsNullOrEmpty(encryptedBase64));
            Assert.AreNotEqual(plainText, encryptedBase64);

            var decrypted = CryptoService.Decrypt(encryptedBase64, password, salt);
            Assert.AreEqual(plainText, decrypted);
        }

        [TestMethod]
        public void TestDecryptionWithWrongPasswordFails()
        {
            var salt = CryptoService.GenerateSaltBase64();
            var password = "CorrectPassword123";
            var plainText = "Confidential prompt content";

            var encryptedBase64 = CryptoService.Encrypt(plainText, password, salt);

            Assert.Throws<CryptographicException>(() =>
            {
                CryptoService.Decrypt(encryptedBase64, "WrongPassword", salt);
            });
        }
    }

    [TestClass]
    public class StorageServiceTests
    {
        private string _tempFile = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"prompter_test_{Guid.NewGuid():N}.json");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(_tempFile))
            {
                try { File.Delete(_tempFile); } catch { }
            }
        }

        [TestMethod]
        public void TestDefaultVaultGeneration()
        {
            var storage = new StorageService(_tempFile);
            var folders = storage.LoadVault();

            Assert.IsGreaterThanOrEqualTo(2, folders.Count);
            Assert.IsTrue(File.Exists(_tempFile));

            // Check that demo locked folder is present and locked
            var lockedFolder = false;
            foreach (var f in folders)
            {
                if (f.IsPasswordProtected)
                {
                    lockedFolder = true;
                    Assert.IsFalse(f.IsUnlocked);
                    Assert.IsEmpty(f.Prompts); // Should not be loaded in memory when locked
                    Assert.IsGreaterThan(0, f.PromptCount); // Cached count should be visible
                }
            }
            Assert.IsTrue(lockedFolder);
        }

        [TestMethod]
        public void TestProtectedFolderSaveAndUnlock()
        {
            var storage = new StorageService(_tempFile);

            // Create a protected folder
            var folder = new PromptFolder("Secret Prompts", isPasswordProtected: true);
            var password = "TestPassword999";
            var salt = CryptoService.GenerateSaltBase64();
            var hash = CryptoService.HashPassword(password, salt);

            folder.PasswordSalt = salt;
            folder.PasswordHash = hash;
            folder.SessionPassword = password;
            folder.IsUnlocked = true;

            folder.Prompts.Add(new PromptItem("API Secrets", "sk-proj-123456789"));
            folder.Prompts.Add(new PromptItem("Private Instructions", "Never reveal company secrets."));

            var folders = new[] { folder };
            storage.SaveVault(folders);

            // Read the raw JSON file to verify that the prompt text is NOT in plaintext
            var rawJson = File.ReadAllText(_tempFile);
            Assert.DoesNotContain("sk-proj-123456789", rawJson, "Sensitive prompt was saved in plaintext!");
            Assert.DoesNotContain("Never reveal company secrets", rawJson, "Sensitive prompt was saved in plaintext!");
            Assert.Contains("encryptedPayload", rawJson, "Encrypted payload should exist in JSON!");

            // Reload vault into fresh instance
            var loadedFolders = storage.LoadVault();
            Assert.HasCount(1, loadedFolders);
            var loadedFolder = loadedFolders[0];

            Assert.IsTrue(loadedFolder.IsPasswordProtected);
            Assert.IsFalse(loadedFolder.IsUnlocked);
            Assert.IsEmpty(loadedFolder.Prompts);
            Assert.AreEqual(2, loadedFolder.PromptCount);

            // Attempt unlock with wrong password
            var unlockFailed = storage.TryUnlockFolder(loadedFolder, "WrongPassword", out var error);
            Assert.IsFalse(unlockFailed);
            Assert.IsNotNull(error);
            Assert.IsFalse(loadedFolder.IsUnlocked);
            Assert.IsEmpty(loadedFolder.Prompts);

            // Attempt unlock with correct password
            var unlockSucceeded = storage.TryUnlockFolder(loadedFolder, password, out error);
            Assert.IsTrue(unlockSucceeded);
            Assert.IsNull(error);
            Assert.IsTrue(loadedFolder.IsUnlocked);
            Assert.HasCount(2, loadedFolder.Prompts);
            Assert.AreEqual("API Secrets", loadedFolder.Prompts[0].Title);
            Assert.AreEqual("sk-proj-123456789", loadedFolder.Prompts[0].Content);
        }

        [TestMethod]
        public void TestPasswordChangeAndRemovalFlow()
        {
            var storage = new StorageService(_tempFile);
            var folder = new PromptFolder("Dynamic Security", isPasswordProtected: true);
            var initialPwd = "InitialPassword123";
            var salt = CryptoService.GenerateSaltBase64();
            folder.PasswordSalt = salt;
            folder.PasswordHash = CryptoService.HashPassword(initialPwd, salt);
            folder.SessionPassword = initialPwd;
            folder.IsUnlocked = true;
            folder.Prompts.Add(new PromptItem("Test Key", "my-secret-key"));

            storage.SaveVault(new[] { folder });

            // 1. Change password
            var newPwd = "BrandNewPassword456";
            var newSalt = CryptoService.GenerateSaltBase64();
            folder.PasswordSalt = newSalt;
            folder.PasswordHash = CryptoService.HashPassword(newPwd, newSalt);
            folder.SessionPassword = newPwd;
            storage.SaveVault(new[] { folder });

            // Verify can unlock with new password
            var reload = storage.LoadVault();
            Assert.IsFalse(storage.TryUnlockFolder(reload[0], initialPwd, out _));
            Assert.IsTrue(storage.TryUnlockFolder(reload[0], newPwd, out _));
            Assert.AreEqual("my-secret-key", reload[0].Prompts[0].Content);

            // 2. Remove password protection
            reload[0].IsPasswordProtected = false;
            reload[0].PasswordSalt = null;
            reload[0].PasswordHash = null;
            reload[0].EncryptedPayload = null;
            reload[0].SessionPassword = null;
            storage.SaveVault(reload);

            // Verify loaded without password requirement
            var publicReload = storage.LoadVault();
            Assert.IsFalse(publicReload[0].IsPasswordProtected);
            Assert.IsTrue(publicReload[0].IsUnlocked);
            Assert.AreEqual("my-secret-key", publicReload[0].Prompts[0].Content);
        }
    }

    [TestClass]
    public class ModelTests
    {
        [TestMethod]
        public void TestPromptItemStatsAndSnippet()
        {
            var prompt = new PromptItem("Assistant Persona", "You are an assistant. Help the user concisely and accurately.");
            Assert.AreEqual("Assistant Persona", prompt.DisplayTitle);
            Assert.AreEqual(10, prompt.WordCount);
            Assert.AreEqual(61, prompt.CharacterCount);
            Assert.StartsWith("You are an assistant", prompt.PreviewSnippet);

            prompt.Title = "";
            Assert.AreEqual("(Untitled Prompt)", prompt.DisplayTitle);
        }

        [TestMethod]
        public void TestFolderLockClearsPromptsFromMemory()
        {
            var folder = new PromptFolder("My Vault", isPasswordProtected: true)
            {
                SessionPassword = "mypassword",
                IsUnlocked = true
            };

            folder.Prompts.Add(new PromptItem("Test 1", "Content 1"));
            folder.Prompts.Add(new PromptItem("Test 2", "Content 2"));
            Assert.AreEqual(2, folder.PromptCount);

            folder.Lock();

            Assert.IsFalse(folder.IsUnlocked);
            Assert.IsNull(folder.SessionPassword);
            Assert.IsEmpty(folder.Prompts);
            Assert.AreEqual(2, folder.PromptCount); // Cached count retained
            Assert.AreEqual("🔒", folder.StatusIcon);
        }
    }

    [TestClass]
    public class ThemeServiceTests
    {
        [TestMethod]
        public void TestThemeToggleAndEvents()
        {
            var themeService = ThemeService.Instance;
            themeService.ApplyTheme(AppTheme.Light);
            Assert.AreEqual(AppTheme.Light, themeService.CurrentTheme);

            AppTheme? receivedTheme = null;
            themeService.ThemeChanged += theme => receivedTheme = theme;

            themeService.ToggleTheme();
            Assert.AreEqual(AppTheme.Dark, themeService.CurrentTheme);
            Assert.AreEqual(AppTheme.Dark, receivedTheme);

            themeService.ToggleTheme();
            Assert.AreEqual(AppTheme.Light, themeService.CurrentTheme);
            Assert.AreEqual(AppTheme.Light, receivedTheme);
        }

        [TestMethod]
        public void TestDialogsInstantiationDoesNotThrow()
        {
            Exception? caughtException = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    if (System.Windows.Application.Current == null)
                    {
                        _ = new System.Windows.Application();
                    }

                    var folderDlg = new Prompter.Views.FolderDialog();
                    Assert.IsNotNull(folderDlg);

                    var promptDlg = new Prompter.Views.PromptDialog();
                    Assert.IsNotNull(promptDlg);

                    var folder = new PromptFolder("Test Folder", isPasswordProtected: true);
                    var storage = new StorageService(System.IO.Path.GetTempFileName());
                    var pwdDlg = new Prompter.Views.PasswordDialog(folder, storage);
                    Assert.IsNotNull(pwdDlg);

                    var changePwdDlg = new Prompter.Views.ChangePasswordDialog(folder, storage);
                    Assert.IsNotNull(changePwdDlg);

                    var addModelDlg = new Prompter.Views.AddModelDialog();
                    Assert.IsNotNull(addModelDlg);
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.IsNull(caughtException, caughtException?.ToString());
        }
    }

    [TestClass]
    public class LocalModelInfoTests
    {
        [TestMethod]
        public void TestQwenModelDetection()
        {
            var qwen1 = new LocalModelInfo { Name = "qwen2.5:3b", Family = "qwen2" };
            Assert.IsTrue(qwen1.IsQwen);

            var qwen2 = new LocalModelInfo { Name = "qwythos-9b:latest", Family = "qwen35" };
            Assert.IsTrue(qwen2.IsQwen);

            var llama = new LocalModelInfo { Name = "llama3:8b", Family = "llama" };
            Assert.IsFalse(llama.IsQwen);

            var gemma = new LocalModelInfo { Name = "gemma2:9b", Family = "gemma2" };
            Assert.IsFalse(gemma.IsQwen);
        }

        [TestMethod]
        public void TestDisplayNameAndDetailsBadge()
        {
            var model = new LocalModelInfo
            {
                Name = "qwen2.5:3b",
                Family = "qwen2",
                ParameterSize = "3B",
                Quantization = "Q4_K_M",
                Source = "Ollama"
            };

            Assert.Contains("⭐ [Qwen]", model.DisplayName);
            Assert.Contains("3B", model.DisplayName);
            Assert.AreEqual("⭐ [Qwen] qwen2.5:3b (3B)", model.DisplayName);
            Assert.AreEqual("Qwen • 3B • Q4_K_M • Ollama", model.DetailsBadge);

            var gemmaModel = new LocalModelInfo
            {
                Name = "gemma4:12b",
                ParameterSize = "12B",
                Quantization = "Q4_0",
                Source = "Ollama"
            };

            Assert.IsTrue(gemmaModel.IsGemma);
            Assert.Contains("💎 [Gemma]", gemmaModel.DisplayName);
            Assert.AreEqual("💎 [Gemma] gemma4:12b (12B)", gemmaModel.DisplayName);
            Assert.AreEqual("Gemma • 12B • Q4_0 • Ollama", gemmaModel.DetailsBadge);
        }

        [TestMethod]
        public void TestOtherModelFamiliesDetection()
        {
            var llama = new LocalModelInfo { Name = "llama3.2:3b", ParameterSize = "3B" };
            Assert.IsTrue(llama.IsLlama);
            Assert.AreEqual("Llama", llama.FamilyName);
            Assert.Contains("🦙 [Llama]", llama.DisplayName);

            var deepseek = new LocalModelInfo { Name = "deepseek-r1:1.5b", ParameterSize = "1.5B" };
            Assert.IsTrue(deepseek.IsDeepSeek);
            Assert.AreEqual("DeepSeek", deepseek.FamilyName);
            Assert.Contains("🐋 [DeepSeek]", deepseek.DisplayName);

            var mistral = new LocalModelInfo { Name = "mistral:7b", ParameterSize = "7B" };
            Assert.IsTrue(mistral.IsMistral);
            Assert.AreEqual("Mistral", mistral.FamilyName);
            Assert.Contains("🌪️ [Mistral]", mistral.DisplayName);

            var glm = new LocalModelInfo { Name = "glm-5.3-flash:cloud" };
            Assert.IsTrue(glm.IsGlm);
            Assert.AreEqual("GLM", glm.FamilyName);
            Assert.Contains("🌐 [GLM]", glm.DisplayName);

            var phi = new LocalModelInfo { Name = "phi3:mini", ParameterSize = "3.8B" };
            Assert.IsTrue(phi.IsPhi);
            Assert.AreEqual("Phi", phi.FamilyName);
            Assert.Contains("🔬 [Phi]", phi.DisplayName);

            var custom = new LocalModelInfo { Name = "my-custom-model" };
            Assert.AreEqual("Local", custom.FamilyName);
            Assert.Contains("🤖", custom.DisplayName);
        }
    }

    [TestClass]
    public class ChatMessageTests
    {
        [TestMethod]
        public void TestContentPropertyChangedForStreaming()
        {
            var msg = new ChatMessage("Assistant", "");
            string? changedProp = null;
            msg.PropertyChanged += (s, e) => changedProp = e.PropertyName;

            msg.Content += "Hello ";
            Assert.AreEqual("Content", changedProp);
            Assert.AreEqual("Hello ", msg.Content);

            msg.Content += "world!";
            Assert.AreEqual("Content", changedProp);
            Assert.AreEqual("Hello world!", msg.Content);
        }

        [TestMethod]
        public void TestIsGeneratingPropertyChanged()
        {
            var msg = new ChatMessage("Assistant", "Generating...");
            string? changedProp = null;
            msg.PropertyChanged += (s, e) => changedProp = e.PropertyName;

            msg.IsGenerating = true;
            Assert.AreEqual(nameof(ChatMessage.IsGenerating), changedProp);
            Assert.IsTrue(msg.IsGenerating);

            msg.IsGenerating = false;
            Assert.AreEqual(nameof(ChatMessage.IsGenerating), changedProp);
            Assert.IsFalse(msg.IsGenerating);
        }

        [TestMethod]
        public void TestRoleHelpers()
        {
            var userMsg = new ChatMessage("User", "Hello");
            Assert.IsTrue(userMsg.IsUser);
            Assert.IsFalse(userMsg.IsAssistant);
            Assert.IsFalse(userMsg.IsError);

            var assistantMsg = new ChatMessage("Assistant", "Hi there!");
            Assert.IsFalse(assistantMsg.IsUser);
            Assert.IsTrue(assistantMsg.IsAssistant);
            Assert.IsFalse(assistantMsg.IsError);

            var errorMsg = new ChatMessage("Error", "Something failed");
            Assert.IsFalse(errorMsg.IsUser);
            Assert.IsFalse(errorMsg.IsAssistant);
            Assert.IsTrue(errorMsg.IsError);
        }

        [TestMethod]
        public void TestThinkingContentPropertyChanged()
        {
            var msg = new ChatMessage("Assistant", "");
            Assert.IsFalse(msg.HasThinking);

            var changedProps = new System.Collections.Generic.List<string>();
            msg.PropertyChanged += (s, e) => { if (e.PropertyName != null) changedProps.Add(e.PropertyName); };

            msg.ThinkingContent += "Analyzing the user query...";
            Assert.IsTrue(msg.HasThinking);
            Assert.Contains(nameof(ChatMessage.ThinkingContent), changedProps);
            Assert.Contains(nameof(ChatMessage.HasThinking), changedProps);
            Assert.AreEqual("Analyzing the user query...", msg.ThinkingContent);
        }
    }

    [TestClass]
    public class LocalAiServiceTests
    {
        [TestMethod]
        public async Task TestScanModelsAsyncReturnsListWithoutThrowing()
        {
            var service = LocalAiService.Instance;
            var models = await service.ScanModelsAsync();

            Assert.IsNotNull(models);
            // If any Qwen models exist, the first Qwen model should appear before non-Qwen models
            var qwenIdx = models.FindIndex(m => m.IsQwen);
            var nonQwenIdx = models.FindIndex(m => !m.IsQwen);
            if (qwenIdx >= 0 && nonQwenIdx >= 0)
            {
                // In MSTest, IsLessThan(upperBound, value) checks value < upperBound
                Assert.IsLessThan(nonQwenIdx, qwenIdx);
            }
        }
    }

    [TestClass]
    public class MainViewModelChatTests
    {
        [TestMethod]
        public void TestChatClearAndActivePromptInsertion()
        {
            Exception? caughtException = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var vm = new MainViewModel();

                    // Test ClearChatCommand
                    vm.ChatMessages.Add(new ChatMessage("User", "Test question"));
                    Assert.IsGreaterThan(1, vm.ChatMessages.Count);
                    vm.ClearChatCommand.Execute(null);
                    Assert.HasCount(1, vm.ChatMessages);
                    Assert.Contains("cleared", vm.ChatMessages[0].Content);

                    // Test InsertActivePromptCommand
                    var testFolder = new PromptFolder("Test Folder");
                    var testPrompt = new PromptItem("Coding Prompt", "Write a python script to parse CSV files.");
                    testFolder.Prompts.Add(testPrompt);
                    vm.Folders.Add(testFolder);
                    vm.SelectedFolder = testFolder;
                    vm.SelectedPrompt = testPrompt;

                    vm.ChatInputText = "";
                    Assert.IsTrue(vm.InsertActivePromptCommand.CanExecute(null));
                    vm.InsertActivePromptCommand.Execute(null);
                    Assert.AreEqual("Write a python script to parse CSV files.", vm.ChatInputText);

                    // Insert again appends with double newline
                    vm.InsertActivePromptCommand.Execute(null);
                    Assert.AreEqual("Write a python script to parse CSV files.\n\nWrite a python script to parse CSV files.", vm.ChatInputText);
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.IsNull(caughtException, caughtException?.ToString());
        }
    }

    [TestClass]
    public class SwarmUiServiceTests
    {
        [TestMethod]
        public void TestSwarmPathsAndDefaults()
        {
            var service = SwarmUiService.Instance;
            Assert.AreEqual("http://localhost:7801", service.BaseUrl);
            Assert.AreEqual(@"X:\SwarmUI", service.SwarmRootPath);
            Assert.AreEqual(@"X:\SwarmUI\Models\Stable-Diffusion", service.SwarmModelsPath);
            Assert.AreEqual(@"X:\SwarmUI\Output", service.SwarmOutputPath);
        }

        [TestMethod]
        public async Task TestListModelsFindsStableDiffusionModels()
        {
            var service = SwarmUiService.Instance;
            var models = await service.ListModelsAsync();

            Assert.IsNotNull(models);
            // Verify models on disk or API are discovered
            if (Directory.Exists(service.SwarmModelsPath))
            {
                Assert.IsGreaterThan(0, models.Count, "Should discover models in X:\\SwarmUI\\Models\\Stable-Diffusion");
                foreach (var m in models)
                {
                    Assert.IsTrue(m.IsImageModel, $"Model {m.Name} should be marked as an image model");
                    Assert.IsGreaterThan(0, m.StandardWidth, $"Model {m.Name} should have positive default width");
                    Assert.IsGreaterThan(0, m.StandardHeight, $"Model {m.Name} should have positive default height");
                }

                // Verify SDXL resolution is 1024 and SD 1.5 is 512
                var sdxlModel = models.FirstOrDefault(m => m.IsSdxl);
                if (sdxlModel != null)
                {
                    Assert.AreEqual(1024, sdxlModel.StandardWidth);
                    Assert.AreEqual(1024, sdxlModel.StandardHeight);
                }

                var dreamshaperModel = models.FirstOrDefault(m => m.Name.Contains("dreamshaper", StringComparison.OrdinalIgnoreCase));
                if (dreamshaperModel != null)
                {
                    Assert.AreEqual(512, dreamshaperModel.StandardWidth);
                    Assert.AreEqual(512, dreamshaperModel.StandardHeight);
                }
            }
        }

        [TestMethod]
        public void TestImageModelMetadataBadges()
        {
            var sdxl = new LocalModelInfo
            {
                Name = "SDXL 1.0.safetensors",
                IsImageModel = true,
                StandardWidth = 1024,
                StandardHeight = 1024
            };

            Assert.IsTrue(sdxl.IsSdxl);
            Assert.IsFalse(sdxl.IsSd15);
            Assert.AreEqual("SDXL", sdxl.FamilyName);
            Assert.Contains("🎨 [SDXL]", sdxl.DisplayName);
            Assert.IsFalse(sdxl.DisplayName.Contains("1024×1024"));
            Assert.IsFalse(sdxl.DetailsBadge.Contains("50 steps"));
            Assert.IsFalse(sdxl.DetailsBadge.Contains("CFG 7"));
            Assert.IsFalse(sdxl.DetailsBadge.Contains("1024×1024"));

            var sd15 = new LocalModelInfo
            {
                Name = "dreamshaper_8.safetensors",
                IsImageModel = true,
                StandardWidth = 512,
                StandardHeight = 512
            };

            Assert.IsTrue(sd15.IsSd15);
            Assert.IsFalse(sd15.IsSdxl);
            Assert.AreEqual("SD 1.5", sd15.FamilyName);
            Assert.Contains("🎨 [SD 1.5]", sd15.DisplayName);
            Assert.IsFalse(sd15.DisplayName.Contains("512×512"));
            Assert.IsFalse(sd15.DetailsBadge.Contains("50 steps"));
        }

        [TestMethod]
        public void TestVideoModelsAreIdentifiedAndExcluded()
        {
            var wanGguf = "wan22EnhancedNSFWSViCamera_nolightningSVICfQ8H.gguf";
            var wanSafe = "wan22RemixT2VI2V_i2vLowV30.safetensors";
            var standardSdxl = "SDXL 1.0.safetensors";

            Assert.IsTrue(SwarmUiService.IsVideoModelName(wanGguf));
            Assert.IsTrue(SwarmUiService.IsVideoModelName(wanSafe));
            Assert.IsFalse(SwarmUiService.IsVideoModelName(standardSdxl));

            var wanModelInfo = new LocalModelInfo { Name = wanSafe };
            Assert.IsTrue(wanModelInfo.IsVideoModel);

            var sdxlModelInfo = new LocalModelInfo { Name = standardSdxl };
            Assert.IsFalse(sdxlModelInfo.IsVideoModel);
        }

        [TestMethod]
        public void TestChatMessageImagePromptProperty()
        {
            var msg = new ChatMessage("Assistant", "Prompt: \"Cyberpunk cityscape\"")
            {
                IsImageMessage = true,
                ImagePrompt = "Cyberpunk cityscape"
            };

            Assert.AreEqual("Cyberpunk cityscape", msg.ImagePrompt);
        }

        [TestMethod]
        public void TestDefaultModelIndicatorsAndPersistence()
        {
            var imageModel1 = new LocalModelInfo { Name = "SDXL 1.0.safetensors", ModelId = "SDXL 1.0.safetensors", IsImageModel = true };
            var imageModel2 = new LocalModelInfo { Name = "Pony v23.safetensors", ModelId = "Pony v23.safetensors", IsImageModel = true };

            Assert.IsFalse(imageModel1.IsDefault);
            Assert.IsFalse(imageModel1.DisplayName.StartsWith("⭐"));
            Assert.IsFalse(imageModel1.DetailsBadge.Contains("⭐ Default"));

            imageModel1.IsDefault = true;
            Assert.IsTrue(imageModel1.IsDefault);
            Assert.IsTrue(imageModel1.DisplayName.StartsWith("⭐"));
            Assert.IsTrue(imageModel1.DetailsBadge.Contains("⭐ Default"));

            var chatModel = new LocalModelInfo { Name = "qwen2.5:3b", ModelId = "qwen2.5:3b", IsImageModel = false, ParameterSize = "3B" };
            Assert.IsFalse(chatModel.IsDefault);

            chatModel.IsDefault = true;
            Assert.IsTrue(chatModel.IsDefault);
            Assert.IsTrue(chatModel.DisplayName.StartsWith("⭐"));
            Assert.IsTrue(chatModel.DetailsBadge.Contains("⭐ Default"));
        }

        [TestMethod]
        public void TestChatMessageImagePropertiesAndNotifications()
        {
            var msg = new ChatMessage("Assistant", "")
            {
                IsImageMessage = true,
                IsImageLoading = true,
                ImageDimensions = "1024×1024",
                ImageSteps = 50,
                ImageCfg = 7.0
            };

            Assert.IsTrue(msg.IsImageMessage);
            Assert.IsTrue(msg.IsImageLoading);
            Assert.AreEqual(50, msg.ImageSteps);
            Assert.AreEqual(7.0, msg.ImageCfg);
            Assert.AreEqual("1024×1024", msg.ImageDimensions);

            var changed = new System.Collections.Generic.List<string>();
            msg.PropertyChanged += (s, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

            msg.IsImageLoading = false;
            Assert.Contains(nameof(ChatMessage.IsImageLoading), changed);

            msg.ImagePath = @"X:\SwarmUI\Output\test.png";
            Assert.Contains(nameof(ChatMessage.ImagePath), changed);
            Assert.Contains(nameof(ChatMessage.HasImageFile), changed);
        }

        [TestMethod]
        public void TestSingleInstanceMutexBehavior()
        {
            var mutexName = "Prompter_SingleInstance_Test_Mutex";
            using var mutex1 = new System.Threading.Mutex(true, mutexName, out var createdFirst);
            Assert.IsTrue(createdFirst, "First mutex creation should succeed");

            using var mutex2 = new System.Threading.Mutex(true, mutexName, out var createdSecond);
            Assert.IsFalse(createdSecond, "Second mutex instance should detect that an instance is already running");
        }
    }

    [TestClass]
    public class SystemEnvironmentDetectorTests
    {
        [TestMethod]
        public void TestDetectSwarmUiFindsInstallationIfPresent()
        {
            var detector = SystemEnvironmentDetector.Instance;
            var swarm = detector.DetectSwarmUi();
            Assert.IsNotNull(swarm);
            Assert.AreEqual("SwarmUI", swarm.Name);

            if (Directory.Exists(@"X:\SwarmUI"))
            {
                Assert.IsTrue(swarm.IsInstalled);
                Assert.AreEqual(@"X:\SwarmUI", swarm.RootDirectory);
                Assert.IsNotNull(swarm.ModelsDirectory);
                Assert.IsNotNull(swarm.OutputDirectory);
            }
        }

        [TestMethod]
        public void TestDetectOllamaFindsInstallationIfPresent()
        {
            var detector = SystemEnvironmentDetector.Instance;
            var ollama = detector.DetectOllama();
            Assert.IsNotNull(ollama);
            Assert.AreEqual("Ollama", ollama.Name);

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (File.Exists(Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe")))
            {
                Assert.IsTrue(ollama.IsInstalled);
                Assert.IsNotNull(ollama.ExecutablePath);
            }
        }

        [TestMethod]
        public void TestDetectQwenFindsModelsIfPresent()
        {
            var detector = SystemEnvironmentDetector.Instance;
            var qwen = detector.DetectQwen();
            Assert.IsNotNull(qwen);
            Assert.AreEqual("Qwen", qwen.Name);
        }

        [TestMethod]
        public void TestDetectAllCombinesAllServices()
        {
            var detector = SystemEnvironmentDetector.Instance;
            var all = detector.DetectAll();
            Assert.IsNotNull(all);
            Assert.IsNotNull(all.SwarmUi);
            Assert.IsNotNull(all.Ollama);
            Assert.IsNotNull(all.Qwen);
        }

        [TestMethod]
        public void TestLoadPromptForEditing()
        {
            var vm = new MainViewModel();
            var prompt = "a majestic lion in the savanna, sunset lighting";
            vm.LoadPromptForEditing(prompt);

            Assert.AreEqual(prompt, vm.ChatInputText);
            Assert.IsTrue(vm.IsStatusVisible);
            Assert.IsTrue(vm.StatusMessage.Contains("Prompt loaded into editor"));
        }

        [TestMethod]
        public void TestCopyChatMessageExtractsImagePromptOrContent()
        {
            var vm = new MainViewModel();

            var textMsg = new ChatMessage("Assistant", "Here is your response.");
            vm.CopyChatMessage(textMsg);
            Assert.IsTrue(vm.IsStatusVisible);
            Assert.IsTrue(vm.StatusMessage.Contains("copied to clipboard"));

            var imageMsg = new ChatMessage("Assistant", "Prompt: \"Cyberpunk car\"")
            {
                IsImageMessage = true,
                ImagePrompt = "Cyberpunk car"
            };
            vm.CopyChatMessage(imageMsg);
            Assert.IsTrue(vm.IsStatusVisible);
            Assert.IsTrue(vm.StatusMessage.Contains("copied to clipboard"));
        }

        [TestMethod]
        public void TestCopyTextToClipboardWithCustomMessage()
        {
            var vm = new MainViewModel();
            vm.CopyTextToClipboard("Some custom prompt", "✓ Prompt copied to clipboard!");
            Assert.IsTrue(vm.IsStatusVisible);
            Assert.AreEqual("✓ Prompt copied to clipboard!", vm.StatusMessage);

            // Null or empty does not crash or update
            vm.CopyTextToClipboard("", "Should not show");
            Assert.AreEqual("✓ Prompt copied to clipboard!", vm.StatusMessage);
        }

        [TestMethod]
        public void TestCleanPromptStripsPrefixesAndQuotes()
        {
            var raw1 = "\"**Prompt:** A medium shot of an attractive young woman\"";
            var clean1 = MainViewModel.CleanPrompt(raw1);
            Assert.AreEqual("A medium shot of an attractive young woman", clean1);

            var raw2 = "**Prompt:** A cinematic render of a futuristic skyline";
            var clean2 = MainViewModel.CleanPrompt(raw2);
            Assert.AreEqual("A cinematic render of a futuristic skyline", clean2);

            var raw3 = "**Image Prompt:** \"Hyperrealistic oil painting of an owl\"";
            var clean3 = MainViewModel.CleanPrompt(raw3);
            Assert.AreEqual("Hyperrealistic oil painting of an owl", clean3);

            var raw4 = "Prompt: A lush green forest with sunbeams";
            var clean4 = MainViewModel.CleanPrompt(raw4);
            Assert.AreEqual("A lush green forest with sunbeams", clean4);

            var raw5 = "A pure prompt without any prefix";
            var clean5 = MainViewModel.CleanPrompt(raw5);
            Assert.AreEqual("A pure prompt without any prefix", clean5);

            Assert.AreEqual(string.Empty, MainViewModel.CleanPrompt(null));
            Assert.AreEqual(string.Empty, MainViewModel.CleanPrompt("   "));
        }
    }

    [TestClass]
    public class LoraTests
    {
        [TestMethod]
        public void TestLoraModelInfoPropertiesAndClamping()
        {
            var lora = new LoraModelInfo
            {
                Id = "C/Wowifier XL.safetensors",
                Name = "Wowifier XL.safetensors",
                Title = "Wowifier XL",
                Folder = "C",
                Architecture = "stable-diffusion-xl-v1-base/lora",
                SizeBytes = 456522350
            };

            // Default strength
            Assert.AreEqual(1.0, lora.Strength, 0.001);
            Assert.AreEqual("1.00", lora.StrengthFormatted);
            Assert.AreEqual("SDXL", lora.ArchitectureBadge);
            Assert.IsTrue(lora.IsSdxl);
            Assert.AreEqual("Wowifier XL (1.00)", lora.DisplayNameWithWeight);
            Assert.AreEqual("435 MB", lora.SizeFormatted);

            // Strength adjustment
            lora.Strength = 0.85;
            Assert.AreEqual(0.85, lora.Strength, 0.001);
            Assert.AreEqual("0.85", lora.StrengthFormatted);
            Assert.AreEqual("Wowifier XL (0.85)", lora.DisplayNameWithWeight);

            // Two-way formatted string
            lora.StrengthFormatted = "1.25";
            Assert.AreEqual(1.25, lora.Strength, 0.001);

            // Clamping range [-2.0, 2.0]
            lora.Strength = 5.0;
            Assert.AreEqual(2.0, lora.Strength, 0.001);

            lora.Strength = -10.0;
            Assert.AreEqual(-2.0, lora.Strength, 0.001);
        }

        [TestMethod]
        public void TestLoraManagerViewModelDualListManagement()
        {
            var vm = new LoraManagerViewModel();

            var lora1 = new LoraModelInfo
            {
                Id = "C/Detailer.safetensors",
                Name = "Detailer.safetensors",
                Title = "Detailer",
                RelativePath = "C/Detailer.safetensors",
                Folder = "C",
                Strength = 1.0
            };

            var lora2 = new LoraModelInfo
            {
                Id = "SD/Styles/Anime.safetensors",
                Name = "Anime.safetensors",
                Title = "Anime Style",
                RelativePath = "SD/Styles/Anime.safetensors",
                Folder = "SD/Styles",
                Strength = 1.0
            };

            vm.AvailableLoras.Add(lora1);
            vm.AvailableLoras.Add(lora2);

            Assert.IsFalse(vm.HasActiveLoras);
            Assert.AreEqual(0, vm.ActiveCount);

            // Add lora1 from Available (Column B) to Active (Column A)
            vm.AddLora(lora1);
            Assert.IsTrue(vm.HasActiveLoras);
            Assert.AreEqual(1, vm.ActiveCount);
            Assert.AreEqual("C/Detailer.safetensors", vm.ActiveLoras[0].RelativePath);

            // Duplicate prevention
            vm.AddLora(lora1);
            Assert.AreEqual(1, vm.ActiveCount);

            // Add second LoRA
            vm.AddLora(lora2);
            Assert.AreEqual(2, vm.ActiveCount);

            // Remove lora1
            vm.RemoveLora(vm.ActiveLoras[0]);
            Assert.AreEqual(1, vm.ActiveCount);
            Assert.AreEqual("SD/Styles/Anime.safetensors", vm.ActiveLoras[0].RelativePath);

            // Clear all
            vm.ClearAllActive();
            Assert.IsFalse(vm.HasActiveLoras);
            Assert.AreEqual(0, vm.ActiveCount);
        }

        [TestMethod]
        public void TestLoraManagerViewModelFilteringAndSearch()
        {
            var vm = new LoraManagerViewModel();

            vm.AvailableLoras.Add(new LoraModelInfo
            {
                Id = "1",
                Name = "Cyberpunk_Streets.safetensors",
                Title = "Cyberpunk Streets",
                Folder = "SD/Styles",
                Category = "SD",
                TriggerPhrase = "cyberpunk neon lights"
            });

            vm.AvailableLoras.Add(new LoraModelInfo
            {
                Id = "2",
                Name = "Watercolor_Portrait.safetensors",
                Title = "Watercolor Portrait",
                Folder = "SD/Styles",
                Category = "SD",
                TriggerPhrase = "watercolor painting"
            });

            vm.AvailableLoras.Add(new LoraModelInfo
            {
                Id = "3",
                Name = "Detailer.safetensors",
                Title = "Face Detailer",
                Folder = "C",
                Category = "C"
            });

            vm.AvailableLorasView.Refresh();
            Assert.AreEqual(3, vm.FilteredAvailableCount);

            // Search by Title
            vm.SearchText = "Cyber";
            Assert.AreEqual(1, vm.FilteredAvailableCount);

            // Search by Trigger Phrase
            vm.SearchText = "watercolor";
            Assert.AreEqual(1, vm.FilteredAvailableCount);

            // Clear search
            vm.SearchText = "";
            Assert.AreEqual(3, vm.FilteredAvailableCount);

            // Filter by Folder
            vm.SelectedFolder = "C";
            Assert.AreEqual(1, vm.FilteredAvailableCount);

            vm.SelectedFolder = "SD/Styles";
            Assert.AreEqual(2, vm.FilteredAvailableCount);

            vm.SelectedFolder = "All Folders";
            Assert.AreEqual(3, vm.FilteredAvailableCount);
        }

        [TestMethod]
        public async Task TestSwarmUiDiskLoraScanFiltersVideoAndWan()
        {
            var tempLoraDir = Path.Combine(Path.GetTempPath(), $"prompter_lora_test_{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(Path.Combine(tempLoraDir, "SD", "Styles"));
                Directory.CreateDirectory(Path.Combine(tempLoraDir, "C"));
                Directory.CreateDirectory(Path.Combine(tempLoraDir, "WAN", "Video"));
                Directory.CreateDirectory(Path.Combine(tempLoraDir, "NSFW", "WAN"));

                File.WriteAllText(Path.Combine(tempLoraDir, "SD", "Styles", "Retro_Anime.safetensors"), "dummy");
                File.WriteAllText(Path.Combine(tempLoraDir, "C", "Super_Detailer.safetensors"), "dummy");
                File.WriteAllText(Path.Combine(tempLoraDir, "WAN", "Video", "wan2.1_motion.safetensors"), "dummy");
                File.WriteAllText(Path.Combine(tempLoraDir, "NSFW", "WAN", "wan_t2v_action.safetensors"), "dummy");

                var swarmService = new SwarmUiService
                {
                    SwarmLoraPath = tempLoraDir,
                    BaseUrl = "http://localhost:9999" // Unreachable port to force disk scan fallback
                };

                var loras = await swarmService.ListLorasAsync();

                Assert.HasCount(2, loras);
                Assert.IsTrue(loras.Any(l => l.Name.Equals("Retro_Anime.safetensors", StringComparison.OrdinalIgnoreCase)));
                Assert.IsTrue(loras.Any(l => l.Name.Equals("Super_Detailer.safetensors", StringComparison.OrdinalIgnoreCase)));

                // Assert video/wan models are filtered out
                Assert.IsFalse(loras.Any(l => l.Name.Contains("wan", StringComparison.OrdinalIgnoreCase)));
                Assert.IsFalse(loras.Any(l => l.Folder.Contains("wan", StringComparison.OrdinalIgnoreCase)));
            }
            finally
            {
                if (Directory.Exists(tempLoraDir))
                {
                    try { Directory.Delete(tempLoraDir, true); } catch { }
                }
            }
        }

        [TestMethod]
        public void TestMainViewModelActiveLorasPropertiesAndCommands()
        {
            var vm = new MainViewModel();

            Assert.IsFalse(vm.HasActiveLoras);
            Assert.AreEqual(0, vm.ActiveLorasCount);
            Assert.AreEqual("🧬 LoRAs", vm.ActiveLorasChipText);

            var lora = new LoraModelInfo
            {
                Id = "C/Wowifier.safetensors",
                Name = "Wowifier.safetensors",
                Title = "Wowifier",
                RelativePath = "C/Wowifier.safetensors",
                Strength = 0.80
            };

            vm.ActiveLoras.Add(lora);

            Assert.IsTrue(vm.HasActiveLoras);
            Assert.AreEqual(1, vm.ActiveLorasCount);
            Assert.AreEqual("1 active", vm.ActiveLorasCountText);
            Assert.AreEqual("🧬 1 LoRA", vm.ActiveLorasChipText);
            Assert.AreEqual("Wowifier (0.80)", vm.ActiveLorasSummary);

            // Remove active LoRA via command
            vm.RemoveActiveLoraCommand.Execute(lora);
            Assert.IsFalse(vm.HasActiveLoras);
            Assert.AreEqual(0, vm.ActiveLorasCount);
        }
    }
}
