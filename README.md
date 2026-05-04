# ACT_Plugin_EQ1 — EverQuest 1 (English) Parsing Plugin for Advanced Combat Tracker

A from-scratch ACT parsing plugin for the original EverQuest. Implements the
`IActPluginV1` contract: hooks `FormActMain.BeforeLogLineRead` and converts
EQ1 chat-log lines into `MasterSwing` events that ACT's encounter engine,
real-time mini-parse window, custom triggers, and exports all consume.

## Build

Open in Visual Studio (or run MSBuild) targeting **.NET Framework 4.8**:

```
"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe" ^
  ACT_Plugin_EQ1\ACT_Plugin_EQ1.csproj /p:Configuration=Release
```

Output: `ACT_Plugin_EQ1\bin\Release\ACT_Plugin_EQ1.dll`.

The project references `Advanced Combat Tracker.exe` at the standard install
path; adjust the `<HintPath>` in the `.csproj` if ACT is installed elsewhere.

## Install

1. In ACT, open **Options → Plugins → Plugin Listing**.
2. Browse to `ACT_Plugin_EQ1.dll` (or copy it to your ACT plugin folder).
3. Tick the checkbox to enable. The plugin's UI tab (**EQ1 Parser**) appears
   under the Plugins section.
4. In **Options → Parsing**, the parser advertises:
   - `Log file filter`: `eqlog*.txt`
   - `Log parent folder name`: `Logs`
   - `Character file regex`: `eqlog_(?<charname>[^_]+)_.+\.txt`
5. Point ACT at your EverQuest `Logs` folder and start parsing.

## Configuration

The **EQ1 Parser** tab provides per-feature toggles:

| Setting | Default | Effect |
| --- | --- | --- |
| Parse self / friendly heals | on | Heal-on-X, heal-self, heal-other lines |
| Parse rune / absorb messages | on | "X absorbed N damage" rune ticks |
| Parse damage-over-time ticks | on | DoT ticks on you and third parties |
| Log unmatched combat lines | off | Diagnostics: dumps unrecognized lines |

Settings persist to `<AppData>\Advanced Combat Tracker\Config\ACT_Plugin_EQ1.config.xml`.

## Detected events

The parser recognizes 25 distinct line categories. On a 437,834-line raid log
(`eq1log_Waer.txt`), it matches **341,421 of 342,715 combat-shaped lines
(99.6%)**.

| Category | Example |
| --- | --- |
| Player melee swing | `You slash a wan ghoul knight for 28 points of damage.` |
| Player melee miss | `You try to slash a wan ghoul knight, but miss!` |
| Player frenzy / extra | `You frenzy on a wan ghoul knight for 12 points of damage.` |
| Player slay | `You have slain a wan ghoul knight!` |
| NPC / 3rd-party melee | `A wan ghoul knight slashes YOU for 31 points of damage.` |
| NPC / 3rd-party miss | `A wan ghoul knight tries to slash YOU, but YOU dodge!` |
| Player spell DD | `You hit a wan ghoul knight for 162 points of magic damage by Dismiss Undead.` |
| NPC / 3rd-party spell DD | `Qrst hit a wan ghoul knight for 162 points of magic damage by Dismiss Undead.` |
| DoT tick (your spell) | `A wan ghoul knight has taken 11 damage from your Engulfing Darkness.` |
| DoT tick (other caster) | `A wan ghoul knight has taken 11 damage by Splurt.` |
| DoT tick (attributed) | `You have taken 11 damage from Splurt by a wan ghoul knight.` |
| Heal on you | `Altheia healed you for 287 hit points by Greater Healing.` |
| Self heal | `Qrst healed herself over time for 102 hit points by Ethereal Cleansing.` |
| Heal other | `Altheia healed Bob for 100 hit points by Greater Healing. (Critical)` |
| Rune absorb | `You gain a rune for 35 points of absorption.` |
| Mend | `You magically mend your wounds and heal considerable damage.` |
| Player death | `You have been slain by a wan ghoul knight!` |
| "You died" (no attacker) | `You died.` |
| Other slain | `A wan ghoul knight has been slain by Hendo!` |
| Zone change | `You have entered The Estate of Unrest.` |
| Damage shield (thorns/flames/etc.) | `YOU are pierced by a vampire bat's thorns for 14 points of non-melee damage!` |
| Damage shield absorbed | `A yun ghoul wizard's magical skin absorbs the damage of Ennui's flames.` |
| Generic non-melee | `You were hit by non-melee for 389 damage.` |
| Spell resist | `A wan ghoul knight resisted your Dismiss Undead!` |
| Taunt | `Hendo has captured a wan ghoul knight's attention!` |

Critical / dodge / parry / block / riposte / resist / absorbed / invulnerable
modifiers are detected via the `(...)` tail and recorded as `Dnum` specials so
ACT colorizes them correctly.

## Architecture notes

- **Pattern-driven dispatch.** `BuildRules()` registers a `List<Rule>` of
  `{ Regex, Handler, DetectedType, Keyword }`. `OnBeforeLogLineRead` extracts
  the body, runs a fast keyword pre-filter, then matches in priority order.
- **Time parsing.** EQ1 prefixes every line with `[ddd MMM dd HH:mm:ss yyyy]`
  (24 chars). `ParseEqDateTime` uses `DateTime.TryParseExact` with `en-US`.
- **Character detection.** When the active log file changes, the plugin reads
  `LogFilePath`, extracts the character via `eqlog_(?<charname>[^_]+)_…`, and
  pushes it into `ActGlobals.charName` so "You" resolves to the right name in
  third-party logs.
- **Encounter discipline.** Every non-trivial handler calls
  `ActGlobals.oFormActMain.SetEncounter(time, attacker, victim)` before
  `AddCombatAction(...)`, matching ACT's documented contract.

## Files

- `ACT_Plugin_EQ1.csproj` — Library project, .NET Framework 4.8.
- `EQ1Parser.cs` — All parsing rules, handlers, settings, UI.
- `test_patterns.ps1` — Standalone PowerShell harness that mirrors the rule
  set so coverage can be measured against any EQ1 log without launching ACT.
