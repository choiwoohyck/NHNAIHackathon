using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 튜토리얼 경로를 타이틀에서부터 끝까지 실제로 밟아보는 검증기.
//
//   타이틀 START(첫 플레이) → 사건 선택을 건너뛰고 취조실 → 튜토리얼 전용 사건 →
//   비트를 하나씩 따라가 모순 성립 → 타이틀 복귀 → 다시 START 하면 이번엔 사건 선택으로
//
// 실행: Tools ▸ Editor0 ▸ 튜토리얼 플레이 검증
//       배치모드 -executeMethod TutorialPlaythrough.RunFromCommandLine (실패 시 종료코드 1)
public static class TutorialPlaythrough
{
    [MenuItem("Tools/Editor0/튜토리얼 플레이 검증")]
    public static void RunFromMenu() => Begin(false);

    public static void RunFromCommandLine() => Begin(true);

    static bool savedOptionsEnabled;
    static EnterPlayModeOptions savedOptions;

    static void Begin(bool exitWhenDone)
    {
        TutorialDriver.ExitProcessWhenDone = exitWhenDone;

        var start = EditorBuildSettings.scenes.FirstOrDefault(
            s => s.enabled && System.IO.Path.GetFileNameWithoutExtension(s.path) == "Start");
        if (start == null)
        {
            Debug.LogError("[TutorialPlaythrough] Build Settings에 Start 씬이 없습니다.");
            if (exitWhenDone) EditorApplication.Exit(1);
            return;
        }

        // 배치모드에서 플레이 진입 시의 도메인 리로드는 에디터 루프를 멈춰 세운다.
        savedOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        savedOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        TutorialDriver.RestoreEditorSettings = () =>
        {
            EditorSettings.enterPlayModeOptionsEnabled = savedOptionsEnabled;
            EditorSettings.enterPlayModeOptions = savedOptions;
        };

        // '처음 하는 플레이어' 상태로 맞춘다.
        TutorialController.ForgetSeen();
        GameSession.SelectedCase = null;
        GameSession.TutorialMode = false;
        GameSession.ClearVerdict();

        EditorSceneManager.OpenScene(start.path, OpenSceneMode.Single);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        var go = new GameObject("~TutorialDriver");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<TutorialDriver>();
    }
}

// 튜토리얼 비트를 하나씩 읽어, 그 비트가 지목한 버튼을 눌러 진행시킨다.
public class TutorialDriver : MonoBehaviour
{
    public static bool ExitProcessWhenDone;
    public static System.Action RestoreEditorSettings;

    readonly List<string> steps = new List<string>();
    readonly List<string> failures = new List<string>();

    void Start() => StartCoroutine(Run());

    IEnumerator Run()
    {
        // --- 타이틀 → How To Play → 튜토리얼로 해보기 ---
        // (START가 첫 플레이에 튜토리얼을 가로챌지는 씬 설정에 달렸으므로,
        //  씬 설정과 무관하게 항상 존재하는 이 입구로 검증한다.)
        yield return WaitForScene("Start", 20f);
        if (Failed) { Report(); yield break; }

        if (!Click("How To Play")) { Fail("타이틀에서 'How To Play' 버튼을 찾지 못했습니다."); Report(); yield break; }
        yield return null;
        Step("타이틀: How To Play 열기");

        if (!Click("튜토리얼로 해보기")) { Fail("설명서에서 '튜토리얼로 해보기' 버튼을 찾지 못했습니다."); Report(); yield break; }
        Step("설명서: 튜토리얼로 해보기");

        // 사건 선택을 건너뛰고 곧장 취조실로 가야 한다.
        yield return WaitForScene("Main", 20f);
        if (Failed)
        {
            Fail("현재 씬: " + SceneManager.GetActiveScene().name + " — 튜토리얼은 사건 선택을 건너뛰고 Main으로 가야 합니다.");
            Report();
            yield break;
        }
        Step("사건 선택을 거치지 않고 취조실로 진입");

        if (GameSession.SelectedCase == null)
            Fail("튜토리얼 사건이 물려 있지 않습니다.");
        else if (GameSession.SelectedCase.caseId != "TUTORIAL")
            Fail("튜토리얼 사건이 아닙니다: " + GameSession.SelectedCase.caseId);
        else
            Step("튜토리얼 전용 사건 로드됨 (" + GameSession.SelectedCase.caseTitle + ")");
        if (Failed) { Report(); yield break; }

        // --- 튜토리얼 비트 진행 ---
        // 튜토리얼은 사건 브리핑 대사가 끝난 뒤에 뜬다 — 대사를 넘겨 주며 기다린다.
        TutorialController tutorial = null;
        yield return WaitUntil(() =>
        {
            AdvanceDialogueIfSpeaking();
            tutorial = Object.FindFirstObjectByType<TutorialController>();
            return tutorial != null;
        }, 30f, "튜토리얼이 시작되지 않았습니다(브리핑 후 TutorialController를 찾지 못함).");
        if (Failed) { Report(); yield break; }

        var plan = Private(tutorial, "plan");
        if (plan == null) { Fail("TutorialPlan을 만들지 못했습니다 — 튜토리얼 사건에 안내할 모순 경로가 없습니다."); Report(); yield break; }
        Step("경로 도출: " + Describe(plan));

        var beats = Private(tutorial, "beats") as IList;
        Step("비트 " + (beats != null ? beats.Count : 0) + "개 생성됨");

        int guard = 0;
        int lastIndex = -1;
        bool sawContradiction = false;
        bool sawVerdict = false;
        VerdictResult verdictResult = VerdictResult.WrongSuspect;

        while (tutorial != null && !tutorial.IsFinished && guard++ < 200)
        {
            int i = (int)Private(tutorial, "index");
            if (i != lastIndex)
            {
                lastIndex = i;
                Step("[" + i + "] " + Flatten((string)Private(beats[i], "caption")));
            }

            yield return DriveCurrentBeat(tutorial, beats);
            if (Failed) break;

            if ((bool)Private(tutorial, "contradictionDone")) sawContradiction = true;

            // 판정 결과는 씬이 바뀌기 전에 잡아둬야 한다(타이틀로 가면 초기화된다).
            if (!sawVerdict && (bool)Private(tutorial, "verdictSubmitted"))
            {
                sawVerdict = true;
                verdictResult = GameSession.LastVerdict;
            }

            yield return null;
        }

        if (!Failed && !sawContradiction)
            Fail("모순이 성립하지 않았습니다 — 튜토리얼이 핵심을 가르치지 못했습니다.");
        else if (!Failed)
            Step("모순 성립 확인");

        if (!Failed && !sawVerdict)
            Fail("판결까지 진행되지 않았습니다 — 튜토리얼에서 판단을 해보지 못했습니다.");
        else if (!Failed && verdictResult != VerdictResult.Success)
            Fail("판결 결과가 유죄가 아닙니다: " + verdictResult + " (정답만 넣었으므로 Success여야 합니다).");
        else if (!Failed)
            Step("판결 진행 확인 — " + verdictResult);

        if (Failed) { Report(); yield break; }

        // --- 타이틀 복귀 ---
        yield return WaitForScene("Start", 20f);
        if (Failed)
        {
            Fail("튜토리얼이 끝난 뒤 타이틀로 돌아오지 않았습니다 (현재 씬: " + SceneManager.GetActiveScene().name + ").");
            Report();
            yield break;
        }
        Step("튜토리얼 종료 후 타이틀 복귀");

        if (!TutorialController.AlreadySeen) Fail("튜토리얼 완료 표시가 기록되지 않았습니다.");
        if (GameSession.TutorialMode) Fail("타이틀로 왔는데 TutorialMode가 아직 켜져 있습니다.");
        if (GameSession.SelectedCase != null) Fail("타이틀로 왔는데 튜토리얼 사건이 남아 있습니다.");

        // --- 복귀한 타이틀에서 START를 누르면 실제 게임으로 ---
        if (!Click("START")) { Fail("복귀한 타이틀에서 START 버튼을 찾지 못했습니다."); Report(); yield break; }
        yield return WaitForScene("CaseSelect", 20f);
        if (Failed)
            Fail("튜토리얼 후 START는 사건 선택으로 가야 합니다 (현재 씬: " + SceneManager.GetActiveScene().name + ").");
        else
            Step("복귀 후 START → 사건 선택 (튜토리얼 반복되지 않음)");

        Report();
    }

    // 현재 비트의 target을 호출해 RectTransform을 얻고, 거기 붙은 Button을 누른다.
    IEnumerator DriveCurrentBeat(TutorialController tutorial, IList beats)
    {
        int i = (int)Private(tutorial, "index");
        if (beats == null || i >= beats.Count) yield break;

        var beat = beats[i];
        var targetFunc = Private(beat, "target");
        var isDone = Private(beat, "isDone");

        if (targetFunc == null)
        {
            // 텍스트 전용 비트 → '확인'
            if (isDone == null)
            {
                yield return WaitUntil(() => FindButton("확인") != null, 10f,
                    "[" + i + "] '확인' 버튼이 나타나지 않았습니다.");
                if (Failed) yield break;
                FindButton("확인").onClick.Invoke();
                yield return null;
                yield break;
            }

            // 하이라이트 없이 조건만 기다리는 비트 → 지금은 '판결 빈칸 채우고 제출' 단계다.
            yield return DriveVerdictSubmit(i, isDone);
            yield break;
        }

        RectTransform target = null;
        yield return WaitUntil(() =>
        {
            target = Invoke(targetFunc) as RectTransform;
            return target != null && target.gameObject.activeInHierarchy;
        }, 20f, "[" + i + "] 안내 대상이 화면에 나타나지 않았습니다: " + Flatten((string)Private(beat, "caption")));
        if (Failed) yield break;

        var button = target.GetComponent<Button>();
        if (button == null)
        {
            Fail("[" + i + "] 안내 대상 '" + target.name + "' 에 Button이 없어 누를 수 없습니다.");
            yield break;
        }

        button.onClick.Invoke();

        int startIndex = i;
        yield return WaitUntil(() =>
        {
            AdvanceDialogueIfSpeaking();
            if (tutorial == null || tutorial.IsFinished) return true;
            return (bool)Invoke(isDone) || (int)Private(tutorial, "index") != startIndex;
        }, 25f, "[" + i + "] '" + target.name + "' 을 눌렀지만 다음 비트로 넘어가지 않았습니다.");
    }

    // 판결 패널의 (항목, 입력칸) 짝을 읽어 각 칸에 그 항목의 정답을 넣고 제출한다.
    IEnumerator DriveVerdictSubmit(int i, object isDone)
    {
        VerdictController verdict = null;
        yield return WaitUntil(() =>
        {
            verdict = Object.FindFirstObjectByType<VerdictController>();
            return verdict != null && verdict.IsOpen;
        }, 15f, "[" + i + "] 판결 패널이 열려 있지 않습니다.");
        if (Failed) yield break;

        var pairs = Private(verdict, "pairs") as IEnumerable;
        if (pairs == null) { Fail("[" + i + "] 판결 입력칸을 읽지 못했습니다."); yield break; }

        int filled = 0;
        foreach (var pair in pairs)
        {
            var t = pair.GetType();
            var field = t.GetField("Item1").GetValue(pair) as CulpritDetection.CaseField;
            var input = t.GetField("Item2").GetValue(pair) as InputField;
            if (field == null || input == null) continue;
            input.text = field.answer ?? "";
            filled++;
        }

        if (filled == 0) { Fail("[" + i + "] 판결에 채울 빈칸이 없습니다 — 튜토리얼 사건에 판정 항목이 없습니다."); yield break; }
        Step("판결: 빈칸 " + filled + "칸을 정답으로 입력");

        var submit = FindButton("제출");
        if (submit == null) { Fail("[" + i + "] '제출' 버튼을 찾지 못했습니다."); yield break; }
        submit.onClick.Invoke();

        yield return WaitUntil(() => (bool)Invoke(isDone), 15f,
            "[" + i + "] 제출했지만 판정이 완료되지 않았습니다.");
    }

    // 대사가 출력 중이면 대사창을 클릭해 넘긴다(튜토리얼은 대사 중 화면을 막지 않는다).
    static void AdvanceDialogueIfSpeaking()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.State != DialogueState.Speaking) return;

        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (b.name == "DialoguePanel" || b.name == "DialogPanel")
            {
                b.onClick.Invoke();
                return;
            }
    }

    // ------------------------------------------------------------------
    static Button FindButton(string label)
    {
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var t = b.GetComponentInChildren<Text>(true);
            if (t != null && t.text != null && t.text.Trim() == label) return b;

            var tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null && tmp.text != null && tmp.text.Trim() == label) return b;
        }
        return null;
    }

    bool Click(string label)
    {
        var b = FindButton(label);
        if (b == null) return false;
        b.onClick.Invoke();
        return true;
    }

    static string Describe(object plan)
    {
        string s1 = SuspectName(Private(plan, "firstSuspect"));
        string s2 = SuspectName(Private(plan, "secondSuspect"));
        return s1 + " + " + s2 + " → " + Private(Private(plan, "contradiction"), "id");
    }

    static string SuspectName(object suspect) =>
        suspect == null ? "?" : (string)Private(suspect, "suspectName");

    static object Private(object target, string name)
    {
        if (target == null) return null;
        var t = target.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var f = t.GetField(name, flags);
        if (f != null) return f.GetValue(target);
        var p = t.GetProperty(name, flags);
        return p != null ? p.GetValue(target) : null;
    }

    static object Invoke(object del) => ((System.Delegate)del).DynamicInvoke();

    static string Flatten(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace('\n', ' ').Replace('\r', ' ').Trim();

    bool Failed => failures.Count > 0;

    IEnumerator WaitForScene(string sceneName, float timeout)
    {
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == sceneName, timeout,
            "'" + sceneName + "' 씬으로 넘어가지 않았습니다.");
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

    void Step(string s) => steps.Add(s);

    void Fail(string s) => failures.Add(s);

    void Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("===== 튜토리얼 플레이 검증 =====");
        foreach (var s in steps) sb.AppendLine("  · " + s);
        foreach (var f in failures) sb.AppendLine("  ✗ " + f);
        sb.AppendLine(failures.Count == 0
            ? "결과: 통과 — 튜토리얼이 끝까지 진행되고 타이틀로 복귀했습니다."
            : "결과: 실패 (" + failures.Count + "건)");

        var text = sb.ToString();
        Debug.Log(text);
        System.Console.WriteLine(text);

        if (RestoreEditorSettings != null) RestoreEditorSettings();

        if (ExitProcessWhenDone) EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
