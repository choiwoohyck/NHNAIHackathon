using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// InterrogationCase(ScriptableObject)용 커스텀 인스펙터.
//   · 상단 요약        : 용의자/질문/증언 개수
//   · 그래프 검증       : 없는 id 참조 / 순환(cycle)을 즉시 점검 (문자열 id 오타 방지)
//   · 샘플 데이터로 채우기 : 이 에셋을 시연용 사건으로 덮어씀 (학습/수정 출발점)
//
// 메뉴 Tools → Editor0 → Create Sample Interrogation Case 로 '채워진' 에셋을 새로 만들 수도 있다.
[CustomEditor(typeof(InterrogationCase))]
public class InterrogationCaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var caseAsset = (InterrogationCase)target;

        EditorGUILayout.LabelField("취조 사건 · 방향 그래프", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "용의자 " + Count(caseAsset.suspects) + "명    ·    질문 " + QuestionCount(caseAsset) +
            "개    ·    증언 " + Count(caseAsset.testimonies) + "개",
            MessageType.None);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("그래프 검증 (Validate)")) ValidateCase(caseAsset);
        if (GUILayout.Button("샘플 데이터로 채우기")) FillFromSample(caseAsset);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }

    static int Count<T>(List<T> list) => list == null ? 0 : list.Count;

    static int QuestionCount(InterrogationCase c)
    {
        int n = 0;
        if (c.suspects != null)
            foreach (var s in c.suspects)
                if (s != null && s.questions != null) n += s.questions.Count;
        return n;
    }

    static void ValidateCase(InterrogationCase caseAsset)
    {
        var issues = caseAsset.Validate();
        if (issues.Count == 0)
            EditorUtility.DisplayDialog("그래프 검증", "이상 없음 ✔\n모든 참조가 유효하고 순환(cycle)이 없습니다.", "확인");
        else
            EditorUtility.DisplayDialog("그래프 검증 — 문제 " + issues.Count + "건", string.Join("\n", issues), "확인");
    }

    static void FillFromSample(InterrogationCase caseAsset)
    {
        if (!EditorUtility.DisplayDialog("샘플 데이터로 채우기",
                "이 에셋의 내용을 시연용 샘플 사건으로 덮어씁니다. 계속할까요?", "덮어쓰기", "취소"))
            return;

        Undo.RecordObject(caseAsset, "Fill Interrogation Case With Sample");
        CopySampleInto(caseAsset);
        EditorUtility.SetDirty(caseAsset);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Editor0/Create Sample Interrogation Case")]
    static void CreateSampleAsset()
    {
        var so = CreateInstance<InterrogationCase>();
        CopySampleInto(so);

        var path = AssetDatabase.GenerateUniqueAssetPath("Assets/SampleInterrogationCase.asset");
        AssetDatabase.CreateAsset(so, path);
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = so;
        Debug.Log("[Editor0] 샘플 취조 사건 생성: " + path);
    }

    // 코드 그래프(SampleCaseGraph)의 내용을 SO 필드로 복사한다.
    // SampleCaseGraph는 Unity 비의존 순수 C#이라, SO 결합은 이 에디터 쪽에만 둔다.
    static void CopySampleInto(InterrogationCase target)
    {
        var g = SampleCaseGraph.Build();
        target.caseId = g.caseId;
        target.caseTitle = g.caseTitle;
        target.briefing = g.briefing;
        target.suspects = new List<SuspectData>(g.Suspects);
        target.testimonies = new List<Testimony>(g.Testimonies);

        // 최종 판결 데이터
        target.culpritSuspectId = g.culpritSuspectId;
        target.verdictTypoThreshold = g.verdictTypoThreshold;
        target.minimumCorrectFields = g.minimumCorrectFields;
        target.verdictTemplate = g.verdictTemplate;
        target.verdictFields = new List<CulpritDetection.CaseField>(g.verdictFields);
    }
}
