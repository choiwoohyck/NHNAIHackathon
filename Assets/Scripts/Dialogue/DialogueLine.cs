using System;

// 대사 한 줄 (화자 + 텍스트)
[Serializable]
public struct DialogueLine
{
    public string speaker;
    public string text;

    public DialogueLine(string speaker, string text)
    {
        this.speaker = speaker;
        this.text = text;
    }
}
