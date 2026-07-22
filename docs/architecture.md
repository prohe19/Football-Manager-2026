# Architecture

## The core idea

Instead of reading FM's memory from *outside* (Genie Scout / FMRTE style), we run **our own C# code inside the game process** using **BepInEx**, the Unity IL2CPP mod loader that FM26 supports.

```
                    ┌─────────────────────────────────────────┐
                    │        Football Manager 2026 (.exe)       │
                    │            Unity 6 · IL2CPP               │
                    │                                           │
   BepInEx  ───────►│   ┌───────────────────────────────────┐  │
   loads our        │   │        FM26 Scout Mod (our C#)     │  │
   plugin on        │   │                                   │  │
   launch           │   │  • reads game's player/staff      │  │
                    │   │    objects directly (IL2CPP interop)  │
                    │   │  • computes rankings (CA/PA/role) │  │
                    │   │  • draws an in-game overlay UI    │  │
                    │   └───────────────────────────────────┘  │
                    └─────────────────────────────────────────┘
```

Because we're **inside** the process:
- No admin rights / no "attaching" to another process.
- No brittle raw memory offsets — we reference game **types and fields by name**, which survives patches far better.
- We can render UI on the game's own screen.

## The three layers of the mod

1. **Loader layer (BepInEx)** — not our code. It injects us on launch. *(Stage 0)*
2. **Data layer** — finds the game's player/staff objects and reads the fields we need: CA, PA, age, position, staff role/rating. This is the part that requires reverse-engineering FM26's assemblies to learn the class/field names. *(Stages 2–4, see [reverse-engineering.md](reverse-engineering.md))*
3. **Presentation layer** — an in-game overlay: one button that expands into feature panels (Top Players / Wonderkids / Staff) with filters and sorting. *(Stages 1, 5)*

## UI approach

For the overlay we start with **IMGUI** (`OnGUI`) — the simplest way to draw a window + button from a BepInEx plugin without touching FM's own UI system. It's not pretty, but it's reliable and quick to iterate.

Later (Stage 5) we can consider integrating with FM26's native Unity UI for a more "built-in" look — but that's a nice-to-have, and much harder. IMGUI gets the features working first.

## What "CA" and "PA" are

- **CA (Current Ability)** — FM's hidden 1–200 score of how good a player is *now*.
- **PA (Potential Ability)** — hidden 1–200 ceiling of how good they can *become*.
- A **wonderkid** ≈ young player with high PA and a big **PA − CA gap** (lots of room to grow).

These are internal fields on the game's player objects. They're not shown in the normal UI, which is exactly why a mod that surfaces them is useful.

## Known risks

- **Per-patch maintenance.** Field/class names can change with updates; we may need to re-dump after big patches.
- **IL2CPP quirks.** Interop with an IL2CPP game can be fiddly (generics, collections, null handling). We'll handle these as we hit them.
- **CA/PA representation.** They might not be plain integer fields — could be computed or stored indirectly. Stage 2/3 will tell us.
- **We can't test from the dev side.** All runtime verification happens on the user's machine (see [workflow.md](workflow.md)).
