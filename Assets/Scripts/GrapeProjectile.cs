using UnityEngine;

// 포도알 — 목표 지점까지 **포물선**을 그리며 날아가고, 닿으면 터져서 독성 안개를 남긴다.
// 그림은 기본 오브를 그대로 빌려 쓴다(사용자 결정: 전용 이펙트를 새로 만들지 않는다).
//
// 적을 쫓지 않는다. 던진 순간의 좌표로 날아가므로 적이 움직이면 빗나가는데, 그게 의도다 —
// 안개가 넓게 깔려서 정확히 맞히는 것보다 "어디에 깔리느냐"가 이 스킬의 판단 지점이다.
public class GrapeProjectile : MonoBehaviour
{
    private Vector3 start;
    private Vector3 target;
    private float flightTime;
    private float arcHeight;
    private float elapsed;
    private float spinSpeed;
    private System.Action<Vector3> onLand;

    // Init을 받기 전까지는 아무것도 하지 않는다. 적이 없어 **전방에 떠서 기다리는** 알이 이 상태로 머문다
    // (PlayerSkills.HoldGrapesUntilEnemy가 적이 나오면 Init을 준다). 회전도 안 시킨다 — 멈춘 것으로 보여야 한다.
    // ⚠️ 이 가드가 없으면 flightTime이 0이라 t가 곧장 1이 되어 **소환되자마자 (0,0)에 착탄**한다.
    private bool waiting = true;

    public void Init(Vector3 target, float flightTime, float arcHeight, System.Action<Vector3> onLand)
    {
        waiting = false;
        start = transform.position;
        this.target = target;
        this.flightTime = Mathf.Max(0.05f, flightTime);
        this.arcHeight = arcHeight;
        this.onLand = onLand;
        elapsed = 0f;
        spinSpeed = Random.Range(180f, 360f) * (Random.value < 0.5f ? -1f : 1f);
    }

    private void Update()
    {
        if (waiting) return;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / flightTime);

        // 직선 보간 + 사인 아치. 물리를 쓰지 않는 이유는 **착탄 지점과 시각이 정확해야** 하기 때문이다
        // (안개 위치가 던질 때 이미 정해져 있어야 여러 발이 고르게 퍼진다).
        Vector3 pos = Vector3.Lerp(start, target, t);
        pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
        transform.position = pos;
        transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);

        if (t >= 1f)
        {
            onLand?.Invoke(target);
            Destroy(gameObject);
        }
    }
}
