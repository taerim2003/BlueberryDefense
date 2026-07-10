using UnityEngine;

// 레벨업/진화 패널처럼 게임을 멈추는 모달 UI가 동시에 여러 개 열릴 수 있어서,
// 하나가 닫힌다고 바로 Time.timeScale을 풀면 안 됨 — 참조 카운트로 관리.
public static class ModalPause
{
    private static int count;

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
}
