using System.Collections.Generic;
using UnityEngine;
using CulpritDetection;

// 취조 사건 하나를 '데이터'로 담는 ScriptableObject.
// 코드(SampleCaseGraph) 대신 인스펙터에서 사건을 작성/편집할 수 있게 해준다.
//
// 만드는 법:
//   1) Project 창 우클릭 → Create → Editor0 → Interrogation Case  (빈 사건)
//   2) Tools → Editor0 → Create Sample Interrogation Case         (샘플이 채워진 사건)
//
// 런타임에는 BuildGraph()로 CaseGraph(방향 그래프 엔진)를 만들어 쓴다.
// 진행 상태(무엇을 물어봤는지 등)는 CaseProgress가 따로 관리하므로, 이 에셋은 순수 데이터로 유지된다.
// 필드가 전부 직렬화 가능한 QuestionNode/Testimony/SuspectData라 인스펙터에 그대로 펼쳐진다.
[CreateAssetMenu(fileName = "NewInterrogationCase", menuName = "Editor0/Interrogation Case", order = 1)]
public class InterrogationCase : ScriptableObject
{
    [Header("사건 정보")]
    public string caseId = "CASE_ID";
    public string caseTitle = "사건 제목";
    [TextArea(2, 5)] public string briefing;

    [Header("증언  (id를 질문의 Grant / 해금 조건에서 참조)")]
    public List<Testimony> testimonies = new List<Testimony>();

    [Header("용의자 및 질문 노드")]
    public List<SuspectData> suspects = new List<SuspectData>();

    [Header("최종 판결")]
    [Tooltip("정답 용의자 id (suspects 중 하나의 id와 일치해야 함).")]
    [SuspectId] public string culpritSuspectId;
    [Range(0f, 1f)] public float verdictTypoThreshold = 0.8f;
    [Tooltip("성공에 필요한 최소 정답 필드 수. 0이면 모든 필드가 정답이어야 함.")]
    public int minimumCorrectFields = 0;
    [Tooltip("빈칸 채우기 문장. {필드label} 위치에 입력칸이 생김. 예: {동기}가 {대상}을 노렸다. 비우면 세로 나열.")]
    [TextArea(2, 4)] public string verdictTemplate;
    [Tooltip("사건 설명 판정 필드(동기/수단/대상/목적 등). 각 필드는 정답+동의어를 가진다.")]
    public List<CaseField> verdictFields = new List<CaseField>();

    /// <summary>인스펙터 데이터로부터 런타임 방향 그래프를 만든다.</summary>
    public CaseGraph BuildGraph()
    {
        var g = new CaseGraph
        {
            caseId = caseId,
            caseTitle = caseTitle,
            briefing = briefing,
            culpritSuspectId = culpritSuspectId,
            verdictTypoThreshold = verdictTypoThreshold,
            minimumCorrectFields = minimumCorrectFields,
            verdictTemplate = verdictTemplate,
            verdictFields = verdictFields != null ? new List<CaseField>(verdictFields) : new List<CaseField>()
        };
        if (testimonies != null) foreach (var t in testimonies) g.AddTestimony(t);
        if (suspects != null) foreach (var s in suspects) g.AddSuspect(s);
        return g;
    }

    /// <summary>없는 id 참조/순환 등을 점검한다. 문제가 없으면 빈 목록.</summary>
    public List<string> Validate() => BuildGraph().Validate();
}
