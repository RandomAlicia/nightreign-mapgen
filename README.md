# NightReign Map Generator — Windows Setup & Usage

This guide shows how to clone, build, and run the map generator on **Windows**, and how to use the provided batch scripts for common workflows.

---

## Prerequisites

- **Git**
- **.NET SDK** (8.x recommended; 7.x OK if your project targets it)

> If you need them:
>
> ```powershell
> winget install --id Git.Git -e
> winget install --id Microsoft.DotNet.SDK.8 -e
> git --version
> dotnet --info
> ```

---

## Clone & Build

1) Open **PowerShell** or **CMD**.
2) Clone and **enter the project directory** (where `Program.cs` lives):
   ```powershell
   git clone https://github.com/<you>/nightreign-mapgen.git
   cd .\nightreign-mapgen\
   ```
3) Restore and build:
   ```powershell
   dotnet restore
   dotnet add package Magick.NET-Q8-AnyCPU
   dotnet build -c Release
   ```

### Run a single pattern (optional)
From `nightreign-mapgen\`:
```powershell
dotnet run -c Release -- ..\data\pattern\pattern_000.json
```

> The generated PNG goes to `output\` by default. Paths in the repo assume you’re running from the `nightreign-mapgen\` folder.

---

## Batch Scripts

> **Important:** These scripts expect specific working directories. Follow the locations below exactly.

### A) Generate PNG maps — `mapgen.bat` (run from **repo root**)

From the repository **root** (one level **above** `nightreign-mapgen\`):

- **Specific seeds** (e.g., 31, 71, 73):
  ```powershell
  .\mapgen.bat 31 71 73
  ```
  Renders **only** those seed IDs.

- **All maps** (~320 total):
  ```powershell
  .\mapgen.bat
  ```
  Renders everything. (On the current code it’s about **~20 minutes** on your setup.)

### B) Quick test set — `test.bat`

- From **repo root**:
  ```powershell
  .\test.bat
  ```
- Or, if you’re already inside `nightreign-mapgen\`:
  ```powershell
  ..\test.bat
  ```

### C) Convert PNG → JPG — `png2jpg.bat` (run from **nightreign-mapgen\**)

From **inside** the `nightreign-mapgen\` folder:
```powershell
.\png2jpg.bat
```
Converts everything in `output\` to JPGs in `outout-jpn\` (per the script).

---

## Tweaking Rendering (styles, sizes, fonts)

Edit **`nightreign-mapgen\appsettings.json`** to change:
- Text styles (font, size, outline/shadow)
- Icon sizes/offsets
- Language/i18n
- Other rendering options

Re-run your chosen command (`dotnet run`, `mapgen.bat`, etc.) to see changes.

---


## Tips & Gotchas

- **Working directory matters:** Always run commands from the directories noted above—batch files use relative paths.
- **Paths with spaces (e.g., OneDrive):** Quote the pattern path:
  ```powershell
  dotnet run -c Release -- "..\data\pattern\pattern 000.json"
  ```
- **Verbose logging:** If you’ve enabled a `Program.Verbose` flag in your code, keep it `false` for faster runs.
- **Magick.NET resource limit APIs:** Some versions don’t expose `SetResourceLimit`. It’s safe to omit.

---

## Folder Layout (typical)

```
<repo root>\
├─ mapgen.bat
├─ test.bat
├─ nightreign-mapgen\
│  ├─ Program.cs
│  ├─ Program.Helpers.cs
│  ├─ png2jpg.bat
│  ├─ appsettings.json
│  ├─ output\          (PNG results)
│  └─ outout-jpn\      (JPG results from png2jpg.bat)
└─ data\
   └─ pattern\
      ├─ pattern_000.json
      └─ pattern_*.json
```

---

## Quick Commands Recap

```powershell
# Build
cd .\nightreign-mapgen\
dotnet restore
dotnet build -c Release

# Render specific seeds (from repo root)
.\mapgen.bat 31 71 73

# Render them all (from repo root)
.\mapgen.bat

# Test sample set
.\test.bat        # from root
..\test.bat       # from nightreign-mapgen\

# PNG -> JPG (from nightreign-mapgen\)
.\png2jpg.bat
```

Happy mapping! 🎯