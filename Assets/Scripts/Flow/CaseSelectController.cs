using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.Video;

// 사건 선택 화면 (책상 위 사건파일 사진/영상 기반).
//   흐름: [닫힌 파일] → '사건 열기' → (여는 영상 재생) → [열린 파일 + 사건 정보] → '수사 시작' → (페이드) → 취조 씬
//
// 리소스는 모든 사건이 공용으로 쓴다(닫힌 사진 1 / 열린 사진 1 / 여는 영상 1). 사건별 정보(번호·제목·브리핑)는
// 사진 위에 UI 텍스트로 얹으므로, 사건이 늘어도 새 이미지를 만들 필요가 없다.
//
// 사용법: 빈 CaseSelect 씬 → 아무 GameObject에 이 스크립트 → Cases와 Closed/Open Sprite, Open Video를 지정.
// 리소스를 비워두면 색 박스/즉시 전환으로 대체되어 흐름은 그대로 돈다.
public class CaseSelectController : MonoBehaviour
{
    [Header("표시할 사건들")]
    [SerializeField]
    InterrogationCase[] cases;

    [Header("사건파일 리소스 (모든 사건 공용)")]
    [SerializeField]
    Sprite closedFileSprite; // 닫힌 사건파일 사진

    [SerializeField]
    Sprite openFileSprite; // 열린 사건파일 사진 (여는 영상의 마지막 프레임과 같게)

    [FormerlySerializedAs("closeVideo")]
    [SerializeField]
    VideoClip openVideo; // 파일 '여는' 영상 (없으면 크로스페이드로 대체)

    [Tooltip(
        "WebGL 대응: StreamingAssets 폴더에 넣은 영상 파일명. 지정 시 이 URL로 재생(웹·에디터 모두 동작). 비우면 위 VideoClip 사용."
    )]
    [SerializeField]
    string openVideoFileName = "CaseSelectStartVideo.mp4";

    [Header("사운드 (선택)")]
    [SerializeField]
    AudioClip openSfx;

    [SerializeField]
    AudioClip startSfx;

    [Header("전환")]
    [SerializeField]
    string interrogationSceneName = "Main";

    [SerializeField]
    string titleSceneName = "Start";

    [Header("열린 상태 정보 위치 (0~1 화면 비율, 인스펙터에서 조정)")]
    [Tooltip(
        "열린 파일 위에 표시되는 번호·제목·브리핑 블록의 위치. 왼쪽으로 옮기려면 X값을 낮추면 된다."
    )]
    [SerializeField]
    Vector2 openInfoAnchorMin = new Vector2(0.52f, 0.16f);

    [SerializeField]
    Vector2 openInfoAnchorMax = new Vector2(0.84f, 0.66f);

    [Header("도장 (Stamp Sprite 지정 시 그 이미지, 비우면 텍스트로 대체)")]
    [SerializeField]
    Sprite stampSprite;

    [SerializeField]
    Vector2 stampAnchorMin = new Vector2(0.38f, 0.64f);

    [SerializeField]
    Vector2 stampAnchorMax = new Vector2(0.64f, 0.80f);

    [Header("연출 (리소스 불필요)")]
    [SerializeField]
    bool vignette = true;

    [SerializeField]
    bool startFlicker = true;

    [SerializeField]
    bool lightBreathe = true;

    [Header("타이밍(초)")]
    [SerializeField]
    float openFade = 0.35f;

    [SerializeField]
    float endFade = 0.4f;

    static readonly Color DeskColor = new Color(0.08f, 0.11f, 0.12f, 1f);

    Image bgClosed,
        bgOpen,
        flicker;
    RawImage vignetteImg,
        videoImg;
    RectTransform closedGroup,
        openGroup,
        openInfo;
    Text closedNumber,
        closedTitle,
        openNumber,
        openTitle,
        briefingText,
        navText;
    RectTransform stamp;

    int current;
    bool busy; // 열기/전환 중 입력 잠금
    bool loaded;
    AudioSource audioSrc;

    InterrogationCase Current =>
        (cases != null && current >= 0 && current < cases.Length) ? cases[current] : null;

    void Start()
    {
        DialogueUIUtil.EnsureEventSystem();
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        Build();
        ShowClosed(instant: true);
        StartCoroutine(Intro());
    }

    // ------------------------------------------------------------------
    // UI 구성
    // ------------------------------------------------------------------
    void Build()
    {
        var canvas = DialogueUIUtil.CreateCanvas("CaseSelectCanvas", 40);

        bgClosed = FullImage(canvas.transform, "BG_Closed", closedFileSprite, DeskColor);
        bgOpen = FullImage(canvas.transform, "BG_Open", openFileSprite, DeskColor);
        SetAlpha(bgOpen, 0f);

        if (vignette)
        {
            var vGO = new GameObject("Vignette", typeof(RectTransform), typeof(RawImage));
            vGO.transform.SetParent(canvas.transform, false);
            vignetteImg = vGO.GetComponent<RawImage>();
            vignetteImg.texture = MakeVignette(256);
            vignetteImg.raycastTarget = false;
            DialogueUIUtil.Stretch(
                (RectTransform)vGO.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero
            );
        }

        // 닫힌 상태 UI (파일 중앙 위에 제목/번호)
        closedGroup = Group(canvas.transform, "ClosedGroup");
        closedNumber = Label(
            closedGroup,
            "ClosedNumber",
            20,
            new Vector2(0.30f, 0.55f),
            new Vector2(0.70f, 0.61f),
            new Color(0.85f, 0.9f, 0.85f, 0.9f),
            FontStyle.Normal
        );
        closedTitle = Label(
            closedGroup,
            "ClosedTitle",
            22,
            new Vector2(0.28f, 0.45f),
            new Vector2(0.72f, 0.55f),
            Color.white,
            FontStyle.Bold
        );
        var hint = Label(
            closedGroup,
            "Hint",
            16,
            new Vector2(0.3f, 0.4f),
            new Vector2(0.7f, 0.45f),
            new Color(1f, 1f, 1f, 0.5f),
            FontStyle.Italic
        );
        hint.text = "사건 파일을 열어 확인하세요";
        var openBtn = MakeButton(
            closedGroup,
            "OpenBtn",
            "사건 열기",
            new Vector2(0.42f, 0.12f),
            new Vector2(0.58f, 0.19f),
            new Color(0.15f, 0.28f, 0.3f, 0.95f),
            OnOpen
        );

        // 사건이 여러 개면 좌우 네비게이션
        navText = Label(
            closedGroup,
            "Nav",
            18,
            new Vector2(0.42f, 0.04f),
            new Vector2(0.58f, 0.1f),
            new Color(1f, 1f, 1f, 0.8f),
            FontStyle.Normal
        );
        var prev = MakeButton(
            closedGroup,
            "Prev",
            "◀",
            new Vector2(0.34f, 0.04f),
            new Vector2(0.4f, 0.1f),
            new Color(0.2f, 0.2f, 0.26f, 0.9f),
            () => Move(-1)
        );
        var next = MakeButton(
            closedGroup,
            "Next",
            "▶",
            new Vector2(0.6f, 0.04f),
            new Vector2(0.66f, 0.1f),
            new Color(0.2f, 0.2f, 0.26f, 0.9f),
            () => Move(1)
        );
        bool multi = cases != null && cases.Length > 1;
        navText.gameObject.SetActive(multi);
        prev.gameObject.SetActive(multi);
        next.gameObject.SetActive(multi);

        // 타이틀로 되돌아가는 길(닫힌 상태에서만 — 열린 상태에는 '← 뒤로'가 있다).
        MakeButton(
            closedGroup,
            "TitleBtn",
            "← 타이틀",
            new Vector2(0.05f, 0.9f),
            new Vector2(0.16f, 0.96f),
            new Color(0.18f, 0.18f, 0.22f, 0.9f),
            OnTitle
        );

        // 열린 상태 UI — 정보(번호/제목/브리핑/도장)를 한 컨테이너에 담아 통째로 위치 조정 가능
        openGroup = Group(canvas.transform, "OpenGroup");

        openInfo = new GameObject("OpenInfo", typeof(RectTransform)).GetComponent<RectTransform>();
        openInfo.SetParent(openGroup, false);
        DialogueUIUtil.Stretch(
            openInfo,
            openInfoAnchorMin,
            openInfoAnchorMax,
            Vector2.zero,
            Vector2.zero
        );

        openNumber = Label(
            openInfo,
            "OpenNumber",
            20,
            new Vector2(0f, 0.88f),
            new Vector2(1f, 1f),
            new Color(0.2f, 0.22f, 0.2f),
            FontStyle.Normal
        );
        openTitle = Label(
            openInfo,
            "OpenTitle",
            24,
            new Vector2(0f, 0.72f),
            new Vector2(1f, 0.88f),
            new Color(0.1f, 0.12f, 0.12f),
            FontStyle.Bold
        );
        briefingText = Label(
            openInfo,
            "Briefing",
            13,
            new Vector2(0f, 0f),
            new Vector2(1f, 0.70f),
            new Color(0.18f, 0.2f, 0.2f),
            FontStyle.Normal
        );

        briefingText.alignment = TextAnchor.UpperLeft;
        briefingText.resizeTextForBestFit = true;
        briefingText.resizeTextMaxSize = 17;
        briefingText.resizeTextMinSize = 11;
        briefingText.horizontalOverflow = HorizontalWrapMode.Wrap;
        briefingText.verticalOverflow = VerticalWrapMode.Truncate;

        stamp = BuildStamp(openGroup); // 도장은 화면 기준 위치(Stamp Anchor)로 배치

        MakeButton(
            openGroup,
            "StartBtn",
            "수사 시작",
            new Vector2(0.56f, 0.06f),
            new Vector2(0.74f, 0.14f),
            new Color(0.45f, 0.12f, 0.14f, 0.95f),
            OnStart
        );
        MakeButton(
            openGroup,
            "BackBtn",
            "← 뒤로",
            new Vector2(0.05f, 0.9f),
            new Vector2(0.16f, 0.96f),
            new Color(0.18f, 0.18f, 0.22f, 0.9f),
            OnBack
        );

        if (cases == null || cases.Length == 0)
        {
            var empty = Label(
                closedGroup,
                "Empty",
                20,
                new Vector2(0.2f, 0.42f),
                new Vector2(0.8f, 0.6f),
                new Color(1f, 0.6f, 0.6f),
                FontStyle.Bold
            );
            empty.text =
                "표시할 사건이 없습니다.\nInspector의 Cases에 InterrogationCase를 추가하세요.";
            hint.gameObject.SetActive(false);
            openBtn.gameObject.SetActive(false);
        }

        // 형광등 깜빡임 오버레이
        flicker = FullImage(canvas.transform, "Flicker", null, new Color(0f, 0f, 0f, 0f));
        flicker.raycastTarget = false;

        // 영상 재생용(처음엔 숨김) — 항상 맨 위에서 재생
        var vidGO = new GameObject("Video", typeof(RectTransform), typeof(RawImage));
        vidGO.transform.SetParent(canvas.transform, false);
        videoImg = vidGO.GetComponent<RawImage>();
        videoImg.raycastTarget = false;
        DialogueUIUtil.Stretch(
            (RectTransform)vidGO.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        vidGO.SetActive(false);

        RefreshCaseText();
    }

    void RefreshCaseText()
    {
        var c = Current;
        closedNumber.text = c != null ? c.caseNumber : "";
        closedTitle.text = c != null ? c.caseTitle : "";
        openNumber.text = c != null ? c.caseNumber : "";
        openTitle.text = c != null ? c.caseTitle : "";
        briefingText.text = c != null ? c.briefing : "";
        if (navText != null && cases != null && cases.Length > 0)
            navText.text = (current + 1) + " / " + cases.Length;
    }

    // ------------------------------------------------------------------
    // 상태 전환
    // ------------------------------------------------------------------
    void ShowClosed(bool instant)
    {
        closedGroup.gameObject.SetActive(true);
        openGroup.gameObject.SetActive(false);
        if (instant)
            SetAlpha(bgOpen, 0f);
    }

    void OnOpen()
    {
        if (busy || Current == null)
            return;
        busy = true;
        Play(openSfx);
        closedGroup.gameObject.SetActive(false);

        bool hasVideo = !string.IsNullOrEmpty(openVideoFileName) || openVideo != null;
        if (hasVideo)
            PlayOpenVideo();
        else
            StartCoroutine(OpenCrossfade());
    }

    // 여는 영상 재생 → 끝나면 열린 사진 + 정보 표시
    // WebGL 호환: StreamingAssets 파일을 URL로 재생하고, RenderTexture를 원본 해상도로 만들어 화질 손실을 막는다.
    void PlayOpenVideo()
    {
        // videoImg는 아직 켜지 않는다 — 텍스처가 없으면 RawImage가 '흰색'으로 보인다.
        // Prepare 끝나고 첫 프레임이 RenderTexture에 들어온 뒤에 켠다(그동안 뒤의 닫힌 파일이 보임).
        var vp = gameObject.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = false;
        vp.waitForFirstFrame = true;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.audioOutputMode = VideoAudioOutputMode.Direct;

        if (!string.IsNullOrEmpty(openVideoFileName))
        {
            // WebGL은 VideoClip 재생이 안 되므로 StreamingAssets URL로 재생(에디터에서도 동작)
            vp.source = VideoSource.Url;
            vp.url = Application.streamingAssetsPath + "/" + openVideoFileName;
        }
        else
        {
            vp.source = VideoSource.VideoClip;
            vp.clip = openVideo;
        }

        bool done = false;
        System.Action finish = () =>
        {
            if (done)
                return;
            done = true;
            OnOpenVideoDone(vp);
        };

        vp.prepareCompleted += src =>
        {
            // 원본 해상도로 RenderTexture 생성 → 다운스케일 없이 선명하게
            int w = (int)src.width,
                h = (int)src.height;
            if (w <= 0 || h <= 0)
            {
                w = 1920;
                h = 1080;
            }
            var rt = new RenderTexture(w, h, 0);
            src.targetTexture = rt;
            videoImg.texture = rt;
            src.Play();
            StartCoroutine(RevealVideoNextFrame()); // 첫 프레임 그려진 뒤 켜기(흰/검 깜빡임 방지)
            StartCoroutine(DelayAction((float)src.length + 1.5f, finish)); // 끝 이벤트 누락 대비
        };
        vp.loopPointReached += _ => finish();
        vp.errorReceived += (_, __) => finish();
        vp.Prepare();

        // 준비 자체가 안 끝나는 경우(웹 로딩 실패 등) 백업 타임아웃
        StartCoroutine(
            DelayAction(
                8f,
                () =>
                {
                    if (!done && (vp == null || !vp.isPrepared))
                        finish();
                }
            )
        );
    }

    // 첫 프레임이 RenderTexture에 렌더된 다음 프레임에 videoImg를 켠다(흰/검 깜빡임 방지).
    IEnumerator RevealVideoNextFrame()
    {
        yield return null;
        if (videoImg != null)
            videoImg.gameObject.SetActive(true);
    }

    void OnOpenVideoDone(VideoPlayer vp)
    {
        SetAlpha(bgOpen, 1f); // 열린 사진 고정
        videoImg.gameObject.SetActive(false);
        if (vp != null)
        {
            vp.Stop();
            Destroy(vp);
        }
        if (videoImg.texture is RenderTexture rt) // RenderTexture 정리(메모리)
        {
            videoImg.texture = null;
            rt.Release();
            Destroy(rt);
        }
        StartCoroutine(ShowOpenInfo());
    }

    IEnumerator ShowOpenInfo()
    {
        openGroup.gameObject.SetActive(true);
        SetGroupAlpha(openGroup, 0f);
        yield return Lerp(openFade, k => SetGroupAlpha(openGroup, k));
        SetGroupAlpha(openGroup, 1f);
        yield return StampThud();
        busy = false;
    }

    // 영상이 없을 때: 닫힌 → 열린 사진 크로스페이드
    IEnumerator OpenCrossfade()
    {
        openGroup.gameObject.SetActive(true);
        SetGroupAlpha(openGroup, 0f);
        yield return Lerp(
            openFade,
            k =>
            {
                SetAlpha(bgOpen, k);
                SetGroupAlpha(openGroup, k);
            }
        );
        SetAlpha(bgOpen, 1f);
        SetGroupAlpha(openGroup, 1f);
        yield return StampThud();
        busy = false;
    }

    void OnBack()
    {
        if (busy)
            return;
        busy = true;
        StartCoroutine(BackSequence());
    }

    IEnumerator BackSequence()
    {
        yield return Lerp(
            openFade,
            k =>
            {
                SetAlpha(bgOpen, 1f - k);
                SetGroupAlpha(openGroup, 1f - k);
            }
        );
        ShowClosed(instant: true);
        busy = false;
    }

    void Move(int dir)
    {
        if (busy || cases == null || cases.Length == 0)
            return;
        current = (current + dir + cases.Length) % cases.Length;
        RefreshCaseText();
    }

    // 수사 시작 → (여는 영상은 이미 열 때 썼으므로) 검은 페이드 후 취조 씬으로
    void OnStart()
    {
        if (busy || Current == null)
            return;
        busy = true;
        Play(startSfx);
        GameSession.SelectedCase = Current;
        StartCoroutine(FadeAndLoad());
    }

    IEnumerator FadeAndLoad()
    {
        yield return Lerp(endFade, k => SetAlpha(flicker, k)); // 검게 페이드
        LoadInterrogation();
    }

    void LoadInterrogation()
    {
        if (loaded)
            return;
        loaded = true;
        GameSession.SelectedCase = Current;

        if (
            !string.IsNullOrEmpty(interrogationSceneName)
            && Application.CanStreamedLevelBeLoaded(interrogationSceneName)
        )
        {
            SceneManager.LoadScene(interrogationSceneName);
            return;
        }

        // 씬을 못 찾으면 검은 화면에 그대로 멈춘다 — 원인을 남기고 화면을 되돌린다.
        Debug.LogError(
            "[CaseSelectController] '"
                + interrogationSceneName
                + "' 씬을 로드할 수 없습니다. Build Settings에 등록되어 있는지 확인하세요."
        );
        loaded = false;
        SetAlpha(flicker, 0f);
        busy = false;
    }

    // 타이틀 화면으로 되돌아간다.
    void OnTitle()
    {
        if (busy)
            return;
        busy = true;
        GameSession.SelectedCase = null;
        StartCoroutine(FadeAndLoadTitle());
    }

    IEnumerator FadeAndLoadTitle()
    {
        yield return Lerp(endFade, k => SetAlpha(flicker, k));
        if (
            !string.IsNullOrEmpty(titleSceneName)
            && Application.CanStreamedLevelBeLoaded(titleSceneName)
        )
        {
            SceneManager.LoadScene(titleSceneName);
            yield break;
        }
        Debug.LogError(
            "[CaseSelectController] '"
                + titleSceneName
                + "' 씬을 로드할 수 없습니다. Build Settings에 등록되어 있는지 확인하세요."
        );
        SetAlpha(flicker, 0f);
        busy = false;
    }

    // ------------------------------------------------------------------
    // 연출
    // ------------------------------------------------------------------
    IEnumerator Intro()
    {
        if (startFlicker)
        {
            // 형광등이 몇 번 깜빡이며 들어오는 느낌
            float[] seq = { 0.85f, 0f, 0.6f, 0f, 0.9f, 0.1f, 0f };
            foreach (var a in seq)
            {
                SetAlpha(flicker, a);
                yield return new WaitForSeconds(0.05f);
            }
            SetAlpha(flicker, 0f);
        }
    }

    void Update()
    {
        // 블라인드 사이로 드는 빛이 아주 미세하게 호흡하는 느낌(배경 밝기 ±4%)
        if (lightBreathe && closedFileSprite != null)
        {
            float b = 0.96f + 0.04f * Mathf.Sin(Time.time * 0.8f);
            Tint(bgClosed, b);
            if (openFileSprite != null)
                Tint(bgOpen, b);
        }
    }

    IEnumerator StampThud()
    {
        if (stamp == null)
            yield break;
        stamp.gameObject.SetActive(true);
        var cg = stamp.GetComponent<CanvasGroup>();
        if (cg != null)
            cg.alpha = 0f;
        yield return Lerp(
            0.22f,
            k =>
            {
                float s = Mathf.Lerp(1.5f, 1f, Ease(k));
                stamp.localScale = new Vector3(s, s, 1f);
                if (cg != null)
                    cg.alpha = k;
            }
        );
        stamp.localScale = Vector3.one;
        if (cg != null)
            cg.alpha = 1f;
    }

    RectTransform BuildStamp(RectTransform parent)
    {
        var go = new GameObject("Stamp", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));

        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;

        DialogueUIUtil.Stretch(rt, stampAnchorMin, stampAnchorMax, Vector2.zero, Vector2.zero);

        rt.localRotation = Quaternion.Euler(0, 0, -8f);

        // 도장 뒤의 빨간 배경
        var background = go.GetComponent<Image>();
        background.sprite = null;
        background.color = new Color(0.55f, 0.06f, 0.06f, 0.55f);
        background.raycastTarget = false;

        if (stampSprite != null)
        {
            // 빨간 배경 위에 도장 이미지 배치
            var imageGO = new GameObject("StampImage", typeof(RectTransform), typeof(Image));

            imageGO.transform.SetParent(rt, false);

            var imageRT = imageGO.GetComponent<RectTransform>();

            // 배경 가장자리가 조금 보이도록 여백 설정
            DialogueUIUtil.Stretch(
                imageRT,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 8f),
                new Vector2(-8f, -8f)
            );

            var stampImage = imageGO.GetComponent<Image>();
            stampImage.sprite = stampSprite;
            stampImage.preserveAspect = true;
            stampImage.color = Color.white;
            stampImage.raycastTarget = false;

            background.color = new Color(0.55f, 0.06f, 0.06f, 0f);
        }
        else
        {
            // 사진이 없을 때 텍스트 도장
            var t = Label(
                rt,
                "StampText",
                24,
                Vector2.zero,
                Vector2.one,
                new Color(1f, 0.82f, 0.75f, 1f),
                FontStyle.Bold
            );

            t.text = "사건 배정";

            var outline = t.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.3f, 0.02f, 0.02f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        go.SetActive(false);
        return rt;
    }

    // ------------------------------------------------------------------
    // 유틸
    // ------------------------------------------------------------------
    static Image FullImage(Transform parent, string name, Sprite sprite, Color fallback)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        if (sprite != null)
        {
            img.sprite = sprite;
            img.color = Color.white;
        }
        else
            img.color = fallback;
        DialogueUIUtil.Stretch(
            (RectTransform)go.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        return img;
    }

    RectTransform Group(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        DialogueUIUtil.Stretch(
            (RectTransform)go.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        return (RectTransform)go.transform;
    }

    Text Label(
        RectTransform parent,
        string name,
        int size,
        Vector2 aMin,
        Vector2 aMax,
        Color color,
        FontStyle style
    )
    {
        var t = DialogueUIUtil.CreateText(parent, name, size, TextAnchor.MiddleCenter, color);
        t.font = DialogueUIUtil.KoreanFont;
        t.fontStyle = style;
        t.raycastTarget = false;
        DialogueUIUtil.Stretch(t.rectTransform, aMin, aMax, Vector2.zero, Vector2.zero);
        return t;
    }

    Button MakeButton(
        RectTransform parent,
        string name,
        string label,
        Vector2 aMin,
        Vector2 aMax,
        Color color,
        UnityEngine.Events.UnityAction onClick
    )
    {
        var btn = DialogueUIUtil.CreateButton(parent, name, label, color);
        var lbl = btn.GetComponentInChildren<Text>();
        if (lbl != null)
        {
            lbl.font = DialogueUIUtil.KoreanFont;
            lbl.fontSize = 22;
        }
        DialogueUIUtil.Stretch(
            btn.GetComponent<RectTransform>(),
            aMin,
            aMax,
            Vector2.zero,
            Vector2.zero
        );
        btn.onClick.AddListener(onClick);
        return btn;
    }

    void Play(AudioClip clip)
    {
        if (clip != null && audioSrc != null)
            audioSrc.PlayOneShot(clip);
    }

    static void SetAlpha(Graphic g, float a)
    {
        if (g == null)
            return;
        var c = g.color;
        c.a = a;
        g.color = c;
    }

    static void SetGroupAlpha(RectTransform group, float a)
    {
        var cg = group.GetComponent<CanvasGroup>();
        if (cg != null)
            cg.alpha = a;
    }

    static void Tint(Image img, float b)
    {
        if (img == null)
            return;
        var c = img.color;
        img.color = new Color(b, b, b, c.a);
    }

    static float Ease(float x) => 1f - (1f - x) * (1f - x);

    IEnumerator Lerp(float dur, System.Action<float> step)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            step(Mathf.Clamp01(dur <= 0 ? 1f : t / dur));
            yield return null;
        }
        step(1f);
    }

    IEnumerator DelayAction(float seconds, System.Action action)
    {
        yield return new WaitForSeconds(seconds);
        action();
    }

    Texture2D MakeVignette(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        float c = size / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c; // 0(중앙)~1.41(모서리)
            float a = Mathf.Clamp01((d - 0.55f) / 0.75f);
            a = a * a * 0.8f;
            tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }
}
