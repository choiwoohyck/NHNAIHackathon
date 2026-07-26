using System;
using System.Collections.Generic;
using UnityEngine;   // 인스펙터 드롭다운용 마커 애트리뷰트([QuestionId]/[TestimonyId]) 참조

// 취조 질문 그래프(방향 그래프)의 노드 하나.
//
// 화면상으로는 "선택지 하나"지만, 내부적으로는 여러 선행 질문/증언을 조건으로 가질 수 있는
// 그래프 노드다. 각 노드는 자신의 해금 조건(prerequisite)을 스스로 들고 있고(= pull 방식),
// 조건이 충족되면 취조 목록에 자동으로 나타난다. 트리처럼 부모가 하나로 고정되지 않으므로,
// "A의 진술 + B의 진술을 모두 확보해야 열리는 모순 질문" 같은 구조를 자연스럽게 표현한다.
//
// 런타임 상태(물어봤는지/증언을 얻었는지)는 여기 두지 않고 CaseProgress가 따로 관리한다.
// 덕분에 이 클래스는 순수 데이터라 ScriptableObject/JSON으로 그대로 직렬화할 수 있다.
public enum NodeKind
{
    Normal,        // 일반 질문
    Contradiction  // 모순 질문 (기록지에서 특정 증언을 선택해야 열림)
}

[Serializable]
public class QuestionNode
{
    public string id;
    public string label;                 // 선택지 버튼에 표시될 문구
    public NodeKind kind = NodeKind.Normal;

    public List<DialogueLine> lines = new List<DialogueLine>();  // 선택 시 순서대로 출력될 대사

    // ------------------------------------------------------------------
    // 해금 조건 (조건이 충족되면 목록에 나타난다)
    // ------------------------------------------------------------------
    [QuestionId]  public List<string> requiredQuestionIds = new List<string>();   // 선행 질문(이 질문들을 이미 물어봤어야 함)
    [TestimonyId] public List<string> requiredTestimonyIds = new List<string>();  // 선행 증언(이 증언들을 이미 확보했어야 함)
    public bool requireAll = true;                                  // true=모든 조건(AND), false=하나라도(OR)

    // 모순 질문 전용: 이 증언을 '기록지에서 선택'해야만 질문이 열린다. 비어 있으면 무시.
    [TestimonyId] public string requiredSelectedTestimonyId;

    // ------------------------------------------------------------------
    // 결과
    // ------------------------------------------------------------------
    [TestimonyId] public List<string> grantTestimonyIds = new List<string>();     // 이 질문으로 새로 확보되는 증언

    public bool repeatable = false;      // true면 여러 번 물어볼 수 있음(기본은 한 번)

    public QuestionNode() { }

    public QuestionNode(string id, string label)
    {
        this.id = id;
        this.label = label;
    }

    // ------------------------------------------------------------------
    // 코드로 데이터를 짤 때 읽기 쉽도록 하는 체이닝 빌더.
    // 예)  new QuestionNode("A_C_LEAVE", "그날 연차였다던데요?")
    //          .NeedSelected("T_B_LEAVE").NeedTestimony("T_A_ALIBI")
    //          .Say("월터", "...").Say("클로버", "...")
    //          .Grant("T_A_RECANT_LEAVE");
    // ------------------------------------------------------------------
    public QuestionNode Say(string speaker, string text)
    {
        lines.Add(new DialogueLine(speaker, text));
        return this;
    }

    public QuestionNode NeedQuestion(params string[] ids)
    {
        if (ids != null) requiredQuestionIds.AddRange(ids);
        return this;
    }

    public QuestionNode NeedTestimony(params string[] ids)
    {
        if (ids != null) requiredTestimonyIds.AddRange(ids);
        return this;
    }

    /// <summary>선행 조건을 OR(하나라도 충족)로 바꾼다. 기본은 AND.</summary>
    public QuestionNode AnyOf()
    {
        requireAll = false;
        return this;
    }

    /// <summary>기록지에서 선택해야 열리는 모순 질문으로 지정한다.</summary>
    public QuestionNode NeedSelected(string testimonyId)
    {
        requiredSelectedTestimonyId = testimonyId;
        kind = NodeKind.Contradiction;
        return this;
    }

    public QuestionNode Grant(params string[] testimonyIds)
    {
        if (testimonyIds != null) grantTestimonyIds.AddRange(testimonyIds);
        return this;
    }

    public QuestionNode Repeat()
    {
        repeatable = true;
        return this;
    }
}
