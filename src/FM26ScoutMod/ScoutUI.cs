using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using FM.UI;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (fetch hunt) — find how to FETCH a value, not just read the cache.
///
/// v0.7.1 proved: new PersonReference(index) constructs fine and
/// AcceptsProperty(PlayerCA)=True, but InteropReference.TryGetValue(uint,out int)
/// returns false for every property. So TryGetValue reads a local value cache the
/// binding system fills when the UI binds a reference — it is NOT a live DB fetch.
///
/// This build dumps the COMPLETE callable surface of a person reference (walking
/// the whole base chain: PersonReference -> DatabaseRecordReference ->
/// InteropReference) plus the interop factories and SI.Core.Record, so we can see
/// the real fetch/enumerate call. It also runs small live experiments (TryGetValue
/// vs TryGetProperty, and PersonReference.GetInstance()) and logs the results.
///
/// See docs/binding-api-probe.md.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private const uint PID_PlayerCA = 1346584898;

    private bool _open = true;
    private bool _ran;
    private float _nextTry;
    private int _tries;
    private int _lines;

    // Extra types (beyond the PersonReference chain) to dump declared members of.
    private static readonly string[] ExtraDumps =
    {
        "DatabaseRecordReferenceFactory", "FMInteropReferenceFactory",
        "IInteropReferenceFactory", "Record", "DynamicReference",
    };

    private void OnGUI()
    {
        if (!_ran && Time.unscaledTime >= _nextTry)
        {
            _nextTry = Time.unscaledTime + 2f;
            TryProbe();
        }

        if (GUI.Button(new Rect(12, 12, 150, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;
        if (!_open)
            return;

        GUI.Box(new Rect(12, 48, 400, 150), "FM26 Scout Mod  v" + Plugin.PluginVersion);
        GUI.Label(new Rect(24, 74, 380, 22), "Stage 2 - hunting the value-fetch call");
        GUI.Label(new Rect(24, 100, 380, 40), _ran ? $"DONE - dumped {_lines} lines.\nSee LogOutput.log." : $"probing... tries={_tries}");
    }

    private void L(string msg) { _lines++; Plugin.Logger.LogInfo(msg); }

    private void TryProbe()
    {
        _tries++;
        try
        {
            L($"===== FM26 Scout Mod: FETCH HUNT (v{Plugin.PluginVersion}) =====");

            // 1) Full callable surface of a person reference, base chain included.
            L("----- PersonReference inheritance chain (declared members per level) -----");
            DumpChain(typeof(PersonReference));

            // 2) A few related types (factories / Record) that might expose the fetch.
            L("----- related types -----");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in typeof(PersonReference).Assembly.GetTypes())
            {
                if (t?.Name != null && Array.IndexOf(ExtraDumps, t.Name) >= 0 && seen.Add(t.FullName))
                    DumpType(t);
            }
            foreach (var t in typeof(SI.Interop.InteropReference).Assembly.GetTypes())
            {
                if (t?.Name != null && Array.IndexOf(ExtraDumps, t.Name) >= 0 && seen.Add(t.FullName))
                    DumpType(t);
            }

            // 3) Live experiments.
            L("----- live experiments -----");
            Experiment();

            L($"===== FM26 Scout Mod: fetch hunt done, {_lines} lines =====");
            _ran = true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[FM26 Scout Mod] probe #{_tries} error: {ex}");
        }
    }

    private void Experiment()
    {
        MethodInfo tryGetValue = FindMethod2("TryGetValue");
        MethodInfo tryGetProp = FindMethod2("TryGetProperty");

        // (a) freshly-built person #0
        try
        {
            var p = new PersonReference(0);
            L($"  new PersonReference(0): TryGetValue(CA)={Invoke2(tryGetValue, p)}  TryGetProperty(CA)={Invoke2(tryGetProp, p)}");
        }
        catch (Exception e) { L("  new PersonReference(0) err: " + e.Message); }

        // (b) the "current" instance the UI may have bound
        try
        {
            var inst = PersonReference.GetInstance();
            if (inst == null) { L("  PersonReference.GetInstance() -> null"); }
            else L($"  PersonReference.GetInstance(): id={SafeId(inst)}  TryGetValue(CA)={Invoke2(tryGetValue, inst)}  TryGetProperty(CA)={Invoke2(tryGetProp, inst)}");
        }
        catch (Exception e) { L("  PersonReference.GetInstance() err: " + e.Message); }
    }

    private static string SafeId(object refObj)
    {
        try
        {
            var idProp = refObj.GetType().GetProperty("ID");
            if (idProp == null) return "?";
            var id = idProp.GetValue(refObj);
            var inner = id?.GetType().GetProperty("ID");
            return inner?.GetValue(id)?.ToString() ?? id?.ToString() ?? "?";
        }
        catch { return "?"; }
    }

    private static string Invoke2(MethodInfo m, object inst)
    {
        if (m == null) return "<no method>";
        try
        {
            object[] args = { PID_PlayerCA, 0 };
            object r = m.Invoke(inst, args);
            bool ok = r is bool b && b;
            return $"{ok} (val={args[1]})";
        }
        catch (Exception e) { return "err:" + e.Message; }
    }

    private static MethodInfo FindMethod2(string name)
    {
        try
        {
            foreach (var m in typeof(PersonReference).GetMethods())
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(uint)) return m;
            }
        }
        catch { }
        return null;
    }

    // Walk the base chain, dumping declared members at each level until we hit
    // Object / the Il2CppInterop base (which only carry plumbing noise).
    private void DumpChain(Type t)
    {
        while (t != null)
        {
            string bn = t.BaseType?.Name ?? "";
            if (t.Name == "Object" || t.Name == "Il2CppObjectBase") break;
            DumpType(t);
            if (bn == "Object" || bn == "Il2CppObjectBase" || bn == "") break;
            t = t.BaseType;
        }
    }

    private void DumpType(Type t)
    {
        try
        {
            L($"  == {t.FullName}  (base: {t.BaseType?.Name ?? "?"}) ==");
            const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var c in Safe(() => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
            {
                var ps = c.GetParameters();
                var sb = new StringBuilder("     .ctor(");
                for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Short(ps[i].ParameterType)).Append(' ').Append(ps[i].Name); }
                L(sb.Append(')').ToString());
            }
            foreach (var m in Safe(() => t.GetMethods(F)))
            {
                if (m.Name.StartsWith("get__", StringComparison.Ordinal) || m.Name.StartsWith("set__", StringComparison.Ordinal)) continue;
                if (m.Name.StartsWith("get_m_", StringComparison.Ordinal) || m.Name.StartsWith("set_m_", StringComparison.Ordinal)) continue;
                L("     " + Sig(m));
            }
            foreach (var f in Safe(() => t.GetFields(F)))
                L($"     field {Short(f.FieldType)} {f.Name}");
        }
        catch (Exception e) { L($"  <dump err {t?.FullName}: {e.Message}>"); }
    }

    private static T[] Safe<T>(Func<T[]> f) { try { return f() ?? Array.Empty<T>(); } catch { return Array.Empty<T>(); } }

    private static string Sig(MethodInfo m)
    {
        var sb = new StringBuilder();
        if (m.IsStatic) sb.Append("static ");
        Type rt = null; try { rt = m.ReturnType; } catch { }
        sb.Append(Short(rt)).Append(' ').Append(m.Name).Append('(');
        var ps = Safe(() => m.GetParameters());
        for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Short(ps[i].ParameterType)).Append(' ').Append(ps[i].Name); }
        return sb.Append(')').ToString();
    }

    private static string Short(Type t) => t?.Name ?? "?";
}
