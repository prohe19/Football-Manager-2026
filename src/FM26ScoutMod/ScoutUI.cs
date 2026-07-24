using UnityEngine;
using FM.UI;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (step 1) — the property dumper.
///
/// FM26 stores person data as a property-binding system: each attribute (name, age,
/// CA, PA, ...) is a numeric PropertyID, and the game can tell us each property's
/// human-readable name via DbSummaryPersonReference.GetPropertyDescriptionInternal(id).
///
/// This panel adds a button that walks the property IDs and logs each one's name to
/// the BepInEx console, so we can find which IDs are "Current Ability" / "Potential
/// Ability" — instead of guessing. See docs/findings-data-model.md.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    // IL2CPP-injected MonoBehaviours must expose this IntPtr constructor.
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private bool _open;
    private string _status = "Load a save, then click Dump.";

    private void OnGUI()
    {
        if (GUI.Button(new Rect(12, 12, 130, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;

        if (!_open)
            return;

        GUI.Box(new Rect(12, 48, 340, 200), "FM26 Scout Mod");
        GUI.Label(new Rect(24, 74, 320, 22), "Stage 2 - reading FM26's data");
        GUI.Label(new Rect(24, 96, 320, 22), "Step 1: discover the property IDs");

        if (GUI.Button(new Rect(24, 122, 316, 28), "Dump person properties -> console"))
            DumpPersonProperties();

        GUI.Label(new Rect(24, 156, 320, 44), _status);
        GUI.Label(new Rect(24, 222, 320, 22), "v" + Plugin.PluginVersion);
    }

    /// <summary>
    /// Walk property IDs 0..N, and for each one the person schema accepts, log its
    /// description. This empirically reveals the CA/PA property IDs.
    /// </summary>
    private void DumpPersonProperties()
    {
        try
        {
            int count = DbSummaryPersonReference.GetPropertyCountInternal();
            Plugin.Logger.LogInfo($"===== FM26 Scout Mod: person property dump (reported count = {count}) =====");

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
                catch { desc = "<error reading description>"; }

                Plugin.Logger.LogInfo($"  Prop {id} = {desc}");
                found++;
            }

            Plugin.Logger.LogInfo($"===== FM26 Scout Mod: done, dumped {found} properties =====");
            _status = $"Dumped {found} props (count={count}).\nSee BepInEx console.";
        }
        catch (System.Exception ex)
        {
            _status = "Error - see console.";
            Plugin.Logger.LogError($"[FM26 Scout Mod] Property dump failed: {ex}");
        }
    }
}
