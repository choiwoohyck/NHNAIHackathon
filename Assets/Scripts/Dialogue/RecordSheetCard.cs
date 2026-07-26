using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 용의자 한 명의 취조 기록지를 표현하는 오브젝트.
// InterrogationController가 취조를 중지/종료할 때마다 RecordBookController를 통해 생성/갱신된다.
// 문장 하나하나가 버튼으로 되어 있어서, 이후 모순 제시 기능에서 이 클릭 이벤트를 그대로 사용하면 된다.
public class RecordSheetCard : MonoBehaviour
{
    Text titleText;
    RectTransform lineContainer;
    readonly List<GameObject> lineObjects = new List<GameObject>();

    public void Build()
    {
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

        foreach (var record in session.statements)
        {
            var btn = DialogueUIUtil.CreateButton(lineContainer, "Line_" + record.recordId, record.text, new Color(1f, 1f, 1f, 0.05f));
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;

            var label = btn.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.fontSize = 16;

            var captured = record;
            btn.onClick.AddListener(() => OnLineClicked(captured));

            lineObjects.Add(btn.gameObject);
        }
    }

    void OnLineClicked(StatementRecord record)
    {
        Debug.Log("[기록 선택] (" + record.suspectName + ") " + record.text);
        // TODO: 모순 판정 컨트롤러와 연결해서 다른 용의자 진술과 비교하는 로직을 붙이면 된다.
    }
}
