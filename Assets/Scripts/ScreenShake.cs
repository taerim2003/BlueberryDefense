using UnityEngine;
using DG.Tweening;

// 카메라를 잠깐 흔든다(파인애플 망치처럼 "묵직하게 내려찍는" 스킬용).
//
// 이 프로젝트의 카메라는 플레이어를 따라다니지 않는다 — 부팅 때 RunBootstrap이 map.cameraYLift로
// 한 번 올려 두면 그대로 고정이다. 그래서 "흔들기 직전 위치"를 기준점으로 잡아 두고 끝나면 그리로 되돌리면 된다.
// (따라다니는 카메라가 생기면 이 전제가 깨진다 — 그땐 카메라를 빈 부모 밑에 넣고 부모를 흔들 것.)
//
// ⚠️ 흔드는 동안 Camera.main.transform.position을 읽는 코드(적 스폰 x·화살비 시작점)가 그만큼 흔들린 값을 본다.
//    진폭이 0.3유닛 이하라 화면 밖 스폰 지점에는 영향이 없지만, 카메라 위치로 **판정**을 하게 되면 그땐 문제가 된다.
public static class ScreenShake
{
    // 망치 내려찍기. 파인애플의 **기본공격**이라 판마다 수백 번 흔들린다 —
    // 한 방의 손맛보다 누적 멀미가 먼저 온다(8/25 빌드 검수: "너무 많이 흔들려서 멀미남").
    // 화면 높이가 10유닛이니 0.12 = 약 1.2%.
    public const float SwingStrength = 0.12f;
    public const float SwingDuration = 0.18f;

    private static Transform cam;
    private static Vector3 basePos;
    private static Tween tween;

    public static void Shake(float strength, float duration)
    {
        Camera main = Camera.main;
        if (main == null) return;

        // 씬이 바뀌면 카메라도 새 오브젝트다. 도메인 리로드를 끈 설정에서도 옛 Transform이 남지 않게 매번 비교한다.
        if (cam != main.transform)
        {
            cam = main.transform;
            tween = null;
        }

        // 겹쳐 흔들면 DOTween이 오프셋을 누적해 카메라가 제자리로 안 돌아온다. 이전 것을 끄고 기준점부터 복구한다.
        if (tween != null && tween.IsActive())
        {
            tween.Kill();
            cam.position = basePos;
        }

        basePos = cam.position;
        // 레벨업 등으로 timeScale=0이 되면 스케일 시간 트윈은 흔들린 자리에서 얼어붙는다 → 무조건 unscaled로 끝낸다.
        tween = cam.DOShakePosition(duration, new Vector3(strength, strength, 0f), 18, 90f, false, true)
                   .SetUpdate(true)
                   .OnComplete(() => cam.position = basePos);
    }
}
