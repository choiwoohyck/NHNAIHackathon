using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 취조 흐름 전체를 진행시키는 컨트롤러 (프로토타입용).
// 질문 데이터는 ScriptableObject 없이 이 스크립트 안에서 직접 만들며,
// 선택지 버튼 라벨은 "내용1", "내용2"처럼 임시 문구로 채워 바로 플레이 테스트할 수 있게 한다.
//
// 사용법: 빈 GameObject 하나에 이 스크립트만 붙이면 DialogueManager / RecordBookController가
// 자동으로 추가되고, 씬에 필요한 UI가 런타임에 전부 생성된다.
public class InterrogationController : MonoBehaviour
{
    DialogueManager dialogueManager;
    RecordBookController recordBook;

    readonly Dictionary<string, SuspectSession> sessions = new Dictionary<string, SuspectSession>();
    readonly List<string> suspectOrder = new List<string>();
    SuspectSession currentSession;

    void Awake()
    {
        dialogueManager = GetComponent<DialogueManager>();
        if (dialogueManager == null) dialogueManager = gameObject.AddComponent<DialogueManager>();

        recordBook = GetComponent<RecordBookController>();
        if (recordBook == null) recordBook = gameObject.AddComponent<RecordBookController>();
    }

    void Start()
    {
        BuildTestData();
        BuildSuspectBar();
    }

    // ------------------------------------------------------------------
    // 테스트용 하드코딩 데이터. 실제 사건 데이터로 교체할 때 이 메서드만 갈아끼우면 된다.
    // ------------------------------------------------------------------
    void BuildTestData()
    {
        var suspectA = new List<Question>
        {
            new Question("A_Q1", "내용1", "A는 사건 당일 회사에서 근무했다고 진술함.",
                new DialogueLine("월터", "사건 당일 어디에 있었습니까?"),
                new DialogueLine("A", "그날은 회사에서 계속 근무하고 있었습니다.")),

            new Question("A_Q2", "내용2", "A는 피해자와 개인적인 친분이 없다고 주장함.",
                new DialogueLine("월터", "피해자와는 어떤 관계입니까?"),
                new DialogueLine("A", "그냥 아는 사이일 뿐, 개인적인 친분은 없습니다.")),

            new Question("A_Q3", "내용3", "A는 국가 DB 접근 권한이 없다고 진술함.",
                new DialogueLine("월터", "국가 전산망에 접근할 수 있습니까?"),
                new DialogueLine("A", "아니요, 저는 그런 권한이 없습니다.")),
        };

        var suspectB = new List<Question>
        {
            new Question("B_Q1", "내용1", "B는 A가 사건 당일 연차를 사용했다고 진술함.",
                new DialogueLine("월터", "A는 사건 당일 어디에 있었습니까?"),
                new DialogueLine("B", "A는 그날 연차를 썼어요. 회사에 나오지 않았습니다.")),

            new Question("B_Q2", "내용2", "B는 최근 A와 업무상 갈등이 있었다고 인정함.",
                new DialogueLine("월터", "A와의 관계는 어떻습니까?"),
                new DialogueLine("B", "최근에 업무 때문에 좀 부딪힌 적은 있어요.")),
        };

        sessions["A"] = new SuspectSession("A", "용의자 A", suspectA);
        suspectOrder.Add("A");

        sessions["B"] = new SuspectSession("B", "용의자 B", suspectB);
        suspectOrder.Add("B");
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

        foreach (var suspectId in suspectOrder)
        {
            var id = suspectId;
            var session = sessions[id];
            var btn = DialogueUIUtil.CreateButton(bar, "Call_" + id, session.suspectName + " 호출", new Color(0.2f, 0.2f, 0.2f, 0.8f));
            btn.gameObject.AddComponent<LayoutElement>().preferredWidth = 160;
            btn.onClick.AddListener(() => CallSuspect(id));
        }

        var stopBtn = DialogueUIUtil.CreateButton(bar, "StopBtn", "취조 중지", new Color(0.4f, 0.2f, 0.1f, 0.8f));
        stopBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 140;
        stopBtn.onClick.AddListener(PauseCurrentSuspect);

        var endBtn = DialogueUIUtil.CreateButton(bar, "EndBtn", "취조 종료", new Color(0.1f, 0.3f, 0.1f, 0.8f));
        endBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 140;
        endBtn.onClick.AddListener(EndCurrentSuspect);

        var bookBtn = DialogueUIUtil.CreateButton(bar, "BookBtn", "기록지 보기", new Color(0.1f, 0.1f, 0.4f, 0.8f));
        bookBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 140;
        bookBtn.onClick.AddListener(() => recordBook.ToggleVisible());
    }

    // ------------------------------------------------------------------
    // 취조 진행 로직
    // ------------------------------------------------------------------
    void CallSuspect(string suspectId)
    {
        dialogueManager.ResetToIdle();
        currentSession = sessions[suspectId];
        PresentChoices();
    }

    void PresentChoices()
    {
        if (currentSession == null) return;
        dialogueManager.ShowChoices(currentSession.GetAvailableQuestions(), OnQuestionSelected);
    }

    void OnQuestionSelected(Question question)
    {
        question.asked = true;
        var session = currentSession;

        dialogueManager.ShowLines(question.responseLines, () =>
        {
            if (!string.IsNullOrEmpty(question.recordText))
            {
                var record = new StatementRecord(
                    session.suspectId + "_" + question.id,
                    session.suspectId,
                    session.suspectName,
                    question.recordText);
                session.statements.Add(record);
            }

            PresentChoices();
        });
    }

    void PauseCurrentSuspect()
    {
        if (currentSession == null) return;

        dialogueManager.ResetToIdle();
        recordBook.AddOrUpdateSheet(currentSession);
        currentSession = null;
    }

    void EndCurrentSuspect()
    {
        if (currentSession == null) return;

        currentSession.completed = true;
        dialogueManager.ResetToIdle();
        recordBook.AddOrUpdateSheet(currentSession);
        currentSession = null;
    }
}
