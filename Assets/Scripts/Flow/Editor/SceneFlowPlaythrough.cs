using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 실제로 플레이모드를 켜고 버튼을 눌러 한 바퀴 돌려보는 검증기.
// SceneFlowValidator가 '연결이 되어 있는가'를 본다면, 이쪽은 '정말 도는가'를 본다.
//
//   1바퀴: Start → (START) → CaseSelect → (사건 열기 → 수사 시작) → Main
//          → (사건 판결: 진범 지목 + 정답 입력 → 제출) → EndingScene(유죄)
//          → (다시 수사하기) → CaseSelect → (← 타이틀) → Start
//   2바퀴: 두 번째 사건을 골라 같은 경로를 돌되, 엉뚱한 용의자를 지목해
//          EndingScene(무죄) → (처음으로) → Start  ← 재플레이 시 상태가 남지 않는지도 함께 본다.
//
// 실행: 메뉴 Tools ▸ Editor0 ▸ 씬 흐름 플레이 검증
//       또는 배치모드 -executeMethod SceneFlowPlaythrough.RunFromCommandLine (실패 시 종료코드 1)
public static class SceneFlowPlaythrough
{
    [MenuItem("Tools/Editor0/씬 흐름 플레이 검증")]
    public static void RunFromMenu() => Begin(false);

    public static void RunFromCommandLine() => Begin(true);

    static bool savedOptionsEnabled;
    static EnterPlayModeOptions savedOptions;

    static void Begin(bool exitWhenDone)
    {
        FlowDriver.ExitProcessWhenDone = exitWhenDone;

        var start = EditorBuildSettings.scenes.FirstOrDefault(
            s => s.enabled && System.IO.Path.GetFileNameWithoutExtension(s.path) == "Start");
        if (start == null)
        {
            Debug.LogError("[Playthrough] Build Settings에 Start 씬이 없습니다.");
            if (exitWhenDone) EditorApplication.Exit(1);
            return;
        }

        // 배치모드에서는 플레이 진입 시의 도메인 리로드가 에디터 루프를 멈춰 세운다.
        // 리로드를 끄면 같은 도메인에서 그대로 이어져 헤드리스로 돌릴 수 있다.
        // (게임 쪽 static 상태는 StartController가 직접 초기화하므로 오히려 그 동작까지 함께 검증된다.)
        savedOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        savedOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        FlowDriver.RestoreEditorSettings = RestoreEditorSettings;

        // 이 검증의 대상은 '실제 게임 루프'다. 튜토리얼은 START를 가로채 별도 경로로 빠지므로
        // 이미 본 상태로 맞춰 두고 돈다(튜토리얼 경로는 TutorialPlaythrough가 따로 본다).
        TutorialController.MarkSeen();

        EditorSceneManager.OpenScene(start.path, OpenSceneMode.Single);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    static void RestoreEditorSettings()
    {
        EditorSettings.enterPlayModeOptionsEnabled = savedOptionsEnabled;
        EditorSettings.enterPlayModeOptions = savedOptions;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        var go = new GameObject("~FlowDriver");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<FlowDriver>();
    }
}

// 플레이모드 안에서 실제로 버튼을 눌러 흐름을 진행시키는 드라이버.
// 에디터 어셈블리에 있으므로 빌드에는 절대 포함되지 않는다.
public class FlowDriver : MonoBehaviour
{
    public static bool ExitProcessWhenDone;
    public static System.Action RestoreEditorSettings;

    readonly List<string> steps = new List<string>();
    readonly List<string> failures = new List<string>();

    void Start() => StartCoroutine(Run());

    IEnumerator Run()
    {
        yield return Lap(
            lapName: "1바퀴 (첫 사건 · 진범 지목 → 유죄)",
            caseIndex: 0,
            pickCulprit: true,
            expected: VerdictResult.Success,
            exitLabel: "다시 수사하기",
            exitScene: "CaseSelect");

        // 엔딩에서 '다시 수사하기'로 돌아온 CaseSelect에서 타이틀까지 되돌아가 루프를 닫는다.
        if (failures.Count == 0)
        {
            yield return ClickAndWait("← 타이틀", "Start", 15f);
        }

        // 2바퀴 — 두 번째 사건을 고르고, 일부러 오인 지목한다.
        if (failures.Count == 0)
        {
            yield return Lap(
                lapName: "2바퀴 (두 번째 사건 · 오인 지목 → 무죄)",
                caseIndex: 1,
                pickCulprit: false,
                expected: VerdictResult.WrongSuspect,
                exitLabel: "처음으로",
                exitScene: "Start");
        }

        Report();
    }

    // ------------------------------------------------------------------
    IEnumerator Lap(string lapName, int caseIndex, bool pickCulprit,
                    VerdictResult expected, string exitLabel, string exitScene)
    {
        Step("── " + lapName + " ──");

        // [Start] 타이틀 — 새 판 시작이므로 이전 상태가 지워져 있어야 한다.
        yield return WaitForScene("Start", 20f);
        if (Failed) yield break;

        if (GameSession.SelectedCase != null)
            Fail("Start 씬 진입 후에도 GameSession.SelectedCase가 남아 있습니다.");
        if (GameSession.HasVerdict)
            Fail("Start 씬 진입 후에도 이전 판결(GameSession.HasVerdict)이 남아 있습니다.");
        else
            Step("Start: 이전 판 상태가 초기화됨");

        yield return ClickAndWait("START", "CaseSelect", 20f);
        if (Failed) yield break;

        // [CaseSelect] 사건 선택
        var caseSelect = Object.FindFirstObjectByType<CaseSelectController>();
        if (caseSelect == null) { Fail("CaseSelect 씬에서 CaseSelectController를 찾지 못했습니다."); yield break; }

        var cases = (InterrogationCase[])Private(caseSelect, "cases");
        if (cases == null || cases.Length == 0) { Fail("CaseSelect: 사건 목록이 비어 있습니다."); yield break; }

        int target = Mathf.Clamp(caseIndex, 0, cases.Length - 1);
        for (int guard = 0; guard < cases.Length && (int)Private(caseSelect, "current") != target; guard++)
        {
            if (!Click("▶")) break;
            yield return null;
        }
        int chosen = (int)Private(caseSelect, "current");
        if (chosen != target)
            Step("CaseSelect: 사건 " + (target + 1) + "번을 고르지 못해 " + (chosen + 1) + "번으로 진행합니다(사건 수 " + cases.Length + ").");
        else
            Step("CaseSelect: 사건 " + (chosen + 1) + "/" + cases.Length + " 선택 (" + cases[chosen].caseTitle + ")");

        if (!Click("사건 열기")) { Fail("CaseSelect: '사건 열기' 버튼을 찾지 못했습니다."); yield break; }

        // 여는 영상/크로스페이드가 끝나고 입력 잠금(busy)이 풀릴 때까지 기다린다.
        // 버튼이 보이기만 해도 아직 연출 중이면 클릭이 무시되므로 busy까지 확인해야 한다.
        yield return WaitUntil(() =>
            {
                var b = FindButton("수사 시작");
                return b != null && b.gameObject.activeInHierarchy && !(bool)Private(caseSelect, "busy");
            },
            30f, "CaseSelect: '수사 시작'을 누를 수 있는 상태가 되지 않았습니다(파일 여는 연출이 끝나지 않음).");
        if (Failed) yield break;
        Step("CaseSelect: 사건 파일 열기 연출 완료");

        var expectedCase = cases[chosen];
        yield return ClickAndWait("수사 시작", "Main", 25f);
        if (Failed) yield break;

        // [Main] 취조 — 사건 선택 화면에서 고른 사건이 실제로 넘어왔는지 확인
        if (!ReferenceEquals(GameSession.SelectedCase, expectedCase))
            Fail("Main: 선택한 사건이 전달되지 않았습니다. 기대=" + expectedCase.name +
                 ", 실제=" + (GameSession.SelectedCase != null ? GameSession.SelectedCase.name : "(없음)"));

        var interrogation = Object.FindFirstObjectByType<InterrogationController>();
        if (interrogation == null) { Fail("Main 씬에서 InterrogationController를 찾지 못했습니다."); yield break; }

        yield return WaitUntil(() => Private(interrogation, "graph") != null, 10f,
                               "Main: 사건 그래프가 만들어지지 않았습니다.");
        if (Failed) yield break;

        var graph = (CaseGraph)Private(interrogation, "graph");
        if (graph.caseTitle != expectedCase.caseTitle)
            Fail("Main: 로드된 사건이 다릅니다. 기대='" + expectedCase.caseTitle + "', 실제='" + graph.caseTitle + "'");
        else
            Step("Main: 선택한 사건이 그대로 로드됨 (" + graph.caseTitle + ", 용의자 " + graph.Suspects.Count + "명)");

        if (GameSession.HasVerdict)
            Fail("Main 진입 시 이전 판결이 남아 있습니다(재수사 시 결과가 섞입니다).");

        // 판결 패널 열기 → 용의자 지목 → 빈칸 채우기 → 제출
        if (!Click("사건 판결", "Verdict", "VerdictBtn"))
        {
            Fail("Main: '사건 판결' 버튼을 찾지 못했습니다.");
            yield break;
        }
        yield return null;

        var verdict = Object.FindFirstObjectByType<VerdictController>();
        if (verdict == null || !verdict.IsOpen) { Fail("Main: 판결 패널이 열리지 않았습니다."); yield break; }

        var suspectButtons = (IDictionary)Private(verdict, "suspectButtons");
        if (suspectButtons == null || suspectButtons.Count == 0) { Fail("판결: 용의자 버튼이 없습니다."); yield break; }

        string culprit = graph.culpritSuspectId;
        string pick = null;
        foreach (var key in suspectButtons.Keys)
        {
            var id = (string)key;
            if (pickCulprit ? id == culprit : id != culprit) { pick = id; break; }
        }
        if (pick == null) { Fail("판결: 지목할 용의자를 고르지 못했습니다(용의자가 1명뿐인가요?)."); yield break; }

        ((Button)suspectButtons[pick]).onClick.Invoke();
        Step("판결: 용의자 '" + pick + "' 지목 (진범=" + culprit + ")");

        // 빈칸은 항상 정답으로 채운다 — 실패는 오직 '누구를 지목했는가'에서만 나오게 하기 위함.
        int filled = FillAnswers(verdict);
        Step("판결: 사건 설명 " + filled + "칸을 정답으로 입력");

        if (!Click("제출")) { Fail("판결: '제출' 버튼을 찾지 못했습니다."); yield break; }

        // [EndingScene]
        yield return WaitForScene("EndingScene", 20f);
        if (Failed) yield break;

        // 결과 화면 앞에 엔딩 컷씬이 붙는다 — 배치모드에서는 영상이 안 뜨므로 건너뛴다.
        yield return SkipCutsceneIfPlaying();
        if (Failed) yield break;

        if (!GameSession.HasVerdict)
            Fail("EndingScene: 판결 결과가 전달되지 않았습니다.");
        else if (GameSession.LastVerdict != expected)
            Fail("EndingScene: 판정이 기대와 다릅니다. 기대=" + expected + ", 실제=" + GameSession.LastVerdict +
                 " (사건 설명 " + GameSession.CorrectFields + "/" + GameSession.TotalFields + ")");
        else
            Step("EndingScene: " + expected + " (사건 설명 " + GameSession.CorrectFields + "/" + GameSession.TotalFields + ")");

        if (Object.FindFirstObjectByType<EndingController>() == null)
            Fail("EndingScene: EndingController가 없습니다.");

        yield return ClickAndWait(exitLabel, exitScene, 20f);
        if (Failed) yield break;
        Step("EndingScene → " + exitScene + " ('" + exitLabel + "')");
    }

    // 엔딩 컷씬이 재생 중이면 '건너뛰기'를 눌러 결과 화면까지 진행시킨다.
    IEnumerator SkipCutsceneIfPlaying()
    {
        var player = Object.FindFirstObjectByType<CutscenePlayer>();
        if (player == null) yield break;

        var ending = Object.FindFirstObjectByType<EndingController>();
        int cuts = ending != null ? ending.BuildCutscene().Count : 0;
        if (cuts == 0) { Fail("엔딩 컷씬이 한 컷도 만들어지지 않았습니다."); yield break; }
        Step("엔딩 컷씬 " + cuts + "컷 구성됨");

        // 대사 컷이 클릭으로 넘어가는지 한 번 확인하고 나머지는 건너뛴다
        // (배치모드에서는 영상이 준비되지 않아 컷마다 타임아웃을 기다리게 된다).
        var advance = GameObject.Find("AdvanceArea");
        if (advance == null) { Fail("컷씬에 진행 영역(AdvanceArea)이 없습니다."); yield break; }

        // 첫 대사 컷은 페이드인과 최소 노출 시간이 있으므로 몇 프레임에 걸쳐 눌러 본다.
        var advanceButton = advance.GetComponent<Button>();
        for (int i = 0; i < 40; i++)
        {
            advanceButton.onClick.Invoke();
            yield return null;
        }

        var stillPlaying = Object.FindFirstObjectByType<CutscenePlayer>();
        if (stillPlaying == null || !stillPlaying.IsPlaying)
        {
            Fail("대사 컷에서 클릭 한 번에 컷씬 전체가 끝나버렸습니다.");
            yield break;
        }
        Step("대사 컷 클릭 진행 확인 — 나머지는 건너뛰기");

        var skip = FindButton("건너뛰기");
        if (skip == null) { Fail("엔딩 컷씬에 '건너뛰기' 버튼이 없습니다 — 넘어갈 방법이 없습니다."); yield break; }
        skip.onClick.Invoke();

        yield return WaitUntil(() =>
            {
                var p = Object.FindFirstObjectByType<CutscenePlayer>();
                return p == null || !p.IsPlaying;
            },
            10f, "'건너뛰기'를 눌렀지만 컷씬이 끝나지 않았습니다.");
        if (!Failed) yield return null;   // 결과 UI가 만들어질 한 프레임
    }

    // 판결 패널의 빈칸을 정답 보기 버튼을 눌러 채운다(플레이어가 하는 그대로).
    internal static int FillAnswers(VerdictController verdict)
    {
        var slots = Private(verdict, "slots") as IEnumerable;
        if (slots == null) return 0;

        int n = 0;
        foreach (var slot in slots)
        {
            var field = Private(slot, "field") as CulpritDetection.CaseField;
            var options = Private(slot, "options") as IDictionary;
            if (field == null || options == null) continue;

            var answer = field.answer ?? "";
            if (!options.Contains(answer)) continue;   // 보기에 없는 정답이면 채울 방법이 없다

            ((Button)options[answer]).onClick.Invoke();
            n++;
        }
        return n;
    }

    // ------------------------------------------------------------------
    // 버튼 / 씬 헬퍼
    // ------------------------------------------------------------------
    bool Failed => failures.Count > 0;

    // 라벨(Text/TMP) 또는 GameObject 이름으로 버튼을 찾는다.
    // 씬에 미리 배치된 아이콘 버튼은 글자가 없어서 이름으로만 찾을 수 있다(예: Main 씬의 'Verdict').
    static Button FindButton(params string[] identifiers)
    {
        var wanted = identifiers.Select(Normalize).ToArray();
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (b == null) continue;

            var txt = b.GetComponentInChildren<Text>(true);
            string s = txt != null ? txt.text : null;
            if (string.IsNullOrEmpty(s))
            {
                var tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true);
                s = tmp != null ? tmp.text : null;
            }

            var labelKey = Normalize(s);
            var nameKey = Normalize(b.gameObject.name);
            if (wanted.Any(w => w.Length > 0 && (w == labelKey || w == nameKey))) return b;
        }
        return null;
    }

    static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s) if (!char.IsWhiteSpace(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    bool Click(params string[] identifiers)
    {
        var b = FindButton(identifiers);
        if (b == null) return false;
        b.onClick.Invoke();
        return true;
    }

    IEnumerator ClickAndWait(string label, string sceneName, float timeout)
    {
        if (!Click(label))
        {
            Fail("'" + label + "' 버튼을 찾지 못했습니다 (현재 씬: " + SceneManager.GetActiveScene().name + ").");
            yield break;
        }
        yield return WaitForScene(sceneName, timeout);
    }

    IEnumerator WaitForScene(string sceneName, float timeout)
    {
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == sceneName, timeout,
            "'" + sceneName + "' 씬으로 넘어가지 않았습니다 (" + timeout + "초 대기, 현재 씬: " +
            SceneManager.GetActiveScene().name + ").");
        if (!Failed) yield return null;   // Start()가 한 번 돌 시간을 준다
    }

    IEnumerator WaitUntil(System.Func<bool> condition, float timeout, string failMessage)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (Time.realtimeSinceStartup < end)
        {
            bool ok = false;
            try { ok = condition(); } catch { }
            if (ok) yield break;
            yield return null;
        }
        Fail(failMessage);
    }

    internal static object Private(object target, string fieldName)
    {
        if (target == null) return null;
        var f = target.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return f != null ? f.GetValue(target) : null;
    }

    // ------------------------------------------------------------------
    void Step(string s) => steps.Add(s);

    void Fail(string s) => failures.Add(s);

    void Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("===== 씬 흐름 플레이 검증 =====");
        foreach (var s in steps) sb.AppendLine("  · " + s);
        foreach (var f in failures) sb.AppendLine("  ✗ " + f);
        sb.AppendLine(failures.Count == 0
            ? "결과: 통과 — 루프가 두 바퀴 모두 정상적으로 돌았습니다."
            : "결과: 실패 (" + failures.Count + "건)");

        var text = sb.ToString();
        Debug.Log(text);
        System.Console.WriteLine(text);

        if (RestoreEditorSettings != null) RestoreEditorSettings();

        if (ExitProcessWhenDone)
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        else
            EditorApplication.isPlaying = false;
    }
}
