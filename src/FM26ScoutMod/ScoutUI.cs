using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FM26ScoutMod;

/// <summary>
/// Stage 2 (tree spy II) — same continuous snapshots as v0.11, but with REAL
/// value extraction.
///
/// v0.11 proved the tree walk works at scale (9,500+ nodes: squad rows with
/// per-player Age, ability star ranges, the whole PlayerAttributesBlock, and
/// game.Search with Results/PersonList). But every value printed as
/// "get_IsAlive=True | get_IsPooled=False": SI.Core.TypedValue exposes only
/// flag PROPERTIES non-generically, so the old Get*/As*/To* heuristic matched
/// the flags, and the real payload accessor — which must be a GENERIC method
/// like Get&lt;T&gt;() — was explicitly excluded by the IsGenericMethod filter.
///
/// v0.12 proved As&lt;T&gt; is the payload accessor: strings/bools printed real
/// values. v0.13 finishes the job: DataType is a real managed Type, so we bind
/// As&lt;RealType&gt; to get a correctly-typed payload, then DRILL into wrapper
/// objects (DynamicNumber/DynamicReference hold the actual numbers — Age,
/// attribute 1-20 values, star ranges) by dumping their API once and invoking
/// their no-arg primitive getters. Lists (game.Search PersonList/Results,
/// Team.FilteredPlayers) print their count + first items.
/// </summary>
public class ScoutUI : MonoBehaviour
{
    public ScoutUI(System.IntPtr ptr) : base(ptr) { }

    private const int MaxSnapshots = 12;
    private const float SnapshotInterval = 15f;
    private const int MaxColdLinesPerPass = 120;   // hot lines always print

    // Known PropertyIDs (docs/property-ids.md + observed live in v0.11) → label.
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
        // Seen live in the v0.11 tree:
        { 1131757922u, "CurrentAbilityStarRange" },
        { 1349468514u, "PotentialAbilityStarRange" },
        { 1128481106u, "CoachCurrentAbilityStarRange" },
        { 1129333074u, "CoachPotentialAbilityStarRange" },
        { 1112556614u, "AttributeValues" },
        { 1886680684u, "PropertyValue" },
        { 2036486263u, "ScoutedCurrentAbilityInfo" },
        { 1399683185u, "ScoutedPotentialAbilityInfo" },
        { 1464367445u, "StarRange" },
        { 1936023922u, "Search" },
        { 1919251317u, "Results" },
        { 1886547059u, "PersonList" },
        { 1936877166u, "SearchIsFinished" },
        { 1179414636u, "FilteredPlayers" },
        { 1970170212u, "UniqueId" },
        { 1851878757u, "Name" },
        { 1886157170u, "Player" },
        { 1885696627u, "Person" },
    };

    // Subset of KnownProps that always deserves a "!!!" line. (Name/Search etc.
    // appear hundreds of times, so they are labelled but not hot.)
    private static readonly HashSet<uint> HotProps = new()
    {
        1346584898u, 1347436866u, 862020186u, 1647325216u, 825565216u,
        844321568u, 1480150644u, 1131757922u, 1349468514u, 1128481106u,
        1129333074u, 1112556614u, 1886680684u, 2036486263u, 1399683185u,
        1464367445u, 1919251317u, 1886547059u, 1936877166u, 1179414636u,
    };

    private bool _open = true;
    private int _snapshots;
    private float _nextTry;
    private int _tries;
    private int _lastNodeCount = -1;
    private string _status = "waiting for the binding subsystem...";
    private readonly HashSet<ulong> _seen = new();

    // ---- TypedValue extraction state ----
    private bool _tvApiDumped;
    private List<MethodInfo> _tvGenericGetters;   // generic<T>, 0 params or 1 out param
    private List<MethodInfo> _tvConversions;      // static op_Implicit/op_Explicit(TypedValue) -> primitive
    private readonly Dictionary<string, MethodInfo> _tvWinners = new();  // DataType name -> bound accessor (null = known-unreadable)

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
        GUI.Label(new Rect(24, 74, 410, 22), "Stage 2 - reading REAL values from the tree");
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
                string val = ExtractTypedValue(tv);

                string path = null;
                if (keyCtor != null && getPathDebug != null)
                {
                    try { path = getPathDebug.Invoke(subsystem, new object[] { keyCtor.Invoke(new object[] { hash }) }) as string; }
                    catch { }
                }

                string label = KnownProps.TryGetValue(propId, out var pn) ? pn : null;
                bool isHot = HotProps.Contains(propId)
                    || (path != null && (Has(path, "abil") || Has(path, "potential") || Has(path, "attribute")))
                    || Has(name, "abil") || Has(name, "potential") || Has(name, "attribute");

                string line = $"  name={name}  propID={propId}{(label != null ? $"<{label}>" : "")}  path={Trunc(path ?? "?")}  value={val}";
                if (isHot) { hot++; L("!!!" + line); }
                else if (printed < MaxColdLinesPerPass) { printed++; L(line); }
            }

            L($"----- pass {_snapshots}: new={added}, printed={printed}, HOT={hot} -----");
            if (_snapshots >= MaxSnapshots)
                LogWinnerSummary();
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
        _tvGenericGetters = new List<MethodInfo>();
        _tvConversions = new List<MethodInfo>();

        foreach (var m in Safe(() => tvType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)))
        {
            var ps = Safe(m.GetParameters);
            var sb = new StringBuilder();
            for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(ps[i].ParameterType?.Name); }
            string gen = "";
            if (m.IsGenericMethodDefinition)
            {
                var ga = Safe(m.GetGenericArguments);
                gen = "<" + string.Join(",", Array.ConvertAll(ga, a => a.Name)) + ">";
            }
            L($"     {(m.IsStatic ? "static " : "")}{TryGet(() => (object)m.ReturnType?.Name) ?? "?"} {m.Name}{gen}({sb})");

            // Candidate A: generic Get<T>()-style — 1 type arg, 0 params or 1 byref (TryGet<T>(out T)).
            if (m.IsGenericMethodDefinition && !m.IsStatic && Safe(m.GetGenericArguments).Length == 1)
            {
                if (ps.Length == 0 || (ps.Length == 1 && ps[0].ParameterType != null && ps[0].ParameterType.IsByRef))
                    _tvGenericGetters.Add(m);
            }
            // Candidate B: static conversion op taking a TypedValue, returning a primitive/string.
            if (m.IsStatic && !m.IsGenericMethodDefinition && ps.Length == 1
                && ps[0].ParameterType != null && ps[0].ParameterType.IsAssignableFrom(tvType))
            {
                string rn = TryGet(() => (object)m.ReturnType?.Name) as string;
                if (rn == "Int32" || rn == "UInt32" || rn == "Int64" || rn == "Single" || rn == "Double"
                    || rn == "Boolean" || rn == "Byte" || rn == "String")
                    _tvConversions.Add(m);
            }
        }
        L($"----- TypedValue candidates: generic={_tvGenericGetters.Count}  conversions={_tvConversions.Count} -----");
    }

    private string ExtractTypedValue(object tv)
    {
        if (tv == null) return "null";
        Type tvt = tv.GetType();
        try
        {
            if (Call(tv, tvt, "get_IsNull") is bool b && b) return "<null>";

            string dtName = null;
            object dt = Call(tv, tvt, "get_DataType");
            Type realT = dt as Type;   // managed Type if interop mapped it directly...
            if (dt != null) dtName = realT?.Name ?? Call(dt, dt.GetType(), "get_Name") as string;
            if (realT == null && dt != null)
            {
                // ...otherwise it's an Il2CppSystem.Type: resolve the managed
                // proxy class by full name so As<RealType> gives a typed payload.
                string fn = Call(dt, dt.GetType(), "get_FullName") as string;
                if (fn != null) realT = ResolveProxyType(fn);
            }
            dtName ??= "?";

            if (!_tvApiDumped) { _tvApiDumped = true; DumpTypedValueApi(tvt); }

            // v0.14 lesson: As<T> with an il2cpp STRUCT type (GameDate, DateTime,
            // Color32…) asks for a generic instantiation that was never compiled
            // into the game -> instant native crash, uncatchable. So generic
            // As<T> is now used ONLY for known-safe primitives; everything else
            // goes through the non-generic `Object Get()` + TryCast (constraint-
            // checked, cannot crash) with AsString() as the fallback.

            if (_tvUseGet.Contains(dtName))
                return $"[{dtName}] {ViaGet(tv, tvt, dtName)}";

            // Reuse a primitive accessor that already worked for this data type.
            if (_tvWinners.TryGetValue(dtName, out var winner))
            {
                if (winner == null) return $"[{dtName}] <?>";
                object rr = InvokeGetterRaw(winner, tv);
                return $"[{dtName}] {(rr == null ? "<read-failed>" : FormatPayload(rr, dtName))}";
            }

            // Primitive payloads: bind As<T> to the matching CLR primitive.
            if (_tvGenericGetters != null)
            {
                foreach (Type clr in CandidateClrTypes(dtName))
                {
                    foreach (var g in _tvGenericGetters)
                    {
                        MethodInfo bound;
                        try { bound = g.MakeGenericMethod(clr); } catch { continue; }
                        object r = InvokeGetterRaw(bound, tv);
                        if (r != null)
                        {
                            _tvWinners[dtName] = bound;
                            L($"  >>> extractor found: {dtName} -> {g.Name}<{clr.Name}>");
                            return $"[{dtName}] {FormatPayload(r, dtName)}";
                        }
                    }
                }
            }

            // Everything else: the crash-proof route.
            _tvUseGet.Add(dtName);
            return $"[{dtName}] {ViaGet(tv, tvt, dtName)}";
        }
        catch (Exception e) { return "<err:" + e.Message + ">"; }
    }

    private readonly HashSet<string> _tvUseGet = new();

    // Crash-proof payload read: non-generic `Object Get()`, re-typed via
    // TryCast where possible, with `AsString()` as the universal fallback.
    private string ViaGet(object tv, Type tvt, string dtName)
    {
        object payload = Call(tv, tvt, "Get");
        string formatted = payload == null ? null : FormatPayload(payload, dtName);
        if (formatted != null && !formatted.StartsWith("<opaque", StringComparison.Ordinal))
            return formatted;
        string s = TryGet(() => Call(tv, tvt, "AsString")) as string;
        if (!string.IsNullOrEmpty(s))
            return Trunc(s);
        return formatted ?? "<no-payload>";
    }

    private static object InvokeGetterRaw(MethodInfo m, object tv)
    {
        try
        {
            var ps = m.GetParameters();
            if (m.IsStatic && ps.Length == 1)
                return m.Invoke(null, new[] { tv });
            if (ps.Length == 0)
                return m.Invoke(tv, null);
            if (ps.Length == 1 && ps[0].ParameterType.IsByRef)
            {
                var args = new object[1];
                object ok = m.Invoke(tv, args);
                if ((ok is bool okb && okb || ok == null) && args[0] != null)
                    return args[0];
            }
        }
        catch { }
        return null;
    }

    // ---------- payload drilling (v0.13) ----------
    // v0.12 showed As<T> unwraps TypedValue, but numbers/lists come back as
    // wrapper objects (DynamicNumber, DynamicReference, List`1) whose ToString
    // is useless. Here we go one level deeper: dump each wrapper type's API
    // once, then invoke its no-arg primitive/string getters to print the
    // actual number/text inside.

    private readonly Dictionary<string, List<MethodInfo>> _drillGetters = new();
    private int _drillDumps;

    private string FormatPayload(object r, string dtName) => FormatPayload(r, dtName, 0);

    private string FormatPayload(object r, string dtName, int depth)
    {
        if (r == null) return "null";
        Type rt = r.GetType();
        if (rt.IsPrimitive || rt.IsEnum || r is string)
            return Trunc(SafeToString(r));

        // v0.13 lesson: As<Object> hands back a proxy typed as the interop BASE
        // (Il2CppSystem.Object), which exposes nothing. Use interop's own
        // GetIl2CppType + TryCast<T> (pointer-rewrap fallback) to re-type it.
        if (rt.FullName == "Il2CppSystem.Object")
        {
            r = UpCast(r);
            rt = r.GetType();
        }

        // A TypedValue nested inside a container: unwrap it the normal way.
        if (rt.Name.Contains("TypedValue") && depth < 3)
            return ExtractTypedValue(r);

        // A meaningful ToString (not just a type name) wins — this is how
        // DynamicNumber prints its number.
        string s = SafeToString(r);
        if (!string.IsNullOrEmpty(s) && s != "Il2CppSystem.Object" && s != rt.FullName && s != rt.Name)
            return Trunc(s);

        // Enumerable payloads (List`1, DynamicReference's key/value map, star
        // ranges): print count + first entries.
        if (depth < 2 && HasNoArgMethod(rt, "GetEnumerator"))
            return DescribeEnumerable(r, depth);

        if (depth >= 2) return $"<{rt.Name}>";
        return DrillPayload(r, rt.Name);
    }

    private static bool HasNoArgMethod(Type t, string name)
    {
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (m.Name == name && !m.IsGenericMethodDefinition && m.GetParameters().Length == 0) return true;
        return false;
    }

    private string DescribeEnumerable(object coll, int depth)
    {
        Type ct = coll.GetType();
        object cnt = Call(coll, ct, "get_Count");
        var sb = new StringBuilder(ct.Name.StartsWith("List`", StringComparison.Ordinal) ? "List" : ct.Name)
            .Append("(count=").Append(cnt == null ? "?" : cnt.ToString()).Append(')');
        int i = 0;
        foreach (object item in EnumerateIl2Cpp(coll))
        {
            sb.Append(i == 0 ? " [" : ", ");
            if (item == null) sb.Append("null");
            else
            {
                object k = Call(item, item.GetType(), "get_Key");
                object v = Call(item, item.GetType(), "get_Value");
                if (k != null || v != null)
                    sb.Append(Trunc(SafeToString(k))).Append('=')
                      .Append(v == null ? "null" : FormatPayload(v, "", depth + 1));
                else
                    sb.Append(FormatPayload(item, "", depth + 1));
            }
            if (++i >= 5) { sb.Append(", …"); break; }
        }
        if (i > 0) sb.Append(']');
        return sb.ToString();
    }

    // Re-type an interop-base proxy as its actual class, resolved by il2cpp
    // full name against the loaded interop assemblies.
    private object UpCast(object payload)
    {
        try
        {
            object il2t = Call(payload, payload.GetType(), "GetIl2CppType");
            if (il2t == null) return payload;
            string fn = Call(il2t, il2t.GetType(), "get_FullName") as string;
            if (string.IsNullOrEmpty(fn)) return payload;
            Type proxy = ResolveProxyType(fn);
            if (proxy == null) return payload;
            MethodInfo tc = null;
            foreach (var m in payload.GetType().GetMethods())
                if (m.Name == "TryCast" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0) { tc = m; break; }
            if (tc != null)
            {
                object cast = null;
                try { cast = tc.MakeGenericMethod(proxy).Invoke(payload, null); } catch { }
                if (cast != null) return cast;
            }
            // TryCast returns null for closed generics whose il2cpp class
            // pointer isn't registered (List`1<...>). Every proxy class has an
            // IntPtr ctor though — re-wrap the raw pointer directly. Safe here
            // because the type came from the object's own GetIl2CppType.
            if (Call(payload, payload.GetType(), "get_Pointer") is IntPtr ip && ip != IntPtr.Zero)
            {
                var ctor = proxy.GetConstructor(new[] { typeof(IntPtr) });
                if (ctor != null)
                {
                    try { return ctor.Invoke(new object[] { ip }); } catch { }
                }
            }
            return payload;
        }
        catch { return payload; }
    }

    private readonly Dictionary<string, Type> _proxyTypeCache = new();

    // il2cpp full name -> managed interop proxy Type. Handles List`1[[Elem,...]]
    // and the System.* -> Il2CppSystem.* namespace mapping.
    private Type ResolveProxyType(string fullName)
    {
        if (_proxyTypeCache.TryGetValue(fullName, out var cached)) return cached;
        Type result = null;
        int g = fullName.IndexOf("`1[[", StringComparison.Ordinal);
        if (g >= 0)
        {
            string outer = fullName.Substring(0, g) + "`1";
            int start = g + 4;
            int comma = fullName.IndexOf(',', start);
            Type elem = comma > start ? ResolveProxyType(fullName.Substring(start, comma - start)) : null;
            Type outerDef = FindLoadedType(outer) ?? FindLoadedType(MapSystem(outer));
            if (outerDef != null && elem != null)
                try { result = outerDef.MakeGenericType(elem); } catch { }
        }
        else
        {
            result = FindLoadedType(fullName) ?? FindLoadedType(MapSystem(fullName));
        }
        if (result == null)
            L($"  ??? no proxy type found for il2cpp type '{Trunc(fullName)}'");
        _proxyTypeCache[fullName] = result;
        return result;
    }

    private static string MapSystem(string n)
        => n.StartsWith("System.", StringComparison.Ordinal) ? "Il2CppSystem." + n.Substring(7) : n;

    private static Type FindLoadedType(string fullName)
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = null;
            try { t = a.GetType(fullName); } catch { }
            if (t != null) return t;
        }
        return null;
    }

    private string DrillPayload(object payload, string typeName)
    {
        Type pt = payload.GetType();
        if (!_drillGetters.TryGetValue(typeName, out var getters))
        {
            getters = new List<MethodInfo>();
            bool dump = _drillDumps < 10;
            if (dump) { _drillDumps++; L($"----- payload API ({pt.FullName}) -----"); }
            foreach (var m in Safe(() => pt.GetMethods(BindingFlags.Public | BindingFlags.Instance)))
            {
                var ps = Safe(m.GetParameters);
                string rn = TryGet(() => (object)m.ReturnType?.Name) as string;
                // skip interop plumbing inherited from Il2CppObjectBase/Object
                string decl = TryGet(() => (object)m.DeclaringType?.FullName) as string;
                bool plumbing = decl == null || decl == "System.Object" || decl == "Il2CppSystem.Object"
                    || decl.StartsWith("Il2CppInterop", StringComparison.Ordinal);
                if (dump && !plumbing)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(ps[i].ParameterType?.Name); }
                    L($"     {rn} {m.Name}{(m.IsGenericMethod ? "<T>" : "")}({sb})");
                }
                if (plumbing || ps.Length != 0 || m.IsGenericMethod || rn == null) continue;
                bool valueish = rn == "String" || rn == "Int32" || rn == "Int64" || rn == "Single"
                    || rn == "Double" || rn == "Boolean" || rn == "UInt32" || rn == "Byte" || rn == "Int16";
                if (valueish && m.Name != "ToString" && m.Name != "GetHashCode" && m.Name != "get_WasCollected")
                    getters.Add(m);
            }
            // public getters before internal m_ backing fields
            getters.Sort((a, x) => a.Name.StartsWith("get_m_", StringComparison.Ordinal)
                .CompareTo(x.Name.StartsWith("get_m_", StringComparison.Ordinal)));
            _drillGetters[typeName] = getters;
        }

        var outp = new StringBuilder();
        int ok = 0;
        foreach (var g in getters)
        {
            object r;
            try { r = g.Invoke(payload, null); } catch { continue; }
            if (r == null) continue;
            if (ok > 0) outp.Append(' ');
            outp.Append(g.Name).Append('=').Append(Trunc(SafeToString(r)));
            if (++ok >= 4) break;
        }
        return ok > 0 ? outp.ToString() : $"<opaque {pt.Name}>";
    }

    private static IEnumerable<Type> CandidateClrTypes(string dtName)
    {
        switch (dtName)
        {
            case "Int32": yield return typeof(int); break;
            case "UInt32": yield return typeof(uint); break;
            case "Int64": yield return typeof(long); break;
            case "UInt64": yield return typeof(ulong); break;
            case "Int16": yield return typeof(short); break;
            case "UInt16": yield return typeof(ushort); break;
            case "Byte": yield return typeof(byte); break;
            case "SByte": yield return typeof(sbyte); break;
            case "Single": yield return typeof(float); break;
            case "Double": yield return typeof(double); break;
            case "Boolean": yield return typeof(bool); break;
            case "Char": yield return typeof(char); break;
            case "String":
                yield return typeof(string);
                yield return typeof(Il2CppSystem.Object);
                break;
            default:
                // Game types go through the non-generic Get() route instead —
                // binding As<T> to arbitrary (struct) types crashes the game.
                yield break;
        }
    }

    private void LogWinnerSummary()
    {
        L("----- TypedValue extractor summary (DataType -> accessor) -----");
        foreach (var kv in _tvWinners)
            L($"     {kv.Key} -> {(kv.Value == null ? "<none worked>" : kv.Value.Name)}");
    }

    // ---------- plumbing (same as v0.10/0.11) ----------

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
                if (x.Name == method && !x.IsGenericMethodDefinition && x.GetParameters().Length == 0) { m = x; break; }
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
