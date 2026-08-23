using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (tree spy) — continuous binding-tree snapshots with REAL values.
///
/// v0.10.0 proved GetPathDebug decodes the live tree into readable paths, and
/// revealed Node carries its own PropertyID (m_propID) and TypedValue (Value).
/// Two problems fixed here:
///   1. Snapshots fired at the main menu (node threshold too low). Now we walk
///      every ~15s, printing only NEW nodes each pass, for up to 12 passes —
///      plenty of time to load the save, open a player profile, squad, search.
///   2. TypedValue.ToString() is just the type name. Now we dump TypedValue's
///      full method list once, and extract real values by invoking its no-arg
///      Get*/As*/To* methods returning primitives/strings.
///
/// Every node line shows: name, PropertyID (labelled if it's one of OUR known
/// IDs like PlayerCurrentAbility), path, and the extracted value. Nodes whose
/// PropID matches CA/PA/Age/Name get a "!!!" prefix. See binding-api-probe.md.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private const int MaxSnapshots = 12;
    private const float SnapshotInterval = 15f;

    // Known PropertyIDs (docs/property-ids.md) to label nodes with.
    private static readonly Dictionary<uint, string> KnownProps = new()
    {
        { 1346584898u, "PlayerCurrentAbility" },
        { 1347436866u, "PlayerPotentialAbility" },
        { 862020186u,  "NonPlayerCurrentAbility" },
        { 1647325216u, "NonPlayerPotentialAbility" },
        { 825565216u,  "Age" },
        { 862938733u,  "IsPlayer" },
        { 1718186862u, "FirstName" },
        { 1936024430u, "SecondName" },
        { 1349481321u, "Position" },
        { 844321568u,  "CurrentAbilityStars" },
        { 1480150644u, "PotentialAbilityStars" },
    };

    private bool _open = true;
    private int _snapshots;
    private float _nextTry;
    private int _tries;
    private int _lastNodeCount = -1;
    private string _status = "waiting for the binding subsystem...";
    private readonly HashSet<ulong> _seen = new();
    private bool _tvMethodsDumped;
    private List<MethodInfo> _tvGetters;   // discovered TypedValue extractors

    private void OnGUI()
    {
        if (_snapshots < MaxSnapshots && Time.unscaledTime >= _nextTry)
        {
            _nextTry = Time.unscaledTime + (_snapshots == 0 ? 3f : SnapshotInterval);
            TrySnapshot();
        }

        if (GUI.Button(new Rect(12, 12, 150, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;
        if (!_open)
            return;

        GUI.Box(new Rect(12, 48, 430, 170), "FM26 Scout Mod  v" + Plugin.PluginVersion);
        GUI.Label(new Rect(24, 74, 410, 22), "Stage 2 - spying the tree (new nodes each pass)");
        GUI.Label(new Rect(24, 100, 410, 44), $"snapshots: {_snapshots}/{MaxSnapshots}   nodes: {_lastNodeCount}\n{_status}");
        GUI.Label(new Rect(24, 150, 410, 60), "While this runs: load your save, open your SQUAD,\nopen a PLAYER PROFILE, open PLAYER SEARCH.\nEach new screen adds nodes we capture.");
    }

    private static void L(string msg) => Plugin.Logger.LogInfo(msg);

    private void TrySnapshot()
    {
        _tries++;
        try
        {
            object subsystem = GetBindingSubsystem();
            if (subsystem == null)
            {
                _status = $"attempt #{_tries}: binding subsystem not up yet";
                _nextTry = Time.unscaledTime + 3f;   // poll fast until it exists
                return;
            }

            Type bindingsType = subsystem.GetType();
            while (bindingsType != null && bindingsType.Name != "Bindings")
                bindingsType = bindingsType.BaseType;
            object nodes = Call(subsystem, bindingsType, "get_m_nodes");
            if (nodes == null) { _status = "m_nodes null"; return; }

            int count = ToInt(Call(nodes, nodes.GetType(), "get_Count"));
            _lastNodeCount = count;

            MethodInfo getRootKey = bindingsType.GetMethod("get_RootKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            object rootKey = getRootKey?.Invoke(null, null);
            Type keyType = rootKey?.GetType();
            ConstructorInfo keyCtor = keyType?.GetConstructor(new[] { typeof(ulong) });
            MethodInfo getPathDebug = keyType != null ? FindByName(bindingsType, "GetPathDebug", keyType) : null;

            _snapshots++;
            L($"===== FM26 Scout Mod: TREE SPY pass {_snapshots}/{MaxSnapshots} (v{Plugin.PluginVersion}) — nodes={count}, new below =====");

            int printed = 0, added = 0, hot = 0;
            foreach (object kv in EnumerateIl2Cpp(nodes))
            {
                if (kv == null) continue;
                object hashObj = Call(kv, kv.GetType(), "get_Key");
                object node = Call(kv, kv.GetType(), "get_Value");
                if (hashObj == null || node == null) continue;
                ulong hash = Convert.ToUInt64(hashObj);
                if (!_seen.Add(hash)) continue;
                added++;

                Type nt = node.GetType();
                string name = Call(node, nt, "get_Name") as string ?? "";
                uint propId = 0;
                object pid = Call(node, nt, "get_PropID");
                if (pid != null) { var v = Call(pid, pid.GetType(), "get_ID"); if (v != null) propId = Convert.ToUInt32(v); }
                object tv = Call(node, nt, "get_Value");

                if (tv != null && !_tvMethodsDumped)
                {
                    _tvMethodsDumped = true;
                    DumpTypedValueApi(tv.GetType());
                }
                string val = ExtractTypedValue(tv);

                string path = null;
                if (keyCtor != null && getPathDebug != null)
                {
                    try { path = getPathDebug.Invoke(subsystem, new object[] { keyCtor.Invoke(new object[] { hash }) }) as string; }
                    catch { }
                }

                string label = KnownProps.TryGetValue(propId, out var pn) ? pn : null;
                bool isHot = label != null
                    || (path != null && (Has(path, "abil") || Has(path, "potential") || Has(path, "attribute")))
                    || Has(name, "abil") || Has(name, "potential") || Has(name, "attribute");

                string line = $"  name={name}  propID={propId}{(label != null ? $"<{label}>" : "")}  path={Trunc(path ?? "?")}  value={val}";
                if (isHot) { hot++; L("!!!" + line); }
                else if (printed < 500) { printed++; L(line); }
            }

            L($"----- pass {_snapshots}: new={added}, printed={printed}, HOT={hot} -----");
            _status = _snapshots >= MaxSnapshots
                ? "all passes done - send me LogOutput.log"
                : $"pass {_snapshots} done ({added} new). Keep navigating screens!";
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[FM26 Scout Mod] spy #{_tries} error: {ex}");
        }
    }

    // ---------- TypedValue extraction ----------

    private void DumpTypedValueApi(Type tvType)
    {
        L($"----- TypedValue API ({tvType.FullName}) -----");
        _tvGetters = new List<MethodInfo>();
        foreach (var m in Safe(() => tvType.GetMethods(BindingFlags.Public | BindingFlags.Instance)))
        {
            string rn = null; try { rn = m.ReturnType?.Name; } catch { }
            var ps = Safe(m.GetParameters);
            var sb = new StringBuilder();
            for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(ps[i].ParameterType?.Name); }
            L($"     {rn} {m.Name}({sb})");

            // collect no-arg, non-generic extractors returning primitive/string/object
            if (ps.Length != 0 || m.IsGenericMethod || rn == null) continue;
            bool valueish = rn == "String" || rn == "Int32" || rn == "Int64" || rn == "Single"
                || rn == "Double" || rn == "Boolean" || rn == "UInt32" || rn == "Byte" || rn == "Object";
            bool named = m.Name.StartsWith("Get", StringComparison.Ordinal)
                || m.Name.StartsWith("As", StringComparison.Ordinal)
                || m.Name.StartsWith("To", StringComparison.Ordinal)
                || m.Name.StartsWith("get_", StringComparison.Ordinal);
            if (valueish && named && m.Name != "ToString" && m.Name != "GetHashCode")
                _tvGetters.Add(m);
        }
        L($"----- TypedValue extractors selected: {_tvGetters.Count} -----");
    }

    private string ExtractTypedValue(object tv)
    {
        if (tv == null) return "null";
        try
        {
            // IsNull short-circuit
            var isNull = Call(tv, tv.GetType(), "get_IsNull");
            if (isNull is bool b && b) return "<null-value>";

            string typeName = null;
            var dt = Call(tv, tv.GetType(), "get_DataType");
            if (dt != null) { try { typeName = (dt as Type)?.Name ?? Call(dt, dt.GetType(), "get_Name") as string; } catch { } }

            if (_tvGetters != null)
            {
                var results = new StringBuilder();
                int okCount = 0;
                foreach (var g in _tvGetters)
                {
                    object r;
                    try { r = g.Invoke(tv, null); } catch { continue; }
                    if (r == null) continue;
                    string s = SafeToString(r);
                    if (string.IsNullOrEmpty(s) || s == tv.GetType().FullName) continue;
                    if (okCount > 0) results.Append(" | ");
                    results.Append(g.Name).Append('=').Append(Trunc(s));
                    if (++okCount >= 2) break;
                }
                if (okCount > 0) return $"[{typeName ?? "?"}] {results}";
            }
            return $"[{typeName ?? "?"}] <no extractor>";
        }
        catch (Exception e) { return "<err:" + e.Message + ">"; }
    }

    // ---------- plumbing (same as v0.10.0) ----------

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

    private static IEnumerable<object> EnumerateIl2Cpp(object collection)
    {
        MethodInfo ge = null;
        foreach (var m in collection.GetType().GetMethods())
            if (m.Name == "GetEnumerator" && m.GetParameters().Length == 0) { ge = m; break; }
        if (ge == null) yield break;
        object en = null;
        try { en = ge.Invoke(collection, null); } catch { }
        if (en == null) yield break;
        MethodInfo mn = en.GetType().GetMethod("MoveNext", Type.EmptyTypes);
        MethodInfo cur = en.GetType().GetMethod("get_Current", Type.EmptyTypes);
        if (mn == null || cur == null) yield break;
        while (true)
        {
            bool has;
            try { has = mn.Invoke(en, null) is bool ok && ok; } catch { yield break; }
            if (!has) yield break;
            object c = null;
            try { c = cur.Invoke(en, null); } catch { }
            yield return c;
        }
    }

    private static object Call(object inst, Type t, string method)
    {
        try
        {
            MethodInfo m = null;
            foreach (var x in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (x.Name == method && x.GetParameters().Length == 0) { m = x; break; }
            return m?.Invoke(inst, null);
        }
        catch { return null; }
    }

    private static MethodInfo FindByName(Type t, string name, Type keyType)
    {
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (m.Name != name || m.IsGenericMethod) continue;
            var ps = m.GetParameters();
            if (ps.Length != 1) continue;
            var pt = ps[0].ParameterType;
            if (pt == keyType || (pt.IsByRef && pt.GetElementType() == keyType)) return m;
        }
        return null;
    }

    private static bool Has(string s, string sub) => s != null && s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
    private static string SafeToString(object o) { try { return o.ToString(); } catch { return null; } }
    private static string Trunc(string s) => s == null ? "null" : (s.Length > 140 ? s.Substring(0, 140) + "…" : s);
    private static object TryGet(Func<object> f) { try { return f(); } catch { return null; } }
    private static int ToInt(object o) { try { return Convert.ToInt32(o); } catch { return -1; } }
    private static T[] Safe<T>(Func<T[]> f) { try { return f() ?? Array.Empty<T>(); } catch { return Array.Empty<T>(); } }
}
