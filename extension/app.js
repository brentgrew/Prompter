/**
 * Prompter Chrome Extension - Main UI Controller
 */

(function () {
  'use strict';

  // State
  let vault = null;
  let activeFolderId = 'all';
  let searchQuery = '';
  const unlockedFolders = new Set();
  let toastTimeout = null;

  // DOM Elements
  const searchInput = document.getElementById('search-input');
  const searchClear = document.getElementById('search-clear');
  const folderTabs = document.getElementById('folder-tabs');
  const promptsList = document.getElementById('prompts-list');
  const btnOpenSidepanel = document.getElementById('btn-open-sidepanel');
  const btnManageFolders = document.getElementById('btn-manage-folders');
  const btnVault = document.getElementById('btn-vault');
  const btnSettings = document.getElementById('btn-settings');
  const btnNewPrompt = document.getElementById('btn-new-prompt');

  // Modals
  const modalPrompt = document.getElementById('modal-prompt');
  const modalPromptTitle = document.getElementById('modal-prompt-title');
  const promptEditId = document.getElementById('prompt-edit-id');
  const promptTitleInput = document.getElementById('prompt-title');
  const promptFolderSelect = document.getElementById('prompt-folder');
  const promptContentInput = document.getElementById('prompt-content');
  const promptStats = document.getElementById('prompt-stats');
  const btnCleanPrompt = document.getElementById('btn-clean-prompt');
  const btnSavePrompt = document.getElementById('btn-save-prompt');

  const modalFolders = document.getElementById('modal-folders');
  const newFolderNameInput = document.getElementById('new-folder-name');
  const btnAddFolder = document.getElementById('btn-add-folder');
  const foldersList = document.getElementById('folders-list');

  const modalVault = document.getElementById('modal-vault');
  const btnExportVault = document.getElementById('btn-export-vault');
  const btnTriggerImport = document.getElementById('btn-trigger-import');
  const vaultFileInput = document.getElementById('vault-file-input');

  const modalUnlock = document.getElementById('modal-unlock');
  const unlockFolderIdInput = document.getElementById('unlock-folder-id');
  const unlockPasswordInput = document.getElementById('unlock-password');
  const btnSubmitUnlock = document.getElementById('btn-submit-unlock');

  const modalSettings = document.getElementById('modal-settings');

  const appToast = document.getElementById('app-toast');
  const toastIcon = document.getElementById('toast-icon');
  const toastText = document.getElementById('toast-text');

  // Theme Management
  const DEFAULT_THEME = 'dark';
  let currentTheme = DEFAULT_THEME;

  async function initTheme() {
    try {
      const data = await chrome.storage.local.get('prompter_theme');
      currentTheme = data.prompter_theme;
      if (currentTheme !== 'light' && currentTheme !== 'dark') {
        currentTheme = DEFAULT_THEME;
      }
    } catch (e) {
      currentTheme = DEFAULT_THEME;
    }
    applyTheme(currentTheme);
    updateThemeUI(currentTheme);
  }

  function applyTheme(theme) {
    if (theme === 'light') {
      document.documentElement.setAttribute('data-theme', 'light');
    } else {
      document.documentElement.setAttribute('data-theme', 'dark');
    }
  }

  function updateThemeUI(theme) {
    const activeTheme = (theme === 'light') ? 'light' : 'dark';
    const radio = document.querySelector(`input[name="prompter-theme"][value="${activeTheme}"]`);
    if (radio) {
      radio.checked = true;
    }
    // Update active highlight class on theme cards
    document.querySelectorAll('.theme-option-card').forEach(card => {
      const cardRadio = card.querySelector('input[type="radio"]');
      if (cardRadio && cardRadio.value === activeTheme) {
        card.classList.add('active');
      } else {
        card.classList.remove('active');
      }
    });
  }

  async function setTheme(theme) {
    currentTheme = (theme === 'light') ? 'light' : 'dark';
    applyTheme(currentTheme);
    updateThemeUI(currentTheme);
    await chrome.storage.local.set({ prompter_theme: currentTheme });
    const label = currentTheme === 'dark' ? 'Dark' : 'Light';
    showToast(`Color theme set to ${label}`, '🎨');
  }

  // Initialization
  async function init() {
    await initTheme();
    vault = await PrompterStorage.loadVault();
    bindEvents();
    renderFolderTabs();
    renderPromptsList();
  }

  // Event Listeners
  function bindEvents() {
    // Open Side Panel
    if (btnOpenSidepanel) {
      btnOpenSidepanel.addEventListener('click', () => {
        chrome.runtime.sendMessage({ action: 'OPEN_SIDEPANEL' }, () => {
          window.close();
        });
      });
    }

    // Modal Triggers
    btnNewPrompt.addEventListener('click', () => openPromptModal());
    btnManageFolders.addEventListener('click', () => openFolderModal());
    btnVault.addEventListener('click', () => openVaultModal());
    if (btnSettings) {
      btnSettings.addEventListener('click', () => {
        updateThemeUI(currentTheme);
        openModal('modal-settings');
      });
    }

    // Theme selector listeners
    document.querySelectorAll('input[name="prompter-theme"]').forEach(radio => {
      radio.addEventListener('change', (e) => {
        if (e.target.checked) {
          setTheme(e.target.value);
        }
      });
    });

    // Cross-view theme sync (Side Panel <-> Popup)
    chrome.storage.onChanged.addListener((changes, area) => {
      if (area === 'local' && changes.prompter_theme) {
        const newTheme = (changes.prompter_theme.newValue === 'light') ? 'light' : 'dark';
        currentTheme = newTheme;
        applyTheme(newTheme);
        updateThemeUI(newTheme);
      }
      if (area === 'local' && changes.prompter_vault && changes.prompter_vault.newValue) {
        vault = changes.prompter_vault.newValue;
        renderFolderTabs();
        renderPromptsList();
        const foldersModal = document.getElementById('modal-folders');
        if (foldersModal && foldersModal.classList.contains('active')) {
          renderFolderManagerList();
        }
      }
    });

    // Modal Close Buttons
    document.querySelectorAll('.modal-close').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const modalId = e.currentTarget.getAttribute('data-modal');
        closeModal(modalId);
      });
    });

    // Close modal when clicking on overlay background
    document.querySelectorAll('.modal-overlay').forEach(overlay => {
      overlay.addEventListener('click', (e) => {
        if (e.target === overlay) {
          overlay.classList.remove('active');
        }
      });
    });

    // Search Input
    searchInput.addEventListener('input', (e) => {
      searchQuery = e.target.value.trim().toLowerCase();
      searchClear.style.display = searchQuery ? 'block' : 'none';
      renderPromptsList();
    });

    searchClear.addEventListener('click', () => {
      searchInput.value = '';
      searchQuery = '';
      searchClear.style.display = 'none';
      searchInput.focus();
      renderPromptsList();
    });

    // Keyboard Shortcuts
    window.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') {
        const openModal = document.querySelector('.modal-overlay.active');
        if (openModal) {
          openModal.classList.remove('active');
        } else if (searchInput.value) {
          searchInput.value = '';
          searchQuery = '';
          searchClear.style.display = 'none';
          renderPromptsList();
        }
      } else if (e.key === '/' && !isTypingInInput(document.activeElement)) {
        e.preventDefault();
        searchInput.focus();
      }
    });

    // Prompt Form Handlers
    promptContentInput.addEventListener('input', updatePromptStats);
    btnCleanPrompt.addEventListener('click', () => {
      const original = promptContentInput.value;
      const cleaned = PrompterStorage.cleanPrompt(original);
      if (cleaned !== original) {
        promptContentInput.value = cleaned;
        updatePromptStats();
        showToast('Cleaned prompt prefixes!');
      } else {
        showToast('Prompt is already clean');
      }
    });
    btnSavePrompt.addEventListener('click', handleSavePrompt);

    // Folder Modal Handlers
    btnAddFolder.addEventListener('click', handleAddFolder);
    newFolderNameInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') handleAddFolder();
    });

    // Vault Modal Handlers
    btnExportVault.addEventListener('click', handleExportVault);
    btnTriggerImport.addEventListener('click', () => vaultFileInput.click());
    vaultFileInput.addEventListener('change', handleImportVaultFile);

    // Password Unlock Handler
    btnSubmitUnlock.addEventListener('click', handleUnlockFolder);
    unlockPasswordInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') handleUnlockFolder();
    });
  }

  function isTypingInInput(el) {
    if (!el) return false;
    const tag = (el.tagName || '').toLowerCase();
    return tag === 'input' || tag === 'textarea' || el.isContentEditable;
  }

  // Toast Notification
  function showToast(message, icon = '⚡') {
    if (!appToast) return;
    toastText.textContent = message;
    toastIcon.textContent = icon;
    appToast.classList.add('active');

    if (toastTimeout) clearTimeout(toastTimeout);
    toastTimeout = setTimeout(() => {
      appToast.classList.remove('active');
    }, 2200);
  }

  // Modals Helper
  function openModal(id) {
    const el = document.getElementById(id);
    if (el) el.classList.add('active');
  }

  function closeModal(id) {
    const el = document.getElementById(id);
    if (el) el.classList.remove('active');
  }

  let draggedTabFolderIndex = null;

  // Folder Tabs Rendering
  function renderFolderTabs() {
    folderTabs.innerHTML = '';

    // Calculate total prompts count
    let totalPrompts = 0;
    vault.Folders.forEach(f => {
      totalPrompts += (f.Prompts ? f.Prompts.length : (f.PromptCount || 0));
    });

    // "All" Tab
    const allTab = document.createElement('div');
    allTab.className = `folder-tab ${activeFolderId === 'all' ? 'active' : ''}`;
    allTab.innerHTML = `<span>All</span> <span class="tab-badge">${totalPrompts}</span>`;
    allTab.addEventListener('click', () => {
      activeFolderId = 'all';
      renderFolderTabs();
      renderPromptsList();
    });
    folderTabs.appendChild(allTab);

    // Individual Folder Tabs
    vault.Folders.forEach((folder, index) => {
      const isLocked = folder.IsPasswordProtected && !unlockedFolders.has(folder.Id);
      const count = isLocked ? '🔒' : (folder.Prompts ? folder.Prompts.length : (folder.PromptCount || 0));

      const tab = document.createElement('div');
      tab.className = `folder-tab ${activeFolderId === folder.Id ? 'active' : ''}`;
      tab.setAttribute('draggable', 'true');
      tab.setAttribute('data-index', index.toString());
      tab.setAttribute('title', `${folder.Name} (drag tab to reorder)`);
      tab.innerHTML = `
        <span>${escapeHtml(folder.Name)}</span>
        <span class="tab-badge">${count}</span>
      `;

      tab.addEventListener('click', () => {
        if (folder.IsPasswordProtected && !unlockedFolders.has(folder.Id)) {
          openUnlockModal(folder.Id);
          return;
        }
        activeFolderId = folder.Id;
        renderFolderTabs();
        renderPromptsList();
      });

      // Drag and drop for tabs
      tab.addEventListener('dragstart', (e) => {
        draggedTabFolderIndex = index;
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', index.toString());
        setTimeout(() => tab.classList.add('tab-dragging'), 0);
      });
      tab.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
        if (draggedTabFolderIndex !== null && draggedTabFolderIndex !== index) {
          tab.classList.add('tab-drag-over');
        }
      });
      tab.addEventListener('dragleave', () => {
        tab.classList.remove('tab-drag-over');
      });
      tab.addEventListener('drop', (e) => {
        e.preventDefault();
        tab.classList.remove('tab-drag-over');
        if (draggedTabFolderIndex !== null && draggedTabFolderIndex !== index) {
          reorderFolder(draggedTabFolderIndex, index);
        }
      });
      tab.addEventListener('dragend', () => {
        tab.classList.remove('tab-dragging');
        document.querySelectorAll('.folder-tab').forEach(t => t.classList.remove('tab-drag-over', 'tab-dragging'));
        draggedTabFolderIndex = null;
      });

      folderTabs.appendChild(tab);
    });
  }

  // Prompts List Rendering
  function renderPromptsList() {
    promptsList.innerHTML = '';

    // Collect all candidate prompts according to activeFolderId
    let items = [];
    vault.Folders.forEach(folder => {
      const isLocked = folder.IsPasswordProtected && !unlockedFolders.has(folder.Id);
      if (isLocked) return; // Protected folders only show when unlocked

      if (activeFolderId === 'all' || activeFolderId === folder.Id) {
        (folder.Prompts || []).forEach(p => {
          items.push({
            prompt: p,
            folder: folder
          });
        });
      }
    });

    // Filter by search query
    if (searchQuery) {
      items = items.filter(({ prompt, folder }) => {
        const titleMatch = (prompt.Title || '').toLowerCase().includes(searchQuery);
        const contentMatch = (prompt.Content || '').toLowerCase().includes(searchQuery);
        const folderMatch = (folder.Name || '').toLowerCase().includes(searchQuery);
        return titleMatch || contentMatch || folderMatch;
      });
    }

    // Empty State
    if (items.length === 0) {
      const emptyDiv = document.createElement('div');
      emptyDiv.className = 'empty-state';
      emptyDiv.innerHTML = `
        <div class="empty-icon">${searchQuery ? '🔍' : '📝'}</div>
        <div class="empty-title">${searchQuery ? 'No matching prompts found' : 'No prompts here yet'}</div>
        <div class="empty-desc">${searchQuery ? 'Try searching for different keywords or clear your filter.' : 'Create your first prompt or import your vault.'}</div>
        <button id="btn-empty-action" class="btn-primary">
          <span>${searchQuery ? 'Clear Search' : '➕ Create Prompt'}</span>
        </button>
      `;

      emptyDiv.querySelector('#btn-empty-action').addEventListener('click', () => {
        if (searchQuery) {
          searchInput.value = '';
          searchQuery = '';
          searchClear.style.display = 'none';
          renderPromptsList();
        } else {
          openPromptModal();
        }
      });

      promptsList.appendChild(emptyDiv);
      return;
    }

    // Render Cards
    items.forEach(({ prompt, folder }) => {
      const card = document.createElement('div');
      card.className = 'prompt-card';

      const wordCount = (prompt.Content || '').trim() ? (prompt.Content || '').trim().split(/\s+/).length : 0;
      const charCount = (prompt.Content || '').length;

      card.innerHTML = `
        <div class="card-top">
          <div class="card-title" title="${escapeHtml(prompt.Title)}">${escapeHtml(prompt.Title)}</div>
          <span class="card-folder-badge">${escapeHtml(folder.Name)}</span>
        </div>
        <div class="card-snippet" title="Click to copy prompt">${escapeHtml(prompt.Content || '')}</div>
        <div class="card-bottom">
          <div class="card-actions">
            <button class="btn-action btn-action-insert" title="Insert prompt directly into active webpage (ChatGPT, Claude, etc.)">⚡</button>
            <button class="btn-action btn-action-copy" title="Copy to clipboard">📋</button>
            <button class="btn-action btn-action-edit" title="Edit prompt">✏️</button>
            <button class="btn-action btn-action-delete" title="Delete prompt">🗑️</button>
          </div>
          <span class="card-stats">${wordCount} words · ${charCount} chars</span>
        </div>
      `;

      // Event: Insert into active tab
      card.querySelector('.btn-action-insert').addEventListener('click', (e) => {
        e.stopPropagation();
        insertPromptIntoActiveTab(prompt.Content);
      });

      // Event: Copy to clipboard
      card.querySelector('.btn-action-copy').addEventListener('click', (e) => {
        e.stopPropagation();
        copyToClipboard(prompt.Content);
      });

      // Event: Clicking card snippet copies to clipboard
      card.querySelector('.card-snippet').addEventListener('click', (e) => {
        e.stopPropagation();
        copyToClipboard(prompt.Content);
      });

      // Event: Edit prompt
      card.querySelector('.btn-action-edit').addEventListener('click', (e) => {
        e.stopPropagation();
        openPromptModal(prompt, folder.Id);
      });

      // Event: Delete prompt
      card.querySelector('.btn-action-delete').addEventListener('click', (e) => {
        e.stopPropagation();
        handleDeletePrompt(prompt.Id, folder.Id);
      });

      promptsList.appendChild(card);
    });
  }

  // 1-Click Page Inserter Logic
  async function insertPromptIntoActiveTab(text) {
    if (!text) return;
    try {
      let [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      if (!tab || !tab.id) {
        const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
        tab = tabs && tabs[0];
      }
      if (!tab || !tab.id) {
        copyToClipboard(text, 'Copied prompt to clipboard (no active web tab)');
        return;
      }

      // Check if restricted URL
      const url = tab.url || '';
      if (url.startsWith('chrome://') || url.startsWith('chrome-extension://') || url.startsWith('edge://') || url.startsWith('about:')) {
        copyToClipboard(text, 'Copied to clipboard (browser internal page)');
        return;
      }

      // Set global variable on active tab and inject script
      await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        func: (payload) => {
          window.PROMPTER_INSERT_PAYLOAD = payload;
        },
        args: [text]
      });

      const results = await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        files: ['injector.js']
      });

      const res = results && results[0] && results[0].result;
      if (res && res.success) {
        showToast('Prompt inserted into page!', '⚡');
      } else if (res && res.copied) {
        showToast('Copied to clipboard (no text box found)', '📋');
      } else {
        showToast('Prompt inserted into page!', '⚡');
      }
    } catch (err) {
      console.warn('Direct injection failed, falling back to clipboard copy:', err);
      copyToClipboard(text, 'Copied to clipboard (insertion fallback)');
    }
  }

  // Clipboard Helper
  function copyToClipboard(text, customToastMsg = 'Copied prompt to clipboard!') {
    navigator.clipboard.writeText(text).then(() => {
      showToast(customToastMsg, '📋');
    }).catch(() => {
      showToast('Failed to copy to clipboard', '❌');
    });
  }

  // Add / Edit Prompt Modal Logic
  function openPromptModal(prompt = null, folderId = null) {
    // Populate folders dropdown
    promptFolderSelect.innerHTML = '';
    vault.Folders.forEach(f => {
      const opt = document.createElement('option');
      opt.value = f.Id;
      opt.textContent = f.Name + (f.IsPasswordProtected ? ' 🔒' : '');
      promptFolderSelect.appendChild(opt);
    });

    if (prompt) {
      modalPromptTitle.textContent = 'Edit Prompt';
      promptEditId.value = prompt.Id;
      promptTitleInput.value = prompt.Title || '';
      promptContentInput.value = prompt.Content || '';
      promptFolderSelect.value = folderId || (vault.Folders[0] ? vault.Folders[0].Id : '');
    } else {
      modalPromptTitle.textContent = 'New Prompt';
      promptEditId.value = '';
      promptTitleInput.value = '';
      promptContentInput.value = '';
      if (activeFolderId !== 'all') {
        promptFolderSelect.value = activeFolderId;
      } else if (vault.Folders.length > 0) {
        promptFolderSelect.value = vault.Folders[0].Id;
      }
    }

    updatePromptStats();
    openModal('modal-prompt');
    promptTitleInput.focus();
  }

  function updatePromptStats() {
    const text = promptContentInput.value;
    const words = text.trim() ? text.trim().split(/\s+/).length : 0;
    const chars = text.length;
    promptStats.textContent = `${words} words · ${chars} chars`;
  }

  async function handleSavePrompt() {
    const id = promptEditId.value;
    const title = promptTitleInput.value.trim() || 'Untitled Prompt';
    const content = promptContentInput.value;
    const folderId = promptFolderSelect.value;

    if (!folderId) {
      showToast('Please select a folder', '⚠️');
      return;
    }

    const targetFolder = vault.Folders.find(f => f.Id === folderId);
    if (!targetFolder) return;
    if (!targetFolder.Prompts) targetFolder.Prompts = [];

    if (id) {
      // Editing existing prompt
      // First remove from any existing folder
      vault.Folders.forEach(f => {
        if (f.Prompts) {
          f.Prompts = f.Prompts.filter(p => p.Id !== id);
          f.PromptCount = f.Prompts.length;
        }
      });

      targetFolder.Prompts.unshift({
        Id: id,
        Title: title,
        Content: content,
        CreatedAt: new Date().toISOString(),
        UpdatedAt: new Date().toISOString()
      });
    } else {
      // Creating new prompt
      const newPrompt = {
        Id: 'prompt_' + Date.now().toString(36) + Math.random().toString(36).substr(2, 5),
        Title: title,
        Content: content,
        CreatedAt: new Date().toISOString(),
        UpdatedAt: new Date().toISOString()
      };
      targetFolder.Prompts.unshift(newPrompt);
    }

    targetFolder.PromptCount = targetFolder.Prompts.length;
    await PrompterStorage.saveVault(vault);

    closeModal('modal-prompt');
    renderFolderTabs();
    renderPromptsList();
    showToast(id ? 'Prompt updated!' : 'Prompt created!');
  }

  async function handleDeletePrompt(promptId, folderId) {
    if (!confirm('Are you sure you want to delete this prompt?')) return;

    const folder = vault.Folders.find(f => f.Id === folderId);
    if (folder && folder.Prompts) {
      folder.Prompts = folder.Prompts.filter(p => p.Id !== promptId);
      folder.PromptCount = folder.Prompts.length;
      await PrompterStorage.saveVault(vault);
      renderFolderTabs();
      renderPromptsList();
      showToast('Prompt deleted');
    }
  }

  // Folder Manager Modal Logic
  function openFolderModal() {
    renderFolderManagerList();
    newFolderNameInput.value = '';
    openModal('modal-folders');
    newFolderNameInput.focus();
  }

  // Folder Reordering Logic
  async function reorderFolder(fromIndex, toIndex) {
    if (fromIndex === toIndex || fromIndex < 0 || toIndex < 0 ||
        fromIndex >= vault.Folders.length || toIndex >= vault.Folders.length) {
      return;
    }
    const [movedFolder] = vault.Folders.splice(fromIndex, 1);
    vault.Folders.splice(toIndex, 0, movedFolder);

    await PrompterStorage.saveVault(vault);
    renderFolderManagerList();
    renderFolderTabs();
    showToast(`Reordered "${movedFolder.Name}"`, '↕️');
  }

  let draggedFolderIndex = null;

  function renderFolderManagerList() {
    foldersList.innerHTML = '';
    vault.Folders.forEach((folder, index) => {
      const count = folder.Prompts ? folder.Prompts.length : (folder.PromptCount || 0);

      const row = document.createElement('div');
      row.className = 'folder-manage-item';
      row.setAttribute('draggable', 'true');
      row.setAttribute('data-id', folder.Id);
      row.setAttribute('data-index', index.toString());

      row.innerHTML = `
        <div class="folder-manage-left">
          <div class="folder-drag-handle" title="Drag to reorder folder">
            <span>⋮⋮</span>
          </div>
          <div class="folder-info">
            <span class="folder-name-text" title="${escapeHtml(folder.Name)}">
              ${escapeHtml(folder.Name)} ${folder.IsPasswordProtected ? '🔒' : ''}
            </span>
            <span class="tab-badge">${count} prompts</span>
          </div>
        </div>
        <div class="folder-manage-actions">
          <button type="button" class="btn-icon btn-rename-folder" title="Rename Folder">✏️</button>
          <button type="button" class="btn-icon btn-delete-folder" title="Delete Folder">🗑️</button>
        </div>
      `;

      // Rename & Delete click handlers
      row.querySelector('.btn-rename-folder').addEventListener('click', (e) => {
        e.stopPropagation();
        handleRenameFolder(folder.Id);
      });
      row.querySelector('.btn-delete-folder').addEventListener('click', (e) => {
        e.stopPropagation();
        handleDeleteFolder(folder.Id);
      });

      // Drag and drop events for modal folder row
      row.addEventListener('dragstart', (e) => {
        if (e.target.closest('button')) {
          e.preventDefault();
          return;
        }
        draggedFolderIndex = index;
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', index.toString());
        setTimeout(() => row.classList.add('is-dragging'), 0);
      });

      row.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
        if (draggedFolderIndex === null || draggedFolderIndex === index) return;

        const rect = row.getBoundingClientRect();
        const midY = rect.top + rect.height / 2;
        if (e.clientY < midY) {
          row.classList.add('drag-over-top');
          row.classList.remove('drag-over-bottom');
        } else {
          row.classList.add('drag-over-bottom');
          row.classList.remove('drag-over-top');
        }
      });

      row.addEventListener('dragleave', () => {
        row.classList.remove('drag-over-top', 'drag-over-bottom');
      });

      row.addEventListener('drop', (e) => {
        e.preventDefault();
        row.classList.remove('drag-over-top', 'drag-over-bottom');
        if (draggedFolderIndex === null || draggedFolderIndex === index) return;

        const rect = row.getBoundingClientRect();
        const midY = rect.top + rect.height / 2;
        const isBelow = e.clientY >= midY;
        let targetIndex = isBelow ? index + 1 : index;
        if (draggedFolderIndex < targetIndex) {
          targetIndex--;
        }
        if (draggedFolderIndex !== targetIndex) {
          reorderFolder(draggedFolderIndex, targetIndex);
        }
      });

      row.addEventListener('dragend', () => {
        document.querySelectorAll('.folder-manage-item').forEach(el => {
          el.classList.remove('drag-over-top', 'drag-over-bottom', 'is-dragging');
        });
        draggedFolderIndex = null;
      });

      foldersList.appendChild(row);
    });
  }

  async function handleAddFolder() {
    const name = newFolderNameInput.value.trim();
    if (!name) return;

    const exists = vault.Folders.some(f => f.Name.toLowerCase() === name.toLowerCase());
    if (exists) {
      showToast('A folder with this name already exists', '⚠️');
      return;
    }

    const newFolder = {
      Id: 'folder_' + Date.now().toString(36) + Math.random().toString(36).substr(2, 5),
      Name: name,
      IsPasswordProtected: false,
      PromptCount: 0,
      Prompts: []
    };

    vault.Folders.push(newFolder);
    await PrompterStorage.saveVault(vault);

    newFolderNameInput.value = '';
    renderFolderManagerList();
    renderFolderTabs();
    showToast('Folder created!');
  }

  async function handleRenameFolder(folderId) {
    const folder = vault.Folders.find(f => f.Id === folderId);
    if (!folder) return;

    const newName = prompt('Enter new folder name:', folder.Name);
    if (newName && newName.trim() && newName.trim() !== folder.Name) {
      folder.Name = newName.trim();
      await PrompterStorage.saveVault(vault);
      renderFolderManagerList();
      renderFolderTabs();
      renderPromptsList();
      showToast('Folder renamed');
    }
  }

  async function handleDeleteFolder(folderId) {
    if (vault.Folders.length <= 1) {
      showToast('Cannot delete the last remaining folder', '⚠️');
      return;
    }

    const folder = vault.Folders.find(f => f.Id === folderId);
    if (!folder) return;

    const count = folder.Prompts ? folder.Prompts.length : (folder.PromptCount || 0);
    const msg = count > 0
      ? `Folder "${folder.Name}" contains ${count} prompts. Are you sure you want to delete it? All prompts inside will be permanently deleted.`
      : `Delete folder "${folder.Name}"?`;

    if (!confirm(msg)) return;

    vault.Folders = vault.Folders.filter(f => f.Id !== folderId);
    if (activeFolderId === folderId) {
      activeFolderId = 'all';
    }

    await PrompterStorage.saveVault(vault);
    renderFolderManagerList();
    renderFolderTabs();
    renderPromptsList();
    showToast('Folder deleted');
  }

  // Vault Backup & Sync Modal Logic
  function openVaultModal() {
    openModal('modal-vault');
  }

  function handleExportVault() {
    const jsonStr = PrompterStorage.exportVaultJson(vault);
    const blob = new Blob([jsonStr], { type: 'application/json;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `prompter_vault_${new Date().toISOString().slice(0, 10)}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
    showToast('Vault exported to JSON!', '📥');
  }

  async function handleImportVaultFile(e) {
    const file = e.target.files && e.target.files[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = async (evt) => {
      try {
        const text = evt.target.result;
        const mode = document.querySelector('input[name="import-mode"]:checked')?.value || 'merge';
        vault = await PrompterStorage.importVaultJson(text, mode);
        closeModal('modal-vault');
        renderFolderTabs();
        renderPromptsList();
        showToast('Vault imported successfully!', '✅');
      } catch (err) {
        alert('Failed to import vault: ' + err.message);
      } finally {
        vaultFileInput.value = '';
      }
    };
    reader.readAsText(file);
  }

  // Password Unlock Logic
  function openUnlockModal(folderId) {
    unlockFolderIdInput.value = folderId;
    unlockPasswordInput.value = '';
    openModal('modal-unlock');
    unlockPasswordInput.focus();
  }

  async function handleUnlockFolder() {
    const folderId = unlockFolderIdInput.value;
    const password = unlockPasswordInput.value;
    const folder = vault.Folders.find(f => f.Id === folderId);

    if (!folder) return;

    if (!folder.PasswordSalt || !folder.PasswordHash) {
      // In case salt/hash missing, unlock directly
      unlockedFolders.add(folderId);
      closeModal('modal-unlock');
      activeFolderId = folderId;
      renderFolderTabs();
      renderPromptsList();
      return;
    }

    const isValid = await PrompterCrypto.verifyPassword(password, folder.PasswordSalt, folder.PasswordHash);
    if (isValid) {
      unlockedFolders.add(folderId);

      // If EncryptedPayload exists, decrypt prompts
      if (folder.EncryptedPayload && (!folder.Prompts || folder.Prompts.length === 0)) {
        try {
          const decryptedJson = await PrompterCrypto.decrypt(folder.EncryptedPayload, password, folder.PasswordSalt);
          const parsed = JSON.parse(decryptedJson);
          folder.Prompts = (Array.isArray(parsed) ? parsed : []).map(p => ({
            Id: p.Id || p.id || 'p_' + Math.random().toString(36).substr(2, 9),
            Title: p.Title || p.title || 'Untitled Prompt',
            Content: p.Content || p.content || '',
            CreatedAt: p.CreatedAt || p.createdAt || new Date().toISOString(),
            UpdatedAt: p.UpdatedAt || p.updatedAt || new Date().toISOString()
          }));
          folder.PromptCount = folder.Prompts.length;
        } catch (e) {
          console.error('Decryption failed:', e);
        }
      }

      closeModal('modal-unlock');
      activeFolderId = folderId;
      renderFolderTabs();
      renderPromptsList();
      showToast('Folder unlocked!', '🔓');
    } else {
      showToast('Incorrect password', '❌');
      unlockPasswordInput.select();
    }
  }

  // HTML escaping helper to prevent XSS
  function escapeHtml(str) {
    if (!str) return '';
    return str
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  // Start app on DOMContentLoaded
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
