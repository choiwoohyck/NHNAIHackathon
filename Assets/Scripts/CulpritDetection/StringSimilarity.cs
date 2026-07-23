using System;

namespace CulpritDetection
{
    /// <summary>
    /// 문자열 표면(글자 모양) 기반 유사도. WebGL 안전(순수 C#, 외부 의존성 없음).
    /// - Levenshtein 편집거리로 오타를 흡수
    /// - 한국어는 자모(초성/중성/종성)로 분해해 비교하면 오타에 더 강함
    /// </summary>
    public static class StringSimilarity
    {
        /// <summary>두 문자열의 Levenshtein 편집거리(삽입/삭제/치환 최소 횟수).</summary>
        public static int Levenshtein(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? 0 : b.Length;
            if (string.IsNullOrEmpty(b)) return a.Length;

            // 메모리 절약: 2행만 사용
            int[] prev = new int[b.Length + 1];
            int[] curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }
                var tmp = prev; prev = curr; curr = tmp;
            }
            return prev[b.Length];
        }

        /// <summary>0~1 정규화 유사도(1 = 동일). 자모 분해 후 편집거리로 계산.</summary>
        public static float Similarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1f;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0f;

            string da = Hangul.DecomposeToJamo(a);
            string db = Hangul.DecomposeToJamo(b);
            int dist = Levenshtein(da, db);
            int max = Math.Max(da.Length, db.Length);
            return max == 0 ? 1f : 1f - (float)dist / max;
        }
    }

    /// <summary>한글 자모 분해 유틸.</summary>
    public static class Hangul
    {
        private const int Base = 0xAC00;   // '가'
        private const int Last = 0xD7A3;   // '힣'
        private static readonly char[] Cho = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ".ToCharArray();
        private static readonly char[] Jung = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ".ToCharArray();
        private static readonly char[] Jong = " ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ".ToCharArray();

        /// <summary>완성형 한글을 초/중/종성 낱자 문자열로 분해. 한글이 아니면 그대로 둔다.</summary>
        public static string DecomposeToJamo(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length * 3);
            foreach (char c in text)
            {
                if (c >= Base && c <= Last)
                {
                    int idx = c - Base;
                    int cho = idx / (21 * 28);
                    int jung = (idx % (21 * 28)) / 28;
                    int jong = idx % 28;
                    sb.Append(Cho[cho]).Append(Jung[jung]);
                    if (jong != 0) sb.Append(Jong[jong]);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
