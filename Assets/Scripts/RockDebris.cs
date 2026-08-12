using UnityEngine;

// 망치에 맞은 자리에서 튀어오르는 돌조각 하나. 스폰한 쪽(PlayerSkills.SpawnRockDebris)이 Launch로 초기 속도를 준다.
// ObjectPool로 재사용되므로 Awake가 다시 돌지 않는다 — 스폰마다 초기화해야 하는 값은 전부 Launch에서 되돌린다.
[RequireComponent(typeof(SpriteRenderer))]
public class RockDebris : MonoBehaviour
{
    [SerializeField] private float gravity = 18f;
    [SerializeField] private float lifetime = 0.55f;

    private SpriteRenderer sr;
    private Vector2 velocity;
    private float spinDegPerSec;
    private float timer;

    private void Awake() => sr = GetComponent<SpriteRenderer>();

    public void Launch(Vector2 initialVelocity, float spin)
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        velocity = initialVelocity;
        spinDegPerSec = spin;
        timer = 0f;
        transform.rotation = Quaternion.identity;

        Color c = sr.color;
        c.a = 1f; // 직전 재생에서 0까지 페이드된 채 반납됐다 — 되돌리지 않으면 투명한 돌이 날아간다
        sr.color = c;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= lifetime)
        {
            ObjectPool.Instance.Despawn(gameObject);
            return;
        }

        velocity.y -= gravity * Time.deltaTime;
        transform.position += (Vector3)(velocity * Time.deltaTime);
        transform.Rotate(0f, 0f, spinDegPerSec * Time.deltaTime);

        Color c = sr.color;
        c.a = 1f - timer / lifetime;
        sr.color = c;
    }
}
