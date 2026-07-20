using UnityEngine;

// 적 1종의 밸런스 스탯(Tier A). 프리팹의 Enemy 컴포넌트가 참조한다.
// Awake에서 인스턴스 런타임 필드로 복사해 쓴다 — SO는 공유 에셋이라 스테이지 배율을 여기 곱하면 안 됨.
// 이관 대상은 순수 스칼라뿐. 행동 플래그(isFlying/isTreasure/blocksProjectiles)·VFX·스프라이트·사망분출은 프리팹에 남는다.
[CreateAssetMenu(fileName = "EnemyDefinition", menuName = "BlueberryDefense/Enemy Definition")]
public class EnemyDefinition : ScriptableObject
{
    public float moveSpeed = 2f;
    public int damage = 10;
    public float maxHealth = 20f;
    public int xpValue = 5;
    public float essenceDropChance = 0.15f; // 처치 시 정수 드랍 확률(보물은 확률 무시·확정)
    public int essenceDropAmount = 2;        // 드랍 시 지급 정수량
}
