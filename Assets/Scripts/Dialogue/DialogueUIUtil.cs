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
    public static Font DefaultFont
    {
        get
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }
    }

    static Font _koreanFont;
    // 한글 표시/입력(InputField)용 동적 폰트. OS의 맑은 고딕 등을 사용하고, 실패 시 기본 폰트로 대체.
    public static Font KoreanFont
    {
        get
        {
            if (_koreanFont != null) return _koreanFont;
            try
            {
                _koreanFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Dotum", "Batang", "Arial" }, 16);
            }
            catch { }
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

    public static RectTransform Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }
}
