# Roadmap

We build in **stages**, proving each layer works before moving on. If a stage fails, we find out cheaply and know exactly where the problem is — instead of debugging a giant mod all at once.

Each stage is a commit (or set of commits) in this repo.

---

## Stage 0 — Injection works ✅ **COMPLETE**

**Goal:** Prove BepInEx can load our code inside FM26 on your machine.

- [x] Install BepInEx 6 (IL2CPP) into the FM26 folder — see [setup-bepinex.md](setup-bepinex.md) — **BepInEx 6.0.0-be.738 confirmed**
- [x] Launch FM26, confirm the **BepInEx console window** appears — **confirmed; interop assemblies generated (Unity 6000.0.52f1, .NET 6.0.7)**
- [x] Build our Stage 0 plugin (`src/FM26ScoutMod`) and drop it in `BepInEx/plugins` — **built with .NET SDK 10 → net6.0**
- [x] See our log line in the console: `== FM26 Scout Mod: Stage 0 injection successful ==` — **✅ confirmed printing in-game**

✅ **DONE** — our code runs inside FM26. Confirmed runtime details in [environment.md](environment.md).

---

## Stage 1 — UI inside the game ✅ **COMPLETE**

**Goal:** Draw *something* on screen from our mod.

- [x] Render a small floating **button** over the game — **"Scout" button, top-left**
- [x] Clicking it **expands** an (empty) panel — **confirmed in-game (main menu)**
- [x] Panel can be opened/closed without crashing the game — **IMGUI overlay via injected MonoBehaviour**

✅ **DONE** — our own UI renders inside FM26 (game v26.3.2). Toggle works.

---

## Stage 2 — Touch real game data (the risky one) ⬅️ **we are here**

**Goal:** Read actual data out of the game's own objects.

- [x] Dump FM26's assemblies to find the data model — **done via ILSpy**
- [x] **Understand how data is stored** — property-binding system, see [findings-data-model.md](findings-data-model.md)
- [x] Runtime-dump the person **property schema** (find the CA/PA property IDs by name) — **✅ all 8,233 properties extracted; CA/PA IDs found, see [property-ids.md](property-ids.md)**
- [x] Reach the live data store — **✅ `EmbeddedDataHandler.s_bindingSubsystem` → the binding tree, decoded to readable paths via `GetPathDebug` (v0.10–0.11); player Age/star-range/attribute nodes and the game's own `game.Search` engine located, see [binding-api-probe.md](binding-api-probe.md)**
- [ ] Read one person's value for a property — **v0.12 in test: extract the payload out of `SI.Core.TypedValue` via its generic getter**
- [ ] Locate the "all persons" query and print **one real player's name** — **candidate found: `game.Search` (Results / PersonList / SearchIsFinished)**

✅ **Done when:** our mod prints a real player's name (and ideally their CA) from your save.

### Agreed data strategy (2026-08-24)

**Option A first — the game's own data layer.** Drive the binding tree +
`game.Search` for live, always-current data. This is the current path (v0.12).

**Option B after — save-file parsing (the Genie Scout way), embedded in the
same in-game overlay.** The mod can read the `.fm` save straight off disk and
show results in the same panel. Bigger reverse-engineering effort; fallback /
complement if Option A hits a wall.

Either way the UI is the same: one in-game button/hotkey → a large overlay
(~1/3 screen or resizable) over the running game — no alt-tabbing, no second app.

> **Milestone (build 26.3.2):** the registry dumper worked. We now have the exact PropertyIDs —
> `PlayerCurrentAbility = 1346584898`, `PlayerPotentialAbility = 1347436866`
> (staff: `NonPlayerCurrentAbility = 862020186`, `NonPlayerPotentialAbility = 1647325216`),
> plus every attribute, reputation and identity field. Full map in [property-ids.md](property-ids.md).
> Remaining work in this stage is *reading a value* for one of these IDs on a real person.

> Key insight: FM26 has no `Player.CurrentAbility` field — data is `(ReferenceID, PropertyID) → value`,
> and property **names are discoverable at runtime** via `GetPropertyDescriptionInternal`. So we let
> the game tell us the CA/PA IDs instead of guessing. See [findings-data-model.md](findings-data-model.md).

---

## Stage 3 — First real feature: Top Players by CA

**Goal:** The headline feature.

- [ ] Find the **Current Ability (CA)** field on a player
- [ ] Rank all players by CA (via `game.Search` / Option A)
- [ ] Show **Top 10** in the panel, with a **position filter**

✅ **Done when:** the panel shows a correct Top-10-by-CA list.

---

## Stage 4 — Wonderkids + Staff (the "better than Genie Scout" bar)

**Goal:** Round out the scouting suite. These are the end-goal features that
make the in-game app beat Genie Scout.

- [ ] Find **Potential Ability (PA)** + player **age**
- [ ] **Top 10 young players (wonderkids)** = young + high PA, ranked, **filterable by position**
- [ ] Find **staff** objects + their role/rating (NonPlayerCA/PA already mapped)
- [ ] **Top 10 staff**, filterable **by role** (scout, coach, GK coach, physio, …)

✅ **Done when:** all three lists (players / wonderkids / staff) work with filters.

---

## Stage 5 — Polish

**Goal:** Make it feel like a real product.

- [ ] Clean, expandable feature menu (the "one button → many features" vision)
- [ ] Better filters (league, nationality, age range, PA−CA "room to grow")
- [ ] Nicer styling, sortable columns, search
- [ ] Packaging so it installs via a mod loader (e.g. FMMLoader26 / Thunderstore)

---

## Guiding principles

- **Small commits, one stage at a time.** No boiling the ocean.
- **You run and test; I write and adjust.** See [workflow.md](workflow.md).
- **Everything documented here on GitHub** so progress is always clear.
