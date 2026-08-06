using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 화면의 전화기 오브젝트를 클릭하면 용의자 호출 목록 팝업을 띄우는 컨트롤러.
// 팝업 속 "OO 호출" 버튼은 기존 SuspectBar의 호출 버튼과 동일하게 동작한다
// (InterrogationController.CallSuspect를 그대로 호출).
public class PhoneCallController : MonoBehaviour
{
    [Header("커스텀 UI 참조 (선택, 비워두면 자동 생성)")]
    [Tooltip("전화기 아이콘에 쓸 스프라이트(예: Telephone.png). 비워두면 색상 버튼 + '전화' 텍스트로 대체된다.")]
    [SerializeField] Sprite phoneSprite;
    [SerializeField] Canvas canvasOverride;
    [SerializeField] Button phoneButtonOverride;
    [SerializeField] RectTransform popupPanelOverride;
    [SerializeField] Transform popupListContainerOverride;

    Button phoneButton;
    RectTransform popupPanel;
    Transform popupListContainer;
    readonly List<GameObject> spawnedButtons = new List<GameObject>();

    Action<string> onCallSuspect;

    void Awake()
    {
        DialogueUIUtil.EnsureEventSystem();
        BuildUI();
        SetPopupVisible(false);
    }

    void BuildUI()
    {
        var canvas = canvasOverride != null ? canvasOverride : DialogueUIUtil.CreateCanvas("PhoneCallCanvas", 8);

        // --- 전화기 아이콘 (화면 우하단) ---
        if (phoneButtonOverride != null)
        {
            phoneButton = phoneButtonOverride;
        }
        else
        {
            var go = new GameObject("PhoneButton", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);

            var image = go.AddComponent<Image>();
            if (phoneSprite != null)
            {
                image.sprite = phoneSprite;
                image.preserveAspect = true;
            }
            else
            {
                image.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
                var label = DialogueUIUtil.CreateText(go.transform, "Label", 20, TextAnchor.MiddleCenter, Color.white);
                DialogueUIUtil.Stretch(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                label.text = "전화";
            }

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(140, 140);
            rt.anchoredPosition = new Vector2(-40, 40);

            phoneButton = go.AddComponent<Button>();
        }
        phoneButton.onClick.AddListener(TogglePopup);

        // --- 호출 팝업 (전화기 바로 위) ---
        popupPanel = popupPanelOverride != null
            ? popupPanelOverride
            : DialogueUIUtil.CreatePanel(canvas.transform, "PhoneCallPopup", new Color(0.05f, 0.05f, 0.05f, 0.92f));
        if (popupPanelOverride == null)
        {
            popupPanel.anchorMin = popupPanel.anchorMax = new Vector2(1f, 0f);
            popupPanel.pivot = new Vector2(1f, 0f);
            popupPanel.sizeDelta = new Vector2(260, 60);
            popupPanel.anchoredPosition = new Vector2(-40, 190);

            var panelFitter = popupPanel.gameObject.AddComponent<ContentSizeFitter>();
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        if (popupListContainerOverride != null)
        {
            popupListContainer = popupListContainerOverride;
        }
        else
        {
            var listGO = new GameObject("List", typeof(RectTransform));
            listGO.transform.SetParent(popupPanel, false);
            var listRt = listGO.GetComponent<RectTransform>();
            DialogueUIUtil.Stretch(listRt, Vector2.zero, Vector2.one, new Vector2(10, 10), new Vector2(-10, -10));

            var layout = listGO.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            var fitter = listGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            popupListContainer = listRt;
        }
    }

    /// <summary>
    /// 용의자 목록으로 팝업 버튼을 (다시) 구성한다. onSelected는 기존 "OO 호출" 버튼과 동일한 콜백
    /// (InterrogationController가 CallSuspect를 넘겨준다).
    /// </summary>
    public void SetSuspects(IEnumerable<SuspectData> suspects, Action<string> onSelected)
    {
        onCallSuspect = onSelected;

        foreach (var go in spawnedButtons) Destroy(go);
        spawnedButtons.Clear();

        foreach (var s in suspects)
        {
            var id = s.id;
            var btn = DialogueUIUtil.CreateButton(popupListContainer, "Call_" + id, s.suspectName + " 호출", new Color(0.2f, 0.2f, 0.2f, 0.8f));
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            btn.onClick.AddListener(() =>
            {
                SetPopupVisible(false);
                onCallSuspect?.Invoke(id);
            });
            spawnedButtons.Add(btn.gameObject);
        }
    }

    void TogglePopup() => SetPopupVisible(!popupPanel.gameObject.activeSelf);
    void SetPopupVisible(bool visible) => popupPanel.gameObject.SetActive(visible);
}
