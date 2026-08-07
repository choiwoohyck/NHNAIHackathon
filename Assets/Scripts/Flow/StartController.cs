using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 타이틀 화면 컨트롤러. 씬에 이미 배치된 START / How To Play 버튼에 동작을 붙인다.
//
// 흐름:  [타이틀] → 'START' → (검은 페이드) → 사건 선택 씬
//                → 'How To Play' → 조작 설명 오버레이 (닫기로 복귀)
//
// 버튼은 인스펙터에서 지정할 수도 있고, 비워두면 씬 안의 Button 중 라벨(Text/TMP_Text)이
// "START" / "HOW TO PLAY"인 것을 찾아 자동으로 연결한다. 그래도 못 찾으면 코드로 START 버튼을
// 만들어 붙이므로, 씬 구성이 바뀌어도 타이틀 → 사건 선택 흐름은 끊기지 않는다.
public class StartController : MonoBehaviour
{
    [Header("버튼 (비워두면 라벨로 자동 탐색)")]
    [SerializeField]
    Button startButton;

    [SerializeField]
    Button howToPlayButton;

    [Header("튜토리얼")]
    [Tooltip("켜면 아직 한 번도 안 해본 플레이어가 START를 누를 때 튜토리얼로 먼저 보낸다. " +
             "튜토리얼이 끝나면 타이틀로 돌아오므로, 그다음 START부터 실제 게임이다.")]
    [SerializeField]
    bool autoTutorialOnFirstPlay = true;

    [Tooltip("튜토리얼 전용 사건. 비워두면 Resources/TutorialCase 를 찾아 쓴다.")]
    [SerializeField]
    InterrogationCase tutorialCase;

    [Header("전환")]
    [SerializeField]
    string caseSelectSceneName = "CaseSelect";

    [Tooltip("튜토리얼은 사건 선택을 거치지 않고 이 씬으로 곧장 들어간다.")]
    [SerializeField]
    string interrogationSceneName = "Main";

    [SerializeField]
    float fadeSeconds = 0.5f;

    [Header("사운드 (선택)")]
    [SerializeField]
    AudioClip startSfx;

    [Header("조작 설명")]
    [TextArea(6, 16)]
    [SerializeField]
    string howToPlayText =
        "■ 목표\n"
        + "  용의자들을 취조해 증언을 모으고, 진술 사이의 모순을 찾아 범인을 지목한다.\n\n"
        + "■ 진행\n"
        + "  1. 전화기로 용의자를 호출한다.\n"
        + "  2. 질문을 골라 증언을 확보한다. 새 증언이 다음 질문을 연다.\n"
        + "  3. 기록지에서 증언 두 개를 고르면 모순 여부를 판정한다.\n"
        + "     모순이 성립하면 해당 용의자를 곧바로 추궁한다.\n"
        + "  4. '취조 종료'로 대상을 바꾸고, 충분히 모였다면 '사건 판결'을 연다.\n\n"
        + "■ 판결\n"
        + "  범인으로 판단한 용의자를 지목하고 사건 설명의 빈칸을 채워 제출한다.\n"
        + "  범인과 사건 설명이 모두 맞아야 유죄가 선고된다.";

    Image fade; // 전환용 검은 화면
    RectTransform helpRoot; // How To Play 오버레이
    AudioSource audioSrc;
    bool busy;

    void Start()
    {
        DialogueUIUtil.EnsureEventSystem();

        // 타이틀로 돌아왔다는 건 새 플레이의 시작 — 이전 상태를 전부 지운다.
        GameSession.SelectedCase = null;
        GameSession.TutorialMode = false;
        GameSession.ClearVerdict();

        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;

        BuildOverlays();
        WireButtons();
    }

    // ------------------------------------------------------------------
    // 버튼 연결
    // ------------------------------------------------------------------
    void WireButtons()
    {
        var buttons = new List<Button>(
            Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        );

        if (howToPlayButton == null)
            howToPlayButton = FindByLabel(buttons, "HOWTOPLAY");
        if (startButton == null)
            startButton = FindByLabel(buttons, "START");

        // 라벨을 못 읽는 구성이면, How To Play가 아닌 나머지 버튼 하나를 START로 본다.
        if (startButton == null)
            foreach (var b in buttons)
                if (b != null && b != howToPlayButton && !IsOwnButton(b))
                {
                    startButton = b;
                    break;
                }

        // 그래도 없으면 직접 만든다 (타이틀 → 사건 선택 경로를 절대 끊지 않기 위함).
        if (startButton == null)
            startButton = BuildFallbackStartButton();

        startButton.onClick.AddListener(OnStart);
        if (howToPlayButton != null)
            howToPlayButton.onClick.AddListener(ShowHelp);
    }

    // 라벨(Text 또는 TMP_Text)에서 공백/대소문자를 지우고 비교한다. "START", "HOWTOPLAY" 등.
    static Button FindByLabel(List<Button> buttons, string normalizedLabel)
    {
        foreach (var b in buttons)
        {
            if (b == null)
                continue;
            if (Normalize(LabelOf(b)) == normalizedLabel)
                return b;
        }
        return null;
    }

    static string LabelOf(Button b)
    {
        var tmp = b.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrEmpty(tmp.text))
            return tmp.text;
        var txt = b.GetComponentInChildren<Text>(true);
        return txt != null ? txt.text : "";
    }

    static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (!char.IsWhiteSpace(c))
                sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    bool IsOwnButton(Button b) =>
        b != null && helpRoot != null && b.transform.IsChildOf(helpRoot);

    // ------------------------------------------------------------------
    // 동작
    // ------------------------------------------------------------------
    // START — 처음 하는 사람이면 튜토리얼로 먼저 보내고, 아니면 사건 선택으로.
    void OnStart()
    {
        if (autoTutorialOnFirstPlay && !TutorialController.AlreadySeen)
        {
            StartWithTutorial();
            return;
        }
        GoTo(caseSelectSceneName);
    }

    // 튜토리얼은 사건을 고르게 하지 않는다 — 전용 사건을 물려 곧장 취조실로 들여보낸다.
    // 끝나면 TutorialController가 타이틀로 되돌린다.
    void StartWithTutorial()
    {
        var tutorial = tutorialCase != null
            ? tutorialCase
            : Resources.Load<InterrogationCase>(TutorialController.CaseResourceName);

        if (tutorial == null)
        {
            Debug.LogError(
                "[StartController] 튜토리얼 사건을 찾을 수 없습니다. "
                    + "Tools ▸ Editor0 ▸ 튜토리얼 사건 생성 으로 Resources/"
                    + TutorialController.CaseResourceName
                    + " 을 만들어 주세요. 일단 사건 선택으로 보냅니다."
            );
            GoTo(caseSelectSceneName);
            return;
        }

        GameSession.SelectedCase = tutorial;
        GameSession.TutorialMode = true;
        GoTo(interrogationSceneName);
    }

    void GoTo(string sceneName)
    {
        if (busy)
            return;
        busy = true;
        HideHelp();
        if (startSfx != null)
            audioSrc.PlayOneShot(startSfx);
        StartCoroutine(FadeAndLoad(sceneName));
    }

    IEnumerator FadeAndLoad(string sceneName)
    {
        float t = 0f;
        while (t < fadeSeconds)
        {
            t += Time.deltaTime;
            SetFade(fadeSeconds <= 0f ? 1f : Mathf.Clamp01(t / fadeSeconds));
            yield return null;
        }
        SetFade(1f);

        if (!string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
            yield break;
        }

        // 씬이 Build Settings에 없으면 이동하지 못한다 — 페이드를 되돌리고 원인을 남긴다.
        Debug.LogError(
            "[StartController] '"
                + sceneName
                + "' 씬을 로드할 수 없습니다. Build Settings에 등록되어 있는지 확인하세요."
        );
        SetFade(0f);
        busy = false;
    }

    void ShowHelp()
    {
        if (busy || helpRoot == null)
            return;
        helpRoot.gameObject.SetActive(true);
    }

    void HideHelp()
    {
        if (helpRoot != null)
            helpRoot.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // 오버레이 구성 (페이드 + 조작 설명)
    // ------------------------------------------------------------------
    void BuildOverlays()
    {
        var helpCanvas = DialogueUIUtil.CreateCanvas("StartHelpCanvas", 60);
        helpRoot = DialogueUIUtil.CreatePanel(
            helpCanvas.transform,
            "HowToPlayDim",
            new Color(0f, 0f, 0f, 0.82f)
        );
        DialogueUIUtil.Stretch(helpRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var panel = DialogueUIUtil.CreatePanel(
            helpRoot,
            "HowToPlayPanel",
            new Color(0.1f, 0.1f, 0.13f, 0.98f)
        );
        DialogueUIUtil.Stretch(
            panel,
            new Vector2(0.18f, 0.12f),
            new Vector2(0.82f, 0.9f),
            Vector2.zero,
            Vector2.zero
        );

        var title = DialogueUIUtil.CreateText(
            panel,
            "Title",
            32,
            TextAnchor.MiddleCenter,
            new Color(1f, 0.82f, 0.42f)
        );
        title.font = DialogueUIUtil.KoreanFont;
        title.fontStyle = FontStyle.Bold;
        title.text = "게임 방법";
        DialogueUIUtil.Stretch(
            title.rectTransform,
            new Vector2(0f, 0.87f),
            new Vector2(1f, 0.98f),
            Vector2.zero,
            Vector2.zero
        );

        var body = DialogueUIUtil.CreateText(
            panel,
            "Body",
            18,
            TextAnchor.UpperLeft,
            new Color(0.92f, 0.94f, 0.95f)
        );
        body.font = DialogueUIUtil.KoreanFont;
        body.text = howToPlayText;
        DialogueUIUtil.Stretch(
            body.rectTransform,
            new Vector2(0.06f, 0.16f),
            new Vector2(0.94f, 0.85f),
            Vector2.zero,
            Vector2.zero
        );

        // 읽는 것보다 한 번 해보는 게 빠른 사람을 위한 입구.
        HelpButton(panel, "TutorialBtn", "튜토리얼로 해보기",
                   new Vector2(0.24f, 0.04f), new Vector2(0.48f, 0.12f),
                   new Color(0.2f, 0.32f, 0.3f, 0.96f), StartWithTutorial);

        HelpButton(panel, "CloseBtn", "닫기",
                   new Vector2(0.52f, 0.04f), new Vector2(0.76f, 0.12f),
                   new Color(0.2f, 0.2f, 0.28f, 0.95f), HideHelp);

        helpRoot.gameObject.SetActive(false);

        // 페이드는 항상 맨 위(설명 오버레이보다도 위)에 둔다.
        var fadeCanvas = DialogueUIUtil.CreateCanvas("StartFadeCanvas", 90);
        fade = DialogueUIUtil
            .CreatePanel(fadeCanvas.transform, "Fade", new Color(0f, 0f, 0f, 0f))
            .GetComponent<Image>();
        fade.raycastTarget = false;
        DialogueUIUtil.Stretch(
            fade.rectTransform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
    }

    Button HelpButton(RectTransform parent, string name, string label,
                      Vector2 anchorMin, Vector2 anchorMax, Color color,
                      UnityEngine.Events.UnityAction onClick)
    {
        var btn = DialogueUIUtil.CreateButton(parent, name, label, color);
        var lbl = btn.GetComponentInChildren<Text>();
        if (lbl != null)
        {
            lbl.font = DialogueUIUtil.KoreanFont;
            lbl.fontSize = 20;
        }
        DialogueUIUtil.Stretch(btn.GetComponent<RectTransform>(), anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        btn.onClick.AddListener(onClick);
        return btn;
    }

    // 코드로 만드는 예비 START 버튼 (씬에 버튼이 하나도 없을 때만 쓰인다).
    Button BuildFallbackStartButton()
    {
        var canvas = DialogueUIUtil.CreateCanvas("StartFallbackCanvas", 55);
        var btn = DialogueUIUtil.CreateButton(
            canvas.transform,
            "StartBtn",
            "START",
            new Color(0.45f, 0.12f, 0.14f, 0.95f)
        );
        var lbl = btn.GetComponentInChildren<Text>();
        if (lbl != null)
        {
            lbl.font = DialogueUIUtil.KoreanFont;
            lbl.fontSize = 28;
        }
        DialogueUIUtil.Stretch(
            btn.GetComponent<RectTransform>(),
            new Vector2(0.42f, 0.1f),
            new Vector2(0.58f, 0.18f),
            Vector2.zero,
            Vector2.zero
        );
        return btn;
    }

    void SetFade(float a)
    {
        if (fade == null)
            return;
        var c = fade.color;
        c.a = a;
        fade.color = c;
    }
}
