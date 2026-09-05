/* lalalazy plugin catalog — shared by the landing index and the per-mod pages. */
const PLUGINS = [
  {
    slug: 'gluttonycombo', name: 'Gluttony Combo', origin: 'Fork', hasWindow: true,
    short: 'Your whole rotation, condensed onto a single button — and then some.',
    tag: 'Your whole rotation. One button. Zero effort.',
    command: '/gluttony', credit: 'Fork of WrathCombo by Team Wrath / PunishXIV · GPLv3',
    features: [
      { t: 'One-button combos', d: 'Condenses combos and mutually exclusive abilities onto a single button.' },
      { t: 'Mitigation overlap protection', d: 'Healer raidwide mitigation no longer double-stacks and wastes a GCD.' },
      { t: 'Smart ground heals', d: 'Ground-targeted heals auto-place on tanks instead of asking you to click.' },
    ],
  },
  {
    slug: 'pvpsolver', name: 'PvP Solver', origin: 'Fork', hasWindow: true,
    short: 'Auto-rotation for PvP combat. All jobs, activates automatically in PvP zones.',
    tag: 'Wins the duty while you sip your coffee.',
    command: '/pvpsolver · /pvs', credit: 'Fork of RotationSolverReborn by ArchiDog1998 / FFXIV-CombatReborn · GPLv3',
    features: [
      { t: 'Every job covered', d: 'PvP rotations for all jobs, with action IDs remapped to their PvP equivalents.' },
      { t: 'Auto-activates', d: 'Switches on the moment you load into a PvP zone — nothing to toggle.' },
      { t: 'Pairs with Gluttony', d: 'Designed to run alongside Gluttony Combo, which keeps your PvE covered.' },
    ],
  },
  {
    slug: 'lazymarketcompanion', name: 'Lazy Market Companion', origin: 'Original', hasWindow: true,
    short: 'Auto-lists your always-sell items through your retainers and matches board prices.',
    tag: 'Your always-sell list, listed and priced while you sleep.',
    features: [
      { t: 'Auto-Market list', d: 'Items you always sell, each with stack size, keep-in-bags reserve and per-retainer cap. Right-click an item in your bags to add it.' },
      { t: 'Bags and retainer loot', d: 'Lists from your inventory and from the retainer\'s own venture haul.' },
      { t: 'Match, never undercut', d: 'New listings go up at the current lowest price (or Universalis); existing ones get re-priced.' },
      { t: 'Runs inside AutoRetainer', d: 'Optional hook into the venture cycle so every retainer gets stocked and priced hands-free.' },
    ],
  },
  {
    slug: 'autopotion', name: 'AutoPotion', origin: 'Original', hasWindow: true,
    short: 'Auto-uses HP potions and deep-dungeon regen items at your thresholds.',
    tag: 'Never watch your HP bar again.',
    command: '/autopotion · /pot', credit: 'Original plugin by dajoey · built on the Dalamud SDK',
    features: [
      { t: 'Best potion, always', d: 'Scans your bag and fires the highest-tier HP potion available (HQ first).' },
      { t: 'Deep dungeon aware', d: 'Sustaining, Empyrean, Orthos, Eurekan and Pilgrim\u2019s potions, by zone.' },
      { t: 'Per-job profiles', d: 'Separate toggles and thresholds for every class — ethers on casters, off on tanks.' },
    ],
  },
  {
    slug: 'armoire', name: 'Armoire Auto-Fill', origin: 'Original', hasWindow: true,
    short: 'A per-dungeon view of the armoire gear pieces you\u2019re still missing.',
    tag: 'Know exactly which glamour pieces you\u2019re missing.',
    command: '/armoire', credit: 'Original plugin by dajoey · joins the Cabinet sheet with LuminaSupplemental',
    features: [
      { t: 'Missing-piece tracker', d: 'Lists every armoire-eligible dungeon drop you don\u2019t yet own.' },
      { t: 'Three-state detection', d: 'Knows whether each piece is in the armoire, in inventory, or equipped.' },
      { t: 'Completion progress', d: 'A running tally per dungeon and overall, so you can chase the gaps.' },
    ],
  },
  {
    slug: 'lazywtmath', name: 'Lazy WT Math', origin: 'Fork', hasWindow: false,
    short: 'Row probabilities on the Wondrous Tails board, plus your reshuffle odds.',
    tag: 'The odds on your Wondrous Tails, done for you.',
    command: 'overlay', credit: 'Fork of EzWondrousTails',
    features: [
      { t: 'Row probabilities', d: 'Shows the chance of completing each line directly on the journal.' },
      { t: 'Reshuffle math', d: 'Tells you the average odds of what a Second Chance shuffle would do.' },
      { t: 'In-place overlay', d: 'Numbers render right on the Wondrous Tails window — nothing extra to open.' },
    ],
  },
  {
    slug: 'lazyfateautomation', name: 'Lazy Fate Automation', origin: 'Original', hasWindow: true,
    short: 'Fully automated FATE grinding using vnavmesh, lifestream and Gluttony Combo.',
    tag: 'FATE grinding that runs itself.',
    command: '/lazyfate', credit: 'Original plugin by dajoey · orchestrates vnavmesh + lifestream',
    features: [
      { t: 'End-to-end automation', d: 'Finds, travels to, and clears FATEs without you touching the keyboard.' },
      { t: 'Smart travel', d: 'Uses vnavmesh for pathing and lifestream for the longer hops.' },
      { t: 'Auto-combat', d: 'Leans on Gluttony Combo so your damage rotation just happens.' },
    ],
  },
  {
    slug: 'lazyskywardtracker', name: 'Lazy Skyward Tracker', origin: 'Original', hasWindow: true,
    short: 'Track your Skybuilders\u2019 points for all jobs toward the Pteranodon mount.',
    tag: 'Every Skybuilders\u2019 point, all the way to the Pteranodon.',
    command: '/lazysky', credit: 'Original plugin by dajoey',
    features: [
      { t: 'All jobs at once', d: 'Live point totals for every crafter, side by side.' },
      { t: 'Mount progress', d: 'Rolls each job into your overall progress toward the Pteranodon.' },
      { t: 'Per-job breakdown', d: 'Green when a job\u2019s done, orange while it\u2019s still climbing.' },
    ],
  },
  {
    slug: 'lazycurrencyspender', name: 'Lazy Currency Spender', origin: 'Original', hasWindow: true,
    short: 'Finds the best way to spend your tomestones, scrips and Poetics — backed by live Universalis prices.',
    tag: 'Spend every tomestone where it’s worth the most.',
    command: '/currencyspender · /lazycur', credit: 'Original plugin by dajoey',
    features: [
      { t: 'Universalis-priced', d: 'Pulls live market prices to show the exact gil-per-currency value of each exchange.' },
      { t: 'Tomestone gear & items', d: 'Surfaces weekly-capped and uncapped tomestone gear, weapons and items worth redeeming.' },
      { t: 'Fills your collections', d: 'Scans your sheet for missing minions, mounts and orchestrion rolls you can buy with what you hold.' },
    ],
  },
  {
    slug: 'lazyfoodbuff', name: 'LazyFoodBuff', origin: 'Original', hasWindow: true,
    short: 'Auto-eats food in combat duties (incl. deep dungeons), with per-job selection and a low-food warning.',
    tag: 'Never forget to eat your food again.',
    command: '/lazyfoodbuff', credit: 'Original plugin by dajoey \u00b7 built on the Dalamud SDK',
    features: [
      { t: 'Auto-eat in duties', d: 'Eats food when you enter combat duties \u2014 dungeons, raids, trials, alliance raids, criterion and variant.' },
      { t: 'Best food, per job', d: 'Auto-selects the highest-value food in your bag for your current job, or use a manual per-job pick.' },
      { t: 'Refresh & warn', d: 'Re-eats before the buff expires and warns you in chat when you are running low on food.' },
    ],
  },
  {
    slug: 'lazygearcollector', name: 'Lazy Gear Collector', origin: 'Original', hasWindow: true,
    short: 'Tracks upgradable gear sets \u2014 what you own, what tier it is, and what it costs to finish.',
    tag: 'Stop doing gear bookkeeping in your head.',
    command: '/lazygear', credit: 'Original plugin by dajoey',
    features: [
      { t: 'Every role, every slot', d: 'Occult Crescent North Horn\u2019s Phantom Vision sets \u2014 7 roles, 5 slots, 4 tiers \u2014 with per-role progress and click-through detail.' },
      { t: 'Prices from the game itself', d: 'Reads the shop tables at runtime, so obol and fixative costs are the game\u2019s own numbers and survive patches.' },
      { t: 'Spots free trade-ups', d: 'Flags Arcanaut\u2019s gear you can exchange straight in, including the two-step route that saves 4,000 obols a piece.' },
    ],
  },
  {
    slug: 'lazyoccultcrescent', name: 'Lazy Occult Crescent', origin: 'Fork', hasWindow: true,
    short: 'Field companion for Occult Crescent \u2014 South Horn and North Horn. Radar, trackers, and an optional farm loop.',
    tag: 'Knows the whole Crescent. Both horns.',
    command: '/lazyoccult \u00b7 /lazyoc', credit: 'Fork of BOCCHI by OhKannaDuh \u00b7 AGPLv3',
    features: [
      { t: 'North Horn, day one', d: 'FATE and Critical Encounter tables datamined from 7.55, plus all six North Horn aetheryte shards.' },
      { t: 'Learns the zone itself', d: 'Shard and event positions are read from the live object table and cached, so a new zone bootstraps over your first lap instead of needing a hand survey.' },
      { t: 'Treasure & carrot radar', d: 'Draws lines to nearby coffers and Fortune Carrots, with a precomputed optimal looting route.' },
      { t: 'Drives Gluttony Combo', d: 'Uses GluttonyCombo for rotations when installed \u2014 the only provider that implements all eight phantom jobs added in 7.55.' },
    ],
  },
  {
    slug: 'lazycrafter', name: 'LazyCrafter', origin: 'Original', hasWindow: true,
    short: 'Catalogs every recipe you can craft, prices it with Universalis, and hands the missing materials to Artisan, GatherBuddyReborn, AutoRetainer and Lifestream.',
    tag: 'What can I make right now \u2014 and what would it take to make the rest?',
    command: '/lcraft', credit: 'Original plugin by dajoey \u00b7 orchestrates Artisan + GatherBuddyReborn + AutoRetainer + Lifestream',
    features: [
      { t: 'Whole-craft catalog', d: 'Every recipe on your crafters, priced with Universalis, with both cost columns \u2014 cash (mats you own are free) and at-market.' },
      { t: 'Effort buckets', d: 'Missing materials sorted by how much work they take: in bags, on a retainer, vendor, gathering node, market board, or a retainer venture away.' },
      { t: 'One cart, one button', d: 'Stack crafts into a cart and press Dispatch: the dependency tree is planned and each leg goes to the plugin that already does it best.' },
      { t: 'Live Run tab', d: 'Every step with state and a plain-English reason. When a leg cannot be automated it stops and tells you exactly what to buy, then resumes.' },
    ],
  },
{
    slug: 'lazyretainerlive', name: 'LazyRetainerLive', origin: 'Original', hasWindow: true,
    short: 'Serves the logged-in character\u2019s live retainer table to the ffxiv dashboard, so venture countdowns update the moment a venture completes.',
    tag: 'Real-time retainer venture countdowns for your dashboard.',
    command: '/lazyretainerlive', credit: 'Original plugin by dajoey \u00b7 built on the Dalamud SDK',
    features: [
      { t: 'Live venture countdowns', d: 'Reads the in-game retainer table every second \u2014 the same data AutoRetainer\u2019s own timers come from \u2014 instead of the config file that only saves at AutoRetainer\u2019s leisure.' },
      { t: 'Dashboard-ready JSON', d: 'Serves the exact frame shape the ffxiv dashboard relay already speaks, on loopback only. When you are not logged in it answers 503 and the dashboard quietly falls back to file data.' },
      { t: 'Read-only companion', d: 'Never writes, assigns, or collects anything. AutoRetainer stays the boss of your retainers; this plugin just lets your dashboard see what it sees.' },
    ],
},
  {
    slug: 'lazyfishsitter', name: 'Lazy Fish Sitter', origin: 'Original', hasWindow: true,
    short: 'Sits you down while you fish. That is it.',
    tag: 'Fishing is better sitting down.',
    features: [
      { t: 'Sit while you cast', d: 'Checks every few seconds while you are fishing and runs /sit if you are standing.' },
      { t: 'Stays out of the way', d: 'Never re-sits once you are seated (ground sit, chair, or pose) and pauses in cutscenes, events and combat.' },
    ],
  },
];
if (typeof module !== 'undefined') module.exports = PLUGINS;
