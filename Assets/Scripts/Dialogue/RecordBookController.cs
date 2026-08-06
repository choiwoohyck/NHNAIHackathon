using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 취조 중지/종료 시 진술을 "기록지 오브젝트"로 만들어 책상 위에 보관/전시하는 매니저.
// 용의자별로 RecordSheetCard 오브젝트 하나씩을 만들고, 같은 용의자가 재호출되어
// 진술이 추가되면 새 카드를 만들지 않고 기존 카드 내용만 갱신한다.
//
// 기록 문장 클릭은 OnStatementClicked 콜백으로 상위(InterrogationController)에 전달되어
// 방향 그래프의 모순 질문 해금 판정에 쓰인다.
public class RecordBookController : MonoBehaviour
{
    // 기록 문장(증언)을 클릭했을 때 호출된다. InterrogationController가 구독한다.
    public Action<StatementRecord> OnStatementClicked;

    [Header("커스텀 UI 참조 (선택, 비워두면 자동 생성)")]
    [Tooltip("비워두면 런타임에 자동 생성된다. 지정하면 해당 오브젝트를 그대로 사용한다.")]
    [SerializeField] Canvas canvasOverride;
    [SerializeField] RectTransform bookPanelOverride;
    [SerializeField] Text titleOverride;
    [SerializeField] Button closeBtnOverride;
    [SerializeField] RectTransform cardContainerOverride;

    RectTransform bookPanel;
    RectTransform cardContainer;

    readonly Dictionary<string, RecordSheetCard> cards = new Dictionary<string, RecordSheetCard>();

    void Awake()
    {
        DialogueUIUtil.EnsureEventSystem();
        BuildUI();
        bookPanel.gameObject.SetActive(false);
    }

    void BuildUI()
    {
        var canvas = canvasOverride != null ? canvasOverride : DialogueUIUtil.CreateCanvas("RecordBookCanvas", 20);

        bookPanel = bookPanelOverride != null
            ? bookPanelOverride
            : DialogueUIUtil.CreatePanel(canvas.transform, "RecordBookPanel", new Color(0.05f, 0.05f, 0.05f, 0.92f));
        if (bookPanelOverride == null)
            DialogueUIUtil.Stretch(bookPanel, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.9f), Vector2.zero, Vector2.zero);

        var title = titleOverride != null
            ? titleOverride
            : DialogueUIUtil.CreateText(bookPanel, "Title", 26, TextAnchor.MiddleLeft, Color.white);
        if (titleOverride == null)
        {
            title.fontStyle = FontStyle.Bold;
            title.text = "취조 기록지  —  서로 모순되는 문장 2개를 선택하세요";
            DialogueUIUtil.Stretch(title.rectTransform, new Vector2(0f, 0.92f), new Vector2(0.85f, 1f), new Vector2(20, 0), new Vector2(0, 0));
        }

        var closeBtn = closeBtnOverride != null
            ? closeBtnOverride
            : DialogueUIUtil.CreateButton(bookPanel, "CloseBtn", "닫기", new Color(0.3f, 0.1f, 0.1f, 0.9f));
        if (closeBtnOverride == null)
            DialogueUIUtil.Stretch(closeBtn.GetComponent<RectTransform>(), new Vector2(0.87f, 0.92f), new Vector2(1f, 1f), new Vector2(0, 5), new Vector2(-15, -5));
        closeBtn.onClick.AddListener(Hide);

        if (cardContainerOverride != null)
        {
            cardContainer = cardContainerOverride;
        }
        else
        {
            var containerGO = new GameObject("CardContainer", typeof(RectTransform));
            containerGO.transform.SetParent(bookPanel, false);
            cardContainer = containerGO.GetComponent<RectTransform>();
            DialogueUIUtil.Stretch(cardContainer, new Vector2(0f, 0f), new Vector2(1f, 0.9f), new Vector2(20, 20), new Vector2(-20, -10));

            var layout = containerGO.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 15;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
        }
    }

    public bool IsVisible => bookPanel != null && bookPanel.gameObject.activeSelf;
    public void ToggleVisible() => bookPanel.gameObject.SetActive(!bookPanel.gameObject.activeSelf);
    public void Show() => bookPanel.gameObject.SetActive(true);
    public void Hide() => bookPanel.gameObject.SetActive(false);

    /// <summary>
    /// 세션 데이터를 기반으로 기록지 카드 오브젝트를 생성하거나 이미 있으면 내용만 갱신한다.
    /// (기록지는 취조 중지/종료 시점에 생성된다.)
    /// </summary>
    public void AddOrUpdateSheet(SuspectSession session)
    {
        RecordSheetCard card;
        if (!cards.TryGetValue(session.suspectId, out card))
        {
            var cardGO = new GameObject("RecordCard_" + session.suspectId, typeof(RectTransform));
            cardGO.transform.SetParent(cardContainer, false);
            cardGO.AddComponent<LayoutElement>().preferredWidth = 320;

            card = cardGO.AddComponent<RecordSheetCard>();
            card.Build(rec => OnStatementClicked?.Invoke(rec));
            cards[session.suspectId] = card;
        }

        card.Refresh(session);
    }

    /// <summary>모든 기록지에서 선택 강조를 갱신한다. null/비어 있으면 전부 해제.</summary>
    public void SetSelected(ICollection<string> selectedRecordIds)
    {
        foreach (var card in cards.Values) card.SetSelected(selectedRecordIds);
    }
}
