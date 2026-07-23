// 한 판 동안만 유효한 static 상태를 판 시작 시 전부 되돌린다.
//
// 왜 필요한가: 진화/레벨업 효과 상당수가 static 필드에 누적된다(치명타 배율, 기본공격 추가 피해,
// 낙뢰 체인 여부 등). static은 씬 로드로 초기화되지 않고 **프로세스가 살아있는 동안 유지**되므로,
// 리셋을 빠뜨리면 다음 판에 지난 판의 효과가 그대로 남는다(레벨 1 패시브가 진화 효과를 갖는 등).
// 새 static 런타임 상태를 추가하면 **여기(또는 소유 클래스의 ResetRunState)에도 반드시 등록할 것.**
//
// GameManager.Awake에서 호출한다. 값 리셋만 하고 이벤트 구독(LightningStorm.OnProc)은 건드리지 않는다
// — 구독은 각 컴포넌트가 Awake/OnDestroy 짝으로 관리하며, Awake 실행 순서가 정해져 있지 않아
//   여기서 구독을 지우면 이미 등록한 컴포넌트의 구독까지 날아갈 수 있다.
public static class RunState
{
    public static void ResetAll()
    {
        PlayerSkills.ResetRunState();
        PlayerPassives.ResetRunState();
        LightningStorm.ResetRunState();
        BuffTracker.ResetRunState();
        DamageMeter.Reset();
        ModalPause.ResetRunState();

        EnemySpawner.ExtraTreasureChance = 0f; // 지식 연계 path1로 누적되는 보물상자 등장 확률

        // MetaBonuses/MetaRun은 여기서 건드리지 말 것 — MetaRunApplier.Awake가 Reset 직후
        // 스킬트리 값을 얹는데, Awake 순서가 보장되지 않아 여기서 또 Reset하면 그 값이 지워질 수 있다.
    }
}
