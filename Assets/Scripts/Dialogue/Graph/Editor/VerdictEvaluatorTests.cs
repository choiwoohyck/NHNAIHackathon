using System.Collections.Generic;
using CulpritDetection;
using UnityEditor;
using UnityEngine;

// 최종 판결 판정(VerdictEvaluator) 회귀 테스트.
//
// 핵심은 '빈칸 순서'와 '채점 순서'가 다를 때다. 화면의 빈칸은 verdictTemplate이 쓴 순서대로
// 만들어지고, 채점은 verdictFields 순서로 돈다. 예전에는 값만 순서대로 넘겨서 문장이 필드 순서와
// 다르게 쓰이면 다른 필드의 정답과 대조했다(전부 맞혀도 증거 불충분). 아래 첫 테스트가 그 회귀다.
//
// 실행: 메뉴 Tools ▸ Editor0 ▸ 판결 판정 테스트
//       또는 배치모드 -executeMethod VerdictEvaluatorTests.RunFromCommandLine (실패 시 종료코드 1)
public static class VerdictEvaluatorTests
{
    [MenuItem("Tools/Editor0/판결 판정 테스트")]
    public static void RunFromMenu()
    {
        var report = Run();
        Debug.Log(report);
    }

    public static void RunFromCommandLine()
    {
        var report = Run();
        Debug.Log(report);
        System.Console.WriteLine(report);
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static int failures;
    static System.Text.StringBuilder log;

    static string Run()
    {
        failures = 0;
        log = new System.Text.StringBuilder();
        log.AppendLine("===== 판결 판정 테스트 =====");

        TemplateOrderDiffersFromFieldOrder();
        MissingAnswerDoesNotShiftTheRest();
        SameFieldAppearsTwiceInTemplate();
        WrongSuspectFailsEvenWithPerfectAnswers();
        OneWrongFieldGivesInsufficientEvidence();
        MinimumCorrectFieldsAllowsPartialAnswers();
        SynonymsAndTyposStillResolve();

        log.AppendLine(failures == 0 ? "결과: 통과" : "결과: 실패 (" + failures + "건)");
        return log.ToString();
    }

    // ------------------------------------------------------------------
    // 회귀: 문장이 필드 선언 순서와 다른 순서로 빈칸을 놓는다.
    // 필드 선언은 동기 → 대상 → 수단, 문장은 대상 → 수단 → 동기.
    // 전부 정답을 넣었으니 반드시 Success 여야 한다.
    // (예전 코드는 값만 순서대로 넘겨서 '대상'의 답을 '동기'와 대조 → 0/3 증거 불충분이었다.)
    // ------------------------------------------------------------------
    static void TemplateOrderDiffersFromFieldOrder()
    {
        var graph = MakeGraph();
        var answers = AnswersInTemplateOrder(graph, "대상", "수단", "동기");

        int correct, total;
        var result = VerdictEvaluator.Evaluate(graph, "A", answers, out correct, out total);

        Check("문장 순서가 필드 순서와 달라도 정답이면 유죄",
              result == VerdictResult.Success && correct == 3 && total == 3,
              result + " (" + correct + "/" + total + ")");
    }

    // 답이 하나 빠져도 나머지가 밀려서 오답이 되면 안 된다.
    static void MissingAnswerDoesNotShiftTheRest()
    {
        var graph = MakeGraph();
        var fields = graph.verdictFields;

        // '동기'는 아예 넘기지 않는다. 나머지 둘은 정답.
        var answers = new List<KeyValuePair<CaseField, string>>
        {
            Pair(fields, "수단", "해킹"),
            Pair(fields, "대상", "국가 DB"),
        };

        int correct, total;
        var result = VerdictEvaluator.Evaluate(graph, "A", answers, out correct, out total);

        Check("답이 빠진 필드만 오답 처리되고 나머지는 밀리지 않는다",
              result == VerdictResult.InsufficientEvidence && correct == 2 && total == 3,
              result + " (" + correct + "/" + total + ")");
    }

    // 같은 필드가 문장에 두 번 나오면 빈칸도 두 개다. 먼저 채운 값을 답으로 본다.
    static void SameFieldAppearsTwiceInTemplate()
    {
        var graph = MakeGraph();
        var fields = graph.verdictFields;

        var answers = new List<KeyValuePair<CaseField, string>>
        {
            Pair(fields, "동기", "돈"),        // 정답
            Pair(fields, "동기", "복수"),      // 같은 필드의 두 번째 칸 (오답)
            Pair(fields, "대상", "국가 DB"),
            Pair(fields, "수단", "해킹"),
        };

        int correct, total;
        var result = VerdictEvaluator.Evaluate(graph, "A", answers, out correct, out total);

        Check("같은 필드가 두 번 나오면 먼저 채운 값을 쓴다",
              result == VerdictResult.Success && correct == 3 && total == 3,
              result + " (" + correct + "/" + total + ")");
    }

    static void WrongSuspectFailsEvenWithPerfectAnswers()
    {
        var graph = MakeGraph();
        var answers = AnswersInTemplateOrder(graph, "수단", "동기", "대상");

        int correct, total;
        var result = VerdictEvaluator.Evaluate(graph, "B", answers, out correct, out total);

        Check("오인 지목은 사건 설명과 무관하게 무죄",
              result == VerdictResult.WrongSuspect && correct == 3,
              result + " (" + correct + "/" + total + ")");
    }

    static void OneWrongFieldGivesInsufficientEvidence()
    {
        var graph = MakeGraph();
        var fields = graph.verdictFields;

        var answers = new List<KeyValuePair<CaseField, string>>
        {
            Pair(fields, "수단", "해킹"),
            Pair(fields, "동기", "복수"),      // 오답 (정답은 '돈')
            Pair(fields, "대상", "국가 DB"),
        };

        int correct, total;
        var result = VerdictEvaluator.Evaluate(graph, "A", answers, out correct, out total);

        Check("범인은 맞고 설명이 틀리면 증거 불충분",
              result == VerdictResult.InsufficientEvidence && correct == 2 && total == 3,
              result + " (" + correct + "/" + total + ")");
    }

    static void MinimumCorrectFieldsAllowsPartialAnswers()
    {
        var graph = MakeGraph();
        graph.minimumCorrectFields = 2;
        var fields = graph.verdictFields;

        var answers = new List<KeyValuePair<CaseField, string>>
        {
            Pair(fields, "수단", "해킹"),
            Pair(fields, "동기", "복수"),      // 오답
            Pair(fields, "대상", "국가 DB"),
        };

        int correct, total;
        var result = VerdictEvaluator.Evaluate(graph, "A", answers, out correct, out total);

        Check("최소 정답 수를 넘기면 유죄",
              result == VerdictResult.Success && correct == 2,
              result + " (" + correct + "/" + total + ")");
    }

    // 순서를 맞춰 넘기는 과정에서 동의어·오타 허용이 죽지 않았는지.
    static void SynonymsAndTyposStillResolve()
    {
        var graph = MakeGraph();
        var fields = graph.verdictFields;

        var answers = new List<KeyValuePair<CaseField, string>>
        {
            Pair(fields, "수단", "크래킹"),    // '해킹'의 동의어
            Pair(fields, "동기", "금전"),      // '돈'의 동의어
            Pair(fields, "대상", "국가DB"),    // 띄어쓰기만 다름
        };

        int correct, total;
        var result = VerdictEvaluator.Evaluate(graph, "A", answers, out correct, out total);

        Check("동의어·오타 허용이 유지된다",
              result == VerdictResult.Success && correct == 3,
              result + " (" + correct + "/" + total + ")");
    }

    // ------------------------------------------------------------------
    // 헬퍼
    // ------------------------------------------------------------------
    static CaseGraph MakeGraph()
    {
        return new CaseGraph
        {
            caseId = "TEST",
            culpritSuspectId = "A",
            verdictTypoThreshold = 0.8f,
            minimumCorrectFields = 0,
            // 선언 순서: 동기 → 대상 → 수단
            verdictFields = new List<CaseField>
            {
                new CaseField("동기", "돈").AddOption("돈", "금전", "재산").AddOption("복수", "원한"),
                new CaseField("대상", "국가 DB").AddOption("국가 DB", "국가DB", "전산망").AddOption("은행"),
                new CaseField("수단", "해킹").AddOption("해킹", "크래킹").AddOption("내부 유출"),
            }
        };
    }

    // VerdictController가 만드는 짝 목록을 흉내낸다 — 인자로 준 label 순서가 곧 문장 속 빈칸 순서.
    static List<KeyValuePair<CaseField, string>> AnswersInTemplateOrder(CaseGraph graph, params string[] labelsInTemplateOrder)
    {
        var answers = new List<KeyValuePair<CaseField, string>>();
        foreach (var label in labelsInTemplateOrder)
        {
            var field = Find(graph.verdictFields, label);
            answers.Add(new KeyValuePair<CaseField, string>(field, field.answer));   // 전부 정답으로 채움
        }
        return answers;
    }

    static KeyValuePair<CaseField, string> Pair(List<CaseField> fields, string label, string input) =>
        new KeyValuePair<CaseField, string>(Find(fields, label), input);

    static CaseField Find(List<CaseField> fields, string label)
    {
        foreach (var f in fields) if (f.label == label) return f;
        throw new System.ArgumentException("테스트 데이터에 '" + label + "' 필드가 없습니다.");
    }

    static void Check(string name, bool passed, string detail)
    {
        if (passed) log.AppendLine("  ✓ " + name);
        else
        {
            failures++;
            log.AppendLine("  ✗ " + name + " — 실제: " + detail);
        }
    }
}
