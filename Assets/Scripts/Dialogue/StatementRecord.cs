using System;

// 확보된 진술 한 문장 (기록지에 실제로 표시되는 단위)
[Serializable]
public class StatementRecord
{
    public string recordId;
    public string suspectId;
    public string suspectName;
    public string text;

    public StatementRecord(string recordId, string suspectId, string suspectName, string text)
    {
        this.recordId = recordId;
        this.suspectId = suspectId;
        this.suspectName = suspectName;
        this.text = text;
    }
}
