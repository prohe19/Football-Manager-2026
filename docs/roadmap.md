# Roadmap

We build in **stages**, proving each layer works before moving on. If a stage fails, we find out cheaply and know exactly where the problem is — instead of debugging a giant mod all at once.

Each stage is a commit (or set of commits) in this repo.

---

## Stage 0 — Injection works ⬅️ **we are here**

**Goal:** Prove BepInEx can load our code inside FM26 on your machine.

- [x] Install BepInEx 6 (IL2CPP) into the FM26 folder — see [setup-bepinex.md](setup-bepinex.md) — **BepInEx 6.0.0-be.738 confirmed**
- [x] Launch FM26, confirm the **BepInEx console window** appears — **confirmed; interop assemblies generated (Unity 6000.0.52f1, .NET 6.0.7)**
- [ ] Build our Stage 0 plugin (`src/FM26ScoutMod`) and drop it in `BepInEx/plugins`
- [ ] See our log line in the console: `== FM26 Scout Mod: Stage 0 injection successful ==`

✅ **Done when:** our plugin's message prints in the BepInEx console.

> Confirmed runtime details captured in [environment.md](environment.md).

---

## Stage 1 — UI inside the game

**Goal:** Draw *something* on screen from our mod.

- [ ] Render a small floating **button** over the game
- [ ] Clicking it **expands** an (empty) panel
- [ ] Panel can be opened/closed without crashing the game

✅ **Done when:** you can toggle our panel in-game.

---

## Stage 2 — Touch real game data (the risky one)

**Goal:** Read actual data out of the game's own objects.

- [ ] Dump FM26's assemblies to find the class/field names — see [reverse-engineering.md](reverse-engineering.md)
- [ ] Locate the "player" object and a list of all players
- [ ] Print **one real player's name** to the console

✅ **Done when:** our mod prints a real player's name from your save.

> This is the highest-risk stage. If FM26's data is reachable here, everything after is "just" building features on top.

---

## Stage 3 — First real feature: Top Players by CA

**Goal:** The headline feature.

- [ ] Find the **Current Ability (CA)** field on a player
- [ ] Rank all players by CA
- [ ] Show **Top 10** in the panel, with a **position filter**

✅ **Done when:** the panel shows a correct Top-10-by-CA list.

---

## Stage 4 — Wonderkids + Staff

**Goal:** Round out the scouting suite.

- [ ] Find **Potential Ability (PA)** + player **age**
- [ ] **Top wonderkids** = young + high PA, ranked, filterable by position
- [ ] Find **staff** objects + their role/rating
- [ ] **Top staff** by role (scout, coach, GK coach, physio, …)

✅ **Done when:** all three lists (players / wonderkids / staff) work.

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
