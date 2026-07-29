using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// 취조 방향 그래프 시각화 창 (읽기 전용 노드 뷰).
//   메뉴: Tools → Editor0 → Interrogation Graph Viewer
//
//   · 질문(파랑) / 모순 질문(빨강) / 증언(호박) 노드를 의존 깊이에 따라 좌→우 열로 배치
//   · 엣지: 선행질문(파랑) · 선행증언(호박) · 기록지 선택(빨강) · 증언 획득(초록)
//   · 빈 곳 드래그로 이동(팬), 노드 클릭 시 하단에 상세 정보 표시
public class InterrogationGraphWindow : EditorWindow
{
    [MenuItem("Tools/Editor0/Interrogation Graph Viewer")]
    static void Open()
    {
        var w = GetWindow<InterrogationGraphWindow>("취조 그래프");
        w.minSize = new Vector2(760, 480);
        w.Rebuild();
    }

    // ---- 소스 ----
    InterrogationCase sourceAsset;
    bool useSample = true;

    // ---- 뷰 모델 ----
    class VNode { public string key, id, title, tooltip; public int kind, depth; public Rect rect; }
    struct VEdge { public string from, to; public int type; }

    readonly List<VNode> nodes = new List<VNode>();
    readonly Dictionary<string, VNode> map = new Dictionary<string, VNode>();
    readonly List<VEdge> edges = new List<VEdge>();

    Vector2 pan = new Vector2(20, 20);
    string selectedKey;

    const float NodeW = 176, NodeH = 56, GapX = 92, GapY = 18, Margin = 20;
    const float TopBarH = 22, DetailH = 60;

    GUIStyle idStyle, subStyle, detailStyle;

    static readonly Color BgColor = new Color(0.16f, 0.16f, 0.18f);
    static readonly Color QuestionFill = new Color(0.28f, 0.40f, 0.64f);
    static readonly Color ContradictionFill = new Color(0.60f, 0.26f, 0.26f);
    static readonly Color TestimonyFill = new Color(0.60f, 0.47f, 0.20f);

    void OnEnable() { Rebuild(); }

    // ------------------------------------------------------------------
    // 그래프 → 뷰 모델
    // ------------------------------------------------------------------
    void Rebuild()
    {
        nodes.Clear(); map.Clear(); edges.Clear();

        CaseGraph g = (useSample || sourceAsset == null) ? SampleCaseGraph.Build() : sourceAsset.BuildGraph();
        if (g == null) { Repaint(); return; }

        foreach (var s in g.Suspects)
            if (s != null && s.questions != null)
                foreach (var q in s.questions)
                {
                    if (q == null || string.IsNullOrEmpty(q.id)) continue;
                    int kind = q.kind == NodeKind.Contradiction ? 1 : 0;
                    AddNode("Q:" + q.id, q.id, s.suspectName + " · " + q.label, kind, QuestionTooltip(s, q));
                }

        foreach (var t in g.Testimonies)
        {
            if (t == null || string.IsNullOrEmpty(t.id)) continue;
            AddNode("T:" + t.id, t.id, t.text, 2, "증언 · " + t.ownerSuspectName + "\n" + t.text);
        }

        foreach (var s in g.Suspects)
            if (s != null && s.questions != null)
                foreach (var q in s.questions)
                {
                    if (q == null) continue;
                    string qk = "Q:" + q.id;
                    if (q.requiredQuestionIds != null) foreach (var pre in q.requiredQuestionIds) AddEdge("Q:" + pre, qk, 0);
                    if (q.requiredTestimonyIds != null) foreach (var pre in q.requiredTestimonyIds) AddEdge("T:" + pre, qk, 1);
                    if (!string.IsNullOrEmpty(q.requiredSelectedTestimonyId)) AddEdge("T:" + q.requiredSelectedTestimonyId, qk, 3);
                    if (!string.IsNullOrEmpty(q.requiredSelectedTestimonyId2)) AddEdge("T:" + q.requiredSelectedTestimonyId2, qk, 3);
                    if (q.grantTestimonyIds != null) foreach (var tid in q.grantTestimonyIds) AddEdge(qk, "T:" + tid, 2);
                }

        Layout();
        Repaint();
    }

    void AddNode(string key, string id, string title, int kind, string tooltip)
    {
        if (map.ContainsKey(key)) return;
        var n = new VNode { key = key, id = id, title = title, kind = kind, tooltip = tooltip };
        nodes.Add(n); map[key] = n;
    }

    void AddEdge(string from, string to, int type)
    {
        if (map.ContainsKey(from) && map.ContainsKey(to)) edges.Add(new VEdge { from = from, to = to, type = type });
    }

    static string QuestionTooltip(SuspectData s, QuestionNode q)
    {
        var sb = new StringBuilder();
        sb.Append(q.kind == NodeKind.Contradiction ? "모순 질문 · " : "질문 · ").Append(s.suspectName).Append('\n');
        sb.Append("id: ").Append(q.id).Append('\n').Append("label: ").Append(q.label);
        if (q.requiredQuestionIds != null && q.requiredQuestionIds.Count > 0) sb.Append("\n선행질문: ").Append(string.Join(", ", q.requiredQuestionIds));
        if (q.requiredTestimonyIds != null && q.requiredTestimonyIds.Count > 0) sb.Append("\n선행증언: ").Append(string.Join(", ", q.requiredTestimonyIds));
        if (!string.IsNullOrEmpty(q.requiredSelectedTestimonyId)) sb.Append("\n선택증언: ").Append(q.requiredSelectedTestimonyId);
        if (!string.IsNullOrEmpty(q.requiredSelectedTestimonyId2)) sb.Append("\n선택증언2: ").Append(q.requiredSelectedTestimonyId2);
        if (q.grantTestimonyIds != null && q.grantTestimonyIds.Count > 0) sb.Append("\n획득증언: ").Append(string.Join(", ", q.grantTestimonyIds));
        sb.Append("\n조건: ").Append(q.requireAll ? "AND(모두)" : "OR(하나라도)");
        return sb.ToString();
    }

    // 의존 깊이(레이어)로 열 배치
    void Layout()
    {
        var incoming = new Dictionary<string, List<string>>();
        foreach (var n in nodes) incoming[n.key] = new List<string>();
        foreach (var e in edges) if (incoming.ContainsKey(e.to)) incoming[e.to].Add(e.from);

        var memo = new Dictionary<string, int>();
        var visiting = new HashSet<string>();
        foreach (var n in nodes) n.depth = Depth(n.key, incoming, memo, visiting);

        var colY = new Dictionary<int, float>();
        foreach (var n in nodes) // 삽입 순서 유지
        {
            if (!colY.ContainsKey(n.depth)) colY[n.depth] = Margin;
            float x = Margin + n.depth * (NodeW + GapX);
            float y = colY[n.depth];
            n.rect = new Rect(x, y, NodeW, NodeH);
            colY[n.depth] = y + NodeH + GapY;
        }
    }

    static int Depth(string key, Dictionary<string, List<string>> incoming, Dictionary<string, int> memo, HashSet<string> visiting)
    {
        if (memo.TryGetValue(key, out int d)) return d;
        if (visiting.Contains(key)) return 0; // 순환 안전장치
        visiting.Add(key);
        int best = 0;
        if (incoming.TryGetValue(key, out var srcs))
            foreach (var src in srcs)
            {
                int cand = Depth(src, incoming, memo, visiting) + 1;
                if (cand > best) best = cand;
            }
        visiting.Remove(key);
        memo[key] = best;
        return best;
    }

    // ------------------------------------------------------------------
    // GUI
    // ------------------------------------------------------------------
    void EnsureStyles()
    {
        if (idStyle != null) return;
        idStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 10, normal = { textColor = Color.white } };
        subStyle = new GUIStyle(EditorStyles.label) { fontSize = 9, wordWrap = true, normal = { textColor = new Color(1f, 1f, 1f, 0.9f) } };
        detailStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, richText = true };
    }

    void OnGUI()
    {
        EnsureStyles();

        var canvasRect = new Rect(0, TopBarH, position.width, position.height - TopBarH - DetailH);

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(canvasRect, BgColor);
            foreach (var e in edges) DrawEdge(canvasRect, e);
            foreach (var n in nodes) DrawNode(canvasRect, n);
        }

        HandleInput(canvasRect);
        DrawTopBar();
        DrawDetailBar();
    }

    Rect Abs(Rect canvasRect, VNode n) =>
        new Rect(canvasRect.x + pan.x + n.rect.x, canvasRect.y + pan.y + n.rect.y, n.rect.width, n.rect.height);

    void DrawNode(Rect canvasRect, VNode n)
    {
        var r = Abs(canvasRect, n);
        if (!r.Overlaps(canvasRect)) return; // 화면 밖 컬링

        Color fill = n.kind == 0 ? QuestionFill : n.kind == 1 ? ContradictionFill : TestimonyFill;
        EditorGUI.DrawRect(r, fill);

        bool sel = n.key == selectedKey;
        DrawBorder(r, sel ? Color.white : new Color(0f, 0f, 0f, 0.6f), sel ? 2f : 1f);

        GUI.Label(new Rect(r.x + 6, r.y + 4, r.width - 12, 15), n.id, idStyle);
        GUI.Label(new Rect(r.x + 6, r.y + 20, r.width - 12, r.height - 24), new GUIContent(Truncate(n.title, 42), n.tooltip), subStyle);
    }

    static void DrawBorder(Rect r, Color c, float t)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    void DrawEdge(Rect canvasRect, VEdge e)
    {
        VNode a, b;
        if (!map.TryGetValue(e.from, out a) || !map.TryGetValue(e.to, out b)) return;
        Rect ra = Abs(canvasRect, a), rb = Abs(canvasRect, b);

        Vector3 p0 = new Vector3(ra.xMax, ra.center.y);
        Vector3 p1 = new Vector3(rb.xMin, rb.center.y);
        // 타깃이 왼쪽에 있으면(역방향) 아래로 살짝 돌리는 대신 그대로 곡선 처리
        Vector3 t0 = p0 + new Vector3(50, 0);
        Vector3 t1 = p1 - new Vector3(50, 0);

        Color c = e.type == 0 ? new Color(0.5f, 0.6f, 0.9f)   // 선행질문
                : e.type == 1 ? new Color(0.85f, 0.7f, 0.3f)  // 선행증언
                : e.type == 2 ? new Color(0.4f, 0.75f, 0.45f) // 획득
                :               new Color(0.85f, 0.4f, 0.4f);  // 선택(모순)
        Handles.DrawBezier(p0, p1, t0, t1, c, null, e.type == 3 ? 3.5f : 2.2f);
    }

    void HandleInput(Rect canvasRect)
    {
        var e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            selectedKey = HitTest(canvasRect, e.mousePosition);
            Repaint();
        }
        else if (e.type == EventType.MouseDrag && (e.button == 0 || e.button == 2))
        {
            pan += e.delta;
            e.Use();
            Repaint();
        }
    }

    string HitTest(Rect canvasRect, Vector2 mouse)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
            if (Abs(canvasRect, nodes[i]).Contains(mouse)) return nodes[i].key;
        return null;
    }

    void DrawTopBar()
    {
        var bar = new Rect(0, 0, position.width, TopBarH);
        EditorGUI.DrawRect(bar, new Color(0.22f, 0.22f, 0.25f));

        bool prevSample = useSample;
        var prevAsset = sourceAsset;

        useSample = GUI.Toggle(new Rect(6, 2, 92, 18), useSample, "샘플 사용", EditorStyles.miniButton);
        using (new EditorGUI.DisabledScope(useSample))
            sourceAsset = (InterrogationCase)EditorGUI.ObjectField(new Rect(102, 2, 230, 18), sourceAsset, typeof(InterrogationCase), false);

        if (GUI.Button(new Rect(340, 2, 66, 18), "새로고침", EditorStyles.miniButton)) Rebuild();
        if (GUI.Button(new Rect(410, 2, 66, 18), "정렬 리셋", EditorStyles.miniButton)) { pan = new Vector2(20, 20); Repaint(); }

        if (prevSample != useSample || prevAsset != sourceAsset) Rebuild();

        // 범례
        DrawSwatch(position.width - 300, "질문", QuestionFill);
        DrawSwatch(position.width - 232, "모순", ContradictionFill);
        DrawSwatch(position.width - 164, "증언", TestimonyFill);
    }

    void DrawSwatch(float x, string label, Color c)
    {
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(new Rect(x, 5, 12, 12), c);
        GUI.Label(new Rect(x + 16, 2, 48, 18), label);
    }

    void DrawDetailBar()
    {
        var bar = new Rect(0, position.height - DetailH, position.width, DetailH);
        EditorGUI.DrawRect(bar, new Color(0.12f, 0.12f, 0.14f));

        string text = "노드를 클릭하면 상세 정보가 여기에 표시됩니다.  ·  빈 곳을 드래그하면 이동합니다.";
        if (selectedKey != null && map.TryGetValue(selectedKey, out var n))
            text = n.tooltip;

        GUI.Label(new Rect(bar.x + 10, bar.y + 4, bar.width - 20, bar.height - 8), text, detailStyle);
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
