using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prompter.Models;

namespace Prompter.Services
{
    public class StorageService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _filePath;

        public StorageService(string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath))
            {
                _filePath = customPath;
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var folder = Path.Combine(appData, "Prompter");
                Directory.CreateDirectory(folder);
                _filePath = Path.Combine(folder, "prompts_vault.json");
            }
        }

        public string FilePath => _filePath;

        public ObservableCollection<PromptFolder> LoadVault()
        {
            if (!File.Exists(_filePath))
            {
                var initialFolders = CreateDefaultVault();
                SaveVault(initialFolders);
                return initialFolders;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var vaultDto = JsonSerializer.Deserialize<VaultDataDto>(json, JsonOptions);
                if (vaultDto == null || vaultDto.Folders == null)
                {
                    return CreateDefaultVault();
                }

                var folders = new ObservableCollection<PromptFolder>();
                foreach (var folderDto in vaultDto.Folders)
                {
                    var folder = new PromptFolder
                    {
                        Id = folderDto.Id ?? Guid.NewGuid().ToString("N"),
                        Name = folderDto.Name ?? "Untitled Folder",
                        IsPasswordProtected = folderDto.IsPasswordProtected,
                        PasswordSalt = folderDto.PasswordSalt,
                        PasswordHash = folderDto.PasswordHash,
                        EncryptedPayload = folderDto.EncryptedPayload,
                        CachedPromptCount = folderDto.PromptCount
                    };

                    if (folder.IsPasswordProtected)
                    {
                        folder.IsUnlocked = false;
                        folder.SessionPassword = null;
                    }
                    else
                    {
                        folder.IsUnlocked = true;
                        if (folderDto.Prompts != null)
                        {
                            foreach (var pDto in folderDto.Prompts)
                            {
                                folder.Prompts.Add(new PromptItem
                                {
                                    Id = pDto.Id ?? Guid.NewGuid().ToString("N"),
                                    Title = pDto.Title ?? "Untitled Prompt",
                                    Content = pDto.Content ?? string.Empty,
                                    CreatedAt = pDto.CreatedAt ?? DateTime.Now,
                                    UpdatedAt = pDto.UpdatedAt ?? DateTime.Now
                                });
                            }
                        }
                    }

                    folders.Add(folder);
                }

                return folders;
            }
            catch
            {
                // Fallback to default if file was corrupted or unreadable
                return CreateDefaultVault();
            }
        }

        public void SaveVault(IEnumerable<PromptFolder> folders)
        {
            var vaultDto = new VaultDataDto
            {
                Version = 1,
                UpdatedAt = DateTime.UtcNow,
                Folders = new List<FolderDto>()
            };

            foreach (var folder in folders)
            {
                var folderDto = new FolderDto
                {
                    Id = folder.Id,
                    Name = folder.Name,
                    IsPasswordProtected = folder.IsPasswordProtected,
                    PasswordSalt = folder.PasswordSalt,
                    PasswordHash = folder.PasswordHash,
                    EncryptedPayload = folder.EncryptedPayload,
                    PromptCount = folder.PromptCount
                };

                if (folder.IsPasswordProtected)
                {
                    if (folder.IsUnlocked && !string.IsNullOrEmpty(folder.SessionPassword) && !string.IsNullOrEmpty(folder.PasswordSalt))
                    {
                        // Folder is open in this session: re-encrypt prompts before writing to disk
                        var promptsList = new List<PromptDto>();
                        foreach (var p in folder.Prompts)
                        {
                            promptsList.Add(new PromptDto
                            {
                                Id = p.Id,
                                Title = p.Title,
                                Content = p.Content,
                                CreatedAt = p.CreatedAt,
                                UpdatedAt = p.UpdatedAt
                            });
                        }

                        var promptsJson = JsonSerializer.Serialize(promptsList, JsonOptions);
                        folderDto.EncryptedPayload = CryptoService.Encrypt(promptsJson, folder.SessionPassword, folder.PasswordSalt);
                        folder.EncryptedPayload = folderDto.EncryptedPayload;
                        folderDto.PromptCount = folder.Prompts.Count;
                    }
                    else
                    {
                        // Folder is locked: keep existing encrypted payload
                        folderDto.EncryptedPayload = folder.EncryptedPayload;
                        folderDto.PromptCount = folder.CachedPromptCount;
                    }

                    folderDto.Prompts = null; // NEVER save plaintext for protected folders
                }
                else
                {
                    // Unprotected folder: serialize prompts directly
                    folderDto.EncryptedPayload = null;
                    folderDto.Prompts = new List<PromptDto>();
                    foreach (var p in folder.Prompts)
                    {
                        folderDto.Prompts.Add(new PromptDto
                        {
                            Id = p.Id,
                            Title = p.Title,
                            Content = p.Content,
                            CreatedAt = p.CreatedAt,
                            UpdatedAt = p.UpdatedAt
                        });
                    }
                    folderDto.PromptCount = folder.Prompts.Count;
                }

                vaultDto.Folders.Add(folderDto);
            }

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(vaultDto, JsonOptions);
            var tempFile = _filePath + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _filePath, overwrite: true);
        }

        public string ExportVaultJson(IEnumerable<PromptFolder> folders)
        {
            var vaultDto = new VaultDataDto
            {
                Version = 1,
                UpdatedAt = DateTime.UtcNow,
                Folders = new List<FolderDto>()
            };

            foreach (var folder in folders)
            {
                var folderDto = new FolderDto
                {
                    Id = folder.Id,
                    Name = folder.Name,
                    IsPasswordProtected = folder.IsPasswordProtected,
                    PasswordSalt = folder.PasswordSalt,
                    PasswordHash = folder.PasswordHash,
                    EncryptedPayload = folder.EncryptedPayload,
                    PromptCount = folder.PromptCount
                };

                if (folder.IsPasswordProtected)
                {
                    if (folder.IsUnlocked && !string.IsNullOrEmpty(folder.SessionPassword) && !string.IsNullOrEmpty(folder.PasswordSalt))
                    {
                        var promptsList = new List<PromptDto>();
                        foreach (var p in folder.Prompts)
                        {
                            promptsList.Add(new PromptDto
                            {
                                Id = p.Id,
                                Title = p.Title,
                                Content = p.Content,
                                CreatedAt = p.CreatedAt,
                                UpdatedAt = p.UpdatedAt
                            });
                        }

                        var promptsJson = JsonSerializer.Serialize(promptsList, JsonOptions);
                        folderDto.EncryptedPayload = CryptoService.Encrypt(promptsJson, folder.SessionPassword, folder.PasswordSalt);
                        folderDto.PromptCount = folder.Prompts.Count;
                    }
                    else
                    {
                        folderDto.EncryptedPayload = folder.EncryptedPayload;
                        folderDto.PromptCount = folder.CachedPromptCount;
                    }
                    folderDto.Prompts = null;
                }
                else
                {
                    folderDto.EncryptedPayload = null;
                    folderDto.Prompts = new List<PromptDto>();
                    foreach (var p in folder.Prompts)
                    {
                        folderDto.Prompts.Add(new PromptDto
                        {
                            Id = p.Id,
                            Title = p.Title,
                            Content = p.Content,
                            CreatedAt = p.CreatedAt,
                            UpdatedAt = p.UpdatedAt
                        });
                    }
                    folderDto.PromptCount = folder.Prompts.Count;
                }

                vaultDto.Folders.Add(folderDto);
            }

            return JsonSerializer.Serialize(vaultDto, JsonOptions);
        }

        public (int FolderCount, int PromptCount) ExportVaultToFile(IEnumerable<PromptFolder> folders, string destinationPath)
        {
            var folderList = folders is IList<PromptFolder> list ? list : new List<PromptFolder>(folders);
            var json = ExportVaultJson(folderList);

            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(destinationPath, json);

            int totalPrompts = 0;
            foreach (var f in folderList)
            {
                totalPrompts += f.PromptCount;
            }

            return (folderList.Count, totalPrompts);
        }

        public bool TryUnlockFolder(PromptFolder folder, string password, out string? errorMessage)
        {
            errorMessage = null;

            if (!folder.IsPasswordProtected)
            {
                folder.IsUnlocked = true;
                return true;
            }

            if (string.IsNullOrEmpty(folder.PasswordSalt) || string.IsNullOrEmpty(folder.PasswordHash))
            {
                errorMessage = "Folder security configuration is missing.";
                return false;
            }

            if (!CryptoService.VerifyPassword(password, folder.PasswordSalt, folder.PasswordHash))
            {
                errorMessage = "Incorrect password. Please try again.";
                return false;
            }

            try
            {
                folder.Prompts.Clear();
                if (!string.IsNullOrEmpty(folder.EncryptedPayload))
                {
                    var decryptedJson = CryptoService.Decrypt(folder.EncryptedPayload, password, folder.PasswordSalt);
                    var promptDtos = JsonSerializer.Deserialize<List<PromptDto>>(decryptedJson, JsonOptions);
                    if (promptDtos != null)
                    {
                        foreach (var p in promptDtos)
                        {
                            folder.Prompts.Add(new PromptItem
                            {
                                Id = p.Id ?? Guid.NewGuid().ToString("N"),
                                Title = p.Title ?? "Untitled Prompt",
                                Content = p.Content ?? string.Empty,
                                CreatedAt = p.CreatedAt ?? DateTime.Now,
                                UpdatedAt = p.UpdatedAt ?? DateTime.Now
                            });
                        }
                    }
                }

                folder.SessionPassword = password;
                folder.IsUnlocked = true;
                folder.CachedPromptCount = folder.Prompts.Count;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "Failed to decrypt prompts: " + ex.Message;
                return false;
            }
        }

        private ObservableCollection<PromptFolder> CreateDefaultVault()
        {
            var folders = new ObservableCollection<PromptFolder>();

            // 1. General & Productivity
            var general = new PromptFolder("General & Productivity", isPasswordProtected: false);
            general.Prompts.Add(new PromptItem(
                "Professional Email Polish",
                "Please review and rewrite the following email to make it concise, polite, professional, and clear. Preserve the original intent, maintain a collaborative tone, and fix any grammatical or phrasing issues:\n\n[PASTE EMAIL HERE]"
            ));
            general.Prompts.Add(new PromptItem(
                "Meeting Notes & Action Items",
                "Extract the key discussion points, decisions made, and numbered action items (with owner and deadline if mentioned) from the following raw meeting transcript:\n\n[PASTE TRANSCRIPT HERE]"
            ));
            folders.Add(general);

            // 2. Coding & Software Design
            var dev = new PromptFolder("Software Development", isPasswordProtected: false);
            dev.Prompts.Add(new PromptItem(
                "Senior Code Reviewer",
                "Act as a Principal Software Engineer conducting a thorough code review. Review the code below for:\n1. Bugs, race conditions, edge cases, and nullability issues\n2. Performance bottlenecks and algorithmic efficiency\n3. Clean architecture, separation of concerns, and idiomatic style\n4. Concrete suggestions with refactored code snippets\n\n```[language]\n[PASTE CODE HERE]\n```"
            ));
            dev.Prompts.Add(new PromptItem(
                "Unit Test Generator",
                "Generate comprehensive unit tests for the following class/function. Cover normal paths, boundary values, error/exception cases, and mock any external dependencies. Use standard testing frameworks and assertions:\n\n[PASTE CODE HERE]"
            ));
            dev.Prompts.Add(new PromptItem(
                "Bug Root-Cause Diagnostic",
                "I am encountering the following bug/stack trace in my application. Analyze the symptoms, explain the most likely root causes in order of probability, and provide step-by-step diagnostic steps and code fixes:\n\nError Message / Log:\n[PASTE LOG HERE]\n\nRelevant Code:\n[PASTE CODE HERE]"
            ));
            folders.Add(dev);

            // 3. Demo Locked Folder
            var salt = CryptoService.GenerateSaltBase64();
            var demoPassword = "password123";
            var hash = CryptoService.HashPassword(demoPassword, salt);
            var samplePrivatePrompts = new List<PromptDto>
            {
                new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Confidential Client Briefing",
                    Content = "Analyze the attached sensitive client project scope. Ensure no proprietary trade secrets or vendor names are leaked. Summarize deliverable milestones and risk factors:\n\n[PASTE SENSITIVE SPEC HERE]",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                },
                new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Private API Deployment Checklist",
                    Content = "Verify all environment variables, production secrets, TLS certificates, and OAuth callbacks before deploying the AI gateway to production.\n\nChecklist:\n- [ ] Database credentials rotated\n- [ ] OpenAI / Gemini API keys scoped\n- [ ] Rate limits configured",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                }
            };
            var privateJson = JsonSerializer.Serialize(samplePrivatePrompts, JsonOptions);
            var encrypted = CryptoService.Encrypt(privateJson, demoPassword, salt);

            var secureFolder = new PromptFolder("Confidential (Demo Password: password123)", isPasswordProtected: true)
            {
                PasswordSalt = salt,
                PasswordHash = hash,
                EncryptedPayload = encrypted,
                CachedPromptCount = samplePrivatePrompts.Count,
                IsUnlocked = false
            };
            folders.Add(secureFolder);

            return folders;
        }

        #region DTOs
        public class VaultDataDto
        {
            public int Version { get; set; } = 1;
            public DateTime UpdatedAt { get; set; }
            public List<FolderDto> Folders { get; set; } = new();
        }

        public class FolderDto
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public bool IsPasswordProtected { get; set; }
            public string? PasswordSalt { get; set; }
            public string? PasswordHash { get; set; }
            public string? EncryptedPayload { get; set; }
            public int PromptCount { get; set; }
            public List<PromptDto>? Prompts { get; set; }
        }

        public class PromptDto
        {
            public string? Id { get; set; }
            public string? Title { get; set; }
            public string? Content { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }
        #endregion
    }
}
