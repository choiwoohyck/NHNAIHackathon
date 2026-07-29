using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 용의자 한 명의 취조 기록지를 표현하는 오브젝트.
// InterrogationController가 취조를 중지/종료할 때마다 RecordBookController를 통해 생성/갱신된다.
// 문장 하나하나가 버튼으로 되어 있어, 플레이어가 이를 클릭해 모순 근거(증언)로 선택한다.
// 선택된 문장은 시각적으로 강조되고, 클릭 이벤트는 상위(InterrogationController)로 전달되어
// 방향 그래프의 모순 질문 해금 판정에 쓰인다.
public class RecordSheetCard : MonoBehaviour
{
    static readonly Color LineNormalColor = new Color(1f, 1f, 1f, 0.05f);
    static readonly Color LineSelectedColor = new Color(0.9f, 0.75f, 0.2f, 0.35f); // 선택 강조(호박색)

    Text titleText;
    RectTransform lineContainer;
    Action<StatementRecord> onLineClicked;

    readonly List<GameObject> lineObjects = new List<GameObject>();
    readonly Dictionary<string, Image> lineImages = new Dictionary<string, Image>(); // recordId → 버튼 배경

    public void Build(Action<StatementRecord> onLineClicked)
    {
        this.onLineClicked = onLineClicked;

        var bg = gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.06f);

        titleText = DialogueUIUtil.CreateText(transform, "Title", 22, TextAnchor.MiddleLeft, Color.white);
        titleText.fontStyle = FontStyle.Bold;
        DialogueUIUtil.Stretch(titleText.rectTransform, new Vector2(0f, 0.88f), new Vector2(1f, 1f), new Vector2(10, 0), new Vector2(-10, 0));

        var containerGO = new GameObject("Lines", typeof(RectTransform));
        containerGO.transform.SetParent(transform, false);
        lineContainer = containerGO.GetComponent<RectTransform>();
        DialogueUIUtil.Stretch(lineContainer, Vector2.zero, new Vector2(1f, 0.88f), new Vector2(10, 10), new Vector2(-10, -10));

        var layout = containerGO.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        var fitter = containerGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void Refresh(SuspectSession session)
    {
        titleText.text = session.suspectName + "의 증언";

        foreach (var go in lineObjects) Destroy(go);
        lineObjects.Clear();
        lineImages.Clear();

        foreach (var record in session.statements)
        {
            var btn = DialogueUIUtil.CreateButton(lineContainer, "Line_" + record.recordId, record.text, LineNormalColor);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;

            var label = btn.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.fontSize = 16;

            var captured = record;
            btn.onClick.AddListener(() => onLineClicked?.Invoke(captured));

            lineObjects.Add(btn.gameObject);
            var img = btn.GetComponent<Image>();
            if (img != null && !string.IsNullOrEmpty(record.recordId)) lineImages[record.recordId] = img;
        }
    }

    /// <summary>선택된 증언 문장들을 강조한다. null/빈 목록이면 전부 해제.</summary>
    public void SetSelected(ICollection<string> selectedRecordIds)
    {
        bool any = selectedRecordIds != null && selectedRecordIds.Count > 0;
        foreach (var kv in lineImages)
            kv.Value.color = (any && selectedRecordIds.Contains(kv.Key)) ? LineSelectedColor : LineNormalColor;
    }
}
