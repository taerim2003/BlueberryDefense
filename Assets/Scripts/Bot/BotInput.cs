// 봇 플레이테스트(Tools/BotPlaytest, `balance` 스킬)가 게임 안을 관측·조종하는 창구.
//
// 🔴 **평소 게임에는 아무 영향이 없어야 한다.** 필드는 전부 기본값(false/null)이고, 호출부는 `?.Invoke` 한 줄뿐이다.
//    값을 세우는 쪽은 에디터 전용 `BotPilot`/`BotRecorder`뿐이라 빌드에서는 영원히 비어 있다.
//    이 파일만 `#if UNITY_EDITOR` 밖에 둔 이유: PlayerSkills·Enemy의 호출부가 빌드에서도 컴파일돼야 해서.
//
// 훅을 새로 걸 때도 **게임 로직을 바꾸지 말고 관측만** 한다 — 봇이 재는 값이 사람이 하는 게임과 달라지면 측정이 무의미하다.
public static class BotInput
{
    // QWER 꾹 누르기. PlayerSkills.Update가 키 입력과 OR로 본다(Game 뷰 포커스와 무관하게 동작해야 무인 실행이 된다).
    public static bool HoldSkills;

    // 스킬 시전 확정 직후(TryUseSkill). baseCd = 감소율 곱하기 전 쿨, cdMult = 스킬트리·패시브 감소율.
    public static System.Action<EquippedSkill, float, float> OnCast;

    // 적이 플레이어를 박치기한 순간(플레이어 피해의 유일한 경로). 피해량은 방어 적용 전.
    public static System.Action<Enemy, int> OnPlayerHit;

    // 적이 피해를 받은 순간. source는 DamageMeter와 같은 규칙(낙뢰 발동은 Lightning), hpBefore는 이 타격 직전 체력.
    public static System.Action<Enemy, ActiveSkillId?, float, float> OnEnemyDamaged;

    // 적 사망(isDead가 세워지는 순간 1회). 막타 스킬은 직전 OnEnemyDamaged의 source로 판정한다.
    public static System.Action<Enemy> OnEnemyKilled;

    // UFO(캐리어)가 투하를 마치고 화면 위로 빠져나간 순간 — 처치가 아니다.
    public static System.Action<Enemy> OnEnemyEscaped;

    // 감속·기절(multiplier 0) / 넉백이 걸린 순간.
    public static System.Action<Enemy, float, float> OnSlow;
    public static System.Action<Enemy, float> OnKnockback;
}
