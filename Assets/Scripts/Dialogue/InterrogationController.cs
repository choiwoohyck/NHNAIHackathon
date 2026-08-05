using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 취조 흐름 전체를 진행시키는 컨트롤러.
//
// 질문 순서를 하드코딩하지 않고, 방향 그래프(CaseGraph) + 진행 상태(CaseProgress)로 굴린다.
//   - 어떤 질문을 보여줄지 : graph.GetAvailableQuestions(용의자, 진행상태, 선택한 증언)
//   - 질문을 하면          : 대사를 출력하고 증언을 확보 → 진행 상태 갱신 → 새 질문 자동 해금
//   - 모순 질문            : 기록지에서 올바른 증언 문장을 선택했을 때만 목록에 나타남
//
// 사용법: 빈 GameObject 하나에 이 스크립트만 붙이면 DialogueManager / RecordBookController가
// 자동으로 추가되고, 씬에 필요한 UI가 런타임에 전부 생성된다.
public class InterrogationController : MonoBehaviour
{
    [Header("사건 데이터")]
    [Tooltip("인스펙터에서 만든 취조 사건(ScriptableObject). 비워두면 코드 샘플(SampleCaseGraph)로 자동 대체된다. 사건 선택 화면에서 진입하면 그쪽 선택이 우선한다.")]
    [SerializeField] InterrogationCase caseAsset;

    [Header("씬 전환")]
    [Tooltip("판결 제출 시 이동할 엔딩 씬 이름(Build Settings에 등록돼 있어야 이동, 아니면 패널 내 텍스트로 표시).")]
    [SerializeField] string endingSceneName = "Ending";

    DialogueManager dialogueManager;
    RecordBookController recordBook;
    VerdictController verdict;

    CaseGraph graph;
    CaseProgress progress;
    readonly Dictionary<string, SuspectSession> sessions = new Dictionary<string, SuspectSession>();

    string currentSuspectId;
    SuspectSession CurrentSession =>
        string.IsNullOrEmpty(currentSuspectId) ? null : sessions[currentSuspectId];

    void Awake()
    {
        dialogueManager = GetComponent<DialogueManager>();
        if (dialogueManager == null) dialogueManager = gameObject.AddComponent<DialogueManager>();

        recordBook = GetComponent<RecordBookController>();
        if (recordBook == null) recordBook = gameObject.AddComponent<RecordBookController>();

        verdict = GetComponent<VerdictController>();
        if (verdict == null) verdict = gameObject.AddComponent<VerdictController>();
        verdict.endingSceneName = endingSceneName;

        recordBook.OnStatementClicked = OnRecordSelected;
    }

    void Start()
    {
        // 그래프 데이터 로드: 사건 선택 화면에서 고른 사건 > 인스펙터 지정 사건 > 코드 샘플 순.
        var chosen = GameSession.SelectedCase != null ? GameSession.SelectedCase : caseAsset;
        graph = chosen != null ? chosen.BuildGraph() : SampleCaseGraph.Build();
        progress = new CaseProgress();
        GameSession.ClearVerdict();   // 새 취조 시작 시 이전 판결 상태 초기화

        // authored 데이터에 잘못된 참조/순환이 없는지 점검.
        foreach (var issue in graph.Validate()) Debug.LogWarning("[CaseGraph] " + issue);

        foreach (var s in graph.Suspects)
            sessions[s.id] = new SuspectSession(s.id, s.suspectName, s.occupation);

        BuildSuspectBar();
        ShowBriefing();
    }

    void ShowBriefing()
    {
        if (graph == null || string.IsNullOrEmpty(graph.briefing)) return;
        var lines = new List<DialogueLine>
        {
            new DialogueLine("사건 브리핑", graph.caseTitle),
            new DialogueLine("사건 브리핑", graph.briefing),
        };
        dialogueManager.ShowLines(lines, null);
    }

    // ------------------------------------------------------------------
    // 상단 컨트롤 바: 용의자 호출 / 취조 중지 / 취조 종료 / 기록지 보기
    // ------------------------------------------------------------------
    void BuildSuspectBar()
    {
        var canvas = DialogueUIUtil.CreateCanvas("SuspectBarCanvas", 5);

        var bar = DialogueUIUtil.CreatePanel(canvas.transform, "SuspectBar", new Color(0f, 0f, 0f, 0.5f));
        DialogueUIUtil.Stretch(bar, new Vector2(0f, 0.93f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.padding = new RectOffset(10, 10, 5, 5);
        layout.childForceExpandWidth = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        foreach (var s in graph.Suspects)
        {
            var id = s.id;
            var btn = DialogueUIUtil.CreateButton(bar, "Call_" + id, s.suspectName + " 호출", new Color(0.2f, 0.2f, 0.2f, 0.8f));
            btn.gameObject.AddComponent<LayoutElement>().preferredWidth = 160;
            btn.onClick.AddListener(() => CallSuspect(id));
        }

        var stopBtn = DialogueUIUtil.CreateButton(bar, "StopBtn", "취조 중지", new Color(0.4f, 0.2f, 0.1f, 0.8f));
        stopBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
        stopBtn.onClick.AddListener(PauseCurrentSuspect);

        var endBtn = DialogueUIUtil.CreateButton(bar, "EndBtn", "취조 종료", new Color(0.1f, 0.3f, 0.1f, 0.8f));
        endBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
        endBtn.onClick.AddListener(EndCurrentSuspect);

        var bookBtn = DialogueUIUtil.CreateButton(bar, "BookBtn", "기록지 보기", new Color(0.1f, 0.1f, 0.4f, 0.8f));
        bookBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
        bookBtn.onClick.AddListener(() => recordBook.ToggleVisible());

        var verdictBtn = DialogueUIUtil.CreateButton(bar, "VerdictBtn", "사건 판결", new Color(0.45f, 0.1f, 0.35f, 0.9f));
        verdictBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
        verdictBtn.onClick.AddListener(OpenVerdict);
    }

    // 모든 취조를 마친 뒤 최종 판결 화면을 연다.
    // (제안서대로 '필수 용의자 전원 종료' 시점에만 열고 싶으면 아래 가드의 주석을 해제하면 된다.)
    void OpenVerdict()
    {
        // if (progress.completedSuspectIds.Count < graph.Suspects.Count) return;

        if (CurrentSession != null)
        {
            recordBook.AddOrUpdateSheet(CurrentSession);
            currentSuspectId = null;
        }
        dialogueManager.ResetToIdle();
        recordBook.Hide();
        verdict.Show(graph);
    }

    // ------------------------------------------------------------------
    // 취조 진행 로직
    // ------------------------------------------------------------------
    void CallSuspect(string suspectId)
    {
        if (!sessions.ContainsKey(suspectId)) return;
        dialogueManager.ResetToIdle();
        currentSuspectId = suspectId;
        progress.ClearSelection();                        // 용의자 전환 시 기록지 선택 초기화
        recordBook.SetSelected(progress.selectedTestimonyIds);
        PresentChoices();
    }

    // 현재 진행 상태 + 선택한 증언을 그래프에 물어 '지금 가능한 질문'만 보여준다.
    void PresentChoices()
    {
        if (string.IsNullOrEmpty(currentSuspectId)) return;
        var available = graph.GetAvailableQuestions(currentSuspectId, progress);
        dialogueManager.ShowChoices(available, OnQuestionSelected);
    }

    void OnQuestionSelected(QuestionNode node)
    {
        progress.MarkAsked(node.id);

        // 모순 질문은 사용되면 선택한 근거(증언)를 소비한다.
        bool consumedSelection = node.requiredSelectedTestimonyIds != null && node.requiredSelectedTestimonyIds.Count > 0;

        dialogueManager.ShowLines(node.lines, () =>
        {
            GrantTestimonies(node);

            if (consumedSelection)
            {
                progress.ClearSelection();
                recordBook.SetSelected(progress.selectedTestimonyIds);
            }

            PresentChoices();
        });
    }

    // 질문의 결과로 증언을 확보한다: 진행 상태에 기록 + 해당 용의자의 기록지 문장으로 추가.
    void GrantTestimonies(QuestionNode node)
    {
        if (node.grantTestimonyIds == null) return;

        foreach (var tid in node.grantTestimonyIds)
        {
            var t = graph.GetTestimony(tid);
            if (t == null) continue;

            progress.AddTestimony(tid);

            SuspectSession owner;
            if (!sessions.TryGetValue(t.ownerSuspectId, out owner)) owner = CurrentSession;
            if (owner == null) continue;

            owner.AddStatement(new StatementRecord(t.id, t.ownerSuspectId, t.ownerSuspectName, t.text));
            // 이미 책상에 나와 있는 기록지라면 즉시 반영(없으면 중지/종료 때 생성).
            recordBook.UpdateSheetIfExists(owner);
        }
    }

    void PauseCurrentSuspect()
    {
        if (CurrentSession == null) return;
        dialogueManager.ResetToIdle();
        recordBook.AddOrUpdateSheet(CurrentSession);   // 확보한 진술을 기록지로 생성
        currentSuspectId = null;
    }

    void EndCurrentSuspect()
    {
        if (CurrentSession == null) return;
        CurrentSession.completed = true;
        progress.MarkSuspectCompleted(currentSuspectId);
        dialogueManager.ResetToIdle();
        recordBook.AddOrUpdateSheet(CurrentSession);
        currentSuspectId = null;
    }

    static readonly Color StatusHint = new Color(0.82f, 0.82f, 0.88f, 1f);
    static readonly Color StatusOk = new Color(0.5f, 0.9f, 0.55f, 1f);
    static readonly Color StatusBad = new Color(0.95f, 0.55f, 0.55f, 1f);

    // ------------------------------------------------------------------
    // 기록지 문장 선택 → '서로 모순되는 두 진술'을 고르면 모순 질문 해금.
    // 한쪽만 누른 상태에서는 아직 판정하지 않고, 두 번째 진술까지 골라야 판정한다.
    // ------------------------------------------------------------------
    void OnRecordSelected(StatementRecord rec)
    {
        if (rec == null) return;

        // 선택 토글(최대 2개) + 강조 갱신
        progress.ToggleSelection(rec.recordId, 2);
        recordBook.SetSelected(progress.selectedTestimonyIds);

        int count = progress.selectedTestimonyIds.Count;

        // 취조 중이 아니면 판정하지 않고 강조만(그냥 기록 열람).
        if (CurrentSession == null)
        {
            recordBook.SetStatus("용의자를 호출한 뒤 모순되는 두 진술을 제시하세요.", StatusHint);
            return;
        }

        // 두 진술이 모이기 전에는 안내만.
        if (count < 2)
        {
            recordBook.SetStatus(count == 1
                ? "서로 모순되는 진술을 하나 더 선택하세요.  (1/2)"
                : "모순되는 두 진술을 선택하세요.  (0/2)", StatusHint);
            return;
        }

        // 두 진술 선택 완료 → 모순 판정
        if (graph.SelectionUnlocksContradiction(currentSuspectId, progress))
        {
            // 올바른 모순 → 모순 질문이 목록에 나타난다(선택 상태 유지).
            recordBook.SetStatus("모순 발견 — 추궁할 수 있습니다.", StatusOk);
            recordBook.Hide();
            PresentChoices();
        }
        else
        {
            // 서로 모순되지 않는 두 진술 → 월터가 지적하고 선택을 해제한다(기록지는 열어 둠).
            progress.ClearSelection();
            recordBook.SetSelected(progress.selectedTestimonyIds);
            recordBook.SetStatus("월터: 이건 모순이 아니다.", StatusBad);
        }
    }
}
