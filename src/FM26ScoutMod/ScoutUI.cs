using UnityEngine;
using FM.UI;
using SI.Bindable.Reference.Core;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (step 1) — dump FM26's property registry.
///
/// FM26 registers every bindable property in a global IdentifierSet:
///   PropertyIdentifierSet.Instance.m_idToInfo : Dictionary&lt;uint, PropertyIDInfo&gt;
/// This walks that dictionary and logs each property's id + human name (via
/// DbSummaryPersonReference.GetPropertyDescriptionInternal). Any name that looks
/// ability/reputation-related is flagged as a warning so it stands out.
///
/// Runs automatically (no clicking — FM's UI eats our clicks): it retries every
/// couple of seconds until the registry is populated, then dumps once.
/// See docs/findings-data-model.md.
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
        if (!_dumped && Time.unscaledTime >= _nextTry)
        {
            _nextTry = Time.unscaledTime + 2f;
            TryDumpRegistry();
        }

        if (GUI.Button(new Rect(12, 12, 150, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;
        if (!_open)
            return;

        GUI.Box(new Rect(12, 48, 400, 150), "FM26 Scout Mod  v" + Plugin.PluginVersion);
        GUI.Label(new Rect(24, 74, 380, 22), "Stage 2 - dumping property registry");
        string s = _dumped
            ? $"DONE - registry had {_lastCount} properties.\nSee BepInEx console / LogOutput.log."
            : $"Scanning for registry... tries={_tries}, lastCount={_lastCount}";
        GUI.Label(new Rect(24, 100, 380, 60), s);
        GUI.Label(new Rect(24, 168, 380, 22), "(no clicking needed - watch the console)");
    }

    private void TryDumpRegistry()
    {
        _tries++;
        try
        {
            PropertyIdentifierSet reg = PropertyIdentifierSet.Instance;
            if (reg == null)
            {
                Plugin.Logger.LogInfo($"[FM26 Scout Mod] scan #{_tries}: registry Instance is null (not ready yet)");
                return;
            }

            var dict = reg.m_idToInfo;
            if (dict == null)
            {
                Plugin.Logger.LogInfo($"[FM26 Scout Mod] scan #{_tries}: m_idToInfo is null");
                return;
            }

            int total = dict.Count;
            _lastCount = total;
            Plugin.Logger.LogInfo($"[FM26 Scout Mod] scan #{_tries}: registry has {total} properties");
            if (total <= 0)
                return;

            Plugin.Logger.LogInfo($"===== FM26 Scout Mod: property registry dump ({total}) =====");
            // Read the real NAME + DisplayName off each PropertyIDInfo (inherited from
            // IdentifierInfo). Log every entry, and flag ability/potential/reputation.
            int n = 0;
            foreach (uint id in dict.Keys)
            {
                string name = "", disp = "";
                try
                {
                    var info = dict[id];
                    if (info != null)
                    {
                        name = info.Name ?? "";
                        disp = info.DisplayName ?? "";
                    }
                }
                catch (System.Exception e) { name = "<err:" + e.Message + ">"; }

                Plugin.Logger.LogInfo($"  {id} = \"{name}\" | \"{disp}\"");

                string blob = (name + " " + disp).ToLowerInvariant();
                if (blob.Contains("abil") || blob.Contains("potential") || blob.Contains("reputation"))
                    Plugin.Logger.LogWarning($"  *** MATCH: {id} = \"{name}\" | \"{disp}\" ***");
                n++;
            }
            Plugin.Logger.LogInfo($"===== FM26 Scout Mod: done, dumped {n} properties =====");
            _dumped = true;
        }
        catch (System.Exception ex)
        {
            Plugin.Logger.LogError($"[FM26 Scout Mod] scan #{_tries} error: {ex}");
        }
    }
}
