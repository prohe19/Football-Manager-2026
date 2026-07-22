# Workflow — who does what

This mod runs inside a Windows game that the AI assistant **cannot run or test**. So we
split the work clearly. Being honest about this up front keeps us fast.

## What I (the assistant) do

- Write the plugin C# code, the ranking/scouting logic, and the in-game UI.
- Write and maintain all docs in this repo.
- Interpret your dumps/logs/errors and turn them into working code.
- Structure the project, manage git, keep the roadmap current.

## What you do

- **Run and test** everything on your Windows PC with FM26 installed.
- Install BepInEx, build the plugin (`dotnet build`), drop the DLL in `BepInEx/plugins`.
- Run the reverse-engineering dumps when we reach Stage 2 (I'll guide each step).
- **Report back**: console logs, screenshots, crashes, and dump snippets.

## The loop

```
  I write code  ──►  you build + run in FM26  ──►  you report what happened
        ▲                                                     │
        └─────────────────  I adjust  ◄───────────────────────┘
```

**Your feedback is my only view into the game.** The more concrete (exact error text,
`LogOutput.log`, screenshots), the faster I can fix things.

## Ground rules

- **One stage at a time** (see [roadmap.md](roadmap.md)). Don't jump ahead.
- **Expect breakage** early — missing DLLs, wrong references, IL2CPP quirks. That's normal;
  paste the error and we iterate.
- **Everything on GitHub**: code, docs, and decisions live here so progress is always clear.
- **Back up saves** before testing new builds.
