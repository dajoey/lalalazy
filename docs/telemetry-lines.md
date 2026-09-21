# Telemetry line grammar: `ER|` errors and `RP|` problem reports

Source of truth for the lines written by the shared error-reporting library
`src/Shared/LalaTelemetry` (namespace `Lalalazy.Telemetry`). The pilot plugins are GluttonyCombo and
LazyMarketCompanion. The grammar is pinned by `tests/LalaTelemetry.Harness`: key order, escaping,
the report block shape and the size bounds are all asserted there. Run
`dotnet tests/LalaTelemetry.Harness/bin/Release/net10.0/LalaTelemetry.Harness.dll --print-samples`
to print fresh synthetic examples of every line kind.

Older positional lines (`CT|`, `BT|`, `CR|`, `MT|`, `PT|`, `FT|`, `PS|`, `PSP|`, `XB|`, `XB+|`, `XP|`)
are unchanged. Their grammars live in the `*TelemetryFormat.cs` file that writes them. This library
only adds `ER|`, `RP|` and the ring-only `NT|`.

## 1. Where the lines are

Every line is one entry in `dalamud.log`, written through the plugin's own `IPluginLog`:

```
2026-09-21 18:04:05.123 -04:00 [ERR] [GluttonyCombo] ER|1788000000000|error|p=GluttonyCombo|...
```

The `[Context]` tag is the plugin's InternalName. In ffxivdb `plugin_log_lines` it is `context`, and
`message` starts at the prefix, so the filter is `context = '<Plugin>' AND message LIKE 'ER|%'`.
The message template is `{Line:l}`, so braces in exception text reach the file literally.

## 2. Common record grammar (`ER|`, `RP|`, `NT|`)

```
record   = prefix unixms "|" kind *( "|" field )
prefix   = 2*2UPPER "|"                 ; "ER|", "RP|", "NT|"
unixms   = 1*DIGIT                      ; UTC milliseconds since the Unix epoch
kind     = 1*( LOWER / "-" )            ; e.g. error, summary, begin, ring
field    = key "=" value
key      = 1*LOWER                      ; ASCII a-z only, never contains "="
value    = *( escaped-char )            ; see the escaping table
```

Parsing is exact. Split the record on the literal `|`. After escaping, a value never contains a raw
`|`, CR, LF, TAB or other control character. Split each field on its first `=`. Unescape the value.

| Raw character | Written as |
|---|---|
| `\` | `\\` |
| `\|` (pipe) | `\p` |
| LF | `\n` |
| CR | `\r` |
| TAB | `\t` |
| any other U+0000 to U+001F, and U+007F | `\u` followed by 4 lowercase hex digits (e.g. `\u0001`) |
| everything else (all other Unicode) | itself (UTF-8 in the file) |

An unknown escape (`\q`) or a trailing lone `\` is kept literally. The writer never produces one.

**Truncation is explicit.** A value longer than its cap is cut to its first N source characters,
never splitting a surrogate pair. The key is then listed in a final field `tr=<key>[,<key>...]`,
for example `tr=m,st`. `tr` is always the last field when present, and it is never used as a data key.

**Key order.** Keys are written in the fixed order listed below. Parsers should still look keys up by
name and ignore unknown keys. A future version may add optional keys (always before `tr`), but it will
never rename or reuse one.

## 3. `ER|` lines: errors

| kind | level | when |
|---|---|---|
| `start` | INF | once per plugin load: identity only (`p v ch c`), the anchor for joining errors to a build |
| `error` | ERR | an exception at a top-level catch point (framework tick, hook detour, IPC/event handler, command) |
| `swallowed` | WRN | an exception the code deliberately continues past (was a silent or Debug-only catch) |
| `unobserved` | ERR | `TaskScheduler.UnobservedTaskException` whose stack involves THIS plugin's assembly (other plugins' faulted tasks are ignored) |
| `summary` | level of the kind it summarises | suppressed repeats of one fingerprint (see 3.3) |
| `trip` | ERR | a circuit breaker stopped a per-frame handler (see 3.4) |
| `recover` | INF | a stopped handler ran clean again |

### 3.1 Keys

| key | kinds | meaning |
|---|---|---|
| `p` | all | plugin InternalName |
| `v` | all | assembly version, e.g. `1.0.4.229` |
| `ch` | all | `testing`, `production` or `dev` from Dalamud (`IDalamudPluginInterface.IsTesting` / `IsDev`) for this load, or `unknown` |
| `c` | all | short git SHA (at least 10 hex) of the source tree the DLL was built from, or `unknown` when the build had no git. Release commits are made after packaging, so this is normally the release commit's parent. |
| `a` | all but `start` | area: a stable machine name for the catch point, e.g. `tick`, `tick.bst`, `action-replacer`, `automation.draw` |
| `fp` | error, swallowed, unobserved, summary, trip | fingerprint, 12 lowercase hex (see 3.2) |
| `n` | error, swallowed, unobserved, summary | occurrences of this fingerprint since the plugin loaded, this one included |
| `tt` | all but `start` | TerritoryType id (0 = unknown) |
| `j` | all but `start` | ClassJob row id (0 = unknown). Numeric on purpose: the review job folds digits, so jobs never split a group. |
| `lv` | all but `start` | character level (0 = unknown) |
| `cb` | all but `start` | `1` = InCombat |
| `du` | all but `start` | `1` = BoundByDuty (any of the 3 duty flags) |
| `x` | error, swallowed, unobserved, summary, trip | full type name of the outermost exception |
| `m` | error, swallowed, unobserved, summary, trip | its message (cap 512) |
| `d` | error, swallowed (optional) | caller detail such as `item 4487 hq=1` (cap 300). It is not part of the fingerprint. |
| `st` | error, swallowed, unobserved | `Exception.ToString()`: type, message, inner exceptions and every frame, with `in <file>:line <n>` for the plugin's own frames because the PDB is embedded (cap 8000) |
| `of` | summary | the kind being summarised (`error` / `swallowed` / `unobserved`) |
| `sup` | summary | occurrences suppressed since the previous line for this fingerprint |
| `span` | summary | milliseconds since the previous line for this fingerprint |
| `ev` | trip, recover | `tripped` (first stop), `reopened` (a retry failed), `recovered` |
| `trips` | trip, recover | times this breaker has opened this session |
| `fails` | trip, recover | failures recorded by this breaker this session |
| `cool` | trip, recover | milliseconds until the next retry (0 on recover) |
| `tr` | any | truncated keys (section 2) |

Key order per kind (asserted by the harness):

```
start      p,v,ch,c
error      p,v,ch,c,a,fp,n,tt,j,lv,cb,du,x,m,[d],st
swallowed  p,v,ch,c,a,fp,n,tt,j,lv,cb,du,x,m,[d],st
unobserved p,v,ch,c,a,fp,n,tt,j,lv,cb,du,x,m,st             (a is always task.unobserved)
summary    p,v,ch,c,a,fp,n,tt,j,lv,cb,du,of,sup,span,x,m
trip       p,v,ch,c,a,ev,trips,fails,cool,tt,j,lv,cb,du,fp,x,m
recover    p,v,ch,c,a,ev,trips,fails,cool,tt,j,lv,cb,du
```

Context (`tt j lv cb du`) is read live on the framework thread. Off it (task continuations, the
finalizer thread), it is the last framework-thread read, refreshed once a second.

### 3.2 Fingerprint

`fp = first 12 hex of SHA-1( "<p>|<a>|<typeChain>|<frames>" )`

- `typeChain`: outer-to-inner exception type full names joined by `>`. For an AggregateException, only the first inner exception is followed.
- `frames`: the first 5 frames of the outermost exception in the chain that has any. For a task's AggregateException, that is its first inner exception. Each frame is written as `Namespace.Type.Method`, read without file info (no paths, no line numbers), with every run of digits replaced by `#`. Frames are joined by `;`.
- If no exception in the chain has frames (it was never thrown), the message is used instead, with quoted text replaced by `'_'` and digits by `#`.

So `fp` does not change with the message, line shifts, lambda renumbering, the build host or the
job/zone. It changes with the plugin, the area, the exception types and the top of the call path.
**Group `ER|` rows by `(p, fp)`**, not by the normalised message. The message carries ids and names,
and the stack text differs between builds.

### 3.3 Rate limiting (per fingerprint, per plugin load)

1. The first 3 occurrences are written in full (`n=1..3`).
2. Later occurrences are counted, not written. At most one `summary` per 60 s carries them.
   `sup` is the count since the previous line and `n` is the running total. A count still pending
   when the failure stops is written once 60 s have passed since that fingerprint's previous line
   (checked every second). It is also written when a report is filed, and on unload.
3. After 5 minutes without an occurrence the fingerprint re-arms: the next one is written in full
   again, preceded by a `summary` if a count was still pending.

Reconciliation: for one `(p, fp)` in one load, once nothing is pending (always true after unload),
`max(n)` over all its lines equals the number of full lines plus the sum of `sup`.

### 3.4 Circuit breakers

Per-frame handlers run under a breaker named by `a`. The pilots use these areas:

| plugin | area | handler |
|---|---|---|
| GluttonyCombo | `tick` | the whole framework tick (auto-rotation, save queue, state) |
| GluttonyCombo | `tick.bst` | Beastmaster collectors and Crucible familiar selection, inside `tick` |
| GluttonyCombo | `tick.status` | server info bar text and tankbuster/AoE alerts, inside `tick` |
| LazyMarketCompanion | `automation.draw` | retainer automation overlay + AutoRetainer session watchdog |
| LazyMarketCompanion | `markers.draw` | inventory marker overlay |
| LazyMarketCompanion | `market-board.tick` | market-board price-request timeouts |
| LazyMarketCompanion | `inventory.tick` | Inventory tab upkeep: last-seen capture, AutoRetainer file reads, the running Inventory action |
| LazyMarketCompanion | `inventory.tooltip` | the "who handles this stack" box under item tooltips |
| LazyMarketCompanion | `inventory.tab` | the Inventory tab's draw |

- **Trip:** 10 failures within 5 s. One `trip` line (`ev=tripped`) is written, and ONE chat notice
  for the whole session, e.g. `Gluttony Combo: auto-rotation and the per-frame update stopped after
  repeated errors and will retry on its own. Details are in the plugin log. "/gluttony report <what
  happened>" writes a problem report.`
- **Retry:** after 30 s one trial call runs. If it fails, the breaker reopens (`trip`, `ev=reopened`)
  with double the cooldown, capped at 5 min, and no further notice is shown. If it runs clean, a
  `recover` line is written, plus one "is running again" notice if the stop notice was shown.
- While `automation.draw` is open, LazyMarketCompanion releases AutoRetainer postprocess requests
  untouched instead of starting a session nothing would watch.

## 4. `RP|` problem report

Written by `/<cmd> report <what happened>` (`/gluttony report ...`, `/lmc report ...`) or by the
"Report a problem" button in the plugin window. Chat confirms `Problem report <id> written to the
plugin log.` At most one report per 10 s.

**Framing.** ONE log entry at **WRN**, so it outlives the 7-day INF purge. The entry is several
physical lines joined by LF. Only the first line carries the timestamp and `[WRN] [<Plugin>]`. The
harvester folds the continuation lines into the same row, so `plugin_log_lines.message` holds the
whole block. Every physical line is also a complete, independently parseable record:

```
RP|<unixms>|<section>|id=<id>|...
```

`<unixms>` and `id` are identical on every line of one report. The block is complete only if its
first line is `begin`, its last line is `end`, and both carry `lines=` equal to the number of
physical lines. Reject anything else as truncated.

`id` is 8 characters of Crockford base32: 7 characters of the unix second, plus 1 per-load counter
character (e.g. `1N95DVA1`). It is the id printed in chat.

Sections, in this order (`*` = repeated, possibly zero times):

| section | keys | content |
|---|---|---|
| `begin` | `id fmt p v ch c lines` | `fmt` = report format version (currently `1`) |
| `text` | `id t` | the free text as typed (cap 1000) |
| `game` | `id src at tt j ja lv cb du li pos tgt cond` | `src` = `live` or `cached` or `none`, `at` = unix ms of the read, `ja` = job abbreviation, `li` = logged in, `pos` = `x,y,z` (2 decimals, empty if unknown), `tgt` = `<ObjectKind>:<BaseId>:<EntityId>:<name>` where another player's name is always written `pc`, `cond` = every set ConditionFlag name, comma-separated |
| `state` | `id s` | `guard.<area>=<closed/open/halfopen>/<trips>/<failures>` for every breaker, then the plugin's own state (`;`-separated `k=v`). GluttonyCombo: auto-rotation on/paused/locked, opener, telemetry switch, every enabled preset for the current job (`*` = in auto-rotation). LazyMarketCompanion: its `/lmc debug` state. |
| `config` | `id s` | `k=v;...` scalars of the plugin configuration (bool as 0/1, numbers, enums). Collections are `name#=count`, and nested objects `parent.child=`. Strings are never included. Cap 4000. |
| `errs` | `id n held seen` | `n` err lines follow (newest 16 of `held` in memory, `seen` since load) |
| `err`* | `id i at l` | `l` = a full `ER|` line (cap 2000, then `tr=l`), oldest first |
| `rings` | `id n seen` | `n` ring lines follow, `seen` = lines ever added since load |
| `ring`* | `id i at l` | `l` = one telemetry line exactly as the plugin formatted it (cap 600), oldest first. It is either a positional line (`CT|`, `BT|`, `MT|`, ...) or an `ER|` head cut to 300 chars, or `NT|<ms>|notice|a=..|m=..` (a chat notice that was shown), or `RP|<ms>|filed|id=..` (an earlier report). |
| `addons` | `id n visible` | names of every visible addon, sorted, comma-separated (at most 150) |
| `addon`* | `id name n vals` | up to 12 visible WINDOWS (names starting with `_` are HUD parts and appear in `visible` only, `ChatLog*` is skipped). `n` = AtkValue count, `vals` = `index:type=value;` for the first 24 defined values. Types: `b` bool, `i` int, `u` uint, `f` float, `s` string (cap 48, `;` replaced by `,`), `tN` other type N. |
| `note`* | `id m` | a section that could not be read and why. The report is still written. |
| `end` | `id lines` | |

The `l` values of `err` and `ring` are records in their own right. Unescape `l` once to get the
original line, then parse it with its own grammar.

**Size.** Every section is capped. The worst case with every cap hit is under 200 KB (asserted). A
typical report is 10 to 40 KB.

### 4.1 What the ring holds

Each plugin keeps the last 200 telemetry lines in memory, always, whatever its telemetry switch says
(nothing is written to the log unless the switch is on):

- GluttonyCombo: `CT|` combo decisions. With the switch off, a changed decision is still formatted
  into the ring, capped at 10 lines/s (burst 20), never per frame. `BT|`, `CR|`, `XB|`, `XB+|` and
  `XP|` are recorded only while the switch is on (their sampling runs per frame). `PS|` and `PSP|`
  (Crucible familiar selection) are recorded whenever they are emitted, which is independent of the switch.
- LazyMarketCompanion: `MT|` price decisions. With the switch off, a light copy with `itemId` 0 and
  `qty` 0 (no Item-sheet scan) goes to the ring, one per price decision. Every `IV|` Inventory-tab
  action line (section 4.2) also goes to the ring, whatever the switch says.
- Both: the head of every `ER|` line, every chat notice (`NT|`), every filed report (`RP|..|filed`).

The plugins' own narrative INF lines (`[LMC] ...`) are not in the ring. They are already in the log;
join them to a report on `context` and a time window around the report's `unixms`.

### 4.2 `IV|` Inventory-tab action lines (LazyMarketCompanion)

Every action the Inventory tab takes, or refuses, is one `IV|` line at INF, in the same record grammar
as section 2 (built with the shared `TelemetryLineBuilder`; fixed key order, pinned by
`tests/LazyMarketCompanion.Harness` case group I10). The same line is appended to the append-only audit
file `<pluginConfigs>/LazyMarketCompanion/lmc_inventory_actions.log`. Container names are the
FFXIVClientStructs `InventoryType` names (`RetainerPage2`, `Inventory1`, `ArmoryBody`).

```
IV|<ms>|vendor|v|r|src|i|hq|q|est|ok|why          one venture-loot stack sold through the open retainer
IV|<ms>|move|v|src|dst|i|hq|ok|rc|why             one bags -> Armoury move (why ends with the trigger: manual / idle)
IV|<ms>|undo|v|src|dst|i|hq|ok|rc|why             one Armoury -> bags move back
IV|<ms>|batch|v|what|ev|n|okn|fail|why            what = vendor / move / undo; ev = begin / end / refused
IV|<ms>|optin|v|i|on|rail                         an item allowed past (on=1) or back behind (on=0) the unique / untradable / rare rail
```

`ok=1` on `vendor` means the sell call was issued after the slot re-read matched; `ok=1` on `move`/`undo`
means the source slot emptied and the destination slot was seen holding the item.

## 5. Reference parser (Python)

```python
import re

ESC = {"\\": "\\", "p": "|", "n": "\n", "r": "\r", "t": "\t"}

def unescape(v: str) -> str:
    out, i = [], 0
    while i < len(v):
        c = v[i]
        if c == "\\" and i + 1 < len(v):
            e = v[i + 1]
            if e in ESC:
                out.append(ESC[e]); i += 2; continue
            if e == "u" and i + 5 < len(v) and re.fullmatch(r"[0-9a-fA-F]{4}", v[i + 2:i + 6]):
                out.append(chr(int(v[i + 2:i + 6], 16))); i += 6; continue
        out.append(c); i += 1
    return "".join(out)

def parse_line(line: str):
    """One ER|/RP|/NT| record -> (prefix, unixms, kind, {key: value}) or None."""
    parts = line.rstrip("\r").split("|")          # safe: escaped values never contain a raw '|'
    if len(parts) < 3 or not re.fullmatch(r"[A-Z]{2}", parts[0]) or not parts[1].isdigit():
        return None
    fields = {}
    for p in parts[3:]:
        key, eq, value = p.partition("=")          # split on the FIRST '=' only
        if not eq or not re.fullmatch(r"[a-z]+", key):
            return None
        fields.setdefault(key, unescape(value))   # first occurrence wins
    return parts[0] + "|", int(parts[1]), unescape(parts[2]), fields

def parse_report(message: str):
    """A plugin_log_lines.message that starts with 'RP|' -> dict, or None if incomplete."""
    recs = [parse_line(l) for l in message.split("\n") if l.strip()]
    if not recs or any(r is None or r[0] != "RP|" for r in recs):
        return None
    begin, end = recs[0], recs[-1]
    rid = begin[3].get("id")
    if begin[2] != "begin" or end[2] != "end" or any(r[3].get("id") != rid for r in recs):
        return None
    if int(begin[3]["lines"]) != len(recs) or int(end[3]["lines"]) != len(recs):
        return None                               # truncated block
    rep = {"id": rid, "unixms": begin[1], "begin": begin[3], "ring": [], "err": [], "addon": [], "note": []}
    for _, _, kind, f in recs[1:-1]:
        if kind == "ring":
            rep["ring"].append((int(f["at"]), f["l"]))       # raw line: CT|/BT|/MT|... have their own grammars
        elif kind == "err":
            rep["err"].append(parse_line(f["l"]))            # always an ER| record (may carry tr=l if cut)
        elif kind in ("addon", "note"):
            rep[kind].append(f)
        else:
            rep[kind] = f                              # text, game, state, config, errs, rings, addons
    return rep
```

This parser was run against the harness's `--print-samples` output (6 `ER|` lines and one 25-line
`RP|` block). It also rejects a block with its `end` line removed.

## 6. Examples (synthetic)

```
[INF] ER|1788000000000|start|p=GluttonyCombo|v=1.0.4.229|ch=testing|c=8dabb3bf20
[ERR] ER|1788000000000|error|p=GluttonyCombo|v=1.0.4.229|ch=testing|c=8dabb3bf20|a=tick|fp=3f6a7627e6b3|n=1|tt=1252|j=39|lv=100|cb=1|du=0|x=System.TypeInitializationException|m=The type initializer for 'Some.Static.Holder' threw an exception.|st=System.TypeInitializationException: The type initializer for 'Some.Static.Holder' threw an exception.\n ---> System.NullReferenceException: static ctor\n   --- End of inner exception stack trace ---\n   at GluttonyCombo.Example.Tick() in C:\\...\\src\\GluttonyCombo\\GluttonyCombo\\Example.cs:line 42\n   ...
[ERR] ER|1788000000144|trip|p=GluttonyCombo|v=1.0.4.229|ch=testing|c=8dabb3bf20|a=tick|ev=tripped|trips=1|fails=10|cool=30000|tt=1252|j=39|lv=100|cb=1|du=0|fp=3f6a7627e6b3|x=System.TypeInitializationException|m=The type initializer for 'Some.Static.Holder' threw an exception.
[ERR] ER|1788000061192|summary|p=GluttonyCombo|v=1.0.4.229|ch=testing|c=8dabb3bf20|a=tick|fp=3f6a7627e6b3|n=12|tt=1252|j=39|lv=100|cb=1|du=0|of=error|sup=9|span=61160|x=System.TypeInitializationException|m=The type initializer for 'Some.Static.Holder' threw an exception.
[INF] ER|1788000091208|recover|p=GluttonyCombo|v=1.0.4.229|ch=testing|c=8dabb3bf20|a=tick|ev=recovered|trips=1|fails=12|cool=0|tt=1252|j=39|lv=100|cb=1|du=0
[WRN] ER|1788000100000|swallowed|p=LazyMarketCompanion|v=0.1.65.0|ch=testing|c=8dabb3bf20|a=config.import-dagobert|fp=0b1c2d3e4f50|n=1|tt=129|j=18|lv=100|cb=0|du=0|x=Newtonsoft.Json.JsonReaderException|m=Unexpected character ...|d=starting with defaults|st=...
```

A report, abridged (the real block also carries `err`/`ring` lines for every entry):

```
[WRN] RP|1788000106208|begin|id=1N95DVA1|fmt=1|p=GluttonyCombo|v=1.0.4.229|ch=testing|c=8dabb3bf20|lines=12
RP|1788000106208|text|id=1N95DVA1|t=rotation stopped after the second pull \p expected Brutal Rage
RP|1788000106208|game|id=1N95DVA1|src=live|at=1788000106200|tt=1252|j=39|ja=BST|lv=100|cb=1|du=0|li=1|pos=12.50,0.00,-40.25|tgt=BattleNpc:1234:1073741901:Training Dummy|cond=InCombat,NormalConditions
RP|1788000106208|state|id=1N95DVA1|s=guard.tick=closed/1/12;guard.tick.bst=closed/0/0;guard.tick.status=closed/0/0;auto=1;paused=0;locked=0;actionChanging=1;comboTelemetry=0;activeJobPresets=5;opener=...;presets=BST_ST_Main*,...
RP|1788000106208|config|id=1N95DVA1|s=Version=7;ActionChanging=1;Throttle=50;EnabledActions#=412;...
RP|1788000106208|errs|id=1N95DVA1|n=1|held=1|seen=1
RP|1788000106208|err|id=1N95DVA1|i=0|at=1788000000000|l=ER\p1788000000000\perror\pp=GluttonyCombo\p...
RP|1788000106208|rings|id=1N95DVA1|n=1|seen=1
RP|1788000106208|ring|id=1N95DVA1|i=0|at=1788000091000|l=CT\p1788000091000\pBST\pBST_ST_Main\p44880\p44881\p1.20\p0-\p87.5\p
RP|1788000106208|addons|id=1N95DVA1|n=3|visible=SelectString,_ActionBar,_TargetInfo
RP|1788000106208|addon|id=1N95DVA1|name=SelectString|n=5|vals=0:s=Choose;1:u=3;2:s=Option A;
RP|1788000106208|end|id=1N95DVA1|lines=12
```

## 7. Notes for the ffxivdb pipeline

- `ER|` rows land in the existing 07:10 review like any ERR/WRN row. The review groups by
  `sha1(level|context|normalized message)`, so a group can still split by `m`/`st` text. Grouping
  `ER|` rows by `(context, fp)` (section 3.2) is the stable key.
- `RP|` blocks are WRN and would otherwise be one-row groups in that review. Handle them as reports
  (section 4): exclude `message LIKE 'RP|%'` from the error grouping and route each block by its `id`.
- `ER|start` (INF) gives version, channel and commit for every load. It is the join key between a
  `c=` SHA and a published build.
- Stack frames from the plugin's own code now carry `in <absolute build path>:line <n>`: the PDB is
  embedded in the DLL, and packaging still ships no `.pdb`. The path is wherever the DLL was built.
