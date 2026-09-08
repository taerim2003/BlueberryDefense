using UnityEngine;

// 진화 스킬 1종(스킬 × 루트 × 티어)의 밸런스 데이터.
//
// 🔴 **값이 0이면 "이 진화는 이 축을 안 정한다"** 는 뜻이라 지금까지의 동작(`ApplyPathTierEffect`의 배수 +
//    `EvolutionRoutes.EvolveDamageMult`)이 그대로 남는다. 그래서 에셋을 비워 두면 게임은 예전과 100% 같게 돈다.
//    값을 넣는 순간부터 그 축만 에셋이 이긴다 — `Prog_*`(SkillProgression)와 같은 규약이다.
//
// ⚠️ 여기 값은 **진화 직후의 시작값**이다. 진화하면 표시 레벨이 1로 리셋되므로, 레벨업 커브(`levels`)도
//    그 시점부터 다시 탄다. `levels`가 비어 있으면 원래 스킬의 `Prog_*` 커브를 그대로 쓴다.
//
// ⚠️ `stage`는 **화면에 보이는 진화 차수(1차·2차)** 다. 코드의 `PathTier`(1차=2, 2차=3)와 다르다 —
//    변환은 `EvolutionRoutes.TargetPathTier`가 한다. 여기에 PathTier 값을 적지 말 것.
[CreateAssetMenu(fileName = "EvolutionProgression", menuName = "BlueberryDefense/Evolution Progression")]
public class EvolutionProgression : ScriptableObject
{
    [Header("어느 진화인가")]
    public ActiveSkillId skill;
    [Range(0, 1)] public int route;    // 진화창의 왼쪽(0) / 오른쪽(1) 루트
    [Range(1, 2)] public int stage = 1; // 1차 / 2차

    // 🔴 0 = "안 정한다"(지금 동작 유지). 넣으면 그 축만 이 값으로 **덮어쓴다**.
    [Header("진화 직후 시작값 — 0이면 현행 유지")]
    public float baseDamage = 0f;
    public float baseCooldown = 0f;
    public float baseDuration = 0f;
    public int baseHits = 0;
    public int basePierce = 0;
    public int baseProjectiles = 0;

    [Header("레벨업 커브 (비어 있으면 원래 스킬의 Prog_* 커브를 그대로 쓴다)")]
    public LevelUpStep[] levels;

    // 목표 레벨(2 이상)에 적용할 스텝. 커브가 비어 있거나 범위 밖이면 null → 호출부가 Prog_*로 폴백한다.
    public LevelUpStep StepForLevel(int level)
    {
        int idx = level - 2;
        return levels != null && idx >= 0 && idx < levels.Length ? levels[idx] : null;
    }
}
