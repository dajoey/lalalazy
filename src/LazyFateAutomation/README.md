# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/lazyfateautomation-icon.png)

# Lazy Fate Automation — FATE Grinding & Relic Farming

A fully automated, standalone FATE grinding plugin for Final Fantasy XIV that coordinates pathfinding, dialogue skipping, zone swapping, and combat auto-rotation. Optimized for Yo-kai Watch events, Zodiac/Anima/Resistance relic weapon grinds, and Bicolor Gemstone farming.

---

## 🌟 Core Features

* **Comprehensive Grind Modes:**
  * **Yo-kai Watch:** Automatically equips the Yo-kai Watch and targets zones/FATEs for specific medals.
  * **Relic Weapon Grinds:** Built-in spreadsheet offset logic for Zodiac Atma, Anima Luminous Crystals, and Resistance Memories.
  * **Bicolor Gemstone Farming:** Tracks gemstone caps and automatically swaps zones when completed.
* **Seamless IPC Automations:**
  * **vnavmesh:** Fully automated 3D flight pathing, ground movement, and stuck recovery.
  * **lifestream:** Handles automatic teleportation and zone swapping to keep the grind running continuously.
  * **TextAdvance:** Auto-completes dialogues and quest strings.
  * **Gluttony Combo & BossMod:** Integrates combat rotations. Supports **either BossMod OR BossModReborn** interchangeably as the active auto-rotation engine.
* **Robust Customization:** Drag-and-drop FATE sorting priorities, custom blacklist support, and real-time status UI.

---

## 🚀 Installation

Add the custom plugin repository URL to your Dalamud settings:

```text
https://raw.githubusercontent.com/dajoey/lalalazy/main/pluginmaster.json
```

1. In-game, type `/xlsettings` in chat to open Dalamud Settings.
2. Select the **Experimental** tab.
3. Scroll to **Custom Plugin Repositories**, paste the repository URL into the empty field, and click **+**.
4. Click **Save and Close** (bottom-right).
5. Open `/xlplugins` in chat, search for **Lazy Fate Automation** in the **Available Plugins** tab, and click **Install**.

---

## 🛠️ Commands

| **Chat Command** | **Function** |
|:---|:---|
| `/lazyfate` | Opens the main configuration, priority sorting, and FATE tracker GUI. |

---

## ⚙️ Configuration Parameters

| **Setting** | **Default** | **Description** |
|:---|:---:|:---|
| **Enabled** | `Off` | Master toggle to start or stop the automated FATE grinding loop. |
| **Grind Mode** | `None` | Selected grind category (Yo-kai, Relics, Gemstones, or None/Custom). |
| **Zone Selector** | `Default` | Configured swap zones where the loop travels when a zone runs out of FATEs. |

---

## 🛠️ Build Requirements

Requires the .NET 10 SDK and Dalamud SDK.

```powershell
cd src/LazyFateAutomation
dotnet build --configuration Release
```

---

## ⚖️ Credits & Licensing

* **Original Tweaks:** Extracted and modified from CBT (Croizat's Bundle of Tweaks) by **croizat**.
* **Refactoring & Release:** Upgraded to standard C# extensions and standalone architecture by **dajoey**.
