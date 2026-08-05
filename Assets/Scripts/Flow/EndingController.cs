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

        // 다시 수사하기 → 사건 선택 화면으로
        var btn = DialogueUIUtil.CreateButton(bg, "AgainBtn", "다시 수사하기", new Color(0.2f, 0.2f, 0.28f, 0.95f));
        var lbl = btn.GetComponentInChildren<Text>();
        if (lbl != null) { lbl.font = DialogueUIUtil.KoreanFont; lbl.fontSize = 22; }
        DialogueUIUtil.Stretch(btn.GetComponent<RectTransform>(), new Vector2(0.38f, 0.03f), new Vector2(0.62f, 0.09f), Vector2.zero, Vector2.zero);
        btn.onClick.AddListener(GoToCaseSelect);

        // 사운드
        var clip = success ? successSfx : failSfx;
        if (clip != null) AudioSource.PlayClipAtPoint(clip, Vector3.zero);

        // 페이드 인
        var cg = bg.gameObject.AddComponent<CanvasGroup>();
        StartCoroutine(FadeIn(cg));
    }

    void GoToCaseSelect()
    {
        GameSession.ClearVerdict();
        if (!string.IsNullOrEmpty(caseSelectSceneName) && Application.CanStreamedLevelBeLoaded(caseSelectSceneName))
            SceneManager.LoadScene(caseSelectSceneName);
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
