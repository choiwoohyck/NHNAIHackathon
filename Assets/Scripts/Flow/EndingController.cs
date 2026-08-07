using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// 엔딩 씬에 두는 컨트롤러. 판결 결과(GameSession.LastVerdict)에 따라 이미지를 출력한다.
// 사용법: 빈 Ending 씬을 만들고, 아무 GameObject에 이 스크립트를 붙인 뒤
//         성공/오인/증거불충분 스프라이트를 Inspector에서 지정하면 된다(없으면 색 박스로 대체).
public class EndingController : MonoBehaviour
{
    [Header("결과 이미지 (없으면 색 박스로 대체)")]
    [SerializeField] Sprite successSprite;        // 유죄(사건 해결)
    [SerializeField] Sprite wrongSuspectSprite;   // 무죄(오인 지목)
    [SerializeField] Sprite insufficientSprite;   // 증거 불충분

    [Header("결과 문구")]
    [SerializeField] string successText = "유죄 — 사건 해결";
    [SerializeField] string wrongText = "무죄 — 오인 지목";
    [SerializeField] string insufficientText = "증거 불충분";

    [Header("사운드 (선택)")]
    [SerializeField] AudioClip successSfx;
    [SerializeField] AudioClip failSfx;

    [Header("전환")]
    [SerializeField] string caseSelectSceneName = "CaseSelect";
    [SerializeField] string titleSceneName = "Start";
    [SerializeField] float fadeInSeconds = 0.6f;

    void Start()
    {
        DialogueUIUtil.EnsureEventSystem();
        Build();
    }

    void Build()
    {
        var result = GameSession.HasVerdict ? GameSession.LastVerdict : VerdictResult.WrongSuspect;
        bool success = result == VerdictResult.Success;

        Sprite sprite = success ? successSprite
                       : result == VerdictResult.WrongSuspect ? wrongSuspectSprite
                       : insufficientSprite;
        string label = success ? successText
                      : result == VerdictResult.WrongSuspect ? wrongText
                      : insufficientText;
        Color accent = success ? new Color(0.35f, 0.8f, 0.45f)
                      : result == VerdictResult.WrongSuspect ? new Color(0.85f, 0.35f, 0.35f)
                      : new Color(0.85f, 0.7f, 0.3f);

        var canvas = DialogueUIUtil.CreateCanvas("EndingCanvas", 50);

        var bg = DialogueUIUtil.CreatePanel(canvas.transform, "EndingBG", new Color(0.04f, 0.04f, 0.05f, 1f));
        DialogueUIUtil.Stretch(bg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // 결과 이미지 (스프라이트 없으면 강조색 박스)
        var imgGO = new GameObject("ResultImage", typeof(RectTransform), typeof(Image));
        imgGO.transform.SetParent(bg, false);
        var img = imgGO.GetComponent<Image>();
        var irt = (RectTransform)imgGO.transform;
        irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.58f);
        irt.pivot = new Vector2(0.5f, 0.5f);
        if (sprite != null)
        {
            img.sprite = sprite; img.preserveAspect = true; img.color = Color.white;
            irt.sizeDelta = new Vector2(760, 460);
        }
        else
        {
            img.color = accent;
            irt.sizeDelta = new Vector2(480, 300);
        }

        // 결과 문구
        var text = DialogueUIUtil.CreateText(bg, "ResultText", 40, TextAnchor.MiddleCenter, Color.white);
        text.font = DialogueUIUtil.KoreanFont; text.fontStyle = FontStyle.Bold; text.text = label;
        DialogueUIUtil.Stretch(text.rectTransform, new Vector2(0.1f, 0.16f), new Vector2(0.9f, 0.26f), Vector2.zero, Vector2.zero);

        // 사건 설명 점수 (있으면)
        if (GameSession.TotalFields > 0)
        {
            var score = DialogueUIUtil.CreateText(bg, "Score", 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));
            score.font = DialogueUIUtil.KoreanFont;
            score.text = "사건 설명 " + GameSession.CorrectFields + " / " + GameSession.TotalFields;
            DialogueUIUtil.Stretch(score.rectTransform, new Vector2(0.1f, 0.11f), new Vector2(0.9f, 0.15f), Vector2.zero, Vector2.zero);
        }

        // 루프를 닫는 두 갈래: 사건 선택으로 돌아가 다시 수사하거나, 타이틀로 완전히 나간다.
        MakeNavButton(bg, "AgainBtn", "다시 수사하기", new Color(0.2f, 0.2f, 0.28f, 0.95f),
                      new Vector2(0.28f, 0.03f), new Vector2(0.49f, 0.09f), GoToCaseSelect);
        MakeNavButton(bg, "TitleBtn", "처음으로", new Color(0.16f, 0.16f, 0.2f, 0.95f),
                      new Vector2(0.51f, 0.03f), new Vector2(0.72f, 0.09f), GoToTitle);

        // 사운드
        var clip = success ? successSfx : failSfx;
        if (clip != null) AudioSource.PlayClipAtPoint(clip, Vector3.zero);

        // 페이드 인
        var cg = bg.gameObject.AddComponent<CanvasGroup>();
        StartCoroutine(FadeIn(cg));
    }

    void MakeNavButton(RectTransform parent, string name, string label, Color color,
                       Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
    {
        var btn = DialogueUIUtil.CreateButton(parent, name, label, color);
        var lbl = btn.GetComponentInChildren<Text>();
        if (lbl != null) { lbl.font = DialogueUIUtil.KoreanFont; lbl.fontSize = 22; }
        DialogueUIUtil.Stretch(btn.GetComponent<RectTransform>(), anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        btn.onClick.AddListener(onClick);
    }

    // 다시 수사하기 → 사건 선택 화면. 고른 사건은 거기서 다시 정해지므로 판결만 지운다.
    void GoToCaseSelect()
    {
        GameSession.ClearVerdict();
        Load(caseSelectSceneName);
    }

    // 처음으로 → 타이틀. 한 판이 끝난 것이므로 선택한 사건까지 비운다.
    void GoToTitle()
    {
        GameSession.ClearVerdict();
        GameSession.SelectedCase = null;
        Load(titleSceneName);
    }

    void Load(string sceneName)
    {
        if (!string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
            return;
        }
        Debug.LogError("[EndingController] '" + sceneName + "' 씬을 로드할 수 없습니다. Build Settings에 등록되어 있는지 확인하세요.");
    }

    IEnumerator FadeIn(CanvasGroup cg)
    {
        float t = 0f; cg.alpha = 0f;
        while (t < fadeInSeconds)
        {
            t += Time.deltaTime;
            cg.alpha = fadeInSeconds <= 0f ? 1f : Mathf.Clamp01(t / fadeInSeconds);
            yield return null;
        }
        cg.alpha = 1f;
    }
}
