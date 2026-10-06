using UnityEngine;

// 진화 스킬 1종(스킬 × 루트 × 티어)의 밸런스 데이터.
//
// 🔴 **값이 0이면 "이 진화는 이 축을 안 정한다"** 는 뜻이라 진화 전 값을 그대로 물려받는다.
//    값을 넣는 순간부터 그 축만 에셋이 이긴다 — `Prog_*`(SkillProgression)와 같은 규약이다.
//
// 🔴 **쿨타임과 피해는 2026-09-28부터 44종 전부가 값을 채워 두었다**(사용자 결정). 코드 곳곳에서 곱하던 배수를
//    전부 없앴으므로, 어떤 진화의 시작 쿨·피해가 얼마인지는 **이 에셋 한 장만 열면 된다.** 0으로 되돌리지 말 것.
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
