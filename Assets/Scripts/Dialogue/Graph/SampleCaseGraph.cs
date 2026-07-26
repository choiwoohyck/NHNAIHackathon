// 시연용 사건 "아슬라니아 국가 전산망 침입 사건"을 코드로 구성한다.
//
// 제안서의 3대 모순을 방향 그래프로 그대로 표현했다.
//   모순 1) 출근 여부  : 동료(T_B_LEAVE) ↔ 클로버 알리바이(T_A_ALIBI)
//   모순 2) 관리자 권한: 보안(T_C_ADMIN) ↔ 클로버 권한없음(T_A_NOAUTH)
//   모순 3) 미공개 정보: 관리자 권한 번복 이후 열리는 지식 모순(A_KNOWLEDGE)
//
// 이 파일은 그래프의 '데이터'만 담는다. 지금은 코드로 짜지만, 필드가 전부 직렬화 가능하므로
// 나중에 ScriptableObject 인스펙터나 JSON에서 똑같은 구조로 채워 넣으면 된다.
//
// 그래프가 보여주는 4가지 해금 방식:
//   1) 취조 시작부터        : 선행 조건 없는 노드            (A_WHERE, B_A_WHERE, C_ADMIN ...)
//   2) 특정 질문 이후        : NeedQuestion(...)             (A_WHO_WITH, C_BREACH)
//   3) 특정 증언 + 문장 선택 : NeedTestimony + NeedSelected  (A_C_LEAVE, A_C_ADMIN)
//   4) 모순 질문 성공 이후   : 모순 질문 id를 NeedQuestion   (A_KNOWLEDGE)
public static class SampleCaseGraph
{
    const string W = "월터";

    public static CaseGraph Build()
    {
        var g = new CaseGraph
        {
            caseId = "CASE_NETWORK_INTRUSION",
            caseTitle = "아슬라니아 국가 전산망 침입 사건",
            briefing =
                "7월 23일 오후 3시 20분, 국가 DB에 비인가 접근이 발생해 전산망이 약 15분간 마비되었다.\n" +
                "침입 과정에서 내부 관리자 권한이 사용되었다. 내부 시스템에 접근 가능한 3인을 취조하라."
        };

        // 용의자 id / 이름
        const string A = "A", B = "B", C = "C";
        const string nameA = "클로버", nameB = "리나", nameC = "가렛";

        // ------------------------------------------------------------------
        // 증언(그래프의 핵심 토큰). 질문으로 확보되고, 다른 질문의 조건이 된다.
        // ------------------------------------------------------------------
        g.AddTestimony(new Testimony("T_A_ALIBI", A, nameA, "클로버는 사건 당일 회사에서 정상 근무했다고 주장함.", "회사", "근무", "알리바이"));
        g.AddTestimony(new Testimony("T_A_NOAUTH", A, nameA, "클로버는 국가 DB를 수정할 권한이 없다고 진술함.", "권한", "관리자"));
        g.AddTestimony(new Testimony("T_A_RELATION", A, nameA, "클로버는 피해 기관과 개인적 이해관계가 없다고 주장함.", "관계"));

        g.AddTestimony(new Testimony("T_B_LEAVE", B, nameB, "동료는 클로버가 사건 당일 연차라 회사에 없었다고 진술함.", "연차", "결근"));
        g.AddTestimony(new Testimony("T_B_CONFLICT", B, nameB, "동료는 최근 클로버와 업무상 갈등이 있었다고 인정함.", "갈등"));

        g.AddTestimony(new Testimony("T_C_ADMIN", C, nameC, "보안 담당자는 클로버에게 지난달부터 임시 관리자 권한이 있었다고 진술함.", "관리자", "권한"));
        g.AddTestimony(new Testimony("T_C_INTERNAL", C, nameC, "보안 담당자는 침입 로그가 외부가 아닌 내부 접속이었다고 진술함.", "내부", "로그"));

        // 모순 추궁으로 새로 확보되는 증언
        g.AddTestimony(new Testimony("T_A_RECANT_LEAVE", A, nameA, "추궁당하자 클로버는 사건 당일 출근하지 않았음을 인정함.", "번복", "결근"));
        g.AddTestimony(new Testimony("T_A_RECANT_ADMIN", A, nameA, "클로버는 임시 관리자 권한을 갖고 있었음을 인정함.", "번복", "권한"));
        g.AddTestimony(new Testimony("T_A_KNOWLEDGE", A, nameA, "클로버가 미공개 정보인 '기록 삭제' 사실을 알고 있었음이 드러남.", "미공개", "지식모순"));

        // ------------------------------------------------------------------
        // 용의자 A: 클로버 (범인)
        // ------------------------------------------------------------------
        g.AddSuspect(new SuspectData(A, nameA, "정부기관 전산관리 부서 직원").Add(
            // 1) 취조 시작부터 가능한 일반 질문
            new QuestionNode("A_WHERE", "사건 당일 어디에 있었습니까?")
                .Say(W, "사건 당일 오후 3시, 어디에 계셨습니까?")
                .Say(nameA, "회사에서 계속 근무하고 있었습니다.")
                .Grant("T_A_ALIBI"),

            new QuestionNode("A_AUTH", "국가 DB를 수정할 권한이 있습니까?")
                .Say(W, "국가 DB를 수정할 수 있는 권한을 갖고 계셨습니까?")
                .Say(nameA, "아니요. 저에게 그런 권한은 없습니다.")
                .Grant("T_A_NOAUTH"),

            new QuestionNode("A_RELATION", "피해 기관과 어떤 관계입니까?")
                .Say(W, "피해를 입은 기관과 개인적인 이해관계가 있습니까?")
                .Say(nameA, "전혀요. 그저 업무로 엮였을 뿐입니다.")
                .Grant("T_A_RELATION"),

            // 2) 선행 '질문'으로 열리는 후속 질문
            new QuestionNode("A_WHO_WITH", "그 시간 회사에 누가 함께 있었습니까?")
                .NeedQuestion("A_WHERE")
                .Say(W, "근무 중이었다면, 그 시간 곁에 누가 있었습니까?")
                .Say(nameA, "다들 각자 자리에 있어서… 딱히 저를 본 사람은 없을 겁니다."),

            // 3) 모순 질문 1 — 출근 여부: 동료 증언(T_B_LEAVE) 선택 + 클로버 알리바이(T_A_ALIBI) 필요
            new QuestionNode("A_C_LEAVE", "동료는 당신이 그날 연차였다고 합니다. 어느 쪽이 사실입니까?")
                .NeedSelected("T_B_LEAVE")
                .NeedTestimony("T_A_ALIBI")
                .Say(W, "당신은 그날 회사에서 근무했다고 했습니다. 그런데 동료는 당신이 연차를 써서 나오지 않았다고 진술했습니다. 어느 쪽이 사실입니까?")
                .Say(nameA, "…그날은, 사실 출근하지 않았습니다. 제가 착각했던 모양입니다.")
                .Grant("T_A_RECANT_LEAVE"),

            // 3) 모순 질문 2 — 관리자 권한: 보안 증언(T_C_ADMIN) 선택 + 클로버 권한없음(T_A_NOAUTH) 필요
            new QuestionNode("A_C_ADMIN", "보안 담당자는 당신에게 관리자 권한이 있었다고 합니다.")
                .NeedSelected("T_C_ADMIN")
                .NeedTestimony("T_A_NOAUTH")
                .Say(W, "권한이 없다고 하셨죠. 그런데 보안 담당자는 지난달부터 당신에게 임시 관리자 권한이 있었다고 진술했습니다.")
                .Say(nameA, "…임시 권한이 있긴 했습니다. 말할 필요가 없다고 생각했을 뿐입니다.")
                .Grant("T_A_RECANT_ADMIN"),

            // 4) 모순 질문(A_C_ADMIN) 성공 이후 열리는 지식 모순 질문
            new QuestionNode("A_KNOWLEDGE", "삭제된 기록을 어떻게 알고 있었습니까?")
                .NeedQuestion("A_C_ADMIN")
                .Say(W, "한 가지 더. 당신은 방금 '기록이 삭제됐다'고 했습니다. 그 사실은 외부에 공개된 적이 없습니다.")
                .Say(nameA, "그건… 그저 짐작이었습니다.")
                .Grant("T_A_KNOWLEDGE")
        ));

        // ------------------------------------------------------------------
        // 용의자 B: 동료 리나 — 클로버의 알리바이를 깨는 핵심 증언 제공
        // ------------------------------------------------------------------
        g.AddSuspect(new SuspectData(B, nameB, "전산관리 부서 동료").Add(
            new QuestionNode("B_A_WHERE", "클로버는 사건 당일 어디에 있었습니까?")
                .Say(W, "동료인 클로버는 사건 당일 어디에 있었습니까?")
                .Say(nameB, "클로버요? 그날 연차 썼어요. 회사에 안 나왔습니다.")
                .Grant("T_B_LEAVE"),

            new QuestionNode("B_RELATION", "클로버와의 관계는 어떻습니까?")
                .Say(W, "클로버와의 사이는 어떻습니까?")
                .Say(nameB, "요즘 업무 때문에 좀 부딪혔어요. 솔직히 편한 사이는 아닙니다.")
                .Grant("T_B_CONFLICT")
        ));

        // ------------------------------------------------------------------
        // 용의자 C: 보안 담당자 가렛 — 관리자 권한 모순의 근거 제공
        // ------------------------------------------------------------------
        g.AddSuspect(new SuspectData(C, nameC, "정부기관 보안 담당자").Add(
            new QuestionNode("C_ADMIN", "클로버의 시스템 접근 권한은 어땠습니까?")
                .Say(W, "클로버의 시스템 접근 권한은 어느 정도였습니까?")
                .Say(nameC, "지난달부터 임시 관리자 권한이 부여돼 있었습니다.")
                .Grant("T_C_ADMIN"),

            new QuestionNode("C_BREACH", "권한은 어떻게 탈취됐다고 봅니까?")
                .NeedQuestion("C_ADMIN")
                .Say(W, "그 권한이 외부에서 탈취됐다고 보십니까?")
                .Say(nameC, "처음엔 그렇게 봤습니다. 그런데 로그를 다시 보니… 접속은 내부에서 이뤄졌습니다.")
                .Grant("T_C_INTERNAL")
        ));

        // ------------------------------------------------------------------
        // 최종 판결: 정답 용의자 = 클로버(A). 사건 설명은 동기/수단/대상/목적 4칸으로 판정.
        // ------------------------------------------------------------------
        g.culpritSuspectId = A;
        g.verdictTypoThreshold = 0.8f;
        g.minimumCorrectFields = 0; // 0 = 4칸 모두 정답이어야 성공
        // {범인}은 지목한 용의자 이름이 채워지는 슬롯. 나머지 {동기}/{수단}/{대상}/{목적}은 입력칸.
        g.verdictTemplate =
            "{범인}은(는) {동기} 때문에\n" +
            "{대상}을(를) 노려 {수단}(으)로\n" +
            "국가 DB를 침입해, 전산망을 {목적}시키려 했다.";
        g.verdictFields = new System.Collections.Generic.List<CulpritDetection.CaseField>
        {
            new CulpritDetection.CaseField("동기", "증오").AddOption("증오", "반감", "적대", "혐오"),
            new CulpritDetection.CaseField("수단", "해킹").AddOption("해킹", "침입", "전산 공격", "공격"),
            new CulpritDetection.CaseField("대상", "더 킹").AddOption("더 킹", "국왕", "지배자", "체제", "정부"),
            new CulpritDetection.CaseField("목적", "마비").AddOption("마비", "정지", "중단"),
        };

        return g;
    }
}
