using System;
using System.Collections.Generic;

namespace CulpritDetection
{
    /// <summary>선택지 하나 (정답 표기값 + 동의어).</summary>
    [Serializable]
    public class CaseOption
    {
        public string value;                       // canonical(표시/정답 비교값)
        public List<string> synonyms = new List<string>();

        public CaseOption() { }
        public CaseOption(string value, params string[] synonyms)
        {
            this.value = value;
            if (synonyms != null) this.synonyms.AddRange(synonyms);
        }
    }

    /// <summary>
    /// 카테고리 한 개(범인/흉기/장소/범행 동기/범행 시각/목격자 …).
    /// 구조화 입력의 핵심 단위 — 각 필드는 독립적으로 판정되어 파싱 예외가 없다.
    /// </summary>
    [Serializable]
    public class CaseField
    {
        public string label;                       // "범인", "범행 동기" 등
        public string prompt;                      // (선택) 질문 문구. 비우면 label 사용
        public string answer;                      // 정답 canonical
        public List<CaseOption> options = new List<CaseOption>();

        public CaseField() { }
        public CaseField(string label, string answer) { this.label = label; this.answer = answer; }

        public string DisplayPrompt() => string.IsNullOrEmpty(prompt) ? label : prompt;

        /// <summary>선택지 추가(체이닝 가능). 첫 인자가 canonical, 나머지는 동의어.</summary>
        public CaseField AddOption(string canonical, params string[] synonyms)
        {
            options.Add(new CaseOption(canonical, synonyms));
            return this;
        }

        /// <summary>플레이어 입력을 canonical로 해석(오타 허용). 못 맞추면 null.</summary>
        public string Resolve(string input, float typoThreshold)
        {
            string n = Norm(input);
            if (n.Length == 0) return null;

            string best = null; float bestSim = 0f;
            foreach (var o in options)
            {
                if (o == null || string.IsNullOrEmpty(o.value)) continue;
                foreach (var surf in Surfaces(o))
                {
                    string s = Norm(surf);
                    if (s.Length == 0) continue;
                    float sim = n == s ? 1f : StringSimilarity.Similarity(n, s);
                    if (sim >= typoThreshold && sim > bestSim) { bestSim = sim; best = o.value; }
                }
            }
            return best;
        }

        /// <summary>입력이 정답인가.</summary>
        public bool IsCorrect(string input, float typoThreshold)
        {
            string r = Resolve(input, typoThreshold);
            return r != null && Norm(r) == Norm(answer);
        }

        public List<string> OptionValues()
        {
            var list = new List<string>();
            foreach (var o in options) if (o != null && !string.IsNullOrEmpty(o.value)) list.Add(o.value);
            return list;
        }

        private static IEnumerable<string> Surfaces(CaseOption o)
        {
            yield return o.value;
            if (o.synonyms != null) foreach (var s in o.synonyms) yield return s;
        }

        private static string Norm(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().ToLowerInvariant();
    }

    /// <summary>사건 전체 = 카테고리 목록. 판정 결과 묶음.</summary>
    public struct FieldResult
    {
        public string Label;
        public bool Correct;
        public string Answer;
        public string Resolved; // 플레이어 입력이 해석된 값(null이면 못 맞춤)
    }

    public static class CaseJudge
    {
        /// <summary>필드별 입력(label→값)을 받아 각 필드 판정.</summary>
        public static List<FieldResult> Judge(List<CaseField> fields, IList<string> inputs, float typoThreshold)
        {
            var results = new List<FieldResult>();
            for (int i = 0; i < fields.Count; i++)
            {
                string input = (inputs != null && i < inputs.Count) ? inputs[i] : "";
                string resolved = fields[i].Resolve(input, typoThreshold);
                bool ok = resolved != null &&
                          resolved.Trim().ToLowerInvariant() == (fields[i].answer ?? "").Trim().ToLowerInvariant();
                results.Add(new FieldResult
                {
                    Label = fields[i].label,
                    Correct = ok,
                    Answer = fields[i].answer,
                    Resolved = resolved
                });
            }
            return results;
        }
    }
}
