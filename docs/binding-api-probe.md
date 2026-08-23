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
