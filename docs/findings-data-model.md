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

## Open questions (for step 2/3)

- Exact shape of `PropertyID` / `ReferenceID` (how to get/pass the raw uint).
- How to read a *typed value* for (ReferenceID, PropertyID) — the binding call.
- How to get the list of all person ReferenceIDs (the "all players" query).
