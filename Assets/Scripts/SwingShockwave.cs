using System.Collections.Generic;
using UnityEngine;

// 휘두르기 2루트 진화로 생기는 충격파. 내려찍은 자리에서 **맵 끝까지** 왼쪽으로 달려나가며
// 지나치는 적을 하나씩 때리고 살짝 밀어낸다.
//
// Projectile과 달리 관통 예산이 없다 — 닿는 적을 **전부** 때리고 멈추지 않는다(방패 블루베리도 못 막는다).
// 대신 같은 적을 두 번 때리지 않도록 hitEnemies로 걸러낸다.
[RequireComponent(typeof(Collider2D))]
public class SwingShockwave : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 16f;
    [SerializeField] private float lifetime = 2.5f;  // 맵이 넓어져도 끝까지 가도록 넉넉히(fieldScale 1.8 기준 약 24유닛)

    public float Damage { get; set; }
    public float CritChance { get; set; }
    public float Knockback { get; set; }

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

        enemy.TakeSkillHit(Damage, CritChance, ActiveSkillId.Swing);
        if (Knockback > 0f) enemy.ApplyKnockback(Knockback);
    }
}
