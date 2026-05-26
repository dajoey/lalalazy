# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/armoire-icon.png)

# Armoire Auto-Fill — Dungeon Collectibles Scanner

A premium Dalamud utility plugin that scans and displays a per-dungeon checklist of all armoire-eligible gear drops in Final Fantasy XIV, showing whether each collectible piece is in your inventory, in your armoire cabinet, or still missing.

---

## 🌟 Core Features

* **Complete Cabinet Scans:** Reads the in-game Cabinet sheet and matches items against LuminaSupplemental.Excel drop tables to locate armoire-eligible gear drop sources from every expansion.
* **Three-State Ownership Detection:**
  * **Inventory:** Located in your inventory, armory chest, saddlebag, or currently equipped.
  * **Armoire:** Stored inside the Inn Room Armoire cabinet.
  * **Missing:** Not yet obtained or stored.
* **Inn Cabinet Sync:** Simply open the Armoire UI at any Inn Room once per play session to populate the in-armoire status instantly.
* **Declutter & Filter UI:**
  * Sort drops by expansion and level.
  * Hide fully completed dungeons by default.
  * Toggle options to show or hide currently owned items.

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
5. Open `/xlplugins` in chat, search for **Armoire Auto-Fill** in the **Available Plugins** tab, and click **Install**.

---

## 🛠️ Commands

| **Chat Command** | **Function** |
|:---|:---|
| `/armoire` | Opens the main Armoire Auto-Fill dungeon checklist window. |

---

## ⚙️ How Armoire Detection Works

The plugin retrieves cabinet data from two sources, in order of preference:
1. **Live API (`UIState.Cabinet.IsItemInCabinet`):** Full row coverage, including current-expansion items. Only usable while the armoire UI is loaded (when you open an armoire NPC at an inn). The plugin hooks the `Cabinet` and `MiragePrismPrismBox` addons to snapshot the checklist whenever the UI refreshes.
2. **Bitmap Fallback (`ItemFinderModule.CabinetItemUnlockBits`):** Auto-populates on login, but is capped at 4000 cabinet rows (FixedSizeArray125<uint>), so current-expansion items past row 4000 won't show. Used only as a cold-start fallback before the live API is available.

*Once the live API has run at least once this session, the plugin locks that snapshot rather than downgrading back to the bitmap. The cached result is persisted across sessions.*

---

## 🛠️ Build Requirements

Requires the .NET 10 SDK and Dalamud SDK.

```powershell
cd src/ArmoireAutoFill
dotnet build --configuration Release
```

---

## ⚖️ Credits & Licensing

* **Original Plugin:** Built from scratch by **dajoey** using the [Dalamud plugin framework](https://github.com/goatcorp/Dalamud).
* **Data Sources:** Drop-table data ships via [LuminaSupplemental.Excel](https://github.com/Critical-Impact/LuminaSupplemental) (GPL-3.0, by Critical-Impact). 
* **Inspiration:** Cabinet observation technique inspired by [seventhxiv/Collections](https://github.com/seventhxiv/Collections).
