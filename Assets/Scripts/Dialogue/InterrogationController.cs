using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 취조 흐름 전체를 진행시키는 컨트롤러.
//
// 질문 순서를 하드코딩하지 않고, 방향 그래프(CaseGraph) + 진행 상태(CaseProgress)로 굴린다.
//   - 어떤 질문을 보여줄지 : graph.GetAvailableQuestions(용의자, 진행상태)  — 모순 질문은 이 목록에 없음
//   - 질문을 하면          : 대사를 출력하고 증언을 확보 → 진행 상태 갱신 → 기록지 즉시 갱신 → 새 질문 자동 해금
//   - 모순 질문            : 기록지에서 증언 문장 2개를 선택하면 graph.FindContradictionNode로 즉시 판정해,
//                           성립하면 근거의 소유 용의자에게 곧바로 전환해 추궁한다
//
// 사용법: 빈 GameObject 하나에 이 스크립트만 붙이면 DialogueManager / RecordBookController가
// 자동으로 추가되고, 씬에 필요한 UI가 런타임에 전부 생성된다.
public class InterrogationController : MonoBehaviour
{
    [Header("사건 데이터")]
    [Tooltip("이 씬을 단독 실행할 때 쓰는 기본 사건(ScriptableObject).\n" +
             "사건 선택 화면을 거쳐 들어오면 거기서 고른 사건(GameSession.SelectedCase)이 우선한다.\n" +
             "둘 다 없으면 코드 샘플(SampleCaseGraph)로 자동 대체된다.")]
    [SerializeField] InterrogationCase caseAsset;

    [Header("커스텀 UI 참조 (선택, 비워두면 자동 생성)")]
    [Tooltip("비워두면 런타임에 자동 생성된다. 지정하면 해당 오브젝트를 그대로 사용한다.\n" +
             "취조 종료/판결 버튼이 화면에 배치된다(배경 바 없음). 기록지 보기는 용의자별 아이콘 버튼으로 옮겨갔고, 용의자 호출은 전화기 팝업으로 이동했다.")]
    [SerializeField] Canvas actionButtonsCanvasOverride;
    [Tooltip("버튼들이 배치될 컨테이너(배경 없이 레이아웃만). 비워두면 자동 생성된다.")]
    [SerializeField] RectTransform actionButtonsContainerOverride;
    [SerializeField] Button endBtnOverride;
    [SerializeField] Button verdictBtnOverride;
    [SerializeField] Button logBtnOverride;

    [Header("기록지 아이콘 버튼 (최대 3명, 진술 완료 시 활성화)")]
    [Tooltip("용의자별 기록지 아이콘 버튼. 인덱스 0/1/2가 사건의 용의자 순서(graph.Suspects)와 대응한다.\n" +
             "씬에 미리 배치해두고 비활성 상태로 시작, 해당 용의자의 취조가 끝나면 활성화한다. 클릭하면 기록지 보기가 열린다.")]
    [SerializeField] Button[] recordSheetButtonOverrides = new Button[3];

    [Header("인물 호출 연출")]
    [Tooltip("전화로 용의자를 호출했을 때 암전/등장 연출을 재생할 컨트롤러.")]
    [SerializeField] InterrogationLightFlicker lightFlicker;

    DialogueManager dialogueManager;
    RecordBookController recordBook;
    VerdictController verdict;
    PhoneCallController phoneCall;
    DialogueLogController dialogueLog;

    CaseGraph graph;
    CaseProgress progress;
    readonly Dictionary<string, SuspectSession> sessions = new Dictionary<string, SuspectSession>();

    string currentSuspectId;
    SuspectSession CurrentSession =>
        string.IsNullOrEmpty(currentSuspectId) ? null : sessions[currentSuspectId];

    // ------------------------------------------------------------------
    // 진행 알림 (튜토리얼 등 관전자용). 게임 로직은 이 이벤트에 의존하지 않는다.
    // ------------------------------------------------------------------
    public event System.Action<string> SuspectCalled;        // 용의자 id
    public event System.Action<string> SuspectEnded;         // 용의자 id
    public event System.Action<string> TestimonyGranted;     // 증언 id
    public event System.Action<string> StatementSelected;    // 기록지에서 고른 증언 id
    public event System.Action<QuestionNode> ContradictionResolved;
    public event System.Action VerdictOpened;

    /// <summary>현재 진행 중인 사건 그래프. 튜토리얼이 안내할 경로를 짤 때 읽는다.</summary>
    public CaseGraph Graph => graph;

    /// <summary>해당 용의자의 기록지 아이콘 버튼(없으면 null). 튜토리얼이 하이라이트 대상으로 쓴다.</summary>
    public Button RecordSheetButtonFor(string suspectId)
    {
        if (graph == null || string.IsNullOrEmpty(suspectId)) return null;
        for (int i = 0; i < graph.Suspects.Count; i++)
        {
            if (graph.Suspects[i].id != suspectId) continue;
            return i < recordSheetButtonOverrides.Length ? recordSheetButtonOverrides[i] : null;
        }
        return null;
    }

    void Awake()
    {
        dialogueManager = GetComponent<DialogueManager>();
        if (dialogueManager == null) dialogueManager = gameObject.AddComponent<DialogueManager>();

        recordBook = GetComponent<RecordBookController>();
        if (recordBook == null) recordBook = gameObject.AddComponent<RecordBookController>();

        verdict = GetComponent<VerdictController>();
        if (verdict == null) verdict = gameObject.AddComponent<VerdictController>();

        phoneCall = GetComponent<PhoneCallController>();
        if (phoneCall == null) phoneCall = gameObject.AddComponent<PhoneCallController>();

        dialogueLog = GetComponent<DialogueLogController>();
        if (dialogueLog == null) dialogueLog = gameObject.AddComponent<DialogueLogController>();

        recordBook.OnStatementClicked = OnRecordSelected;
    }

    void Start()
    {
        // 그래프 데이터 로드. 우선순위:
        //   1) 사건 선택 화면에서 고른 사건(GameSession.SelectedCase)
        //   2) 인스펙터의 기본 사건(caseAsset) — 이 씬만 단독 실행할 때
        //   3) 코드 샘플(SampleCaseGraph)
        var selected = GameSession.SelectedCase != null ? GameSession.SelectedCase : caseAsset;
        graph = selected != null ? selected.BuildGraph() : SampleCaseGraph.Build();

        // 사건을 새로 시작하는 시점이므로 이전 판결 결과를 지운다(재수사 시 결과가 섞이지 않게).
        GameSession.ClearVerdict();

        progress = new CaseProgress();

        // authored 데이터에 잘못된 참조/순환이 없는지 점검.
        foreach (var issue in graph.Validate()) Debug.LogWarning("[CaseGraph] " + issue);

        foreach (var s in graph.Suspects)
            sessions[s.id] = new SuspectSession(s.id, s.suspectName, s.occupation);

        BuildActionButtons();
        WireRecordSheetButtons();
        phoneCall.SetSuspects(graph.Suspects, CallSuspect);
        ShowBriefing();

        if (GameSession.TutorialMode) StartTutorial();
    }

    // 튜토리얼은 별도 오브젝트로 띄운다(끝나면 자기 자신만 정리하고 사라지도록).
    void StartTutorial()
    {
        var go = new GameObject("Tutorial");
        go.AddComponent<TutorialController>().Begin(this, phoneCall, recordBook);
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
    // 화면에 떠 있는 컨트롤 버튼: 취조 종료 / 사건 판결
    // (배경 바 없이 버튼만 배치한다. 용의자 호출은 PhoneCallController의 전화기 팝업으로 옮겨졌다.)
    // ------------------------------------------------------------------
    void BuildActionButtons()
    {
        var canvas = actionButtonsCanvasOverride != null ? actionButtonsCanvasOverride : DialogueUIUtil.CreateCanvas("ActionButtonsCanvas", 5);

        RectTransform container;
        if (actionButtonsContainerOverride != null)
        {
            container = actionButtonsContainerOverride;
        }
        else
        {
            var go = new GameObject("ActionButtons", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            container = go.GetComponent<RectTransform>();
            DialogueUIUtil.Stretch(container, new Vector2(0f, 0.93f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 5, 5);
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childForceExpandWidth = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
        }

        var endBtn = endBtnOverride != null ? endBtnOverride : DialogueUIUtil.CreateButton(container, "EndBtn", "취조 종료", new Color(0.1f, 0.3f, 0.1f, 0.8f));
        if (endBtnOverride == null) endBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
        endBtn.onClick.AddListener(EndCurrentSuspect);

        var verdictBtn = verdictBtnOverride != null ? verdictBtnOverride : DialogueUIUtil.CreateButton(container, "VerdictBtn", "사건 판결", new Color(0.45f, 0.1f, 0.35f, 0.9f));
        if (verdictBtnOverride == null) verdictBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
        verdictBtn.onClick.AddListener(OpenVerdict);

        var logBtn = logBtnOverride != null ? logBtnOverride : DialogueUIUtil.CreateButton(container, "LogBtn", "대사 로그 보기", new Color(0.15f, 0.25f, 0.25f, 0.85f));
        if (logBtnOverride == null) logBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 150;
        logBtn.onClick.AddListener(() => dialogueLog.ToggleVisible());
    }

    // 용의자별 기록지 아이콘 버튼 클릭 시 기록지 보기를 연다(기존 "기록지 보기" 버튼과 동일한 동작).
    void WireRecordSheetButtons()
    {
        foreach (var btn in recordSheetButtonOverrides)
        {
            if (btn != null) btn.onClick.AddListener(() => recordBook.ToggleVisible());
        }
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
        VerdictOpened?.Invoke();
    }

    // ------------------------------------------------------------------
    // 취조 진행 로직
    // ------------------------------------------------------------------
    void CallSuspect(string suspectId)
    {
        if (!sessions.ContainsKey(suspectId)) return;
        dialogueManager.ResetToIdle();
        currentSuspectId = suspectId;

        // 대사 로그: 이번이 처음 부르는 거면 "입장", 중지했다가 다시 부른 거면 "호출".
        var session = sessions[suspectId];
        dialogueLog.AddEvent(session.suspectName + (session.everCalled ? "를 호출했습니다." : "가 입장하였습니다."));
        session.everCalled = true;

        // 용의자마다 다른 인물 이미지로 암전 → 등장 연출을 재생하고, 화면이 다 밝아진 뒤에 대사창을 띄운다.
        var suspect = graph.GetSuspect(suspectId);
        if (lightFlicker != null && suspect != null)
            lightFlicker.PlayCharacterCall(
                suspect.portrait,
                PresentChoices,
                suspect.useCustomPortraitSize ? suspect.portraitSize : (Vector2?)null);
        else
            PresentChoices();

        SuspectCalled?.Invoke(suspectId);
    }

    // 현재 진행 상태를 그래프에 물어 '지금 가능한 질문'만 보여준다(모순 질문은 목록에 없다 — 기록지에서 직접 판정).
    void PresentChoices()
    {
        if (string.IsNullOrEmpty(currentSuspectId)) return;
        var available = graph.GetAvailableQuestions(currentSuspectId, progress);
        dialogueManager.ShowChoices(available, OnQuestionSelected);

        // 더 물어볼 질문이 없으면 지금까지 확보한 진술로 기록지를 만들고 아이콘 버튼을 켠다
        // (증언을 얻을 때마다 이미 갱신되므로, 여긴 안전망 역할).
        if (available == null || available.Count == 0)
            RevealRecordSheetFor(CurrentSession);
    }

    // 용의자 세션의 기록지를 즉시 갱신하고, 대응하는 아이콘 버튼을 활성화한다. 인덱스는 graph.Suspects 순서를 따른다.
    void RevealRecordSheetFor(SuspectSession session)
    {
        if (session == null) return;

        recordBook.AddOrUpdateSheet(session);

        for (int i = 0; i < graph.Suspects.Count; i++)
        {
            if (graph.Suspects[i].id != session.suspectId) continue;
            if (i < recordSheetButtonOverrides.Length && recordSheetButtonOverrides[i] != null)
                recordSheetButtonOverrides[i].gameObject.SetActive(true);
            break;
        }
    }

    // 세션(입장/호출 ~ 중지/종료)이 진행 중일 때만 대사를 로그에 남기고 실제로 대사창을 띄운다.
    void ShowSessionLines(List<DialogueLine> lines, System.Action onComplete)
    {
        if (currentSuspectId != null)
            foreach (var line in lines) dialogueLog.AddLine(line.speaker, line.text);
        dialogueManager.ShowLines(lines, onComplete);
    }

    void OnQuestionSelected(QuestionNode node)
    {
        progress.MarkAsked(node.id);

        // 모순 질문은 사용되면 선택한 근거(증언)들을 소비한다.
        bool consumedSelection = node.RequiredSelectedCount() > 0;

        ShowSessionLines(node.lines, () =>
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

    // 질문의 결과로 증언을 확보한다: 진행 상태에 기록 + 해당 용의자의 기록지 문장으로 추가하고 즉시 공개한다.
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

            // 표시 이름은 세션(=SuspectData)에서 가져온다. Testimony.ownerSuspectName은
            // 용의자 이름을 바꿔도 따라오지 않아 옛 이름이 남는다 — 진실은 SuspectData 하나로 둔다.
            owner.AddStatement(new StatementRecord(t.id, t.ownerSuspectId, owner.suspectName, t.text));
            // 증언 확보 즉시 기록지에 반영하고 아이콘 버튼을 켠다.
            RevealRecordSheetFor(owner);

            TestimonyGranted?.Invoke(t.id);
        }
    }

    void EndCurrentSuspect()
    {
        if (CurrentSession == null) return;
        var endedId = currentSuspectId;
        CurrentSession.completed = true;
        progress.MarkSuspectCompleted(currentSuspectId);
        dialogueManager.ResetToIdle();
        recordBook.AddOrUpdateSheet(CurrentSession);
        currentSuspectId = null;

        if (lightFlicker != null) lightFlicker.PlayCharacterDismiss();

        SuspectEnded?.Invoke(endedId);
    }

    // ------------------------------------------------------------------
    // 기록지 문장 선택(최대 2개) → 모순 성립 시 곧바로 해당 용의자에게 추궁 / "이건 모순이 아니다" 판정
    // ------------------------------------------------------------------
    void OnRecordSelected(StatementRecord rec)
    {
        if (rec == null) return;
        string sel = rec.recordId; // recordId == 증언 id

        // 이미 선택된 문장을 다시 누르면 선택 해제.
        if (progress.IsSelected(sel))
        {
            progress.selectedTestimonyIds.Remove(sel);
            recordBook.SetSelected(progress.selectedTestimonyIds);
            return;
        }

        // 새 문장 선택(최대 2개, 가득 차 있으면 오래된 것부터 밀려남).
        progress.ToggleSelected(sel);
        recordBook.SetSelected(progress.selectedTestimonyIds);
        StatementSelected?.Invoke(sel);

        // 아직 두 개가 다 모이지 않았으면 대기.
        if (progress.SelectedCount < CaseProgress.MaxSelected) return;

        // 두 문장이 모였다 → 취조 중인 대상/현재 대상과 무관하게 모순 성립 여부를 판정한다.
        string owner;
        var node = graph.FindContradictionNode(progress, out owner);
        recordBook.Hide();

        if (node != null)
        {
            // 모순 성립 → 근거를 제공한 용의자와 무관하게, 해당 진술의 소유자에게 곧바로 추궁(대상이 바뀌면 자동 전환).
            currentSuspectId = owner;
            ContradictionResolved?.Invoke(node);
            OnQuestionSelected(node);
            return;
        }

        // 모순 아님 → 월터가 지적하고 선택을 해제한다.
        progress.ClearSelected();
        recordBook.SetSelected(null);
        ShowSessionLines(
            new List<DialogueLine> { new DialogueLine("월터", "이건 모순이 아니다.") },
            PresentChoices);
    }
}
