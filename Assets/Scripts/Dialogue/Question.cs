using System.Collections.Generic;

// 취조 질문 하나.
// 프로토타입 단계라 ScriptableObject 대신 순수 C# 클래스로 두고, InterrogationController에서
// 코드로 직접 만들어 쓴다. label 자리에 "내용1", "내용2" 같은 임시 문구를 넣어 바로 테스트 가능.
public class Question
{
    public string id;
    public string label;                       // 선택지 버튼에 표시될 문구
    public List<DialogueLine> responseLines;    // 선택 시 순서대로 출력될 대사
    public string recordText;                   // 기록지에 남길 요약 문장 (비워두면 기록되지 않음)
    public bool oneTime;                        // true면 한 번 물어보면 목록에서 사라짐
    public bool asked;

    public Question(string id, string label, string recordText = null, params DialogueLine[] lines)
    {
        this.id = id;
        this.label = label;
        this.recordText = recordText;
        responseLines = new List<DialogueLine>(lines);
        oneTime = true;
        asked = false;
    }
}
