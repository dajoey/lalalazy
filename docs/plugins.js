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
    slug: 'dagobert', name: 'Dagobert Price Matcher', origin: 'Fork', hasWindow: true,
    short: 'Matches market board prices instead of undercutting.',
    tag: 'Match the board. Never undercut yourself again.',
    command: '/pricematch', credit: 'Fork of Dagobert by SHOEGAZEssb · AGPLv3',
    features: [
      { t: 'Exact-match by default', d: 'Default match amount is 0 — list at the current lowest, no race to the bottom.' },
      { t: 'Configurable margin', d: 'Set a match amount if you want to sit a little under or over the board.' },
      { t: 'Hands-off listing', d: 'Re-prices as you list, so you spend less time fiddling with the retainer.' },
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
    short: 'Auto-eats food in combat duties, with per-job selection and a low-time warning.',
    tag: 'Never forget to eat your food again.',
    command: '/lazyfoodbuff', credit: 'Original plugin by dajoey \u00b7 built on the Dalamud SDK',
    features: [
      { t: 'Auto-eat in duties', d: 'Eats food when you enter combat duties \u2014 dungeons, raids, trials, alliance raids, criterion and variant.' },
      { t: 'Best food, per job', d: 'Auto-selects the highest-value food in your bag for your current job, or use a manual per-job pick.' },
      { t: 'Refresh & warn', d: 'Re-eats before the buff expires and warns you in chat when it is running low.' },
    ],
  },
];
if (typeof module !== 'undefined') module.exports = PLUGINS;
