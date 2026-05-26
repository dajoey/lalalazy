# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/lazysightseeing-icon.png)

# Lazy Sightseeing — Fully Automated Sightseeing Log Completion

A premium Dalamud exploration plugin that fully automates FFXIV sightseeing log completion. It navigates to vista coordinates, skips locked weather/time windows, waits for open windows, executes the correct emotes, and teleports you back to safety when done.

---

## 🛠️ Required Dependencies

To use Lazy Sightseeing, you must have the following external plugins installed in Dalamud:
1. **vnavmesh:** For real-time 3D flight pathing and ground mesh navigation.
2. **lifestream:** For seamless teleportation between zones and inn rooms (using `/li inn`).

---

## 🌟 Core Features

* **Ban-Safe Navigation (Anti-Ban Hardening):** 100% compliant with safe-play guidelines. Memory snapping (direct coordinate teleportation cheats) has been completely removed to prevent server-side client bans. All movement uses natural flight pathing.
* **Smart Flight Takeoff:** Gracefully handles transitioning from ground movement to flight. Starts pathing via `flyto`, triggers a single **Jump** input to initiate takeoff, and falls back to ground movement (`/vnav moveto`) if flying is locked in the zone.
* **Robust Arrival Evaluation:** Features an advanced distance evaluator that tracks horizontal 2D distance (`< 2.0 yalms`) and vertical elevation (`< 4.0 yalms`) separately. This prevents dismount and emote failures caused by player elevation offsets on large mounts or hovering flight altitudes.
* **Stuck Mount Recovery:** Tracks character movement in real-time. If pathing gets stuck or stationary for more than 3 seconds, it resets the mount attempt latch (`_triedMount = false`) and tries mounting/un-stuck routines automatically.
* **Emote Standstill Fix:** Automatically cancels emotes via Jump when starting to navigate, resolving the bug where players got stuck in the `movingtosight` state with arms crossed.
* **Weather & Time Window Sheet Parsing:** Evaluates FFXIV weather sheets and in-game time windows dynamically. It will automatically re-prioritize vistas based on current weather windows, skipping locked weather targets and returning to the inn when all active windows are closed.

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
5. Open `/xlplugins` in chat, search for **Lazy Sightseeing** in the **Available Plugins** tab, and click **Install**.

---

## 🛠️ How to Use

1. Ensure `vnavmesh` and `lifestream` are loaded and active in Dalamud.
2. Toggle **Auto-Sightseeing** on in the checklist UI.
3. Watch your character travel, dismount, execute the vista emote, and clean out your sightseeing log.
