# Confirmed environment

Runtime facts verified from a real BepInEx load on the target machine (Stage 0).
These pin down what our plugin must target and where things live.

## Game

| Field | Value |
|---|---|
| Platform | Steam |
| Steam App ID | `3551340` |
| Build ID | `23583635` (content updated Jun 11, 2026) |
| In-game version | `26.3.2` |
| Game folder (this machine) | `D:\SteamLibrary\steamapps\common\Football Manager 26\` |
| Executable | `fm.exe` (BepInEx reports process name `fm`) |
| Data folder | `fm_Data\` |
| IL2CPP metadata | `fm_Data\il2cpp_data\Metadata\global-metadata.dat` |

> Note: the folder is `Football Manager 26` (space, no "20"), not `Football Manager 2026`.

## Engine / runtime

| Field | Value |
|---|---|
| Unity | `6000.0.52f1` (Unity 6) |
| Scripting backend | IL2CPP |
| IL2CPP metadata version | `31` |
| .NET runtime (BepInEx) | **.NET 6.0.7** → plugins target `net6.0` |

## BepInEx

| Field | Value |
|---|---|
| Version | `6.0.0-be.738` (IL2CPP, x64) |
| Console | Enabled and confirmed working |
| Interop assemblies | Generated OK → `...\Football Manager 26\BepInEx\interop\` |
| Interop gen stats | 486 fields + 17,178 methods restored (13 fields / 1,129 methods failed — normal) |
| Plugins folder | `...\Football Manager 26\BepInEx\plugins\` |

## Why the interop folder matters

`BepInEx\interop\` contains generated C# assemblies mirroring the game's own types.
This is what our plugin references to call game code, **and** what we'll browse to find
the Player / CA / PA / Staff types in Stage 2 (often easier than a separate Il2CppDumper
run, since these are already produced on every launch).
