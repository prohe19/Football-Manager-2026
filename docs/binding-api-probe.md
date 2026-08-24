# Binding API probe (Stage 2, step 2 — "plan A")

We already have the PropertyIDs (see [property-ids.md](property-ids.md)). The one
missing piece before we can read a real player's CA is the exact **binding call**:
given a person *Reference* + a *PropertyID*, which method returns the value?

Rather than guess the API and burn a build-and-test cycle, **v0.5.0 reflects over
FM26's own assemblies at runtime and dumps the API surface** — the same
"let the game tell us" approach that cracked the property registry.

## What v0.5.0 does

On load (a couple of seconds in — no clicking needed), `ScoutUI` sweeps every
loaded `SI.*` / `FM.*` / `*Bindable*` assembly and logs three sections:

1. **`interesting types`** — every type whose name contains
   `Reference` / `Binding` / `Bindable` / `Property` / `Person` / `Player`.
   This is how we spot a **rich reference type** (something beefier than
   `DbSummaryPersonReference`, which only had 3 properties) that actually exposes
   CA/PA/attributes.
2. **`deep dump of key types`** — full constructor + method + field signatures for
   the core types: `DbSummaryPersonReference`, `PropertyID`, `ReferenceID`,
   `PropertyIdentifierSet`, `PropertyIDInfo`, `InteropReference`, `BindingKind`.
   (Shows us how a `PropertyID`/`ReferenceID` wraps its raw `uint`, and how a
   reference is constructed.)
3. **`CANDIDATE READERS`** — the payoff: every method anywhere that **takes a
   `PropertyID` or `uint` and returns a primitive / string / binding**. One of
   these is the value-read call we need.

## How to run it

1. Close FM26 (a running game locks the DLL).
2. Build in Visual Studio (`Release`) — the post-build step copies
   `FM26ScoutMod.dll` into `…\Football Manager 26\BepInEx\plugins`.
3. Launch FM26 and **load a save** (so the game data layer is live).
4. Wait ~5 seconds. The panel shows `DONE - dumped N lines.`
5. Send me **`BepInEx\LogOutput.log`** (or paste the `BINDING API PROBE` section).

## What I'm looking for in your log

- A reference type richer than `DbSummaryPersonReference` (e.g. a
  `Db…PersonReference` / `Db…PlayerReference` with many methods).
- The shape of `PropertyID` / `ReferenceID` — is it a struct wrapping a `uint`?
  How do we build one from the raw ID (e.g. `1346584898`)?
- A `CANDIDATE READER` like `int GetInt(PropertyID)` / `float GetValue(...)` /
  `GetBinding(PropertyID)` — the call that returns the actual number.

Once we see those, the next build reads your first player's CA for real and
Stage 2 is done.

---

## ✅ Results (v0.5.0 probe, in a loaded save — 4,002 lines)

The probe swept **26** `SI.*` / `FM.*` assemblies (**6,042 types**) and answered
every open question.

### 1. `PropertyID` / `ReferenceID` are thin `uint` wrappers

```
struct SI.Bindable.Reference.Core.PropertyID   // : System.ValueType
    .ctor(UInt32 id)
    static PropertyID op_Implicit(UInt32 id)    // (PropertyID)1346584898u
    static UInt32     op_Implicit(PropertyID)   // back to uint
    UInt32 get_ID();  field UInt32 id

struct SI.Bindable.Reference.Core.ReferenceID  // : System.ValueType
    .ctor(UInt32 id)
    static ReferenceID op_Implicit(UInt32 id)
    ReferenceIDInfo GetInfo()                    // live info for an id
    UInt32 get_ID();  field UInt32 id
```

So building the CA property to ask for is just `(PropertyID)1346584898u`
(or `new PropertyID(1346584898u)`).

### 2. The value-read call: `InteropReference.TryGetValue`

Every `…Reference` type derives from **`SI.Interop.InteropReference`**, which
carries the actual read/write methods:

```
class SI.Interop.InteropReference   // base of ALL references
    .ctor(List<PropertyID> ids)              // build from id(s)
    ReferenceID get_ID()
    Boolean TryGetValue(UInt32 id, Int32& value)     // ⭐ READ a property
    Boolean TryGetProperty(UInt32 id, Int32& value)  // (same shape)
    Void    SetValue(UInt32 id, Int32 value)         // (write — later)
    Boolean AcceptsProperty(UInt32 property)
    Void    GetProperties(List<PropertyID> properties)
    field List<PropertyID> m_id
```

**`bool TryGetValue(uint propertyId, out int value)`** is the call we needed.
CA/PA are ints (0–200), so a player reference bound to a real person gives
`refr.TryGetValue(1346584898u, out int ca)`.

### 3. Every reference exposes the schema statics (as expected)

Each of the ~1,500 `…Reference` types has the boilerplate schema methods
(`AcceptsPropertyInternal`, `GetPropertyTypeInternal`,
`GetPropertyDescriptionInternal`, `DerivesFromInternal`). These describe the
schema; the **instance** `TryGetValue` reads the data.

### 4. Candidate rich person/player reference types (from Section 1)

Beyond the thin `DbSummaryPersonReference` (3 props), the dump lists real
person/player references to target for CA/PA:

- `FM.UI.PersonReference`, `FM.UI.IPlayerReference`, `FM.UI.IPersonBaseReference`,
  `FM.UI.INonPlayerReference`
- `FM.UI.PlayerAttributeReference`, `FM.UI.PlayerReportReference`,
  `FM.UI.MatchPlayerReference`, `FM.UI.SquadOverviewPlayerReference`
- Squad/collection routes to enumerate players:
  `FM.UI.TeamSquadReference`, `FM.UI.ClubReference`, `FM.UI.TacticsTeamSelectionReference`

## The one remaining unknown → Stage 2 finish

We can read a value **once we hold a live reference bound to a real person.**
The missing link is obtaining a player's live `ReferenceID` (or a bound
reference instance) from the loaded save. Two routes to try next:

1. **Reference registry** — `ReferenceIdentifierSet.Instance` (+ `ReferenceIDInfo`,
   `ReferenceID.GetInfo()`) may enumerate live references the same way
   `PropertyIdentifierSet` enumerated properties.
2. **Binding plumbing** — `SI.Bindable.Bindings`, `BindingSubsystem`, and the
   `VisualFunctionLibrary` helpers (`GetPropertyValue`, `TryGetDataReference`,
   `GetDynamicReference`) are how the game's own UI turns a `ReferenceID` into a
   value; one of these is our entry point.

The next build (v0.6.0) does a focused deep-dump of those types **and** attempts
a first real read, so we go from "we know the call" to "we printed a CA".

---

## ✅ v0.6.0 result — the database shortcut

The navigation dump revealed we don't need to walk Club→Squad at all. Person
records are **directly indexable**:

```
FM.UI.PersonReference : FM.UI.DatabaseRecordReference : ... : SI.Interop.InteropReference
    .ctor(Int32 index)     // build person #index straight from the DB
    .ctor(List<PropertyID> ids)
    static PersonReference GetInstance()
```

`ClubReference`, `TeamReference`, and `NationalTeamContainerReference` share the
same `DatabaseRecordReference` base and the same `.ctor(Int32 index)` — the whole
database is index-addressable. And because `PersonReference` inherits
`InteropReference.TryGetValue(uint, out int)`, the read is simply:

```csharp
var p = new PersonReference(index);
p.TryGetValue(1346584898u, out int currentAbility);   // PlayerCurrentAbility
p.TryGetValue(1347436866u, out int potentialAbility);  // PlayerPotentialAbility
```

Root object confirmed too: `GameReference.GetInstance()` (a static singleton).

So v0.7.0 scans person indices and reads IsPlayer / Age / CA / PA for each — the
first real values out of the save. This is exactly how external tools read the
DB, but from inside the running game.

---

## ⚠️ v0.7.1 / v0.8.0 result — `TryGetValue` is a cache, not a DB fetch

Reading via `InteropReference.TryGetValue` does **not** work for attribute data:

- `new PersonReference(index)` constructs fine (`ctorOk` for all indices) and
  `AcceptsProperty(PlayerCA) = True` (the schema accepts CA)…
- …but `TryGetValue(uint, out int)` returns **false for every property**, even on
  `PersonReference.GetInstance()` (a real reference, id `1145176065`).

The v0.8.0 base-chain dump explains why:

```
FM.UI.DatabaseRecordReference : SI.Interop.InteropReference
    .ctor(DatabaseTableType type, Int32 index)   // identity = (table, packed index)
    Int64 get_Data1()                            // native-side handle
SI.Interop.InteropReference
    bool TryGetValue(uint id, out int value)     // reads a LOCAL value cache
    void SetValue(uint id, int value)            // native pushes values in here
```

`TryGetValue`/`SetValue` are a **small client-side cache** that FM26's binding
system fills only for the values a screen is currently showing. The real
attribute database lives in the native `game_plugin` (C++), and the managed UI
reads it **lazily through the binding subsystem** (`SI.Bindable.Bindings`) via
data handlers that fetch from native and `Set` the result into the binding tree.

**Consequence:** there is no simple managed "read person X's CA" call. Bulk DB
reads (Top-10-by-CA across everyone) require either driving the binding subsystem
/ native fetch, or reading the save file / process memory the way Genie Scout
does. Next: hunt for a live handle to the `Bindings` store (it already holds
fetched values) and any method returning a typed value for a reference+property.

---

## ⛔ v0.9.x conclusion — the managed layer can't do a bulk DB read

We reached the live store: `FM.UI.EmbeddedDataHandler.s_bindingSubsystem` is the
running `SI.Bindable.BindingSubsystem` (non-null once a save is loaded). Its
`Bindings.DataSet` is an `IReadOnlyList<IReadOnlyData>` where each element is:

```
SI.Bindable.IReadOnlyData
    DataKey   Key     // SI.Bindable.Bindings+DataKey — an opaque HASH, no readable path
    TypedValue Value  // null for every slot we saw
```

Empirically the pool has ~4000 slots and every entry we enumerated has an opaque
hashed key and **`Value = null`**. So the managed binding tree is a low-level,
hash-keyed node pool that is mostly empty — not a table of player→attribute.

**Why this is a dead end for the original goal.** FM26 keeps the real attribute
database in the native `game_plugin` (C++). The managed UI reads values *lazily*:
when a screen displays a player, a data handler fetches from native and pushes a
value into the binding tree for just that on-screen data. Consequences:

- There is no managed call that returns "person X's CA" on demand.
- Even harvesting the binding tree only yields whatever the UI is *currently*
  drawing (a handful of players), with opaque hashed keys.
- "Top-N by CA across the whole database" requires every player's data, which the
  game never bulk-loads into managed memory.

**Therefore bulk scouting (Top players / wonderkids / staff across the DB) cannot
come from the managed/binding layer.** It requires reading the database directly:

1. **Save-file parsing** — how FM Genie Scout actually works: read the `.fm` save
   off disk and decode records. Gives full bulk data; independent of the running
   game; but is a large reverse-engineering effort on FM26's save format.
2. **Native process-memory reading** — read `game_plugin`'s in-memory DB structs.
   Also large RE, and fragile across patches.
3. **Scope down to on-screen enhancement** — an in-game overlay that reads/annotates
   the player the UI is *already* showing (via the binding tree). Achievable, but
   not the full-DB Top-N vision.

What we *have* firmly delivered: BepInEx injection + in-game IMGUI overlay, and a
complete, reproducible map of FM26's property schema (see property-ids.md). Those
are solid foundations regardless of which data-read path we choose next.

---

## ✅ v0.10–0.11 breakthrough — the tree is readable after all

The v0.9.x "dead end" was the wrong entry point, not a wall. Going through
`Bindings.m_nodes` (a `Dictionary<ulong, Bindings+Node>`) instead of `DataSet`,
and decoding each hash with `Bindings.GetPathDebug(new Key(hash))`, the whole
live binding tree comes out with **readable paths**. The v0.11.0 spy captured
9,500+ nodes across portal → squad → player profile → search:

- **Per-player squad rows**: `…playertable3.Items.N.binding.Age` (PropID
  825565216), plus `CurrentAbilityStars`, `CurrentAbilityStarRange` /
  `PotentialAbilityStarRange` (and `Coach…` variants for staff).
- **Player profile**: a full `PlayerAttributesBlock` with `AttributeValues`
  nodes and per-attribute `PropertyValue` (1886680684) — the actual 1–20
  attribute numbers flow through here.
- **Scouting**: `ScoutedCurrentAbilityInfo` / `ScoutedPotentialAbilityInfo`
  with a `StarRange` child.
- **The bulk engine**: `game.Search` is a live `SearchReference` with
  `Results`, `PersonList`, `SearchIsFinished`, `Clubs/Nations/Competitions` —
  the game's own player-search query, addressable from code. `Team.FilteredPlayers`
  exists too. This is the road to Genie-Scout-style bulk lists.

New PropertyIDs observed live: CurrentAbilityStarRange=1131757922,
PotentialAbilityStarRange=1349468514, CoachCurrentAbilityStarRange=1128481106,
CoachPotentialAbilityStarRange=1129333074, AttributeValues=1112556614,
PropertyValue=1886680684, ScoutedCurrentAbilityInfo=2036486263,
ScoutedPotentialAbilityInfo=1399683185, StarRange=1464367445.

### The one bug: TypedValue payload extraction (fixed in v0.12)

Every node's `Value` is an `SI.Core.TypedValue`, but v0.11 printed only
`get_IsAlive=True | get_IsPooled=False`. Cause: `TypedValue` exposes only flag
**properties** non-generically (`IsAlive`, `IsPooled`, `IsNull`, `DataType`,
`IsValueType`) — the payload accessor must be a **generic `Get<T>()`-style
method**, which v0.11's heuristic explicitly skipped (`IsGenericMethod` filter).
`DataType` does report the real payload type (`[Int32]`, `[String]`,
`[Boolean]`, `[ClubReference]`…), so v0.12 binds each 1-type-arg generic method
to the CLR type matching `DataType` and invokes it (fallbacks: TryGet-style out
params, static conversion operators, `Il2CppSystem.Object`). The first accessor
that works per data type is cached and logged as `>>> extractor found:`.

### ✅ v0.12 result — `As<T>` is the payload accessor; one layer left

The v0.12 run confirmed **`TypedValue.As<T>()` unwraps every payload** (the
extractor summary listed ~75 data types, all via `As`). Strings and booleans
now print for real: attribute names ("Acceleration", "Reflexes"…), tactic
role names ("Ball-Playing Goalkeeper"), position strings ("M (C), AM (RLC)"),
transfer value ranges ("€142M - €166M"), wages, `SearchIsFinished=False`.

The remaining gap: **numeric payloads are wrapped** — `Age`, per-attribute
`PropertyValue` (the 1–20 numbers), star ranges all come back as
`DynamicNumber` / `DynamicReference` wrapper objects, and the bulk lists
(`game.Search.PersonList`/`Results`, `Team.FilteredPlayers`) as `List``1` —
and we only printed their useless `ToString`. v0.13 drills one level deeper:
`DataType` is a *managed* `System.Type`, so we bind `As<RealType>` to get the
correctly-typed wrapper, dump each wrapper type's API once, and invoke its
no-arg primitive getters (lists: print count + first items).

### ✅ v0.22 log analysis — why Top PA/CA stayed empty, and the join key (→ v0.23)

The v0.22 log (`rows=260, named=86, CA=19, PA=9`, both lists empty) proved the
capture pipeline works but the **names and star ratings land on different
rows**:

- **Names** come from streamed lists (`…2B.1.3.items0.N` — squad side list,
  medical centre, …): `name=Mile Svilar … ca=-1 pa=-1 idx=-1`. No stars, no index.
- **Star ranges + Age** come from the squad **playertable**
  (`…AA.…playertable3.Items.N.…`) and YouthSetup `StreamedTable3.Items.N` —
  which have **no Name node at all**.
- The old display filter demanded `Name != null && stars > 0` on the *same*
  row → intersection empty → "0 scouted" forever.

**The join key discovered in the same log:** each playertable row has
`Items.N.2.PlayerIndex` (propID **1230661448**) = a `DynamicNumber` holding the
person's **DB index** — the log showed `PlayerIndex … 40087` matching
`get_m_index=40087` inside `GameStateCache25.Manager.Club.MainTeam.Players`.
Player profile pages carry the same index as a `PersonReference` binding
(`…B3.…BindingVariables4.7.binding → get_m_index=39505` = Malo Gusto).

Also confirmed: profile **RoleTable** rows carry per-ROLE star ranges under
propID-0 `binding` nodes (harmless — we only capture star props by node propID).

**v0.23 therefore:**
1. captures `PlayerIndex` (1230661448) + `FirstName`/`SecondName` per row,
2. merges rows into a **person-level shadow DB keyed by DB index** at the end
   of each pass (end-of-pass, so recycled rows can't donate values to their
   previous occupant),
3. adds page pseudo-rows (32-hex screen roots) so an open player profile joins
   name+index+stars in one record,
4. Top PA/CA sort the person DB and show `player #<idx>` placeholders when the
   name hasn't been seen yet — the lists are never silently empty again.

### v0.23 result → v0.24 plan

v0.23's index join produced star+age+index records (the Top lists finally
render — as `player #<idx>`), but **joined=0**: no record ever got a name.
The log proved why: `FirstName`/`SecondName` nodes don't exist anywhere in
the live tree, the named side-lists (`…items0.N`) carry no person index under
any node we read, and star tables carry no Name-prop node.

v0.24 is the reconnaissance build to close that gap:
1. **Row X-ray** — on verbose passes, print the *complete* node list of one
   starred row and one named row (the 120-line cold cap had been hiding the
   name cell all along).
2. **Generic PersonReference capture** — any row node whose value is a
   top-level `[PersonReference]` supplies the row's index (was: only nodes
   literally named `binding`), with a source-path guard so a second
   person-reference in the same row can't fight the first, and re-read every
   pass via a remembered node set so recycled rows can't keep a stale index.
3. **Bindings API dump** — one-time reflection dump of every method on the
   `Bindings` class, to scope creating bindings ourselves (ask the game for
   `<person>.Name` by index — the clean endgame for names and for driving
   `game.Search`).

### ✅ v0.24 X-ray results — name cell FOUND, join bug found (→ v0.25)

The row X-ray answered everything at once:

- **Star tables DO carry a name cell**: `InitialSurname` (propID **1767075437**),
  a styled string — display text "D. Essugo", and the base64 tooltip markup
  inside decodes to *"Click to view Dário Cassia Luís Essugo's profile"* —
  i.e. the person's **full name** is embedded in every star-table row.
- **The join hijack**: rows also embed `Human` / `humanteam` PersonReferences —
  always the human MANAGER (idx 267375), used by the game for tooltip/context.
  v0.24's generic capture grabbed those first and the source-path guard then
  locked out the row's REAL person (in the row's own `.binding` node).
- The named side-lists (`…items0.N`) are dead ends: name + shirt number only,
  no person reference. They are no longer needed.
- `Bindings` class API dumped: low-level plumbing (Data/Node bookkeeping,
  HashPath, GetPathDebug) — no high-level "create binding by path" here; that
  lives elsewhere if we ever need it. Parked.

**v0.25:** capture `InitialSurname` (full name from tooltip, display text as
fallback); exclude Human-flavoured refs from identity; source priority — the
row's own `.binding` node and `PlayerIndex` prop are authoritative and may
correct a weak guess, same-node changes still reset the row (recycling).

### v0.25 result → v0.26

Identity fix confirmed working (starred rows now carry correct distinct
indexes: 93734, 45353, 145097 — no manager hijack). But `InitialSurname`
appeared ZERO times in the whole run: it is a **hover-tooltip node**, created
only while the mouse is over a name cell (v0.24 had exactly two — the two rows
the user happened to hover). Not a scalable name source.

**v0.26:** the visible name column must be a plain propID-0 string cell we
never captured. So: (1) every processed node now goes through Capture, and
untyped `[String]` cells of a row are examined — markup carrying the profile
tooltip ("Click to view …'s profile") yields a definitive full name; a plain
string passing a looks-like-a-name test (space + lowercase, no digits/parens)
is a weak fallback; (2) every string cell is remembered per row and printed in
the starred-row diagnostics (`cells=[…]`), so the next log shows exactly what
each column holds; (3) name strength tracked end-to-end (weak names never
overwrite tooltip/prop names).

### ✅ v0.26 X-ray — THE name bug found: marker chars are 0x01/0x02, not ☺☻

The squad playertable row X-ray showed the name node was there all along —
`Items.N.2.6.binding.Name` (propID 1851878757), styled string with display
"Jorrel Hato" and full name in the tooltip markup ("Click to view Jorrel
Evan Hato's profile") — captured every pass, then thrown away: styled-string
markers are CONTROL characters (0x01/0x02). Old consoles render those as ☺☻,
which is where the earlier "☺…☻" assumption came from; the literal
U+263A/U+263B never matched, display parsing failed, and the >60-char blob
was rejected. Search-result rows (playertable2 under PlayerSearch) have the
same shape — paging through in-game search feeds the DB the whole database.

**v0.27:** SplitStyled() treats any char < 0x20 (plus real ☺☻) as marker,
classifies a ≥24-char leading base64 segment as markup and the next segment
as display text; ParseDisplayString/TooltipName/NameFromStyled rebuilt on it
(guid-like strings now yield NO display text instead of junk names).

### 🎉 v0.27 CONFIRMED WORKING IN-GAME

User screenshot: "TOP CURRENT ABILITY — 57 scouted" with full names, ages and
CA/PA star ranges (Cole Jermaine Palmer 4.8*, Moisés Isaac Caicedo Corozo,
Estêvão 4.5*/4.5-5*, Jamal Musiala, Warren Zaïre-Emery 3.8-4.3*/4-5* …).
The 0x01/0x02 marker fix was the last missing piece. Stage 3's core loop —
browse → capture → rank — is live.

### v0.28 field report → v0.29 (persistence)

v0.28 in-game: join rate 100% again (18/18 via Player Search rows — Gyökeres,
Romero, Gakpo, with ages). "Filters unclickable" diagnosis: they click fine but
matched ZERO because search-table rows carry no position cell (`cells=[]`) and
the in-memory DB resets every restart, so a fresh session had no positions at
all. The real fix is persistence:

**v0.29:** the person DB is saved to `BepInEx/config/FM26ScoutMod.scoutdb.tsv`
whenever it changes and loaded at startup — scouted data now accumulates
across sessions (browse once, keep forever; values refresh when re-browsed).
Plus F10 (cycle position filter) / F11 (U21 toggle) keyboard fallbacks, and
the empty-list message now says how many scouted players lack position/age
and which screens fill them in.

### v0.29 field report + Genie Scout benchmark → v0.30

v0.29 in-game: 53/53 joined, positions captured, first persistence run (DB
file created). User benchmarked FM Genie Scout on the same save: Top-15 by
rating/potential across ALL 41,299 players, exact CA/PA numbers (0–200),
nation/club/wage/value columns, bargain finder. The gap to close is bulk
coverage + exact values; our edge is being live and in-game.

**v0.30:** (1) assembly scan — one-time reflection sweep over every FM./SI.
managed assembly for methods taking a `PersonReference` (flagging those that
also take a property identifier) — hunting the direct (person, property) read
primitive that would give whole-database coverage without the UI;
(2) weekly-wage capture (propID 1364088387, display + raw number map) as a
new persisted column in the Top lists; (3) panel widened for the wage column.
