using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (fetch hunt II) — find the live binding store and the real value read.
///
/// v0.8.0 proved TryGetValue is only a client cache. FM26 reads attributes lazily
/// through SI.Bindable.Bindings (data handlers fetch from the native game_plugin
/// and Set the result into the binding tree). So this build hunts:
///
///   1. A live handle to the Bindings / BindingSubsystem singleton (static
///      property/field/0-arg method returning it) — and, if found, reports how
///      many live bindings it holds (DataSet count) + its root node.
///   2. Any method that RETURNS a value for a reference/property — i.e. returns
///      SI.Core.TypedValue, or is named Fetch/Query/GetPropertyValue/RequestData.
///
/// One of these is the door to reading real player data. See binding-api-probe.md.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private static readonly string[] AsmFilters = { "SI.", "FM.", "Bindable" };
    private static readonly string[] SingletonTypeNames = { "Bindings", "BindingSubsystem" };
    private static readonly string[] FetchNameHints =
    {
        "GetPropertyValue", "Fetch", "Query", "RequestData", "ReadProperty",
        "GetValueForProperty", "ResolveValue", "GetTypedValue",
    };

    private bool _open = true;
    private bool _ran;
    private float _nextTry;
    private int _tries;
    private int _lines;

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
        GUI.Label(new Rect(24, 74, 380, 22), "Stage 2 - finding the live binding store");
        GUI.Label(new Rect(24, 100, 380, 40), _ran ? $"DONE - {_lines} lines. See LogOutput.log." : $"probing... tries={_tries}");
    }

    private void L(string msg) { _lines++; Plugin.Logger.LogInfo(msg); }

    private void TryProbe()
    {
        _tries++;
        try
        {
            var types = new List<Type>();
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string nm = null; try { nm = a.GetName().Name; } catch { }
                if (nm == null) continue;
                bool keep = false;
                foreach (var f in AsmFilters) if (nm.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) { keep = true; break; }
                if (!keep) continue;
                foreach (var t in SafeTypes(a)) if (t != null) types.Add(t);
            }

            L($"===== FM26 Scout Mod: FETCH HUNT II (v{Plugin.PluginVersion}) =====");
            L($"scanned {types.Count} types");

            // 1) Singleton sources for Bindings / BindingSubsystem.
            L("----- singleton sources (static member -> Bindings/BindingSubsystem) -----");
            object liveBindings = null;
            string liveFrom = null;
            const BindingFlags SF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var t in types)
            {
                foreach (var p in Safe(() => t.GetProperties(SF)))
                {
                    if (!IsSingletonType(p.PropertyType)) continue;
                    L($"  PROP {t.FullName}.{p.Name} : {p.PropertyType.Name}");
                    if (liveBindings == null) { liveBindings = TryGet(() => p.GetValue(null)); if (liveBindings != null) liveFrom = $"{t.Name}.{p.Name}"; }
                }
                foreach (var f in Safe(() => t.GetFields(SF)))
                {
                    if (!IsSingletonType(f.FieldType)) continue;
                    L($"  FIELD {t.FullName}.{f.Name} : {f.FieldType.Name}");
                    if (liveBindings == null) { liveBindings = TryGet(() => f.GetValue(null)); if (liveBindings != null) liveFrom = $"{t.Name}.{f.Name}"; }
                }
                foreach (var m in Safe(() => t.GetMethods(SF)))
                {
                    if (m.GetParameters().Length != 0) continue;
                    Type rt = null; try { rt = m.ReturnType; } catch { }
                    if (!IsSingletonType(rt)) continue;
                    L($"  METHOD {t.FullName}.{m.Name}() : {rt.Name}");
                    if (liveBindings == null) { liveBindings = TryGet(() => m.Invoke(null, null)); if (liveBindings != null) liveFrom = $"{t.Name}.{m.Name}()"; }
                }
            }

            // If we got a live Bindings instance, report how much it holds.
            if (liveBindings != null)
            {
                L($"  >>> LIVE Bindings obtained from {liveFrom} (type {liveBindings.GetType().Name})");
                try
                {
                    var dsProp = liveBindings.GetType().GetProperty("DataSet");
                    var ds = dsProp?.GetValue(liveBindings);
                    var countProp = ds?.GetType().GetProperty("Count");
                    var count = countProp?.GetValue(ds);
                    L($"  >>> DataSet count = {count?.ToString() ?? "?"}");
                }
                catch (Exception e) { L("  DataSet read err: " + e.Message); }
                try
                {
                    var rootProp = liveBindings.GetType().GetProperty("RootNode");
                    var root = rootProp?.GetValue(liveBindings);
                    L($"  >>> RootNode = {(root == null ? "null" : "present")}");
                }
                catch (Exception e) { L("  RootNode read err: " + e.Message); }
            }
            else
            {
                L("  (no live Bindings instance obtained from a static member)");
            }

            // 2) Methods that return a TypedValue, or are named like a fetch.
            L("----- value-producing methods (return TypedValue, or fetch-named) -----");
            int shown = 0;
            foreach (var t in types)
            {
                MethodInfo[] ms;
                try { ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); }
                catch { continue; }
                foreach (var m in ms)
                {
                    string rn = null; try { rn = m.ReturnType?.Name; } catch { }
                    bool retTyped = rn == "TypedValue";
                    bool named = false;
                    foreach (var h in FetchNameHints) if (m.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) { named = true; break; }
                    if (!retTyped && !named) continue;
                    // keep the signal high: only in the binding/interop/UI namespaces
                    string ns = t.Namespace ?? "";
                    if (!(ns.StartsWith("SI.Bindable") || ns.StartsWith("SI.Interop") || ns.StartsWith("SI.Core") || ns.StartsWith("FM.UI")))
                        continue;
                    L($"  {t.FullName}.{Sig(m)}");
                    if (++shown >= 120) { L("  ...(cap)"); goto done; }
                }
            }
        done:
            L($"===== FM26 Scout Mod: fetch hunt II done, {_lines} lines =====");
            _ran = true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[FM26 Scout Mod] probe #{_tries} error: {ex}");
        }
    }

    private static bool IsSingletonType(Type t)
    {
        if (t == null) return false;
        foreach (var n in SingletonTypeNames) if (t.Name == n) return true;
        return false;
    }

    private static object TryGet(Func<object> f) { try { return f(); } catch { return null; } }

    private static string Sig(MethodInfo m)
    {
        var sb = new StringBuilder();
        if (m.IsStatic) sb.Append("static ");
        Type rt = null; try { rt = m.ReturnType; } catch { }
        sb.Append(rt?.Name ?? "?").Append(' ').Append(m.Name).Append('(');
        var ps = Safe(() => m.GetParameters());
        for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(ps[i].ParameterType?.Name ?? "?").Append(' ').Append(ps[i].Name); }
        return sb.Append(')').ToString();
    }

    private static T[] Safe<T>(Func<T[]> f) { try { return f() ?? Array.Empty<T>(); } catch { return Array.Empty<T>(); } }

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types ?? Array.Empty<Type>(); }
        catch { return Array.Empty<Type>(); }
    }
}
