/**
 * Prompter Chrome Extension - Background Service Worker (Manifest V3)
 */

// Initialize extension settings and context menus on install
chrome.runtime.onInstalled.addListener(() => {
  // Ensure clicking extension icon opens the popup (not side panel immediately)
  if (chrome.sidePanel && chrome.sidePanel.setPanelBehavior) {
    chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: false }).catch(() => {});
  }

  // Create Context Menus
  chrome.contextMenus.create({
    id: 'open_prompter_sidepanel',
    title: '⚡ Open Prompter Side Panel',
    contexts: ['action']
  });

  chrome.contextMenus.create({
    id: 'save_selection_as_prompt',
    title: '💾 Save selection to Prompter',
    contexts: ['selection']
  });
});

// Handle Context Menu clicks
chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === 'open_prompter_sidepanel') {
    if (tab && tab.windowId) {
      await chrome.sidePanel.open({ windowId: tab.windowId });
    }
  } else if (info.menuItemId === 'save_selection_as_prompt') {
    const selectedText = (info.selectionText || '').trim();
    if (!selectedText) return;

    // Load existing vault from storage and insert new prompt into first folder
    chrome.storage.local.get(['prompter_vault'], (res) => {
      let vault = res.prompter_vault;
      if (!vault || !vault.Folders || vault.Folders.length === 0) {
        vault = {
          Version: 1,
          UpdatedAt: new Date().toISOString(),
          Folders: [
            {
              Id: 'folder_general',
              Name: 'General & Productivity',
              IsPasswordProtected: false,
              PromptCount: 0,
              Prompts: []
            }
          ]
        };
      }

      const targetFolder = vault.Folders.find(f => !f.IsPasswordProtected) || vault.Folders[0];
      const titleWords = selectedText.split(/\s+/).slice(0, 5).join(' ');
      const newPrompt = {
        Id: 'prompt_' + Date.now().toString(36) + Math.random().toString(36).substr(2, 5),
        Title: titleWords + (selectedText.length > titleWords.length ? '...' : ''),
        Content: selectedText,
        CreatedAt: new Date().toISOString(),
        UpdatedAt: new Date().toISOString()
      };

      if (!targetFolder.Prompts) targetFolder.Prompts = [];
      targetFolder.Prompts.unshift(newPrompt);
      targetFolder.PromptCount = targetFolder.Prompts.length;
      vault.UpdatedAt = new Date().toISOString();

      chrome.storage.local.set({ prompter_vault: vault }, () => {
        // Show notification or badge
        chrome.action.setBadgeText({ text: '✓' });
        chrome.action.setBadgeBackgroundColor({ color: '#10B981' });
        setTimeout(() => chrome.action.setBadgeText({ text: '' }), 2500);
      });
    });
  }
});

// Handle keyboard shortcuts
chrome.commands.onCommand.addListener(async (command) => {
  if (command === 'open_side_panel') {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab && tab.windowId) {
      await chrome.sidePanel.open({ windowId: tab.windowId });
    }
  }
});

// Handle internal messages
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === 'OPEN_SIDEPANEL') {
    chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
      if (tabs[0] && tabs[0].windowId) {
        chrome.sidePanel.open({ windowId: tabs[0].windowId }).then(() => {
          sendResponse({ success: true });
        }).catch((err) => {
          sendResponse({ success: false, error: err.message });
        });
      } else {
        sendResponse({ success: false, error: 'No active window found' });
      }
    });
    return true; // Keep message channel open for async response
  }
});
