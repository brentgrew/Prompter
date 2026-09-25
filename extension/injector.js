/**
 * Prompter Chrome Extension - 1-Click Page Inserter
 * Injected into the active web tab to paste prompt text directly into
 * ChatGPT, Claude, Midjourney, SwarmUI, or any web textarea/contenteditable.
 */

(function(promptText) {
  if (!promptText) return;

  function findTargetElement() {
    // 1. Check current activeElement if it is editable
    const active = document.activeElement;
    if (active && (isEditable(active) || active.isContentEditable)) {
      return active;
    }

    // 2. Check if activeElement is an iframe with its own activeElement
    if (active && active.tagName === 'IFRAME') {
      try {
        const inner = active.contentDocument.activeElement;
        if (inner && (isEditable(inner) || inner.isContentEditable)) {
          return inner;
        }
      } catch (e) {}
    }

    // 3. Known AI Chat Interfaces Selectors
    const selectors = [
      // ChatGPT
      '#prompt-textarea',
      'div[id="prompt-textarea"]',
      'div[contenteditable="true"][data-placeholder]',
      'textarea[data-id="root"]',

      // Claude
      'div.ProseMirror[contenteditable="true"]',
      'div[contenteditable="true"][aria-label*="Claude"]',
      'fieldset div[contenteditable="true"]',

      // Midjourney / Discord
      'textarea[placeholder*="Imagine"]',
      'div[role="textbox"][contenteditable="true"]',

      // SwarmUI
      '#prompt',
      'textarea.prompt_input',
      'textarea#prompt_text',
      'textarea#main_prompt',

      // Google Gemini / Copilot / Perplexity
      'rich-textarea [contenteditable="true"]',
      'div[contenteditable="true"][role="combobox"]',
      'textarea[placeholder*="Ask"]',
      'textarea[placeholder*="Message"]',
      'textarea[placeholder*="Prompt"]',
      'textarea[placeholder*="Search"]',

      // Generic textareas and inputs
      'textarea:not([readonly]):not([disabled])',
      'input[type="text"]:not([readonly]):not([disabled])',
      '[contenteditable="true"]'
    ];

    for (const sel of selectors) {
      try {
        const el = document.querySelector(sel);
        if (el && isElementVisible(el)) {
          return el;
        }
      } catch (e) {}
    }

    return null;
  }

  function isEditable(el) {
    if (!el) return false;
    const tag = el.tagName ? el.tagName.toLowerCase() : '';
    if (tag === 'textarea') return !el.readOnly && !el.disabled;
    if (tag === 'input') {
      const type = (el.type || '').toLowerCase();
      return ['text', 'search', 'url', 'email', ''].includes(type) && !el.readOnly && !el.disabled;
    }
    return el.isContentEditable;
  }

  function isElementVisible(el) {
    if (!el) return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 && window.getComputedStyle(el).visibility !== 'hidden';
  }

  function insertIntoInput(el, text) {
    el.focus();

    // Standard input or textarea
    if (el.tagName && (el.tagName.toLowerCase() === 'textarea' || el.tagName.toLowerCase() === 'input')) {
      const start = el.selectionStart ?? el.value.length;
      const end = el.selectionEnd ?? el.value.length;
      const currentVal = el.value || '';
      const newVal = currentVal.substring(0, start) + text + currentVal.substring(end);

      // React property descriptor override to trigger React's synthetic change tracker
      const proto = el.tagName.toLowerCase() === 'textarea'
        ? window.HTMLTextAreaElement.prototype
        : window.HTMLInputElement.prototype;
      const nativeSetter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;

      if (nativeSetter) {
        nativeSetter.call(el, newVal);
      } else {
        el.value = newVal;
      }

      el.selectionStart = el.selectionEnd = start + text.length;

      // Dispatch simulated events
      el.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
      el.dispatchEvent(new Event('change', { bubbles: true, cancelable: true }));
      return true;
    }

    // Contenteditable (ChatGPT, Claude, ProseMirror, Lexical)
    if (el.isContentEditable) {
      // 1. Try document.execCommand first (safest for undo history & framework listeners)
      let execSuccess = false;
      try {
        execSuccess = document.execCommand('insertText', false, text);
      } catch (e) {}

      if (!execSuccess) {
        // Fallback: Use Selection & Range API
        const sel = window.getSelection();
        if (sel && sel.rangeCount > 0) {
          const range = sel.getRangeAt(0);
          range.deleteContents();
          const textNode = document.createTextNode(text);
          range.insertNode(textNode);
          range.setStartAfter(textNode);
          range.setEndAfter(textNode);
          sel.removeAllRanges();
          sel.addRange(range);
        } else {
          el.innerText = (el.innerText || '') + text;
        }

        // Trigger input events for ProseMirror/Lexical/React
        el.dispatchEvent(new InputEvent('input', {
          bubbles: true,
          cancelable: true,
          inputType: 'insertText',
          data: text
        }));
      }

      el.dispatchEvent(new Event('change', { bubbles: true }));
      return true;
    }

    return false;
  }

  function showToast(message, isSuccess = true) {
    const existing = document.getElementById('prompter-floating-toast');
    if (existing) existing.remove();

    const toast = document.createElement('div');
    toast.id = 'prompter-floating-toast';
    toast.style.cssText = `
      position: fixed;
      bottom: 24px;
      right: 24px;
      z-index: 2147483647;
      background: ${isSuccess ? '#10B981' : '#6366F1'};
      color: #FFFFFF;
      padding: 10px 18px;
      border-radius: 8px;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
      font-size: 13.5px;
      font-weight: 600;
      box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.4), 0 8px 10px -6px rgba(0, 0, 0, 0.3);
      display: flex;
      align-items: center;
      gap: 8px;
      pointer-events: none;
      transition: opacity 0.25s ease, transform 0.25s ease;
      transform: translateY(10px);
      opacity: 0;
    `;

    toast.innerHTML = `<span>⚡</span> <span>${message}</span>`;
    document.body.appendChild(toast);

    requestAnimationFrame(() => {
      toast.style.opacity = '1';
      toast.style.transform = 'translateY(0)';
    });

    setTimeout(() => {
      toast.style.opacity = '0';
      toast.style.transform = 'translateY(10px)';
      setTimeout(() => toast.remove(), 300);
    }, 2200);
  }

  // Execution
  const target = findTargetElement();
  if (target) {
    insertIntoInput(target, promptText);
    showToast('Prompt inserted into page!');
  } else {
    // If no editable element was found, copy to clipboard as graceful fallback
    navigator.clipboard.writeText(promptText).then(() => {
      showToast('No active text box found — copied to clipboard!', false);
    }).catch(() => {
      showToast('Could not find active text box on page.', false);
    });
  }
})(PROMPTER_INSERT_PAYLOAD);
