using System.Collections.Generic;

// 용의자 한 명의 '런타임 기록 상태' (확보한 진술 문장 모음).
// 취조를 중지해도 파괴되지 않고 InterrogationController가 들고 있다가 재호출/기록지 생성에 쓴다.
//
// 어떤 질문을 물어볼 수 있는지(해금 판정)는 이제 이 클래스가 아니라 CaseGraph + CaseProgress가
// 담당한다. 그래서 여기서는 순수하게 "기록지에 올릴 문장"만 관리한다.
public class SuspectSession
{
    public string suspectId;
    public string suspectName;
    public string occupation;
    public readonly List<StatementRecord> statements = new List<StatementRecord>();
    public bool completed;
    public bool everCalled; // 대사 로그에 "입장" / "호출" 중 어떤 문구를 쓸지 구분하는 데 쓰인다.

    public SuspectSession(string suspectId, string suspectName, string occupation)
    {
        this.suspectId = suspectId;
        this.suspectName = suspectName;
        this.occupation = occupation;
    }

    // 같은 증언이 중복 기록되지 않도록(반복 질문/재확보 대비).
    public void AddStatement(StatementRecord record)
    {
        if (record == null) return;
        if (statements.Exists(r => r.recordId == record.recordId)) return;
        statements.Add(record);
    }
}
