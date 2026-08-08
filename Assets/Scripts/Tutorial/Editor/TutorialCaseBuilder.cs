using System.Collections.Generic;
using CulpritDetection;
using UnityEditor;
using UnityEngine;

// 튜토리얼 전용 사건을 Assets/Resources/TutorialCase.asset 으로 만들어 둔다.
//
// 실제 사건과 섞이지 않도록 사건 선택 화면에는 올리지 않고, StartController가 Resources에서 바로 읽어
// 취조실로 직행한다. 그래서 이 파일은 반드시 Resources 폴더 아래에 있어야 한다.
//
// 내용은 '모순 한 번'만 가르치면 되므로 최소로 짰다:
//   용의자 2명 · 질문 2개 · 모순 1개. 판결까지는 가지 않으므로 판정 필드는 두지 않는다.
//
// 실행: Tools ▸ Editor0 ▸ 튜토리얼 사건 생성
//       배치모드 -executeMethod TutorialCaseBuilder.RunFromCommandLine
public static class TutorialCaseBuilder
{
    const string AssetPath = "Assets/Resources/" + TutorialController.CaseResourceName + ".asset";

    [MenuItem("Tools/Editor0/튜토리얼 사건 생성")]
    public static void RunFromMenu()
    {
        var asset = Create();
        EditorGUIUtility.PingObject(asset);
        Debug.Log("[TutorialCaseBuilder] " + AssetPath + " 생성 완료");
    }

    public static void RunFromCommandLine()
    {
        Create();
        Debug.Log("[TutorialCaseBuilder] " + AssetPath + " 생성 완료");
        System.Console.WriteLine("tutorial case written: " + AssetPath);
        EditorApplication.Exit(0);
    }

    public static InterrogationCase Create()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        var c = ScriptableObject.CreateInstance<InterrogationCase>();

        c.caseId = "TUTORIAL";
        c.caseNumber = "CASE-000";
        c.caseTitle = "훈련 과제 · 자료실 침입";
        c.briefing =
            "훈련용 과제다. 어젯밤 누군가 자료실에 들어갔다.\n"
            + "두 사람에게 같은 밤을 묻고, 말이 어긋나는 자리를 찾아라.";

        // --- 증언 ---
        c.testimonies = new List<Testimony>
        {
            new Testimony("TUT_A_ALIBI", "A", "박 주임",
                "박 주임은 어젯밤 자료실 근처에 간 적이 없다고 진술함."),
            new Testimony("TUT_B_SAW", "B", "이 사원",
                "이 사원은 어젯밤 박 주임이 자료실에서 나오는 것을 봤다고 진술함."),
            new Testimony("TUT_A_ADMIT", "A", "박 주임",
                "추궁당하자 박 주임은 어젯밤 자료실에 들어갔음을 인정함."),
        };

        // --- 용의자 A: 박 주임 ---
        var a = new SuspectData("A", "박 주임", "자료실 담당");
        a.Add(
            new QuestionNode("TUT_A_WHERE", "어젯밤 어디에 있었습니까?")
                .Say("월터", "어젯밤 자료실. 근처에 갔었나?")
                .Say("박 주임", "아니요. 퇴근하고 곧장 집으로 갔습니다.")
                .Grant("TUT_A_ALIBI"),

            new QuestionNode("TUT_A_CONTRA", "자료실에서 나오는 걸 본 사람이 있는데요?")
                .NeedSelected("TUT_A_ALIBI", "TUT_B_SAW")
                .Say("월터", "곧장 집으로 갔다고 했지. 그런데 자네가 자료실에서 나오는 걸 본 사람이 있어.")
                .Say("박 주임", "…두고 온 게 있어서 잠깐 들렀습니다. 들어간 건 맞습니다.")
                .Say("월터", "잠깐이라. 그 잠깐을 아주 자세히 들어봐야겠군.")
                .Grant("TUT_A_ADMIT")
        );

        // --- 용의자 B: 이 사원 ---
        var b = new SuspectData("B", "이 사원", "야간 경비");
        b.Add(
            new QuestionNode("TUT_B_NIGHT", "어젯밤 자료실 쪽에서 누굴 봤습니까?")
                .Say("월터", "어젯밤 자료실 쪽. 누구 봤나?")
                .Say("이 사원", "박 주임님이요. 열 시쯤에 자료실에서 나오시더라고요.")
                .Grant("TUT_B_SAW")
        );

        c.suspects = new List<SuspectData> { a, b };

        // --- 판결 ---
        // 훈련 과제이므로 빈칸 두 개만 둔다. 두 답 모두 플레이어가 방금 기록지에서 읽은 진술에
        // 그대로 적혀 있어야 한다("답은 증언 안에 있다"를 몸으로 익히게 하는 게 목적).
        c.culpritSuspectId = "A";
        c.verdictTemplate = "{범인}은(는) {시각}쯤 {장소}에 들어갔다.";
        c.verdictFields = new List<CaseField>
        {
            new CaseField("시각", "열 시")
                .AddOption("열 시", "10시", "열시", "밤 열 시", "밤 10시", "22시")
                .AddOption("새벽 세 시", "3시", "새벽 3시")
                .AddOption("점심시간", "정오"),

            new CaseField("장소", "자료실")
                .AddOption("자료실")
                .AddOption("탕비실")
                .AddOption("서버실"),
        };

        var existing = AssetDatabase.LoadAssetAtPath<InterrogationCase>(AssetPath);
        if (existing != null) AssetDatabase.DeleteAsset(AssetPath);

        AssetDatabase.CreateAsset(c, AssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return c;
    }
}
