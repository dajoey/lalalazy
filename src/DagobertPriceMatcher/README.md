# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/dagobert-icon.png)

# Dagobert Price Matcher — Retainer Market Board Price Sync

An automated retainer pricing plugin that adjusts your listed market board offers to **match** the lowest existing price exactly (0 undercut default), protecting market rates from descending into severe undercutting wars.

---

## 🌟 Core Features

* **Exact Price Matching:** Default adjustment value is set to `0` (exact matching). You can still configure a custom positive or negative value to undercut or overprice if desired.
* **Auto-Pinch Automation:** Automatically clicks the final confirmation and price adjustment buttons inside the retainer UI, making pricing sweeps completely friction-free.
* **AutoRetainer IPC Integration:** Fully integrates with AutoRetainer IPC for smooth, automated retainer inventory adjustments.
* **Safe Missing-Price Handling:** Fallback safeguards built in to handle cases where market board query limits are hit or prices are not returned, preventing accidental listing at default/NPC vendor values.

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
5. Open `/xlplugins` in chat, search for **Dagobert - Price Matcher** in the **Available Plugins** tab, and click **Install**.

---

## 🛠️ Commands

| **Chat Command** | **Function** |
|:---|:---|
| `/pricematch` | Opens the main configuration GUI to manage price match offsets. |

---

## ⚙️ Configuration Parameters

| **Setting** | **Default** | **Description** |
|:---|:---:|:---|
| **Match Amount** | `0` | Gil difference from lowest offer. Use negative values to undercut, or positive to overprice. |
| **Auto-pinch** | `Enabled` | Automatically clicks price adjustment and listing confirmation buttons. |

---

## 🛠️ Build Requirements

Requires the .NET 10 SDK and Dalamud SDK.

```powershell
cd src/DagobertPriceMatcher
dotnet build --configuration Release
```

---

## ⚖️ Credits & Licensing

* **Forked from [Dagobert](https://github.com/SHOEGAZEssb/Dagobert)** by **SHOEGAZEssb**.
* Licensed under the **AGPLv3** license. See `LICENSE.md` for details.
* **Key Improvements:** 
  * Changed default pricing offset from `-1` (undercut by 1 gil) to `0` (exact price match).
  * AutoRetainer pricing IPC integrations.
