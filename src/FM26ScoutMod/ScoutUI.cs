using UnityEngine;

namespace FM26ScoutMod;

/// <summary>
/// Stage 1 — the in-game overlay.
///
/// A single "Scout" button that expands into a panel. Drawn with Unity's
/// immediate-mode GUI (OnGUI), which is the simplest way to render our own UI
/// from a BepInEx plugin without touching FM26's native interface yet.
///
/// This is deliberately bare — the point of Stage 1 is just to prove we can
/// draw a working, clickable panel on top of the game. Real scouting data
/// (top players by CA, wonderkids by PA, staff by role) arrives in later stages.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    // IL2CPP-injected MonoBehaviours must expose this IntPtr constructor.
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private bool _open;

    private void OnGUI()
    {
        // The always-visible toggle button, top-left.
        if (GUI.Button(new Rect(12, 12, 130, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;

        if (!_open)
            return;

        // The expandable panel.
        GUI.Box(new Rect(12, 48, 280, 182), "FM26 Scout Mod");
        GUI.Label(new Rect(24, 78, 260, 22), "Stage 1: the UI is alive!");
        GUI.Label(new Rect(24, 104, 260, 22), "Coming next:");
        GUI.Label(new Rect(24, 126, 260, 22), "-  Top players by CA");
        GUI.Label(new Rect(24, 148, 260, 22), "-  Top wonderkids by PA");
        GUI.Label(new Rect(24, 170, 260, 22), "-  Top staff by role");
        GUI.Label(new Rect(24, 200, 260, 22), "v" + Plugin.PluginVersion);
    }
}
