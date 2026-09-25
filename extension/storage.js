/**
 * Prompter Chrome Extension - Storage & Vault Management
 * Manages prompts, folders, auto-cleaning, and prompter_vault.json import/export.
 */

const PrompterStorage = {
  STORAGE_KEY: 'prompter_vault',

  // Cleans leading prompt prefixes matching Prompter desktop StripLeadingPromptPrefix
  cleanPrompt(text) {
    if (!text) return '';
    let result = text.trim();

    // Strip markdown code fences if wrapped in ``` ... ```
    if (result.startsWith('```') && result.endsWith('```') && result.length > 6) {
      const firstLineEnd = result.indexOf('\n');
      const lastLineStart = result.lastIndexOf('\n');
      if (firstLineEnd > 0 && lastLineStart > firstLineEnd) {
        result = result.substring(firstLineEnd + 1, lastLineStart).trim();
      }
    }

    const prefixes = [
      '**prompt:**',
      '**prompt**:',
      '**prompt** -',
      '**prompt**-',
      '**prompt**',
      '### prompt:',
      '### prompt',
      '## prompt:',
      '## prompt',
      '# prompt:',
      '# prompt',
      '**image prompt:**',
      '**image prompt**:',
      '**image prompt** -',
      '**image prompt**',
      'image prompt:',
      'image prompt -',
      'positive prompt:',
      'positive prompt -',
      '**positive prompt:**',
      '**positive prompt**:',
      '**positive prompt**',
      'prompt:',
      'prompt -'
    ];

    let stripped = true;
    while (stripped) {
      stripped = false;

      // Strip wrapping quotes
      if ((result.startsWith('"') && result.endsWith('"') && result.length >= 2) ||
          (result.startsWith('“') && result.endsWith('”') && result.length >= 2) ||
          (result.startsWith("'") && result.endsWith("'") && result.length >= 2)) {
        result = result.substring(1, result.length - 1).trim();
        stripped = true;
      }

      const lower = result.toLowerCase();
      for (const prefix of prefixes) {
        if (lower.startsWith(prefix)) {
          // If bare prefix, ensure it's followed by colon, dash, space, or newline
          if (['**prompt**', '### prompt', '## prompt', '# prompt', '**image prompt**', '**positive prompt**'].includes(prefix)) {
            const rest = result.substring(prefix.length);
            if (rest.length > 0 && !/[\s:\-–—]/.test(rest[0])) {
              continue;
            }
          }

          let after = result.substring(prefix.length).trim();
          after = after.replace(/^[\s:\-–—]+/, '').trim();
          result = after;
          stripped = true;
          break;
        }
      }
    }

    return result;
  },

  createDefaultVault() {
    return {
      Version: 1,
      UpdatedAt: new Date().toISOString(),
      Folders: [
        {
          Id: 'folder_general',
          Name: 'General & Productivity',
          IsPasswordProtected: false,
          PromptCount: 2,
          Prompts: [
            {
              Id: 'p_email_polish',
              Title: 'Professional Email Polish',
              Content: 'Please review and rewrite the following email to make it concise, polite, professional, and clear. Preserve the original intent, maintain a collaborative tone, and fix any grammatical or phrasing issues:\n\n[PASTE EMAIL HERE]',
              CreatedAt: new Date().toISOString(),
              UpdatedAt: new Date().toISOString()
            },
            {
              Id: 'p_meeting_notes',
              Title: 'Meeting Notes & Action Items',
              Content: 'Extract the key discussion points, decisions made, and numbered action items (with owner and deadline if mentioned) from the following raw meeting transcript:\n\n[PASTE TRANSCRIPT HERE]',
              CreatedAt: new Date().toISOString(),
              UpdatedAt: new Date().toISOString()
            }
          ]
        },
        {
          Id: 'folder_dev',
          Name: 'Software Development',
          IsPasswordProtected: false,
          PromptCount: 3,
          Prompts: [
            {
              Id: 'p_code_review',
              Title: 'Senior Code Reviewer',
              Content: 'Act as a Principal Software Engineer conducting a thorough code review. Review the code below for:\n1. Bugs, race conditions, edge cases, and nullability issues\n2. Performance bottlenecks and algorithmic efficiency\n3. Clean architecture, separation of concerns, and idiomatic style\n4. Concrete suggestions with refactored code snippets\n\n```[language]\n[PASTE CODE HERE]\n```',
              CreatedAt: new Date().toISOString(),
              UpdatedAt: new Date().toISOString()
            },
            {
              Id: 'p_unit_tests',
              Title: 'Unit Test Generator',
              Content: 'Generate comprehensive unit tests for the following class/function. Cover normal paths, boundary values, error/exception cases, and mock any external dependencies. Use standard testing frameworks and assertions:\n\n[PASTE CODE HERE]',
              CreatedAt: new Date().toISOString(),
              UpdatedAt: new Date().toISOString()
            },
            {
              Id: 'p_bug_diagnostic',
              Title: 'Bug Root-Cause Diagnostic',
              Content: 'I am encountering the following bug/stack trace in my application. Analyze the symptoms, explain the most likely root causes in order of probability, and provide step-by-step diagnostic steps and code fixes:\n\nError Message / Log:\n[PASTE LOG HERE]\n\nRelevant Code:\n[PASTE CODE HERE]',
              CreatedAt: new Date().toISOString(),
              UpdatedAt: new Date().toISOString()
            }
          ]
        },
        {
          Id: 'folder_creative',
          Name: 'Creative & Images',
          IsPasswordProtected: false,
          PromptCount: 2,
          Prompts: [
            {
              Id: 'p_photorealistic_portrait',
              Title: 'Cinematic Portrait Prompt',
              Content: 'A striking hyper-realistic portrait photograph, dramatic rim lighting, 85mm lens f/1.4, cinematic atmosphere, 8k resolution, photorealistic skin textures, intricate details.',
              CreatedAt: new Date().toISOString(),
              UpdatedAt: new Date().toISOString()
            },
            {
              Id: 'p_scifi_landscape',
              Title: 'Cyberpunk Sci-Fi Landscape',
              Content: 'Vibrant cyberpunk metropolis at twilight, neon reflections in rain-slicked pavement, flying vehicles in foggy distance, towering holographic advertisements, octane render, photorealistic.',
              CreatedAt: new Date().toISOString(),
              UpdatedAt: new Date().toISOString()
            }
          ]
        }
      ]
    };
  },

  async loadVault() {
    return new Promise((resolve) => {
      chrome.storage.local.get([this.STORAGE_KEY], (res) => {
        let vault = res[this.STORAGE_KEY];
        if (!vault || !vault.Folders || vault.Folders.length === 0) {
          vault = this.createDefaultVault();
          this.saveVault(vault);
        }
        resolve(vault);
      });
    });
  },

  async saveVault(vault) {
    vault.UpdatedAt = new Date().toISOString();
    // Update cached prompt counts
    if (vault.Folders) {
      vault.Folders.forEach(f => {
        f.PromptCount = f.Prompts ? f.Prompts.length : (f.PromptCount || 0);
      });
    }
    return new Promise((resolve) => {
      chrome.storage.local.set({ [this.STORAGE_KEY]: vault }, () => {
        resolve(vault);
      });
    });
  },

  exportVaultJson(vault) {
    const exportDto = {
      Version: vault.Version || 1,
      UpdatedAt: new Date().toISOString(),
      Folders: (vault.Folders || []).map(f => ({
        Id: f.Id || 'f_' + Math.random().toString(36).substr(2, 9),
        Name: f.Name || 'Untitled Folder',
        IsPasswordProtected: !!f.IsPasswordProtected,
        PasswordSalt: f.PasswordSalt || null,
        PasswordHash: f.PasswordHash || null,
        EncryptedPayload: f.EncryptedPayload || null,
        PromptCount: f.Prompts ? f.Prompts.length : (f.PromptCount || 0),
        Prompts: (f.Prompts || []).map(p => ({
          Id: p.Id || 'p_' + Math.random().toString(36).substr(2, 9),
          Title: p.Title || 'Untitled Prompt',
          Content: p.Content || '',
          CreatedAt: p.CreatedAt || new Date().toISOString(),
          UpdatedAt: p.UpdatedAt || new Date().toISOString()
        }))
      }))
    };

    return JSON.stringify(exportDto, null, 2);
  },

  async importVaultJson(jsonText, mode = 'merge') {
    let parsed;
    try {
      parsed = JSON.parse(jsonText);
    } catch (e) {
      throw new Error('Invalid JSON file format.');
    }

    // Support both PascalCase and camelCase fields
    const rawFolders = parsed.Folders || parsed.folders;
    if (!rawFolders || !Array.isArray(rawFolders)) {
      throw new Error('Invalid Prompter vault schema: missing Folders list.');
    }

    const normalizedFolders = rawFolders.map(rf => {
      const rawPrompts = rf.Prompts || rf.prompts || [];
      const prompts = rawPrompts.map(rp => ({
        Id: rp.Id || rp.id || 'p_' + Date.now().toString(36) + Math.random().toString(36).substr(2, 5),
        Title: rp.Title || rp.title || 'Untitled Prompt',
        Content: rp.Content || rp.content || '',
        CreatedAt: rp.CreatedAt || rp.createdAt || new Date().toISOString(),
        UpdatedAt: rp.UpdatedAt || rp.updatedAt || new Date().toISOString()
      }));

      return {
        Id: rf.Id || rf.id || 'f_' + Date.now().toString(36) + Math.random().toString(36).substr(2, 5),
        Name: rf.Name || rf.name || 'Untitled Folder',
        IsPasswordProtected: !!(rf.IsPasswordProtected ?? rf.isPasswordProtected),
        PasswordSalt: rf.PasswordSalt || rf.passwordSalt || null,
        PasswordHash: rf.PasswordHash || rf.passwordHash || null,
        EncryptedPayload: rf.EncryptedPayload || rf.encryptedPayload || null,
        PromptCount: prompts.length || rf.PromptCount || rf.promptCount || 0,
        Prompts: prompts
      };
    });

    const currentVault = await this.loadVault();

    if (mode === 'replace') {
      currentVault.Folders = normalizedFolders;
    } else {
      // Merge mode
      normalizedFolders.forEach(newFolder => {
        const existingFolder = currentVault.Folders.find(ef => ef.Name.toLowerCase() === newFolder.Name.toLowerCase());
        if (existingFolder) {
          if (!existingFolder.Prompts) existingFolder.Prompts = [];
          newFolder.Prompts.forEach(np => {
            const exists = existingFolder.Prompts.some(ep => ep.Title.toLowerCase() === np.Title.toLowerCase());
            if (!exists) {
              existingFolder.Prompts.push(np);
            }
          });
          existingFolder.PromptCount = existingFolder.Prompts.length;
        } else {
          currentVault.Folders.push(newFolder);
        }
      });
    }

    await this.saveVault(currentVault);
    return currentVault;
  }
};
