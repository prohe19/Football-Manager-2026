using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using FM.UI;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (final) — read REAL player values from the database.
///
/// The v0.6.0 navigation dump revealed the shortcut:
///   FM.UI.PersonReference : DatabaseRecordReference : ... : SI.Interop.InteropReference
///   PersonReference has a  .ctor(Int32 index)  — build person #index directly,
///   and inherits  bool TryGetValue(uint propertyId, out int value)  from
///   InteropReference. So we can read the DB straight by index, no UI needed.
///
/// This build scans person indices 0..N, and for each reads (via reflection, so
/// we don't depend on the exact out/ref signature Il2CppInterop generated):
///   IsPlayer (862938733), Age (825565216),
///   PlayerCurrentAbility (1346584898), PlayerPotentialAbility (1347436866),
///   NonPlayerCurrentAbility (862020186).
/// It logs the first valid people plus the best CA/PA found — the proof that we
/// can read live abilities. See docs/property-ids.md and docs/binding-api-probe.md.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    // Property IDs (from docs/property-ids.md).
    private const uint PID_IsPlayer      = 862938733;
    private const uint PID_Age           = 825565216;
    private const uint PID_PlayerCA      = 1346584898;
    private const uint PID_PlayerPA      = 1347436866;
    private const uint PID_NonPlayerCA   = 862020186;

    private const int ScanCount = 3000;   // how many person indices to try this pass

    private bool _open = true;
    private bool _ran;
    private float _nextTry;
    private int _tries;

    // results for the on-screen panel
    private int _scanned, _valid, _players, _bestCA = -1, _bestCAidx = -1, _bestPA = -1, _bestPAidx = -1;

    private void OnGUI()
    {
        if (!_ran && Time.unscaledTime >= _nextTry)
        {
            _nextTry = Time.unscaledTime + 2f;
            TryReadPlayers();
        }

        if (GUI.Button(new Rect(12, 12, 150, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;
        if (!_open)
            return;

        GUI.Box(new Rect(12, 48, 400, 170), "FM26 Scout Mod  v" + Plugin.PluginVersion);
        GUI.Label(new Rect(24, 74, 380, 22), "Stage 2 - reading real player abilities");
        string s = _ran
            ? $"scanned {_scanned}, valid {_valid}, players {_players}\n" +
              $"best CA = {_bestCA} (person #{_bestCAidx})\n" +
              $"best PA = {_bestPA} (person #{_bestPAidx})"
            : $"reading... tries={_tries}";
        GUI.Label(new Rect(24, 100, 380, 80), s);
        GUI.Label(new Rect(24, 186, 380, 22), "(details in BepInEx console / LogOutput.log)");
    }

    private void L(string msg) => Plugin.Logger.LogInfo(msg);

    private void TryReadPlayers()
    {
        _tries++;
        try
        {
            MethodInfo tryGet = FindTryGetValue();
            if (tryGet == null)
            {
                L($"[FM26 Scout Mod] read #{_tries}: TryGetValue(uint, out int) not found yet, retrying");
                return;
            }

            L($"===== FM26 Scout Mod: FIRST VALUE READ (v{Plugin.PluginVersion}) =====");

            // Sanity: the root game reference should resolve once a save is live.
            try
            {
                var game = GameReference.GetInstance();
                L($"  GameReference.GetInstance() -> {(game == null ? "null" : "ok")}");
            }
            catch (Exception e) { L("  GameReference.GetInstance() err: " + e.Message); }

            int shown = 0;
            for (int i = 0; i < ScanCount; i++)
            {
                PersonReference p;
                try { p = new PersonReference(i); }
                catch { continue; }
                if (p == null) continue;

                int isP = ReadInt(tryGet, p, PID_IsPlayer, out bool okIsP);
                int age = ReadInt(tryGet, p, PID_Age, out bool okAge);
                int pca = ReadInt(tryGet, p, PID_PlayerCA, out bool okPCA);
                int ppa = ReadInt(tryGet, p, PID_PlayerPA, out bool okPPA);
                int nca = ReadInt(tryGet, p, PID_NonPlayerCA, out bool okNCA);

                if (!(okIsP || okAge || okPCA || okPPA || okNCA))
                    continue;   // nothing readable at this index

                _scanned++;
                if (okIsP && isP != 0) _players++;
                _valid++;
                if (okPCA && pca > _bestCA) { _bestCA = pca; _bestCAidx = i; }
                if (okPPA && ppa > _bestPA) { _bestPA = ppa; _bestPAidx = i; }

                if (shown < 30)
                {
                    shown++;
                    L($"  #{i} isPlayer={fmt(okIsP, isP)} age={fmt(okAge, age)} " +
                      $"PlayerCA={fmt(okPCA, pca)} PlayerPA={fmt(okPPA, ppa)} NonPlayerCA={fmt(okNCA, nca)}");
                }
            }

            L($"----- summary: scanned={_scanned} valid={_valid} players={_players} " +
              $"bestPlayerCA={_bestCA} @#{_bestCAidx}  bestPlayerPA={_bestPA} @#{_bestPAidx} -----");
            L("===== FM26 Scout Mod: value read done =====");
            _ran = true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[FM26 Scout Mod] read #{_tries} error: {ex}");
        }
    }

    private static string fmt(bool ok, int v) => ok ? v.ToString() : "-";

    // TryGetValue is inherited from SI.Interop.InteropReference; GetMethods (no
    // DeclaredOnly) includes it. We invoke via reflection so we don't depend on
    // whether Il2CppInterop generated the byref param as `out` or `ref`.
    private static MethodInfo FindTryGetValue()
    {
        try
        {
            foreach (var m in typeof(PersonReference).GetMethods())
            {
                if (m.Name != "TryGetValue") continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(uint))
                    return m;
            }
        }
        catch { }
        return null;
    }

    private static int ReadInt(MethodInfo m, object inst, uint id, out bool ok)
    {
        ok = false;
        try
        {
            object[] args = { id, 0 };
            object r = m.Invoke(inst, args);
            ok = r is bool b && b;
            return args[1] is int v ? v : 0;
        }
        catch { ok = false; return 0; }
    }
}
