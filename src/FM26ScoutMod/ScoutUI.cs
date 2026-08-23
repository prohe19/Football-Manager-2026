using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (binding-tree walker) — decode the live tree with GetPathDebug.
///
/// v0.9.2 read the wrong structure: Bindings.DataSet is a recycling pool
/// (opaque hashed DataKeys, null Values). The REAL tree is Bindings.m_nodes
/// (Dictionary&lt;UInt64 hash, Node&gt;), and Bindings ships a decoder:
///     String GetPathDebug(Key key)   — readable path for any live node
///     TypedValue Get(Key key)        — the node's current value
///
/// This build walks m_nodes and prints path + value for every live node, twice:
/// snapshot 1 as soon as the save is loaded, snapshot 2 ~30s later (open a
/// player's profile in between!). A player's CA/PA paths appearing in snapshot 2
/// reveal the addressing pattern — then we can bind those paths ourselves for
/// ANY player and let the game's own handlers fetch from native. That is the
/// road to in-game Genie Scout. See docs/binding-api-probe.md.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private bool _open = true;
    private int _dumps;                // snapshots completed (target: 2)
    private float _nextTry;
    private int _tries;
    private int _nodeCount = -1;
    private string _status = "waiting for save...";

    private void OnGUI()
    {
        if (_dumps < 2 && Time.unscaledTime >= _nextTry)
        {
            _nextTry = Time.unscaledTime + 3f;
            TryWalkTree();
        }

        if (GUI.Button(new Rect(12, 12, 150, 30), _open ? "Scout  [-]" : "Scout  [+]"))
            _open = !_open;
        if (!_open)
            return;

        GUI.Box(new Rect(12, 48, 420, 170), "FM26 Scout Mod  v" + Plugin.PluginVersion);
        GUI.Label(new Rect(24, 74, 400, 22), "Stage 2 - walking the live binding tree");
        GUI.Label(new Rect(24, 100, 400, 44), $"snapshots done: {_dumps}/2   nodes: {_nodeCount}\n{_status}");
        GUI.Label(new Rect(24, 150, 400, 44), "After the save loads: OPEN A PLAYER'S PROFILE\nand stay there ~30s for snapshot 2.");
    }

    private static void L(string msg) => Plugin.Logger.LogInfo(msg);

    private void TryWalkTree()
    {
        _tries++;
        try
        {
            object subsystem = GetBindingSubsystem();
            if (subsystem == null)
            {
                _status = $"attempt #{_tries}: binding subsystem not up yet";
                if (_tries % 5 == 0) L($"[FM26 Scout Mod] {_status}");
                return;
            }

            // Bindings base type (BindingSubsystem : Bindings).
            Type bindingsType = subsystem.GetType();
            while (bindingsType != null && bindingsType.Name != "Bindings")
                bindingsType = bindingsType.BaseType;
            if (bindingsType == null) { _status = "Bindings base type not found"; L(_status); _dumps = 2; return; }

            object nodes = Call(subsystem, bindingsType, "get_m_nodes");
            if (nodes == null) { _status = "m_nodes null"; return; }

            int count = ToInt(Call(nodes, nodes.GetType(), "get_Count"));
            _nodeCount = count;
            if (count < 50)
            {
                _status = $"attempt #{_tries}: only {count} nodes (not in a save yet?)";
                if (_tries % 5 == 0) L($"[FM26 Scout Mod] {_status}");
                return;
            }

            // Key struct: take its Type from the static RootKey property.
            MethodInfo getRootKey = bindingsType.GetMethod("get_RootKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            object rootKey = getRootKey?.Invoke(null, null);
            if (rootKey == null) { _status = "RootKey null"; L(_status); _dumps = 2; return; }
            Type keyType = rootKey.GetType();

            // Key from UInt64 hash: ctor(ulong) or set the first ulong field.
            ConstructorInfo keyCtor = keyType.GetConstructor(new[] { typeof(ulong) });
            FieldInfo keyHashField = null;
            if (keyCtor == null)
            {
                foreach (var f in keyType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    if (f.FieldType == typeof(ulong)) { keyHashField = f; break; }
            }

            MethodInfo getPathDebug = FindByName(bindingsType, "GetPathDebug", keyType);
            MethodInfo getValue = FindGet(bindingsType, keyType);
            L($"===== FM26 Scout Mod: BINDING TREE WALK #{_dumps + 1} (v{Plugin.PluginVersion}) =====");
            L($"  nodes={count}  keyType={keyType.FullName}  ctor(ulong)={(keyCtor != null)}  hashField={(keyHashField != null ? keyHashField.Name : "-")}");
            L($"  GetPathDebug={(getPathDebug != null)}  Get(Key)->TypedValue={(getValue != null)}");

            // Walk the dictionary: enumerate KeyValuePair<ulong, Node>.
            int printed = 0, walked = 0, interesting = 0;
            bool nodeShapeDumped = false, tvShapeDumped = false;
            foreach (object kv in EnumerateIl2Cpp(nodes))
            {
                if (kv == null) continue;
                object hashObj = Call(kv, kv.GetType(), "get_Key");
                object node = Call(kv, kv.GetType(), "get_Value");
                if (hashObj == null) continue;
                ulong hash = Convert.ToUInt64(hashObj);
                walked++;

                if (!nodeShapeDumped && node != null)
                {
                    nodeShapeDumped = true;
                    L($"----- Node type: {node.GetType().FullName} -----");
                    DumpMembers(node.GetType());
                }

                // Build a Key for this hash.
                object key = null;
                try
                {
                    if (keyCtor != null) key = keyCtor.Invoke(new object[] { hash });
                    else if (keyHashField != null) { key = Activator.CreateInstance(keyType); keyHashField.SetValue(key, hash); }
                }
                catch { }
                if (key == null) continue;

                string path = null;
                try { path = getPathDebug?.Invoke(subsystem, new object[] { key }) as string; } catch { }
                object val = null;
                try { val = getValue?.Invoke(subsystem, new object[] { key }); } catch { }

                if (!tvShapeDumped && val != null)
                {
                    tvShapeDumped = true;
                    L($"----- TypedValue type: {val.GetType().FullName} -----");
                    DumpMembers(val.GetType());
                }

                string vs = val == null ? "null" : Trunc(SafeToString(val));
                string line = $"  {hash:X16}  path={Trunc(path ?? "?")}  value={vs}";
                bool hot = path != null && (path.IndexOf("abil", StringComparison.OrdinalIgnoreCase) >= 0
                                         || path.IndexOf("potential", StringComparison.OrdinalIgnoreCase) >= 0
                                         || path.IndexOf("person", StringComparison.OrdinalIgnoreCase) >= 0
                                         || path.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0);
                if (hot) { interesting++; L("**" + line); }
                else if (printed < 400) { printed++; L(line); }
                if (walked >= 4000) { L("  ...(walk cap 4000)"); break; }
            }

            L($"----- walk done: walked={walked}, printed={printed}, interesting(person/player/abil)={interesting} -----");
            L($"===== FM26 Scout Mod: tree walk #{_dumps + 1} done =====");

            _dumps++;
            if (_dumps == 1)
            {
                _status = "snapshot 1 done - OPEN A PLAYER PROFILE now; snapshot 2 in ~30s";
                _nextTry = Time.unscaledTime + 30f;
                L("[FM26 Scout Mod] >>> Open a player's profile now — snapshot 2 fires in ~30 seconds <<<");
            }
            else
            {
                _status = "both snapshots done - send me LogOutput.log";
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[FM26 Scout Mod] walk #{_tries} error: {ex}");
        }
    }

    // ---------- reflection plumbing ----------

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

    /// Enumerate any Il2Cpp collection via its GetEnumerator/MoveNext/Current.
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
            try { has = mn.Invoke(en, null) is bool b && b; } catch { yield break; }
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
            var m = t.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (m == null)
                foreach (var x in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    if (x.Name == method && x.GetParameters().Length == 0) { m = x; break; }
            return m?.Invoke(inst, null);
        }
        catch { return null; }
    }

    /// Find instance method `name` taking exactly one Key (possibly byref).
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

    /// Find the non-generic Get(Key) -> TypedValue.
    private static MethodInfo FindGet(Type t, Type keyType)
    {
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (m.Name != "Get" || m.IsGenericMethod) continue;
            var ps = m.GetParameters();
            if (ps.Length != 1) continue;
            var pt = ps[0].ParameterType;
            if (pt == keyType || (pt.IsByRef && pt.GetElementType() == keyType))
            {
                string rn = null; try { rn = m.ReturnType?.Name; } catch { }
                if (rn == "TypedValue") return m;
            }
        }
        return null;
    }

    private static void DumpMembers(Type t)
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var p in Safe(() => t.GetProperties(F))) L($"     prop {p.PropertyType?.Name} {p.Name}");
        foreach (var f in Safe(() => t.GetFields(F))) L($"     field {f.FieldType?.Name} {f.Name}");
    }

    private static string SafeToString(object o) { try { return o.ToString(); } catch { return "<tostring err>"; } }
    private static string Trunc(string s) => s == null ? "null" : (s.Length > 160 ? s.Substring(0, 160) + "…" : s);
    private static object TryGet(Func<object> f) { try { return f(); } catch { return null; } }
    private static int ToInt(object o) { try { return Convert.ToInt32(o); } catch { return -1; } }
    private static T[] Safe<T>(Func<T[]> f) { try { return f() ?? Array.Empty<T>(); } catch { return Array.Empty<T>(); } }
}
