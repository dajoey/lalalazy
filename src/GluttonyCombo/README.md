# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/gluttonycombo-icon.png)

# Gluttony Combo — XIVCombo for very lazy players

Condenses combos and mutually exclusive abilities onto a single button — and then some. **Gluttony Combo** is a premium, lalalazy-branded fork of the GPLv3 licensed **Wrath Combo** plugin, heavily enhanced with custom automation logic and set-and-forget optimizations.

> [!WARNING]
> **Wrath Combo Conflict:**
> Gluttony Combo replaces Wrath Combo. You **cannot** run both simultaneously. Please disable or uninstall the stock Wrath Combo plugin before installing Gluttony Combo to prevent client crashes.

---

## 🌟 Core Enhancements (Over Stock Wrath Combo)

While remaining synchronized with upstream Wrath Combo fixes and rotation updates, Gluttony Combo introduces unique features designed to make combat gameplay exceptionally smooth and hands-off:

### 🎯 1. Dynamic BossMod Distance Syncing
* **What it does:** Automatically adjusts your BossMod / BossModReborn AI target distance configuration on job change.
* **Why it matters:** Eliminates manual configuration resets. Keeps your character perfectly positioned for positionals or safe casting ranges.
* **Ranges Configured:**
  * **Melee DPS & Tanks:** Sets BossMod target distance to `3` (optimal melee range).
  * **Healers:** Sets target distance to `15` (safe casting range).
  * **Ranged / Magical DPS:** Sets target distance to `20` (max range safety).

### 🛡️ 2. Dark Knight (DRK) TBN Enhancements
* **Tankbuster Auto-Mitigation:** Integrates with `HasIncomingTankBusterEffect()` to automatically cast **The Blackest Night (TBN)** whenever an incoming tankbuster is tracked, guaranteeing mitigation is active before impact.
* **Trash Pull Smart Mitigation:** Automatically tracks incoming hostiles targeting you using `EnemiesTargetingPlayerCount()`. During dungeon trash pulls (when **3 or more** enemies target the player), TBN will cast on cooldown, bypassing standard health thresholds for maximum active shield coverage.

### 💖 3. Intelligent Healer & Support Automation
* **Ground-Targeted Auto-Placement:** Automatically places ground-targeted heals (like Asylum, Sacred Soil, Earthly Star) directly on the target tank's current coordinates, bypassing manual ground-targeting.
* **Raidwide Overlap Protection:** Intelligent detour checks that prevent overlapping major healer mitigation shields when a co-healer's equivalent barrier is already active.

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
5. Open `/xlplugins` in chat, search for **Gluttony Combo** in the **Available Plugins** tab, and click **Install**.

---

## 🛠️ Commands

| **Chat Command** | **Function** |
|:---|:---|
| `/gluttony` | Toggles the main plugin window where you can toggle combo options. |
| `/gluttony pve` | Opens the main plugin window to the PvE tab. |
| `/gluttony pvp` | Opens the main plugin window to the PvP tab. |
| `/gluttony settings` | Opens the main plugin window to the Settings tab. |
| `/gluttony autosettings` | Opens the main plugin window to the Auto-Rotation tab. |
| `/gluttony <X>` | Opens the main window to a specific job's PvE features (e.g., `/gluttony drk`, `/gluttony whm`). |
| `/gluttony auto` | Toggles Auto-Rotation **on** or **off**. |
| `/gluttony auto <state>` | Sets Auto-Rotation to a specific state (e.g., `/gluttony auto on`, `/gluttony auto off`). |
| `/gluttony combo` | Toggles action replacing **on** or **off**. When off, actions won't be replaced, but Auto-Rotation still runs. |
| `/gluttony toggle <ID>` | Toggles a specific feature or option **on** or **off** (does not work while in combat). |
| `/gluttony set <ID>` | Turns a specific feature/option **on**. |
| `/gluttony unset <ID>` | Turns a specific feature/option **off**. |
| `/gluttony unsetall` | Turns all features and options **off** at once. |

---

## 🔗 Use with Other Plugins

### [Orbwalker](https://puni.sh/plugin/Orbwalker)
Gluttony Combo can use Orbwalker to automatically pause player movement during Auto-Rotation casts:
1. Open Auto-Rotation Settings: `/gluttony autosettings`.
2. Check "Enable Orbwalker Integration".
3. Open Orbwalker and configure: `/orbwalker`.

### [AutoDuty](https://github.com/erdelf/AutoDuty)
Use Gluttony Combo as your AutoDuty rotation engine:
1. Open AutoDuty Config: `/autoduty cfg`.
2. Expand the "Duty Config Settings" section.
3. Enable "Auto Manage Rotation Plugin State".
4. Ensure your jobs are set up for auto-rotation under `> Wrath Config Options <`.

### [Questionable](https://puni.sh/plugin/questionable)
Set Gluttony Combo as your preferred combat module during questing:
1. Open Questionable Settings: `/qst config`.
2. Go to the "General" tab.
3. Select "Wrath Combo" (which maps directly to Gluttony Combo) as the "Preferred Combat Module".

---

## ⚖️ Credits & Attribution

* **Upstream Project:** [WrathCombo](https://github.com/PunishXIV/WrathCombo) by Team Wrath / PunishXIV. Licensed under GPLv3.
* All upstream credits and rotation layouts belong to PunishXIV and contributors.
