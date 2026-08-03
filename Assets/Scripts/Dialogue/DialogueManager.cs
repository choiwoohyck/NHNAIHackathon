using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public enum DialogueState
{
    Idle,
    Speaking,
    ChoiceWait
}

// 대화창 / 선택지창을 관리한다.
// 대사가 나오는 동안(Speaking)에는 DialoguePanel만 켜져 있고,
// 질문을 골라야 하는 타이밍(ChoiceWait)에는 DialoguePanel이 꺼지고 ChoicePanel이 대신 켜진다.
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("연출 설정")]
    public float typingSpeed = 0.02f; // 글자당 출력 간격(초). 0이면 즉시 전체 출력
    public Color dialoguePanelColor = new Color(0f, 0f, 0f, 0.75f);
    public Color choicePanelColor = new Color(0f, 0f, 0f, 0.85f);
    public Color choiceButtonColor = new Color(1f, 1f, 1f, 0.08f);
    public Color contradictionChoiceColor = new Color(0.5f, 0.12f, 0.12f, 0.55f); // 모순 질문은 붉게 강조

    [Header("커스텀 UI 참조 (선택, 비워두면 자동 생성)")]
    [Tooltip("비워두면 런타임에 자동 생성된다. 지정하면 해당 오브젝트를 그대로 사용한다.")]
    [SerializeField] Canvas canvasOverride;
    [SerializeField] RectTransform dialoguePanelOverride;
    [SerializeField] Text speakerTextOverride;
    [SerializeField] Text bodyTextOverride;
    [SerializeField] Text continueHintTextOverride;
    [SerializeField] RectTransform choicePanelOverride;
    [SerializeField] RectTransform choiceListContainerOverride;

    public DialogueState State { get; private set; } = DialogueState.Idle;

    RectTransform dialoguePanel;
    Text speakerText;
    Text bodyText;
    Text continueHintText;

    RectTransform choicePanel;
    RectTransform choiceListContainer;
    readonly List<GameObject> spawnedChoiceButtons = new List<GameObject>();

    Queue<DialogueLine> lineQueue = new Queue<DialogueLine>();
    Action onLinesDone;
    Coroutine typingRoutine;
    string currentLineFullText;
    bool lineFullyShown;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        DialogueUIUtil.EnsureEventSystem();
        BuildUI();
        SetDialoguePanelVisible(false);
        SetChoicePanelVisible(false);
    }

    void BuildUI()
    {
        var canvas = canvasOverride != null ? canvasOverride : DialogueUIUtil.CreateCanvas("DialogueCanvas", 10);

        // --- 대사창 ---
        dialoguePanel = dialoguePanelOverride != null
            ? dialoguePanelOverride
            : DialogueUIUtil.CreatePanel(canvas.transform, "DialoguePanel", dialoguePanelColor);
        if (dialoguePanelOverride == null)
            DialogueUIUtil.Stretch(dialoguePanel, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.3f), Vector2.zero, Vector2.zero);

        speakerText = speakerTextOverride != null
            ? speakerTextOverride
            : DialogueUIUtil.CreateText(dialoguePanel, "SpeakerText", 28, TextAnchor.UpperLeft, Color.white);
        if (speakerTextOverride == null)
        {
            speakerText.fontStyle = FontStyle.Bold;
            DialogueUIUtil.Stretch(speakerText.rectTransform, new Vector2(0f, 0.72f), new Vector2(1f, 1f), new Vector2(30, 0), new Vector2(-30, -10));
        }

        bodyText = bodyTextOverride != null
            ? bodyTextOverride
            : DialogueUIUtil.CreateText(dialoguePanel, "BodyText", 24, TextAnchor.UpperLeft, Color.white);
        if (bodyTextOverride == null)
            DialogueUIUtil.Stretch(bodyText.rectTransform, new Vector2(0f, 0.1f), new Vector2(1f, 0.72f), new Vector2(30, 0), new Vector2(-30, 0));

        continueHintText = continueHintTextOverride != null
            ? continueHintTextOverride
            : DialogueUIUtil.CreateText(dialoguePanel, "ContinueHint", 18, TextAnchor.LowerRight, new Color(1f, 1f, 1f, 0.6f));
        if (continueHintTextOverride == null)
        {
            continueHintText.text = "▼ 클릭하여 계속";
            DialogueUIUtil.Stretch(continueHintText.rectTransform, new Vector2(0.6f, 0f), new Vector2(1f, 0.12f), new Vector2(0, 0), new Vector2(-20, 0));
        }

        // 대사창 전체를 클릭하면 다음 대사로 진행되도록 패널 자체에 버튼을 붙인다(이미 있으면 재사용).
        var advanceButton = dialoguePanel.GetComponent<Button>();
        if (advanceButton == null) advanceButton = dialoguePanel.gameObject.AddComponent<Button>();
        advanceButton.transition = Selectable.Transition.None;
        advanceButton.onClick.AddListener(OnDialogueClicked);

        // --- 선택지창 ---
        choicePanel = choicePanelOverride != null
            ? choicePanelOverride
            : DialogueUIUtil.CreatePanel(canvas.transform, "ChoicePanel", choicePanelColor);
        if (choicePanelOverride == null)
            DialogueUIUtil.Stretch(choicePanel, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.4f), Vector2.zero, Vector2.zero);

        if (choiceListContainerOverride != null)
        {
            choiceListContainer = choiceListContainerOverride;
        }
        else
        {
            var listGO = new GameObject("ChoiceList", typeof(RectTransform));
            listGO.transform.SetParent(choicePanel, false);
            choiceListContainer = listGO.GetComponent<RectTransform>();
            DialogueUIUtil.Stretch(choiceListContainer, Vector2.zero, Vector2.one, new Vector2(20, 20), new Vector2(-20, -20));

            var layout = listGO.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            var fitter = listGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    void SetDialoguePanelVisible(bool visible) => dialoguePanel.gameObject.SetActive(visible);
    void SetChoicePanelVisible(bool visible) => choicePanel.gameObject.SetActive(visible);

    /// <summary>
    /// 대사 여러 줄을 순서대로 출력한다. 다 출력되면 onComplete가 호출된다.
    /// 호출 즉시 선택지창은 꺼지고 대사창이 켜진다.
    /// </summary>
    public void ShowLines(IEnumerable<DialogueLine> lines, Action onComplete)
    {
        SetChoicePanelVisible(false);
        SetDialoguePanelVisible(true);

        lineQueue = new Queue<DialogueLine>(lines);
        onLinesDone = onComplete;
        State = DialogueState.Speaking;
        ShowNextLine();
    }

    void ShowNextLine()
    {
        if (lineQueue.Count == 0)
        {
            SetDialoguePanelVisible(false);
            State = DialogueState.Idle;
            var callback = onLinesDone;
            onLinesDone = null;
            callback?.Invoke();
            return;
        }

        var line = lineQueue.Dequeue();
        speakerText.text = line.speaker;

        if (typingRoutine != null) StopCoroutine(typingRoutine);
        typingRoutine = StartCoroutine(TypeLine(line.text));
    }

    IEnumerator TypeLine(string full)
    {
        currentLineFullText = full;
        lineFullyShown = false;
        bodyText.text = "";
        continueHintText.gameObject.SetActive(false);

        if (typingSpeed <= 0f)
        {
            bodyText.text = full;
        }
        else
        {
            var sb = new StringBuilder();
            for (int i = 0; i < full.Length; i++)
            {
                sb.Append(full[i]);
                bodyText.text = sb.ToString();
                yield return new WaitForSeconds(typingSpeed);
            }
        }

        lineFullyShown = true;
        continueHintText.gameObject.SetActive(true);
    }

    void OnDialogueClicked()
    {
        if (State != DialogueState.Speaking) return;

        if (!lineFullyShown)
        {
            // 타이핑 중에 클릭하면 즉시 완성해서 보여준다.
            if (typingRoutine != null) StopCoroutine(typingRoutine);
            bodyText.text = currentLineFullText;
            lineFullyShown = true;
            continueHintText.gameObject.SetActive(true);
            return;
        }

        ShowNextLine();
    }

    /// <summary>
    /// 선택 가능한 질문 목록을 보여준다. 대사창은 꺼지고 선택지창이 켜진다.
    /// 플레이어가 하나를 고르면 선택지창이 다시 꺼지고 onSelected가 호출된다.
    /// 목록은 CaseGraph가 현재 진행 상태에 맞춰 계산해 넘겨준다(방향 그래프 해금 결과).
    /// </summary>
    public void ShowChoices(List<QuestionNode> questions, Action<QuestionNode> onSelected)
    {
        SetDialoguePanelVisible(false);
        SetChoicePanelVisible(true);
        State = DialogueState.ChoiceWait;

        foreach (var go in spawnedChoiceButtons) Destroy(go);
        spawnedChoiceButtons.Clear();

        if (questions == null || questions.Count == 0)
        {
            var hint = DialogueUIUtil.CreateText(choiceListContainer, "NoQuestionHint", 20, TextAnchor.MiddleCenter, Color.gray);
            hint.text = "(더 물어볼 질문이 없습니다)";
            var hintLe = hint.gameObject.AddComponent<LayoutElement>();
            hintLe.preferredHeight = 50;
            spawnedChoiceButtons.Add(hint.gameObject);
            return;
        }

        foreach (var q in questions)
        {
            var color = q.kind == NodeKind.Contradiction ? contradictionChoiceColor : choiceButtonColor;
            var button = DialogueUIUtil.CreateButton(choiceListContainer, "Choice_" + q.id, q.label, color);
            var le = button.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 50;

            var captured = q;
            button.onClick.AddListener(() =>
            {
                SetChoicePanelVisible(false);
                onSelected?.Invoke(captured);
            });

            spawnedChoiceButtons.Add(button.gameObject);
        }
    }

    /// <summary>
    /// 대사창/선택지창을 모두 끄고 대기 상태로 되돌린다. 취조 중지/종료, 용의자 전환 시 사용.
    /// </summary>
    public void ResetToIdle()
    {
        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }
        lineQueue.Clear();
        onLinesDone = null;

        SetDialoguePanelVisible(false);
        SetChoicePanelVisible(false);
        State = DialogueState.Idle;
    }
}
