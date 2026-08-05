using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// 사건 선택 화면. 어두운 책상 위 사건파일들을 보여주고, 하나를 고르면 그 사건(InterrogationCase)을
// GameSession에 담아 취조 씬으로 넘어간다.
// 사용법: 빈 CaseSelect 씬을 만들고 아무 GameObject에 이 스크립트를 붙인 뒤,
//         Cases에 InterrogationCase들을 넣고(같은 사건을 두 번 넣어 파일 2개로 보여줘도 됨),
//         배경/파일 스프라이트·효과음을 Inspector에서 지정하면 된다(없으면 색 박스로 대체).
public class CaseSelectController : MonoBehaviour
{
    [Header("표시할 사건들")]
    [SerializeField] InterrogationCase[] cases;

    [Header("아트 (없으면 색 박스로 대체)")]
    [SerializeField] Sprite backgroundSprite;
    [SerializeField] Sprite fileSprite;

    [Header("사운드 (선택)")]
    [SerializeField] AudioClip slideSfx;
    [SerializeField] AudioClip hoverSfx;
    [SerializeField] AudioClip selectSfx;

    [Header("전환")]
    [SerializeField] string interrogationSceneName = "Chat";

    [Header("타이밍(초)")]
    [SerializeField] float lightOn = 0.5f;   // 조명이 켜지는(배경이 어둠→정상으로 밝아지는) 시간
    [SerializeField] float filesIn = 0.5f;
    [SerializeField] float selectMove = 0.35f;
    [SerializeField] float zoom = 0.5f;
    [SerializeField] float transition = 0.3f;

    static readonly Color IdleTint = new Color(0.72f, 0.72f, 0.72f, 1f); // 밝기 ~65-72%
    static readonly Color HoverTint = Color.white;
    static readonly Color GlowColor = new Color(0.3f, 0.85f, 0.9f, 1f);  // 청록 외곽광

    class FileView
    {
        public RectTransform rt;
        public InterrogationCase data;
        public Vector2 homePos;
        public Image fileImg;
        public Image caseImg;
        public Image glow;
        public CanvasGroup cg;
    }

    RectTransform boardArea;
    Image fadeOverlay;
    readonly List<FileView> files = new List<FileView>();
    FileView hovered;
    bool ready;
    bool selecting;
    AudioSource audioSrc;

    void Start()
    {
        DialogueUIUtil.EnsureEventSystem();
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        Build();
        StartCoroutine(Intro());
    }

    void Build()
    {
        var canvas = DialogueUIUtil.CreateCanvas("CaseSelectCanvas", 40);

        // 배경(어두운 취조실)
        var bg = DialogueUIUtil.CreatePanel(canvas.transform, "Board", new Color(0.05f, 0.05f, 0.06f, 1f));
        DialogueUIUtil.Stretch(bg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (backgroundSprite != null) { bg.GetComponent<Image>().sprite = backgroundSprite; bg.GetComponent<Image>().color = Color.white; }
        boardArea = bg;

        var title = DialogueUIUtil.CreateText(bg, "Title", 26, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f));
        title.font = DialogueUIUtil.KoreanFont; title.fontStyle = FontStyle.Bold;
        title.text = "사건 파일을 선택하세요";
        DialogueUIUtil.Stretch(title.rectTransform, new Vector2(0.1f, 0.88f), new Vector2(0.9f, 0.96f), Vector2.zero, Vector2.zero);

        int n = cases != null ? cases.Length : 0;
        const float spacing = 380f, fileW = 300f, fileH = 420f;

        for (int i = 0; i < n; i++)
        {
            var data = cases[i];
            float x = (i - (n - 1) / 2f) * spacing;
            float rot = n <= 1 ? 0f : Mathf.Lerp(-2f, 2f, (float)i / (n - 1));
            files.Add(CreateFile(bg, data, new Vector2(x, 0f), rot, fileW, fileH));
        }

        if (n == 0)
        {
            var hint = DialogueUIUtil.CreateText(bg, "Empty", 20, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.6f));
            hint.font = DialogueUIUtil.KoreanFont;
            hint.text = "표시할 사건이 없습니다.\nInspector의 Cases에 InterrogationCase를 추가하세요.";
            DialogueUIUtil.Stretch(hint.rectTransform, new Vector2(0.2f, 0.4f), new Vector2(0.8f, 0.6f), Vector2.zero, Vector2.zero);
        }

        // 전환용 종이색 오버레이(처음엔 투명)
        var ov = DialogueUIUtil.CreatePanel(canvas.transform, "FadeOverlay", new Color(0.93f, 0.9f, 0.82f, 0f));
        DialogueUIUtil.Stretch(ov, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fadeOverlay = ov.GetComponent<Image>();
        fadeOverlay.raycastTarget = false;
    }

    FileView CreateFile(RectTransform parent, InterrogationCase data, Vector2 home, float rot, float w, float h)
    {
        var go = new GameObject("File_" + (data != null ? data.caseId : "?"), typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.45f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = home;
        rt.localRotation = Quaternion.Euler(0, 0, rot);

        var cg = go.AddComponent<CanvasGroup>();

        // 외곽광(맨 뒤)
        var glow = ChildImage(rt, "Glow", GlowColor);
        glow.raycastTarget = false;
        DialogueUIUtil.Stretch(glow.rectTransform, Vector2.zero, Vector2.one, new Vector2(-16, -16), new Vector2(16, 16));
        var gc = glow.color; gc.a = 0f; glow.color = gc;

        // 파일(폴더) 이미지 — 전체 채움, 클릭 대상
        var fileImg = ChildImage(rt, "Paper", IdleTint);
        DialogueUIUtil.Stretch(fileImg.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (fileSprite != null) fileImg.sprite = fileSprite;
        fileImg.raycastTarget = true;

        // 사건 이미지(종이 위)
        var caseImg = ChildImage(rt, "CaseImage", IdleTint);
        caseImg.raycastTarget = false;
        DialogueUIUtil.Stretch(caseImg.rectTransform, new Vector2(0.12f, 0.34f), new Vector2(0.88f, 0.86f), Vector2.zero, Vector2.zero);
        if (data != null && data.caseImage != null) caseImg.sprite = data.caseImage;
        else caseImg.color = new Color(0.5f, 0.52f, 0.58f, 1f); // 이미지 없으면 회색 판

        // 사건 번호 / 이름
        var num = DialogueUIUtil.CreateText(rt, "Number", 18, TextAnchor.MiddleCenter, new Color(0.2f, 0.2f, 0.25f));
        num.font = DialogueUIUtil.KoreanFont; num.raycastTarget = false;
        num.text = data != null ? data.caseNumber : "";
        DialogueUIUtil.Stretch(num.rectTransform, new Vector2(0.1f, 0.22f), new Vector2(0.9f, 0.3f), Vector2.zero, Vector2.zero);

        var nameText = DialogueUIUtil.CreateText(rt, "Name", 20, TextAnchor.MiddleCenter, new Color(0.12f, 0.12f, 0.15f));
        nameText.font = DialogueUIUtil.KoreanFont; nameText.fontStyle = FontStyle.Bold; nameText.raycastTarget = false;
        nameText.text = data != null ? data.caseTitle : "";
        DialogueUIUtil.Stretch(nameText.rectTransform, new Vector2(0.08f, 0.1f), new Vector2(0.92f, 0.22f), Vector2.zero, Vector2.zero);

        var fv = new FileView { rt = rt, data = data, homePos = home, fileImg = fileImg, caseImg = caseImg, glow = glow, cg = cg };

        var pointer = go.AddComponent<CaseFilePointer>();
        pointer.onEnter = () => OnHover(fv, true);
        pointer.onExit = () => OnHover(fv, false);
        pointer.onClick = () => OnClick(fv);

        return fv;
    }

    static Image ChildImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    // ------------------------------------------------------------------
    // 인트로 / 호버 / 선택
    // ------------------------------------------------------------------
    IEnumerator Intro()
    {
        // 배경은 항상 '불투명'으로 둔다. 조명 켜짐 = alpha가 아니라 '밝기(색)'를 어둠→정상으로 올림.
        var bgImg = boardArea.GetComponent<Image>();
        Color lit = bgImg.color;                 // Build에서 정한 정상 배경색(또는 스프라이트 흰색)
        Color dark = lit * 0.22f; dark.a = 1f;   // 조명 꺼진 상태(불투명 유지)
        bgImg.color = dark;

        // 파일은 아래에 숨겨 두고(각자 CanvasGroup으로 투명 처리 → 배경엔 영향 없음)
        var starts = new Vector2[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            starts[i] = files[i].homePos + new Vector2(0, -140f);
            files[i].rt.anchoredPosition = starts[i];
            files[i].cg.alpha = 0f;
        }

        // 1) 조명 켜짐: 배경을 어둠 → 정상 밝기로 (알파는 계속 1)
        float t = 0f;
        while (t < lightOn)
        {
            t += Time.deltaTime;
            bgImg.color = Color.Lerp(dark, lit, Ease(Mathf.Clamp01(t / lightOn)));
            yield return null;
        }
        bgImg.color = lit;

        // 2) 파일이 아래에서 미끄러져 올라오며 페이드인
        if (files.Count > 0) Play(slideSfx);
        t = 0f;
        while (t < filesIn)
        {
            t += Time.deltaTime;
            float k = Ease(Mathf.Clamp01(t / filesIn));
            for (int i = 0; i < files.Count; i++)
            {
                files[i].rt.anchoredPosition = Vector2.Lerp(starts[i], files[i].homePos, k);
                files[i].cg.alpha = k;
            }
            yield return null;
        }
        for (int i = 0; i < files.Count; i++) { files[i].rt.anchoredPosition = files[i].homePos; files[i].cg.alpha = 1f; }

        ready = true;
    }

    void OnHover(FileView fv, bool entering)
    {
        if (!ready || selecting) return;
        if (entering) { hovered = fv; Play(hoverSfx); }
        else if (hovered == fv) hovered = null;
    }

    void Update()
    {
        if (!ready || selecting) return;
        foreach (var fv in files)
        {
            bool hov = fv == hovered;
            float s = Mathf.Lerp(fv.rt.localScale.x, hov ? 1.04f : 1f, Time.deltaTime * 12f);
            fv.rt.localScale = new Vector3(s, s, 1f);

            Vector2 target = fv.homePos + (hov ? new Vector2(0, 12f) : Vector2.zero);
            fv.rt.anchoredPosition = Vector2.Lerp(fv.rt.anchoredPosition, target, Time.deltaTime * 12f);

            var tint = Color.Lerp(fv.fileImg.color, hov ? HoverTint : IdleTint, Time.deltaTime * 12f);
            fv.fileImg.color = tint;
            if (fv.data != null && fv.data.caseImage != null) fv.caseImg.color = tint;

            if (fv.glow != null)
            {
                var c = fv.glow.color;
                c.a = Mathf.Lerp(c.a, hov ? 0.5f : 0f, Time.deltaTime * 12f);
                fv.glow.color = c;
            }
        }
    }

    void OnClick(FileView fv)
    {
        if (!ready || selecting) return;
        selecting = true;
        hovered = null;
        Play(selectSfx);
        StartCoroutine(SelectSequence(fv));
    }

    IEnumerator SelectSequence(FileView chosen)
    {
        // 선택하지 않은 파일은 옆으로 밀리며 암전
        foreach (var fv in files)
        {
            if (fv == chosen) continue;
            StartCoroutine(SlideAway(fv));
        }

        // 선택한 파일: 중앙으로 이동 + 1.0 → 1.25 확대
        Vector2 from = chosen.rt.anchoredPosition;
        Vector3 fromScale = chosen.rt.localScale;
        chosen.rt.SetAsLastSibling();
        float t = 0f;
        float dur = selectMove + zoom;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Ease(Mathf.Clamp01(t / dur));
            chosen.rt.anchoredPosition = Vector2.Lerp(from, Vector2.zero, k);
            float sc = Mathf.Lerp(fromScale.x, 1.25f, k);
            chosen.rt.localScale = new Vector3(sc, sc, 1f);
            chosen.fileImg.color = Color.Lerp(chosen.fileImg.color, HoverTint, Time.deltaTime * 10f);
            yield return null;
        }

        // 종이색으로 화면 전환
        yield return FadeImageAlpha(fadeOverlay, 0f, 1f, transition);

        // 사건 넘기고 취조 씬으로
        GameSession.SelectedCase = chosen.data;
        if (!string.IsNullOrEmpty(interrogationSceneName) && Application.CanStreamedLevelBeLoaded(interrogationSceneName))
            SceneManager.LoadScene(interrogationSceneName);
    }

    IEnumerator SlideAway(FileView fv)
    {
        Vector2 from = fv.rt.anchoredPosition;
        Vector2 to = from + new Vector2(Mathf.Sign(from.x == 0 ? -1 : from.x) * 500f, -60f);
        float t = 0f;
        while (t < selectMove)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / selectMove);
            fv.rt.anchoredPosition = Vector2.Lerp(from, to, k);
            fv.cg.alpha = 1f - k;
            yield return null;
        }
        fv.cg.alpha = 0f;
    }

    // ------------------------------------------------------------------
    // 유틸
    // ------------------------------------------------------------------
    void Play(AudioClip clip) { if (clip != null && audioSrc != null) audioSrc.PlayOneShot(clip); }

    static float Ease(float x) => 1f - (1f - x) * (1f - x); // easeOutQuad

    static IEnumerator FadeImageAlpha(Image img, float a, float b, float dur)
    {
        float t = 0f;
        var c = img.color;
        while (t < dur) { t += Time.deltaTime; c.a = Mathf.Lerp(a, b, dur <= 0 ? 1f : t / dur); img.color = c; yield return null; }
        c.a = b; img.color = c;
    }
}

// 파일 위 포인터 이벤트(호버/클릭)를 콜백으로 넘기는 작은 헬퍼.
public class CaseFilePointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public System.Action onEnter, onExit, onClick;
    public void OnPointerEnter(PointerEventData e) { onEnter?.Invoke(); }
    public void OnPointerExit(PointerEventData e) { onExit?.Invoke(); }
    public void OnPointerClick(PointerEventData e) { onClick?.Invoke(); }
}
