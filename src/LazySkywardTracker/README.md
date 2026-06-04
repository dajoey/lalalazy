# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/lazyskywardtracker-icon.png)

# Lazy Skyward Tracker — Skybuilders' Points Achievement Progress Tracker

A clean, lightweight Dalamud plugin designed to track your progress toward the **Pteranodon** mount (the "Castle in the Sky" achievement). It displays your current Skyward Points for all 11 Disciple of the Hand and Land classes in a single, unobtrusive window.

---

## 🌟 Core Features

* **Overall Pteranodon Mount Progress:** Displays a master progress bar summing your total points across all classes out of the required 5,500,000 points.
* **11 Class Tracking:** Tracks points for all Disciples of the Hand (Carpenter, Blacksmith, Armorer, Goldsmith, Leatherworker, Weaver, Alchemist, Culinarian) and Land (Miner, Botanist, Fisher).
* **Live Memory Interop:** Uses pointer arithmetic (`Achievement.Instance() + 0x0C`) to read the client's internal completed achievements bitfield, immediately showing completed jobs as "Completed" without waiting for server responses.
* **Server Intercept Hooking:** Automatically detours `ReceiveAchievementProgress` using ECommons' hook manager to update and cache in-progress point values sent from the server.
* **Compact ImGui Table:** Provides a clean interface with color-coded progress bars (green for completed, orange/yellow for in-progress) and exact point numbers.

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
5. Open `/xlplugins` in chat, search for **Lazy Skyward Tracker** in the **Available Plugins** tab, and click **Install**.

---

## 🛠️ How to Use

1. Type `/lazysky` in your chat bar to open the tracker window.
2. Click **Refresh Points** to query the server for your current point tallies across all jobs.
3. Keep the window open to monitor your progress as you craft or gather in the Firmament and Diadem.
