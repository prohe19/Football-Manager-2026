using UnityEngine;
using FM.UI;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (step 1) — the property dumper, now CLICK-FREE.
///
/// FM26's own UI eats mouse clicks that land over it, so a button is unreliable.
/// Instead this auto-scans: every couple of seconds it asks the game for the person
/// property count, and as soon as that's available (> 0) it dumps every property's
/// name to the BepInEx console — no clicking required. The panel shows a live
/// tries/lastCount readout so we always get feedback. See docs/findings-data-model.md.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    // IL2CPP-injected MonoBehaviours must expose this IntPtr constructor.
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private bool _open = true;
    private bool _dumped;
    private float _nextTry;
    private int _tries;
    private int _lastCount = -1;

    private void OnGUI()
    {
        // Auto-scan — no clicking needed. Retry every 2s until the person schema is
        // available (count > 0), then dump once.
        if (!_dumped && Time.unscaledTime >= _nextTry)
        {
            _nextTry = Time.unscaledTime + 2f;
            TryAutoDump();
        }

        if (GUI.Button(new Rect(12, 12, 150, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;
        if (!_open)
            return;

        GUI.Box(new Rect(12, 48, 390, 150), "FM26 Scout Mod  v" + Plugin.PluginVersion);
        GUI.Label(new Rect(24, 74, 370, 22), "Stage 2 - auto-scanning for data");

        string s = _dumped
            ? $"DONE - dumped properties (count={_lastCount}).\nSee BepInEx console."
            : $"Auto-scanning... tries={_tries}, lastCount={_lastCount}";
        GUI.Label(new Rect(24, 100, 370, 60), s);
        GUI.Label(new Rect(24, 168, 370, 22), "(no clicking needed - watch the console)");
    }

    private void TryAutoDump()
    {
        _tries++;
        try
        {
            int count = DbSummaryPersonReference.GetPropertyCountInternal();
            _lastCount = count;
            Plugin.Logger.LogInfo($"[FM26 Scout Mod] auto-scan #{_tries}: GetPropertyCountInternal() = {count}");
            if (count <= 0)
                return; // schema not ready yet; try again in 2s

            DumpAll(count);
            _dumped = true;
        }
        catch (System.Exception ex)
        {
            Plugin.Logger.LogError($"[FM26 Scout Mod] auto-scan #{_tries} error: {ex.Message}");
        }
    }

    private void DumpAll(int count)
    {
        Plugin.Logger.LogInfo($"===== FM26 Scout Mod: person property dump (count={count}) =====");
        int found = 0;
        for (uint id = 0; id < 4096u; id++)
        {
            bool accepts;
            try { accepts = DbSummaryPersonReference.AcceptsPropertyInternal(id); }
            catch { continue; }
            if (!accepts)
                continue;

            string desc;
            try { desc = DbSummaryPersonReference.GetPropertyDescriptionInternal(id); }
            catch { desc = "<err>"; }

            Plugin.Logger.LogInfo($"  Prop {id} = {desc}");
            found++;
        }
        Plugin.Logger.LogInfo($"===== FM26 Scout Mod: done, dumped {found} properties (count={count}) =====");
    }
}
