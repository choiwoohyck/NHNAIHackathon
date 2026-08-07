using System.Collections.Generic;
using CulpritDetection;

// 최종 판결 결과.
public enum VerdictResult
{
    Success,              // 범인 찾기 성공 (유죄)
    WrongSuspect,         // 오인 지목 (무죄)
    InsufficientEvidence  // 증거 불충분
}

// 최종 판결 판정 (판정 로직만 담아 UI와 분리 — CaseJudge 재사용).
//
// 제안서 11.6의 판정 순서를 그대로 따른다:
//   1) 지목 용의자가 정답 용의자와 다르면            → WrongSuspect (문장과 무관)
//   2) 정답 용의자 + 사건 설명이 기준 충족           → Success
//   3) 정답 용의자 + 사건 설명이 기준 미달           → InsufficientEvidence
//
// 사건 설명 판정은 기존 CulpritDetection의 구조화 방식(필드별 동의어·오타 허용)을 재사용한다.
public static class VerdictEvaluator
{
    /// <summary>
    /// 판결을 판정한다. 입력은 '어느 필드의 답인지'가 붙은 짝으로 받는다.
    ///
    /// 화면의 빈칸 순서는 verdictTemplate이 정하고, 채점 순서는 verdictFields가 정한다.
    /// 이 둘은 얼마든지 다를 수 있으므로, 입력을 순서만 있는 목록으로 받으면 다른 필드의
    /// 정답과 대조하게 된다. 그래서 짝으로 받아 여기서 verdictFields 순서에 맞춰 정렬한다.
    /// </summary>
    public static VerdictResult Evaluate(
        CaseGraph graph, string selectedSuspectId, IList<KeyValuePair<CaseField, string>> answers,
        out int correctFields, out int totalFields)
    {
        var fields = graph != null && graph.verdictFields != null ? graph.verdictFields : new List<CaseField>();
        var aligned = AlignToFields(fields, answers);
        var results = CaseJudge.Judge(fields, aligned, graph != null ? graph.verdictTypoThreshold : 0.8f);

        correctFields = 0;
        totalFields = results.Count;
        foreach (var r in results) if (r.Correct) correctFields++;

        // 1) 오인 지목: 문장 내용과 관계없이 실패
        if (!string.Equals(selectedSuspectId, graph != null ? graph.culpritSuspectId : null))
            return VerdictResult.WrongSuspect;

        // 2)/3) 정답 용의자 → 사건 설명 판정
        int need = (graph != null && graph.minimumCorrectFields > 0) ? graph.minimumCorrectFields : totalFields;
        bool enough = totalFields == 0 || correctFields >= need;
        return enough ? VerdictResult.Success : VerdictResult.InsufficientEvidence;
    }

    /// <summary>(필드, 입력) 짝들을 CaseJudge가 기대하는 verdictFields 순서의 배열로 옮긴다.</summary>
    static string[] AlignToFields(List<CaseField> fields, IList<KeyValuePair<CaseField, string>> answers)
    {
        var aligned = new string[fields.Count];
        if (answers == null) return aligned;

        foreach (var answer in answers)
        {
            int index = IndexOf(fields, answer.Key);
            if (index < 0) continue;   // 이 사건에 없는 필드는 채점하지 않는다

            // 한 필드가 문장에 두 번 나오면 빈칸도 두 개다 — 먼저 채워진 값을 답으로 본다.
            if (string.IsNullOrWhiteSpace(aligned[index])) aligned[index] = answer.Value;
        }
        return aligned;
    }

    // 보통은 같은 객체를 그대로 넘겨받지만, 필드를 다시 만들어 넘기는 경우를 대비해 label로도 찾는다.
    static int IndexOf(List<CaseField> fields, CaseField target)
    {
        if (target == null) return -1;

        for (int i = 0; i < fields.Count; i++)
            if (ReferenceEquals(fields[i], target)) return i;

        var label = (target.label ?? "").Trim();
        if (label.Length == 0) return -1;

        for (int i = 0; i < fields.Count; i++)
            if (fields[i] != null && (fields[i].label ?? "").Trim() == label) return i;

        return -1;
    }
}
