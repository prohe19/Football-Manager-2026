# Findings — FM26's data model (Stage 2)

Discovered by decompiling `FM.UI.DbSummaryPersonReference` (in `FM.UI.dll`) with ILSpy.

## The big picture

FM26 does **not** expose data as plain named fields (there is no `Player.CurrentAbility`).
Instead it uses a **property-binding system**:

- A **person/player is a Reference** — e.g. `DbSummaryPersonReference : InteropReference`
  (base types live in `SI.Interop` / `SI.Bindable.Reference.Core`).
- Each entity is identified by a **`ReferenceID`**.
- Every piece of data about it (name, age, **CA**, **PA**, each attribute) is a
  **`PropertyID`** — basically a numeric key.
- You read data by property: "give me property #N for this reference."

This is why searches for `CurrentAbility` / `PotentialAbility` / `Finishing` found nothing —
those aren't field names, they're *property descriptions* looked up by ID at runtime.

## Key members on `DbSummaryPersonReference`

All of these are the doorway into the data:

| Member | Signature | Use |
|---|---|---|
| `GetPropertyCountInternal()` | `static int` | how many properties exist |
| `AcceptsProperty(uint)` / `AcceptsPropertyInternal(uint)` | `bool` | does this reference have property N |
| `GetProperties(List<PropertyID>)` | `void` (fills list) | enumerate the valid property IDs |
| **`GetPropertyDescriptionInternal(uint)`** | **`static string`** | **human-readable NAME of a property** ⭐ |
| `GetPropertyTypeInternal(uint)` | `static BindingKind` | the data type of a property |
| `GetContexts(uint, List<ContextID>)` | `void` | context/grouping info |
| `GetInstance()` | `static DbSummaryPersonReference` | the shared schema instance |

⭐ `GetPropertyDescriptionInternal` is the golden key: it lets us **discover CA/PA by
name at runtime** instead of guessing.

## What this means for our plan

We can stop guessing and let the game tell us. The path:

1. **Dump the property schema at runtime** — enumerate property IDs and log each one's
   `GetPropertyDescriptionInternal` name. Find which IDs are "Current Ability",
   "Potential Ability", etc. *(next step)*
2. **Read a person's value** for a given PropertyID — via the `SI.Bindable` binding
   system (needs a bit more study of how a `ReferenceID` + `PropertyID` → a value).
3. **Enumerate all persons/players** — find the query/collection that lists people so we
   can rank them.
4. Then: rank by CA (top players), by PA (wonderkids), staff by role.

## Relevant assemblies to keep loaded in ILSpy

- `FM.UI.dll` — `DbSummaryPersonReference` and the other `…Reference` types
- `SI.Interop.dll` — `InteropReference` base, `ReferenceID`, `PropertyID`
- `SI.Bindable.dll`, `SI.Bindable.Reference.Core.dll` — the binding/value system

## Empirical results (v0.3.2 auto-scan, in a loaded save)

Confirmed by calling the methods live from the mod:

- ✅ **The interop bridge works** — `DbSummaryPersonReference.GetPropertyCountInternal()`
  returned a real value (`3`) from inside the game. We can call FM26's data-layer code.
- ⚠️ **`DbSummaryPersonReference` is a *thin summary*** — only **3** properties. It is NOT
  where attributes / CA / PA live. We need a richer reference type.
- ⚠️ **Property IDs are hashed/large, not sequential** — `AcceptsPropertyInternal(0..4095)`
  was false for every id, so brute-forcing small numbers finds nothing. Must enumerate the
  real IDs via `GetProperties(List<PropertyID>)` instead.
- 🐛 A `NullReferenceException` on `Input[/Mouse/leftButton]` shows up (FM's input system vs
  our IMGUI). Non-fatal so far; revisit if it causes trouble.

## Next steps (revised)

1. **Enumerate properly** — call `GetProperties(List<PropertyID>)` on an instance to get the
   real PropertyIDs, then `GetPropertyDescriptionInternal(id)` for each. (Needs the
   `PropertyID` type — check its shape in ILSpy: field or conversion to `uint`.)
2. **Find the right reference type** — `DbSummaryPersonReference` is too thin. Look for a
   richer `Db…Reference` / player-attributes reference that exposes many properties
   (search `Db` + `Reference` in ILSpy, compare `GetPropertyCountInternal`).
3. Read a value for (ReferenceID, PropertyID) via the `SI.Bindable` system.
4. Enumerate all persons/players; then rank by CA / PA / staff role.

## Open questions (for step 2/3)

Mostly answered by the v0.5.0 binding-API probe — see [binding-api-probe.md](binding-api-probe.md).

- ✅ Exact shape of `PropertyID` / `ReferenceID` — both are `struct`s wrapping a
  single `uint id`, with `.ctor(uint)` and implicit `uint`⇄`PropertyID`/`ReferenceID`.
- ✅ How to read a *typed value* — `SI.Interop.InteropReference` (base of every
  `…Reference`) has **`bool TryGetValue(uint propertyId, out int value)`**.
- ⬜ Which reference type exposes CA/PA for a *specific person* (candidates:
  `PersonReference`, `IPlayerReference`, `PlayerAttributeReference`).
- ⬜ How to obtain a live person `ReferenceID` / a bound reference (the "all
  players" query) — the last missing link, via `ReferenceIdentifierSet` or the
  `SI.Bindable` binding subsystem.
