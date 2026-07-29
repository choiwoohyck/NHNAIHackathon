using System.Collections.Generic;

// 사건 진행 중의 '런타임 상태'만 담는다 (authored 데이터와 철저히 분리 — GPT 조언의 CaseState).
//
// 상태를 그래프 데이터와 분리해 두면, 저장/불러오기나 사건 리셋이 쉽고
// 그래프 데이터(QuestionNode/Testimony)는 순수 데이터로 유지할 수 있다.
//
// 취조는 여러 용의자를 오가며 진행되므로, '물어본 질문'과 '확보한 증언'은
// 용의자별이 아니라 사건 전체(전역)로 관리한다. B에게서 얻은 증언이 A의 모순 질문을 여는 구조 때문.
public class CaseProgress
{
    public readonly HashSet<string> askedQuestionIds = new HashSet<string>();
    public readonly HashSet<string> obtainedTestimonyIds = new HashSet<string>();
    public readonly HashSet<string> completedSuspectIds = new HashSet<string>();

    // 현재 기록지에서 모순 근거로 선택된 증언 id들(모순 지목은 두 문장을 선택). 최대 2개.
    public readonly List<string> selectedTestimonyIds = new List<string>();
    public const int MaxSelected = 2;

    public int SelectedCount => selectedTestimonyIds.Count;
    public bool IsSelected(string testimonyId) =>
        !string.IsNullOrEmpty(testimonyId) && selectedTestimonyIds.Contains(testimonyId);

    /// <summary>선택 토글: 이미 선택돼 있으면 해제, 아니면 추가(가득 차 있으면 먼저 비운다).</summary>
    public void ToggleSelected(string testimonyId)
    {
        if (string.IsNullOrEmpty(testimonyId)) return;
        if (selectedTestimonyIds.Contains(testimonyId)) { selectedTestimonyIds.Remove(testimonyId); return; }
        if (selectedTestimonyIds.Count >= MaxSelected) selectedTestimonyIds.Clear();
        selectedTestimonyIds.Add(testimonyId);
    }

    public void ClearSelected() => selectedTestimonyIds.Clear();

    /// <summary>주어진 id들이 모두 선택되어 있는가.</summary>
    public bool AreAllSelected(IEnumerable<string> ids)
    {
        if (ids == null) return false;
        foreach (var id in ids) if (!selectedTestimonyIds.Contains(id)) return false;
        return true;
    }

    public bool IsAsked(string questionId) =>
        !string.IsNullOrEmpty(questionId) && askedQuestionIds.Contains(questionId);

    public bool HasTestimony(string testimonyId) =>
        !string.IsNullOrEmpty(testimonyId) && obtainedTestimonyIds.Contains(testimonyId);

    public bool IsSuspectCompleted(string suspectId) =>
        !string.IsNullOrEmpty(suspectId) && completedSuspectIds.Contains(suspectId);

    public void MarkAsked(string questionId)
    {
        if (!string.IsNullOrEmpty(questionId)) askedQuestionIds.Add(questionId);
    }

    public void AddTestimony(string testimonyId)
    {
        if (!string.IsNullOrEmpty(testimonyId)) obtainedTestimonyIds.Add(testimonyId);
    }

    public void MarkSuspectCompleted(string suspectId)
    {
        if (!string.IsNullOrEmpty(suspectId)) completedSuspectIds.Add(suspectId);
    }
}
