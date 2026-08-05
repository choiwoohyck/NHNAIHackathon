using System.Collections.Generic;
using CulpritDetection;

// 사건 전체의 질문/증언 방향 그래프(authored). 해금 판정 로직의 중심.
//
// 겉으로는 "질문 트리"처럼 후속 질문이 열리지만, 내부적으로는 각 질문이 여러 선행 질문/증언을
// 조건으로 가질 수 있는 방향 비순환 그래프(DAG)다. 판정 로직만 담고 UI에 의존하지 않아
// 재사용/테스트가 쉽고, ScriptableObject/JSON 로더로 채워 넣기도 좋다.
// (질문 노드에는 인스펙터 드롭다운용 마커 애트리뷰트가 붙어 있어 UnityEngine을 참조한다.)
public class CaseGraph
{
    public string caseId;
    public string caseTitle;
    public string briefing;

    // ----- 최종 판결 데이터 -----
    public string culpritSuspectId;                 // 정답 용의자 id
    public float verdictTypoThreshold = 0.8f;       // 사건 설명 필드 동의어/오타 허용도
    public int minimumCorrectFields = 0;            // 성공에 필요한 최소 정답 필드 수 (0 이하 = 전부)
    public string verdictTemplate;                  // "{동기}가 {대상}을..." 빈칸 문장(선택)
    public List<CaseField> verdictFields = new List<CaseField>();  // 동기/수단/대상/목적 등

    readonly List<SuspectData> suspects = new List<SuspectData>();
    readonly List<Testimony> testimonyList = new List<Testimony>();
    readonly Dictionary<string, SuspectData> suspectsById = new Dictionary<string, SuspectData>();
    readonly Dictionary<string, QuestionNode> nodesById = new Dictionary<string, QuestionNode>();
    readonly Dictionary<string, Testimony> testimoniesById = new Dictionary<string, Testimony>();

    public IReadOnlyList<SuspectData> Suspects => suspects;
    public IReadOnlyList<Testimony> Testimonies => testimonyList;

    // ------------------------------------------------------------------
    // 구성
    // ------------------------------------------------------------------
    public void AddSuspect(SuspectData suspect)
    {
        if (suspect == null || string.IsNullOrEmpty(suspect.id)) return;
        suspects.Add(suspect);
        suspectsById[suspect.id] = suspect;
        foreach (var q in suspect.questions)
            if (q != null && !string.IsNullOrEmpty(q.id)) nodesById[q.id] = q;
    }

    public void AddTestimony(Testimony testimony)
    {
        if (testimony == null || string.IsNullOrEmpty(testimony.id)) return;
        if (!testimoniesById.ContainsKey(testimony.id)) testimonyList.Add(testimony);
        testimoniesById[testimony.id] = testimony;
    }

    public SuspectData GetSuspect(string id)
    {
        SuspectData s; return suspectsById.TryGetValue(id ?? "", out s) ? s : null;
    }

    public QuestionNode GetNode(string id)
    {
        QuestionNode n; return nodesById.TryGetValue(id ?? "", out n) ? n : null;
    }

    public Testimony GetTestimony(string id)
    {
        Testimony t; return testimoniesById.TryGetValue(id ?? "", out t) ? t : null;
    }

    // ------------------------------------------------------------------
    // 해금 판정
    // ------------------------------------------------------------------

    /// <summary>지금 이 노드를 물어볼 수 있는가? (그래프 조건 + 런타임 상태로 판정)</summary>
    public bool IsAvailable(QuestionNode node, CaseProgress p)
    {
        if (node == null || p == null) return false;

        // 이미 물어봤고 반복 불가면 목록에서 사라진다.
        if (p.IsAsked(node.id) && !node.repeatable) return false;

        // 모순 질문은 일반 질문 목록에 나타나지 않는다 — 기록지에서 증언 2개를 선택해
        // FindContradictionNode로 직접 판정하고 곧바로 추궁한다.
        if (node.RequiredSelectedCount() > 0) return false;

        return PrerequisitesMet(node, p);
    }

    /// <summary>해당 용의자에게 지금 물어볼 수 있는 질문 목록(authored 순서 유지).</summary>
    public List<QuestionNode> GetAvailableQuestions(string suspectId, CaseProgress p)
    {
        var result = new List<QuestionNode>();
        var s = GetSuspect(suspectId);
        if (s == null) return result;
        foreach (var node in s.questions)
            if (IsAvailable(node, p))
                result.Add(node);
        return result;
    }

    /// <summary>
    /// 지금 선택된 증언 조합(최대 2개)이 어느 용의자의 모순 질문과 정확히 일치하는지 찾는다(순서 무관).
    /// 일치하면 그 질문 노드를 돌려주고 owner에 소속 용의자 id를 담는다. 없으면 null.
    /// </summary>
    public QuestionNode FindContradictionNode(CaseProgress p, out string ownerSuspectId)
    {
        ownerSuspectId = null;
        if (p == null || p.SelectedCount == 0) return null;

        foreach (var s in suspects)
        {
            foreach (var node in s.questions)
            {
                if (node.RequiredSelectedCount() != p.SelectedCount) continue;
                if (p.IsAsked(node.id) && !node.repeatable) continue;
                if (!SameIds(node.requiredSelectedTestimonyIds, p.selectedTestimonyIds)) continue;
                if (!PrerequisitesMet(node, p)) continue;

                ownerSuspectId = s.id;
                return node;
            }
        }
        return null;
    }

    static bool SameIds(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var id in a) if (!b.Contains(id)) return false;
        return true;
    }

    /// <summary>선행 '질문'/'증언' 조건(모순 여부와 무관)만 판정한다.</summary>
    bool PrerequisitesMet(QuestionNode node, CaseProgress p)
    {
        bool hasQ = node.requiredQuestionIds != null && node.requiredQuestionIds.Count > 0;
        bool hasT = node.requiredTestimonyIds != null && node.requiredTestimonyIds.Count > 0;

        // 선행 조건이 아예 없으면 바로 사용 가능.
        if (!hasQ && !hasT) return true;

        if (node.requireAll)
        {
            // AND: 모든 선행 질문 + 모든 선행 증언 충족
            if (hasQ) foreach (var id in node.requiredQuestionIds) if (!p.IsAsked(id)) return false;
            if (hasT) foreach (var id in node.requiredTestimonyIds) if (!p.HasTestimony(id)) return false;
            return true;
        }

        // OR: 선행 질문/증언 중 하나라도 충족되면 열림
        if (hasQ) foreach (var id in node.requiredQuestionIds) if (p.IsAsked(id)) return true;
        if (hasT) foreach (var id in node.requiredTestimonyIds) if (p.HasTestimony(id)) return true;
        return false;
    }

    // ------------------------------------------------------------------
    // 데이터 검증 (authored 실수 잡기용) — 없는 id 참조 / 순환(DAG 위반) 경고
    // ------------------------------------------------------------------
    public List<string> Validate()
    {
        var issues = new List<string>();
        foreach (var kv in nodesById)
        {
            var node = kv.Value;
            foreach (var id in node.requiredQuestionIds)
                if (!nodesById.ContainsKey(id)) issues.Add("[" + node.id + "] 선행 질문 '" + id + "' 을(를) 찾을 수 없음");
            foreach (var id in node.requiredTestimonyIds)
                if (!testimoniesById.ContainsKey(id)) issues.Add("[" + node.id + "] 선행 증언 '" + id + "' 을(를) 찾을 수 없음");
            foreach (var id in node.requiredSelectedTestimonyIds)
                if (!testimoniesById.ContainsKey(id)) issues.Add("[" + node.id + "] 선택 증언 '" + id + "' 을(를) 찾을 수 없음");
            foreach (var id in node.grantTestimonyIds)
                if (!testimoniesById.ContainsKey(id)) issues.Add("[" + node.id + "] 획득 증언 '" + id + "' 을(를) 찾을 수 없음");
        }
        DetectCycles(issues);
        return issues;
    }

    void DetectCycles(List<string> issues)
    {
        var state = new Dictionary<string, int>(); // 0=미방문, 1=방문중, 2=완료
        foreach (var id in nodesById.Keys)
            if (HasCycle(id, state))
            {
                issues.Add("질문 선행 관계에 순환이 있습니다 (…" + id + "…) — DAG가 아님");
                break;
            }
    }

    bool HasCycle(string id, Dictionary<string, int> state)
    {
        int st;
        if (state.TryGetValue(id, out st))
        {
            if (st == 1) return true;   // 방문 중인 노드를 다시 만남 → 순환
            if (st == 2) return false;
        }
        var node = GetNode(id);
        if (node == null) { state[id] = 2; return false; }

        state[id] = 1;
        foreach (var pre in node.requiredQuestionIds)
            if (HasCycle(pre, state)) return true;
        state[id] = 2;
        return false;
    }
}
