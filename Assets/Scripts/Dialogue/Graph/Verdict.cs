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
    public static VerdictResult Evaluate(
        CaseGraph graph, string selectedSuspectId, IList<string> fieldInputs,
        out int correctFields, out int totalFields)
    {
        var fields = graph != null && graph.verdictFields != null ? graph.verdictFields : new List<CaseField>();
        var results = CaseJudge.Judge(fields, fieldInputs, graph != null ? graph.verdictTypoThreshold : 0.8f);

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
}
