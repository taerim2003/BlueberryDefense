// 여러 시스템에 걸치는 전역 스킬 상수 집결지(Tier B — 로직 모양이라 SO로 빼지 않고 코드에 모음).
// 값은 여기서 편집하면 참조처(주로 PlayerSkills)가 별칭 const로 물려받아 전파된다.
// 순수 스칼라(스킬 기본 수치)는 SkillTable(SO), 캐릭터/맵별 데이터는 각 Definition(SO)을 볼 것.
public static class BalanceConstants
{
    public const float GlobalCooldown = 0.4f;       // 모든 스킬 공통 최소 간격 + 쿨타임 하한
    public const float OrbAltarCooldown = 15f;      // 오브 제단(Orb path2 T2+) 쿨타임
    public const float MaxCritChance = 0.7f;        // 치명타 확률 상한(상시 100% 크리 방지)
    public const int BasicAttackBaseHits = 3;       // 기본공격 기본 멀티히트 수
    public const int SnipingBaseShots = 5;          // 스나이핑 타겟당 저격 횟수
    public const float SnipingShotInterval = 0.08f; // 스나이핑 저격 간격
    public const float FlyingArrowSpawnRaise = 0.65f; // 비행 타격 진화 시 발사점 상승
}
