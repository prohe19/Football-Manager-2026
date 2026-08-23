using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (read the live store) — open the game's binding tree and dump values.
///
/// v0.9.0 found the handle: FM.UI.EmbeddedDataHandler.s_bindingSubsystem is the
/// live SI.Bindable.BindingSubsystem (null at the menu, set once a save loads).
/// Its inherited Bindings.DataSet is the list of every value the UI has fetched.
///
/// This build retries until that static is non-null (i.e. in a save), then:
///   - reports DataSet count,
///   - dumps the element ("Data") type's members once so we learn its shape,
///   - prints the first ~150 entries (path/key + value via reflection).
///
/// Open a player's profile before it runs and their attributes should appear in
/// the dump with their paths — the mapping we need to read any player. See
/// docs/binding-api-probe.md.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private bool _open = true;
    private bool _ran;
    private float _nextTry;
    private int _tries;
    private int _count = -1;

    private void OnGUI()
    {
        if (!_ran && Time.unscaledTime >= _nextTry)
        {
            _nextTry = Time.unscaledTime + 3f;
            TryDumpStore();
        }

        if (GUI.Button(new Rect(12, 12, 150, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;
        if (!_open)
            return;

        GUI.Box(new Rect(12, 48, 400, 150), "FM26 Scout Mod  v" + Plugin.PluginVersion);
        GUI.Label(new Rect(24, 74, 380, 22), "Stage 2 - dumping the live binding store");
        GUI.Label(new Rect(24, 100, 380, 40),
            _ran ? $"DONE - DataSet had {_count} entries.\nSee LogOutput.log."
                 : $"waiting for a loaded save... tries={_tries}");
    }

    private static void L(string msg) => Plugin.Logger.LogInfo(msg);

    private void TryDumpStore()
    {
        _tries++;
        try
        {
            object subsystem = GetBindingSubsystem();
            if (subsystem == null)
            {
                L($"[FM26 Scout Mod] attempt #{_tries}: s_bindingSubsystem is null (load a save; will retry)");
                return;
            }

            L($"===== FM26 Scout Mod: LIVE BINDING STORE (v{Plugin.PluginVersion}) =====");
            L($"  BindingSubsystem obtained: {subsystem.GetType().FullName}");

            // DataSet is inherited from Bindings: IReadOnlyList<Data>.
            MethodInfo getDataSet = FindNoArg(subsystem.GetType(), "get_DataSet");
            if (getDataSet == null) { L("  get_DataSet() not found"); _ran = true; return; }

            object list = getDataSet.Invoke(subsystem, null);
            if (list == null) { L("  DataSet is null"); _ran = true; return; }

            var lt = list.GetType();
            MethodInfo getItem = FindGetItem(lt);
            int count = GetCount(list, lt);
            _count = count;
            L($"  DataSet type={lt.Name}  count={count}  (getItem={(getItem != null)})");

            if (getItem != null)
            {
                // Enumerate by index; if Count is unknown, probe until get_Item throws.
                int cap = count >= 0 ? Math.Min(count, 6000) : 6000;
                bool shapeDumped = false;
                int printed = 0, got = 0;
                for (int i = 0; i < cap; i++)
                {
                    object d;
                    try { d = getItem.Invoke(list, new object[] { i }); }
                    catch { break; }   // ran off the end
                    if (d == null) continue;
                    got++;
                    if (!shapeDumped)
                    {
                        shapeDumped = true;
                        L($"----- Data element type: {d.GetType().FullName} -----");
                        DumpMembers(d.GetType());
                        L("----- entries (first 200) -----");
                    }
                    if (printed < 200) { printed++; L($"  [{i}] {Describe(d)}"); }
                }
                L($"  enumerated {got} entries (printed {printed})");
            }

            L("===== FM26 Scout Mod: live store dump done =====");
            _ran = true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[FM26 Scout Mod] dump #{_tries} error: {ex}");
        }
    }

    // Locate FM.UI.EmbeddedDataHandler.s_bindingSubsystem (a static) reflectively.
    private static object GetBindingSubsystem()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            string nm = null; try { nm = a.GetName().Name; } catch { }
            if (nm != "FM.UI") continue;
            Type t = null;
            try { foreach (var x in a.GetTypes()) if (x?.Name == "EmbeddedDataHandler") { t = x; break; } }
            catch (ReflectionTypeLoadException ex) { foreach (var x in ex.Types) if (x?.Name == "EmbeddedDataHandler") { t = x; break; } }
            catch { }
            if (t == null) return null;

            const BindingFlags SF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var p = t.GetProperty("s_bindingSubsystem", SF);
            if (p != null) { var v = TryGet(() => p.GetValue(null)); if (v != null) return v; }
            var f = t.GetField("s_bindingSubsystem", SF);
            if (f != null) { var v = TryGet(() => f.GetValue(null)); if (v != null) return v; }
            return null;
        }
        return null;
    }

    // Build a readable "key/path = value" string from a Data element via reflection.
    private static string Describe(object d)
    {
        var sb = new StringBuilder();
        Type t = d.GetType();
        foreach (var name in new[] { "Key", "Path", "FullPath", "Name" })
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null) { var v = TryGet(() => p.GetValue(d)); if (v != null) { sb.Append(name).Append('=').Append(Trunc(v.ToString())).Append("  "); } }
        }
        foreach (var name in new[] { "Value", "TypedValue", "Current" })
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null) { var v = TryGet(() => p.GetValue(d)); sb.Append(name).Append('=').Append(v == null ? "null" : Trunc(v.ToString())).Append("  "); }
        }
        if (sb.Length == 0) sb.Append("ToString=").Append(Trunc(d.ToString()));
        return sb.ToString();
    }

    private static void DumpMembers(Type t)
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var p in Safe(() => t.GetProperties(F))) L($"     prop {Short(p.PropertyType)} {p.Name}");
        foreach (var f in Safe(() => t.GetFields(F))) L($"     field {Short(f.FieldType)} {f.Name}");
        foreach (var m in Safe(() => t.GetMethods(F)))
        {
            if (m.Name.StartsWith("get_", StringComparison.Ordinal) || m.Name.StartsWith("set_", StringComparison.Ordinal)) continue;
            var ps = m.GetParameters(); var sb = new StringBuilder();
            for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Short(ps[i].ParameterType)); }
            L($"     method {Short(SafeRet(m))} {m.Name}({sb})");
        }
    }

    private static Type SafeRet(MethodInfo m) { try { return m.ReturnType; } catch { return null; } }
    private static string Short(Type t) => t?.Name ?? "?";
    private static string Trunc(string s) => s == null ? "null" : (s.Length > 120 ? s.Substring(0, 120) + "…" : s);
    private static object TryGet(Func<object> f) { try { return f(); } catch { return null; } }
    private static T[] Safe<T>(Func<T[]> f) { try { return f() ?? Array.Empty<T>(); } catch { return Array.Empty<T>(); } }

    // Count may live on a base interface (IReadOnlyCollection) not surfaced by
    // GetMethods() on the IReadOnlyList proxy — so check the interfaces too.
    private static int GetCount(object list, Type lt)
    {
        MethodInfo m = FindNoArg(lt, "get_Count");
        if (m == null)
            foreach (var it in Safe(lt.GetInterfaces)) { m = FindNoArg(it, "get_Count"); if (m != null) break; }
        if (m == null) return -1;
        try { return Convert.ToInt32(m.Invoke(list, null)); } catch { return -1; }
    }

    private static MethodInfo FindNoArg(Type t, string name)
    {
        try { foreach (var m in t.GetMethods()) if (m.Name == name && m.GetParameters().Length == 0) return m; } catch { }
        return null;
    }

    private static MethodInfo FindGetItem(Type t)
    {
        try
        {
            foreach (var m in t.GetMethods())
            {
                if (m.Name != "get_Item") continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && (ps[0].ParameterType == typeof(int) || ps[0].ParameterType.Name == "Int32")) return m;
            }
        }
        catch { }
        return null;
    }
}
