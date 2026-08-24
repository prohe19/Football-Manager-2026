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
        { 1230661448u, "PlayerIndex" },
        { 1767075437u, "InitialSurname" },
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
    private readonly HashSet<ulong> _refNodes = new();   // nodes seen holding a PersonReference
    private bool _bindingsApiDumped;

    // ---- TypedValue extraction state ----
    private bool _tvApiDumped;
    private List<MethodInfo> _tvGenericGetters;   // generic<T>, 0 params or 1 out param
    private List<MethodInfo> _tvConversions;      // static op_Implicit/op_Explicit(TypedValue) -> primitive
    private readonly Dictionary<string, MethodInfo> _tvWinners = new();  // DataType name -> bound accessor (null = known-unreadable)

    private int _view;   // 0 = status, 1 = Top PA (wonderkids), 2 = Top CA
    private Vector2 _panelPos = new Vector2(12, 48);
    private float _panelH = 240;
    private bool _dragging;
    private Vector2 _dragOff;
    private const float PanelW = 500;

    private void OnGUI()
    {
        if (Time.unscaledTime >= _nextTry)
        {
            _nextTry = Time.unscaledTime + (_snapshots == 0 ? 3f : SnapshotInterval);
            TrySnapshot();
        }

        Event e = Event.current;

        // Hotkeys — immune to whatever the game does with the mouse.
        if (e != null && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.F6) { _open = !_open; e.Use(); }
            else if (e.keyCode == KeyCode.F7) { _open = true; _view = 0; e.Use(); }
            else if (e.keyCode == KeyCode.F8) { _open = true; _view = 1; e.Use(); }
            else if (e.keyCode == KeyCode.F9) { _open = true; _view = 2; e.Use(); }
        }

        if (GUI.Button(new Rect(12, 8, 170, 32), _open ? "Scout [F6] -" : "Scout [F6] +"))
            _open = !_open;
        if (!_open)
            return;

        Rect panel = new Rect(_panelPos.x, _panelPos.y, PanelW, _panelH);
        Rect title = new Rect(panel.x, panel.y, panel.width, 26);

        // Manual drag on the title bar.
        if (e != null)
        {
            if (e.type == EventType.MouseDown && title.Contains(e.mousePosition))
            { _dragging = true; _dragOff = e.mousePosition - _panelPos; e.Use(); }
            else if (_dragging && e.type == EventType.MouseDrag)
            { _panelPos = e.mousePosition - _dragOff; e.Use(); }
            else if (e.type == EventType.MouseUp) _dragging = false;
        }

        GUI.Box(panel, "");
        GUI.Box(title, $"FM26 Scout Mod v{Plugin.PluginVersion}   (drag here)   F6 hide");

        float x = panel.x + 10, y = panel.y + 32;
        if (GUI.Button(new Rect(x, y, 130, 34), _view == 0 ? "> Status <" : "Status  [F7]")) _view = 0;
        if (GUI.Button(new Rect(x + 138, y, 130, 34), _view == 1 ? "> Top PA <" : "Top PA  [F8]")) _view = 1;
        if (GUI.Button(new Rect(x + 276, y, 130, 34), _view == 2 ? "> Top CA <" : "Top CA  [F9]")) _view = 2;
        y += 42;

        if (_view == 0)
        {
            GUI.Label(new Rect(x, y, PanelW - 20, 22), $"Shadow scouting DB: {_rows.Count} rows / {_persons.Count} players");
            GUI.Label(new Rect(x, y + 24, PanelW - 20, 64), $"passes: {_snapshots}   nodes: {_lastNodeCount}\n{_status}");
            GUI.Label(new Rect(x, y + 92, PanelW - 20, 60), "Browse SQUADS and PLAYER SEARCH results - every row\nthe game shows is captured (name, age, CA/PA stars).\nThen open Top PA / Top CA (buttons or F8/F9).");
            _panelH = y + 160 - panel.y;
        }
        else
        {
            _panelH = DrawTopList(_view == 1, x, y, panel.y);
        }

        // Swallow every remaining mouse event over the panel so it does not
        // leak into the game UI underneath.
        if (e != null && e.isMouse && panel.Contains(e.mousePosition))
            e.Use();
    }

    private float DrawTopList(bool byPa, float x, float y, float panelTop)
    {
        // Merged per-person records first; raw rows only when they never learned
        // their person index (so we don't show the same player twice). A missing
        // name no longer hides a scouted player — we show a placeholder instead.
        var list = new List<ScoutRow>();
        foreach (var p in _persons.Values)
            if (byPa ? p.PaMax > 0 : p.CaMax > 0)
                list.Add(p);
        foreach (var r in _rows.Values)
            if (r.PersonIndex <= 0 && (byPa ? r.PaMax > 0 : r.CaMax > 0))
                list.Add(r);
        list.Sort((a, b) =>
        {
            int c = byPa ? b.PaMax.CompareTo(a.PaMax) : b.CaMax.CompareTo(a.CaMax);
            if (c == 0) c = byPa ? b.CaMax.CompareTo(a.CaMax) : b.PaMax.CompareTo(a.PaMax);
            return c;
        });

        GUI.Label(new Rect(x, y, PanelW - 20, 20),
            (byPa ? "TOP POTENTIAL (wonderkids)" : "TOP CURRENT ABILITY") + $"  -  {list.Count} scouted");
        y += 22;
        GUI.Label(new Rect(x, y, PanelW - 20, 20), "name                              age    CA       PA");
        y += 20;

        int shown = Math.Min(12, list.Count);
        for (int i = 0; i < shown; i++)
        {
            var r = list[i];
            string nm = r.AnyName ?? (r.PersonIndex > 0 ? $"player #{r.PersonIndex}" : "(name not seen yet)");
            if (nm.Length > 28) nm = nm.Substring(0, 28);
            GUI.Label(new Rect(x, y + i * 20, PanelW - 20, 20),
                $"{i + 1,2}. {nm,-28} {(r.Age > 0 ? r.Age.ToString() : "?"),3}   {Stars(r.CaMin, r.CaMax),-8} {Stars(r.PaMin, r.PaMax)}");
        }
        if (shown == 0)
        {
            GUI.Label(new Rect(x, y, PanelW - 20, 40), "Nothing scouted yet - browse squads / search results\nwith star-rating columns visible, then come back.");
            return y + 56 - panelTop;
        }
        return y + shown * 20 + 12 - panelTop;
    }

    private static string Stars(int min, int max)
    {
        if (max <= 0) return "-";
        float lo = min / 4f, hi = max / 4f;
        return min >= 0 && min != max ? $"{lo:0.#}-{hi:0.#}*" : $"{hi:0.#}*";
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
            bool verbose = _snapshots <= MaxSnapshots;
            if (verbose)
                L($"===== FM26 Scout Mod: TREE SPY pass {_snapshots}/{MaxSnapshots} (v{Plugin.PluginVersion}) — nodes={count}, new below =====");

            // One-time dump of the binding system's own API — the map for driving
            // it directly (create bindings / run game.Search ourselves) later.
            if (!_bindingsApiDumped)
            {
                _bindingsApiDumped = true;
                try
                {
                    L($"===== Bindings API: {bindingsType.FullName} =====");
                    int n = 0;
                    foreach (var m in bindingsType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    {
                        if (m.DeclaringType != bindingsType) continue;
                        var ps = string.Join(", ", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name + " " + p.Name));
                        L($"    {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({ps})");
                        if (++n >= 120) { L("    ...(truncated)"); break; }
                    }
                    L("===== end Bindings API =====");
                }
                catch (Exception e2) { L("Bindings API dump failed: " + e2.Message); }
            }

            // Row X-ray buffer: on verbose passes, collect every row-node line we
            // see so we can print COMPLETE sample rows afterwards (the cold-line
            // cap has been hiding where the name cell of star tables lives).
            List<KeyValuePair<string, string>> xbuf = verbose ? new List<KeyValuePair<string, string>>() : null;

            int printed = 0, added = 0, hot = 0;
            foreach (object kv in EnumerateIl2Cpp(nodes))
            {
                if (kv == null) continue;
                object hashObj = Call(kv, kv.GetType(), "get_Key");
                object node = Call(kv, kv.GetType(), "get_Value");
                if (hashObj == null || node == null) continue;
                ulong hash = Convert.ToUInt64(hashObj);
                bool isNew = _seen.Add(hash);
                if (isNew) added++;

                Type nt = node.GetType();
                string name = Call(node, nt, "get_Name") as string ?? "";
                uint propId = 0;
                object pid = Call(node, nt, "get_PropID");
                if (pid != null) { var v = Call(pid, pid.GetType(), "get_ID"); if (v != null) propId = Convert.ToUInt32(v); }

                // Scout capture runs for interesting nodes EVERY pass (the game
                // recycles table rows, so values change under the same node).
                // _refNodes: nodes that once held a PersonReference — re-read them
                // every pass too, or a recycled row would keep its OLD person's
                // index while capturing the NEW person's name.
                bool interesting = name == "binding" || CapturedProps.Contains(propId) || _refNodes.Contains(hash);
                // Process interesting nodes every pass and NEW nodes always (even
                // after the verbose window — a fresh list needs its PersonReference
                // discovered whenever the user first browses it); print later only
                // while verbose.
                if (!interesting && !isNew) continue;

                object tv = Call(node, nt, "get_Value");
                string val = ExtractTypedValue(tv);
                if (val != null && val.StartsWith("[PersonReference]", StringComparison.Ordinal))
                    _refNodes.Add(hash);

                string path = null;
                if (keyCtor != null && getPathDebug != null)
                {
                    try { path = getPathDebug.Invoke(subsystem, new object[] { keyCtor.Invoke(new object[] { hash }) }) as string; }
                    catch { }
                }

                if (interesting || _refNodes.Contains(hash))
                    Capture(path, name, propId, val);

                if (xbuf != null && path != null && xbuf.Count < 800)
                {
                    string rk = RowKey(path);
                    if (rk != null)
                        xbuf.Add(new KeyValuePair<string, string>(rk,
                            $"      XRAY  name={name} propID={propId} value={Trunc(val)}  ::{path.Substring(rk.Length)}"));
                }

                if (!verbose || !isNew) continue;

                string label = KnownProps.TryGetValue(propId, out var pn) ? pn : null;
                bool isHot = HotProps.Contains(propId)
                    || (path != null && (Has(path, "abil") || Has(path, "potential") || Has(path, "attribute")))
                    || Has(name, "abil") || Has(name, "potential") || Has(name, "attribute");

                string line = $"  name={name}  propID={propId}{(label != null ? $"<{label}>" : "")}  path={Trunc(path ?? "?")}  value={val}";
                if (isHot) { hot++; L("!!!" + line); }
                else if (printed < MaxColdLinesPerPass) { printed++; L(line); }
            }

            // Merge rows into per-person records only after the whole pass, when
            // every recycled row has re-learned its current person index — merging
            // mid-pass could attribute a row's new values to its previous occupant.
            foreach (var r in _rows.Values)
                if (r.PersonIndex > 0)
                    MergePerson(r);

            // Print the X-ray: the COMPLETE node list of one starred row and one
            // named row, to find where star tables keep their name cell (and
            // where name lists keep their person reference).
            if (xbuf != null && xbuf.Count > 0)
            {
                string starKey = null, nameKey = null;
                foreach (var kv2 in xbuf)
                {
                    if (!_rows.TryGetValue(kv2.Key, out var xr)) continue;
                    if (starKey == null && (xr.CaMax > 0 || xr.PaMax > 0)) starKey = kv2.Key;
                    if (nameKey == null && xr.AnyName != null && xr.CaMax <= 0 && xr.PaMax <= 0) nameKey = kv2.Key;
                }
                foreach (string tk in new[] { starKey, nameKey })
                {
                    if (tk == null) continue;
                    L($"      ===== XRAY of row {tk} =====");
                    int shown2 = 0;
                    foreach (var kv2 in xbuf)
                    {
                        if (kv2.Key != tk) continue;
                        L(kv2.Value);
                        if (++shown2 >= 45) { L("      XRAY ...(truncated)"); break; }
                    }
                }
            }

            int named = 0, withCa = 0, withPa = 0;
            foreach (var r in _rows.Values)
            {
                if (r.AnyName != null) named++;
                if (r.CaMax > 0) withCa++;
                if (r.PaMax > 0) withPa++;
            }
            int joined = 0, pCa = 0;
            foreach (var p in _persons.Values)
            {
                if (p.CaMax > 0 || p.PaMax > 0) pCa++;
                if (p.Name != null && (p.CaMax > 0 || p.PaMax > 0)) joined++;
            }
            if (verbose || _snapshots % 6 == 0)
            {
                L($"----- pass {_snapshots}: new={added}, printed={printed}, HOT={hot}, scoutRows={_rows.Count} (named={named} ca={withCa} pa={withPa}) persons={_persons.Count} (starred={pCa} joined={joined}) -----");
                // Starred rows first — those are the ones whose join we are debugging.
                int dumped = 0;
                foreach (var kvp in _rows)
                {
                    var r = kvp.Value;
                    if (r.CaMax <= 0 && r.PaMax <= 0) continue;
                    L($"      starred row: name={r.AnyName ?? "?"} age={r.Age} ca={r.CaMin}-{r.CaMax} pa={r.PaMin}-{r.PaMax} idx={r.PersonIndex} key={Trunc(kvp.Key)}");
                    if (++dumped >= 3) break;
                }
                dumped = 0;
                foreach (var kvp in _rows)
                {
                    var r = kvp.Value;
                    if (r.AnyName == null || r.CaMax > 0 || r.PaMax > 0) continue;
                    L($"      named row:   name={r.AnyName} age={r.Age} idx={r.PersonIndex} key={Trunc(kvp.Key)}");
                    if (++dumped >= 3) break;
                }
                dumped = 0;
                foreach (var kvp in _persons)
                {
                    var p = kvp.Value;
                    L($"      person: idx={kvp.Key} name={p.Name ?? "?"} age={p.Age} ca={p.CaMin}-{p.CaMax} pa={p.PaMin}-{p.PaMax}");
                    if (++dumped >= 3) break;
                }
                if (verbose && _snapshots == MaxSnapshots)
                    LogWinnerSummary();
            }
            _status = $"pass {_snapshots}: rows={_rows.Count}, named={named}, CA={withCa}, PA={withPa}\npersons={_persons.Count}, with stars={pCa}, joined (name+stars)={joined}";
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[FM26 Scout Mod] spy #{_tries} error: {ex}");
        }
    }

    // ---------- Stage 3: shadow scouting DB ----------
    // Every table row the game materializes (squad tables, search results)
    // lives under a "...Items.<n>..." path with a PersonReference binding plus
    // Age / star-range / Name cells. We group those by row and accumulate a
    // scouted-player database that the Top PA / Top CA views sort.

    private class ScoutRow
    {
        public string Name;
        public string FirstName, SecondName;
        public int Age = -1;
        public int CaMin = -1, CaMax = -1;
        public int PaMin = -1, PaMax = -1;
        public int PersonIndex = -1;
        public string IdxPath;   // node that supplied PersonIndex (recycle detection)
        public bool IdxStrong;   // index came from the row's own binding / PlayerIndex

        // Best display name we have for this record.
        public string AnyName =>
            Name ?? (SecondName == null ? null
                   : FirstName == null ? SecondName : FirstName + " " + SecondName);
    }

    private static readonly HashSet<uint> CapturedProps = new()
    {
        825565216u,   // Age
        1851878757u,  // Name
        1718186862u,  // FirstName
        1936024430u,  // SecondName
        1230661448u,  // PlayerIndex (table row → DB person index)
        1767075437u,  // InitialSurname (star tables' name cell)
        844321568u, 1131757922u, 1128481106u,  // CA stars / ranges (+coach)
        1480150644u, 1349468514u, 1129333074u, // PA stars / ranges (+coach)
    };

    private readonly Dictionary<string, ScoutRow> _rows = new();

    // A table row lives under ".Items.<row>." (squad tables) or
    // ".items<n>.<row>." (streamed lists, search results) — group by the path
    // up to and including the row token.
    private static string RowKey(string path)
    {
        int i = path.IndexOf("tems", StringComparison.Ordinal);
        while (i > 0)
        {
            char c0 = path[i - 1];
            if ((c0 == 'I' || c0 == 'i') && (i == 1 || path[i - 2] == '.'))
            {
                int j = i + 4;                                        // after "tems"
                while (j < path.Length && char.IsDigit(path[j])) j++; // "items0"
                if (j < path.Length && path[j] == '.')
                {
                    int k = j + 1;
                    while (k < path.Length && path[k] != '.') k++;    // row token
                    if (k > j + 1) return path.Substring(0, k);
                }
            }
            i = path.IndexOf("tems", i + 1, StringComparison.Ordinal);
        }
        return null;
    }

    // Content pages (player profile etc.) have a 32-hex root segment and no
    // ".Items." rows for the page's own person — group their screen-level nodes
    // into one pseudo-row, so opening a profile joins name + stars + index.
    private static string PageKey(string path)
    {
        int d = path.IndexOf('.');
        if (d < 24) return null;
        int hex = 0;
        for (int i = 0; i < d; i++)
        {
            char c = path[i];
            if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) hex++;
        }
        return hex >= 24 ? path.Substring(0, d) : null;
    }

    private void Capture(string path, string nodeName, uint propId, string val)
    {
        if (path == null || val == null) return;
        string key = RowKey(path);
        bool pageRow = false;
        if (key == null)
        {
            key = PageKey(path);
            if (key == null) return;
            pageRow = true;
        }

        if (!_rows.TryGetValue(key, out var row))
        {
            row = new ScoutRow();
            _rows[key] = row;
        }

        switch (propId)
        {
            case 825565216u:   // Age
                if (TryParseTagged(val, "DynamicNumber", out int age)) row.Age = age;
                break;
            case 844321568u: case 1131757922u: case 1128481106u:   // CA
                if (TryParseRange(val, out int cmin, out int cmax)) { row.CaMin = cmin; row.CaMax = cmax; }
                break;
            case 1480150644u: case 1349468514u: case 1129333074u:  // PA
                if (TryParseRange(val, out int pmin, out int pmax)) { row.PaMin = pmin; row.PaMax = pmax; }
                break;
            case 1851878757u:  // Name — rows also carry club/nation names.
                               // Judge only the path AFTER the row key ("Team"
                               // in "FirstTeamC" earlier in the path must not
                               // disqualify the player's own name).
                               // Page pseudo-rows carry too many unrelated Name
                               // nodes (attribute names, ban notices) — for those
                               // we trust only FirstName/SecondName.
                if (pageRow) break;
                string tail = path.Length > key.Length ? path.Substring(key.Length) : "";
                if (!Has(tail, "Team") && !Has(tail, "Club") && !Has(tail, "Nation") && !Has(tail, "Competition"))
                {
                    string n = ParseDisplayString(val);
                    if (n != null && (row.Name == null || n.Length > row.Name.Length)) row.Name = n;
                }
                break;
            case 1718186862u:  // FirstName
                { var f = ParseDisplayString(val); if (f != null) row.FirstName = f; }
                break;
            case 1936024430u:  // SecondName
                { var s2 = ParseDisplayString(val); if (s2 != null) row.SecondName = s2; }
                break;
            case 1767075437u:  // InitialSurname — star tables' own name cell
                               // ("D. Essugo"); the FULL name hides inside the
                               // styled markup's tooltip ("Click to view
                               // Dário Cassia Luís Essugo's profile").
                {
                    string nm = NameFromStyled(val);
                    if (nm != null && (row.Name == null || nm.Length > row.Name.Length)) row.Name = nm;
                }
                break;
            // NOTE: UniqueId (1970170212) is deliberately NOT used as the join key —
            // it may be a different numbering than PersonReference.m_index, and a
            // mixed-scheme join would merge two different people.
            // PlayerIndex (1230661448) is handled below, after the switch.
        }

        // Any top-level PersonReference value in a row names the row's person —
        // EXCEPT "Human"-flavoured refs, which are always the human MANAGER
        // (rows embed them for tooltip context; the v0.24 log showed them
        // hijacking rows' identities). The row's own ".binding" node is the
        // authoritative identity; anything else is a weak guess.
        if (val.StartsWith("[PersonReference]", StringComparison.Ordinal))
        {
            bool foreign = nodeName == "Human" || nodeName == "humanteam"
                        || path.EndsWith(".Human", StringComparison.Ordinal)
                        || path.Contains(".Human.");
            if (!foreign)
            {
                int k = val.IndexOf("get_m_index=", StringComparison.Ordinal);
                if (k >= 0)
                {
                    int j = k + 12, e = j;
                    while (e < val.Length && char.IsDigit(val[e])) e++;
                    if (e > j && int.TryParse(val.Substring(j, e - j), out int idx))
                        SetRowIndex(key, ref row, idx, path,
                            strong: path.EndsWith(".binding", StringComparison.Ordinal));
                }
            }
        }
        else if (propId == 1230661448u)  // PlayerIndex — DB person index as a number
        {
            if (TryParseTagged(val, "DynamicNumber", out int pidx2) && pidx2 > 0)
                SetRowIndex(key, ref row, pidx2, path, strong: true);
        }
    }

    private void SetRowIndex(string key, ref ScoutRow row, int idx, string srcPath, bool strong)
    {
        if (row.PersonIndex > 0 && row.PersonIndex != idx)
        {
            if (srcPath == row.IdxPath)
            {
                // The SAME node changed its person → the game recycled this
                // table row for another player; start fresh instead of mixing
                // two people's data.
                row = new ScoutRow();
                _rows[key] = row;
            }
            else if (strong && !row.IdxStrong)
            {
                // A strong source corrects a weak guess. Keep the row's data —
                // it belongs to the row; only the identity label was wrong.
            }
            else
            {
                return;   // conflicting weaker/equal source — keep what we have
            }
        }
        row.PersonIndex = idx;
        if (strong || row.IdxPath == null) row.IdxPath = srcPath;
        row.IdxStrong |= strong;
    }

    // Person-level shadow DB: rows keyed by DB person index, merged across tables.
    private readonly Dictionary<int, ScoutRow> _persons = new();

    private void MergePerson(ScoutRow row)
    {
        if (!_persons.TryGetValue(row.PersonIndex, out var p))
        {
            p = new ScoutRow { PersonIndex = row.PersonIndex };
            _persons[row.PersonIndex] = p;
        }
        string n = row.AnyName;
        if (n != null && (p.Name == null || n.Length > p.Name.Length)) p.Name = n;
        if (row.Age > 0) p.Age = row.Age;
        if (row.CaMax > 0) { p.CaMin = row.CaMin; p.CaMax = row.CaMax; }
        if (row.PaMax > 0) { p.PaMin = row.PaMin; p.PaMax = row.PaMax; }
    }

    private static bool TryParseTagged(string v, string tag, out int n)
    {
        n = 0;
        string p = "[" + tag + "] ";
        return v.StartsWith(p, StringComparison.Ordinal) && int.TryParse(v.Substring(p.Length).Trim(), out n);
    }

    private static bool TryParseRange(string v, out int min, out int max)
    {
        max = FindMapInt(v, "1298233430");   // MaxValue
        min = FindMapInt(v, "1298755158");   // MinValue
        return max >= 0;
    }

    private static int FindMapInt(string v, string key)
    {
        string marker = key + "=[DynamicNumber] ";
        int i = v.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return -1;
        i += marker.Length;
        int j = i;
        while (j < v.Length && (char.IsDigit(v[j]) || v[j] == '-')) j++;
        return j > i && int.TryParse(v.Substring(i, j - i), out int n) ? n : -1;
    }

    private static bool IsB64(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
        || c == '+' || c == '/' || c == '=';

    // Name cells in star tables are styled "☺<base64 markup>☻D. Essugo☺" where
    // the base64 tooltip text contains the person's FULL name:
    // "…Click to view Dário Cassia Luís Essugo's profile". Prefer the full name,
    // fall back to the display text.
    private static string NameFromStyled(string val)
    {
        string disp = ParseDisplayString(val);
        string full = null;
        int i = val.IndexOf("[String] ", StringComparison.Ordinal);
        if (i >= 0)
        {
            string s = val.Substring(i + 9).Trim();
            int start = 0;
            while (start < s.Length && (s[start] == '☺' || s[start] == '☻')) start++;
            int end = start;
            while (end < s.Length && IsB64(s[end])) end++;
            if (end - start >= 24 && (end - start) % 4 == 0)
            {
                try
                {
                    string text = System.Text.Encoding.UTF8.GetString(
                        Convert.FromBase64String(s.Substring(start, end - start)));
                    int c = text.IndexOf("Click to view ", StringComparison.Ordinal);
                    if (c >= 0)
                    {
                        c += 14;
                        int d = text.IndexOf("'s profile", c, StringComparison.Ordinal);
                        if (d > c && d - c < 60) full = text.Substring(c, d - c);
                    }
                }
                catch { }
                if (disp == null && end < s.Length)
                {
                    // no ☻ marker — take whatever trails the markup blob
                    string t = s.Substring(end).Trim('☺', '☻', ' ');
                    if (t.Length > 0 && t.Length < 60) disp = t;
                }
            }
        }
        return full ?? disp;
    }

    // The game styles strings as "☺<markup>☻Display Text☺" — pull the display text.
    private static string ParseDisplayString(string v)
    {
        int i = v.IndexOf("[String] ", StringComparison.Ordinal);
        if (i < 0) return null;
        string s = v.Substring(i + 9);
        int b = s.LastIndexOf('☻');
        if (b >= 0)
        {
            int e = s.IndexOf('☺', b);
            s = e > b ? s.Substring(b + 1, e - b - 1) : s.Substring(b + 1);
        }
        s = s.Trim();
        return s.Length > 0 && s.Length < 60 ? s : null;
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
        // DynamicNumber prints its number. Generic containers print their
        // TYPE name from ToString ("System.Collections.Generic.List`1[…]"),
        // which must not short-circuit the enumeration below.
        string s = SafeToString(r);
        if (!string.IsNullOrEmpty(s) && s != "Il2CppSystem.Object" && s != rt.FullName && s != rt.Name
            && !s.Contains("`1[") && !s.StartsWith("System.Collections", StringComparison.Ordinal))
            return Trunc(s);

        // Enumerable payloads (List`1, DynamicReference's key/value map, star
        // ranges): print count + first entries.
        if (depth < 2 && HasNoArgMethod(rt, "GetEnumerator"))
            return DescribeEnumerable(r, depth);

        // Still stuck as the untyped interop base (closed-generic List proxies
        // can't be instantiated): enumerate through il2cpp's own reflection,
        // which needs no proxy typing at all.
        if (depth < 2 && rt.FullName == "Il2CppSystem.Object")
        {
            string d = DescribeUntypedCollection(r, depth);
            if (d != null) return d;
        }

        if (depth >= 2) return $"<{rt.Name}>";
        return DrillPayload(r, rt.Name);
    }

    // Invoke a no-arg il2cpp method by name on an UNTYPED proxy, going through
    // il2cpp reflection: obj.GetIl2CppType().GetMethod(name).Invoke(obj, null).
    private object Il2Invoke(object obj, string method)
    {
        try
        {
            object il2t = Call(obj, obj.GetType(), "GetIl2CppType");
            if (il2t == null) return null;
            MethodInfo getMethod = null;
            foreach (var m in il2t.GetType().GetMethods())
                if (m.Name == "GetMethod" && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(string)) { getMethod = m; break; }
            object mi = getMethod?.Invoke(il2t, new object[] { method });
            if (mi == null) return null;
            foreach (var im in mi.GetType().GetMethods())
                if (im.Name == "Invoke" && im.GetParameters().Length == 2)
                    return TryGet(() => im.Invoke(mi, new object[] { obj, null }));
        }
        catch { }
        return null;
    }

    private static readonly Dictionary<string, Type> UnboxMap = new()
    {
        { "System.Boolean", typeof(bool) }, { "System.Int32", typeof(int) }, { "System.UInt32", typeof(uint) },
        { "System.Int64", typeof(long) }, { "System.UInt64", typeof(ulong) }, { "System.Int16", typeof(short) },
        { "System.UInt16", typeof(ushort) }, { "System.Byte", typeof(byte) }, { "System.SByte", typeof(sbyte) },
        { "System.Single", typeof(float) }, { "System.Double", typeof(double) }, { "System.Char", typeof(char) },
    };

    // Count + first entries of an il2cpp collection we could not re-type.
    // Returns null when the object has no Count (i.e. not a collection).
    // il2cpp-reflection results come back BOXED — UpCast unboxes them.
    private string DescribeUntypedCollection(object coll, int depth)
    {
        object cnt = Il2Invoke(coll, "get_Count");
        if (cnt == null) return null;
        cnt = UpCast(cnt);
        var sb = new StringBuilder("List(count=").Append(Trunc(SafeToString(cnt))).Append(')');
        object en = Il2Invoke(coll, "GetEnumerator");
        int i = 0;
        while (en != null && i < 5)
        {
            object mvRaw = Il2Invoke(en, "MoveNext");
            if (mvRaw == null) break;
            if (!(UpCast(mvRaw) is bool mv && mv)) break;
            object cur = Il2Invoke(en, "get_Current");
            sb.Append(i == 0 ? " [" : ", ").Append(cur == null ? "null" : FormatPayload(cur, "", depth + 1));
            i++;
        }
        if (i >= 5) sb.Append(", …");
        if (i > 0) sb.Append(']');
        return sb.ToString();
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

            // Boxed il2cpp primitives (il2cpp-reflection results arrive this
            // way) -> unbox to a managed primitive via interop's Unbox<T>.
            if (UnboxMap.TryGetValue(fn, out var clr))
            {
                MethodInfo ub = null;
                foreach (var m in payload.GetType().GetMethods())
                    if (m.Name == "Unbox" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0) { ub = m; break; }
                if (ub != null)
                {
                    object u = null;
                    try { u = ub.MakeGenericMethod(clr).Invoke(payload, null); } catch { }
                    if (u != null) return u;
                }
            }

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
    // Generous cap: styled name strings carry their display text at the END,
    // and the scout capture parses these same strings.
    private static string Trunc(string s) => s == null ? "null" : (s.Length > 300 ? s.Substring(0, 300) + "…" : s);
    private static object TryGet(Func<object> f) { try { return f(); } catch { return null; } }
    private static int ToInt(object o) { try { return Convert.ToInt32(o); } catch { return -1; } }
    private static T[] Safe<T>(Func<T[]> f) { try { return f() ?? Array.Empty<T>(); } catch { return Array.Empty<T>(); } }
}
