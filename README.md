# FM26 Scout Mod

An **in-game scouting mod for Football Manager 2026** — think *FM Genie Scout*, but living **inside the game** instead of a separate window.

You open FM26, click one button, and a panel expands with smarter-than-the-game scouting:

- 🏆 **Top players right now** — ranked by hidden **Current Ability (CA)**, filterable by position
- 🌟 **Top wonderkids right now** — ranked by hidden **Potential Ability (PA)**, by position
- 🧑‍🏫 **Top staff** — best scouts, coaches, GK coaches, physios, etc. by role

> **Status:** 🚧 Early development — **Stage 0** (getting the mod to load inside the game). See the [Roadmap](docs/roadmap.md).

---

## Why "inside the game"?

Tools like FM Genie Scout / FMRTE read FM's data from *outside* the process (peeking into memory), which needs admin rights, fights the game's anti-tamper, and breaks on every patch.

FM26 runs on **Unity 6 (IL2CPP)** and has a real modding path via **[BepInEx](https://thunderstore.io/c/football-manager-26/p/BepInEx/BepInExPack_FootballManager26/)** — a supported mod loader that runs **our own C# code *inside* the game process**. That means we can read the game's own player/staff objects directly and draw our own UI in-game. It's the more stable, more powerful path.

See [docs/architecture.md](docs/architecture.md) for the full picture.

---

## Target game version

Pinned to the exact build we're developing against:

| Field | Value |
|---|---|
| Platform | **Steam** |
| Steam App ID | **3551340** (Football Manager 2026) |
| Build ID | **23583635** |
| Content updated | **Jun 11, 2026** |

Mods are version-sensitive. If your build differs, some steps (especially reverse-engineering names/offsets) may need re-checking.

---

## Repository layout

```
.
├── README.md                  ← you are here
├── docs/
│   ├── roadmap.md             ← the staged build plan (start here)
│   ├── setup-bepinex.md       ← Stage 0: install BepInEx + verify it loads
│   ├── architecture.md        ← how the mod works technically
│   ├── reverse-engineering.md ← how we'll find CA/PA/staff in the game (Stage 2)
│   └── workflow.md            ← who does what (I write, you run/test)
└── src/
    └── FM26ScoutMod/          ← the BepInEx plugin (C#)
        ├── FM26ScoutMod.csproj
        └── Plugin.cs          ← Stage 0 "hello world" — proves injection works
```

---

## Getting started

1. Read the [Roadmap](docs/roadmap.md) so the plan is clear.
2. Do **[Stage 0 setup](docs/setup-bepinex.md)** — install BepInEx and confirm the mod loads.
3. Report back what you see, and we climb to the next stage.

---

## Disclaimer

This is a **personal, single-player** scouting aid for a game you own, built with the community-standard BepInEx modding tools. It does not modify online/multiplayer play. Use at your own risk — modding can break with game updates, and you should keep backups of your saves.
