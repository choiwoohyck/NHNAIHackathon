using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 취조 흐름 전체를 진행시키는 컨트롤러.
//
// 질문 순서를 하드코딩하지 않고, 방향 그래프(CaseGraph) + 진행 상태(CaseProgress)로 굴린다.
//   - 어떤 질문을 보여줄지 : graph.GetAvailableQuestions(용의자, 진행상태)
//   - 질문을 하면          : 대사를 출력하고 증언을 확보 → 진행 상태 갱신 → 새 질문 자동 해금
//   - 모순 질문            : 기록지에서 서로 모순되는 증언 문장 2개를 선택했을 때만 목록에 나타남
//
// 사용법: 빈 GameObject 하나에 이 스크립트만 붙이면 DialogueManager / RecordBookController가
// 자동으로 추가되고, 씬에 필요한 UI가 런타임에 전부 생성된다.
public class InterrogationController : MonoBehaviour
{
    [Header("사건 데이터")]
    [Tooltip("인스펙터에서 만든 취조 사건(ScriptableObject). 비워두면 코드 샘플(SampleCaseGraph)로 자동 대체된다.")]
    [SerializeField] InterrogationCase caseAsset;

    DialogueManager dialogueManager;
    RecordBookController recordBook;
    VerdictController verdict;

    Button bookBtn;    // 기록지 보기 버튼 (대화 중에는 비활성)
    Text bookLabel;

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

        recordBook.OnStatementClicked = OnRecordSelected;
    }

    // 대화(대사 출력) 중에는 기록지 보기를 막는다: 버튼을 회색+클릭 불가로 만들고,
    // 이미 열려 있던 기록지도 닫아 겹침/오작동을 방지한다.
    void Update()
    {
        if (dialogueManager == null || bookBtn == null) return;

        bool canView = dialogueManager.State != DialogueState.Speaking;
        if (bookBtn.interactable != canView)
        {
            bookBtn.interactable = canView;
            if (bookLabel != null)
                bookLabel.color = new Color(1f, 1f, 1f, canView ? 1f : 0.35f);
        }
        if (!canView && recordBook != null && recordBook.IsVisible)
            recordBook.Hide();
    }

    // 버튼 콜백(만약을 대비한 이중 방어: 대화 중이면 무시).
    void ToggleRecordBook()
    {
        if (dialogueManager != null && dialogueManager.State == DialogueState.Speaking) return;
        recordBook.ToggleVisible();
    }

    void Start()
    {
        // 그래프 데이터 로드: 인스펙터에 사건 에셋이 지정돼 있으면 그것을, 없으면 코드 샘플을 사용.
        graph = caseAsset != null ? caseAsset.BuildGraph() : SampleCaseGraph.Build();
        progress = new CaseProgress();

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

        bookBtn = DialogueUIUtil.CreateButton(bar, "BookBtn", "기록지 보기", new Color(0.1f, 0.1f, 0.4f, 0.8f));
        bookBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
        bookBtn.onClick.AddListener(ToggleRecordBook);
        bookLabel = bookBtn.GetComponentInChildren<Text>();
        var bookColors = bookBtn.colors;                       // 비활성 시 뚜렷하게 어둡게
        bookColors.disabledColor = new Color(0.25f, 0.25f, 0.3f, 0.6f);
        bookBtn.colors = bookColors;

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

        // 모순 질문은 사용되면 선택한 근거(증언들)를 소비한다.
        bool consumedSelection = node.RequiredSelectedCount() > 0;

        dialogueManager.ShowLines(node.lines, () =>
        {
            GrantTestimonies(node);

            if (consumedSelection)
            {
                progress.ClearSelected();
                recordBook.SetSelected(null);
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
            // 확보 즉시 기록지에 반영한다. (두 문장 모순 지목을 위해 취조 중에도 선택 가능해야 함)
            recordBook.AddOrUpdateSheet(owner);
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

    // ------------------------------------------------------------------
    // 기록지 문장 선택 → 모순 질문 해금 / "이건 모순이 아니다" 판정
    // ------------------------------------------------------------------
    void OnRecordSelected(StatementRecord rec)
    {
        if (rec == null) return;
        string sel = rec.recordId; // recordId == 증언 id

        // 이미 선택된 문장을 다시 누르면 해제.
        if (progress.IsSelected(sel))
        {
            progress.selectedTestimonyIds.Remove(sel);
            recordBook.SetSelected(progress.selectedTestimonyIds);
            return;
        }

        // 새 문장 선택(최대 2개, 가득 차 있으면 새로 시작).
        progress.ToggleSelected(sel);
        recordBook.SetSelected(progress.selectedTestimonyIds);

        // 아직 두 개가 안 모였으면 대기.
        if (progress.SelectedCount < CaseProgress.MaxSelected) return;

        // 두 문장이 모였다 → 취조 순서/현재 대상과 무관하게 모순 여부를 판정한다.
        string owner;
        var node = graph.FindContradictionNode(progress, out owner);
        recordBook.Hide();

        if (node != null)
        {
            // 모순 성립 → 해당 용의자에게 곧바로 추궁(대상이 아니었다면 자동 전환).
            currentSuspectId = owner;
            OnQuestionSelected(node);   // 대사 출력 → 증언 확보 → 선택 소비 → 목록 갱신
            return;
        }

        // 모순 아님 → 지적하고 선택 해제.
        Debug.Log("[모순] 성립 안 됨 — 선택=" + string.Join(",", progress.selectedTestimonyIds));
        progress.ClearSelected();
        recordBook.SetSelected(progress.selectedTestimonyIds);
        dialogueManager.ShowLines(
            new List<DialogueLine> { new DialogueLine("월터", "이 둘은 모순이 아니다.") },
            PresentChoices);
    }
}
