using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 씬 흐름(Start → CaseSelect → Main → EndingScene → CaseSelect/Start) 검증기.
//
// 각 씬을 실제로 열어 흐름 컨트롤러가 붙어 있는지, 그 컨트롤러가 가리키는 다음 씬 이름이
// Build Settings에 '활성 상태로' 등록돼 있는지 확인한다. 마지막에 Start에서 출발해
// 모든 씬에 도달할 수 있는지, 그리고 엔딩에서 되돌아오는 간선이 있어 루프가 닫히는지 본다.
//
// 실행: 메뉴 Tools ▸ Editor0 ▸ 씬 흐름 검증
//       또는 배치모드 -executeMethod SceneFlowValidator.RunFromCommandLine (실패 시 종료코드 1)
public static class SceneFlowValidator
{
    const string StartScene = "Start";
    const string CaseSelectScene = "CaseSelect";
    const string PlayScene = "Main";
    const string EndingScene = "EndingScene";

    [MenuItem("Tools/Editor0/씬 흐름 검증")]
    public static void RunFromMenu()
    {
        var report = Validate();
        Debug.Log(report.ToText());
        if (report.HasErrors)
            EditorUtility.DisplayDialog("씬 흐름 검증", "문제가 발견되었습니다. Console을 확인하세요.", "확인");
        else
            EditorUtility.DisplayDialog("씬 흐름 검증", "루프가 정상적으로 닫혀 있습니다.", "확인");
    }

    public static void RunFromCommandLine()
    {
        var report = Validate();
        Debug.Log(report.ToText());
        // 배치모드 로그는 순서가 섞일 수 있어 stdout으로도 한 번 더 찍는다.
        System.Console.WriteLine(report.ToText());
        EditorApplication.Exit(report.HasErrors ? 1 : 0);
    }

    // ------------------------------------------------------------------
    public class Report
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Info = new List<string>();

        public bool HasErrors => Errors.Count > 0;

        public string ToText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("===== 씬 흐름 검증 =====");
            foreach (var i in Info) sb.AppendLine("  ✓ " + i);
            foreach (var w in Warnings) sb.AppendLine("  ! " + w);
            foreach (var e in Errors) sb.AppendLine("  ✗ " + e);
            sb.AppendLine(HasErrors
                ? "결과: 실패 (" + Errors.Count + "개 오류, " + Warnings.Count + "개 경고)"
                : "결과: 통과 (" + Warnings.Count + "개 경고)");
            return sb.ToString();
        }
    }

    public static Report Validate()
    {
        var r = new Report();

        // 1) Build Settings — 로드 가능한(활성) 씬 목록
        var buildScenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path))
            .ToList();

        foreach (var required in new[] { StartScene, CaseSelectScene, PlayScene, EndingScene })
            if (!buildScenes.Contains(required))
                r.Errors.Add("Build Settings에 '" + required + "' 씬이 활성 상태로 없습니다.");

        if (buildScenes.Count > 0 && buildScenes[0] != StartScene)
            r.Errors.Add("Build Settings의 첫 씬이 '" + buildScenes[0] + "' 입니다 — 빌드가 타이틀이 아닌 곳에서 시작합니다.");
        else if (buildScenes.Count > 0)
            r.Info.Add("Build Settings 시작 씬: " + StartScene);

        if (r.HasErrors) return r;   // 씬을 열어볼 수 없는 상태면 여기서 멈춘다.

        // 2) 씬별 검사 — 흐름 컨트롤러와 그 전환 대상
        var edges = new List<(string from, string to, string label)>();
        var opened = EditorSceneManager.GetActiveScene().path;

        CheckTutorialCase(r);
        CheckStart(r, edges, buildScenes);
        CheckCaseSelect(r, edges, buildScenes);
        CheckPlay(r, edges, buildScenes);
        CheckEnding(r, edges, buildScenes);

        if (!string.IsNullOrEmpty(opened)) EditorSceneManager.OpenScene(opened);

        // 3) 루프 검사 — Start에서 모든 씬에 도달하는가, 엔딩에서 되돌아오는가
        CheckLoop(r, edges);

        foreach (var e in edges)
            r.Info.Add("전환: " + e.from + " --[" + e.label + "]--> " + e.to);

        return r;
    }

    // 튜토리얼 전용 사건은 사건 선택에 올라가지 않고 Resources에서 직접 로드된다.
    static void CheckTutorialCase(Report r)
    {
        var path = "Assets/Resources/" + TutorialController.CaseResourceName + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<InterrogationCase>(path);
        if (asset == null)
        {
            r.Errors.Add("튜토리얼 사건이 없습니다: " + path +
                         " (Tools ▸ Editor0 ▸ 튜토리얼 사건 생성 으로 만들 수 있습니다).");
            return;
        }

        var graph = asset.BuildGraph();
        foreach (var issue in graph.Validate())
            r.Errors.Add("튜토리얼 사건: " + issue);

        var plan = TutorialPlan.Build(graph);
        if (plan == null)
            r.Errors.Add("튜토리얼 사건에서 안내할 모순 경로를 찾지 못했습니다 — 튜토리얼이 실행되지 않습니다.");
        else if (!plan.NeedsSuspectSwitch)
            r.Errors.Add("튜토리얼 사건의 모순이 한 용의자 안에서 끝납니다 — 취조 대상을 바꾸는 핵심을 가르치지 못합니다.");
        else
            r.Info.Add("튜토리얼 사건 경로: " + plan.firstSuspect.suspectName + " + " +
                       plan.secondSuspect.suspectName + " → " + plan.contradiction.id);
    }

    // ------------------------------------------------------------------
    static void CheckStart(Report r, List<(string, string, string)> edges, List<string> buildScenes)
    {
        var scene = Open(StartScene, r);
        if (!scene.IsValid()) return;

        var controller = FindComponent<StartController>(scene);
        if (controller == null)
        {
            r.Errors.Add("Start 씬에 StartController가 없습니다 — START 버튼이 아무 동작도 하지 않습니다.");
            return;
        }
        r.Info.Add("Start 씬: StartController 부착됨 (" + controller.gameObject.name + ")");

        var so = new SerializedObject(controller);

        // START 버튼이 인스펙터에 지정돼 있거나, 라벨로 찾을 수 있어야 한다.
        var startBtn = so.FindProperty("startButton").objectReferenceValue as Button;
        if (startBtn != null)
            r.Info.Add("Start 씬: START 버튼이 인스펙터에 연결됨 (" + startBtn.gameObject.name + ")");
        else if (CountButtons(scene) > 0)
            r.Warnings.Add("Start 씬: START 버튼이 인스펙터에 비어 있습니다 — 런타임에 라벨(\"START\")로 자동 탐색합니다.");
        else
            r.Warnings.Add("Start 씬에 Button이 하나도 없습니다 — 런타임에 예비 START 버튼이 생성됩니다.");

        RequireSceneRef(r, edges, so, "caseSelectSceneName", StartScene, "START", buildScenes);
    }

    static void CheckCaseSelect(Report r, List<(string, string, string)> edges, List<string> buildScenes)
    {
        var scene = Open(CaseSelectScene, r);
        if (!scene.IsValid()) return;

        var controller = FindComponent<CaseSelectController>(scene);
        if (controller == null)
        {
            r.Errors.Add("CaseSelect 씬에 CaseSelectController가 없습니다.");
            return;
        }

        var so = new SerializedObject(controller);
        RequireSceneRef(r, edges, so, "interrogationSceneName", CaseSelectScene, "수사 시작", buildScenes);
        RequireSceneRef(r, edges, so, "titleSceneName", CaseSelectScene, "← 타이틀", buildScenes);

        // 사건 데이터 — 하나도 없으면 '수사 시작'을 누를 수 없어 흐름이 끊긴다.
        var cases = so.FindProperty("cases");
        if (cases == null || cases.arraySize == 0)
        {
            r.Errors.Add("CaseSelect 씬: 표시할 사건(Cases)이 비어 있습니다 — 수사 시작 버튼이 동작하지 않습니다.");
            return;
        }

        r.Info.Add("CaseSelect 씬: 사건 " + cases.arraySize + "개 등록됨");
        for (int i = 0; i < cases.arraySize; i++)
        {
            var asset = cases.GetArrayElementAtIndex(i).objectReferenceValue as InterrogationCase;
            if (asset == null)
            {
                r.Errors.Add("CaseSelect 씬: Cases[" + i + "] 가 비어 있습니다.");
                continue;
            }
            ValidateCase(r, asset);
        }
    }

    static void CheckPlay(Report r, List<(string, string, string)> edges, List<string> buildScenes)
    {
        var scene = Open(PlayScene, r);
        if (!scene.IsValid()) return;

        var interrogation = FindComponent<InterrogationController>(scene);
        if (interrogation == null)
            r.Errors.Add("Main 씬에 InterrogationController가 없습니다.");
        else
        {
            // 단독 실행용 기본 사건(없어도 SampleCaseGraph로 굴러가므로 경고).
            var so = new SerializedObject(interrogation);
            if (so.FindProperty("caseAsset").objectReferenceValue == null)
                r.Warnings.Add("Main 씬: 기본 caseAsset이 비어 있습니다 — 씬 단독 실행 시 코드 샘플 사건으로 대체됩니다.");
            else
                r.Info.Add("Main 씬: InterrogationController 부착됨 (기본 사건 지정됨)");
        }

        var verdict = FindComponent<VerdictController>(scene);
        if (verdict == null)
        {
            // InterrogationController가 Awake에서 자동으로 붙이므로 치명적이진 않다.
            r.Warnings.Add("Main 씬: VerdictController가 씬에 없습니다 — 런타임에 자동 추가됩니다. 엔딩 씬 이름은 기본값('" + EndingScene + "')이 쓰입니다.");
            if (buildScenes.Contains(EndingScene))
                edges.Add((PlayScene, EndingScene, "판결 제출(기본값)"));
            else
                r.Errors.Add("Main → EndingScene 전환 대상 '" + EndingScene + "' 이(가) Build Settings에 없습니다.");
            return;
        }

        RequireSceneRef(r, edges, new SerializedObject(verdict), "endingSceneName", PlayScene, "판결 제출", buildScenes);
    }

    static void CheckEnding(Report r, List<(string, string, string)> edges, List<string> buildScenes)
    {
        var scene = Open(EndingScene, r);
        if (!scene.IsValid()) return;

        var controller = FindComponent<EndingController>(scene);
        if (controller == null)
        {
            r.Errors.Add("EndingScene에 EndingController가 없습니다 — 결과 화면에서 빠져나올 수 없습니다.");
            return;
        }

        var so = new SerializedObject(controller);
        RequireSceneRef(r, edges, so, "caseSelectSceneName", EndingScene, "다시 수사하기", buildScenes);
        RequireSceneRef(r, edges, so, "titleSceneName", EndingScene, "처음으로", buildScenes);

        CheckCutsceneClips(r, so);
    }

    // 엔딩 컷씬 파일이 StreamingAssets에 실제로 있는지 확인한다.
    // 없으면 그 컷은 런타임에 조용히 빠지므로, 여기서 잡아주지 않으면 눈치채기 어렵다.
    static void CheckCutsceneClips(Report r, SerializedObject so)
    {
        if (!so.FindProperty("playCutscene").boolValue)
        {
            r.Warnings.Add("EndingScene: 엔딩 컷씬이 꺼져 있습니다(playCutscene = false).");
            return;
        }

        var wanted = new List<(string label, string file)>
        {
            ("지목", so.FindProperty("accuseClip").stringValue),
            ("판결", so.FindProperty("verdictClip").stringValue),
            ("유죄 머그샷", so.FindProperty("guiltyMugshotClip").stringValue),
            ("유죄 사건기록", so.FindProperty("guiltyCaseClip").stringValue),
        };

        var innocent = so.FindProperty("innocentClips");
        for (int i = 0; i < innocent.arraySize; i++)
        {
            var entry = innocent.GetArrayElementAtIndex(i);
            var id = entry.FindPropertyRelative("suspectId").stringValue;
            wanted.Add(("무죄(" + id + ")", entry.FindPropertyRelative("fileName").stringValue));
        }

        int found = 0;
        foreach (var (label, file) in wanted)
        {
            if (string.IsNullOrEmpty(file))
            {
                r.Warnings.Add("엔딩 컷씬 '" + label + "' 파일명이 비어 있습니다.");
                continue;
            }
            if (EndingController.ClipExists(file)) { found++; continue; }
            r.Errors.Add("엔딩 컷씬 파일이 StreamingAssets에 없습니다: " + file + " (" + label + ")");
        }

        if (found > 0) r.Info.Add("엔딩 컷씬 영상 " + found + "개 확인됨");
    }

    // ------------------------------------------------------------------
    static void ValidateCase(Report r, InterrogationCase asset)
    {
        var graph = asset.BuildGraph();
        var issues = graph.Validate();
        foreach (var issue in issues)
            r.Errors.Add("사건 '" + asset.name + "': " + issue);

        if (graph.Suspects.Count == 0)
            r.Errors.Add("사건 '" + asset.name + "': 용의자가 없습니다.");

        if (string.IsNullOrEmpty(asset.culpritSuspectId))
            r.Errors.Add("사건 '" + asset.name + "': 정답 용의자(culpritSuspectId)가 비어 있습니다 — 판결이 항상 실패합니다.");
        else if (graph.GetSuspect(asset.culpritSuspectId) == null)
            r.Errors.Add("사건 '" + asset.name + "': 정답 용의자 '" + asset.culpritSuspectId + "' 를 용의자 목록에서 찾을 수 없습니다.");

        if (asset.verdictFields == null || asset.verdictFields.Count == 0)
            r.Warnings.Add("사건 '" + asset.name + "': 판결 필드(사건 설명)가 없습니다 — 용의자 지목만으로 판정됩니다.");

        // 튜토리얼은 사건 그래프에서 '한 번의 모순 성립까지의 경로'를 뽑아 안내한다.
        // 경로를 못 뽑으면 이 사건에서는 튜토리얼이 조용히 꺼진다 — 치명적이진 않지만 알아야 한다.
        var plan = TutorialPlan.Build(graph);
        if (plan == null)
            r.Warnings.Add("사건 '" + asset.name + "': 튜토리얼이 안내할 모순 경로를 찾지 못했습니다 " +
                           "(선행 조건 없는 질문 두 개로 모순 증언을 모을 수 있어야 합니다).");
        else
            r.Info.Add("사건 '" + asset.name + "' 튜토리얼 경로: " +
                       plan.firstSuspect.suspectName + " + " + plan.secondSuspect.suspectName +
                       " → " + plan.contradiction.id +
                       (plan.NeedsSuspectSwitch ? "" : "  (같은 용의자 — 취조 전환을 못 가르침)"));

        if (issues.Count == 0)
            r.Info.Add("사건 '" + asset.name + "' (" + asset.caseTitle + "): 그래프 검증 통과, 용의자 " + graph.Suspects.Count + "명");
    }

    // 컨트롤러의 씬 이름 필드가 비어 있지 않고 Build Settings에 활성으로 있는지 확인하고, 간선으로 기록한다.
    static void RequireSceneRef(Report r, List<(string, string, string)> edges, SerializedObject so,
                                string propertyName, string fromScene, string label, List<string> buildScenes)
    {
        var prop = so.FindProperty(propertyName);
        if (prop == null)
        {
            r.Errors.Add(fromScene + ": '" + propertyName + "' 필드를 찾을 수 없습니다(스크립트가 바뀌었나요?).");
            return;
        }

        var target = prop.stringValue;
        if (string.IsNullOrEmpty(target))
        {
            r.Errors.Add(fromScene + " ▸ " + label + ": 대상 씬 이름이 비어 있습니다.");
            return;
        }

        if (!buildScenes.Contains(target))
        {
            r.Errors.Add(fromScene + " ▸ " + label + ": 대상 씬 '" + target + "' 이(가) Build Settings에 활성 상태로 없습니다.");
            return;
        }

        edges.Add((fromScene, target, label));
    }

    static void CheckLoop(Report r, List<(string from, string to, string label)> edges)
    {
        // Start에서 출발해 도달 가능한 씬
        var reachable = new HashSet<string> { StartScene };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var e in edges)
                if (reachable.Contains(e.from) && reachable.Add(e.to)) grew = true;
        }

        foreach (var s in new[] { CaseSelectScene, PlayScene, EndingScene })
            if (!reachable.Contains(s))
                r.Errors.Add("루프 끊김: Start에서 '" + s + "' 씬에 도달할 수 없습니다.");

        // 되돌아오는 간선 — 엔딩에서 나가지 못하면 한 판만 하고 갇힌다.
        bool endingToCaseSelect = edges.Any(e => e.from == EndingScene && e.to == CaseSelectScene);
        bool endingToStart = edges.Any(e => e.from == EndingScene && e.to == StartScene);

        if (!endingToCaseSelect && !endingToStart)
            r.Errors.Add("루프 끊김: EndingScene에서 되돌아가는 경로가 없습니다.");
        else
        {
            if (endingToCaseSelect) r.Info.Add("루프 닫힘: EndingScene → CaseSelect (다시 수사하기)");
            if (endingToStart) r.Info.Add("루프 닫힘: EndingScene → Start (처음으로)");
        }

        if (edges.Any(e => e.from == CaseSelectScene && e.to == StartScene))
            r.Info.Add("루프 닫힘: CaseSelect → Start (← 타이틀)");

        if (reachable.Contains(EndingScene) && (endingToCaseSelect || endingToStart))
            r.Info.Add("전체 순환 확인: Start → CaseSelect → Main → EndingScene → (CaseSelect/Start)");
    }

    // ------------------------------------------------------------------
    static Scene Open(string sceneName, Report r)
    {
        var entry = EditorBuildSettings.scenes.FirstOrDefault(
            s => s.enabled && System.IO.Path.GetFileNameWithoutExtension(s.path) == sceneName);
        if (entry == null)
        {
            r.Errors.Add("'" + sceneName + "' 씬을 Build Settings에서 찾을 수 없습니다.");
            return default;
        }
        return EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
    }

    static T FindComponent<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }

    static int CountButtons(Scene scene)
    {
        int n = 0;
        foreach (var root in scene.GetRootGameObjects())
            n += root.GetComponentsInChildren<Button>(true).Length;
        return n;
    }
}
