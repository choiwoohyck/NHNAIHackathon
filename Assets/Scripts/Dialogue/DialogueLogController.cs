using UnityEngine;
using UnityEngine.UI;

// 대사 로그: 용의자를 부른 시점(입장/호출)부터 취조를 멈출 때(중지/종료)까지 오간 대사를 한 줄씩 쌓는다.
// 기록 자체는 InterrogationController가 세션이 진행되는 동안 계속 AddLine/AddEvent로 쌓아 두고,
// 이 컨트롤러는 그걸 "대사 로그 보기" 버튼으로 열고 닫는 패널에 표시만 한다.
public class DialogueLogController : MonoBehaviour
{
    [Header("커스텀 UI 참조 (선택, 비워두면 자동 생성)")]
    [Tooltip("비워두면 런타임에 자동 생성된다. 지정하면 해당 오브젝트를 그대로 사용한다.")]
    [SerializeField] Canvas canvasOverride;
    [SerializeField] RectTransform panelOverride;
    [SerializeField] ScrollRect scrollRectOverride;
    [SerializeField] RectTransform lineContainerOverride;
    [SerializeField] Button closeBtnOverride;

    static readonly Color EventColor = new Color(0.85f, 0.75f, 0.4f);
    static readonly Color LineColor = Color.white;

    RectTransform panel;
    ScrollRect scrollRect;
    RectTransform lineContainer;

    void Awake()
    {
        DialogueUIUtil.EnsureEventSystem();
        BuildUI();
        panel.gameObject.SetActive(false);
    }

    void BuildUI()
    {
        var canvas = canvasOverride != null ? canvasOverride : DialogueUIUtil.CreateCanvas("DialogueLogCanvas", 25);

        panel = panelOverride != null
            ? panelOverride
            : DialogueUIUtil.CreatePanel(canvas.transform, "DialogueLogPanel", new Color(0.05f, 0.05f, 0.05f, 0.92f));
        if (panelOverride == null)
            DialogueUIUtil.Stretch(panel, new Vector2(0.6f, 0.05f), new Vector2(0.98f, 0.9f), Vector2.zero, Vector2.zero);

        var title = DialogueUIUtil.CreateText(panel, "Title", 22, TextAnchor.MiddleLeft, Color.white);
        title.fontStyle = FontStyle.Bold;
        title.text = "대사 로그";
        DialogueUIUtil.Stretch(title.rectTransform, new Vector2(0f, 0.92f), new Vector2(0.8f, 1f), new Vector2(15, 0), new Vector2(0, 0));

        var closeBtn = closeBtnOverride != null
            ? closeBtnOverride
            : DialogueUIUtil.CreateButton(panel, "CloseBtn", "닫기", new Color(0.3f, 0.1f, 0.1f, 0.9f));
        if (closeBtnOverride == null)
            DialogueUIUtil.Stretch(closeBtn.GetComponent<RectTransform>(), new Vector2(0.82f, 0.92f), new Vector2(1f, 1f), new Vector2(0, 4), new Vector2(-12, -4));
        closeBtn.onClick.AddListener(Hide);

        if (scrollRectOverride != null)
        {
            scrollRect = scrollRectOverride;
            lineContainer = lineContainerOverride != null ? lineContainerOverride : scrollRect.content;
            return;
        }

        var scrollGO = new GameObject("LogScroll", typeof(RectTransform));
        scrollGO.transform.SetParent(panel, false);
        var scrollRt = scrollGO.GetComponent<RectTransform>();
        DialogueUIUtil.Stretch(scrollRt, new Vector2(0f, 0f), new Vector2(1f, 0.9f), new Vector2(15, 15), new Vector2(-15, -10));
        scrollRect = scrollGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var viewportRt = viewportGO.GetComponent<RectTransform>();
        DialogueUIUtil.Stretch(viewportRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewportGO.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);

        var contentGO = new GameObject("Content", typeof(RectTransform));
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRt = contentGO.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        var layout = contentGO.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6;
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        var fitter = contentGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRt;
        scrollRect.content = contentRt;
        lineContainer = contentRt;
    }

    /// <summary>입장/호출 같은 메타 이벤트 한 줄. "OO가 입장하였습니다." 등.</summary>
    public void AddEvent(string text) => AddEntry(text, EventColor, FontStyle.Italic);

    /// <summary>실제 오간 대사 한 줄. "화자: 대사" 형태로 남긴다.</summary>
    public void AddLine(string speaker, string text)
    {
        var formatted = string.IsNullOrEmpty(speaker) ? text : speaker + ": " + text;
        AddEntry(formatted, LineColor, FontStyle.Normal);
    }

    void AddEntry(string text, Color color, FontStyle style)
    {
        var t = DialogueUIUtil.CreateText(lineContainer, "Line", 16, TextAnchor.UpperLeft, color);
        t.fontStyle = style;
        t.text = text;
        t.gameObject.AddComponent<LayoutElement>().minHeight = 22;

        // 새 줄이 추가된 뒤 레이아웃을 즉시 갱신하고 맨 아래로 스크롤한다.
        Canvas.ForceUpdateCanvases();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }

    public void ToggleVisible() => panel.gameObject.SetActive(!panel.gameObject.activeSelf);
    public void Show() => panel.gameObject.SetActive(true);
    public void Hide() => panel.gameObject.SetActive(false);
}
