# Prompter - AI Prompt Manager, Local Chat & SwarmUI Studio

A modern, high-performance Windows desktop application built with C# and WPF (.NET 10) combining an encrypted, folder-organized **AI Prompt Manager** with a dual-engine **Local AI Chat & Image Studio** powered by **Ollama** and **SwarmUI**.

---

## 🌟 Key Features

### 🗂️ 1. Encrypted Prompt Vault & Organization
- **Categorized Folder Tree**: Organize prompts into custom folders (e.g., *Coding*, *Productivity*, *Writing*, *Diffusion Prompts*).
- **AES-256-GCM Encryption**: Password-protect sensitive prompt folders with PBKDF2 (SHA-256, 100,000 iterations). Locked folders remain strictly encrypted on disk and in memory until unlocked.
- **One-Click Quick Actions**:
  - `📋 Copy`: Instant copy button next to every prompt title with feedback toast.
  - `🗑️ Delete`: One-click delete button next to each prompt with confirmation protection.
- **Search & Filtering**: Real-time search across titles and prompt bodies.
- **Global `Pause/Break` Summoning**: Press `Pause/Break` anywhere in Windows to immediately bring Prompter to the front, or summon it from the system tray.
- **Single-Instance Enforcement**: Named system mutex with Win32 message hand-off ensures only one copy runs at a time.

### 💬 2. Local AI Chat Bot (Ollama)
- **Multi-Model Support**: Native streaming chat with local LLMs including **Qwen**, **Gemma**, **Llama**, **DeepSeek**, **Mistral**, **GLM**, and **Phi**.
- **Reasoning Display**: Thought process accordion for reasoning models (DeepSeek-R1, QwQ, etc.).
- **Smooth Pixel Scrolling**: Physical pixel-based smooth scrolling eliminates abrupt list jumping.
- **Auto-Detection**: Dynamically locates local Ollama installations and model manifests.

### 🎨 3. SwarmUI Image Generation Studio
- **REST Integration**: Communicates via SwarmUI API (`http://localhost:7801`) without modifying SwarmUI or ComfyUI installations.
- **Model Discovery**: Auto-detects Stable Diffusion checkpoints (**SDXL 1.0**, **Pony**, **DreamShaper**, etc.) from both API and `X:\SwarmUI\Models\Stable-Diffusion`.
- **Automatic Video Model Filtering**: Intelligently identifies and excludes video generation models (such as Wan models) from image listings.
- **Non-Intrusive Safety**: Prompter **never** auto-runs SwarmUI or heavy diffusion models on startup. Includes a manual one-click "Start" button for complete user control.
- **Full Image Chat Previews**: Complete preview without cropping or cutoff, with click-to-open full resolution in default photo viewer.
- **Image Actions**:
  - `🔄 Regenerate`: Re-runs generation with the exact same prompt in one click.
  - `📁 Open in Folder`: Selects the generated image file in Windows Explorer.
  - `🗑️ Delete`: Deletes the image file from disk and removes it from chat with confirmation.
  - `📋 Copy Image`: Copies the image bitmap directly to the clipboard.

### 🌓 4. Antigravity UI & Native Dark Theme
- **Default Dark Theme**: Clean dark theme with native Windows immersive dark title bars (`DWMWA_USE_IMMERSIVE_DARK_MODE`).
- **Antigravity Chat Input**: Centered floating input card with active model toggle, prompt injection pill, and real-time task status badges.

---

## 🚀 Getting Started

### Prerequisites
- **Windows 10 / 11 (64-bit)**
- **.NET 10 Desktop Runtime** (or .NET 10 SDK to build from source)
- *(Optional)* [Ollama](https://ollama.com/) for local LLM text generation
- *(Optional)* [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) for local image generation

### Build & Run from Source
```powershell
# Clone the repository
git clone https://github.com/brentgrew/Prompter.git
cd Prompter

# Run automated tests
dotnet test Tests\Prompter.Tests.csproj -c Release

# Run application
dotnet run -c Release
```

### Published Binary
A standalone build is ready in `bin\Publish\Prompter.exe`.

---

## ⌨️ Shortcuts & Hotkeys

| Hotkey / Control | Action |
|---|---|
| `Pause/Break` | Bring Prompter to front globally from any window |
| `Enter` | Send message or generate image |
| `Shift + Enter` | Insert newline in chat input |
| `Tray Icon Click` | Show / restore window from system notification area |
| `Minimize [_] / Close [X]` | Minimize Prompter to tray |
