using System.Collections.Generic;

// 용의자 한 명의 취조 진행 상태 (씬에만 존재하는 런타임 데이터).
// 취조를 중지해도 파괴되지 않고 InterrogationController가 들고 있다가 재호출 시 이어서 사용한다.
public class SuspectSession
{
    public string suspectId;
    public string suspectName;
    public List<Question> questions;
    public List<StatementRecord> statements = new List<StatementRecord>();
    public bool completed;

    public SuspectSession(string suspectId, string suspectName, List<Question> questions)
    {
        this.suspectId = suspectId;
        this.suspectName = suspectName;
        this.questions = questions;
    }

    public List<Question> GetAvailableQuestions()
    {
        var result = new List<Question>();
        foreach (var q in questions)
        {
            if (q.oneTime && q.asked) continue;
            result.Add(q);
        }
        return result;
    }
}
