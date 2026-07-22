# Reverse-engineering FM26 (finding CA / PA / staff)

> Needed for **Stage 2+**. You don't need this for Stage 0/1. When we get here, we'll
> do it together — this page is the map.

To read a player's Current Ability, our code needs to know FM26's **internal names**:
which class represents a player, which field holds CA, how to get the list of all
players, etc. IL2CPP strips a lot of this, but it can be recovered by **dumping the
game's assemblies**.

## The tools (run on your PC, on the FM26 install)

- **[Il2CppDumper](https://github.com/Perfare/Il2CppDumper)** — reads `GameAssembly.dll` +
  `global-metadata.dat` and produces a `dump.cs` listing every class, field, and method.
- **[Il2CppInterop](https://github.com/BepInEx/Il2CppInterop)** — already used by BepInEx 6;
  generates the C# interop assemblies our plugin references so we can call the game's code
  by name. BepInEx typically drops these under
  `BepInEx/interop/` after the first run.

## The process (high level)

1. Locate in the game folder:
   - `GameAssembly.dll`
   - `FootballManager2026_Data/il2cpp_data/Metadata/global-metadata.dat`
2. Run **Il2CppDumper** against them → get `dump.cs`.
3. **Search `dump.cs`** for likely names. Good search terms:
   - `Ability`, `CurrentAbility`, `PotentialAbility`, `CA`, `PA`
   - `Player`, `Person`, `Footballer`
   - `Staff`, `Coach`, `Scout`, `JobTitle`, `Role`
4. Paste the promising class/field definitions **into a GitHub issue or here in chat**,
   and I'll turn them into working interop code for the plugin.

## What to share with me

Because I can't run the game, the dump is my window into it. Most useful:
- The **class definition** that looks like a player (fields + types).
- Any field names containing `Ability`, `Potential`, `Current`.
- How the game exposes a **collection of all players/staff** (a manager/database/singleton
  class with a list or lookup).

Even partial/uncertain matches help — send them and we'll narrow it down.

## Notes & caveats

- `dump.cs` can be huge. Don't paste the whole thing — search and share the relevant chunks.
- Names may be **obfuscated** (e.g. `Class1234`). If so, we fall back to matching by structure
  (field types/order) and behavior. Harder, but doable.
- Re-dump after major game patches if names shift.
- This is standard Unity-mod reverse-engineering for a **single-player** game you own —
  the same technique the public FM26 BepInEx mods use.
