using System;
using System.Collections.Generic;

// 증언(진술) 한 조각.
//
// 핵심 설계: '질문'과 '증언'을 분리한다. 질문을 하면 곧바로 다음 질문이 열리는 게 아니라,
// 질문의 결과로 '증언'을 확보하고, 그 증언이 다시 다른 질문의 해금 조건이 된다.
// 이렇게 하면 한 용의자의 증언을 다른 용의자에게 들이대는 모순 시스템과 그래프가 딱 맞물린다.
//
// 확보한 증언은 곧 기록지(StatementRecord)에 표시되는 문장이기도 하다.
[Serializable]
public class Testimony
{
    public string id;
    public string ownerSuspectId;      // 이 증언을 한 용의자
    public string ownerSuspectName;
    public string text;                // 기록지에 표시되는 문장(짧고 명확하게)
    public List<string> keywords = new List<string>();  // (선택) 검색/분류용 키워드

    public Testimony() { }

    public Testimony(string id, string ownerSuspectId, string ownerSuspectName, string text, params string[] keywords)
    {
        this.id = id;
        this.ownerSuspectId = ownerSuspectId;
        this.ownerSuspectName = ownerSuspectName;
        this.text = text;
        if (keywords != null) this.keywords.AddRange(keywords);
    }
}
