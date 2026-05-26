# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/lazyfateautomator-icon.png)

# Lazy FATE Automator — Fully Automated FATE Grinding

A premium Dalamud automation plugin that fully automates FATE farming in Final Fantasy XIV. By orchestrating navigation, combat, and zoning, it streamlines leveling, Yokai Watch, relic weapons, and shared FATE completions with advanced stuck failsafes and combat leasing.

---

## 🛠️ Required Dependencies

To use Lazy FATE Automator, you must have the following external plugins installed in Dalamud:
1. **vnavmesh:** For real-time 3D flight pathing and ground mesh navigation.
2. **lifestream:** For seamless teleportation between zones and Aetherytes.
3. **Gluttony Combo:** For premium auto-rotation combat. The automator automatically leases optimal configuration settings when farming and restores your defaults when disabled.

---

## 🌟 Core Features

* **Ban-Safe Flight Navigation:** Uses natural flight and ground pathing (`vnavmesh`) instead of memory snaps or direct teleport hacks, preventing server-side detection and client bans.
* **Gluttony Combo IPC Leasing:** Programmatically leases configuration settings from Gluttony Combo during active farming. Locks auto-rotation `ON`, enables immediate attack on target acquisition (`InCombatOnly = false`), and prioritizes FATE targets, instantly restoring your exact custom settings when disabled.
* **Smart FATE Prioritization:** Evaluates FATEs in real-time based on active player buffs, remaining duration, completion percentage, proximity, and special bonuses (like active Twist of Fate experience buffs).
* **NPC Starter Trigger Handling:** Detects FATEs that require talking to an NPC to begin, pathing to and initiating the dialogue automatically.
* **Robust Stuck Tracker & Recovery:** Tracks character coordinates in real-time. If stationary for more than 3 seconds, it triggers a navigation recalculation. If stationary for more than 15 seconds, it initiates an emergency lifestream teleport to the closest in-zone Aetheryte to safely escape and resume farming.
* **Interactive ImGui Dashboard:** Real-time FATE tracking tables, instant blacklist selectors, comprehensive session statistics (XP, gil, gems, FATEs per hour), and customizable progress thresholds.

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
5. Open `/xlplugins` in chat, search for **Lazy FATE Automator** in the **Available Plugins** tab, and click **Install**.

---

## 🛠️ How to Use

1. Ensure `vnavmesh`, `lifestream`, and `Gluttony Combo` are loaded and active in Dalamud.
2. Open the UI by typing `/lazyfate` in chat.
3. Adjust your thresholds (e.g., minimum remaining duration, Yokai/relic mode preferences) in the **Settings** tab.
4. Toggle **Auto-FATE Farming** on in the **Dashboard** tab.
5. Watch your character travel, sync levels, combat targets, trigger starter NPCs, and farm FATEs completely unattended.
