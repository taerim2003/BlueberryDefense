using UnityEngine;

// 적 머리 위에 뜨는 상태이상 아이콘 4종을 **한 곳에서** 들고 있는다.
//
// 🔴 적 프리팹마다 배선하지 않는 이유: 적 프리팹이 12장이라 4칸씩 48번을 꽂아야 하고,
//    새 적이 늘 때마다 또 빠뜨린다. 씬 오브젝트 하나가 소유하고 Enemy가 정적으로 집어 간다
//    (SfxLibrary·SkillIconLibrary와 같은 결).
//
// 배선: Battle 씬의 아무 상시 오브젝트(EnemySpawner 등)에 붙이고 4칸을 채운다.
//   기절 = HeadIcon_Stun · 둔화 = HeadIcon_Slow · 중독 = HeadIcon_Poison · 취약 = HeadIcon_Vulnerable
// 미배선이면 아이콘이 안 뜰 뿐 판정은 그대로 돈다.
public class StatusIconLibrary : MonoBehaviour
{
    [SerializeField] private Sprite stun;
    [SerializeField] private Sprite slow;
    [SerializeField] private Sprite poison;
    [SerializeField] private Sprite vulnerable;

    private static StatusIconLibrary instance;

    private void Awake() => instance = this;
    private void OnDestroy() { if (instance == this) instance = null; }

    public static Sprite Stun => instance != null ? instance.stun : null;
    public static Sprite Slow => instance != null ? instance.slow : null;
    public static Sprite Poison => instance != null ? instance.poison : null;
    public static Sprite Vulnerable => instance != null ? instance.vulnerable : null;
}
