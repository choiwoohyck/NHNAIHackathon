using System;
using System.Collections.Generic;
using UnityEngine;

// 용의자 한 명의 '정의' 데이터(취조 전에 미리 작성하는 authored 데이터).
// 런타임 진행 상태(확보한 기록 등)는 SuspectSession / CaseProgress가 따로 들고 있다.
[Serializable]
public class SuspectData
{
    public string id;
    public string suspectName;
    public string occupation;
    [Tooltip("전화로 이 용의자를 호출했을 때 InterrogationLightFlicker.PlayCharacterCall에 넘길 인물 이미지.")]
    public Sprite portrait;
    [Tooltip("체크하면 아래 가로/세로 값으로 Portrait 이미지 크기를 강제 지정한다. 체크 해제 시 기존 UI에 설정된 기본 크기를 그대로 사용한다.")]
    public bool useCustomPortraitSize = false;
    [Tooltip("Portrait 이미지의 가로/세로 크기(px). useCustomPortraitSize가 켜져 있을 때만 적용된다.")]
    public Vector2 portraitSize = new Vector2(400f, 600f);
    public List<QuestionNode> questions = new List<QuestionNode>();

    public SuspectData() { }

    public SuspectData(string id, string suspectName, string occupation)
    {
        this.id = id;
        this.suspectName = suspectName;
        this.occupation = occupation;
    }

    public SuspectData Add(params QuestionNode[] nodes)
    {
        if (nodes != null) questions.AddRange(nodes);
        return this;
    }
}
