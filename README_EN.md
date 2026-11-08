<p align="center">
  <img src="resources/icons/app.ico" width="72" height="72" alt="CapsLang Logo" />
</p>

# CapsLang

<p align="center">
  <b>English</b> •
  <a href="README.md">Русский</a>
</p>

<p align="center">
  <a href="https://github.com/dexston/CapsLang/releases/latest"><img src="https://img.shields.io/github/v/release/dexston/CapsLang?color=2ea44f&logo=github&label=Release" alt="Latest Release" /></a>
  <a href="https://github.com/dexston/CapsLang/releases"><img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078d4?logo=windows" alt="Platform: Windows 10 | 11" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-GPL--3.0-orange.svg" alt="License: GPL-3.0" /></a>
</p>

---

**CapsLang** is a lightweight and fast utility for Windows 10 and 11 that switches the input language by pressing the `Caps Lock` key and automatically disables itself in selected games and applications.

---

## ⚡ Features

- **Language switching via `Caps Lock`**: A single press instantly changes the input language.
- **Standard uppercase mode**: To toggle regular `Caps Lock`, press `Shift + Caps Lock`.
- **Exclusion list (applications and folders)**:
  - Add individual processes (`.exe`) or entire folders with games and work applications (for example, your Steam library).
  - When an excluded application is in focus, CapsLang does not intercept the key, and `Caps Lock` works normally inside that application.
- **Settings backup**: Quickly export and import all settings and exclusion lists into a single file from the window title bar menu.
- **Convenient interface**:
  - Runs in the system tray and takes up no space on the taskbar.
  - Automatically matches Windows 11 Light or Dark theme.
  - Supports English and Russian interface languages.
- **No system load**: Consumes 0% CPU in the background and uses minimal RAM.

---

## 📥 Download and Installation

Two versions of the program are available on the [Releases](https://github.com/dexston/CapsLang/releases) page:

### 1. Installer (`Setup.exe`) — Recommended
Standard PC installation.
- **Main advantage**: Language switching via `Caps Lock` works **in all windows**, including applications running as administrator (Task Manager, terminals, system utilities), while CapsLang itself runs normally without User Account Control (UAC) prompts.

### 2. Portable version (`Portable.zip`)
Does not require installation — simply unpack the archive to any folder and launch `CapsLang.exe`.
- All settings are saved to a `CapsLang_Settings.xml` file alongside the executable.
- *Windows limitation*: Without system installation, the portable version cannot switch layout in windows running as administrator (to work in such windows, `CapsLang.exe` will also need to be run as administrator).

---

## ⚙️ How to Use

### System Tray Icon
- **Double-click with left mouse button** — open settings window.
- **Right mouse button** — control menu (enable/pause, settings, exit).

*If CapsLang is already running, launching the program shortcut again will open the settings window.*

---

## 📄 License

This project is distributed free and open-source under the [GNU General Public License v3.0](LICENSE).
