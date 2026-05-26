# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/autopotion-icon.png)

# AutoPotion — HP, MP, and Deep Dungeon Automation

A lightweight, automated recovery plugin that automatically selects and uses HP potions, MP ethers, and deep dungeon regen items at configurable percentage thresholds. Each job configuration is stored independently, allowing Casters, DPS, and Tanks to have completely customized settings.

---

## 🌟 Core Features

* **High-Tier Potion Scanning:** Automatically scans your inventory and uses the highest-tier available HP potion (HQ > NQ, highest grade first) to ensure efficient healing.
* **Deep Dungeon regen support:** Fully automates using zone-specific regen potions:
  * **Sustaining Potion:** Palace of the Dead (PotD)
  * **Empyrean Aetherpool Potion:** Heaven-on-High (HoH)
  * **Orthos Aetherpool Potion:** Eureka Orthos (EO)
  * **Eurekan Potion:** Eureka (Exploration Zone)
  * **Pilgrim's Potion:** Bozja Southern Front / Zadnor
* **Per-Job Configurations:** Saves separate toggle states and health thresholds for each class (e.g. enabling auto-ethers for Casters while keeping them disabled on Tanks).

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
5. Open `/xlplugins` in chat, search for **AutoPotion** in the **Available Plugins** tab, and click **Install**.

---

## 🛠️ Commands

| **Chat Command** | **Function** |
|:---|:---|
| `/autopotion` | Opens the main configuration GUI to manage job thresholds and toggles. |
| `/pot` | Master toggle to enable or disable auto-potion automation on the fly. |

---

## ⚙️ Configuration Parameters

| **Setting** | **Default** | **Description** |
|:---|:---:|:---|
| **Enabled** | `On` | Master toggle for all potion automation. |
| **HP Threshold %** | `75%` | Uses your best HP potion when health falls below this percentage. |
| **MP Threshold %** | `30%` | Uses your best Ether potion when MP falls below this percentage. |
| **DD Potion Threshold %** | `80%` | Uses a zone-specific deep dungeon regen potion below this percentage. |

---

## 🛠️ Build Requirements

Requires the .NET 10 SDK and Dalamud SDK.

```powershell
cd src/AutoPotion
dotnet build --configuration Release
```

---

## ⚖️ Credits & Licensing

* **Original Plugin:** Built from scratch by **dajoey** using the [Dalamud plugin framework](https://github.com/goatcorp/Dalamud).
