using System.Collections.Generic;
using UnityEngine;

namespace CulpritDetection
{
    /// <summary>
    /// 사건 정의(ScriptableObject). 범인·흉기·장소·동기·증거 등 카테고리를 데이터로 담는다.
    ///
    /// 만드는 법: Project 창에서 우클릭 → Create → CulpritDetection → Case Definition
    /// → 인스펙터에서 fields에 카테고리를 추가(각 카테고리마다 label/answer/options).
    /// 코드 수정 없이 사건을 얼마든지 확장·교체할 수 있다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCase", menuName = "CulpritDetection/Case Definition", order = 0)]
    public class CaseDefinition : ScriptableObject
    {
        public string caseTitle = "사건 파일";

        [Tooltip("동의어/오타 허용 정도(0~1). 1이면 완전 일치만 인정.")]
        [Range(0f, 1f)] public float typoThreshold = 0.8f;

        [Tooltip("빈칸 채우기 문장. {필드label} 위치에 입력칸이 생김. 예: {범인}가 {장소}에서 {흉기}를 사용해서 죽였다.\n" +
                 "줄바꿈 가능. 문장에 안 넣은 필드는 아래에 세로로 나열됨. 비우면 전부 세로 나열.")]
        [TextArea(2, 4)] public string template;

        [Tooltip("카테고리 목록. 범인/흉기/장소/범행 동기/결정적 증거/목격자 등 자유롭게 추가.")]
        public List<CaseField> fields = new List<CaseField>();

        /// <summary>필드 순서대로의 플레이어 입력을 받아 판정.</summary>
        public List<FieldResult> Judge(IList<string> inputs) => CaseJudge.Judge(fields, inputs, typoThreshold);
    }
}
