using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

// 런타임에 uGUI 요소를 코드로 생성하기 위한 공용 헬퍼.
// 프로토타입 단계라 프리팹/에디터 UI 세팅 없이 스크립트만 씬에 올리면 바로 테스트할 수 있도록 하기 위함.
public static class DialogueUIUtil
{
    static Font _defaultFont;
    // 본문(일반) 폰트. 한글 게임이므로 NotoSans를 쓴다(WebGL 포함 모든 플랫폼에서 한글 렌더).
    //  1) Resources의 NotoSansKR-Regular(본문용, 가벼운 웨이트) → 파일을 넣으면 자동 적용.
    //  2) 없으면 NotoSansKR-Black(제목용, 굵음)으로 대체(그래도 한글은 나옴, 조금 두꺼울 뿐).
    //  3) 그래도 없으면 내장 폰트(한글 없음 — 최후의 폴백).
    public static Font DefaultFont
    {
        get
        {
            if (_defaultFont != null) return _defaultFont;
            _defaultFont = Resources.Load<Font>("NotoSansKR-Regular");
            if (_defaultFont == null) _defaultFont = Resources.Load<Font>("NotoSansKR-Black");
            if (_defaultFont == null) _defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_defaultFont == null) _defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _defaultFont;
        }
    }

    static Font _koreanFont;
    // 한글 표시/입력(InputField)용 폰트.
    //  1) Resources의 NotoSansKR(.ttf 임베드) — WebGL 포함 모든 플랫폼에서 동작(웹 한글 표시의 핵심).
    //  2) 폴백: OS 동적 폰트(맑은 고딕 등) — 에디터/PC 전용, WebGL엔 OS 폰트가 없어 안 됨.
    //  3) 그래도 없으면 기본 폰트.
    public static Font KoreanFont
    {
        get
        {
            if (_koreanFont != null) return _koreanFont;

            _koreanFont = Resources.Load<Font>("NotoSansKR-Black");

            if (_koreanFont == null)
            {
                try
                {
                    _koreanFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Dotum", "Batang", "Arial" }, 16);
                }
                catch { }
            }
            if (_koreanFont == null) _koreanFont = DefaultFont;
            return _koreanFont;
        }
    }

    public static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();

        // 주의: 런타임에 코드로 InputSystemUIInputModule을 AddComponent 하면 기본 UI 액션이 비어 있어
        // 마우스 클릭이 UI에 전달되지 않는다(→ 버튼/대사 진행 불가). 레거시 입력이 켜져 있으면(Old/Both)
        // StandaloneInputModule이 가장 확실하고, New 전용 환경에서는 기본 액션을 직접 할당한다.
#if ENABLE_LEGACY_INPUT_MANAGER
        go.AddComponent<StandaloneInputModule>();
#elif ENABLE_INPUT_SYSTEM
        var uiModule = go.AddComponent<InputSystemUIInputModule>();
        uiModule.AssignDefaultActions();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    public static Canvas CreateCanvas(string name, int sortOrder)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortOrder;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static RectTransform CreatePanel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        return go.GetComponent<RectTransform>();
    }

    public static Text CreateText(Transform parent, string name, int fontSize, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = DefaultFont;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    public static Button CreateButton(Transform parent, string name, string label, Color bgColor)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = bgColor;
        var button = go.AddComponent<Button>();

        var text = CreateText(go.transform, "Label", 24, TextAnchor.MiddleCenter, Color.white);
        var textRt = text.rectTransform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        text.text = label;

        return button;
    }

    public static TMP_Text CreateTMPText(Transform parent, string name, int fontSize, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = ToTMPAlignment(anchor);
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    static TextAlignmentOptions ToTMPAlignment(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.TopLeft;
        }
    }

    public static Button CreateTMPButton(Transform parent, string name, string label, Color bgColor, Sprite bgSprite = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = bgColor;
        if (bgSprite != null) image.sprite = bgSprite;
        var button = go.AddComponent<Button>();

        var text = CreateTMPText(go.transform, "Label", 30, TextAnchor.MiddleCenter, Color.white);
        var textRt = text.rectTransform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(16, 0);
        textRt.offsetMax = new Vector2(-16, 0);
        text.text = label;

        return button;
    }

    public static RectTransform Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }
}
