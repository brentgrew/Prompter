/**
 * Prompter Chrome Extension - 1-Click Page Inserter
 * Injected into the active web tab to paste prompt text directly into
 * Gemini, ChatGPT, Claude, Midjourney, SwarmUI, Copilot, or any web textarea/contenteditable.
 * Fully supports Shadow DOM web components (e.g. Gemini's rich-textarea).
 */

(function(promptText) {
  if (!promptText) return { success: false };

  function isEditable(el) {
    if (!el) return false;
    const tag = el.tagName ? el.tagName.toLowerCase() : '';
    if (tag === 'textarea') return !el.readOnly && !el.disabled;
    if (tag === 'input') {
      const type = (el.type || '').toLowerCase();
      return ['text', 'search', 'url', 'email', ''].includes(type) && !el.readOnly && !el.disabled;
    }
    return !!el.isContentEditable;
  }

  function isElementVisible(el) {
    if (!el) return false;
    try {
      const rect = el.getBoundingClientRect();
      const style = window.getComputedStyle(el);
      return (rect.width > 0 || rect.height > 0 || el.offsetWidth > 0 || el.offsetHeight > 0) &&
             style.visibility !== 'hidden' && style.display !== 'none';
    } catch (e) {
      return true;
    }
  }

  // Traverses nested open shadow roots looking for matching selectors
  function querySelectorDeep(selectors, root = document) {
    if (typeof selectors === 'string') selectors = [selectors];

    for (const sel of selectors) {
      try {
        const el = root.querySelector(sel);
        if (el && isElementVisible(el) && (isEditable(el) || el.isContentEditable)) {
          return el;
        }
      } catch (e) {}
    }

    // Traverse child shadow roots
    try {
      const all = root.querySelectorAll('*');
      for (const node of all) {
        if (node.shadowRoot) {
          const found = querySelectorDeep(selectors, node.shadowRoot);
          if (found) return found;
        }
      }
    } catch (e) {}

    return null;
  }

  function findTargetElement() {
    // 1. Check current activeElement (and pierce if it is a shadow host)
    let active = document.activeElement;
    while (active && active.shadowRoot && active.shadowRoot.activeElement) {
      active = active.shadowRoot.activeElement;
    }

    if (active && (isEditable(active) || active.isContentEditable)) {
      return active;
    }

    // 2. Check if activeElement is an iframe with its own activeElement
    if (active && active.tagName === 'IFRAME') {
      try {
        const inner = active.contentDocument && active.contentDocument.activeElement;
        if (inner && (isEditable(inner) || inner.isContentEditable)) {
          return inner;
        }
      } catch (e) {}
    }

    // 3. Gemini specifically: <rich-textarea> (pierce its shadow DOM & light DOM)
    const richTextareas = document.querySelectorAll('rich-textarea');
    for (const rta of richTextareas) {
      if (isElementVisible(rta)) {
        // Shadow DOM check (Gemini standard)
        if (rta.shadowRoot) {
          const shadowEditable = rta.shadowRoot.querySelector(
            'div[contenteditable="true"], .ql-editor, div[role="textbox"], p'
          );
          if (shadowEditable && isElementVisible(shadowEditable)) {
            return shadowEditable;
          }
        }
        // Light DOM fallback
        const lightEditable = rta.querySelector(
          'div[contenteditable="true"], .ql-editor, div[role="textbox"], p'
        );
        if (lightEditable && isElementVisible(lightEditable)) {
          return lightEditable;
        }
        if (isEditable(rta)) return rta;
      }
    }

    // 4. Check for rich editors inside any open shadow roots (e.g. Copilot, custom web components)
    const shadowTarget = querySelectorDeep([
      'div[contenteditable="true"][role="textbox"]',
      'div.ProseMirror[contenteditable="true"]',
      'div.ql-editor[contenteditable="true"]',
      'div[contenteditable="true"]',
      '#prompt-textarea',
      'textarea'
    ]);
    if (shadowTarget) return shadowTarget;

    // 5. Known standard selectors in Light DOM
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

      // Google Gemini & Google AI
      'rich-textarea div[contenteditable="true"]',
      'div[contenteditable="true"][aria-label*="prompt"]',
      'div[contenteditable="true"][aria-label*="Gemini"]',
      'div[contenteditable="true"][role="textbox"]',
      'div.ql-editor[contenteditable="true"]',
      'div.ql-editor',

      // DeepSeek
      'textarea#chat-input',
      'textarea[placeholder*="DeepSeek"]',

      // Microsoft Copilot & Perplexity
      '#userInput',
      'textarea[placeholder*="Ask"]',
      'textarea[placeholder*="Message"]',
      'textarea[placeholder*="Prompt"]',
      'textarea[placeholder*="Search"]',

      // Midjourney / Discord
      'textarea[placeholder*="Imagine"]',
      'div[role="textbox"][contenteditable="true"]',

      // SwarmUI
      '#prompt',
      'textarea.prompt_input',
      'textarea#prompt_text',
      'textarea#main_prompt',

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

  function insertIntoInput(el, text) {
    try {
      el.focus();
    } catch (e) {}

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

      try {
        el.selectionStart = el.selectionEnd = start + text.length;
      } catch (e) {}

      // Dispatch simulated events
      el.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
      el.dispatchEvent(new Event('change', { bubbles: true, cancelable: true }));
      return true;
    }

    // Contenteditable (Gemini, ChatGPT, Claude, ProseMirror, Quill, Lexical)
    if (el.isContentEditable || el.getAttribute('contenteditable') === 'true' || el.classList.contains('ql-editor')) {
      const sel = window.getSelection();

      // Position caret/selection inside el
      if (sel) {
        try {
          const isEmpty = !el.textContent.trim() || el.querySelector('.placeholder') !== null || el.querySelector('br') !== null;
          const range = document.createRange();
          range.selectNodeContents(el);
          if (!isEmpty) {
            range.collapse(false); // append at end
          }
          sel.removeAllRanges();
          sel.addRange(range);
        } catch (e) {}
      }

      // 1. Try document.execCommand first (most reliable for rich text state across frameworks)
      let execSuccess = false;
      try {
        execSuccess = document.execCommand('insertText', false, text);
      } catch (e) {}

      // 2. If execCommand didn't insert text into the DOM, use direct DOM injection
      const sample = text.substring(0, Math.min(15, text.length));
      if (!execSuccess || !el.textContent.includes(sample)) {
        try {
          const p = el.querySelector('p');
          if (p && (!p.textContent.trim() || p.querySelector('br'))) {
            p.textContent = text;
          } else if (p) {
            p.textContent += (p.textContent.endsWith(' ') ? '' : ' ') + text;
          } else if (!el.textContent.trim()) {
            el.innerHTML = `<p>${escapeHtml(text)}</p>`;
          } else {
            el.innerText = text;
          }

          if (sel) {
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false);
            sel.removeAllRanges();
            sel.addRange(range);
          }
        } catch (e) {}

        // Trigger input events for ProseMirror/Quill/Lit/React
        try {
          el.dispatchEvent(new InputEvent('input', {
            bubbles: true,
            cancelable: true,
            inputType: 'insertText',
            data: text
          }));
        } catch (e) {}
      }

      try {
        el.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
        el.dispatchEvent(new Event('change', { bubbles: true, cancelable: true }));
      } catch (e) {}

      return true;
    }

    return false;
  }

  function escapeHtml(str) {
    if (!str) return '';
    return str
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  function showToast(message, isSuccess = true) {
    try {
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
    } catch (e) {}
  }

  // Execution
  const target = findTargetElement();
  if (target) {
    insertIntoInput(target, promptText);
    showToast('Prompt inserted into page!');
    return { success: true };
  } else {
    // If no editable element was found, copy to clipboard as graceful fallback
    navigator.clipboard.writeText(promptText).then(() => {
      showToast('No active text box found — copied to clipboard!', false);
    }).catch(() => {
      showToast('Could not find active text box on page.', false);
    });
    return { success: false, copied: true };
  }
})(window.PROMPTER_INSERT_PAYLOAD);
