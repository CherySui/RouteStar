# RouteStar

[English](README-en.md) | [简体中文](README.md)

> **"Your HoYoverse launcher should launch the world."**

[![Platform](https://img.shields.io/badge/Platform-Windows-blue)](#)
[![Framework](https://img.shields.io/badge/Framework-WinUI%203-blueviolet)](#)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](#)

**RouteStar** is a highly customizable local application launcher framework.

Regarding our design philosophy: Building upon the foundation of an excellent HoYoverse game launcher, players should not be confined to a single game ecosystem. Instead, it can serve as a universal launch platform to add, manage, and launch any other local PC games and software applications.

---

## Core Highlights

- **Custom Local Video Background**  
  Supports setting local multimedia videos directly as the launcher's background. By introducing a local drive resource virtual mapping mechanism, it optimizes cross-origin issues and memory footprint when WebView2 loads large local videos.

- **Dynamic Addition of Other Local Applications**  
  Easily add external programs from your computer via the system file picker. The launcher automatically extracts the corresponding program icons, allowing users to uniformly manage and launch various third-party software or games within the same interface.

- **Seamless Gacha Log Link Extraction**  
  Built-in Gacha Log parsing module. This module can automatically locate and extract local cache files (`data_2`) of games like *Genshin Impact*, *Honkai: Star Rail*, and *Zenless Zone Zero* to obtain the latest gacha verification links.

- **Integration and Optimization of WinUI 3 & WebView2**  
  Built with WinUI 3 for the native shell and WebView2 as the frontend rendering container. The mutual invocation between the two features deep, low-level adaptations for an immersive borderless window, native system shadow feedback, and CSS-based window drag interactions.

---

## Design Goals

While affirming the convenience brought by official game launchers, we hope to empower players with more customization space and control.

The ultimate goal: While providing the practical core features of a "HoYoverse game launcher", it also supports players in launching other external applications at any time. Combined with visual effects like fully customizable video backgrounds, it transforms into a personalized game desktop homepage purely belonging to the player.

---

## Planned Features

- **Full Game Lifecycle Management**  
  Provide complete support for the download, installation, and update features of all HoYoverse games.

- **Plugin and Graphics Enhancement Support**  
  Provide compatibility and support for advanced plugins such as HoYoShade graphics patches and FPS Unlockers for select games.

- **Game News and Ecosystem Integration**  
  Support direct display of official game announcements within the launcher, with plans to deeply integrate HoYoLAB related features.

- **Auxiliary Data and Tool Support**  
  Support the integration of in-game maps (interactive maps) for specific games to further complete the auxiliary information loop of the launcher.

- **Python Plugin System**  
  Once developed, you will be able to build plugins via Python and the application API (similar to Blender's plugin ecosystem), allowing you to create your own tools and features without needing to recompile the application.

---

## Quick Start

1. **Prerequisites**
   - Windows 10 (1809 or later) or Windows 11
   - Visual Studio 2022 (requires **.NET desktop development** and **Windows application development** workloads)
   - Windows App SDK

2. **Build and Run**
   - Clone this repository to your local machine.
   - Open `RouteStar.slnx` using Visual Studio.
   - Select the `x64` or `x86` target platform and run.
   - *(Frontend rendering files should be placed in the `web/` directory within the project, and WebView2 will automatically map and load the local web pages at runtime.)*

---

## License

This project and its source code are open-sourced under the [MIT License](LICENSE).

**"May this journey lead us starward"**

