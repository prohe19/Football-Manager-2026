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
