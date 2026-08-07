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
    }

    public static void Pop()
    {
        count = Mathf.Max(0, count - 1);
        if (count == 0) Time.timeScale = 1f;
    }

    // 판 시작 시 초기화 (RunState에서 호출).
    // 모달이 열린 채로 판이 끝나면 count가 남아 다음 판이 timeScale=0인 채로 시작해 버린다.
    public static void ResetRunState()
    {
        count = 0;
        Time.timeScale = 1f;
    }
}
