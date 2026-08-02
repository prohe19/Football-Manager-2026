using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using FM.UI;
using SI.Bindable.Reference.Core;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (step 2, "plan A") — probe FM26's binding API.
///
/// We already have the PropertyIDs (see docs/property-ids.md). What we DON'T yet
/// know is the exact call that turns (a person Reference + a PropertyID) into a
/// value. Rather than guess, this build reflects over the loaded FM26 / SI
/// assemblies and dumps the API surface:
///
///   1. Every *Reference / *Binding / *Property / *Person / *Player type name
///      (so we can spot a "rich" reference type that exposes CA/PA).
///   2. Full method + constructor signatures for the key types
///      (DbSummaryPersonReference, PropertyID, ReferenceID, PropertyIdentifierSet…).
///   3. "CANDIDATE READERS" — any method that takes a PropertyID/uint and returns
///      a primitive/string/binding. These are our value-read call candidates.
///
/// Runs automatically (no clicking — FM's UI eats our clicks): a couple of
/// seconds after load, dumps once to the BepInEx console / LogOutput.log.
/// See docs/findings-data-model.md and docs/binding-api-probe.md.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    // IL2CPP-injected MonoBehaviours must expose this IntPtr constructor.
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private bool _open = true;
    private bool _dumped;
    private float _nextTry;
    private int _tries;
    private int _lines;

    // Only dig into assemblies that are FM26's own game/binding code.
    private static readonly string[] AsmFilters = { "SI.", "FM.", "Bindable" };

    // Types we want a FULL method/ctor dump for (matched by simple Name).
    private static readonly HashSet<string> DeepDump = new(StringComparer.Ordinal)
    {
        "DbSummaryPersonReference", "PropertyID", "ReferenceID",
        "PropertyIdentifierSet", "PropertyIDInfo", "IdentifierInfo",
        "InteropReference", "BindingKind", "ContextID",
    };

    private void OnGUI()
    {
        if (!_dumped && Time.unscaledTime >= _nextTry)
        {
            _nextTry = Time.unscaledTime + 2f;
            TryProbe();
        }

        if (GUI.Button(new Rect(12, 12, 150, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;
        if (!_open)
            return;

        GUI.Box(new Rect(12, 48, 400, 150), "FM26 Scout Mod  v" + Plugin.PluginVersion);
        GUI.Label(new Rect(24, 74, 380, 22), "Stage 2 - probing the binding API");
        string s = _dumped
            ? $"DONE - dumped {_lines} lines.\nSee BepInEx console / LogOutput.log."
            : $"Probing... tries={_tries}";
        GUI.Label(new Rect(24, 100, 380, 40), s);
        GUI.Label(new Rect(24, 150, 380, 40), "(no clicking needed - watch the console,\n then send me LogOutput.log)");
    }

    private void L(string msg)
    {
        _lines++;
        Plugin.Logger.LogInfo(msg);
    }

    private void TryProbe()
    {
        _tries++;
        try
        {
            // Collect FM26's game/binding assemblies. Start from types we know are
            // loaded, then sweep the AppDomain for anything matching our filters.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var asms = new List<Assembly>();
            AddAsm(asms, seen, SafeAsm(() => typeof(PropertyIdentifierSet).Assembly));
            AddAsm(asms, seen, SafeAsm(() => typeof(DbSummaryPersonReference).Assembly));
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string nm = null;
                try { nm = a.GetName().Name; } catch { }
                if (nm == null) continue;
                foreach (var f in AsmFilters)
                    if (nm.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) { AddAsm(asms, seen, a); break; }
            }

            if (asms.Count == 0)
            {
                Plugin.Logger.LogInfo($"[FM26 Scout Mod] probe #{_tries}: no target assemblies yet, retrying");
                return;
            }

            L($"===== FM26 Scout Mod: BINDING API PROBE (v{Plugin.PluginVersion}) =====");
            L($"target assemblies ({asms.Count}):");
            foreach (var a in asms) L($"  ASM {a.GetName().Name}");

            // Gather all types once (robust against ReflectionTypeLoadException).
            var allTypes = new List<Type>();
            foreach (var a in asms)
                foreach (var t in SafeTypes(a))
                    if (t != null) allTypes.Add(t);
            L($"total types across targets: {allTypes.Count}");

            // ---- Section 1: interesting type NAMES (reference/binding/property/person/player) ----
            L("----- interesting types (name filter) -----");
            foreach (var t in allTypes)
            {
                string n = t.Name ?? "";
                if (Contains(n, "Reference") || Contains(n, "Binding") || Contains(n, "Bindable")
                    || Contains(n, "Property") || Contains(n, "Person") || Contains(n, "Player"))
                    L($"  TYPE {t.FullName}");
            }

            // ---- Section 2: deep method/ctor dump for the key types ----
            L("----- deep dump of key types -----");
            foreach (var t in allTypes)
            {
                if (t.Name != null && DeepDump.Contains(t.Name))
                    DumpType(t);
            }

            // ---- Section 3: CANDIDATE READERS — methods (…, PropertyID/uint, …) -> value ----
            L("----- CANDIDATE READERS: (PropertyID/uint) -> primitive/string/binding -----");
            foreach (var t in allTypes)
            {
                MethodInfo[] methods;
                try { methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); }
                catch { continue; }
                foreach (var m in methods)
                {
                    bool takesProp = false;
                    ParameterInfo[] ps;
                    try { ps = m.GetParameters(); } catch { continue; }
                    foreach (var p in ps)
                    {
                        string pn = p.ParameterType?.Name ?? "";
                        if (pn == "PropertyID" || pn == "UInt32") { takesProp = true; break; }
                    }
                    if (!takesProp) continue;

                    string rn = null;
                    try { rn = m.ReturnType?.Name; } catch { }
                    if (rn == null) continue;
                    bool valueish = m.ReturnType.IsPrimitive || rn == "String"
                        || Contains(rn, "Binding") || rn == "Single" || rn == "Int32"
                        || rn == "Int16" || rn == "Byte" || rn == "Boolean";
                    if (!valueish) continue;

                    L($"  READER {t.FullName}.{Sig(m)}");
                    if (_lines > 4000) { L("  ...(reader cap hit, stopping)"); goto done; }
                }
            }
        done:
            L($"===== FM26 Scout Mod: probe done, {_lines} lines =====");
            _dumped = true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[FM26 Scout Mod] probe #{_tries} error: {ex}");
        }
    }

    private void DumpType(Type t)
    {
        try
        {
            L($"  == {t.FullName}  (base: {t.BaseType?.FullName ?? "?"}) ==");
            ConstructorInfo[] ctors;
            try { ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly); }
            catch { ctors = Array.Empty<ConstructorInfo>(); }
            foreach (var c in ctors)
            {
                var ps = c.GetParameters();
                var sb = new StringBuilder("     .ctor(");
                for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Short(ps[i].ParameterType)).Append(' ').Append(ps[i].Name); }
                sb.Append(')');
                L(sb.ToString());
            }

            MethodInfo[] methods;
            try { methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); }
            catch { methods = Array.Empty<MethodInfo>(); }
            foreach (var m in methods)
                L($"     {Sig(m)}");

            // fields too (PropertyID / ReferenceID likely wrap a raw uint field)
            FieldInfo[] fields;
            try { fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); }
            catch { fields = Array.Empty<FieldInfo>(); }
            foreach (var f in fields)
                L($"     field {Short(f.FieldType)} {f.Name}");
        }
        catch (Exception e) { L($"  <deep-dump err {t?.FullName}: {e.Message}>"); }
    }

    private static string Sig(MethodInfo m)
    {
        var sb = new StringBuilder();
        if (m.IsStatic) sb.Append("static ");
        sb.Append(Short(SafeReturn(m))).Append(' ').Append(m.Name).Append('(');
        ParameterInfo[] ps;
        try { ps = m.GetParameters(); } catch { ps = Array.Empty<ParameterInfo>(); }
        for (int i = 0; i < ps.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(Short(ps[i].ParameterType)).Append(' ').Append(ps[i].Name);
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static Type SafeReturn(MethodInfo m) { try { return m.ReturnType; } catch { return null; } }
    private static string Short(Type t) { return t?.Name ?? "?"; }
    private static bool Contains(string s, string sub) => s != null && s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;

    private static Assembly SafeAsm(Func<Assembly> f) { try { return f(); } catch { return null; } }

    private static void AddAsm(List<Assembly> list, HashSet<string> seen, Assembly a)
    {
        if (a == null) return;
        string nm = null;
        try { nm = a.GetName().Name; } catch { }
        if (nm == null || !seen.Add(nm)) return;
        list.Add(a);
    }

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types ?? Array.Empty<Type>(); }
        catch { return Array.Empty<Type>(); }
    }
}
