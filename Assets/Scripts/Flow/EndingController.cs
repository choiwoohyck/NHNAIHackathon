using System.Collections;
using System.Collections.Generic;
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

    // ------------------------------------------------------------------
    // 엔딩 컷씬 — 결과 화면 앞에 재생한다. 파일은 StreamingAssets에 둔다.
    //
    //   공통  : 지목 장면 → 지목한 인물의 얼굴(정지컷) → 판결 장면
    //   유죄  : guiltyClips 를 순서대로
    //   무죄  : 지목당한 사람이 풀려나는 장면 (누구를 지목했는지에 따라 다르다)
    // ------------------------------------------------------------------
    [System.Serializable]
    public class SuspectClip
    {
        [Tooltip("용의자 id (SuspectData.id)")] public string suspectId;
        [Tooltip("StreamingAssets 안의 파일명")] public string fileName;
    }

    [Header("엔딩 컷씬 (StreamingAssets 파일명, 비우면 해당 컷 생략)")]
    [SerializeField] bool playCutscene = true;

    [Header("법정 스틸 (비우면 Resources/Cutscene 에서 찾는다)")]
    [Tooltip("법정 전경 — \"존경하는 재판장님\"")]
    [SerializeField] Sprite courtWideSprite;

    [Tooltip("증거를 제시하는 장면. 비어 있으면 법정 전경으로 대체한다.")]
    [SerializeField] Sprite lawyerSprite;

    [Tooltip("재판장이 듣고 있는 장면 — \"…\"")]
    [SerializeField] Sprite judgeSprite;

    [Tooltip("영상이 끝나고 멈춰 서는 정면 컷 — \"따라서, 범인은—\". 비우면 Resources/Cutscene/Court_Accuse_Face")]
    [SerializeField] Sprite accuseFaceSprite;

    [Header("법정 대사  ({이름} = 지목한 용의자, {사건} = 사건 제목)")]
    [SerializeField] string openingLine = "존경하는 재판장님.";

    [TextArea(2, 4)]
    [SerializeField] string evidenceLine =
        "이 사건의 진술들은 서로 맞지 않았습니다.\n어긋난 자리를 따라가면 답은 하나뿐입니다.";

    [SerializeField] string judgePonderLine = "……계속하시오.";

    [SerializeField] string accuseLine = "따라서, 범인은—";

    [SerializeField] string namingLine = "{이름}입니다.";

    [SerializeField] string speakerName = "월터";

    [SerializeField] string judgeName = "재판장";

    [Tooltip("범인은 맞혔지만 사건 설명이 부족해 선고까지 가지 못했을 때")]
    [TextArea(2, 4)]
    [SerializeField] string insufficientLine =
        "증거가 부족하오. 이것만으로는 유죄를 선고할 수 없소.\n피고인을 석방하시오.";

    [Tooltip("\"따라서 범인은…\" — 지목하는 장면")]
    [SerializeField] string accuseClip = "Cut_Accuse.mp4";

    [Tooltip("지목한 인물의 얼굴을 보여주는 정지컷 노출 시간(초). 얼굴은 용의자 Portrait을 쓴다.")]
    [SerializeField] float accusedFaceSeconds = 1.6f;

    [Tooltip("재판장의 이유 설명과 유무죄 선고")]
    [SerializeField] string verdictClip = "Cut_Verdict.mp4";

    [Tooltip("유죄 1컷 — 머그샷")]
    [SerializeField] string guiltyMugshotClip = "Cut_Guilty_1.mp4";

    [Tooltip("머그샷과 사건 기록 사이에 끼우는 수감 스틸. 비우면 Resources/Cutscene/Cut_Guilty_Cell")]
    [SerializeField] Sprite guiltyCellSprite;

    [TextArea(2, 3)]
    [SerializeField] string guiltyCellLine = "긴 밤이 끝났다.";

    [Tooltip("유죄 2컷 — 사건 기록으로 남는 장면")]
    [SerializeField] string guiltyCaseClip = "Cut_Guilty_2.mp4";

    [Tooltip("무죄(오인 지목)일 때, 지목당한 용의자별 장면")]
    [SerializeField] SuspectClip[] innocentClips =
    {
        new SuspectClip { suspectId = "B", fileName = "Cut_Innocent_B.mp4" },
        new SuspectClip { suspectId = "C", fileName = "Cut_Innocent_C.mp4" },
    };

    [Tooltip("무죄 마무리 스틸(사건이 책상으로 돌아온 장면). 비우면 Resources/Cutscene/Cut_Innocent_End")]
    [SerializeField] Sprite innocentEndSprite;

    [TextArea(2, 3)]
    [SerializeField] string innocentEndLine = "사건은 다시 책상 위로 돌아왔다.";

    [Header("컷씬 사운드")]
    [Tooltip("컷씬 내내 깔리는 배경음. 비우면 Resources/Cutscene/Cutscene_BGM 을 찾는다.")]
    [SerializeField] AudioClip cutsceneBgm;

    [Tooltip("유죄로 갈렸을 때 갈아끼울 배경음. 비우면 Resources/Cutscene/Cutscene_BGM_Guilty")]
    [SerializeField] AudioClip guiltyBgm;

    [Tooltip("'따라서 범인은—' 영상의 자체 소리를 끈다.")]
    [SerializeField] bool muteAccuseClipAudio = true;

    [Range(0f, 1f)]
    [SerializeField] float cutsceneBgmVolume = 0.35f;

    bool cutscenePlayed;

    void Start()
    {
        DialogueUIUtil.EnsureEventSystem();

        var steps = BuildCutscene();
        if (steps.Count > 0)
        {
            cutscenePlayed = true;
            // 컷씬에서 갈린 배경음(유죄/기본)을 결과 화면까지 그대로 끌고 간다.
            gameObject.AddComponent<CutscenePlayer>()
                      .Play(steps, Build, Resolve(cutsceneBgm, "Cutscene_BGM"), cutsceneBgmVolume, keepBgm: true);
        }
        else Build();
    }

    // 스토리보드 순서대로 컷 목록을 만든다. 없는 파일/이미지는 조용히 빠진다.
    //
    //   1 법정 전경   "존경하는 재판장님."
    //   2 증거 제시   "…어긋난 자리를 따라가면 답은 하나뿐입니다."
    //   3 재판장      "……계속하시오."
    //   4 [영상] 뒤를 돌며 (무음) → 정면 정지컷 "따라서, 범인은—"
    //   5 지목한 얼굴 "{이름}입니다."
    //   6 [영상] 이유 설명 + 유무죄 선고
    //   7~ 결과 분기
    public List<CutscenePlayer.Step> BuildCutscene()
    {
        var steps = new List<CutscenePlayer.Step>();
        if (!playCutscene || !GameSession.HasVerdict) return steps;

        var wide = Resolve(courtWideSprite, "Court_Wide");
        var judge = Resolve(judgeSprite, "Court_Judge");
        var lawyer = Resolve(lawyerSprite, "Court_Lawyer") ?? wide;   // 전용 컷이 없으면 전경으로 대신한다

        AddDialogue(steps, wide, speakerName, openingLine);
        AddDialogue(steps, lawyer, speakerName, evidenceLine);
        AddDialogue(steps, judge, judgeName, judgePonderLine);

        // 4번 — 뒤를 도는 영상(대사 없이 무음으로 흐르고), 돌아선 정면 컷에서 멈춰 대사를 얹는다.
        // 영상에 대사를 붙이면 재생이 끝나는 순간 화면이 검게 비므로 정지컷으로 받아준다.
        AddClip(steps, accuseClip, mute: muteAccuseClipAudio);
        AddDialogue(steps, Resolve(accuseFaceSprite, "Court_Accuse_Face") ?? wide, speakerName, accuseLine);

        // 5번 — 지목한 인물의 법정 얼굴 + "{이름}입니다."
        AddDialogue(steps, AccusedFace() ?? wide, speakerName, namingLine);

        AddClip(steps, verdictClip);

        switch (GameSession.LastVerdict)
        {
            case VerdictResult.Success:
                // 머그샷 → 수감 스틸 → 사건 기록. 여기서부터 배경음을 유죄 테마로 바꾼다.
                // (Success/Fail 사운드는 12초·8초짜리 '곡'이라 여기서 틀면 배경음과 겹친다 — 쓰지 않는다.)
                var guiltyTheme = Resolve(guiltyBgm, "Cutscene_BGM_Guilty");
                AddClip(steps, guiltyMugshotClip, bgm: guiltyTheme);
                AddDialogue(steps, Resolve(guiltyCellSprite, "Cut_Guilty_Cell"), null, guiltyCellLine);
                AddClip(steps, guiltyCaseClip);
                break;

            case VerdictResult.WrongSuspect:
                AddClip(steps, InnocentClipFor(GameSession.AccusedId));
                AddDialogue(steps, Resolve(innocentEndSprite, "Cut_Innocent_End"), null, innocentEndLine);
                break;

            case VerdictResult.InsufficientEvidence:
                // 범인은 맞혔지만 사건 설명이 기준을 채우지 못해 선고까지 가지 못한 경우.
                // 전용 영상이 없으므로 재판장 스틸 위에 결과만 말해 주고, 사무실 컷으로 닫는다.
                AddDialogue(steps, judge, judgeName, insufficientLine);
                AddDialogue(steps, Resolve(innocentEndSprite, "Cut_Innocent_End"), null, innocentEndLine);
                break;
        }

        return steps;
    }

    /// <summary>지목한 인물의 법정 얼굴 컷. 없으면 취조용 초상화로 대신한다.</summary>
    Sprite AccusedFace()
    {
        var id = GameSession.AccusedId;
        if (string.IsNullOrEmpty(id)) return null;

        var face = Resources.Load<Sprite>("Cutscene/Face_" + id);
        return face != null ? face : AccusedPortrait();
    }

    // 인스펙터가 비어 있으면 Resources/Cutscene 에서 같은 이름을 찾는다.
    static Sprite Resolve(Sprite assigned, string resourceName) =>
        assigned != null ? assigned : Resources.Load<Sprite>("Cutscene/" + resourceName);

    static AudioClip Resolve(AudioClip assigned, string resourceName) =>
        assigned != null ? assigned : Resources.Load<AudioClip>("Cutscene/" + resourceName);

    void AddDialogue(List<CutscenePlayer.Step> steps, Sprite sprite, string speaker, string line,
                     AudioClip sfx = null)
    {
        if (sprite == null || string.IsNullOrEmpty(line)) return;

        var step = CutscenePlayer.Step.Dialogue(sprite, speaker, FillTokens(line));
        step.sfx = sfx;
        steps.Add(step);
    }

    // {이름} = 지목한 용의자, {사건} = 사건 제목
    string FillTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var caseAsset = GameSession.SelectedCase;
        string name = AccusedName();
        string title = caseAsset != null ? caseAsset.caseTitle : "";

        return text.Replace("{이름}", string.IsNullOrEmpty(name) ? "그 사람" : name)
                   .Replace("{사건}", title);
    }

    string AccusedName()
    {
        var suspect = FindAccused();
        return suspect != null ? suspect.suspectName : null;
    }

    string InnocentClipFor(string suspectId)
    {
        if (innocentClips == null || string.IsNullOrEmpty(suspectId)) return null;
        foreach (var entry in innocentClips)
            if (entry != null && entry.suspectId == suspectId) return entry.fileName;

        Debug.LogWarning("[Ending] 용의자 '" + suspectId + "' 의 무죄 컷이 없어 생략합니다.");
        return null;
    }

    // 지목한 용의자의 얼굴. 사건 데이터에서 초상화를 가져온다.
    Sprite AccusedPortrait()
    {
        var suspect = FindAccused();
        return suspect != null ? suspect.portrait : null;
    }

    SuspectData FindAccused()
    {
        var caseAsset = GameSession.SelectedCase;
        if (caseAsset == null || caseAsset.suspects == null || string.IsNullOrEmpty(GameSession.AccusedId))
            return null;

        foreach (var s in caseAsset.suspects)
            if (s != null && s.id == GameSession.AccusedId) return s;
        return null;
    }

    void AddClip(List<CutscenePlayer.Step> steps, string fileName, string speaker = null, string line = null,
                 AudioClip sfx = null, AudioClip bgm = null, bool mute = false)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        if (!ClipExists(fileName))
        {
            Debug.LogWarning("[Ending] 컷씬 파일이 없어 생략합니다: " + fileName);
            return;
        }

        var step = string.IsNullOrEmpty(line)
            ? CutscenePlayer.Step.Video(fileName)
            : CutscenePlayer.Step.Video(fileName, speaker, FillTokens(line));
        step.sfx = sfx;
        step.bgm = bgm;
        step.muteVideoAudio = mute;
        steps.Add(step);
    }

    /// <summary>StreamingAssets에 파일이 실제로 있는지. 웹에서는 확인할 수 없어 일단 있다고 본다.</summary>
    public static bool ClipExists(string fileName)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return true;
#else
        return System.IO.File.Exists(System.IO.Path.Combine(Application.streamingAssetsPath, fileName));
#endif
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

        // 사운드 — 컷씬을 봤다면 결과음은 이미 컷씬 안에서 울렸다.
        var clip = success ? successSfx : failSfx;
        if (clip != null && !cutscenePlayed) AudioSource.PlayClipAtPoint(clip, Vector3.zero);

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
