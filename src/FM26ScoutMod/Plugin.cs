using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;

namespace FM26ScoutMod;

/// <summary>
/// FM26 Scout Mod — Stage 0.
///
/// This plugin does exactly one thing: prove that our code loads and runs
/// inside Football Manager 2026 via BepInEx. If you see the log lines from
/// <see cref="Load"/> in the BepInEx console, injection works and we can
/// move on to Stage 1 (drawing an in-game UI).
///
/// See docs/roadmap.md for the plan and docs/setup-bepinex.md for how to run this.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BasePlugin
{
    public const string PluginGuid = "com.prohe19.fm26scoutmod";
    public const string PluginName = "FM26 Scout Mod";
    public const string PluginVersion = "0.7.0";

    /// <summary>Shared logger so other files can log once we grow past Stage 0.</summary>
    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;

        Log.LogInfo($"Plugin {PluginName} v{PluginVersion} is loaded!");

        // Stage 1: register our UI MonoBehaviour with the IL2CPP runtime, then
        // attach it to BepInEx's persistent manager object so OnGUI runs every
        // frame and draws our in-game overlay.
        ClassInjector.RegisterTypeInIl2Cpp<ScoutUI>();
        AddComponent<ScoutUI>();

        Log.LogInfo("== FM26 Scout Mod: Stage 1 UI attached — look for the 'Scout' button in-game ==");
    }
}
