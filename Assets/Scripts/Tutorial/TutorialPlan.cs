using System.Collections.Generic;

// 튜토리얼이 안내할 '한 번의 모순 성립까지의 경로'를 사건 그래프에서 직접 뽑아낸다.
//
// 이 게임에서 모순은 서로 다른 용의자의 진술을 맞대야 성립하도록 authored 되어 있다.
// 그래서 튜토리얼은 특정 사건/이름을 하드코딩할 필요가 없다 — 그래프에게 물어보면 된다:
//
//   1) 모순 질문 하나를 고른다(필요 증언 2개짜리)
//   2) 그 두 증언을 각각 '지금 바로 물어볼 수 있는' 질문에서 얻을 수 있는지 확인한다
//   3) 그 질문들의 주인 = 튜토리얼이 부를 용의자
//
// 조건을 만족하는 모순이 없으면 Build가 null을 돌려주고, 튜토리얼은 조용히 실행되지 않는다.
public class TutorialPlan
{
    public QuestionNode contradiction;          // 목표 — 이게 성립하면 튜토리얼의 핵심이 전달된 것

    public SuspectData firstSuspect;            // 먼저 부를 용의자
    public QuestionNode firstQuestion;          // 그에게 물을 질문
    public Testimony firstTestimony;            // 그 질문으로 얻는 증언

    public SuspectData secondSuspect;
    public QuestionNode secondQuestion;
    public Testimony secondTestimony;

    /// <summary>두 증언의 주인이 서로 다른가(= 취조 대상을 갈아타야 하는가).</summary>
    public bool NeedsSuspectSwitch =>
        firstSuspect != null && secondSuspect != null && firstSuspect.id != secondSuspect.id;

    public static TutorialPlan Build(CaseGraph graph)
    {
        if (graph == null) return null;

        foreach (var suspect in graph.Suspects)
        {
            if (suspect.questions == null) continue;

            foreach (var node in suspect.questions)
            {
                if (node == null || node.RequiredSelectedCount() != 2) continue;

                var plan = TryBuild(graph, node);
                if (plan != null) return plan;
            }
        }
        return null;
    }

    static TutorialPlan TryBuild(CaseGraph graph, QuestionNode contradiction)
    {
        var ids = contradiction.requiredSelectedTestimonyIds;

        var first = FindFeeder(graph, ids[0]);
        var second = FindFeeder(graph, ids[1]);
        if (first == null || second == null) return null;

        // 모순 질문에 선행 조건이 걸려 있어도, 그게 '튜토리얼이 어차피 모을 것들'로 충족된다면 괜찮다.
        // (실제로 모순의 근거 증언 자체를 선행 조건으로 다시 적어둔 사건이 있다.)
        var plannedQuestions = new HashSet<string> { first.question.id, second.question.id };
        var plannedTestimonies = new HashSet<string> { ids[0], ids[1] };
        if (!PrerequisitesSatisfiedBy(contradiction, plannedQuestions, plannedTestimonies)) return null;

        var firstTestimony = graph.GetTestimony(ids[0]);
        var secondTestimony = graph.GetTestimony(ids[1]);
        if (firstTestimony == null || secondTestimony == null) return null;

        // 같은 질문 하나가 두 증언을 다 준다면 '맞대본다'는 교훈이 성립하지 않는다.
        if (first.question == second.question) return null;

        return new TutorialPlan
        {
            contradiction = contradiction,
            firstSuspect = first.suspect,
            firstQuestion = first.question,
            firstTestimony = firstTestimony,
            secondSuspect = second.suspect,
            secondQuestion = second.question,
            secondTestimony = secondTestimony,
        };
    }

    class Feeder
    {
        public SuspectData suspect;
        public QuestionNode question;
    }

    // 선행 조건 없이 바로 물어볼 수 있으면서 해당 증언을 주는 질문을 찾는다.
    static Feeder FindFeeder(CaseGraph graph, string testimonyId)
    {
        if (string.IsNullOrEmpty(testimonyId)) return null;

        foreach (var suspect in graph.Suspects)
        {
            if (suspect.questions == null) continue;

            foreach (var node in suspect.questions)
            {
                if (node == null || node.grantTestimonyIds == null) continue;
                if (!node.grantTestimonyIds.Contains(testimonyId)) continue;
                if (node.RequiredSelectedCount() > 0) continue;   // 모순 질문은 출발점이 될 수 없다
                if (HasPrerequisites(node)) continue;             // 지금 당장 물어볼 수 있어야 한다

                return new Feeder { suspect = suspect, question = node };
            }
        }
        return null;
    }

    static bool HasPrerequisites(QuestionNode node) =>
        (node.requiredQuestionIds != null && node.requiredQuestionIds.Count > 0)
        || (node.requiredTestimonyIds != null && node.requiredTestimonyIds.Count > 0);

    // 선행 조건이 주어진 질문/증언 집합만으로 충족되는가.
    // AND/OR 판정은 CaseGraph.PrerequisitesMet과 같은 규칙을 따른다.
    static bool PrerequisitesSatisfiedBy(QuestionNode node, HashSet<string> questionIds, HashSet<string> testimonyIds)
    {
        bool hasQ = node.requiredQuestionIds != null && node.requiredQuestionIds.Count > 0;
        bool hasT = node.requiredTestimonyIds != null && node.requiredTestimonyIds.Count > 0;
        if (!hasQ && !hasT) return true;

        if (node.requireAll)
        {
            if (hasQ) foreach (var id in node.requiredQuestionIds) if (!questionIds.Contains(id)) return false;
            if (hasT) foreach (var id in node.requiredTestimonyIds) if (!testimonyIds.Contains(id)) return false;
            return true;
        }

        if (hasQ) foreach (var id in node.requiredQuestionIds) if (questionIds.Contains(id)) return true;
        if (hasT) foreach (var id in node.requiredTestimonyIds) if (testimonyIds.Contains(id)) return true;
        return false;
    }
}
