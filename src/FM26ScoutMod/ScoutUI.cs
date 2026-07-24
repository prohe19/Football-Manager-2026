using UnityEngine;
using FM.UI;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (step 1) — the property dumper, with loud diagnostics.
///
/// The panel button walks FM26's person property IDs and logs each one's name via
/// DbSummaryPersonReference.GetPropertyDescriptionInternal, so we can find the CA/PA
/// IDs. This version logs every step (click, method entry, the count call) so we can
/// see exactly where things stop if nothing appears. See docs/findings-data-model.md.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    // IL2CPP-injected MonoBehaviours must expose this IntPtr constructor.
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private bool _open = true;   // start open so the button is obvious
    private int _clicks;
    private string _status = "Ready. Click Dump.";

    private void OnGUI()
    {
        if (GUI.Button(new Rect(12, 12, 140, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;

        if (!_open)
            return;

        GUI.Box(new Rect(12, 48, 380, 210), "FM26 Scout Mod  v" + Plugin.PluginVersion);
        GUI.Label(new Rect(24, 74, 360, 22), "Stage 2 - reading FM26's data");

        if (GUI.Button(new Rect(24, 104, 356, 30), "Dump person properties -> console"))
        {
            _clicks++;
            _status = $"Clicked {_clicks}x - dumping...";
            Plugin.Logger.LogInfo($"[FM26 Scout Mod] >>> Dump button clicked (#{_clicks})");
            DumpPersonProperties();
        }

        GUI.Label(new Rect(24, 142, 360, 90), _status);
    }

    private void DumpPersonProperties()
    {
        Plugin.Logger.LogInfo("[FM26 Scout Mod] DumpPersonProperties() entered");
        try
        {
            Plugin.Logger.LogInfo("[FM26 Scout Mod] calling GetPropertyCountInternal()...");
            int count = DbSummaryPersonReference.GetPropertyCountInternal();
            Plugin.Logger.LogInfo($"[FM26 Scout Mod] GetPropertyCountInternal() = {count}");

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

            Plugin.Logger.LogInfo($"[FM26 Scout Mod] done, dumped {found} props (count={count})");
            _status = $"Clicked {_clicks}x.\nDumped {found} props (count={count}).\nSee console.";
        }
        catch (System.Exception ex)
        {
            _status = $"Clicked {_clicks}x.\nERROR: {ex.Message}\n(see console)";
            Plugin.Logger.LogError($"[FM26 Scout Mod] dump failed: {ex}");
        }
    }
}
