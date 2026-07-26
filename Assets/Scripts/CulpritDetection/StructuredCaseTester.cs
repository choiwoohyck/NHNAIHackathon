using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace CulpritDetection
{
    /// <summary>
    /// 구조화 입력 테스터 (한글 입력 OK). CaseDefinition(SO)을 읽어 UI를 자동 생성한다.
    ///  - template이 있으면 "{범인}가 {장소}에서 {흉기}를 사용해서 죽였다" 처럼
    ///    문장 안 {필드} 자리에 빈칸(입력칸)을 인라인으로 배치.
    ///  - 문장에 안 쓴 필드는 아래에 세로로 나열. template이 비면 전부 세로 나열.
    ///  - 각 칸은 독립 판정(동의어·오타 허용) → 자유문장 파싱 예외가 없음.
    ///
    /// ※ 한글 IME: Active Input Handling = Both. WebGL 한글표시: koreanFont에 .ttf 지정.
    /// </summary>
    [DisallowMultipleComponent]
    public class StructuredCaseTester : MonoBehaviour
    {
        [Header("사건 정의 (비우면 예시 사건)")]
        public CaseDefinition caseDefinition;

        [Header("한글 폰트 (WebGL 필수)")]
        public Font koreanFont;

        private CaseDefinition _case;
        private readonly List<(CaseField field, InputField input)> _pairs = new List<(CaseField, InputField)>();
        private Text _result;
        private Font _font;

        void Start()
        {
            _font = ResolveFont();
            EnsureEventSystem();
            _case = caseDefinition != null ? caseDefinition : BuildDefault();
            BuildUI();
            Recompute();
        }

        private CaseDefinition BuildDefault()
        {
            var c = ScriptableObject.CreateInstance<CaseDefinition>();
            c.caseTitle = "예시 사건 (SO 미지정)";
            c.typoThreshold = 0.8f;
            c.template = "{범인}가 {장소}에서 {흉기}를 사용해서 죽였다";
            c.fields = new List<CaseField>
            {
                new CaseField("범인", "집사").AddOption("집사", "하인", "버틀러").AddOption("주인", "백작").AddOption("요리사"),
                new CaseField("흉기", "칼").AddOption("칼", "나이프", "단검").AddOption("독", "독약").AddOption("총", "권총"),
                new CaseField("장소", "서재").AddOption("서재", "책방", "서고").AddOption("정원").AddOption("부엌", "주방"),
                new CaseField("범행 동기", "유산").AddOption("유산", "돈", "재산").AddOption("복수", "원한").AddOption("치정", "질투"),
                new CaseField("결정적 증거", "장갑").AddOption("장갑").AddOption("발자국", "족적").AddOption("편지", "쪽지"),
            };
            return c;
        }

        // ---------- 판정 ----------
        private void Recompute()
        {
            int correct = 0;
            var sb = new StringBuilder();
            foreach (var pair in _pairs)
            {
                string resolved = pair.field.Resolve(pair.input ? pair.input.text : "", _case.typoThreshold);
                bool ok = resolved != null && Norm(resolved) == Norm(pair.field.answer);
                if (ok) correct++;
                string col = ok ? "#5CFF6E" : "#FF6B6B";
                string mark = ok ? "O" : "X";
                sb.Append($"{pair.field.label} : <color={col}><b>{mark}</b></color>   (정답 {pair.field.answer} / 입력 {resolved ?? "-"})\n");
            }
            int total = _pairs.Count;
            bool all = total > 0 && correct == total;
            string sc = all ? "#5CFF6E" : correct > 0 ? "#FFD24A" : "#FF6B6B";
            sb.Append($"<color={sc}><b>종합 : {correct}/{total}{(all ? "   → 사건 해결!" : "")}</b></color>");
            _result.text = sb.ToString();
        }

        private CaseField FindField(string label)
        {
            string l = (label ?? "").Trim();
            foreach (var f in _case.fields)
                if (f != null && (f.label ?? "").Trim() == l) return f;
            return null;
        }

        private static string Norm(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().ToLowerInvariant();

        // ---------- UI 생성 ----------
        private void BuildUI()
        {
            var canvasGO = new GameObject("CaseTesterCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var panel = CreateRect("Panel", canvasGO.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 1);
            panel.anchoredPosition = new Vector2(20, -20);
            panel.sizeDelta = new Vector2(560, 100); // 높이는 ContentSizeFitter가 조절
            panel.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 0.94f);
            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 16, 16);
            vlg.spacing = 6;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var csf = panel.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddLabel(panel, _case.caseTitle, 18, FontStyle.Bold, Color.white);
            AddLabel(panel, "빈칸을 채우세요 (동의어·오타 허용). 카테고리/문장은 SO에서 편집.",
                     11, FontStyle.Normal, new Color(1f, 0.8f, 0.4f));

            _pairs.Clear();
            var referenced = new HashSet<string>();

            // 1) template 문장(빈칸 인라인)
            if (!string.IsNullOrWhiteSpace(_case.template))
            {
                foreach (var rawLine in _case.template.Split('\n'))
                    BuildTemplateRow(panel, rawLine.Replace("\r", ""), referenced);
                AddLabel(panel, " ", 4, FontStyle.Normal, Color.clear); // 약간의 여백
            }

            // 2) 문장에 안 쓴 필드는 세로 나열
            foreach (var field in _case.fields)
            {
                if (field == null || referenced.Contains(field.label)) continue;
                AddLabel(panel, field.DisplayPrompt(), 13, FontStyle.Bold, new Color(0.75f, 0.85f, 1f));
                var input = AddStackedInput(panel, string.Join(" / ", field.OptionValues()));
                Register(field, input);
            }

            var actRow = AddButtonRow(panel, 30);
            AddButtonTo(actRow, "초기화", () => { foreach (var p in _pairs) if (p.input) p.input.text = ""; });

            AddLabel(panel, "───────────────", 12, FontStyle.Normal, new Color(0.4f, 0.4f, 0.45f));
            _result = AddLabel(panel, "", 15, FontStyle.Bold, Color.white);
            _result.alignment = TextAnchor.UpperLeft;
        }

        // "{범인}가 {장소}에서..." 한 줄 → 텍스트/빈칸 인라인 배치
        private void BuildTemplateRow(RectTransform panel, string line, HashSet<string> referenced)
        {
            var row = AddFlowRow(panel, 34);
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
                    var input = AddInlineInput(row, field.label);
                    Register(field, input);
                    referenced.Add(field.label);
                }
                else
                {
                    AddInlineText(row, "{" + label + "}"); // 없는 필드는 그대로 표시
                }
                i = close + 1;
            }
        }

        private void Register(CaseField field, InputField input)
        {
            _pairs.Add((field, input));
            input.onValueChanged.AddListener(_ => Recompute());
        }

        // ---------- 폰트 / EventSystem ----------
        private Font ResolveFont()
        {
            if (koreanFont != null) return koreanFont;
            Font f = null;
            try
            {
                f = Font.CreateDynamicFontFromOSFont(
                    new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Dotum", "Batang", "Arial" }, 16);
            }
            catch { }
            if (f == null) { try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { } }
            return f;
        }

        private void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        // ---------- uGUI 헬퍼 ----------
        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        private Text AddLabel(RectTransform parent, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font; t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            go.AddComponent<LayoutElement>().minHeight = size + 6;
            return t;
        }

        // 인라인(가로) 흐름 행
        private RectTransform AddFlowRow(RectTransform parent, float height)
        {
            var row = CreateRect("FlowRow", parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            return row;
        }

        private void AddInlineText(RectTransform row, string text)
        {
            var go = new GameObject("T", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(row, false);
            var t = go.GetComponent<Text>();
            t.font = _font; t.text = text; t.fontSize = 15; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            go.AddComponent<LayoutElement>().minHeight = 30;
        }

        private InputField AddInlineInput(RectTransform row, string placeholder)
        {
            var input = MakeInput(row, placeholder, TextAnchor.MiddleCenter);
            var le = input.gameObject.GetComponent<LayoutElement>();
            le.preferredWidth = 110; le.minWidth = 80; le.flexibleWidth = 0;
            le.minHeight = le.preferredHeight = 30;
            return input;
        }

        private InputField AddStackedInput(RectTransform parent, string placeholder)
        {
            var input = MakeInput(parent, placeholder, TextAnchor.MiddleLeft);
            var le = input.gameObject.GetComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 28;
            return input;
        }

        private InputField MakeInput(Transform parent, string placeholder, TextAnchor align)
        {
            var go = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.white;
            var input = go.GetComponent<InputField>();
            input.targetGraphic = go.GetComponent<Image>();
            input.lineType = InputField.LineType.SingleLine;

            var text = MakeChildText(go.transform, _font, Color.black, align);
            text.supportRichText = false;
            var ph = MakeChildText(go.transform, _font, new Color(0.6f, 0.6f, 0.6f), align);
            ph.fontStyle = FontStyle.Italic;
            ph.text = placeholder;

            input.textComponent = text;
            input.placeholder = ph;
            return input;
        }

        private RectTransform AddButtonRow(RectTransform parent, float height)
        {
            var row = CreateRect("Row", parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            return row;
        }

        private Button AddButtonTo(RectTransform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.25f, 0.5f, 0.9f, 1f);
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var txtGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGO.transform.SetParent(go.transform, false);
            var t = txtGO.GetComponent<Text>();
            t.font = _font; t.text = label; t.color = Color.white; t.fontSize = 14;
            t.alignment = TextAnchor.MiddleCenter;
            Stretch(t.rectTransform);
            return go.GetComponent<Button>();
        }

        private static Text MakeChildText(Transform parent, Font font, Color color, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = 15; t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            Stretch(t.rectTransform, 8, 8, 5, 5);
            return t;
        }
    }
}
