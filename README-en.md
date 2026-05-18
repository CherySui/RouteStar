# RouteStar · Xing轨

[English](README-en.md) | [简体中文](README.md)

> **"Your HoYoverse launcher should launch the world."**

[![Platform](https://img.shields.io/badge/Platform-Windows-blue)](#)
[![Framework](https://img.shields.io/badge/Framework-WinUI%203-blueviolet)](#)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](#)

**RouteStar** is a highly customizable HoYoverse launcher framework.

Design philosophy: On top of solid official launcher features, players should not be locked into a single game ecosystem. Instead, it can serve as a universal launch platform to add, manage, and launch any other local PC games and software applications.

---

## What Is Implemented

- **HoYoverse Download and Install**  
  Currently supports downloading and installing *Genshin Impact*, *Honkai: Star Rail*, and *Zenless Zone Zero*.

- **Custom Video Background**  
  Supports setting local multimedia videos directly as the launcher's background. A local drive virtual mapping mechanism is used to optimize cross-origin issues and memory footprint when WebView2 loads large local videos.

- **Add Local Applications**  
  Add external programs with the system file picker. The launcher automatically extracts the program icon so you can manage and launch third-party software or games in one place.

- **Gacha Log Link Extraction and Visualization**  
  Built-in Gacha Log parsing module. It locates and extracts local cache files (`data_2`) for *Genshin Impact*, *Honkai: Star Rail*, and *Zenless Zone Zero* to obtain the latest gacha verification links.

- **Usage Time Tracking**  
  Automatically tracks your play time. You can also bind a process name to local apps in Preferences to track time.

---

## Notes

While affirming the convenience brought by official game launchers, we hope to empower players with more customization space and control.

The ultimate goal: While providing the practical core features of a "HoYoverse game launcher", it also supports players in launching other external applications at any time. Combined with visual effects like fully customizable video backgrounds, it transforms into a personalized game desktop homepage purely belonging to the player.

---

## How to Build

1. **Prerequisites**
   - Windows 10 (1809 or later) or Windows 11
   - Visual Studio 2022 (requires **.NET desktop development** and **Windows application development** workloads)
   - Windows App SDK

2. **Build and Run**
   - Clone this repository to your local machine.
   - Open `RouteStar.slnx` using Visual Studio.
   - Select the `x64` or `x86` target platform and run.
   - *(Frontend rendering files should be placed in the `web/` directory within the project. WebView2 will automatically map and load the local web pages at runtime.)*

---

## Acknowledgements

- **Thanks: [Starward](https://github.com/Scighost/Starward)**  
  This project is an independent framework. During development, we referenced **Starward** for core techniques such as cache file parsing, data reading, and gacha link extraction rules. It provided important theoretical and implementation guidance. Sincere thanks to the author and the open-source community.

---

## License

This project and its source code are open-sourced under the [MIT License](LICENSE).

**"May this journey lead us starward"**

