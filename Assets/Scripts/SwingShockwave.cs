using System.Collections.Generic;
using UnityEngine;

// 휘두르기 2루트 진화로 생기는 충격파. 내려찍은 자리에서 왼쪽으로 달려나가며
// 지나치는 적을 하나씩 때리고 살짝 밀어낸다.
//
// 관통 예산은 없지만 **방패 블루베리는 이걸 막는다**(Projectile·Orb와 같은 규칙 — 방패의 존재 이유를
// 충격파만 무시하면 방패 스테이지가 통째로 무의미해진다). 방패에 닿으면 마지막 한 대를 주고 소멸.
// 그 외에는 닿는 적을 전부 때리며, 같은 적을 두 번 때리지 않도록 hitEnemies로 걸러낸다.
[RequireComponent(typeof(Collider2D))]
public class SwingShockwave : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 16f;
    [SerializeField] private float lifetime = 2.5f;  // 맵이 넓어져도 끝까지 가도록 넉넉히(fieldScale 1.8 기준 약 24유닛)

    public float Damage { get; set; }
    public float CritChance { get; set; }
    public float Knockback { get; set; }
    // 휘두르기 R1 2차 「거대한 파도」 — 노션 문구 "지진파가 커지고 맞은 적들이 **받는 피해가 증가**한다".
    // 0이면 취약을 안 건다(1차 「지진파」).
    public float VulnerableMultiplier { get; set; }
    public float VulnerableDuration { get; set; } = 3f;

    private readonly HashSet<Enemy> hitEnemies = new HashSet<Enemy>();
    private float age;

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * Time.deltaTime);
        age += Time.deltaTime;
        if (age >= lifetime) Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null || !enemy.IsAlive || hitEnemies.Contains(enemy)) return;
        hitEnemies.Add(enemy);

        enemy.TakeSkillHit(Damage, CritChance, ActiveSkillId.Swing,
            VulnerableMultiplier > 0f ? StatusIconLibrary.Vulnerable : null);
        if (Knockback > 0f) enemy.ApplyKnockback(Knockback);
        if (VulnerableMultiplier > 0f) enemy.ApplyVulnerable(VulnerableMultiplier, VulnerableDuration);

        // 방패 블루베리에 막힌다 — 한 대 주고 여기서 멈춘다(뒤에 있는 적은 못 맞힌다).
        if (enemy.BlocksProjectiles) Destroy(gameObject);
    }
}
