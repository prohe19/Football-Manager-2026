# Stage 0 — Install BepInEx & verify the mod loads

Goal: get **BepInEx** (the mod loader) running inside FM26, then confirm our plugin loads. Do this once; every future stage builds on it.

> ⚠️ **Back up your saves first.** Modding can crash the game. Your saves live in
> `Documents\Sports Interactive\Football Manager 2026\`.

---

## 0. Find your FM26 install folder

In Steam: **right-click Football Manager 2026 → Manage → Browse local files.**

It's usually:
```
C:\Program Files (x86)\Steam\steamapps\common\Football Manager 2026\
```
This folder contains the game's `.exe` — call it the **game folder**. Everything below goes here.

---

## 1. Install the BepInEx pack for FM26

FM26 is **Unity 6 + IL2CPP**, so you need **BepInEx 6 (IL2CPP, x64)** — *not* the older BepInEx 5.

Two easy options:

**Option A — Thunderstore (recommended, preconfigured for FM26)**
- Get **BepInExPack FootballManager26**: <https://thunderstore.io/c/football-manager-26/p/BepInEx/BepInExPack_FootballManager26/>
- Or use a mod manager that installs it for you (FMMLoader26 / Vortex).

**Option B — Manual**
- Download the latest **BepInEx 6 IL2CPP (win-x64)** build.
- Extract so that `BepInEx/`, `dotnet/`, `winhttp.dll`, and `doorstop_config.ini` sit **next to the game `.exe`** in the game folder.

---

## 2. First launch (generates config)

- Launch FM26 **once through Steam**, let it reach the main menu, then quit.
- This makes BepInEx generate its folders. You should now have:
  ```
  <game folder>/BepInEx/plugins/     ← our mod goes here
  <game folder>/BepInEx/config/
  <game folder>/BepInEx/core/
  ```

---

## 3. Confirm BepInEx is actually loading

We want to *see* it working, not assume it.

1. Open `<game folder>/BepInEx/config/BepInEx.cfg`
2. Under `[Logging.Console]` set:
   ```ini
   [Logging.Console]
   Enabled = true
   ```
3. Launch FM26 again. A **BepInEx console window** should appear alongside the game, printing loader messages.

✅ **If the console appears → injection works. This is the whole point of Stage 0.**

❌ If it doesn't appear, tell me:
- Which BepInEx version/build you used (5 vs 6, IL2CPP vs Mono)
- What's in `<game folder>/BepInEx/LogOutput.log`
- Whether launching via Steam vs the `.exe` directly changes anything

---

## 4. Build & load our Stage 0 plugin

Our plugin lives in [`src/FM26ScoutMod`](../src/FM26ScoutMod). It does one thing: print a line proving *our* code ran.

**Requirements on your PC:** [.NET SDK 6.0+](https://dotnet.microsoft.com/download).

```bash
# from the repo root, on your PC
cd src/FM26ScoutMod
dotnet build -c Release
```

This produces `bin/Release/net6.0/FM26ScoutMod.dll`.

> If NuGet restore complains about the BepInEx packages, the cleanest path is to
> generate a fresh plugin from the **official BepInEx template** and paste our
> `Plugin.cs` logic in:
> ```bash
> dotnet new install BepInEx.Templates --nuget-source https://nuget.bepinex.dev/v3/index.json
> dotnet new bepinex6_unityil2cpp_plugin -n FM26ScoutMod
> ```
> Then copy the body of our `Plugin.cs` into the generated `Plugin.cs`. Send me any
> build errors and I'll fix the `.csproj` — I can't compile it here, so your build
> output is my feedback loop.

**Install it:** copy `FM26ScoutMod.dll` into `<game folder>/BepInEx/plugins/`.

---

## 5. The Stage 0 success check

Launch FM26. In the BepInEx console, look for:

```
[Info :   BepInEx] Loading [FM26 Scout Mod 0.1.0]
[Info :FM26 Scout Mod] Plugin FM26 Scout Mod v0.1.0 is loaded!
[Info :FM26 Scout Mod] == FM26 Scout Mod: Stage 0 injection successful ==
```

🎉 **See that? Stage 0 is done** — our code runs inside FM26. Send me a screenshot / the console text and we move to **Stage 1 (drawing UI in-game).**

If anything above breaks, copy the error here and we'll sort it out. Broken builds and missing DLLs are *expected* on the first try — that's what this stage is for.
