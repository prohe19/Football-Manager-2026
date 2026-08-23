using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using FM.UI;
using SI.Bindable.Reference.Core;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (step 3) — find the entry point from "loaded save" to "a player".
///
/// The v0.5.0 probe answered how to READ a value:
///   SI.Interop.InteropReference.TryGetValue(uint propertyId, out int value)
/// and that PropertyID/ReferenceID are thin uint wrappers. See
/// docs/binding-api-probe.md.
///
/// The one missing link is holding a live reference bound to a real person.
/// This build deep-dumps (full ctor + method + field signatures) the
/// navigation chain and the binding plumbing so we can see how the game hands
/// out a person/player reference:
///
///   Game → Club/Team → Squad → Person/Player references,
///   the reference registry (ReferenceIdentifierSet / ReferenceIDInfo),
///   and the SI.Bindable value helpers (Bindings / BindingSubsystem /
///   VisualFunctionLibrary GetPropertyValue / GetDynamicReference).
///
/// Output is deliberately small and targeted (no 4,000-line sweep). Runs once
/// automatically a couple of seconds after a save is loaded.
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

    // Full method/ctor/field dump for exactly these types (matched by simple Name):
    // the navigation chain to a player, the reference registry, and the binding
    // value plumbing. Kept small on purpose.
    private static readonly HashSet<string> DeepDump = new(StringComparer.Ordinal)
    {
        // --- navigation: how to walk to a real person/player ---
        "GameReference", "ClubReference", "TeamReference", "TeamSquadReference",
        "TacticsTeamSelectionReference", "SquadOverviewPlayerReference",
        "PersonReference", "IPlayerReference", "IPersonBaseReference",
        "INonPlayerReference", "PlayerAttributeReference", "MatchPlayerReference",
        "NationalTeamContainerReference",
        // --- the reference registry (mirror of PropertyIdentifierSet) ---
        "ReferenceIdentifierSet", "ReferenceIDInfo",
        // --- binding value plumbing (how the UI resolves a value) ---
        "Bindings", "BindingSubsystem",
        "VisualFunctionLibrary_GetPropertyValue",
        "VisualFunctionLibrary_TryGetDataReference",
        "VisualFunctionLibrary_GetDynamicReference",
        "DynamicReference",
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
        GUI.Label(new Rect(24, 74, 380, 22), "Stage 2 - mapping Game->Club->Squad->Player");
        string s = _dumped
            ? $"DONE - dumped {_lines} lines.\nSee BepInEx console / LogOutput.log."
            : $"Mapping... tries={_tries}";
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

            var allTypes = new List<Type>();
            foreach (var a in asms)
                foreach (var t in SafeTypes(a))
                    if (t != null) allTypes.Add(t);

            L($"===== FM26 Scout Mod: NAVIGATION + PLUMBING DUMP (v{Plugin.PluginVersion}) =====");
            L($"scanned {asms.Count} assemblies, {allTypes.Count} types");

            // Deep dump the curated navigation / registry / plumbing types.
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in allTypes)
            {
                if (t.Name != null && DeepDump.Contains(t.Name))
                {
                    DumpType(t);
                    found.Add(t.Name);
                }
            }
            foreach (var want in DeepDump)
                if (!found.Contains(want))
                    L($"  (not found: {want})");

            L($"===== FM26 Scout Mod: dump done, {_lines} lines =====");
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
            {
                // Skip trivial auto-property backing accessors to keep it readable.
                if (m.Name.StartsWith("get__", StringComparison.Ordinal) || m.Name.StartsWith("set__", StringComparison.Ordinal))
                    continue;
                L($"     {Sig(m)}");
            }

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
