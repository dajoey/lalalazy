# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/pvpsolver-icon.png)

# PvP Solver — PvP-Only Auto-Rotation

A premium auto-rotation plugin designed specifically for **FFXIV PvP combat only**. PvP Solver executes flawless job rotations and target selection inside PvP-enabled zones (Frontlines, Crystalline Conflict, and Rival Wings) and turns off immediately upon leaving. 

It is designed to run seamlessly alongside **Gluttony Combo** (which handles PvE combat).

---

## 🌟 Core Features

* **PvP-Only Safety:** Completely stripped of all PvE rotation code. Operates strictly inside PvP zones, leaving PvE combat to Gluttony Combo.
* **Wholly Original Rebrand:** Cleaned of all references and hooks to upstream RotationSolverReborn to prevent command collisions and file conflicts. Commands are unified under `/pvpsolver` and `/pvs`.
* **Smart Cast Protection:** Includes intelligent protection that completely blocks combat action autocasting when no hostile targets are within range inside a PvP zone, preventing wasteful ability spam.
* **Full Rotation Support:** Pre-mapped combat, cooldown, and limit break execution trees for all 22 active FFXIV jobs.

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
5. Open `/xlplugins` in chat, search for **PvP Solver** in the **Available Plugins** tab, and click **Install**.

---

## 🛠️ Commands

| **Chat Command** | **Function** |
|:---|:---|
| `/pvpsolver` | Opens the rotation configuration GUI. |
| `/pvs` | Shortcut to open the rotation configuration GUI. |

---

## ⚙️ Configuration & How to Use

1. Ensure **Gluttony Combo** is loaded for PvE, and **PvP Solver** is loaded for PvP.
2. Type `/pvs` in chat to open the PvP Solver control panel.
3. Select your job class and customize priorities (such as setting thresholds for automatic `Purify` and `Recuperate` execution).
4. Assign a rotation execution key. While inside a PvP zone, holding this key will execute your optimized PvP rotation automatically.

---

## 🛠️ Build Requirements

Requires the .NET 10 SDK and Dalamud SDK.

```bash
# Clean build (required between changes due to source generator caches)
rm -rf PvPSolver.Basic/obj PvPSolver/obj PvPSolver.SourceGenerators/obj
dotnet build --configuration Release
```

---

## ⚖️ Credits & Licensing

* **Forked from [RotationSolverReborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn)** by **ArchiDog1998** / **FFXIV-CombatReborn**.
* Licensed under the **GPLv3** and **Lesser GPLv3** licenses. See `COPYING` and `COPYING.LESSER` for details.
* **Key Improvements:**
  * Removed all PvE rotation logic.
  * Entirely rewired command system to `/pvpsolver` and `/pvs` to prevent system collisions.
  * Added smart cast-prevention checks to prevent action spam on empty targets.
  * Remapped action ID arrays specifically for PvP equivalents.
  * Count-based check fixes to prevent index-out-of-range exceptions on rotation caches.
