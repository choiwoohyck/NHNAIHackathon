using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using CulpritDetection;

// 최종 판결 화면. 모든 취조가 끝난 뒤 "사건 판결" 버튼으로 열린다.
//   1) 범인으로 판단한 용의자를 지목하고
//   2) 사건 설명(동기/수단/대상/목적)을 채운 뒤 제출하면
//   3) VerdictEvaluator가 성공(유죄)/오인지목(무죄)/증거불충분 을 판정한다.
//
// UI는 런타임에 코드로 생성한다(기존 시스템과 동일 방식). 판정 데이터는 CaseGraph에서 읽는다.
public class VerdictController : MonoBehaviour
{
    static readonly Color SuspectNormal = new Color(0.2f, 0.2f, 0.26f, 0.95f);
    static readonly Color SuspectSelected = new Color(0.78f, 0.56f, 0.16f, 0.98f);

    const string CulpritToken = "범인";  // 템플릿의 {범인} 자리 = 지목한 용의자 이름이 채워짐

    [Header("커스텀 UI 참조 (선택, 비워두면 자동 생성)")]
    [Tooltip("비워두면 런타임에 자동 생성된다. 지정하면 해당 오브젝트를 그대로 사용한다.\n" +
             "패널 내부(용의자 버튼/입력칸 등)는 사건 데이터에 맞춰 매번 다시 그려지므로 항상 코드로 생성된다.")]
    [SerializeField] Canvas canvasOverride;
    [SerializeField] RectTransform dimOverride;
    [SerializeField] RectTransform panelOverride;

    RectTransform dim;      // 전체화면 배경(모달). 닫힘 상태에서는 통째로 꺼서 클릭을 가로채지 않게 한다.
    RectTransform panel;
    Font font;

    CaseGraph graph;
    string selectedSuspectId;
    Text resultText;
    Text culpritSlotLabel;   // 문장 속 {범인} 자리(선택한 용의자 이름 표시)

    public string endingSceneName = "EndingScene";   // 제출 시 이 씬으로 결과 전달(Build Settings에 등록돼 있어야 이동).

    readonly List<(CaseField field, InputField input)> pairs = new List<(CaseField, InputField)>();
    readonly Dictionary<string, Button> suspectButtons = new Dictionary<string, Button>();

    public bool IsOpen => dim != null && dim.gameObject.activeSelf;

    void Awake()
    {
        font = DialogueUIUtil.KoreanFont;
        DialogueUIUtil.EnsureEventSystem();
        BuildShell();
        dim.gameObject.SetActive(false);   // 시작 시 판결 UI 전체를 꺼둔다(전체화면 dim이 입력을 막지 않도록)
    }

    void BuildShell()
    {
        var canvas = canvasOverride != null ? canvasOverride : DialogueUIUtil.CreateCanvas("VerdictCanvas", 30);

        // 어두운 전체 배경(열렸을 때만 뒤 클릭 차단)
        dim = dimOverride != null ? dimOverride : DialogueUIUtil.CreatePanel(canvas.transform, "VerdictDim", new Color(0f, 0f, 0f, 0.6f));
        if (dimOverride == null)
            DialogueUIUtil.Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // 가운데 보고서 패널 (세로 레이아웃 + 내용에 맞춰 높이 자동)
        panel = panelOverride != null ? panelOverride : DialogueUIUtil.CreatePanel(dim, "VerdictPanel", new Color(0.1f, 0.1f, 0.13f, 0.98f));
        if (panelOverride == null)
        {
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(780, 200);
        }

        var vlg = panel.gameObject.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 22, 22);
        vlg.spacing = 8;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var csf = panel.gameObject.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = panel.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void Show(CaseGraph g)
    {
        graph = g;
        Populate();
        dim.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (dim != null) dim.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // 내용 구성
    // ------------------------------------------------------------------
    void Populate()
    {
        selectedSuspectId = null;
        pairs.Clear();
        suspectButtons.Clear();
        resultText = null;
        culpritSlotLabel = null;

        for (int i = panel.childCount - 1; i >= 0; i--) Destroy(panel.GetChild(i).gameObject);

        AddLabel("최종 판결 · 사건 보고서", 24, FontStyle.Bold, Color.white);
        AddLabel("범인으로 판단한 용의자를 지목하고, 사건의 전말을 완성하세요.", 13, FontStyle.Normal, new Color(1f, 0.82f, 0.42f));

        // --- 지목 용의자 ---
        AddLabel("지목 용의자", 15, FontStyle.Bold, new Color(0.75f, 0.85f, 1f));
        var suspectRow = AddRow(46, TextAnchor.MiddleLeft, true);
        if (graph != null)
            foreach (var s in graph.Suspects)
            {
                var id = s.id;
                var btn = MakeButton(suspectRow, s.suspectName, SuspectNormal, () => SelectSuspect(id));
                var le = btn.gameObject.GetComponent<LayoutElement>();
                le.minWidth = 150; le.preferredHeight = 40;
                suspectButtons[id] = btn;
            }

        // --- 사건 설명 ---
        AddLabel("사건 설명", 15, FontStyle.Bold, new Color(0.75f, 0.85f, 1f));
        var referenced = new HashSet<string>();

        if (graph != null && !string.IsNullOrWhiteSpace(graph.verdictTemplate))
        {
            foreach (var rawLine in graph.verdictTemplate.Split('\n'))
                BuildTemplateRow(rawLine.Replace("\r", ""), referenced);
        }

        if (graph != null)
            foreach (var field in graph.verdictFields)
            {
                if (field == null || referenced.Contains(field.label)) continue;
                AddLabel(field.DisplayPrompt(), 13, FontStyle.Bold, new Color(0.85f, 0.9f, 1f));
                var input = AddStackedInput(string.Join(" / ", field.OptionValues()));
                Register(field, input);
            }

        // --- 제출 / 닫기 ---
        var actionRow = AddRow(44, TextAnchor.MiddleCenter, true);
        MakeButton(actionRow, "제출", new Color(0.18f, 0.5f, 0.28f, 1f), OnSubmit);
        MakeButton(actionRow, "닫기", new Color(0.4f, 0.16f, 0.16f, 1f), Hide);

        resultText = AddLabel("", 16, FontStyle.Bold, Color.white);
        resultText.alignment = TextAnchor.UpperLeft;
    }

    void SelectSuspect(string suspectId)
    {
        selectedSuspectId = suspectId;
        foreach (var kv in suspectButtons)
        {
            var img = kv.Value.GetComponent<Image>();
            if (img != null) img.color = kv.Key == suspectId ? SuspectSelected : SuspectNormal;
        }
        UpdateCulpritSlot();  // 문장 속 {범인} 자리에 선택한 이름 채우기
    }

    // 문장 속 '범인' 자리. 비어 있으면 밑줄, 용의자 버튼을 누르면 그 이름이 채워진다.
    void AddCulpritSlot(RectTransform row)
    {
        var go = new GameObject("CulpritSlot", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(row, false);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.78f, 0.56f, 0.16f, 0.22f);
        img.raycastTarget = false;

        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 100; le.preferredHeight = 30;

        var t = DialogueUIUtil.CreateText(go.transform, "Txt", 15, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
        t.font = font; t.fontStyle = FontStyle.Bold; t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(6, 2); rt.offsetMax = new Vector2(-6, -2);

        culpritSlotLabel = t;
        UpdateCulpritSlot();
    }

    void UpdateCulpritSlot()
    {
        if (culpritSlotLabel == null) return;
        if (string.IsNullOrEmpty(selectedSuspectId)) { culpritSlotLabel.text = "＿＿＿"; return; }
        var s = graph != null ? graph.GetSuspect(selectedSuspectId) : null;
        culpritSlotLabel.text = s != null ? s.suspectName : selectedSuspectId;
    }

    // ------------------------------------------------------------------
    // 판정
    // ------------------------------------------------------------------
    void OnSubmit()
    {
        if (string.IsNullOrEmpty(selectedSuspectId))
        {
            SetResult("<color=#FFD24A>범인으로 지목할 용의자를 먼저 선택하세요.</color>");
            return;
        }

        // 빈칸은 문장(verdictTemplate) 순서로 놓여 있고 채점은 verdictFields 순서로 하므로,
        // 값만 넘기면 엉뚱한 필드와 대조된다. 어느 필드의 답인지 붙여서 넘긴다.
        var answers = new List<KeyValuePair<CaseField, string>>();
        foreach (var p in pairs)
            answers.Add(new KeyValuePair<CaseField, string>(p.field, p.input != null ? p.input.text : ""));

        int correct, total;
        var result = VerdictEvaluator.Evaluate(graph, selectedSuspectId, answers, out correct, out total);

        // 결과를 엔딩 씬으로 넘긴다. 엔딩 씬이 Build Settings에 있으면 이동, 아니면 패널 내 텍스트로 폴백.
        GameSession.SetVerdict(result, correct, total, graph != null ? graph.culpritSuspectId : null);

        if (!string.IsNullOrEmpty(endingSceneName) && Application.CanStreamedLevelBeLoaded(endingSceneName))
        {
            SceneManager.LoadScene(endingSceneName);
            return;
        }

        ShowInlineResult(result, correct, total);
    }

    // 엔딩 씬이 아직 없을 때의 폴백: 판결 패널 안에 결과 텍스트만 표시.
    void ShowInlineResult(VerdictResult result, int correct, int total)
    {
        switch (result)
        {
            case VerdictResult.Success:
            {
                var culprit = graph.GetSuspect(graph.culpritSuspectId);
                string name = culprit != null ? culprit.suspectName : "용의자";
                SetResult($"<color=#5CFF6E><b>유죄 판결</b></color>\n{name}에게 유죄가 선고되었습니다. 월터의 수사 결과가 국가 공식 사건 기록으로 등록됩니다.  (사건 설명 {correct}/{total})");
                break;
            }
            case VerdictResult.WrongSuspect:
                SetResult("<color=#FF6B6B><b>무죄 판결 — 오인 지목</b></color>\n지목한 용의자는 범인이 아닙니다. 법정에서 무죄가 선고되고, 월터는 상사에게 질책받습니다.");
                break;
            case VerdictResult.InsufficientEvidence:
                SetResult($"<color=#FFD24A><b>증거 불충분</b></color>\n범인은 맞지만, 사건 설명이 판정 기준을 충족하지 못했습니다.  (사건 설명 {correct}/{total})");
                break;
        }
    }

    void SetResult(string richText)
    {
        if (resultText != null) resultText.text = richText;
    }

    // ------------------------------------------------------------------
    // "{동기}가 {대상}을..." 한 줄 → 텍스트/빈칸 인라인 배치
    // ------------------------------------------------------------------
    void BuildTemplateRow(string line, HashSet<string> referenced)
    {
        var row = AddRow(36, TextAnchor.MiddleLeft, false);
        int i = 0;
        while (i < line.Length)
        {
            int open = line.IndexOf('{', i);
            if (open < 0) { if (i < line.Length) AddInlineText(row, line.Substring(i)); break; }
            if (open > i) AddInlineText(row, line.Substring(i, open - i));

            int close = line.IndexOf('}', open + 1);
            if (close < 0) { AddInlineText(row, line.Substring(open)); break; }

            string label = line.Substring(open + 1, close - open - 1).Trim();
            var field = FindField(label);
            if (field != null)
            {
                var input = AddInlineInput(row);
                Register(field, input);
                referenced.Add(field.label);
            }
            else if (label == CulpritToken) AddCulpritSlot(row);
            else AddInlineText(row, "{" + label + "}");
            i = close + 1;
        }
    }

    CaseField FindField(string label)
    {
        string l = (label ?? "").Trim();
        if (graph != null)
            foreach (var f in graph.verdictFields)
                if (f != null && (f.label ?? "").Trim() == l) return f;
        return null;
    }

    void Register(CaseField field, InputField input) => pairs.Add((field, input));

    // ------------------------------------------------------------------
    // uGUI 헬퍼
    // ------------------------------------------------------------------
    Text AddLabel(string text, int size, FontStyle style, Color color)
    {
        var t = DialogueUIUtil.CreateText(panel, "Label", size, TextAnchor.UpperLeft, color);
        t.font = font; t.text = text; t.fontStyle = style; t.supportRichText = true;
        t.gameObject.AddComponent<LayoutElement>().minHeight = size + 8;
        return t;
    }

    RectTransform AddRow(float height, TextAnchor align, bool expandChildren)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(panel, false);
        var rt = go.GetComponent<RectTransform>();
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8; h.childAlignment = align;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = expandChildren; h.childForceExpandHeight = false;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
        return rt;
    }

    void AddInlineText(RectTransform row, string text)
    {
        var t = DialogueUIUtil.CreateText(row, "T", 15, TextAnchor.MiddleLeft, Color.white);
        t.font = font; t.text = text;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.gameObject.AddComponent<LayoutElement>().minHeight = 30;
    }

    Button MakeButton(RectTransform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var btn = DialogueUIUtil.CreateButton(parent, "Btn_" + label, label, color);
        var lbl = btn.GetComponentInChildren<Text>();
        if (lbl != null) { lbl.font = font; lbl.fontSize = 16; }
        // 높이를 명시하지 않으면 HorizontalLayoutGroup이 버튼 높이를 0으로 접어 클릭이 안 된다.
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = 34;
        btn.onClick.AddListener(onClick);
        return btn;
    }

    InputField AddInlineInput(RectTransform row)
    {
        var input = MakeInput(row, TextAnchor.MiddleCenter);
        var le = input.gameObject.GetComponent<LayoutElement>();
        le.preferredWidth = 120; le.minWidth = 90; le.flexibleWidth = 0;
        le.minHeight = le.preferredHeight = 32;
        return input;
    }

    InputField AddStackedInput(string placeholder)
    {
        var input = MakeInput(panel, TextAnchor.MiddleLeft);
        var ph = input.placeholder as Text; if (ph != null) ph.text = placeholder;
        input.gameObject.GetComponent<LayoutElement>().minHeight = 32;
        return input;
    }

    InputField MakeInput(Transform parent, TextAnchor align)
    {
        var go = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Color.white;

        var input = go.GetComponent<InputField>();
        input.targetGraphic = go.GetComponent<Image>();
        input.lineType = InputField.LineType.SingleLine;

        var text = MakeChildText(go.transform, Color.black, align);
        text.supportRichText = false;
        var placeholder = MakeChildText(go.transform, new Color(0.6f, 0.6f, 0.6f), align);
        placeholder.fontStyle = FontStyle.Italic;

        input.textComponent = text;
        input.placeholder = placeholder;
        return input;
    }

    Text MakeChildText(Transform parent, Color color, TextAnchor align)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font; t.fontSize = 15; t.color = color; t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(8, 4); rt.offsetMax = new Vector2(-8, -4);
        return t;
    }
}
