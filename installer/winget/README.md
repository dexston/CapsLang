# Winget Package Manifests for CapsLang

This directory contains the Windows Package Manager (`winget`) manifests for CapsLang, matching the official [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs) directory layout.

## Package Info

- **Package Identifier**: `dexston.CapsLang`
- **Default Locale**: `en-US`
- **Supported Locales**: `en-US`, `ru-RU`
- **Installer Type**: `inno` (machine scope, `/VERYSILENT /NORESTART`)

## Validation

To validate the manifests locally before submitting:

```powershell
winget validate installer/winget/manifests/d/dexston/CapsLang/1.0.0
```

## Submitting to winget-pkgs

### Option 1: Using WingetCreate CLI (Recommended)

1. Run the submit command:
   ```powershell
   wingetcreate submit installer/winget/manifests/d/dexston/CapsLang/1.0.0 --prtitle "New package: dexston.CapsLang version 1.0.0"
   ```
2. If prompted, authenticate with GitHub. `wingetcreate` will automatically fork `microsoft/winget-pkgs`, create a branch, push the manifests, and open the Pull Request.

### Option 2: Manual Pull Request

1. Fork [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs) on GitHub.
2. Copy the folder `manifests/d/dexston/CapsLang/1.0.0/` into the corresponding path of your fork.
3. Open a Pull Request into `microsoft/winget-pkgs:master`.
