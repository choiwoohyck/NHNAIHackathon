using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

// 컷을 순서대로 이어 붙여 재생하는 전체화면 플레이어.
//
// 한 컷은 그림(영상 또는 정지컷) + 대사(선택)로 이루어진다.
//   - 영상   : StreamingAssets에 넣어둔 파일명 (WebGL에서도 되도록 VideoClip이 아니라 URL로 재생)
//   - 정지컷 : Sprite
//   - 대사   : 화면 아래 대사창. 대사가 있으면 스페이스바(또는 클릭)를 누를 때까지 기다린다.
//
// 전환은 전부 암전을 거친다: 컷이 끝나면 검게 내렸다가, 다음 컷의 그림이 준비된 뒤에 올린다.
// 덕분에 영상이 끝나는 순간 화면이 비거나 그림이 툭 바뀌는 일이 없다.
// 정지컷은 아주 천천히 확대되어(Ken Burns) 멈춘 그림처럼 보이지 않게 한다.
//
// 영상이 준비되지 않거나(웹 로딩 실패, 코덱 문제, 배치모드 등) 도중에 오류가 나면 그 컷은
// 건너뛰고 다음으로 넘어간다 — 어떤 경우에도 컷씬에서 멈추지 않는 것이 우선이다.
public class CutscenePlayer : MonoBehaviour
{
    public struct Step
    {
        public string videoFileName;   // StreamingAssets 파일명 (비우면 정지컷)
        public Sprite still;           // 정지컷 이미지
        public float seconds;          // >0 이면 이 시간 뒤 자동 진행, <=0 이면 입력 대기
        public string speaker;
        public string line;
        public AudioClip sfx;          // 이 컷이 시작될 때 한 번 재생
        public AudioClip bgm;          // 지정하면 이 컷부터 배경음을 갈아끼운다(크로스페이드)
        public bool muteVideoAudio;    // 영상에 들어 있는 소리를 죽인다

        public bool IsVideo => !string.IsNullOrEmpty(videoFileName);
        public bool HasLine => !string.IsNullOrEmpty(line);

        public static Step Video(string fileName) =>
            new Step { videoFileName = fileName };

        /// <summary>영상 위에 대사를 얹는다. 영상이 끝나도 대사를 읽을 때까지 기다린다.</summary>
        public static Step Video(string fileName, string speaker, string line) =>
            new Step { videoFileName = fileName, speaker = speaker, line = line };

        public static Step Still(Sprite sprite, float seconds) =>
            new Step { still = sprite, seconds = seconds };

        /// <summary>정지컷 + 대사. 입력이 있을 때까지 머문다.</summary>
        public static Step Dialogue(Sprite sprite, string speaker, string line) =>
            new Step { still = sprite, speaker = speaker, line = line };
    }

    const float PrepareTimeout = 8f;     // 영상 준비가 끝나지 않을 때의 백업 타임아웃
    const float MinLineSeconds = 0.4f;   // 대사가 뜨자마자 넘어가는 것을 막는다
    const float FadeOutSeconds = 0.22f;  // 컷을 마치며 암전
    const float FadeInSeconds = 0.30f;   // 다음 컷을 밝히며
    const float BgmFadeSeconds = 0.6f;
    const float KenBurnsScale = 0.04f;   // 정지컷이 서서히 커지는 정도
    const float KenBurnsSeconds = 8f;

    Canvas canvas;
    RawImage videoImage;
    Image stillImage;
    Image fadeOverlay;
    RectTransform dialogueBox;
    CanvasGroup dialogueGroup;
    Text speakerText;
    Text lineText;
    Text continueHint;

    System.Action onComplete;
    bool finished;
    bool advanceRequested;
    bool keepBgmAfterFinish;

    AudioSource bgmSource;
    AudioSource sfxSource;
    float bgmBaseVolume;
    Coroutine bgmRoutine;

    float stillShownAt = -1f;

    public bool IsPlaying => !finished;

    /// <summary>
    /// 컷들을 순서대로 재생하고, 전부 끝나거나 건너뛰면 onComplete를 부른다.
    /// keepBgm이 true면 컷씬이 끝나도 배경음을 끊지 않는다(결과 화면까지 곡을 이어갈 때).
    /// </summary>
    public void Play(IList<Step> steps, System.Action onComplete, AudioClip bgm = null, float bgmVolume = 0.35f,
                     bool keepBgm = false)
    {
        this.onComplete = onComplete;
        keepBgmAfterFinish = keepBgm;

        if (steps == null || steps.Count == 0)
        {
            Finish();
            return;
        }

        BuildUI();
        BuildAudio(bgm, bgmVolume);
        StartCoroutine(Run(steps));
    }

    IEnumerator Run(IList<Step> steps)
    {
        SetFade(1f);   // 첫 컷도 암전에서 밝아지며 시작한다

        foreach (var step in steps)
        {
            if (finished) yield break;

            if (step.bgm != null) StartBgmSwitch(step.bgm);
            if (step.sfx != null && sfxSource != null) sfxSource.PlayOneShot(step.sfx);
            ShowLine(step);

            if (step.IsVideo) yield return PlayVideo(step.videoFileName, step.muteVideoAudio);
            else yield return ShowStill(step);

            // 대사가 있으면 그림이 끝나도 읽을 때까지 기다린다.
            if (!finished && step.HasLine && step.seconds <= 0f) yield return WaitForAdvance();
            if (finished) yield break;

            // 다음 컷으로 넘어가기 전에 암전 — 그림이 바뀌는 순간을 가린다.
            yield return FadeTo(1f, FadeOutSeconds);
            ClearVisuals();
        }

        Finish();
    }

    // ------------------------------------------------------------------
    // 대사
    // ------------------------------------------------------------------
    void ShowLine(Step step)
    {
        bool has = step.HasLine;
        dialogueBox.gameObject.SetActive(has);
        if (!has) return;

        speakerText.text = step.speaker ?? "";
        speakerText.gameObject.SetActive(!string.IsNullOrEmpty(step.speaker));
        lineText.text = step.line;
        continueHint.gameObject.SetActive(step.seconds <= 0f);
        dialogueGroup.alpha = 1f;
    }

    IEnumerator WaitForAdvance()
    {
        advanceRequested = false;

        float shown = 0f;
        while (shown < MinLineSeconds)
        {
            if (finished) yield break;
            shown += Time.deltaTime;
            yield return null;
        }

        while (!advanceRequested && !finished) yield return null;
        advanceRequested = false;
    }

    void RequestAdvance() => advanceRequested = true;

    void Update()
    {
        if (finished) return;

        if (SpacePressed()) RequestAdvance();

        // 눌러야 넘어간다는 걸 놓치지 않도록 안내를 깜빡인다.
        if (continueHint != null && continueHint.gameObject.activeSelf)
        {
            var c = continueHint.color;
            c.a = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f));
            continueHint.color = c;
        }

        // 정지컷을 아주 느리게 확대해 화면이 죽어 보이지 않게 한다.
        if (stillImage != null && stillImage.gameObject.activeSelf && stillShownAt >= 0f)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - stillShownAt) / KenBurnsSeconds);
            float s = 1f + KenBurnsScale * t;
            stillImage.rectTransform.localScale = new Vector3(s, s, 1f);
        }
    }

    // Active Input Handling이 New 전용인 프로젝트에서도 스페이스바가 먹도록 양쪽을 다 본다.
    static bool SpacePressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Space);
#elif ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#else
        return false;
#endif
    }

    // ------------------------------------------------------------------
    // 영상 한 컷
    // ------------------------------------------------------------------
    IEnumerator PlayVideo(string fileName, bool muteAudio)
    {
        var vp = gameObject.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = false;
        vp.waitForFirstFrame = true;
        vp.renderMode = VideoRenderMode.RenderTexture;
        // 영상에 붙은 소리를 쓰지 않는 컷은 트랙 자체를 끈다(음소거보다 확실하다).
        vp.audioOutputMode = muteAudio ? VideoAudioOutputMode.None : VideoAudioOutputMode.Direct;
        vp.source = VideoSource.Url;
        vp.url = Application.streamingAssetsPath + "/" + fileName;

        bool done = false;
        RenderTexture rt = null;

        vp.prepareCompleted += src =>
        {
            // 원본 해상도로 RenderTexture를 만들어 다운스케일 없이 선명하게 재생한다.
            int w = (int)src.width, h = (int)src.height;
            if (w <= 0 || h <= 0) { w = 1280; h = 720; }

            rt = new RenderTexture(w, h, 0);
            src.targetTexture = rt;
            videoImage.texture = rt;
            src.Play();
            StartCoroutine(RevealVideoNextFrame());
        };
        vp.loopPointReached += _ => done = true;
        vp.errorReceived += (_, msg) =>
        {
            Debug.LogWarning("[Cutscene] '" + fileName + "' 재생 오류: " + msg);
            done = true;
        };
        vp.Prepare();

        // 준비가 끝나길 기다린다. 실패하면 이 컷은 건너뛴다.
        float waited = 0f;
        while (!done && !vp.isPrepared && waited < PrepareTimeout)
        {
            if (finished) break;
            waited += Time.deltaTime;
            yield return null;
        }

        if (!done && !vp.isPrepared)
        {
            Debug.LogWarning("[Cutscene] '" + fileName + "' 준비 시간 초과 — 이 컷을 건너뜁니다.");
            done = true;
            yield return FadeTo(0f, FadeInSeconds);   // 대사만이라도 읽히게 화면은 올린다
        }

        // 재생이 끝나길 기다린다(끝 이벤트가 누락되는 경우를 대비해 길이 기준 여유를 둔다).
        float limit = vp.isPrepared ? (float)vp.length + 1.5f : 0f;
        float played = 0f;
        while (!done && played < limit)
        {
            if (finished) break;
            played += Time.deltaTime;
            yield return null;
        }

        // 마지막 프레임을 화면에 둔 채로 빠져나간다 — 정리는 암전이 끝난 뒤 ClearVisuals가 한다.
        pendingPlayer = vp;
        pendingTexture = rt;
    }

    VideoPlayer pendingPlayer;
    RenderTexture pendingTexture;

    // 첫 프레임이 RenderTexture에 그려진 뒤에 켜고, 그때 화면을 밝힌다.
    IEnumerator RevealVideoNextFrame()
    {
        yield return null;
        if (videoImage == null || finished) yield break;

        videoImage.gameObject.SetActive(true);
        yield return FadeTo(0f, FadeInSeconds);
    }

    // 암전이 끝난 뒤 이전 컷의 흔적을 지운다.
    void ClearVisuals()
    {
        if (videoImage != null)
        {
            videoImage.gameObject.SetActive(false);
            videoImage.texture = null;
        }
        if (stillImage != null)
        {
            stillImage.gameObject.SetActive(false);
            stillImage.rectTransform.localScale = Vector3.one;
        }
        stillShownAt = -1f;

        if (pendingPlayer != null)
        {
            pendingPlayer.Stop();
            Destroy(pendingPlayer);
            pendingPlayer = null;
        }
        if (pendingTexture != null)
        {
            pendingTexture.Release();
            Destroy(pendingTexture);
            pendingTexture = null;
        }
    }

    // ------------------------------------------------------------------
    // 정지컷 한 컷
    // ------------------------------------------------------------------
    IEnumerator ShowStill(Step step)
    {
        if (step.still == null) yield break;

        stillImage.sprite = step.still;
        stillImage.color = Color.white;
        stillImage.rectTransform.localScale = Vector3.one;
        stillImage.gameObject.SetActive(true);
        stillShownAt = Time.unscaledTime;

        yield return FadeTo(0f, FadeInSeconds);

        if (step.seconds > 0f)
        {
            float t = 0f;
            while (t < step.seconds)
            {
                if (finished) break;
                t += Time.deltaTime;
                yield return null;
            }
        }
    }

    // ------------------------------------------------------------------
    // 암전
    // ------------------------------------------------------------------
    void SetFade(float a)
    {
        if (fadeOverlay == null) return;
        var c = fadeOverlay.color;
        c.a = Mathf.Clamp01(a);
        fadeOverlay.color = c;
    }

    IEnumerator FadeTo(float target, float duration)
    {
        if (fadeOverlay == null) yield break;

        float from = fadeOverlay.color.a;
        if (Mathf.Approximately(from, target)) yield break;

        float t = 0f;
        while (t < duration)
        {
            if (finished) yield break;
            t += Time.unscaledDeltaTime;
            SetFade(Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t / duration)));
            yield return null;
        }
        SetFade(target);
    }

    // ------------------------------------------------------------------
    // 소리
    // ------------------------------------------------------------------
    void BuildAudio(AudioClip bgm, float bgmVolume)
    {
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        bgmBaseVolume = bgmVolume;
        if (bgm == null) return;

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.clip = bgm;
        bgmSource.loop = true;
        bgmSource.volume = bgmVolume;
        bgmSource.playOnAwake = false;
        bgmSource.Play();
    }

    // 결과가 갈리는 컷에서 배경음을 바꾼다. 뚝 끊지 않고 서로 넘겨준다.
    void StartBgmSwitch(AudioClip next)
    {
        if (next == null || bgmSource == null || bgmSource.clip == next) return;
        if (bgmRoutine != null) StopCoroutine(bgmRoutine);
        bgmRoutine = StartCoroutine(SwitchBgm(next));
    }

    IEnumerator SwitchBgm(AudioClip next)
    {
        float half = BgmFadeSeconds * 0.5f;
        float from = bgmSource.volume;

        float t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            bgmSource.volume = Mathf.Lerp(from, 0f, t / half);
            yield return null;
        }

        bgmSource.clip = next;
        bgmSource.Play();

        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            bgmSource.volume = Mathf.Lerp(0f, bgmBaseVolume, t / half);
            yield return null;
        }
        bgmSource.volume = bgmBaseVolume;
    }

    // ------------------------------------------------------------------
    void Skip() => StartCoroutine(SkipRoutine());

    // 건너뛰기도 툭 끊지 않고 한 번 접었다 넘긴다.
    IEnumerator SkipRoutine()
    {
        if (finished) yield break;
        yield return FadeTo(1f, FadeOutSeconds);
        Finish();
    }

    void Finish()
    {
        if (finished) return;
        finished = true;

        var callback = onComplete;
        onComplete = null;

        // 배경음은 남겨둘 수 있다 — AudioSource는 이 오브젝트에 붙어 있으므로 컷씬이 사라져도 계속 울린다.
        if (bgmSource != null)
        {
            if (keepBgmAfterFinish) bgmSource.volume = bgmBaseVolume;
            else bgmSource.Stop();
        }

        // 건너뛰기로 중간에 끊기면 재생 코루틴이 정리까지 못 가므로 여기서 직접 치운다.
        foreach (var vp in GetComponents<VideoPlayer>())
        {
            var rt = vp.targetTexture;
            vp.targetTexture = null;
            vp.Stop();
            Destroy(vp);
            if (rt != null) { rt.Release(); Destroy(rt); }
        }
        pendingPlayer = null;
        pendingTexture = null;

        if (canvas != null) Destroy(canvas.gameObject);

        callback?.Invoke();
        Destroy(this);   // 재생이 끝난 플레이어는 남겨둘 이유가 없다
    }

    // ------------------------------------------------------------------
    // 화면 구성
    // ------------------------------------------------------------------
    void BuildUI()
    {
        DialogueUIUtil.EnsureEventSystem();
        canvas = DialogueUIUtil.CreateCanvas("CutsceneCanvas", 70);

        var background = DialogueUIUtil.CreatePanel(canvas.transform, "Black", Color.black);
        DialogueUIUtil.Stretch(background, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // 정지컷 — 화면비를 유지한 채 가운데 맞춤
        var stillGO = new GameObject("Still", typeof(RectTransform), typeof(Image));
        stillGO.transform.SetParent(canvas.transform, false);
        stillImage = stillGO.GetComponent<Image>();
        stillImage.preserveAspect = true;
        stillImage.raycastTarget = false;
        DialogueUIUtil.Stretch((RectTransform)stillGO.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        stillGO.SetActive(false);

        // 영상 — 첫 프레임이 그려지기 전까지 꺼 둔다
        var videoGO = new GameObject("Video", typeof(RectTransform), typeof(RawImage));
        videoGO.transform.SetParent(canvas.transform, false);
        videoImage = videoGO.GetComponent<RawImage>();
        videoImage.raycastTarget = false;
        DialogueUIUtil.Stretch((RectTransform)videoGO.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        videoGO.SetActive(false);

        // 화면 아무 곳이나 눌러도 다음으로 (건너뛰기 버튼보다 먼저 만들어 아래에 깔린다)
        var advance = DialogueUIUtil.CreateButton(canvas.transform, "AdvanceArea", "", new Color(0f, 0f, 0f, 0f));
        advance.transition = Selectable.Transition.None;
        DialogueUIUtil.Stretch(advance.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        advance.onClick.AddListener(RequestAdvance);

        BuildDialogueBox();

        // 암전막 — 그림과 대사 위, 건너뛰기 아래
        fadeOverlay = DialogueUIUtil.CreatePanel(canvas.transform, "Fade", new Color(0f, 0f, 0f, 1f))
                                    .GetComponent<Image>();
        fadeOverlay.raycastTarget = false;
        DialogueUIUtil.Stretch(fadeOverlay.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var skip = DialogueUIUtil.CreateButton(canvas.transform, "SkipBtn", "건너뛰기",
                                               new Color(0.15f, 0.15f, 0.18f, 0.85f));
        var skipLabel = skip.GetComponentInChildren<Text>();
        if (skipLabel != null) { skipLabel.font = DialogueUIUtil.KoreanFont; skipLabel.fontSize = 18; }
        var srt = skip.GetComponent<RectTransform>();
        srt.anchorMin = srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(1f, 1f);
        srt.sizeDelta = new Vector2(150, 44);
        srt.anchoredPosition = new Vector2(-24, -24);
        skip.onClick.AddListener(Skip);
    }

    void BuildDialogueBox()
    {
        dialogueBox = DialogueUIUtil.CreatePanel(canvas.transform, "CutsceneDialogue", new Color(0f, 0f, 0f, 0.78f));
        DialogueUIUtil.Stretch(dialogueBox, new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.26f), Vector2.zero, Vector2.zero);
        var boxImage = dialogueBox.GetComponent<Image>();
        if (boxImage != null) boxImage.raycastTarget = false;   // 클릭은 뒤의 진행 영역이 받는다
        dialogueGroup = dialogueBox.gameObject.AddComponent<CanvasGroup>();

        speakerText = DialogueUIUtil.CreateText(dialogueBox, "Speaker", 24, TextAnchor.UpperLeft,
                                                new Color(1f, 0.82f, 0.42f));
        speakerText.font = DialogueUIUtil.KoreanFont;
        speakerText.fontStyle = FontStyle.Bold;
        speakerText.raycastTarget = false;
        DialogueUIUtil.Stretch(speakerText.rectTransform, new Vector2(0f, 0.68f), new Vector2(1f, 1f),
                               new Vector2(28, 0), new Vector2(-28, -8));

        lineText = DialogueUIUtil.CreateText(dialogueBox, "Line", 26, TextAnchor.UpperLeft,
                                             new Color(0.96f, 0.97f, 0.98f));
        lineText.font = DialogueUIUtil.KoreanFont;
        lineText.raycastTarget = false;
        DialogueUIUtil.Stretch(lineText.rectTransform, new Vector2(0f, 0.16f), new Vector2(1f, 0.70f),
                               new Vector2(28, 0), new Vector2(-28, 0));

        continueHint = DialogueUIUtil.CreateText(dialogueBox, "ContinueHint", 20, TextAnchor.LowerRight,
                                                 new Color(1f, 0.86f, 0.5f, 0.95f));
        continueHint.font = DialogueUIUtil.KoreanFont;
        continueHint.fontStyle = FontStyle.Bold;
        continueHint.text = "[ Space ] 를 눌러 계속";
        continueHint.raycastTarget = false;
        DialogueUIUtil.Stretch(continueHint.rectTransform, new Vector2(0.4f, 0f), new Vector2(1f, 0.18f),
                               Vector2.zero, new Vector2(-20, 0));

        dialogueBox.gameObject.SetActive(false);
    }
}
