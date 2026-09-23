using UnityEngine;

// 레벨업/진화 패널처럼 게임을 멈추는 모달 UI가 동시에 여러 개 열릴 수 있어서,
// 하나가 닫힌다고 바로 Time.timeScale을 풀면 안 됨 — 참조 카운트로 관리.
public static class ModalPause
{
    private static int count;

    // 모달이 하나라도 떠 있는가. Update는 timeScale 0에도 계속 돌기 때문에,
    // "시간이 멈춘 동안 입력을 받으면 안 되는" 쪽이 이 값을 직접 봐야 한다.
    public static bool IsPaused => count > 0;

    public static void Push()
    {
        count++;
        Time.timeScale = 0f;
#if BOT_QA
        var caller = new System.Diagnostics.StackFrame(1).GetMethod();
        Pushers.Add((caller != null ? caller.DeclaringType?.Name + "." + caller.Name : "?")
            + " @" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
#endif
    }

    public static void Pop()
    {
        count = Mathf.Max(0, count - 1);
        if (count == 0) Time.timeScale = 1f;
#if BOT_QA
        if (Pushers.Count > 0) Pushers.RemoveAt(Pushers.Count - 1);
#endif
    }

#if BOT_QA
    // QA 빌드 전용 진단: 아직 안 닫힌 Push가 누구였나(QAInvariants가 "타이틀인데 모달이 남음" 오류에 싣는다). 릴리스 빌드엔 없다.
    public static readonly System.Collections.Generic.List<string> Pushers = new System.Collections.Generic.List<string>();
#endif

    // 판 시작 시 초기화 (RunState에서 호출).
    // 모달이 열린 채로 판이 끝나면 count가 남아 다음 판이 timeScale=0인 채로 시작해 버린다.
    public static void ResetRunState()
    {
        count = 0;
        Time.timeScale = 1f;
#if BOT_QA
        Pushers.Clear();
#endif
    }
}
