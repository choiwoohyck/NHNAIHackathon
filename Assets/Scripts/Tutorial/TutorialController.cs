using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 취조실 인터랙티브 튜토리얼.
//
// 설명을 읽히는 대신 실제로 한 번 시켜본다. 목표는 단 하나 —
// "서로 다른 용의자의 진술을 맞대면 모순이 잡힌다"를 플레이어 손으로 성립시키게 하는 것.
//
// 안내할 경로는 TutorialPlan이 사건 그래프에서 뽑아내므로 사건 이름이나 질문 id를 하드코딩하지 않는다.
//
// 화면 처리:
//   - 하이라이트할 버튼의 사각형만 남기고 상/하/좌/우 네 장의 딤 패널로 화면을 덮는다.
//     이 패널들이 레이캐스트를 먹기 때문에 '지금 눌러야 할 것 외에는 눌리지 않는' 효과가 공짜로 따라온다.
//   - 대사가 출력 중일 때는 딤을 걷는다. 안 그러면 대사를 넘길 수 없어 진행이 막힌다.
//   - 대상을 못 찾으면 막지 않는다. 어떤 경우에도 플레이어가 갇히지 않게 하기 위함.
//   - '건너뛰기'는 항상 눌린다.
public class TutorialController : MonoBehaviour
{
    const string SeenKey = "tutorial_seen";

    /// <summary>튜토리얼 전용 사건의 Resources 이름. StartController와 생성 스크립트가 함께 쓴다.</summary>
    public const string CaseResourceName = "TutorialCase";

    /// <summary>튜토리얼이 끝나면 돌아갈 씬.</summary>
    public const string TitleSceneName = "Start";

    public static bool AlreadySeen => PlayerPrefs.GetInt(SeenKey, 0) == 1;

    public static void ForgetSeen() => PlayerPrefs.DeleteKey(SeenKey);

    public static void MarkSeen()
    {
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();
    }

    class Beat
    {
        public string caption;
        public System.Func<RectTransform> target;   // null이면 스포트라이트 없음(텍스트 전용)
        public System.Func<bool> isDone;            // null이면 '확인' 버튼으로 진행
    }

    InterrogationController interrogation;
    PhoneCallController phone;
    RecordBookController recordBook;
    TutorialPlan plan;

    readonly List<Beat> beats = new List<Beat>();
    int index;
    bool finished;

    // ---- 이벤트로 받아 두는 진행 상태 ----
    // 전화기·기록지는 여러 번 열리므로 불리언이 아니라 '몇 번째로 열렸는가'로 센다.
    int phoneOpenCount;
    int recordOpenCount;
    bool contradictionDone;
    string lastCalled;
    readonly HashSet<string> granted = new HashSet<string>();
    readonly HashSet<string> ended = new HashSet<string>();
    readonly HashSet<string> selected = new HashSet<string>();

    // 구독 해제를 위해 핸들러를 들고 있는다.
    System.Action<string> hCalled, hEnded, hGranted, hSelected;
    System.Action<QuestionNode> hContradiction;
    System.Action hPhone, hRecord, hVerdict;

    // ---- UI ----
    Canvas canvas;
    Image dimTop, dimBottom, dimLeft, dimRight;
    readonly List<Image> border = new List<Image>();
    RectTransform captionBox;
    Text captionText;
    Button confirmButton;

    static readonly Color DimColor = new Color(0f, 0f, 0f, 0.72f);
    static readonly Color BorderColor = new Color(1f, 0.82f, 0.42f, 0.95f);

    public bool IsFinished => finished;

    public void Begin(InterrogationController controller, PhoneCallController phoneCall, RecordBookController book)
    {
        interrogation = controller;
        phone = phoneCall;
        recordBook = book;

        plan = TutorialPlan.Build(interrogation != null ? interrogation.Graph : null);
        if (plan == null)
        {
            // 안내할 수 있는 모순 경로가 없는 사건이면 조용히 물러난다.
            Debug.Log("[Tutorial] 안내할 모순 경로가 없어 튜토리얼을 실행하지 않습니다.");
            Destroy(gameObject);
            return;
        }

        Subscribe();
        BuildUI();
        BuildBeats();
    }

    void Subscribe()
    {
        hCalled = id => lastCalled = id;
        hEnded = id => ended.Add(id);
        hGranted = id => granted.Add(id);
        hSelected = id => selected.Add(id);
        hContradiction = _ => contradictionDone = true;
        hPhone = () => phoneOpenCount++;
        hRecord = () => recordOpenCount++;

        // 판결 화면을 열었다면 안내는 여기서 끝. 안 그러면 튜토리얼 오버레이가 판결 패널을 덮는다.
        hVerdict = Finish;

        interrogation.SuspectCalled += hCalled;
        interrogation.SuspectEnded += hEnded;
        interrogation.TestimonyGranted += hGranted;
        interrogation.StatementSelected += hSelected;
        interrogation.ContradictionResolved += hContradiction;
        interrogation.VerdictOpened += hVerdict;
        if (phone != null) phone.PopupOpened += hPhone;
        if (recordBook != null) recordBook.Opened += hRecord;
    }

    void OnDestroy()
    {
        if (interrogation != null)
        {
            interrogation.SuspectCalled -= hCalled;
            interrogation.SuspectEnded -= hEnded;
            interrogation.TestimonyGranted -= hGranted;
            interrogation.StatementSelected -= hSelected;
            interrogation.ContradictionResolved -= hContradiction;
            interrogation.VerdictOpened -= hVerdict;
        }
        if (phone != null) phone.PopupOpened -= hPhone;
        if (recordBook != null) recordBook.Opened -= hRecord;

        if (canvas != null) Destroy(canvas.gameObject);
    }

    // ------------------------------------------------------------------
    // 안내 순서
    // ------------------------------------------------------------------
    void BuildBeats()
    {
        string firstName = plan.firstSuspect.suspectName;
        string secondName = plan.secondSuspect.suspectName;

        // 전화기·기록지를 '몇 번째로 여는 것'인지 미리 확정해 둔다(뒤에서 또 열리므로).
        int phoneOpensNeeded = 0;
        int recordOpensNeeded = 0;

        Text("이번 한 건만 같이 가겠습니다. 시키는 대로만 하세요.");

        phoneOpensNeeded++;
        int firstPhone = phoneOpensNeeded;
        Spot("취조는 부르는 것부터입니다. 전화기를 드세요.",
             () => Rect(phone != null ? phone.PhoneButtonRect : null),
             () => phoneOpenCount >= firstPhone);

        Spot($"{firstName} 부터 부릅니다.",
             () => FindRect("Call_" + plan.firstSuspect.id),
             () => lastCalled == plan.firstSuspect.id);

        Spot($"「{plan.firstQuestion.label}」 을(를) 물어보세요.",
             () => FindRect("Choice_" + plan.firstQuestion.id),
             () => granted.Contains(plan.firstTestimony.id));

        Text($"받아낸 진술은 기록지에 자동으로 쌓입니다.\n\n\"{Flatten(plan.firstTestimony.text)}\"");

        if (plan.NeedsSuspectSwitch)
        {
            Text("한 사람 말만으로는 아무것도 증명되지 않습니다.\n같은 일을 다른 사람에게도 물어야 합니다.\n\n전화기를 들면 언제든 취조 상대를 바꿀 수 있습니다.");

            phoneOpensNeeded++;
            int secondPhone = phoneOpensNeeded;
            Spot("다시 전화기를 드세요.",
                 () => Rect(phone != null ? phone.PhoneButtonRect : null),
                 () => phoneOpenCount >= secondPhone);

            Spot($"이번엔 {secondName} 입니다.",
                 () => FindRect("Call_" + plan.secondSuspect.id),
                 () => lastCalled == plan.secondSuspect.id);
        }

        Spot($"「{plan.secondQuestion.label}」 을(를) 물어보세요.",
             () => FindRect("Choice_" + plan.secondQuestion.id),
             () => granted.Contains(plan.secondTestimony.id));

        Text($"두 사람의 말이 어긋납니다.\n\n{firstName}: \"{Flatten(plan.firstTestimony.text)}\"\n{secondName}: \"{Flatten(plan.secondTestimony.text)}\"");

        recordOpensNeeded++;
        int firstRecord = recordOpensNeeded;
        Spot("기록지를 열어 두 진술을 맞대봅시다.",
             () => RecordButton(),
             () => recordOpenCount >= firstRecord);

        Spot($"{firstName}의 진술을 고르세요.",
             () => FindRect("Line_" + plan.firstTestimony.id),
             () => selected.Contains(plan.firstTestimony.id));

        Spot($"이제 {secondName}의 진술을 고르세요.\n두 개가 모이면 곧바로 판정이 들어갑니다.",
             () => FindRect("Line_" + plan.secondTestimony.id),
             () => contradictionDone);

        Text("모순이 성립하면 그 진술의 주인에게 즉시 추궁이 들어갑니다.\n이 일의 요령은 방금 보신 것이 전부입니다.");

        // 실제 사건에서 쓸 것들은 '알려만 주고' 시키지는 않는다. 훈련은 모순 한 번으로 끝.
        Text("실제 사건에서는 이렇게 모은 모순으로 범인을 좁힙니다.\n\n"
             + "· 한 사람에게 더 물을 게 없어지면 '취조 종료'가 열립니다.\n"
             + "· 기록지는 용의자별 아이콘으로 언제든 다시 열 수 있습니다.\n"
             + "· 확신이 서면 '사건 판결'에서 범인을 지목하고 사건의 전말을 채웁니다.\n"
             + "  단, 사람을 잘못 짚으면 그걸로 끝입니다.");

        Text("훈련은 여기까지입니다.\n타이틀로 돌아갑니다 — 이제 진짜 사건을 맡으십시오.\n\n— 행운을 빕니다, 수사관님.");
    }

    // 기록지 아이콘은 두 용의자 중 켜져 있는 쪽을 쓴다.
    RectTransform RecordButton()
    {
        var second = Rect(interrogation.RecordSheetButtonFor(plan.secondSuspect.id));
        if (second != null && second.gameObject.activeInHierarchy) return second;
        return Rect(interrogation.RecordSheetButtonFor(plan.firstSuspect.id));
    }

    void Spot(string caption, System.Func<RectTransform> target, System.Func<bool> isDone) =>
        beats.Add(new Beat { caption = caption, target = target, isDone = isDone });

    void Text(string caption) => beats.Add(new Beat { caption = caption });

    // 증언 원문은 줄바꿈·들여쓰기가 섞여 있어 안내문에 그대로 넣으면 지저분하다.
    static string Flatten(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var one = s.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        while (one.Contains("  ")) one = one.Replace("  ", " ");
        return one.Trim();
    }

    static RectTransform Rect(Component c) => c != null ? c.GetComponent<RectTransform>() : null;

    // 런타임에 만들어지는 버튼(선택지 Choice_*, 호출 Call_*, 기록 문장 Line_*)은 이름으로 찾는다.
    static RectTransform FindRect(string name)
    {
        foreach (var rt in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (rt.gameObject.name == name) return rt;
        return null;
    }

    // ------------------------------------------------------------------
    // 진행
    // ------------------------------------------------------------------
    void Update()
    {
        if (finished || beats.Count == 0) return;

        var beat = beats[index];

        var target = beat.target != null ? beat.target() : null;
        bool hasTarget = target != null && target.gameObject.activeInHierarchy;

        // 대사 출력 중에는 화면을 막지 않는다 — 막으면 대사를 넘길 수 없어 진행이 멈춘다.
        bool speaking = DialogueManager.Instance != null
                        && DialogueManager.Instance.State == DialogueState.Speaking;

        bool spotlight = hasTarget && !speaking;
        if (spotlight) PlaceHole(target);
        else HideHole();

        captionText.text = beat.caption;
        PlaceCaption(spotlight ? target : null);

        bool manual = beat.isDone == null;
        if (confirmButton.gameObject.activeSelf != manual)
            confirmButton.gameObject.SetActive(manual);

        if (!manual && beat.isDone()) Advance();
    }

    void Advance()
    {
        index++;
        if (index >= beats.Count) Finish();
    }

    void Finish()
    {
        if (finished) return;
        finished = true;

        MarkSeen();
        GameSession.TutorialMode = false;
        GameSession.SelectedCase = null;

        // 튜토리얼 사건은 계속 플레이할 물건이 아니다(판결 데이터도 없다) — 끝나면 타이틀로 돌려보낸다.
        if (Application.CanStreamedLevelBeLoaded(TitleSceneName))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(TitleSceneName);
            return;
        }

        Debug.LogError("[Tutorial] '" + TitleSceneName + "' 씬을 로드할 수 없어 타이틀로 돌아가지 못했습니다.");
        Destroy(gameObject);   // OnDestroy가 캔버스까지 정리한다
    }

    // ------------------------------------------------------------------
    // 화면 구성
    // ------------------------------------------------------------------
    void BuildUI()
    {
        canvas = DialogueUIUtil.CreateCanvas("TutorialCanvas", 85);

        dimTop = Dim("Dim_Top");
        dimBottom = Dim("Dim_Bottom");
        dimLeft = Dim("Dim_Left");
        dimRight = Dim("Dim_Right");

        for (int i = 0; i < 4; i++)
        {
            var go = new GameObject("Border" + i, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            var img = go.GetComponent<Image>();
            img.color = BorderColor;
            img.raycastTarget = false;
            border.Add(img);
        }

        // --- 안내문 ---
        captionBox = DialogueUIUtil.CreatePanel(canvas.transform, "Caption", new Color(0.06f, 0.07f, 0.09f, 0.96f));

        captionText = DialogueUIUtil.CreateText(captionBox, "Text", 22, TextAnchor.MiddleCenter, new Color(0.95f, 0.96f, 0.97f));
        captionText.font = DialogueUIUtil.KoreanFont;
        captionText.raycastTarget = false;
        DialogueUIUtil.Stretch(captionText.rectTransform, Vector2.zero, Vector2.one, new Vector2(28, 46), new Vector2(-28, -14));

        confirmButton = DialogueUIUtil.CreateButton(captionBox, "ConfirmBtn", "확인", new Color(0.2f, 0.32f, 0.3f, 0.98f));
        var confirmLabel = confirmButton.GetComponentInChildren<Text>();
        if (confirmLabel != null) { confirmLabel.font = DialogueUIUtil.KoreanFont; confirmLabel.fontSize = 18; }
        DialogueUIUtil.Stretch(confirmButton.GetComponent<RectTransform>(),
            new Vector2(0.40f, 0f), new Vector2(0.60f, 0f), new Vector2(0, 8), new Vector2(0, 40));
        confirmButton.onClick.AddListener(Advance);

        // --- 건너뛰기 (딤보다 나중에 만들어 항상 위에서 클릭을 받는다) ---
        var skip = DialogueUIUtil.CreateButton(canvas.transform, "SkipBtn", "튜토리얼 건너뛰기", new Color(0.22f, 0.18f, 0.18f, 0.92f));
        var skipLabel = skip.GetComponentInChildren<Text>();
        if (skipLabel != null) { skipLabel.font = DialogueUIUtil.KoreanFont; skipLabel.fontSize = 16; }
        var srt = skip.GetComponent<RectTransform>();
        srt.anchorMin = srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(1f, 1f);
        srt.sizeDelta = new Vector2(190, 40);
        srt.anchoredPosition = new Vector2(-20, -20);
        skip.onClick.AddListener(Finish);
    }

    Image Dim(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvas.transform, false);
        var img = go.GetComponent<Image>();
        img.color = DimColor;
        img.raycastTarget = true;   // 구멍 밖 클릭을 막는 장치
        return img;
    }

    // 대상 사각형을 화면 비율(0~1)로 바꿔, 그 바깥을 네 장으로 덮는다.
    void PlaceHole(RectTransform target)
    {
        var targetCanvas = target.GetComponentInParent<Canvas>();
        Camera cam = (targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? targetCanvas.worldCamera : null;

        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        const float pad = 6f;
        float x0 = Mathf.Clamp01((min.x - pad) / Screen.width);
        float x1 = Mathf.Clamp01((max.x + pad) / Screen.width);
        float y0 = Mathf.Clamp01((min.y - pad) / Screen.height);
        float y1 = Mathf.Clamp01((max.y + pad) / Screen.height);

        SetDimsActive(true);
        Anchor(dimTop, new Vector2(0f, y1), Vector2.one);
        Anchor(dimBottom, Vector2.zero, new Vector2(1f, y0));
        Anchor(dimLeft, new Vector2(0f, y0), new Vector2(x0, y1));
        Anchor(dimRight, new Vector2(x1, y0), new Vector2(1f, y1));

        // 구멍 테두리 (위/아래/좌/우 얇은 선) — 천천히 맥동시켜 눈에 걸리게 한다.
        float th = 2.5f / Screen.height;
        float tw = 2.5f / Screen.width;
        Anchor(border[0], new Vector2(x0, y1 - th), new Vector2(x1, y1));
        Anchor(border[1], new Vector2(x0, y0), new Vector2(x1, y0 + th));
        Anchor(border[2], new Vector2(x0, y0), new Vector2(x0 + tw, y1));
        Anchor(border[3], new Vector2(x1 - tw, y0), new Vector2(x1, y1));

        float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.2f));
        foreach (var b in border)
        {
            b.gameObject.SetActive(true);
            b.color = new Color(BorderColor.r, BorderColor.g, BorderColor.b, pulse);
        }
    }

    void HideHole()
    {
        SetDimsActive(false);
        foreach (var b in border) b.gameObject.SetActive(false);
    }

    void SetDimsActive(bool on)
    {
        dimTop.gameObject.SetActive(on);
        dimBottom.gameObject.SetActive(on);
        dimLeft.gameObject.SetActive(on);
        dimRight.gameObject.SetActive(on);
    }

    // 하이라이트를 가리지 않는 쪽에 안내문을 붙인다.
    void PlaceCaption(RectTransform target)
    {
        bool bottom = true;
        if (target != null)
        {
            var targetCanvas = target.GetComponentInParent<Canvas>();
            Camera cam = (targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? targetCanvas.worldCamera : null;

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float centerY = RectTransformUtility.WorldToScreenPoint(cam, (corners[0] + corners[2]) * 0.5f).y;
            bottom = centerY > Screen.height * 0.5f;   // 대상이 위쪽이면 안내문은 아래로
        }

        if (bottom) Anchor(captionBox, new Vector2(0.17f, 0.05f), new Vector2(0.83f, 0.25f));
        else Anchor(captionBox, new Vector2(0.17f, 0.75f), new Vector2(0.83f, 0.95f));
    }

    static void Anchor(Component c, Vector2 min, Vector2 max) =>
        DialogueUIUtil.Stretch((RectTransform)c.transform, min, max, Vector2.zero, Vector2.zero);
}
