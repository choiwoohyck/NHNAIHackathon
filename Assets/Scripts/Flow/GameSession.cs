// 씬 사이로 데이터를 넘기는 정적 보관소 (씬을 로드해도 static은 살아남는다).
//   CaseSelect 씬 → 선택한 사건(SelectedCase) → Chat(취조) 씬
//   Chat 씬 판결 → 결과(LastVerdict/점수) → Ending 씬
public static class GameSession
{
    public static InterrogationCase SelectedCase;   // 사건 선택 화면에서 고른 사건 (없으면 컨트롤러의 기본 사건 사용)

    public static bool HasVerdict;
    public static VerdictResult LastVerdict;
    public static int CorrectFields;
    public static int TotalFields;
    public static string CulpritId;

    public static void SetVerdict(VerdictResult result, int correct, int total, string culpritId)
    {
        LastVerdict = result;
        CorrectFields = correct;
        TotalFields = total;
        CulpritId = culpritId;
        HasVerdict = true;
    }

    // 사건을 처음부터 다시 시작할 때(재수사) 판결 상태만 초기화.
    public static void ClearVerdict()
    {
        HasVerdict = false;
        CorrectFields = TotalFields = 0;
        CulpritId = null;
    }
}
