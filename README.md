# Prompter - AI Prompt Manager & Secure Vault

A modern, high-performance Windows desktop application built with C# and WPF (.NET 10) for managing, organizing, securing, and quickly copying AI prompts.

---

## Features

- **Runs in System Tray & Global Pause/Break Hotkey**:
  - Runs in the Windows notification area (system tray) with a custom icon.
  - **Press `Pause/Break` anytime** from any application to instantly summon Prompter to the front.
  - **Click the tray icon** (left or double click) to bring the window up.
  - Right-click tray icon menu:
    - `⚡ Show Prompter (Pause/Break)`
    - `🔒 Lock All Protected Folders`
    - `❌ Exit Prompter`
  - Minimizing or clicking the window close `[X]` hides Prompter to the tray so it stays lightweight in the background without cluttering your taskbar.

- **Folder Organization**:
  - Group prompts into custom categories (e.g. *Code Review*, *Productivity*, *Writing*, *Sensitive Data*).
  - Add, rename, and delete folders with dynamic prompt count indicators.
  - Quick action toolbar and context menus.

- **Password-Protected Folders (AES-256-GCM)**:
  - Add passwords to folders to prevent unauthorized access.
  - When a folder is protected, prompt contents are encrypted on disk using AES-256-GCM and derived via PBKDF2 (SHA-256, 100,000 iterations).
  - Locked folders never keep decrypted prompts in memory.
  - Clean password dialog to unlock folders.
  - Global **Lock All** button to immediately secure all open protected folders in one click.
  - Ability to change passwords or remove password protection at any time.

- **Prompts with Titles**:
  - Every prompt requires a title for fast search and identification.
  - Includes real-time character count and word count statistics.

- **One-Click Clipboard Copying**:
  - **Copy Button Next to Every Title**: Each prompt item in the folder list features a prominent `📋 Copy` button right next to its title.
  - Instant clipboard integration with transient visual feedback banner (`✓ Copied to clipboard!`).
  - Dedicated `Copy Prompt` button in the main prompt viewer as well.

- **Dedicated Prompt Viewer & Text Box**:
  - Clicking any prompt title instantly displays its full content in the large multi-line text box.
  - Easily read, select, copy, or edit prompt text.
  - "Save Changes" button persists updates immediately.

- **Search & Quick Filtering**:
  - Real-time search bar to filter prompts in the active folder by title or content.

- **Local Persistence & Data Safety**:
  - Automatic saving on every change with atomic file replacement to prevent data corruption.
  - Data stored locally in `%LocalAppData%\Prompter\prompts_vault.json`.

---

## Running the Application

### Option 1: Via .NET CLI
Run from the root of the repository:
```bash
dotnet run
```

### Option 2: Direct Executable
Launch the precompiled executable directly:
- **Debug**: `bin\Debug\net10.0-windows\Prompter.exe`
- **Release**: `bin\Release\net10.0-windows\Prompter.exe`

### Option 3: Run Automated Tests
Execute the unit test suite:
```bash
dotnet test Tests\Prompter.Tests.csproj
```

---

## Hotkey & Tray Quick Guide

| Action | How To Trigger |
|--------|----------------|
| **Summon Window** | Press `Pause/Break` key globally on keyboard |
| **Bring Up from Tray** | Click the tray icon with left click or double-click |
| **Tray Context Menu** | Right-click the Prompter icon in the taskbar notification tray |
| **Hide / Minimize to Tray** | Click the window minimize `_` or close `[X]` button |
| **Completely Exit** | Right-click tray icon and choose `❌ Exit Prompter` |
