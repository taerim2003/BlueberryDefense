using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public enum ActiveSkillId
{
    BasicAttack,
    Whirlwind,
    Orb,
    Lightning,
    EagleDrop,
    Sniping,  // 신규: 가장 체력 높은 적을 5회 저격
    Homing,   // 신규: 적 추적 미사일(성장형)
    Shotgun,  // 신규: 산탄 장착(타수 버프)
    Rewind,   // 신규: 다른 스킬 쿨타임을 앞당김(되감기)
    Swing,    // 신규: 파인애플 전용 근접 광역 — 후려쳐서 뒤로 밀어낸다
    GrapeToss, // 신규: 포도 전용 — 독성 포도알을 던져 독안개를 깔고 중독시킨다
    // enum 끝에 추가 — 아이콘 인덱스/저장값 유지
}

// 액티브 스킬의 성격 분류(레벨업 카드·일시정지 요약에 배지로 표시).
public enum SkillCategory { Attack, Buff, Utility }

public class EquippedSkill
{
    public ActiveSkillId Id;
    public Key Key;
    public float Cooldown;
    public float Damage;
    public float CooldownTimer;
    public float ReadySince = -1f; // 쿨이 끝나 발동 대기에 들어간 게임 시각(-1 = 쿨 도는 중). 발동 순서 공정성에 쓴다
    public float PressedUntil = -1f; // 키를 눌러 둔 시전 요청이 살아 있는 게임 시각(PlayerSkills.PressBuffer). 나가면 -1
    public int Level = 1;
    public float Scale = 1f;
    public float ProjectileSpeedMultiplier = 1f;
    public float ProcChanceBonus = 0f;

    // 레벨업 전용 고유 강화치 (진화 트리와 별개)
    public int ExtraPierce = 0; // 관통 +N (SkillStat.Pierce — 스킬 종류를 안 가린다)
    // 한 번의 시전에서 나가는 발사체/투하 수 — 기본공격 화살, 스나이핑 연사, 호밍 미사일, 독수리 투하, 산탄 알
    public int ExtraProjectiles = 0;
    // 동시에 상대하는 적 수 — 오브 동시 타격, 스나이핑 저격 대상
    public int ExtraTargets = 0;
    // 한 번 시전에 나가는 발사 묶음 수 추가 — 산탄의 "빵 빵"을 몇 번 할지(2026-09-19 사용자).
    // ⚠️ ExtraProjectiles(알 수)와 다른 축이다. 볼리가 늘면 같은 피해 묶음이 통째로 한 번 더 나간다.
    public int ExtraVolleys = 0;
    // 반복 타격 간격 배율(작을수록 자주 때림) — 회오리 피해 주기, 독수리 투하 간격
    public float TickIntervalMult = 1f;
    public float ExtraWhirlwindDuration = 0f; // 회오리: 지속시간(초) 추가
    public float ExtraShotgunDuration = 0f;   // 산탄: 버프 지속 + 전탄발사(R1 2차) 지속(초) 추가
    public int GrowthStacks = 0; // 호밍 미사일: 사용할수록 누적되는 성장 스택(이번 판 한정)
    public float RewindAmount = 1f; // 되감기: 다른 스킬 쿨타임을 앞당기는 시간(초). 짝수 레벨업마다 +0.15

    // ── 2026-09-29 사용자 지시로 추가한 성장축 13종 ──
    // 설치물(기계·기둥)이 서 있는 시간. 그 설치물이 뽑는 것의 수명은 ExtraWhirlwindDuration이다.
    public float ExtraInstallDuration = 0f;
    public int ExtraMaxStacks = 0;      // 낙뢰 버프 중첩 상한 추가
    public float SubDamageMult = 1f;    // 폭발·파동처럼 본체에서 갈라진 피해에 곱한다
    public float SubScaleMult = 1f;     // 그 파생 효과의 반경에 곱한다
    public float BossDamageBonus = 0f;  // 보스에게만 더 들어가는 비율
    public int ExtraBonusHits = 0;      // 산탄 버프가 주는 타수 추가
    public float EmpowerMult = 1f;      // 되감기 강화량 배율
    public float PenaltyReduction = 0f; // 그 강화의 쿨 페널티를 깎는 비율(0~1)
    public float AllCooldownBonus = 0f; // 모든 스킬 쿨 추가 감소율(0~1)
    public float EnemySlowBonus = 0f;   // 모든 적 이동속도 감소율(0~1)
    public float ExtraStunDuration = 0f;
    public int StunIntervalBonus = 0;   // 음수면 더 자주 기절한다
    public int ExtraLifesteal = 0;      // 타격당 흡수 체력 추가

    // 진화 효과 저장소. path 0=기본(무의존), 1=패시브 연계, 2=액티브 연계. 각 값은 도달한 티어(0~3).
    // ⚠️ 이제 이 배열을 직접 올리지 않는다 — 진화는 EvolutionRoutes를 통해 루트/티어로만 다룬다(§EvolutionRoutes).
    //    Fire*/TryUseSkill이 이 값을 그대로 읽으므로 저장 형식만 유지하는 것.
    public readonly int[] PathTier = new int[3];

    // ── 진화 상태 (2루트 × 2티어) ──
    public int TotalLevel = 1;      // 진화 리셋과 무관한 누적 레벨 — 표시용(진화 게이트는 표시 레벨 Level을 본다)
    public int EvolutionStage = 0;  // 0=미진화, 1=1차 진화, 2=2차 진화(최종)
    public int Route = -1;          // 1차 진화에서 고른 루트(0/1). 2차는 같은 루트를 이어간다

    // 진화하면 이름이 바뀐다. 미진화면 원래 이름.
    public string DisplayName =>
        EvolutionStage > 0 ? EvolutionRoutes.EvolvedName(Id, Route, EvolutionStage) : PlayerSkills.GetActiveSkillName(Id);
}

public class PlayerSkills : MonoBehaviour
{
    // 전역 상수는 BalanceConstants에 모여 있고 여기선 별칭으로 참조(호출부 이름 유지). 값 편집은 BalanceConstants에서.
    private const float GlobalCooldown = BalanceConstants.GlobalCooldown;
    private const float MaxCritChance = BalanceConstants.MaxCritChance;
    public static readonly Key[] SlotKeys = { Key.Q, Key.W, Key.E, Key.R }; // 슬롯 순서 — GameInput이 패드 버튼을 이 위치로 고른다
    private const float PressBuffer = GlobalCooldown; // 누른 시전 요청을 살려 두는 시간(게임 초) — Update의 한 번 누름 주석

    // 🔴 미니 회오리 피해는 **그 미니를 낳은 스킬 피해의 30%** 다(사용자 결정 2026-09-28).
    //    회오리 R0에서 나온 미니는 회오리 피해의 30%, 독수리 R1에서 나온 미니는 독수리 피해의 30%다.
    //    종전엔 두 스킬이 `MiniWhirlwindDamageBonus`라는 공유 보너스로 서로를 증폭했고 차수별 배율(0.25·0.35)도 달랐다 —
    //    그래서 미니 한 개의 피해를 알려면 두 스킬의 진화 상태를 같이 봐야 했다. 지우고 이 상수 하나로 모았다.
    public const float MiniWhirlwindDamageRatio = 0.3f;
    private const float WhirlwindMainSpacing = 1.8f;   // R0 본체 회오리끼리의 가로 간격(유닛)

    // ── 포도(독성 포도알) ────────────────────────────────────────────────────
    // 진화로 켜지는 것만 static이다. **Enemy가 중독 틱 안에서 읽어야 해서** — 적은 어느 스킬이
    // 자기를 중독시켰는지 모른다. 나머지(안개 크기·포도알 수·도트 간격)는 skill.PathTier를
    // FireGrapeToss가 실시간으로 읽는다.
    public static bool GrapePoisonExplodeOnDeath;    // 생화학 2차: 중독사 시 폭발
    public static float GrapeExplodeRadiusMult = 1f; // 그 폭발 반경에 곱하는 레벨업 "크기"(안개와 같이 커진다)
    public static int GrapeStunEveryNPoisonTicks;    // 찌릿찌릿: N번째 중독 피해마다 기절 (0=없음)
    public static bool GrapeStunAppliesVulnerable;   // 찌릿찌릿 2차: 기절 뒤 취약까지

    public const float GrapePoisonDuration = 3f;     // 중독이 몸에 남는 시간(안개 밖으로 나가도 이만큼 아프다)
    public const float GrapePoisonInterval = 0.5f;   // 도트 간격 → 기본 6틱
    public const float GrapeCloudDuration = 5.5f;    // 안개가 바닥에 깔려 있는 시간(2026-09-27 사용자: 늘려 달라 → 4→5.5)
    public const float GrapeCloudRadius = 1.5f;      // 안개 반경(유닛). skill.Scale이 곱해진다
    // 🔴 알끼리 이만큼은 떨어뜨려 던진다 — **포도 캐릭터 키**(Grape1.png 48px ÷ PPU32 × 프리팹 1.5배)다
    //    (사용자 지시 2026-09-27: "적이 없는 곳이어도 괜찮으니 최대한 적과 가까우면서 서로 떨어지게").
    //    이 값은 **기본 반경(GrapeCloudRadius)에서의** 간격이고, 안개가 커지면 같은 비율로 벌어진다
    //    (2026-09-30 사용자: 안개끼리 점점 겹쳐 보인다). 화면 밖으로 흩어지는 건 ClampGrapeSpot이 막는다.
    public const float GrapeSpotMinGap = 2.25f;
    public const int GrapeBaseBalls = 3;             // 한 번에 던지는 포도알 수
    public const float GrapeFlightTime = 0.55f;
    // 착지 뒤에도 적은 계속 걸어온다 — 이만큼 더 앞에 깔아 적이 안개로 걸어 들어오게 한다(2026-09-20 사용자).
    public const float GrapeLeadDwell = 0.6f;
    public const float GrapeArcHeight = 2.2f;
    // 기절 가동률을 40%로 맞춘 값(2026-09-27 사용자). 찌릿찌릿 1차는 3틱마다 = 1.5초 주기라 0.6/1.5 = 40%.
    // 2차는 2틱마다(1.0초 주기)라 60%가 된다 — 2차가 더 센 것이 맞다고 보고 상수 하나로 둔다.
    public const float GrapeStunDuration = 0.6f;
    public const float GrapeStunVulnerableMult = 1.25f;
    public const float GrapeStunVulnerableDuration = 2f;
    public const float GrapeExplodeRadius = 2f;
    public const float GrapeExplodeDamageRatio = 0.35f; // 터진 적 최대체력 대비
    // 🔴 안개 색은 **진화 루트마다 다르다**(사용자 지시 2026-09-27). 알 그림이 루트마다 바뀌는 것과 짝을 이룬다.
    //    미진화 = 보라(Effect_GrapeBomb) · R0 생화학 = 초록(Sprite_GrapeBombGreen) · R1 찌릿찌릿 = 노랑(Effect_GrapeBombElectric)
    private static readonly Color GrapeCloudColor = new Color(0.42f, 0.16f, 0.55f, 1f);
    private static readonly Color GrapeCloudColorBio = new Color(0.30f, 0.62f, 0.20f, 1f);      // 생화학 초록
    private static readonly Color GrapeCloudColorElectric = new Color(0.88f, 0.80f, 0.18f, 1f); // 찌릿찌릿 노랑

    // 루트를 탄 쪽의 색. 두 루트를 동시에 탈 수 없어(진화는 한 루트만) 순서 판정으로 충분하다.
    private static Color GrapeCloudColorFor(EquippedSkill skill) =>
        skill.PathTier[1] >= 2 ? GrapeCloudColorBio
        : skill.PathTier[2] >= 2 ? GrapeCloudColorElectric
        : GrapeCloudColor;

    private const float MiniWhirlwindScale = 0.4f; // 미니 회오리 크기 배율(바닥선 보정 계산에도 쓰임)
    private const float MiniWhirlwindGroundBlend = 0.5f; // 바닥선 보정을 얼마나 먹일지. 1=큰 회오리와 바닥선 일치(너무 낮았다) / 0=보정 없음

    // 기본공격 공격당 타격횟수(멀티히트). 총 데미지는 유지한 채 N회로 쪼개 각각 크리를 개별 판정 → 메이플식 데미지 숫자. Enemy.TakeSkillHit가 읽음.
    public static int BasicAttackHits = BalanceConstants.BasicAttackBaseHits;

    // 스나이핑: 타겟 1명당 저격 횟수, 저격 간격
    private const int SnipingBaseShots = BalanceConstants.SnipingBaseShots;
    private const int HomingBaseMissiles = BalanceConstants.HomingBaseMissiles;
    private const int EagleBaseDrops = BalanceConstants.EagleBaseDrops;
    private const int ShotgunBasePellets = BalanceConstants.ShotgunBasePellets;
    private const float SnipingShotInterval = BalanceConstants.SnipingShotInterval;

    // 산탄(타수) 버프 — 지속시간 동안 **모든 공격**의 타수를 올린다.
    // 🔴 예전엔 R0(보너스 탄환 장착) 루트가 "최고 공격력 스킬 **하나**에만" 몰아줬다. 2026-09-08에 문구가
    //    "모든 공격에 추가 타수"로 확정되면서 단일 지정(shotgunSingleTarget/shotgunTargetSkill)을 걷어냈다.
    private static float shotgunTimer;
    private static int shotgunBonus;

    // 되감기 R0: 다음에 사용하는 스킬의 피해를 1회 증가시키는 보너스(ComputeBaseDamage가 소비)
    private static float nextSkillDamageBonus;
    // 되감기 R0 2차 「과충전」: 다음 스킬에 얹히는 추가 타수. 소비되면 아래 overcharge* 로 옮겨 간다.
    private static int nextSkillBonusHits;
    private static ActiveSkillId overchargeSkill;
    // 봇 관측용(BotCastPolicy) — 충전 되감기·과충전이 걸어 둔 "다음 스킬 강화"가 아직 소비되지 않고 남아 있는가.
    // 읽기 전용 창구라 게임 동작에는 영향이 없다(BotInput의 관측 원칙과 같다).
    public static bool EmpowerPending => nextSkillDamageBonus > 0f || nextSkillBonusHits > 0;
    private static float overchargeHitsTimer;
    private const float OverchargeWindow = 2.5f;   // 한 캐스트의 타격이 다 끝날 만큼만 — 다음 캐스트로 안 샌다

    // 진화가 정한 타수. 0 = "이 진화는 타수를 안 정한다"라 기본 스킬 값으로 떨어진다.
    // 🔴 진화 에셋의 `baseHits`는 여기 담겨야 실제로 먹는다 — 에셋에 적어 두기만 하면 조용히 무시된다.
    //    타수는 `NaturalHits`(static)가 읽는데 그쪽은 EquippedSkill을 못 보므로 스킬별 static으로 올린다.
    private static readonly System.Collections.Generic.Dictionary<ActiveSkillId, int> evolvedHits =
        new System.Collections.Generic.Dictionary<ActiveSkillId, int>();

    // Enemy.TakeSkillHit가 참조: 스킬 고유 타수(진화 > 기본 스킬 에셋 > 기본공격 특례 > 1)
    public static int NaturalHits(ActiveSkillId source) =>
        evolvedHits.TryGetValue(source, out int h) && h > 0 ? h
        : Prog(source) != null && Prog(source).baseHits > 0 ? Prog(source).baseHits
        : source == ActiveSkillId.BasicAttack ? Mathf.Max(1, BasicAttackHits) : 1;

    // Enemy.TakeSkillHit가 참조: 산탄 버프로 추가되는 타격 수(스킬을 안 가린다) + 과충전이 얹은 1회분
    public static int GlobalBonusHits(ActiveSkillId source)
    {
        int hits = shotgunTimer > 0f ? shotgunBonus : 0;
        if (overchargeHitsTimer > 0f && source == overchargeSkill) hits += RewindOverchargeBonusHits;
        return hits;
    }

    private static PlayerSkills instance; // 근거리 판정 등 정적 메서드가 플레이어 위치를 참조하기 위한 인스턴스

    // ── 스킬트리 강화 노드의 경계값 (2026-09-03 재설계) ──
    // ⚠️ 둘 사이(4초 초과 ~ 5초 미만)인 스킬은 힘·가속 강화를 **둘 다 못 받는다.** 값을 고칠 땐 같이 볼 것.
    private const float StrengthDoubleCooldown = 5f;    // 힘 강화: 이 쿨 **이상**이면 힘 효과 2배
    private const float AccelFastSkillCooldown = 4f;    // 가속 강화: 이 쿨 **이하**면 피해 증가
    private const float AccelFastSkillDamageBonus = 0.3f;

    // 버프류 스킬 = 지속시간 버프를 부여하는 스킬. **산탄은 R0(보너스 탄환 장착) 루트를 탔을 때만** 해당한다 —
    // 미진화 산탄과 R1(메카 버스터)은 순수 공격기라 버프를 아예 안 건다(FireShotgun의 PathTier[1] 게이트와 같은 조건).
    // 그래서 id 만으로는 못 가른다. 루트를 들고 있는 EquippedSkill 을 받는다.
    public static bool IsBuffSkill(EquippedSkill skill) =>
        skill.Id == ActiveSkillId.Lightning
        || (skill.Id == ActiveSkillId.Shotgun && skill.PathTier[1] >= 1);

    // 쿨 초기화(가속 R0 「리프레쉬」)가 발동될 때 — HUD가 구독해 가속 패시브 아이콘에 보잉 연출
    public static System.Action OnRefreshProc;

    [SerializeField] private GameObject basicAttackProjectilePrefab;
    // 화살 R0(암살 연계, path1) = 관통 무한 **큰 화살 한 발**. 그 한 발만 전용 그림으로 갈아끼운다.
    // 1차(T2)=검은 화살 / 2차(T3)=블루베리 사냥꾼.
    // 진화한 화살 그림(1차 = 검은 화살).
    // 🔴 `Effect_ArrowR1`·`Effect_ArrowR2`는 **둘 다 1차 진화 화살이고, 한 애니메이션의 1·2프레임**이다.
    //    티어가 아니다 — 태리미의 작화 도구가 R1로 저장하면 다음 프레임을 R2로 자동 명명한다.
    //    파일을 자르는 것도 아니다(각 파일이 통짜 64x32 한 장). 한 장만 꽂으면 정지 그림이 된다.
    //    ⚠️ `Icon_*R1/R2`는 **루트**라 같은 접미사가 여기선 다른 뜻이다. 새 그림을 받으면 한 번 물을 것.
    [SerializeField] private Sprite[] evolvedArrowFrames;
    // 2차 진화(블루베리 사냥꾼)의 **정면 큰 화살** 전용 그림. **비어 있으면 위 프레임을 그대로 쓴다.**
    // (2026-09-19 현재 전용 그림 없음 — 2차가 1차 그림을 재사용한다. 추격 화살은 아래 별도 필드다.)
    [SerializeField] private Sprite[] evolvedArrowFramesTier2;
    [SerializeField] private float evolvedArrowFps = 12f;
    // 하늘파쇄기 세트의 날렵한 화살 그림(`Assets/Sprites/하늘파쇄기/Effect_BasicAttack.png`).
    // 🔴 **두 곳이 같은 그림을 쓴다**(2026-09-19 사용자 지시):
    //    ① R0 2차 「블루베리 사냥꾼」의 **추격 화살**  ② R1 2차 「하늘 파쇄기」의 **화살비 화살**
    //    ⚠️ 루트의 `Effect_BasicAttack.png`와 **파일명이 같고 GUID가 다른 별개 파일**이다. 섞지 말 것.
    //    미배선이면 둘 다 원본 화살 그림으로 그대로 날아간다(폴백).
    [SerializeField] private Sprite skyShredderArrowSprite;
    // R1 2차 「하늘 파쇄기」의 우주선(`Assets/Sprites/하늘파쇄기/skyshredder_1~4`, 4프레임 플립북).
    // 화면 상단에 **붙박이로** 뜬다 — 미배선이면 배만 안 보이고 화살비는 그대로 돈다.
    [SerializeField] private GameObject skyShredderShipPrefab;
    [SerializeField] private GameObject whirlwindPrefab;
    [SerializeField] private GameObject miniWhirlwindPrefab; // 미니 회오리 전용 그림(Effect_MiniTornado). 미배선이면 본체를 축소해 쓴다(도트가 뭉개짐)
    [SerializeField] private GameObject lightningRodPrefab;  // 피뢰침 기둥(Effect_LightningRod — 테슬라 코일). 미배선이면 임시 프리미티브로 폴백
    [SerializeField] private GameObject zeusStatuePrefab;    // 2차 「제우스의 은총」의 제우스상(Effect_ZeusStatue). 미배선이면 1차 기둥을 키워 쓴다
    [SerializeField] private GameObject bigThunderVfxPrefab; // 피뢰침이 유도하는 큰 낙뢰(Effect_BigThunder) — 틱마다 기둥 꼭대기에 내리친다
    // R0 2차 「초대형 축적 번개」의 번개(`Assets/Sprites/개큰번개/Effect_BigThunder_1~6`).
    // ⚠️ 루트의 `Effect_BigThunder1~5`(피뢰침용, 밑줄 없음)와 **다른 그림**이다. 섞지 말 것.
    // 미배선이면 초대형 번개가 아예 발동하지 않는다(LightningStorm.TryConsumeHugeBolt이 false).
    [SerializeField] private GameObject hugeBoltVfxPrefab;
    // 되감기 표식 — 시전할 때 머리 위에 한 번 떴다 사라진다.
    [SerializeField] private GameObject rewindVfxPrefab;       // 진화 전 기본(Effect_Rewind)
    [SerializeField] private GameObject rewindRoute2VfxPrefab; // R0 충전 되감기 = 다음 스킬 피해(Effect_RewindYellow)
    [SerializeField] private GameObject rockDebrisPrefab;      // 휘두르기에 맞은 적한테서 튀는 돌조각(Particle_Rock)
    // 파인애플 망치는 휘두르기 진화 루트에 따라 그림이 바뀐다(몸 트랙은 그대로, Hammer 자식 트랙만 교체하는 오버라이드).
    [SerializeField] private RuntimeAnimatorController bigHammerController;   // R0 1차 = 쓸어치기
    [SerializeField] private RuntimeAnimatorController shockHammerController; // R1 1차 = 지진파
    // R0 2차 「로열 팔라딘의 망치」. 미배선이면 1차(쓸어치기) 망치 그림 그대로 간다.
    [SerializeField] private RuntimeAnimatorController paladinHammerController;
    // 팔라딘 그림이 1차 그림의 2.05배(308x187 vs 150x96)라 생기는 보정 ÷ 그 위에 얹는 의도 배율 1.25.
    // 즉 화면에서는 1차 망치의 약 1.25배로 보인다. 그림을 다시 그리면 이 값만 1로 되돌리면 된다.
    private const float PaladinHammerArtComp = 1.25f / 2.05f;
    // 망치 그림이 커지는 정도 = 범위 성장분의 이 배수(2026-09-27 사용자). 1이면 종전과 같다.
    // ⚠️ 판정(SwingReach × mult)과 그림자는 **안 건드린다** — 그림만 범위 성장에 맞춰 더 키우는 값이다.
    private const float HammerGrowthGain = 1.5f;
    [SerializeField] private GameObject bigTornadoPrefab;
    // ── 2차 진화 전용 그림(2026-09-19). 전부 **미배선이면 1차 그림으로 떨어진다** — 판정은 그대로 돈다. ──
    [SerializeField] private GameObject skyWailTornadoPrefab; // 회오리 R1 2차 「하늘의 울음」(Effect_SuperTornado)
    [SerializeField] private GameObject hugeOrbPrefab;        // 오브 R0 2차 「초대형 오브」(Effect_HugeOrb)
    [SerializeField] private GameObject jugglerOrbPrefab;     // 오브 R1 2차 「저글러」(Effect_Juggler)
    [SerializeField] private GameObject giantWavePrefab;      // 휘두르기 R1 2차 「거대한 파도」(바다망치/Effect_Wave)
    [SerializeField] private GameObject orbPrefab;
    [SerializeField] private GameObject bigOrbPrefab; // 지식 연계 path1 T2부터 등장하는 큰 초록 오브 비주얼
    [SerializeField] private GameObject shotgunPelletPrefab; // 산탄 알(SmallOrb_Skill 재사용 — 방향성 단발 투사체)
    [SerializeField] private GameObject scatterPelletPrefab;  // 산탄 알 전용 그림(Effect_Scatter 플립북). 미배선이면 shotgunPelletPrefab으로 폴백
    [SerializeField] private GameObject scatterFireVfxPrefab; // 산탄을 뿜을 때 알 뒤에 남는 화약/불꽃(Effect_ScatterFire). 볼리마다 1개
    // R1 2차 「초강력 섬멸용 전탄발사」의 **기계**(FIRE!!!/fullburst_1~12). 총구 화염 자리를 이 그림이 대신한다.
    // 미배선이면 1차와 같은 화염으로 떨어진다.
    [SerializeField] private GameObject fullBurstVfxPrefab;
    [SerializeField] private GameObject eagleDropPrefab;
    [SerializeField] private GameObject eagleImpactVfxPrefab;
    // 폭탄 독수리(R0 1차)의 폭발 — 호밍 미사일이 쓰는 것과 **같은 에셋**(Effect_Explosion)을 배선한다.
    // 안 배선돼 있으면 착탄 연출(eagleImpactVfxPrefab)로 떨어진다 — 판정은 그대로 돌고 그림만 수수해진다.
    [SerializeField] private GameObject eagleBombVfxPrefab;
    [SerializeField] private GameObject snipingEffectPrefab;      // 스나이핑 후속 타격 VFX(Effect_Sniping, 2~5번째 저격)
    [SerializeField] private GameObject snipingSplashPrefab;      // 스나이핑 첫 타격 강조 VFX(Effect_SplashSniping — 기본 이펙트 화려 버전)
    [SerializeField] private GameObject homingMissilePrefab;      // 호밍 미사일 프리팹(추적)
    [SerializeField] private GameObject swingShockwavePrefab;     // 휘두르기 2루트 진화: 맵 끝까지 달리는 충격파
    // 포도알 그림은 기본 오브를 빌려 쓴다(전용 이펙트 없음 — 사용자 결정). 미배선이면 orbPrefab의 그림으로 폴백.
    [SerializeField] private GameObject grapeBallPrefab;
    [SerializeField] private Animator animator;
    [SerializeField] private SkillProgression[] progressions; // 스킬별 시작값+레벨 커브(Tier A). 미할당/미포함 스킬은 코드 기본 규칙 폴백(=현행)

    // 정적 GetDefault*/Apply/Describe가 인스턴스 필드를 못 읽으므로 Awake에서 static 조회맵으로 승격.
    private static System.Collections.Generic.Dictionary<ActiveSkillId, SkillProgression> progressionLookup;
    private static SkillProgression Prog(ActiveSkillId id) =>
        progressionLookup != null && progressionLookup.TryGetValue(id, out var p) ? p : null;

    [SerializeField] private EvolutionProgression[] evolutions; // 진화별(스킬×루트×티어) 시작값+커브. 비어 있으면 현행 폴백

    // 키는 (스킬, 루트, 진화차수). 차수는 화면 기준 1·2다 — PathTier가 아니다.
    private static System.Collections.Generic.Dictionary<(ActiveSkillId, int, int), EvolutionProgression> evolutionLookup;
    private static EvolutionProgression EvoProg(EquippedSkill s) =>
        s != null && s.EvolutionStage > 0 && evolutionLookup != null
        && evolutionLookup.TryGetValue((s.Id, s.Route, s.EvolutionStage), out var e) ? e : null;

    // 🔴 스킬 효과음 배선은 **`Assets/Resources/SkillSfxLibrary.asset`이 소유한다**(2026-09-27 사용자).
    //    종전엔 여기 인스펙터에 클립 5개가 꽂혀 있었다 — 슬롯마다 후보 4개를 담고 들어 보고 고르려면
    //    씬 인스펙터로는 안 되기 때문에 SO로 옮겼다. 고르는 창은 `Window > Blueberry Defense > 스킬 효과음 고르기`.
    //    울릴 때는 `SkillSfx.Play("<슬롯 id>")` 한 줄이다(스로틀·볼륨은 그 안에서 처리).

    private readonly List<EquippedSkill> equippedSkills = new List<EquippedSkill>();
    private float globalCooldownTimer;
    // 스테이지 시작 잠금(사용자 결정 2026-10-07) — 판 시작과 스테이지 전환(쿨타임 초기화) 직후 이 시간 동안 스킬이 나가지 않는다.
    // 수동·자동 시전 둘 다 막는다. 상태형 진화(하늘 파쇄기 등)는 시전이 아니라서 계속 돈다.
    private const float StageStartLock = 0.5f;
    private float stageStartLockTimer = StageStartLock;
    public bool IsStageStartLocked => stageStartLockTimer > 0f; // HUD가 자동 시전 전용 스킬과 같은 마스크로 덮는다
    private float passiveDamageMultiplier = 1f;
    private float strengthDamageBonus;  // 위 배율 중 **힘 패시브가 얹은 몫**만 따로(스킬트리 "힘" 강화가 이 몫만 2배로 쓴다)
    private PlayerPassives passives;
    private PlayerHealth health;

    public bool HasMaxSkills => equippedSkills.Count >= SlotKeys.Length;
    public IReadOnlyList<EquippedSkill> EquippedSkills => equippedSkills;
    public float GlobalCooldownRatio => Mathf.Clamp01(globalCooldownTimer / GlobalCooldown);

    private void Awake()
    {
        instance = this;
        passives = GetComponent<PlayerPassives>();
        health = GetComponent<PlayerHealth>();
        BuildProgressionLookup(); // 정적 조회맵 승격. AcquireSkill 전에 세팅해야 기본 수치가 채워짐
        ResetRunState();          // static 상태 — 판 시작 시 초기화(도메인 리로드 없이도)
        // 시작 스킬은 선택된 캐릭터에서(없으면 기본공격 = 현행). RunConfig 직접 참조라 RunBootstrap 순서에 무의존.
        AcquireSkill(RunConfig.Character != null ? RunConfig.Character.startingSkill : ActiveSkillId.BasicAttack);
        LightningStorm.OnProc += HandleThunderCooldown;
        // 스나이핑 R1 2차 「사이버네틱 벙커」 — 피격에 반응한다. 방어 R0 「망치 반격」과 같은 훅을 쓴다.
        if (health != null) health.OnDamageTaken += HandleBunkerRetaliation;
    }

    private void OnDestroy()
    {
        LightningStorm.OnProc -= HandleThunderCooldown;
        if (health != null) health.OnDamageTaken -= HandleBunkerRetaliation;
    }

    // 이 판에서만 유효한 static 상태 초기화 (RunState에서도 호출)
    // 엔딩 연출 중 스킬 봉인(EndingSequence가 켜고 끈다). 켜져 있으면 쿨도 흐르지 않는다.
    public static bool Sealed;

    public static void ResetRunState()
    {
        Sealed = false;
        GrapePoisonExplodeOnDeath = false;
        GrapeExplodeRadiusMult = 1f;
        GrapeStunEveryNPoisonTicks = 0;
        GrapeStunAppliesVulnerable = false;
        evolvedHits.Clear(); // 진화가 정한 타수는 이번 판 한정이다

        BasicAttackHits = BalanceConstants.BasicAttackBaseHits;
        shotgunTimer = 0f;
        shotgunBonus = 0;
        nextSkillDamageBonus = 0f;
        nextSkillBonusHits = 0;
        overchargeCooldownMult = RewindOverchargeCooldownMult;
        GlobalEnemySlowMult = 1f;
        // ⚠️ 타이머까지 지워야 한다 — 판이 끝나면 Update가 멈춰 값이 그대로 남고,
        //    다음 판 첫 2.5초 동안 과충전 타수가 공짜로 얹힌다.
        overchargeHitsTimer = 0f;
    }

    // 직렬화된 progressions[]를 id→SO 조회맵으로 승격(정적 메서드용). 미포함 스킬은 조회 실패 → 코드 기본 규칙 폴백.
    private void BuildProgressionLookup()
    {
        progressionLookup = new System.Collections.Generic.Dictionary<ActiveSkillId, SkillProgression>();
        if (progressions != null)
            foreach (var p in progressions)
                if (p != null) progressionLookup[p.skill] = p;

        evolutionLookup = new System.Collections.Generic.Dictionary<(ActiveSkillId, int, int), EvolutionProgression>();
        if (evolutions != null)
            foreach (var e in evolutions)
                if (e != null) evolutionLookup[(e.skill, e.route, e.stage)] = e;
    }

    // 스킬트리 "낙뢰 쿨타임 감소": 낙뢰가 칠 때마다 낙뢰 스킬 쿨타임을 조금씩 당긴다.
    private void HandleThunderCooldown()
    {
        if (MetaBonuses.ThunderCooldownPerStrike <= 0f) return;
        EquippedSkill lightning = equippedSkills.FirstOrDefault(s => s.Id == ActiveSkillId.Lightning);
        if (lightning != null)
            lightning.CooldownTimer = Mathf.Max(0f, lightning.CooldownTimer - MetaBonuses.ThunderCooldownPerStrike);
    }

    public float StrengthDamageBonus => strengthDamageBonus; // ESC 요약용 — 힘 패시브가 올린 몫만(스킬트리 공격력 노드 제외)

    public void IncreaseDamageMultiplier(float amount)
    {
        passiveDamageMultiplier += amount;
    }

    // 힘 패시브가 얹은 몫만 따로 기억한다 — 스킬트리 "힘: 쿨 5초 이상 스킬에 2배"가
    // **힘의 몫만** 한 번 더 얹기 때문(스킬트리 공격력 노드까지 2배가 되면 안 된다).
    public void IncreaseStrengthDamage(float amount)
    {
        strengthDamageBonus += amount;
        passiveDamageMultiplier += amount;
    }

    private void Update()
    {
        // 레벨업·보물·진화·ESC 창이 떠 있는 동안엔 스킬이 나가지 않는다.
        // Update는 timeScale 0에도 계속 돌아서, 쿨이 차 있던 스킬이 카드 뒤에서 발동돼 버렸다
        // (자동시전 스나이핑도 같은 이유로 매 프레임 나갔다).
        if (ModalPause.IsPaused || Sealed) return;

        globalCooldownTimer -= Time.deltaTime;
        if (shotgunTimer > 0f) shotgunTimer -= Time.deltaTime;
        if (overchargeHitsTimer > 0f) overchargeHitsTimer -= Time.deltaTime;

        foreach (EquippedSkill skill in equippedSkills)
        {
            skill.CooldownTimer -= Time.deltaTime;
            if (skill.CooldownTimer <= 0f && skill.ReadySince < 0f) skill.ReadySince = Time.time;

            // 스나이핑 R1 「자동 방어 시스템」(path2 T2+): 수동 사용 불가, 쿨타임마다 자동 시전
            // 🔴 상태형 진화(하늘 파쇄기·절멸의 시간)는 여기서 빠진다 — 쿨타임으로 시전하는 것이 아니다.
            if (IsAutoCastOnly(skill) && !IsPassiveState(skill)
                && skill.CooldownTimer <= 0f && globalCooldownTimer <= 0f && stageStartLockTimer <= 0f)
                TryUseSkill(skill);
        }

        UpdateSkyShredderRain();

        if (stageStartLockTimer > 0f) stageStartLockTimer -= Time.deltaTime;

        // 🔴 한 번 누름 = 한 번 시전이다. 꾹 누르고 있어도 다시 나가지 않는다(사용자 결정 2026-10-01 — 예전엔 쿨마다 재발동했다).
        //    누른 입력은 PressBuffer 동안 살려 둔다. 전역 쿨(0.4초) 중에 누른 키를 버리면 Q 직후 W를 누를 때 W가 씹힌다 —
        //    꾹 누르기가 있을 땐 그 빈틈을 누르기가 메웠다. 대상이 없어 못 나간 경우(스나이핑)도 이 시간 동안 다시 시도한다.
        if (!BotInput.HoldSkills)
            foreach (EquippedSkill skill in equippedSkills)
                if (GameInput.SkillPressed(skill.Key)) skill.PressedUntil = Time.time + PressBuffer;

        // 🔴 발동 순서 = **오래 기다린 스킬 먼저**(사용자 결정 2026-09-18). 예전엔 슬롯 순서(Q→R)라,
        //    쿨이 전역 쿨(0.4초)보다 짧아진 스킬이 매 창을 가져가 뒤 슬롯이 거의 안 나갔다
        //    (풀트리 파인애플: 휘두르기 0.33초 → 독수리·회오리·산탄이 가능 횟수의 약 10%만 발동).
        //    동시에 준비됐으면(같은 ReadySince) 목록 순서 = 슬롯 순서로 갈린다.
        if (globalCooldownTimer > 0f || stageStartLockTimer > 0f) return;
        castQueue.Clear();
        foreach (EquippedSkill skill in equippedSkills)
        {
            if (IsAutoCastOnly(skill) || skill.CooldownTimer > 0f) continue;
            // 사람 = 눌러 둔 요청이 살아 있는 스킬. BotInput.HoldSkills = 봇 플레이테스트의 "QWER 꾹"(평소 false) —
            // 봇은 쿨이 차는 즉시 쏜다(쿨마다 한 번씩 정확히 누르는 사람과 같다). 봇일 때만 CastGate(스킬별 발사 허가, BotCastPolicy)를 본다.
            if (BotInput.HoldSkills ? (BotInput.CastGate == null || BotInput.CastGate(skill))
                                    : skill.PressedUntil >= Time.time) castQueue.Add(skill);
        }
        while (castQueue.Count > 0)
        {
            int pick = 0;
            for (int i = 1; i < castQueue.Count; i++)
                if (castQueue[i].ReadySince < castQueue[pick].ReadySince) pick = i;
            EquippedSkill next = castQueue[pick];
            castQueue.RemoveAt(pick);
            TryUseSkill(next); // 대상이 없어 실패하면(스나이핑 등) 전역 쿨이 안 걸려 다음 후보로 넘어간다
            if (next.ReadySince < 0f) next.PressedUntil = -1f; // 나갔다(TryUseSkill이 성공하면 ReadySince를 -1로 돌린다) — 요청 소모
            if (globalCooldownTimer > 0f) break; // 전역 쿨을 거는 스킬이 나갔으면 이번 프레임은 끝(되감기 무전역쿨은 이어서 쏜다)
        }
    }

    private readonly List<EquippedSkill> castQueue = new List<EquippedSkill>(4);

    // 수동 시전이 막히고 자동으로만 나가는 스킬. HUD가 쿨타임 마스크를 계속 씌워 표시한다(HUDController.cs:376).
    //   · 스나이핑 R1(path2) 1차 「자동 방어 시스템」부터
    //   · 화살 R1(path2) 2차 「하늘 파쇄기」 — 2026-09-19 사용자 지시로 **누를 수 없게 되는 대신**
    //     우주선이 하늘에 떠서 화살비를 계속 내린다.
    //     🔴 이 진화는 쿨타임으로 자동 시전하지 않고 **상시 상태**로 돈다(IsContinuousRain · 2026-09-28).
    //        종전엔 쿨 6초에 비가 1초라 5초 동안 아무것도 내리지 않았다.
    public static bool IsAutoCastOnly(EquippedSkill skill) =>
        (skill.Id == ActiveSkillId.Sniping && skill.PathTier[2] >= 2)
        || (skill.Id == ActiveSkillId.BasicAttack && skill.PathTier[2] >= 3)
        || (skill.Id == ActiveSkillId.Rewind && skill.PathTier[2] >= 3);

    // 누를 수도 없고 자동 시전도 하지 않는다 — **가진 것만으로 효과가 나는** 상태형 진화다.
    // IsAutoCastOnly는 "쿨마다 저절로 나간다"까지 포함하므로 그것만으로는 이 둘을 표현할 수 없다.
    //   · 하늘 파쇄기(화살 R1 2차) — 화살이 상시로 내린다(UpdateSkyShredderRain)
    //   · 블루베리 절멸의 시간(되감기 R1 2차) — 쿨감과 전역 쿨 소멸이 상시로 걸린다.
    //     🔴 액티브 능력(쿨 앞당김)은 **봉인**된다(사용자 결정 2026-09-28) — 그 대가로 쿨감을 50%로 올렸다.
    public static bool IsPassiveState(EquippedSkill skill) =>
        IsContinuousRain(skill)
        || (skill.Id == ActiveSkillId.Rewind && skill.PathTier[2] >= 3);

    public bool HasSkill(ActiveSkillId id) => equippedSkills.Any(s => s.Id == id);

    public void AcquireSkill(ActiveSkillId id)
    {
        if (HasMaxSkills || HasSkill(id)) return;

        equippedSkills.Add(new EquippedSkill
        {
            Id = id,
            Key = SlotKeys[equippedSkills.Count],
            Cooldown = GetDefaultCooldown(id),
            Damage = GetDefaultDamage(id),
            // 시작 관통·투사체 수도 에셋이 정한다(둘 다 기본 0이라 값을 안 넣으면 지금과 같다).
            ExtraPierce = GetBasePierce(id),
            ExtraProjectiles = GetBaseProjectiles(id),
        });
        CollectionSave.DiscoverActive(id); // 컬렉션(도감) 발견 기록 — 판을 넘어 남는다
        if (HasMaxSkills) Achievements.OnActiveSlotsFull();
    }

    // 만렙(MaxSkillLevel)에 닿으면 더 이상 레벨업 후보로 뜨지 않는다 — 진화해서 Lv.1로 리셋해야 다시 큰다.
    public bool CanUpgradeSkill(EquippedSkill skill) => skill != null && skill.Level < BalanceConstants.MaxSkillLevel;

    public void UpgradeSkillLevel(ActiveSkillId id)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null || skill.Level >= BalanceConstants.MaxSkillLevel) return;

        skill.Level++;
        skill.TotalLevel++;
        ApplyUpgradeEffect(skill, skill.Level);
        RefreshGlobalEnemySlow(); // 「블루베리 절멸의 시간」의 적 둔화는 판 전체 값이라 레벨업 뒤에 다시 센다
        if (skill.Level >= BalanceConstants.MaxSkillLevel) Achievements.OnSkillMaxLevel();
    }

    // 스킬트리 메타: 특정 스킬을 시작부터 지정 레벨로(진화 게이트 무시). 레벨업 효과를 순서대로 적용.
    public void SetSkillStartLevel(ActiveSkillId id, int targetLevel)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null) return;
        targetLevel = Mathf.Min(targetLevel, BalanceConstants.MaxSkillLevel);
        while (skill.Level < targetLevel)
        {
            skill.Level++;
            skill.TotalLevel++;
            ApplyUpgradeEffect(skill, skill.Level);
        }
        RefreshGlobalEnemySlow();
    }

    // 레벨업 강화는 스킬별 SkillProgression(SO)이 정의한다. 미할당 스킬은 코드 기본 규칙(SkillProgression.DefaultStep=현행)으로 폴백.
    private static LevelUpStep StepFor(EquippedSkill skill, int level)
    {
        // 진화한 스킬은 자기 커브(Evo_*)를 먼저 본다. 그 에셋의 커브가 비어 있으면 원래 스킬 커브로 떨어진다 —
        // 진화 뒤에도 표시 레벨이 1부터 다시 오르므로 두 커브가 같은 level 값을 쓴다.
        LevelUpStep evoStep = EvoProg(skill)?.StepForLevel(level);
        if (evoStep != null) return evoStep;

        SkillProgression p = Prog(skill.Id);
        return p != null ? p.StepForLevel(level) : SkillProgression.DefaultStep(skill.Id, level);
    }

    private static void ApplyUpgradeEffect(EquippedSkill skill, int level)
    {
        ApplyStep(skill, StepFor(skill, level));
    }

    private static float Op(float cur, LevelUpStep s) => s.op == StatOp.Multiply ? cur * s.amount : cur + s.amount;

    private static void ApplyStep(EquippedSkill skill, LevelUpStep s)
    {
        switch (s.stat)
        {
            case SkillStat.Damage: skill.Damage = Op(skill.Damage, s); break;
            case SkillStat.Cooldown: skill.Cooldown = Mathf.Max(GlobalCooldown, Op(skill.Cooldown, s)); break;
            case SkillStat.ProjectileSpeed: skill.ProjectileSpeedMultiplier = Op(skill.ProjectileSpeedMultiplier, s); break;
            case SkillStat.Pierce: skill.ExtraPierce = Mathf.RoundToInt(Op(skill.ExtraPierce, s)); break;
            case SkillStat.ProjectileCount: skill.ExtraProjectiles = Mathf.RoundToInt(Op(skill.ExtraProjectiles, s)); break;
            case SkillStat.ProcChance: skill.ProcChanceBonus = Op(skill.ProcChanceBonus, s); break;
            // 같은 "지속시간" 스텝이라도 붙는 곳은 스킬마다 다르다(카드 문구는 둘 다 "지속시간 N초 증가"라 거짓말이 아니다).
            case SkillStat.Duration:
                if (skill.Id == ActiveSkillId.Shotgun) skill.ExtraShotgunDuration = Op(skill.ExtraShotgunDuration, s);
                else skill.ExtraWhirlwindDuration = Op(skill.ExtraWhirlwindDuration, s);
                break;
            case SkillStat.Scale: skill.Scale = Op(skill.Scale, s); break;
            case SkillStat.RewindAmount: skill.RewindAmount = Op(skill.RewindAmount, s); break;
            case SkillStat.TickRate: skill.TickIntervalMult = Mathf.Max(0.15f, Op(skill.TickIntervalMult, s)); break;
            case SkillStat.MaxTargets: skill.ExtraTargets = Mathf.RoundToInt(Op(skill.ExtraTargets, s)); break;
            case SkillStat.VolleyCount: skill.ExtraVolleys = Mathf.RoundToInt(Op(skill.ExtraVolleys, s)); break;
            case SkillStat.InstallDuration: skill.ExtraInstallDuration = Op(skill.ExtraInstallDuration, s); break;
            case SkillStat.MaxStacks: skill.ExtraMaxStacks = Mathf.RoundToInt(Op(skill.ExtraMaxStacks, s)); break;
            case SkillStat.SubDamage: skill.SubDamageMult = Op(skill.SubDamageMult, s); break;
            case SkillStat.SubScale: skill.SubScaleMult = Op(skill.SubScaleMult, s); break;
            case SkillStat.BossDamage: skill.BossDamageBonus = Op(skill.BossDamageBonus, s); break;
            case SkillStat.BonusHits: skill.ExtraBonusHits = Mathf.RoundToInt(Op(skill.ExtraBonusHits, s)); break;
            case SkillStat.EmpowerBonus: skill.EmpowerMult = Op(skill.EmpowerMult, s); break;
            // 페널티 감소율은 1을 넘으면 쿨이 오히려 줄어드는 보너스가 되므로 상한을 둔다.
            case SkillStat.PenaltyReduction: skill.PenaltyReduction = Mathf.Clamp01(Op(skill.PenaltyReduction, s)); break;
            case SkillStat.AllCooldown: skill.AllCooldownBonus = Mathf.Clamp01(Op(skill.AllCooldownBonus, s)); break;
            case SkillStat.EnemySlow: skill.EnemySlowBonus = Mathf.Clamp01(Op(skill.EnemySlowBonus, s)); break;
            case SkillStat.StunDuration: skill.ExtraStunDuration = Op(skill.ExtraStunDuration, s); break;
            case SkillStat.StunInterval: skill.StunIntervalBonus = Mathf.RoundToInt(Op(skill.StunIntervalBonus, s)); break;
            case SkillStat.Lifesteal: skill.ExtraLifesteal = Mathf.RoundToInt(Op(skill.ExtraLifesteal, s)); break;
        }
    }

    // 스킬 상태를 통째로 넘긴다 — 같은 스텝이라도 스킬·진화에 따라 **읽히는 뜻이 다르기** 때문이다
    // (화살비는 웨이브 수, 스나이핑은 연사, 추적 오브는 개수 …). 안 맞추면 카드가 거짓말을 한다.
    // 🔴 진화 후 **아무 효과가 없는** 스텝은 문구로 가리지 말고 `Evo_*.levels`에서 다른 스텝으로 갈아끼운다(2026-09-18).
    public static string DescribeUpgradeEffect(EquippedSkill skill, int nextLevel) =>
        DescribeStep(StepFor(skill, nextLevel), skill);

    // 미리보기 텍스트를 스텝 데이터에서 생성 → 미리보기·실제 적용이 항상 일치. (Apply와 같은 StepFor 참조)
    // 개수에 **배수**가 붙는 진화(추적 오브·호밍 R1·메카 버스터)는 스텝 값이 아니라 발사 코드와 같은 함수로 센 **실제 증가량**을 적는다.
    private static string DescribeStep(LevelUpStep s, EquippedSkill skill)
    {
        ActiveSkillId id = skill.Id;
        switch (s.stat)
        {
            case SkillStat.Damage:
            {
                int dpct = Mathf.RoundToInt((s.amount - 1f) * 100f);
                // 무엇의 피해인지 적는다 — 본체가 화면에 안 나가는 진화가 있어서 "피해"만 쓰면 무엇이 세지는지 모른다.
                if (s.op == StatOp.Multiply)
                {
                    // 회오리 생성기는 기계가 뽑는 회오리가 유일한 피해원이다.
                    if (id == ActiveSkillId.Whirlwind && skill.PathTier[0] >= 3) return Loc.F("step.Damage.mul.whirlwind", dpct);
                    // 보너스 탄환 장착은 버프와 산탄을 같이 쓴다 — 오르는 쪽이 산탄이라는 것을 밝힌다.
                    if (id == ActiveSkillId.Shotgun && skill.PathTier[1] >= 2) return Loc.F("step.Damage.mul.shotgun", dpct);
                    return Loc.F("step.Damage.mul", dpct);
                }
                return Loc.F("step.Damage.add", s.amount.ToString("0.##"));
            }
            case SkillStat.Cooldown:
                return s.op == StatOp.Multiply
                    ? Loc.F("step.Cooldown.mul", Mathf.RoundToInt((1f - s.amount) * 100f))
                    : Loc.F("step.Cooldown.add", (-s.amount).ToString("0.##"));
            case SkillStat.ProjectileSpeed: return Loc.F("step.ProjectileSpeed", Mathf.RoundToInt(s.amount * 100f));
            case SkillStat.Pierce: return Loc.F("step.Pierce", Mathf.RoundToInt(s.amount));
            case SkillStat.ProjectileCount:
            {
                int newExtra = Mathf.RoundToInt(Op(skill.ExtraProjectiles, s));
                // 화살비 진화는 정면 화살이 없어 발수로 쓸 데가 없다 — 이 스텝이 "비가 오는 시간"으로 읽힌다.
                // 하늘 파쇄기는 시전이 없어 "차례"가 없다 — 이 축이 한 차례에 쏟아지는 **화살 수**로 간다.
                if (IsContinuousRain(skill)) return Loc.F("step.ProjectileCount.skyshredder", Mathf.RoundToInt(s.amount));
                if (ArrowRainReplacesShot(skill)) return Loc.F("step.ProjectileCount.arrowrain", Mathf.RoundToInt(s.amount));
                switch (id)
                {
                    // 블루베리 사냥꾼: 치명타마다 따라붙는 화살 수(2026-09-29 사용자).
                    case ActiveSkillId.BasicAttack when skill.PathTier[1] >= 3:
                        return Loc.F("step.ProjectileCount.chasearrow", Mathf.RoundToInt(s.amount));
                    case ActiveSkillId.Lightning: return Loc.F("step.ProjectileCount.bolt", Mathf.RoundToInt(s.amount)); // 피뢰침 낙뢰 수
                    case ActiveSkillId.Sniping: return Loc.F("step.ProjectileCount.sniping", Mathf.RoundToInt(s.amount)); // 대상당 연사 수
                    case ActiveSkillId.EagleDrop: return Loc.F("step.ProjectileCount.eagle", Mathf.RoundToInt(s.amount)); // 투하 횟수
                    case ActiveSkillId.Homing:
                        return Loc.F("step.ProjectileCount", HomingMissileCount(skill, newExtra) - HomingMissileCount(skill, skill.ExtraProjectiles));
                    case ActiveSkillId.Shotgun:
                        return Loc.F("step.ProjectileCount", ShotgunPelletCount(skill, newExtra) - ShotgunPelletCount(skill, skill.ExtraProjectiles));
                    default: return Loc.F("step.ProjectileCount", Mathf.RoundToInt(s.amount));
                }
            }
            case SkillStat.ProcChance: return Loc.F("step.ProcChance", Mathf.RoundToInt(s.amount * 100f));
            case SkillStat.Duration:
            {
                // 전탄발사는 지속 카드에 FullBurstDurationMult가 곱해져 실제로 늘어난다 — 카드도 그 실제 초를 보여준다.
                bool fullBurstCard = id == ActiveSkillId.Shotgun && skill.PathTier[2] >= 3;
                string secs = (s.amount * (fullBurstCard ? FullBurstDurationMult : 1f)).ToString("0.##");
                // 미진화 산탄엔 지속시간이 없다 — 값은 쌓였다가 진화(보너스 탄환 장착 버프·전탄발사)에서 쓰인다.
                if (id == ActiveSkillId.Shotgun && skill.PathTier[1] <= 0 && skill.PathTier[2] < 2)
                    return Loc.F("step.Duration.deferred", secs);
                // 산탄 R0는 버프 시간, R1은 연사 시간 — 같은 필드지만 화면에 보이는 것이 다르다.
                if (id == ActiveSkillId.Shotgun) return Loc.F(skill.PathTier[2] >= 2 ? "step.Duration.barrage" : "step.Duration.buff", secs);
                if (id == ActiveSkillId.GrapeToss) return Loc.F("step.Duration.cloud", secs);
                if (id == ActiveSkillId.Orb && skill.PathTier[1] >= 3) return Loc.F("step.Duration.orb", secs);
                return Loc.F("step.Duration", secs);
            }
            // 크기가 곧 범위인 스킬은 "무엇의 범위"인지 적는다(2026-09-18).
            case SkillStat.Scale:
            {
                int pct = Mathf.RoundToInt(s.amount * 100f);
                if (id == ActiveSkillId.GrapeToss)
                    return Loc.F(skill.PathTier[1] >= 3 ? "step.Scale.cloudExplosion" : "step.Scale.cloud", pct);
                // 🔴 호밍은 2026-09-29에 폭발 반경이 SubScale로 옮겨져 크기 축이 아무것도 안 한다 —
                //    여기 분기를 두면 죽은 카드가 살아 있는 것처럼 보인다. 호밍 커브엔 크기 칸을 넣지 않는다.
                if (id == ActiveSkillId.EagleDrop && skill.PathTier[1] >= 2) return Loc.F("step.Scale.explosion", pct);
                if (id == ActiveSkillId.EagleDrop && skill.PathTier[2] >= 3) return Loc.F("step.Scale.eagleRain", pct);
                if (id == ActiveSkillId.Lightning && skill.PathTier[1] >= 2) return Loc.F("step.Scale.lightningRod", pct);
                // 초대형 축적 번개: 크기 축이 초대형 낙뢰가 퍼지는 반경으로 간다(2026-09-29 사용자).
                if (id == ActiveSkillId.Lightning && skill.PathTier[0] >= 3) return Loc.F("step.Scale.hugeBolt", pct);
                // 휘두르기는 크기가 곧 때리는 범위다 — "크기"로 쓰면 망치 그림 얘기로 읽힌다(2026-09-29 사용자).
                if (id == ActiveSkillId.Swing) return Loc.F("step.Scale.swing", pct);
                return Loc.F("step.Scale", pct);
            }
            case SkillStat.RewindAmount: return Loc.F("step.RewindAmount", s.amount.ToString("0.##"));
            case SkillStat.TickRate:
            {
                int fast = Mathf.RoundToInt((1f - s.amount) * 100f);
                if (id == ActiveSkillId.EagleDrop) return Loc.F("step.TickRate.eagle", fast);
                if (id == ActiveSkillId.GrapeToss) return Loc.F("step.TickRate.poison", fast);
                // 사이버네틱 벙커: 피격 반격 능력의 자체 쿨(스킬 쿨과 별개).
                if (id == ActiveSkillId.Sniping && skill.PathTier[2] >= 3) return Loc.F("step.TickRate.bunker", fast);
                // 초대형 오브: 주위 적을 끌어당기는 주기.
                if (id == ActiveSkillId.Orb && skill.PathTier[1] >= 3) return Loc.F("step.TickRate.pull", fast);
                return Loc.F("step.TickRate", fast);
            }
            // 동시에 상대하는 적 수(스나이핑 저격 대상). 일반 오브는 관통 무한이라 이 축을 안 쓴다.
            case SkillStat.MaxTargets:
            {
                int n = Mathf.RoundToInt(s.amount);
                // 추적 오브·저글러는 한 마리씩 무는 무리라 이 축이 **알 개수**로 간다(2026-09-29 사용자).
                if (id == ActiveSkillId.Orb && skill.PathTier[2] >= 2) return Loc.F("step.MaxTargets.orb", n);
                // 회오리 폭격: 독수리가 떨어진 자리에 생기는 미니 회오리 수.
                if (id == ActiveSkillId.EagleDrop && skill.PathTier[2] >= 2) return Loc.F("step.MaxTargets.miniTornado", n);
                return Loc.F("step.MaxTargets", n);
            }
            // 발사 묶음이 한 번 더 나간다 — 산탄의 "빵 빵"이 "빵 빵 빵"이 된다.
            case SkillStat.VolleyCount: return Loc.F("step.VolleyCount", Mathf.RoundToInt(s.amount));
            // ── 2026-09-29 추가한 13종 ──
            case SkillStat.InstallDuration:
            {
                string secs = s.amount.ToString("0.##");
                if (id == ActiveSkillId.Whirlwind) return Loc.F("step.InstallDuration.maker", secs);
                if (id == ActiveSkillId.Lightning) return Loc.F("step.InstallDuration.rod", secs);
                if (id == ActiveSkillId.EagleDrop) return Loc.F("step.InstallDuration.eagleRain", secs);
                return Loc.F("step.InstallDuration", secs);
            }
            case SkillStat.MaxStacks: return Loc.F("step.MaxStacks", Mathf.RoundToInt(s.amount));
            case SkillStat.SubDamage:
            {
                int pct = Mathf.RoundToInt((s.amount - 1f) * 100f);
                if (id == ActiveSkillId.Swing) return Loc.F("step.SubDamage.wave", pct);
                if (id == ActiveSkillId.BasicAttack) return Loc.F("step.SubDamage.chasearrow", pct); // 블루베리 사냥꾼
                return Loc.F("step.SubDamage.explosion", pct);
            }
            case SkillStat.SubScale:
            {
                // 더하기 칸(배율에 +0.15)도 있다 — 곱하기 식으로만 읽으면 "+15%"가 "-85%"로 찍혔다(묵직한 탄두 등 7칸).
                int pct = Mathf.RoundToInt((s.op == StatOp.Add ? s.amount : s.amount - 1f) * 100f);
                if (id == ActiveSkillId.Swing) return Loc.F("step.SubScale.wave", pct);
                if (id == ActiveSkillId.GrapeToss) return Loc.F("step.SubScale.plague", pct);
                return Loc.F("step.SubScale.explosion", pct);
            }
            case SkillStat.BossDamage: return Loc.F("step.BossDamage", Mathf.RoundToInt(s.amount * 100f));
            case SkillStat.BonusHits: return Loc.F("step.BonusHits", Mathf.RoundToInt(s.amount));
            case SkillStat.EmpowerBonus: return Loc.F("step.EmpowerBonus", Mathf.RoundToInt((s.amount - 1f) * 100f));
            case SkillStat.PenaltyReduction: return Loc.F("step.PenaltyReduction", Mathf.RoundToInt(s.amount * 100f));
            case SkillStat.AllCooldown: return Loc.F("step.AllCooldown", Mathf.RoundToInt(s.amount * 100f));
            case SkillStat.EnemySlow: return Loc.F("step.EnemySlow", Mathf.RoundToInt(s.amount * 100f));
            case SkillStat.StunDuration: return Loc.F("step.StunDuration", s.amount.ToString("0.##"));
            // 몇 틱마다 기절하는지 — 값이 음수라 "주기가 줄어든다"로 읽는다.
            case SkillStat.StunInterval: return Loc.F("step.StunInterval", Mathf.RoundToInt(-s.amount));
            case SkillStat.Lifesteal: return Loc.F("step.Lifesteal", Mathf.RoundToInt(s.amount));
            default: return "";
        }
    }

    // ── 진화 (2루트 × 2티어, 진화 아이템으로만 열림) ────────────────────────────
    // 1차: 만렙(Lv.10) 도달 → 열려 있는 루트 중 하나 선택 (고르면 Lv.1로 리셋)
    // 2차: 다시 만렙 도달 → 1차에서 고른 루트의 다음 티어(선택지 없음)
    public bool CanEvolve(EquippedSkill skill)
    {
        if (skill == null || skill.EvolutionStage >= EvolutionRoutes.MaxStageFor(skill.Id)) return false;
        // 스킬트리 "진화 해금" / "2차 진화 해금". 트리에 그 노드가 없으면 둘 다 true라 게이팅이 없다.
        if (!(skill.EvolutionStage == 0 ? MetaBonuses.EvolutionUnlocked : MetaBonuses.Evolution2Unlocked)) return false;
        if (skill.Level < EvolutionRoutes.RequiredLevel) return false;
        // 2차는 **열쇠 진화체**가 이미 만들어져 있어야 한다(2026-09-08 신설 — §EvolutionRoutes.Stage2Prereq).
        if (skill.EvolutionStage >= 1 && !IsStage2KeyReady(skill.Id, skill.Route)) return false;
        return SelectableRoutes(skill).Any(r => IsRouteUnlocked(skill.Id, r));
    }

    // 루트 잠금: 연계 대상(패시브/액티브)을 보유해야 그 루트를 고를 수 있다(§EvolutionRoutes).
    public bool IsRouteUnlocked(ActiveSkillId id, int route)
    {
        PassiveSkillId? p = EvolutionRoutes.RoutePassivePrereq(id, route);
        if (p.HasValue && (passives == null || !passives.HasPassive(p.Value))) return false;
        ActiveSkillId? a = EvolutionRoutes.RouteActivePrereq(id, route);
        return !a.HasValue || HasSkill(a.Value);
    }

    // 2차 열쇠: 지정된 (스킬/패시브, 루트)가 **1차 이상 진화**돼 있어야 한다.
    // ⚠️ 보유만으로는 안 된다 — 루트까지 그 루트여야 한다("화살비"는 되고 "검은 화살"은 안 된다).
    public bool IsStage2KeyReady(ActiveSkillId id, int route)
    {
        var (p, a, keyRoute) = EvolutionRoutes.Stage2Prereq(id, route);
        if (p.HasValue)
        {
            EquippedPassive kp = passives != null ? passives.GetPassive(p.Value) : null;
            return kp != null && kp.EvolutionStage >= 1 && kp.Route == keyRoute;
        }
        if (a.HasValue)
        {
            EquippedSkill ks = equippedSkills.FirstOrDefault(s => s.Id == a.Value);
            return ks != null && ks.EvolutionStage >= 1 && ks.Route == keyRoute;
        }
        return true;   // 열쇠가 정의되지 않은 조합은 잠그지 않는다(새 스킬을 넣고 표를 안 채웠을 때)
    }

    // 이 진화에서 고를 수 있는 루트들. 1차는 0·1 둘 다, 2차는 이미 고른 루트 하나뿐.
    public static int[] SelectableRoutes(EquippedSkill skill) =>
        skill.EvolutionStage == 0 ? new[] { 0, 1 } : new[] { skill.Route };

    public void EvolveSkill(ActiveSkillId id, int route)
    {
        EquippedSkill skill = equippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null || !CanEvolve(skill)) return;
        if (skill.EvolutionStage > 0 && route != skill.Route) return; // 2차는 루트 변경 불가
        if (!IsRouteUnlocked(id, route)) return;

        int newTier = skill.EvolutionStage + 1;
        int path = EvolutionRoutes.RoutePath(id, route);

        // 기존 티어 효과를 순서대로 적용 — 새 티어1 = 옛 T1+T2, 새 티어2 = 옛 T3
        foreach (int legacyTier in EvolutionRoutes.LegacyTiersFor(newTier))
        {
            skill.PathTier[path] = legacyTier;
            ApplyPathTierEffect(skill, path, legacyTier);
        }
        skill.PathTier[path] = EvolutionRoutes.TargetPathTier(newTier);

        skill.Route = route;
        skill.EvolutionStage = newTier;
        CollectionSave.DiscoverActiveEvo(id, route, newTier); // 컬렉션(도감) 발견 기록
        Achievements.OnEvolved(newTier);
        SfxPlayer.Play(SfxId.Evolution);

        // 레벨 표시 리셋(누적 레벨 TotalLevel은 유지 — 다음 진화 게이트 기준).
        // 레벨업 커브를 처음부터 다시 타므로 "새 스킬을 1레벨부터 키운다"는 감각이 된다.
        // 🔴 여기서 피해에 곱하던 공통 배수 1.5는 없앴다(사용자 결정 2026-09-28).
        //    진화체의 시작 피해와 쿨타임은 아래 ApplyEvolutionProgression이 읽는 `Evo_*.asset` 하나가 정한다.
        skill.Level = 1;

        // 진화 전용 에셋(Evo_*)이 값을 정해 뒀으면 **그 축만** 덮어쓴다. 비어 있으면(전부 0) 위 계산 그대로 —
        // 그래서 에셋을 안 채운 진화는 지금까지와 100% 같게 돈다.
        ApplyEvolutionProgression(skill);

        // 되감기 R1 2차 「블루베리 절멸의 시간」은 **상시 버프 아이콘**을 띄운다(2026-09-19 사용자 지시).
        // 지속시간이 없는 상태라 끝나는 시각을 무한으로 두고 남은시간 텍스트를 숨긴다.
        // ⚠️ 여기서 한 번만 건다 — 매 프레임 Set을 부르면 Entry를 프레임마다 새로 할당한다.
        if (id == ActiveSkillId.Rewind && skill.PathTier[2] >= 3)
            BuffTracker.Set("RewindEndTimes", float.MaxValue, showTimer: false);
    }

    // 🔴 0 = "이 진화는 이 축을 안 정한다". 여기서 덮어쓰는 건 **시작값**이고, 이후 레벨업은
    //    `StepFor`가 진화 커브(있으면) → 원래 스킬 커브 순으로 태운다.
    private static void ApplyEvolutionProgression(EquippedSkill skill)
    {
        EvolutionProgression e = EvoProg(skill);
        if (e == null) return;
        if (e.baseDamage > 0f) skill.Damage = e.baseDamage;
        if (e.baseCooldown > 0f) skill.Cooldown = Mathf.Max(GlobalCooldown, e.baseCooldown);
        // 타수는 EquippedSkill이 아니라 static 조회맵에 담는다(위 evolvedHits 주석 참고).
        if (e.baseHits > 0) evolvedHits[skill.Id] = e.baseHits;
        if (e.basePierce > 0) skill.ExtraPierce = e.basePierce;
        if (e.baseProjectiles > 0) skill.ExtraProjectiles = e.baseProjectiles;
        // 지속시간은 스킬마다 담기는 필드가 달라 산탄만 따로 받는다(나머지는 BaseDuration이 에셋을 직접 읽는다).
        if (e.baseDuration > 0f && skill.Id == ActiveSkillId.Shotgun)
            skill.ExtraShotgunDuration = e.baseDuration - BaseDuration(skill.Id, 5f + 2f);
    }

    // 티어마다 정확히 하나의 효과만 부여한다. 여기 없는 조합은 PathTier를 직접 읽는 Fire*/TryUseSkill에서
    // 실시간으로 계산하는 기믹(관통 횟수, 분열 소환 수, 슬로우 강화, 낙뢰 재귀/스택 등)이라 영구 스탯 변경이 없다.
    // 진화한 망치로 갈아끼운다. ApplyPathTierEffect가 static이라 instance를 거친다.
    // ⚠️ 딸기처럼 다른 캐릭터가 휘두르기를 진화시켰을 때 파인애플 컨트롤러를 씌우면 **캐릭터가 통째로 파인애플이 된다** —
    //    base 컨트롤러가 같을 때(= 지금 파인애플일 때)만 교체한다.
    // which: 0 = 쓸어치기(R0 1차) · 1 = 지진파(R1 1차) · 2 = 로열 팔라딘의 망치(R0 2차)
    private static void ApplyHammerLook(int which)
    {
        if (instance == null || instance.animator == null) return;

        RuntimeAnimatorController next = which == 1 ? instance.shockHammerController
                                       : which == 2 ? instance.paladinHammerController
                                       : instance.bigHammerController;
        if (next == null) return;   // 팔라딘 미배선이면 1차 망치가 그대로 남는다

        RuntimeAnimatorController cur = instance.animator.runtimeAnimatorController;
        AnimatorOverrideController curOverride = cur as AnimatorOverrideController;
        RuntimeAnimatorController curBase = curOverride != null ? curOverride.runtimeAnimatorController : cur;

        AnimatorOverrideController nextOverride = next as AnimatorOverrideController;
        RuntimeAnimatorController nextBase = nextOverride != null ? nextOverride.runtimeAnimatorController : next;

        if (curBase != nextBase) return; // 다른 캐릭터 — 건드리지 않는다
        instance.animator.runtimeAnimatorController = next;
    }

    // 🔴 **진화는 쿨타임을 줄이지 않는다**(2026-09-07 사용자 결정 — "진화 스킬 쿨들이 다 너무 짧음").
    //    쿨감 분기(낙뢰 R0 1차·독수리 R1 1차·스나이핑 R1 1차·되감기 R1·호밍 R1·오브 R0 2차·처형 사격)를
    //    전부 걷어냈고, 진화 쿨은 `Evo_*.baseCooldown`이 단독으로 정한다.
    //    ⚠️ **쿨타임을 늘리는 분기는 남긴다** — 그건 강한 진화가 치르는 대가지 불만의 대상이 아니다.
    //    새 진화 효과에 쿨감을 넣지 말 것. 도약은 피해·범위·타수로 준다.
    private static void ApplyPathTierEffect(EquippedSkill skill, int path, int newTier)
    {
        switch (skill.Id, path, newTier)
        {
            // path0(기본) — 회오리 R0. 미니 회오리 피해 보너스를 얹던 두 칸은 없앴다(2026-09-28):
            //    미니 피해는 `MiniWhirlwindDamageRatio` 30%로 고정이다.
            // Whirlwind path0 T1/T3(미니 회오리 개수)는 FireWhirlwind에서 매 캐스트마다 실시간 계산

            // path1(패시브 연계)
            // 🔴 독수리 R0 1차는 2026-09-08에 "독수리 비"에서 **폭탄 독수리**로 바뀌었다 — 화면 전체를
            //    10틱 넘게 두들기던 값이 아니라 착탄 폭발이라, 짝이던 쿨 1.4배도 같이 R1 2차로 옮겼다.
            //    (거래의 한쪽만 남기지 말 것 — 「급강하 폭격」에서 한 번 겪은 실수다.)

            // Orb 1,3은 문서상 슬로우 강화만이다(FireOrb 실시간). 딸려 있던 쿨감 0.9배는 걷어냈다.
            case (ActiveSkillId.Orb, 1, 2):
                // 「강력한 마력」 — 크기 증가 + **방패 관통**(FireOrb에서 PiercesShields).
                // 예전엔 여기에 쿨감 25%까지 붙어 크기·쿨·관통이 전부 좋아지는 이중 강화였다.
                // ⚠️ 2026-09-27에 기본 오브의 관통 예산이 사라져 "관통 무한"은 더 이상 이 진화의 값이 아니다 —
                //    남은 값은 크기와 **방패를 뚫는 것**이다. 쿨 1.6배는 `Evo_Orb_R0_*.baseCooldown`으로 옮겼다.
                skill.Scale += 0.3f;
                break;

            // path2(액티브 연계)
            // 독수리 R1 1차는 쿨감 40%가 전부였다 — 걷어내서 지금은 진화 공통 피해 도약만 남는다.

            // 스나이핑 (path1 T2 초과데미지연쇄·path2 T2 자동시전은 Fire/Update에서 실시간 처리)
            // ⚠️ 스나이핑은 (타겟 수 × 연사 수 × 피해)로 세 축이 전부 곱해지는 유일한 스킬이라
            //    피해를 올릴 때 세 축이 함께 커진다는 점을 `Evo_Sniping_*.baseDamage`를 정할 때 감안할 것.

            // 호밍 미사일 (폭발 path1 T2는 FireHoming에서 실시간)
            // 🔴 path1 1차 「묵직한 탄두」의 피해 보너스 ×1.3은 **걷어냈다**(2026-09-27 사용자: 폭발 범위를 키우는
            //    대신 피해 보너스는 없앤다). 지금은 시작 피해가 `Evo_Homing_R0_T1.baseDamage`에 직접 적혀 있다.
            // R1(가속 연계, path2)의 쿨감(0.7배·0.8배)도 걷어냈다 — 개수·크기는 FireHoming이 실시간으로 맡는다.
            // 산탄(Shotgun)은 전부 FireShotgun/FireShotgunPellets에서 실시간 계산(영구 스탯 변경 없음)
            // 🔴 예외 하나 — R1 2차 「전탄발사」는 1차 「메카 버스터」에서 쌓은 지속시간 카드를 **물려받지 않는다**(2026-10-06 사용자).
            //    안 지우면 +0.7초가 넘어와 FullBurstDurationMult가 곱해진다(진화 직후 5초여야 할 것이 7.5초가 된다).
            case (ActiveSkillId.Shotgun, 2, 3):
                skill.ExtraShotgunDuration = 0f;
                break;

            // 되감기 R1(path2)에 있던 자체 쿨감 30%도 걷어냈다.
            // (T3의 **전역** 쿨감은 GlobalCooldownScale에서 실시간 처리 — 그건 진화가 스킬 쿨을 줄이는 게 아니라
            //  되감기 루트의 정체라서 그대로 둔다.)
            // 되감기 R0(다음 스킬 피해, path1)는 FireRewind에서 실시간 계산

            // 휘두르기 path1(힘 연계) 1차 = 타격 범위 1.45배. 범위가 곧 그대로 화력이라
            // 쿨타임으로 대가를 치르게 한다("한 방은 크지만 자주 못 쓴다") — 그 쿨은 이제 `Evo_Swing_R0_*.baseCooldown`이다.
            case (ActiveSkillId.Swing, 1, 2):
                ApplyHammerLook(0); // 망치가 커진다
                break;

            // 휘두르기 path2(회오리 연계) 1차 = 맵 끝까지 가는 충격파. 본체 판정 밖의 적까지 닿는
            // 사실상 사거리 무제한 공격이라, 1루트와 마찬가지로 쿨타임으로 대가를 치르게 한다.
            case (ActiveSkillId.Swing, 2, 2):
                ApplyHammerLook(1); // 충격파를 내는 망치로
                break;

            // 휘두르기 path1 2차 「로열 팔라딘의 망치」 = 망치 그림이 한 번 더 바뀐다(2026-09-19 아트).
            // 효과(장시간 기절)는 FireSwing이 PathTier로 실시간 처리하므로 여기선 그림만 갈아끼운다.
            // 크기는 1차의 1.45배(SwingRangeMult)를 그대로 타므로 이펙트도 같이 커진다.
            case (ActiveSkillId.Swing, 1, 3):
                ApplyHammerLook(2);
                break;

            // 나머지 휘두르기 진화는 영구 스탯 변경이 없다 — 기절(path1)·충격파(path2) 전부
            // FireSwing/SwingRoutine에서 PathTier를 실시간으로 읽어 처리한다.
            // (진화 자체의 피해 1.5배·쿨 0.9배는 EvolutionRoutes가 공통으로 얹는다.)
        }
    }

    // 루트 잠금 조건은 EvolutionRoutes.RoutePrereq가 (스킬, 루트)별로 단독 소유한다(2026-08-06 개편).

    public static string GetActiveSkillName(ActiveSkillId id) =>
        Loc.TOr("skill.name." + id, id.ToString());

    public static string GetSkillCategoryLabel(SkillCategory c) =>
        Loc.TOr("skill.cat." + c, c.ToString());

    // 레벨업 선택지처럼 액티브/패시브가 섞여 나오는 곳에서 쓰는 종류 배지.
    // ⚠️ 리치텍스트 마크업은 코드에 남기고 **낱말만** 표에 둔다 — 번역자가 태그를 깨뜨릴 자리를 만들지 않는다.
    public static string ActiveTypeBadge => "<size=68%><color=#FFD86B>[" + Loc.T("ui.badge.active") + "]</color></size>";
    public static string PassiveTypeBadge => "<size=68%><color=#9BE86B>[" + Loc.T("ui.badge.passive") + "]</color></size>";

    // 레벨업 선택지 제목: 배지를 이름 뒤에 붙인다 — "회오리 [액티브]" / "힘 [패시브]"
    // 🔴 종류 배지(공격/버프/유틸)는 **전부 뺐다**(사용자 결정 2026-09-02) — 액티브·패시브만 남긴다.
    //    `SkillCategory` enum과 라벨만 남아 있다 — 화면엔 안 나오고 에디터의 번역 수확 도구(LocHarvest)만 쓴다.
    public static string GetActiveSkillTitleWithTags(ActiveSkillId id) =>
        $"{GetActiveSkillName(id)} {ActiveTypeBadge}";

    public static string GetPassiveSkillTitleWithTags(PassiveSkillId id) =>
        $"{GetPassiveSkillName(id)} {PassiveTypeBadge}";

    // 이미 장착한 스킬은 진화 후 이름(DisplayName)으로 표시한다.
    public static string GetActiveSkillTitleWithTags(EquippedSkill s) =>
        $"{s.DisplayName} {ActiveTypeBadge}";

    public static string GetPassiveSkillTitleWithTags(EquippedPassive p) =>
        $"{p.DisplayName} {PassiveTypeBadge}";

    public static string GetPassiveSkillName(PassiveSkillId id) =>
        Loc.TOr("passive.name." + id, id.ToString());

    // 🔴 진화 설명은 **일부러 수치를 안 쓴다.** BTD6 파라곤 설명처럼 그림만 던져서,
    //    "고르면 뭐가 나올까"라는 호기심으로 루트를 고르게 하는 게 목적이다.
    //    (실제 수치는 ApplyPathTierEffect와 각 Fire*에 있다 — 밸런스는 거기서 확인할 것.)
    // 문장은 번역 표(`Assets/Localization/Tables/Game`)가 소유한다.
    // 🔴 키 좌표는 **화면에 보이는 것 그대로**다 — 루트 0/1 × 차수 1/2(2026-09-08 개편).
    //    예전엔 legacy path·tier(1~3)를 키에 썼고 1차 칸이 옛 1·2 두 줄을 이어 붙여 그렸다.
    //    지금은 한 칸 = 한 줄이다. **effect 쪽 PathTier(1차=2, 2차=3)와 헷갈리지 말 것** — 그건 그대로다.
    // 정의가 없는 조합은 빈 문자열 — 옛 `_ => ""` 분기와 같다.
    public static string DescribePathEffect(ActiveSkillId id, int route, int tier) =>
        Loc.TOr($"evo.active.desc.{id}.{route}.{tier}", "");

    private void TryUseSkill(EquippedSkill skill)
    {
        if (globalCooldownTimer > 0f || skill.CooldownTimer > 0f) return;

        // 되감기 R0 2차 「과충전」 — 되감기 **직후 한 번** 쓰는 스킬이 타수 +2를 받고 쿨이 2배가 된다.
        // ⚠️ 피해 보너스는 ComputeBaseDamage가 소비하며 **거기서 0으로 지운다** — 그래서 여기서 먼저 집는다.
        bool overcharged = skill.Id != ActiveSkillId.Rewind && nextSkillBonusHits > 0;
        if (overcharged)
        {
            nextSkillBonusHits = 0;
            BuffTracker.Clear("RewindOvercharge");   // 버프 아이콘도 여기서 사라진다
            SkillSfx.Play("Overcharge.consume");     // 소비형 버프라 **터지는 순간**이 따로 있다
        }

        // 타격 기준 치명타: 캐스트 시점엔 확률만 확정하고, 실제 치명타 여부는 각 데미지 이벤트(투사체 명중/틱)마다 개별적으로 굴린다.
        float critChance = GetCritChance(skill);
        float damage = ComputeBaseDamage(skill);

        // 타수는 Enemy.TakeSkillHit가 **명중할 때마다** 읽으므로, 이번 캐스트가 결과를 낼 동안 살아 있어야 한다.
        // 캐스트 시점에 한 번 끊지 못하는 대신 짧은 창(OverchargeWindow)을 둔다 — 화살비처럼 늘어지는 캐스트도 덮되
        // 다음 캐스트로는 안 새는 길이다.
        if (overcharged)
        {
            overchargeSkill = skill.Id;
            overchargeHitsTimer = OverchargeWindow;
        }

        switch (skill.Id)
        {
            case ActiveSkillId.BasicAttack:
                if (!FireBasicAttack(skill, damage, critChance, allowBonusShot: true)) return;
                break;
            case ActiveSkillId.Whirlwind:
                FireWhirlwind(damage, critChance, skill);
                break;
            case ActiveSkillId.Orb:
                FireOrb(damage, critChance, skill);
                break;
            case ActiveSkillId.Lightning:
                // R0(되감기 연계, path0) = **버프 중첩**. 원래 도달 불가능한 path2에 잠들어 있던 효과를 여기로 가져왔다
                // (2026-08-06 명세: "기존 낙뢰 버프 중첩 Path 다시 가져와서 쓰기"). 지속시간이 늘어 스택이 겹치고,
                // 그 스택 수만큼 전체 공격 피해가 오른다 — 낙뢰가 "깔아두는 버프"라는 정체성이 여기서 완성된다.
                // 🔴 R1(힘 연계, path1) = **피뢰침**. 맵 중앙에 꽂아 1초마다 넓은 범위를 내리친다.
                //    이 루트는 기본 낙뢰 버프를 **대체한다**(2026-09-27 사용자 "원래 낙뢰 버프는 없어지고").
                //    종전엔 아래 버프 배선이 PathTier 분기 없이 무조건 돌아서, 피뢰침을 골라도 ProcChance·ProcDamage가
                //    그대로 세팅되고 Enemy.RollProcCount가 계속 발동했다 — 즉 대체가 아니라 **덧붙임**이었다.
                if (skill.PathTier[1] >= 2)
                {
                    LightningStorm.ClearStacks();   // 진화 전에 걸어 둔 버프도 그 자리에서 걷는다
                    LightningStorm.StackingEnabled = false;
                    LightningStorm.StackDamageEnabled = false;
                    LightningStorm.HugeBoltEnabled = false;
                    // 🔴 설치기는 한 번에 하나(2026-10-01 사용자) — 새로 꽂으면 이전 기둥과 그 타격 루틴을 걷는다.
                    RemoveLightningRod();
                    lightningRodRoutine = StartCoroutine(LightningRodRoutine(damage, critChance, empowered: skill.PathTier[1] >= 3, scale: skill.Scale,
                                                       extraDuration: skill.ExtraInstallDuration, extraBolts: skill.ExtraProjectiles));
                    RefreshLightningBuffDisplay();
                    break;
                }

                // 🔴 스택 유지 시간 = 진화 전 10초 × 차수 배율(2026-10-01 사용자 "R0 쿨이 길어 스택이 안 쌓인다").
                //    동시 스택 ≈ 유지 시간 ÷ 쿨. 종전 ×1.3(13초)은 1차 쿨 10→6초에서 스택 1.3~2.2개라 상한 4~10과 "최대 스택" 카드가 빈 칸이었고,
                //    2차 초대형 번개(15스택)는 쿨 6초로 사실상 발동 불가였다. 1차 ×3(30초 → 3~5스택) · 2차 ×9(90초 → 약 15스택).
                //    ⚠️ 2026-10-07부터 "동시 스택 ≈ 유지 시간 ÷ 쿨"은 성립하지 않는다 — 재시전이 스택 전부를 갱신한다(LightningStorm.AddStack).
                //    이 시간은 이제 **다시 안 쓰면 스택이 통째로 꺼지기까지의 여유**다.
                float baseDuration = BaseDuration(skill.Id, 10f) * (skill.PathTier[0] >= 3 ? 9f : skill.PathTier[0] >= 2 ? 3f : 1f);
                // 🔴 스택은 R0 진화부터만 쌓인다(사용자 결정 2026-09-17). 진화 전엔 AddStack이 기존 버프를 갈아끼운다 —
                //    쿨감이 쌓여 쿨이 지속시간보다 짧아지면 진화 없이도 스택이 겹치던 버그.
                LightningStorm.StackingEnabled = skill.PathTier[0] >= 2;
                // 🔴 상한을 AddStack **앞에서** 세운다(2026-09-29). 1차 「뇌운 축적」 4 · 2차 「초대형 축적 번개」 20에서
                //    시작하고, 레벨업 "최대 스택" 카드가 그 위에 더해진다.
                LightningStorm.MaxStacks = (skill.PathTier[0] >= 3 ? HugeBoltBaseMaxStacks : LightningStorm.BaseMaxStacks)
                                           + skill.ExtraMaxStacks;
                LightningStorm.AddStack(baseDuration);
                // 스킬트리 "축전"(thunder_Stack) = 발동 확률 +10%p — 레벨업 "발동 확률" 카드와 같은 덧셈.
                LightningStorm.ProcChance = LightningStorm.BaseProcChance + skill.ProcChanceBonus + MetaBonuses.ThunderProcBonus;
                LightningStorm.ProcDamage = damage;
                LightningStorm.StackDamageEnabled = skill.PathTier[0] >= 2;
                LightningStorm.StackDamageBonusPerStack = skill.PathTier[0] >= 3 ? 0.25f : LightningStorm.BaseStackDamageBonus;
                // R0 2차 「초대형 축적 번개」 — 스택 15 이상이면 낙뢰가 초대형으로 바뀐다(쿨 0.5초).
                // VFX 프리팹을 여기서 넘긴다 — Enemy 프리팹 12장에 같은 칸을 만들지 않으려고.
                LightningStorm.HugeBoltEnabled = skill.PathTier[0] >= 3;
                LightningStorm.HugeBoltVfxPrefab = hugeBoltVfxPrefab;
                // 레벨업 "크기" 축이 초대형 낙뢰가 퍼지는 반경으로 간다(2026-09-29 사용자).
                LightningStorm.HugeBoltRadius = HugeBoltBaseRadius * skill.Scale;
                RefreshLightningBuffDisplay();
                break;
            case ActiveSkillId.EagleDrop:
                StartCoroutine(EagleDropRoutine(damage, critChance, skill));
                break;
            case ActiveSkillId.Sniping:
                if (!FireSniping(damage, critChance, skill)) return; // 조준할 적이 없으면 캐스트 실패(쿨 소모 안 함)
                break;
            case ActiveSkillId.Homing:
                if (!FireHoming(damage, critChance, skill)) return;
                break;
            case ActiveSkillId.Shotgun:
                FireShotgun(damage, critChance, skill);
                break;
            case ActiveSkillId.Rewind:
                FireRewind(skill);
                break;
            case ActiveSkillId.Swing:
                FireSwing(damage, critChance, skill);
                break;
            case ActiveSkillId.GrapeToss:
                FireGrapeToss(damage, skill);
                break;
        }

        // 🔴 시전음 — **여기가 캐스트가 확정된 자리다.** 위 switch에서 실패한 시전(조준할 적이 없는 스나이핑 등)은
        //    return으로 빠져나가므로 여기까지 오지 않는다. 슬롯이 비어 있으면 SkillSfx가 조용히 넘어간다.
        // ⚠️ 회오리·오브·독수리·포도는 각자의 Spawn/Fire 안에서 **더 정확한 순간**에 울린다 — 여기서 또 울리면 두 번이다.
        if (!HasOwnCastSfx(skill.Id)) SkillSfx.Play(skill.Id + ".cast");

        // GCD를 아예 안 거는 경우 둘:
        //   · 스킬트리 "되감기: 전역 쿨타임 미발동" — 되감기만 해당(다른 스킬을 바로 이어 쓸 수 있다).
        //   · 스나이핑 R1 1차 「자동 방어 시스템」 — 문구가 "글로벌 쿨타임을 트리거하지 않는다"다(2026-09-08).
        //     어차피 이 루트는 스킬 쿨 대신 고정 간격 자동시전으로 도므로 GCD가 걸리면 다른 스킬만 막는다.
        bool skipsGcd = (skill.Id == ActiveSkillId.Rewind && MetaBonuses.RewindSkipsGlobalCooldown)
                     || (skill.Id == ActiveSkillId.Sniping && skill.PathTier[2] >= 2);
        if (!skipsGcd)
            globalCooldownTimer = GlobalCooldown * GlobalCooldownScale(); // 되감기 R1: 1차 절반 · 2차 0
        // 스나이핑 자동시전(R1)의 간격도 이제 평범한 스킬 쿨이다 — 1차 3초·2차 1.5초는 `Evo_Sniping_R1_T1/T2`의
        // baseCooldown이 진화 순간 넣는다(2026-09-18 데이터화, 수치 동일). 예전엔 여기 `1.5f : 3f`로 박혀 있어
        // 에셋으로 조정할 수 없었다. ⚠️ 부작용: 힘·가속 트리 보너스의 쿨 구간 판정이 이제 실제 간격(3·1.5초)을 본다.
        // (오브 설치기의 긴 전용 쿨은 오브 R1이 추적 오브 무리로 바뀌며 사라졌다 — 이제 평범한 스킬 쿨을 쓴다)
        // 🔴 진화별로 여기서 더하거나 곱하던 값(검은 화살 +3초 · 화살비 ×2)은 없앴다(사용자 결정 2026-09-28).
        //    그 값은 `Evo_BasicAttack_*.baseCooldown`에 들어가 있다 — 진화 쿨을 알려면 그 에셋 한 장만 보면 된다.
        float baseCd = skill.Cooldown;
        // 되감기 R0 2차 「과충전」의 대가 — 되감기 직후 쓴 **그 한 번**의 쿨이 2배가 된다.
        if (overcharged) baseCd *= overchargeCooldownMult;
        // 스킬트리 "호밍: 기본 쿨 -1초" — 감소**율**이 곱해지기 전의 기본 쿨에서 먼저 뺀다.
        if (skill.Id == ActiveSkillId.Homing && MetaBonuses.HomingCooldownCut > 0f)
            baseCd = Mathf.Max(GlobalCooldown, baseCd - MetaBonuses.HomingCooldownCut);
        float cdMult = MetaBonuses.CooldownMult;
        // 스킬트리 "신속한 회오리": 회오리는 쿨타임 감소분(1-CooldownMult)을 1.5배로 받음
        if (skill.Id == ActiveSkillId.Whirlwind && MetaBonuses.WhirlwindCooldownBonus)
            cdMult = Mathf.Max(0.05f, 1f - 1.5f * (1f - MetaBonuses.CooldownMult));
        // 폐지된 리프레쉬 패시브 (Refresh, 2, 1) — 치트 창으로만 도달: 버프류 스킬(산탄·낙뢰) 쿨타임 감소
        if (PlayerPassives.BuffSkillCooldownMult < 1f && IsBuffSkill(skill))
            cdMult *= PlayerPassives.BuffSkillCooldownMult;
        // 패시브 "가속": 전 스킬 쿨타임 감소. 스킬트리 전역 쿨감과 같은 축이라 곱해서 들어간다.
        if (passives != null && passives.HasPassive(PassiveSkillId.Accel))
        {
            float cut = PlayerPassives.AccelCooldownReduction;
            if (cut > 0f) cdMult *= Mathf.Max(0.05f, 1f - cut);
        }
        // 되감기 R1 2차 「블루베리 절멸의 시간」 — 모든 스킬의 쿨타임이 감소한다(진화 쿨감 금지의 유일한 예외).
        cdMult *= RewindEndTimesCooldownScale();
        skill.CooldownTimer = baseCd * cdMult;
        skill.ReadySince = -1f; // 발동했으니 대기열에서 빠진다(리프레쉬로 쿨이 0이 되면 다음 프레임에 "지금"부터 다시 기다린다)
        BotInput.OnCast?.Invoke(skill, baseCd, cdMult); // 봇 플레이테스트 관측(평소 null)

        // 쿨타임 초기화. 리프레쉬는 폐지됐지만 **가속 진화 path1**이 RefreshChance를 물려받아 켠다 —
        // 그래서 보유 패시브가 아니라 **확률값 자체**를 게이트로 쓴다(둘 중 무엇이 켰든 똑같이 동작).
        if (passives != null && PlayerPassives.RefreshChance > 0f && Random.value < PlayerPassives.RefreshChance)
        {
            skill.CooldownTimer = 0f;
            OnRefreshProc?.Invoke();
            // 리프레쉬 연계 path1: 쿨타임 초기화시마다 체력(또는 초과체력) 회복
            if (health != null && PlayerPassives.RefreshHealOnResetAmount > 0f)
                health.AddOverheal(Mathf.RoundToInt(PlayerPassives.RefreshHealOnResetAmount));
        }
    }

    // 스테이지가 넘어갈 때 GameManager가 호출 — 모든 스킬 쿨타임 초기화
    public void ResetAllCooldowns()
    {
        globalCooldownTimer = 0f;
        foreach (EquippedSkill s in equippedSkills)
            s.CooldownTimer = 0f;
        stageStartLockTimer = StageStartLock;
    }

    // 방어 진화 path1(휘두르기 연계): 피격 시 휘두르기를 쿨타임과 무관하게 한 번 자동 발동한다.
    // 휘두르기 **보유**가 그 루트의 잠금 조건이라(EvolutionRoutes.RoutePrereq) 없을 일은 없지만,
    // 캐릭터 변경 등으로 빠졌을 때 조용히 무시하도록 방어적으로 확인한다.
    public void TriggerAutoSwing(float damageMult)
    {
        EquippedSkill swing = equippedSkills.FirstOrDefault(s => s.Id == ActiveSkillId.Swing);
        if (swing == null) return;
        FireSwing(ComputeBaseDamage(swing) * damageMult, GetCritChance(swing), swing);
    }

    // 리프레쉬 연계(패시브 path2)에서 낙뢰 발동 시에도 호출됨
    public void ReduceAllCooldowns(float amount)
    {
        foreach (EquippedSkill s in equippedSkills)
            s.CooldownTimer = Mathf.Max(0f, s.CooldownTimer - amount);
    }

    // 타격 기준 치명타: 여기선 "이번 캐스트에 적용될 확률"만 정하고, 실제 발동 여부는 각 데미지 이벤트에서 개별적으로 굴린다.
    private float GetCritChance(EquippedSkill skill)
    {
        // 암살 연계 path1 T1: 기본공격 전용 추가 치명타 확률
        float chance = skill.Id == ActiveSkillId.BasicAttack && skill.PathTier[1] >= 1 ? 0.15f : 0f;
        if (passives != null && passives.HasPassive(PassiveSkillId.Assassinate))
            chance += PlayerPassives.AssassinateCritChance;
        chance += MetaBonuses.CritBonus; // 메타 "치명타" 업그레이드(전역 가산)
        // 스킬트리 스킬별 치명타 강화(산탄·스나이핑). ⚠️ 상한(MaxCritChance 70%) 안쪽에서 더해진다 —
        // 다른 치명타 원천이 이미 높으면 +30%p가 그대로 안 들어간다.
        if (skill.Id == ActiveSkillId.Shotgun) chance += MetaBonuses.ShotgunCritBonus;
        else if (skill.Id == ActiveSkillId.Sniping) chance += MetaBonuses.SnipingCritBonus;
        // 암살 R1 「필중 암살」이 상한을 100%로 연다(2026-09-08). 0이면 기본 상한(MaxCritChance 70%) 그대로.
        // ⚠️ 예전엔 "쿨 5초 이상 스킬은 확정 치명타"라 상한을 **건너뛰었다** — 지금은 상한 자체를 올린다.
        //    그래서 확률을 실제로 쌓아야 100%에 닿는다(레벨업이 그 몫을 준다).
        float cap = PlayerPassives.CritChanceCapOverride > 0f ? PlayerPassives.CritChanceCapOverride : MaxCritChance;
        BotInput.OnCritChance?.Invoke(skill.Id, chance, cap); // 봇 관측(평소 null)
        return Mathf.Min(chance, cap);
    }

    // 🔴 스킬 **하나**를 받는다 — 스킬트리 "힘(쿨 5초 이상)"·"가속(쿨 4초 이하)" 강화가 그 스킬의
    //    기본 쿨(skill.Cooldown)을 봐야 해서. 판정 기준은 쿨감이 붙기 전의 **기본 쿨**이다
    //    (쿨감으로 경계를 넘나들면 같은 스킬의 피해가 판 중간에 요동친다).
    // consumeRewindBonus: 되감기 R0의 "다음 스킬 1회 강화"를 이 계산이 써 버릴지.
    // 🔴 상시로 도는 것(하늘 파쇄기의 화살비)은 false로 부른다 — 0.35초마다 부르면 되감기 강화를
    //    플레이어가 몰아주고 싶은 스킬에 쓰기 전에 화살비가 늘 먼저 먹는다.
    private float ComputeBaseDamage(EquippedSkill skill, bool consumeRewindBonus = true)
    {
        ActiveSkillId skillId = skill.Id;
        // 힘 패시브 계열 피해 배율은 하나의 덧셈 풀로 합친다 — path0(전 스킬 공통)과 path2(Q 슬롯 전용)를
        // 곱연산으로 각각 겹쳐 쌓으면 그 스킬에서 배율이 폭발한다. 같은 풀에서 더한 뒤 한 번만 곱한다.
        float damageMultiplier = passiveDamageMultiplier;
        // 힘 연계 path2(패시브): **Q키에 할당된 스킬** 전용 추가 피해량 (덧셈 합류).
        // Q = 가장 먼저 얻은 스킬 = equippedSkills[0] (AcquireSkill이 SlotKeys를 순서대로 붙인다).
        if (PlayerPassives.FirstSlotDamageMultiplierBonus > 0f && equippedSkills.Count > 0 && equippedSkills[0].Id == skillId)
            damageMultiplier += PlayerPassives.FirstSlotDamageMultiplierBonus;

        // 스킬트리 "힘" 강화: 쿨 5초 이상 스킬엔 힘의 몫이 한 번 더 들어간다(= 힘 효과 2배).
        if (MetaBonuses.StrengthSlowSkillDouble && strengthDamageBonus > 0f
            && skill.Cooldown >= StrengthDoubleCooldown)
            damageMultiplier += strengthDamageBonus;

        float damage = skill.Damage * damageMultiplier;

        // 스킬트리 "가속" 강화: 쿨 4초 이하 스킬 피해 +30%. **가속 패시브를 보유했을 때만** 얹힌다.
        if (MetaBonuses.AccelFastSkillDamage && skill.Cooldown <= AccelFastSkillCooldown
            && passives != null && passives.HasPassive(PassiveSkillId.Accel))
            damage *= 1f + AccelFastSkillDamageBonus;

        // 낙뢰 연계 path2 T3: 낙뢰 버프 중첩당 전체 공격 피해량 증가
        if (LightningStorm.StackDamageEnabled && LightningStorm.ActiveStackCount > 0)
            damage *= 1f + LightningStorm.StackDamageBonusPerStack * LightningStorm.ActiveStackCount;

        // 되감기 R0 「충전 되감기」(path1): 되감기 직후 사용하는 스킬(되감기 제외)의 피해를 1회 증가시킨다.
        if (consumeRewindBonus && skillId != ActiveSkillId.Rewind && nextSkillDamageBonus > 0f)
        {
            damage *= 1f + nextSkillDamageBonus;
            nextSkillDamageBonus = 0f;
        }

        if (skill.Damage > 0f) BotInput.OnBaseDamage?.Invoke(skillId, damage / skill.Damage); // 봇 관측(평소 null)
        BotInput.OnDamageShares?.Invoke(skillId, damageMultiplier, strengthDamageBonus + damageMultiplier - passiveDamageMultiplier, MetaBonuses.AccelFastSkillDamage && skill.Cooldown <= AccelFastSkillCooldown && passives != null && passives.HasPassive(PassiveSkillId.Accel) ? AccelFastSkillDamageBonus : 0f); // 봇 관측(평소 null)
        return damage;
    }

    private bool FireBasicAttack(EquippedSkill skill, float damage, float critChance, bool allowBonusShot)
    {
        // 적이 화면에 없어도 발사는 되어야 한다(허공에 쏘더라도 키 입력에 무반응인 건 고장난 것처럼 느껴짐).
        int pierce = skill.ExtraPierce + MetaBonuses.ArrowExtraPierce; // 레벨업 고유 강화 + 스킬트리 "화살 관통"

        // R0(암살 연계, path1) = **개쎈 화살 한 발**. 여러 발 쏘던 것을 한 발로 모으고 관통을 무한으로 준다
        // (방패 블루베리는 그래도 막는다 — Projectile이 BlocksProjectiles에서 끊는다. 2026-08-06 명세 그대로).
        if (skill.PathTier[1] >= 2) pierce = int.MaxValue;

        // R1(독수리 연계, path2) T2+ = 기본공격이 **통째로 화살비로 바뀐다.** 정면 화살은 나가지 않는다.
        // 예전엔 정면 화살과 비가 같이 쏟아져 "중복 출력"으로 보였다(8/24 플레이스루).
        // ⚠️ 그 대가로 ExtraPierce가 이 진화에선 죽은 스탯이 된다 — 사용자 결정.
        if (ArrowRainReplacesShot(skill))
        {
            StartCoroutine(ArrowRainRoutine(skill, damage, critChance, ArrowRainWaveCount(skill)));
        }
        else
        {
            // 첫 발은 즉시(입력 반응성), 추가 발사는 시간차를 두고 연사한다.
            SpawnBasicAttackProjectile(skill, damage, critChance, pierce, 0f, allowBonusShot);
            if (BasicAttackBurstCount(skill) > 0)
                StartCoroutine(BasicAttackBurst(skill, damage, critChance, pierce, allowBonusShot));
        }

        TriggerAttackBody();
        return true;
    }

    // ── 화살 R1(독수리 연계, path2): 화살비 ──
    // 화면 위쪽에서 비스듬히(왼쪽 아래로) 쏟아진다. 각 화살은 **관통이 없다**(명세) — 한 마리 맞히고 사라진다.
    // ⚠️ Projectile은 자기 로컬 left로만 날아간다 → 방향은 회전으로 준다. 스프라이트도 같이 기울어 그림이 맞는다.
    private const float ArrowRainAngle = 50f;          // Vector2.left 기준 시계 방향 = 왼쪽 아래
    private const float ArrowRainWaveInterval = 0.35f;
    private const int ArrowRainArrowsPerWave = 10;     // **화면 폭당** 발수 — 뿌리는 폭이 넓어지면 발수도 같은 밀도로 는다
    // 🔴 화살비는 정면 화살을 대체하므로 화살 한 발이 곧 본체다 — 비율 1(사용자 결정 2026-09-28).
    private const float ArrowRainDamageRatio = 1f;
    private const float ArrowRainMaxSpawnLift = 2f;    // 화면 위 가장자리에서 최대 이만큼 위에 생긴다

    // 떨어지면서 빨라진다 — 하늘에서 막 놓인 듯 느리게 시작해 바닥에 꽂힐 즈음 가장 빠르다.
    // 등속으로 내리면 화살이 "떠내려오는" 것처럼 보여 무게가 없었다(8/24 플레이스루: 낙하 속도감 필요).
    private const float ArrowRainStartSpeed = 0.55f;    // 시작 속도 배율
    private const float ArrowRainAcceleration = 3.2f;   // 초당 배율 증가 — 낙하 약 0.64초 동안 5.5 → 26유닛/초

    // 레벨업 "추가 투사체"는 화살비에선 **비가 오는 시간**으로 읽는다(정면 화살이 없어 발수로 쓸 데가 없다).
    // Prog_BasicAttack이 만렙까지 추가 투사체를 3회(레벨 2·6·10) 주므로 2웨이브 → 최대 5(T3면 6)웨이브가 된다.
    private const int ArrowRainBaseWaves = 2;
    private const int ArrowRainMaxWaves = 6;

    // T2부터 기본공격이 화살비로 교체된다. 이 조건이 곧 "정면 화살이 안 나간다"는 뜻이라 한 곳에 모아 둔다.
    // ⚠️ **기본공격 전용 진화다.** Id를 안 보면 path2를 2티어까지 올린 **다른 스킬**까지 걸려서
    //    레벨업 카드에 "화살비 +N차례"가 뜬다(호밍 "소형 미사일 다발"에서 실제로 그랬다).
    private static bool ArrowRainReplacesShot(EquippedSkill skill) =>
        skill.Id == ActiveSkillId.BasicAttack && skill.PathTier[2] >= 2;

    private static int ArrowRainWaveCount(EquippedSkill skill) =>
        Mathf.Min(ArrowRainBaseWaves + (skill.PathTier[2] >= 3 ? 1 : 0) + skill.ExtraProjectiles,
                  ArrowRainMaxWaves);

    private IEnumerator ArrowRainRoutine(EquippedSkill skill, float damage, float critChance, int waves)
    {
        for (int w = 0; w < waves; w++)
        {
            SpawnArrowRainWave(skill, damage, critChance);
            yield return new WaitForSeconds(ArrowRainWaveInterval);
        }
    }

    // 한 차례의 화살비. 1차 「화살비」는 시전할 때 이 함수를 웨이브 수만큼 반복하고,
    // 2차 「하늘 파쇄기」는 시전 없이 UpdateSkyShredderRain이 상시로 반복한다.
    private void SpawnArrowRainWave(EquippedSkill skill, float damage, float critChance)
    {
        if (basicAttackProjectilePrefab == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;
        float centerX = cam.transform.position.x;
        float topY = cam.transform.position.y + halfHeight;

        // 화살은 왼쪽 아래로 비스듬히 떨어져서, 화면 폭 위에서만 뿌리면 오른쪽 끝 화살도 플레이어 한참 앞에 꽂힌다
        // (캐릭터 바로 앞이 사각지대였다). 그래서 **화면 오른쪽 바깥(캐릭터 뒤)에서도** 쏜다 —
        // 가장 높이 생긴 화살도 플레이어 x까지 닿는 지점까지 뿌리는 폭을 늘린다.
        // ⚠️ 이 스폰은 화면 한참 밖이다 — Projectile.IsFarOffscreen이 **화면 쪽으로 날아오는** 투사체를 안 지워서 산다.
        float leftX = centerX - halfWidth;
        float driftPerDrop = 1f / Mathf.Tan(ArrowRainAngle * Mathf.Deg2Rad); // 낙하 1유닛당 왼쪽으로 가는 거리
        float rightX = Mathf.Max(centerX + halfWidth,
            transform.position.x + (topY + ArrowRainMaxSpawnLift - transform.position.y) * driftPerDrop);
        // 🔴 하늘 파쇄기는 시전이 없어 "차례 수"(ExtraProjectiles)가 쓰이지 않는다 — 그 몫을 여기로 돌린다.
        //    화살이 화면에 더 많아지는 것이 이 진화의 성장축이다(2026-09-29 사용자).
        // 🔴 하늘 파쇄기는 기본 밀도도 2배다(2026-10-06 사용자 "더 빽빽하게 쏴서 화면을 채워줘") — 화면 폭당 10 → 20발.
        int perWaveBase = IsContinuousRain(skill) ? SkyShredderArrowsPerWave + skill.ExtraProjectiles : ArrowRainArrowsPerWave;
        int arrowsPerWave = Mathf.Max(2, Mathf.RoundToInt(perWaveBase * (rightX - leftX) / (2f * halfWidth)));

        float rainDamage = damage * ArrowRainDamageRatio;
        // 2차 「하늘 파쇄기」만 전용 화살 그림을 쓴다(1차 「화살비」는 원본 화살 그대로).
        bool skyShredderArrows = skill.PathTier[2] >= 3;

        for (int i = 0; i < arrowsPerWave; i++)
        {
            // 뿌리는 폭에 고르게 흩되 매 발 조금씩 흔들어 격자처럼 보이지 않게 한다.
            float t = (i + Random.Range(-0.3f, 0.3f)) / (arrowsPerWave - 1);
            float x = Mathf.Lerp(leftX, rightX, Mathf.Clamp01(t));
            Vector3 spawn = new Vector3(x, topY + Random.Range(0.5f, ArrowRainMaxSpawnLift), 0f);

            GameObject obj = ObjectPool.Instance.Spawn(basicAttackProjectilePrefab, spawn, Quaternion.Euler(0f, 0f, ArrowRainAngle));
            obj.transform.localScale *= skill.Scale;
            if (skyShredderArrows) ApplySkyShredderArrowSprite(obj);
            Projectile p = obj.GetComponent<Projectile>();
            if (p == null) continue;
            p.Damage = rainDamage;
            p.CritChance = critChance;
            p.SpeedMultiplier = skill.ProjectileSpeedMultiplier * ArrowRainStartSpeed;
            p.Acceleration = skill.ProjectileSpeedMultiplier * ArrowRainAcceleration;
            // 명세: 1차 화살비의 각 화살은 관통 없음. 2차 하늘 파쇄기만 1회 관통한다(2026-10-06 사용자).
            p.PierceRemaining = skyShredderArrows ? SkyShredderPierce : 0;
        }
    }

    // ── 하늘 파쇄기(화살 R1 2차)의 상시 화살비 ───────────────────────────────
    // 🔴 이 진화는 **시전이 아니라 상태**다(사용자 2026-09-28: "쿨타임도 지속시간도 없고 그냥 계속 내린다").
    //    우주선(UpdateSkyShredderShip)이 상시 떠 있는 판정과 같은 조건을 쓰고, 화살도 같이 상시로 내린다.
    //    그래서 이 스킬은 쿨타임을 쓰지 않는다 — 자동 시전 분기에서도 빠진다.
    // 레벨업 **타격 간격**이 내리는 밀도가 된다. 쿨타임 스텝은 이 진화에서 아무 효과가 없으므로 커브에 넣지 말 것.
    public static bool IsContinuousRain(EquippedSkill skill) =>
        skill.Id == ActiveSkillId.BasicAttack && skill.PathTier[2] >= 3;

    private float skyShredderRainTimer;
    private const float SkyShredderMinInterval = 0.08f;   // 밀도 상한 — 프레임당 스폰이 터지지 않게 막는다
    private const int SkyShredderArrowsPerWave = 20;      // 화면 폭당 발수(1차 화살비는 ArrowRainArrowsPerWave 10)
    private const int SkyShredderPierce = 1;              // 화살 한 발이 적 둘까지 맞힌다

    private void UpdateSkyShredderRain()
    {
        EquippedSkill arrow = null;
        for (int i = 0; i < equippedSkills.Count; i++)   // LINQ는 매 프레임 열거자를 할당한다 — 쓰지 않는다
            if (IsContinuousRain(equippedSkills[i])) { arrow = equippedSkills[i]; break; }
        if (arrow == null) { skyShredderRainTimer = 0f; return; }

        skyShredderRainTimer -= Time.deltaTime;
        if (skyShredderRainTimer > 0f) return;
        skyShredderRainTimer = Mathf.Max(SkyShredderMinInterval, ArrowRainWaveInterval * arrow.TickIntervalMult);
        // 🔴 되감기 강화는 소비하지 않는다 — 상시로 도는 것이 먹으면 플레이어가 몰아줄 수 없다.
        SpawnArrowRainWave(arrow, ComputeBaseDamage(arrow, consumeRewindBonus: false), GetCritChance(arrow));
    }

    // R0(암살 연계)은 **한 발**로 모은다 — 큰 화살 하나가 줄을 통째로 뚫는 게 정체성이라 연사가 있으면 안 된다.
    // (R1(독수리 연계)은 아예 이 함수를 안 탄다 — FireBasicAttack이 화살비로 갈아타 정면 화살을 안 쏜다.)
    // ⚠️ 영구 스탯을 깎지 않고 매 캐스트 실시간으로 계산한다 — 진화 후 레벨업으로 발수를 더 얻어도 그대로 유지된다.
    private static int BasicAttackBurstCount(EquippedSkill skill) =>
        skill.PathTier[1] >= 2 ? 0 : skill.ExtraProjectiles;

    // 추가 발사체를 "두두두둑" 쏟아낸다 — 발사마다 간격을 두고, 세로로 위·아래 번갈아 조금씩 어긋나게.
    // 예전엔 전부 같은 프레임에 0.4씩 위로 쌓아 올려 한 덩어리로 보였다(여러 발 맞는 게 안 보임).
    private IEnumerator BasicAttackBurst(EquippedSkill skill, float damage, float critChance, int pierce, bool allowBonusShot)
    {
        int count = BasicAttackBurstCount(skill); // 캐스트 시점 값으로 고정(도중에 레벨업해도 이번 연사는 그대로)
        for (int i = 1; i <= count; i++)
        {
            yield return new WaitForSeconds(BalanceConstants.BasicAttackBurstInterval);
            // +0.13 / -0.13 / +0.26 / -0.26 … 위아래로 번갈아 벌어진다
            float sign = i % 2 == 1 ? 1f : -1f;
            float magnitude = Mathf.CeilToInt(i * 0.5f) * BalanceConstants.BasicAttackBurstYOffset;
            SpawnBasicAttackProjectile(skill, damage, critChance, pierce, sign * magnitude, allowBonusShot);
        }
    }

    private void SpawnBasicAttackProjectile(EquippedSkill skill, float damage, float critChance, int pierce, float verticalOffset, bool allowBonusShot)
    {
        // 정면 화살은 레인 높이로 날아가 높이 떠 있는 비행 적에는 히트박스가 안 닿을 뿐, 비행 전용 규칙은 없다(Enemy.cs의 requiresAntiAir 폐지 주석).
        // 그쪽은 화살비(위에서 떨어짐)와 추적 화살(대상 지정)이 맡는다.
        GameObject obj = ObjectPool.Instance.Spawn(basicAttackProjectilePrefab, transform.position + Vector3.left * 0.6f + Vector3.down * 0.25f + Vector3.up * verticalOffset, Quaternion.identity);
        obj.transform.localScale *= skill.Scale;
        Projectile projectile = obj.GetComponent<Projectile>();
        projectile.Damage = damage;
        projectile.CritChance = critChance;
        projectile.SpeedMultiplier = skill.ProjectileSpeedMultiplier
            * (skill.PathTier[1] >= 2 ? AssassinArrowSpeedMult : 1f);
        projectile.PierceRemaining = pierce;
        // 검은 화살(암살 연계 진화)은 방패 블루베리를 뚫는다(2026-09-19 사용자).
        // 미진화 정면 화살·화살비는 그대로 막힌다 — Projectile.OnEnable이 매 스폰마다 false로 되돌린다.
        projectile.PiercesShields = skill.PathTier[1] >= 2;
        if (skill.PathTier[1] >= 2) ApplyEvolvedArrowSprite(obj, skill.PathTier[1] >= 3);

        // R0(암살 연계, path1): 이 화살이 **치명타로 때린 적마다** 기본 화살이 따로 날아가 그 적만 노린다.
        // 관통 무한이라 한 발이 여러 적에게 치명타를 낼 수 있고, 그때마다 각각 따라붙는다(명세 그대로).
        // 🔴 **2차 진화(PathTier 3)부터다**(2026-09-07 사용자 결정). 1차(PathTier 2)는 추가 투사체 없이
        //    "개쎈 화살 한 발"만으로 간다 — 그것만으로 충분히 세고, 1차에 얹으니 잘 안 보였다.
        //    ⚠️ `TargetPathTier`가 1차→2, 2차→3으로 매핑한다. `>= 2`로 쓰면 **1차부터** 켜진다.
        bool critChaseArrow = allowBonusShot && skill.PathTier[1] >= 3;
        // 노션 문구가 "**수많은** 추격 화살이 커다란 화살 곁에 따라붙는다"라 2발로는 문구에 한참 못 미쳤다(2026-09-19).
        // 레벨업 "투사체" 몫이 여기 더해져 만렙까지 더 늘어난다.
        int chaseArrows = ChaseArrowBaseCount + skill.ExtraProjectiles;

        projectile.OnHitBonus = (hitEnemy, hitCrit) =>
        {
            if (critChaseArrow && hitCrit && hitEnemy != null)
                for (int i = 0; i < chaseArrows; i++)
                    SpawnChasingArrow(skill, hitEnemy, damage, critChance);
        };
    }

    // 진화한 화살 그림으로 교체. 프리팹의 SpriteRenderer가 자식에 있을 수도 있어 자식까지 훑는다.
    // 2차 그림이 안 배선돼 있으면 1차 그림으로, 그것도 없으면 아무것도 안 한다(원본 화살로 그대로 날아간다).
    private void ApplyEvolvedArrowSprite(GameObject projectileObj, bool tier2)
    {
        Sprite[] frames = tier2 && evolvedArrowFramesTier2 != null && evolvedArrowFramesTier2.Length > 0
            ? evolvedArrowFramesTier2
            : evolvedArrowFrames;
        if (frames == null || frames.Length == 0) return;

        SpriteRenderer sr = projectileObj.GetComponentInChildren<SpriteRenderer>();
        if (sr == null) return;
        sr.sprite = frames[0];
        if (frames.Length < 2) return; // 한 장뿐이면 굳이 플립북을 안 붙인다

        // 화살은 명중하거나 화면 밖으로 나가서 반납되므로 **루프**로 돌린다
        // (비루프면 SpriteFlipbook이 다 재생한 순간 날아가던 화살을 풀에 반납해 버린다).
        SpriteFlipbook fb = sr.GetComponent<SpriteFlipbook>();
        if (fb == null) fb = sr.gameObject.AddComponent<SpriteFlipbook>();
        fb.enabled = true; // 풀에서 꺼낸 화살이면 Projectile.OnEnable이 꺼 둔 상태다
        fb.Play(frames, evolvedArrowFps, true);
    }

    // 하늘파쇄기 세트의 날렵한 화살 한 장으로 갈아끼운다(플립북 없음 — 한 장짜리 정지 그림이다).
    // 반납될 때 `Projectile.OnEnable`이 `baseSprite`로 되돌리므로 풀 재사용 누수는 없다.
    private void ApplySkyShredderArrowSprite(GameObject projectileObj)
    {
        if (skyShredderArrowSprite == null) return;   // 미배선이면 원본 화살 그대로
        SpriteRenderer sr = projectileObj.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.sprite = skyShredderArrowSprite;
    }

    // R0의 따라붙는 기본 화살 — **관통은 없다**(그 대상만 때리고 사라진다).
    // 2026-08-26 사용자 요청으로 셋이 바뀌었다: ① 딸기가 아니라 **암살 화살 뒤쪽**에서 나오고
    // ② **포물선**을 그리며 날아가고 ③ 일반 화살의 **절반 크기**다.
    // 암살 연계(path1) 진화 화살은 느리게 날고 쿨이 길다 — "한 발이 무겁다"를 손으로 느끼게 한다.
    // 🔴 2026-09-19 사용자: 0.6은 "굼벵이 같다"고 해서 0.85로 올렸다. 쿨 +3초는 그대로 둔다 —
    //    "한 발이 무겁다"의 나머지 절반이고, 속도만으로 답답함이 풀리는지 먼저 본다.
    private const float AssassinArrowSpeedMult = 0.85f;  // 정면 화살 속도의 85%

    // 치명타 1회에 따라붙는 추격 화살 수. 🔴 5 → 2 (2026-10-07 사용자: "너무 많다, 절반 정도로").
    //    진화 전 화살의 투사체 카드(최대 +3)가 그대로 넘어와 더해진다 — 여기 값만 보고 "2발"이라 읽지 말 것.
    private const int ChaseArrowBaseCount = 2;
    // 🔴 0.5 → 0.05 (사용자 결정 2026-09-28: 본체 피해의 5%). 치명타 한 번에 5발 이상이 붙고
    //    본체 피해가 2차에서 1,100을 넘으므로, 0.5면 추격 화살 쪽이 본체보다 총 피해가 커졌다.
    private const float ChasingArrowDamageRatio = 0.05f;
    // 2026-09-19 사용자: 전용 그림(skyShredderArrowSprite)이 들어오면서 1배로 확정했었다.
    // 🔴 2026-10-06 사용자가 직접 **절반**으로 줄였다(크기 0.5 · 속도 3배).
    private const float ChasingArrowScale = 0.5f;
    private const float ChasingArrowSpeedMult = 3f;
    private const float ChasingArrowRiseOffset = 0.35f;    // 딸기의 발사 지점보다 살짝 위에서 나간다
    // 🔴 궤적은 **포물선**이다(2026-10-06 사용자 "유도된다는 느낌보다는 … 진짜 화살이 날아가는 것처럼").
    //    종전엔 "위로 붕 뜨는 로브가 아니라 호밍 미사일처럼 가로로 휘어 들어가는" 유도였다 — 사용자가 그 방향을 뒤집었다.
    //    정점 높이 = 비행 거리 × 아래 비율(Projectile.LaunchArc). 한 번에 5발 이상이 같은 자리에서 같은 적으로 나가므로
    //    발마다 비율을 흩어 한 줄로 겹치지 않게 한다.
    private const float ChasingArrowArcMin = 0.12f;
    private const float ChasingArrowArcMax = 0.28f;

    private void SpawnChasingArrow(EquippedSkill skill, Enemy target, float damage, float critChance)
    {
        if (basicAttackProjectilePrefab == null) return;

        // 🔴 치명타는 **대개 그 적을 죽인다** — 암살 사격은 한 발이 무거운 데다 치명타 배율(기본 2배)까지 얹힌다.
        //    그리고 Enemy.TakeDamage는 같은 호출 안에서 동기적으로 isDead를 세우므로,
        //    Projectile이 OnHitBonus를 부를 때는 이미 `target.IsAlive == false`다.
        //    예전엔 여기서 그대로 return해서 **치명타가 떠도 추격 화살이 한 번도 안 보였다.**
        //    → 대상이 죽었으면 그 자리에서 가장 가까운 산 적으로 갈아탄다(2026-08-25 사용자 결정).
        Vector3 hitPos = target != null ? target.transform.position : transform.position;
        if (target == null || !target.IsAlive)
            target = Projectile.NearestLivingEnemy(hitPos);
        if (target == null) return; // 화면에 산 적이 하나도 없을 때만 안 쏜다

        // 🔴 **딸기에게서** 나간다(2026-09-07 사용자 결정 — 명중 지점 뒤에서 짧게 나가니 안 보였다).
        //    화면을 가로질러 적을 쫓아가므로 호밍 궤적이 눈에 들어온다. 발사 지점은 정면 화살과 같은 총구.
        Vector3 origin = transform.position + Vector3.left * 0.6f + Vector3.down * 0.25f
                         + Vector3.up * ChasingArrowRiseOffset;
        Vector2 toTarget = (Vector2)target.transform.position - (Vector2)origin;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        // 첫 프레임 그림이 목표 쪽을 보게만 돌려 둔다 — 이후 방향은 포물선(Projectile.UpdateArc)이 매 프레임 정한다.
        // Projectile의 앞은 자기 로컬 left다(+180).
        float angle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg + 180f;

        GameObject obj = ObjectPool.Instance.Spawn(basicAttackProjectilePrefab, origin, Quaternion.Euler(0f, 0f, angle));
        obj.transform.localScale *= skill.Scale * ChasingArrowScale;
        ApplySkyShredderArrowSprite(obj);   // 2026-09-19 사용자 지시 — 추격 화살은 하늘파쇄기 화살 그림
        Projectile p = obj.GetComponent<Projectile>();
        if (p == null) return;
        // 레벨업 "추격 화살 피해"(SubDamage)가 곱해진다 — 2026-10-06 사용자: 본체 피해 카드를 빼고 이쪽을 키운다.
        p.Damage = damage * ChasingArrowDamageRatio * skill.SubDamageMult;
        p.CritChance = critChance;
        p.SpeedMultiplier = skill.ProjectileSpeedMultiplier * ChasingArrowSpeedMult;
        p.PierceRemaining = 0;

        // 목표까지 포물선으로 날아간다. 날아가는 동안 대상이 죽으면 다른 적으로 갈아탄다.
        p.LaunchArc(target, Random.Range(ChasingArrowArcMin, ChasingArrowArcMax));
    }

    // source: 데미지 집계에 어느 스킬로 잡힐지. 화살 R1은 화살, 스나이핑 R0은 스나이핑으로 잡혀야 한다.
    // marked: true면 독수리를 바로 떨구지 않고 **마크를 먼저 그린다**(스나이핑 R0 2차 「독수리 특공대 지휘관」).
    private void SpawnMiniEagleSpread(Enemy primary, float damage, float critChance, float scale, int maxTargets, ActiveSkillId source = ActiveSkillId.BasicAttack, bool marked = false)
    {
        StartCoroutine(marked ? SnipingMarkStrike(primary, damage, critChance, scale, source)
                              : MiniEagleBonus(primary, damage, critChance, scale, source));
        if (maxTargets <= 1) return;

        IEnumerable<Enemy> nearby = Enemy.Active
            .Where(e => e != null && e != primary && Vector2.Distance(primary.transform.position, e.transform.position) <= 6f)
            .OrderBy(e => Vector2.Distance(primary.transform.position, e.transform.position))
            .Take(maxTargets - 1);

        foreach (Enemy e in nearby)
            StartCoroutine(marked ? SnipingMarkStrike(e, damage, critChance, scale, source)
                                  : MiniEagleBonus(e, damage, critChance, scale, source));
    }

    // ── 스나이핑: 가장 체력 높은 적(들)을 5회씩 저격 ──
    private bool FireSniping(float damage, float critChance, EquippedSkill skill)
    {
        int targets = 1 + skill.ExtraTargets; // 레벨업 주 성장축: 동시 저격 대상 수
        if (MetaBonuses.SnipingExtraTarget) targets += 1; // 스킬트리 "한 발에 두 놈": +1 타겟

        List<Enemy> chosen = Enemy.Active
            .Where(e => e != null)
            .OrderByDescending(e => e.CurrentHealth)
            .Take(targets)
            .ToList();
        if (chosen.Count == 0) return false; // 조준할 적 없음 → 캐스트 실패

        // R0(독수리 연계, path1) = 저격이 꽂힌 자리 **주변 적에게 독수리를 떨군다**(2026-08-06 명세).
        // 화살 R1의 미니 독수리 확산과 같은 장치를 재사용한다 — 단일 대상기였던 스나이핑에 광역이 붙는다.
        bool eagleSplash = skill.PathTier[1] >= 2;
        int eagleTargets = skill.PathTier[1] >= 3 ? 8 : 4;
        // 🔴 미니 독수리는 본체와 별개로 **주변 적 각각에게** 들어가는 서브 딜이다 — 1차 120% · 2차 300%(2026-09-28).
        float eagleRatio = skill.PathTier[1] >= 3 ? 3.0f : 1.2f;

        TriggerAttackBody();
        int shots = SnipingBaseShots + skill.ExtraProjectiles; // 레벨업 보조축: 대상당 연사 수
        // 자동방어 그림(Effect_SplashSniping)은 R1(자동 방어 시스템) 루트만 쓴다. R0(독수리 저격)는 기본 스나이핑 그림 그대로다(2026-10-06 사용자).
        foreach (Enemy target in chosen)
            StartCoroutine(SnipeTarget(target, damage, critChance, eagleSplash, eagleRatio, eagleTargets, shots, skill.Scale,
                                       evolved: skill.PathTier[2] >= 2, snipingCommander: skill.PathTier[1] >= 3));
        return true;
    }

    // ── 스나이핑 R1 2차 「사이버네틱 벙커」 ──────────────────────────────────
    // 🔴 2026-09-19 사용자 명세: "피해를 입을 때마다 캐릭터 주위 일정 거리에 있는 **모든 적**에게
    //    자동방어시스템 공격을 **3회** 가한다. 능력 쿨타임 5초. 발동하면 벙커 이펙트를 캐릭터 **앞**에 출력."
    //    노션 문구도 같다: "피해를 입으면 자동으로 가까운 적들을 쓸어버린다".
    // ⚠️ 쿨타임은 스킬 쿨(자동 시전)과 **별개**다 — 이건 피격 반응 능력의 자체 쿨이다.
    private const float BunkerCooldown = 5f;
    private const int BunkerShots = 3;
    private const float BunkerRadius = 5f;
    // (벙커는 캐릭터 중앙에 겹쳐 띄운다 — 옆으로 밀지 않는다. 2026-09-20 사용자)
    private const float BunkerVfxLifetime = 1.2f;

    [SerializeField] private GameObject bunkerVfxPrefab; // 벙커 그림(Bunker_1~4 플립북). 미배선이면 판정만 돈다
    private float bunkerReadyAt;

    private void HandleBunkerRetaliation(int amount)
    {
        if (Time.time < bunkerReadyAt) return;

        EquippedSkill sniping = null;
        for (int i = 0; i < equippedSkills.Count; i++)
            if (equippedSkills[i].Id == ActiveSkillId.Sniping) { sniping = equippedSkills[i]; break; }
        if (sniping == null || sniping.PathTier[2] < 3) return;

        // 레벨업 "벙커 재가동"(TickRate) — 스킬 쿨과 별개인 이 능력 자체의 쿨을 줄인다(2026-09-29 사용자).
        bunkerReadyAt = Time.time + Mathf.Max(0.5f, BunkerCooldown * sniping.TickIntervalMult);

        if (bunkerVfxPrefab != null)
        {
            // 🔴 **벙커 중앙 = 캐릭터 중앙**(2026-09-20 사용자). 화면 밖으로 상당 부분 잘리는 게 의도다.
            //    프리팹이 sortingOrder 1000으로 **모든 레이어 제일 앞**에 오고 좌우반전돼 있다.
            SpriteRenderer body = GetComponentInChildren<SpriteRenderer>();
            Vector3 at = body != null ? body.bounds.center : transform.position;
            ObjectPool.Instance.SpawnTimed(bunkerVfxPrefab, at, BunkerVfxLifetime);
        }

        float damage = ComputeBaseDamage(sniping);
        float critChance = GetCritChance(sniping);
        float sqrRadius = BunkerRadius * BunkerRadius;
        Vector2 self = transform.position;

        // 반경 안의 **모든** 적에게 자동방어시스템 사격을 3회씩. SnipeTarget을 그대로 재사용해
        // 스파크 그림·연사 간격·데미지 집계가 평소 스나이핑과 같게 나간다(독수리 확산은 끈다 — R1 루트다).
        bunkerTargets.Clear();
        IReadOnlyList<Enemy> active = Enemy.Active;
        for (int i = 0; i < active.Count; i++)
        {
            Enemy e = active[i];
            if (e == null || !e.IsAlive) continue;
            if (((Vector2)e.transform.position - self).sqrMagnitude > sqrRadius) continue;
            bunkerTargets.Add(e);
        }
        // 코루틴이 도는 동안 Enemy.Active가 바뀌므로 **복사본**을 돈다.
        for (int i = 0; i < bunkerTargets.Count; i++)
            StartCoroutine(SnipeTarget(bunkerTargets[i], damage, critChance,
                                       eagleSplash: false, eagleRatio: 0f, eagleTargets: 0,
                                       shots: BunkerShots, scale: sniping.Scale, evolved: true));
    }

    private readonly List<Enemy> bunkerTargets = new List<Enemy>(32);

    // snipingCommander: R0 2차 「독수리 특공대 지휘관」 — 독수리를 바로 떨구지 않고 마크를 먼저 그린다.
    private IEnumerator SnipeTarget(Enemy target, float damage, float critChance, bool eagleSplash, float eagleRatio, int eagleTargets, int shots, float scale, bool evolved, bool snipingCommander = false)
    {
        // 🔴 한 캐스트의 저격은 **전부 같은 그림**을 쓴다. 예전엔 첫 발만 Effect_SplashSniping이었는데,
        //    두 세트는 "기본 vs 화려한 버전"이 아니라 **색 계열이 아예 다르다**(실측: Sniping 계열 hue 42°=주황 /
        //    SplashSniping 계열 hue 214°=하늘·회색). 그래서 미진화 스나이핑에 진화 색이 한 발씩 섞여 나왔다.
        GameObject sparkPrefab = evolved && snipingSplashPrefab != null ? snipingSplashPrefab : snipingEffectPrefab;

        for (int i = 0; i < shots; i++)
        {
            if (target == null) yield break;
            Vector3 pos = target.transform.position;

            if (sparkPrefab != null)
                ObjectPool.Instance.Spawn(sparkPrefab, pos, Quaternion.identity);

            target.TakeSkillHit(damage, critChance, ActiveSkillId.Sniping);

            // R0: 첫 발이 꽂힐 때 그 자리 주변으로 독수리를 떨군다. 연사마다 부르면 한 캐스트에 수십 마리가 되므로 **첫 발만**.
            if (eagleSplash && i == 0 && target != null)
                SpawnMiniEagleSpread(target, damage * eagleRatio, critChance, scale * 0.6f, eagleTargets,
                                     ActiveSkillId.Sniping, marked: snipingCommander);

            yield return new WaitForSeconds(SnipingShotInterval);
        }
    }


    // ── 호밍 미사일: 적 추적 미사일 5개(성장형) ──
    // 미사일 수. 발사(FireHoming)와 레벨업 카드(DescribeStep)가 같이 쓴다 — extraProjectiles만 바꿔 넣어 증가량을 센다.
    // R1(가속 연계, path2): **더 작은 미사일을 더 여러 개**(2026-08-06 명세).
    // 2차 「저 하늘의 별처럼」 = "셀 수 없이 많은 미사일로 세상을 뒤덮는다" — 1차의 2배에서 **5배**로 올린다
    //    (2026-09-08. 새 그림이 필요 없는 진화라 물량으로만 간다).
    // ⚠️ 스킬트리 "더 많은 폭격"의 누적 보너스는 레벨업과 무관해 FireHoming이 따로 더한다.
    private static int HomingMissileCount(EquippedSkill skill, int extraProjectiles)
    {
        int count = HomingBaseMissiles + extraProjectiles;
        if (skill.PathTier[2] >= 3) count *= 5;
        else if (skill.PathTier[2] >= 2) count *= 2;
        return count;
    }

    // 호밍 미사일 발사각. 진화 전·R0은 좁게(±60°), R1 「소형 미사일 다발」 계통은 넓게 편다.
    private const float HomingSpreadHalfAngle = 60f;
    private const float HomingSpreadJitter = 15f;
    private const float HomingWideSpreadHalfAngle = 105f;
    private const float HomingWideSpreadJitter = 22f;

    // ── 호밍 R0 2차 「초강력 슈퍼 로켓」 ────────────────────────────────────
    // 전용 프리팹을 따로 둔다 — 로켓 그림(SuperMissile/Effect_SuperMissile)과
    // **전용 폭발**(SuperMissile/Effect_Explosion_1~7)을 프리팹이 같이 들고 있어야 한다.
    // ⚠️ 그 폭발은 루트의 `Effect_Explosion`(호밍 1차·폭탄 독수리 공용)과 **다른 에셋**이다. 섞지 말 것.
    // 미배선이면 평범한 호밍 미사일 프리팹으로 떨어진다(판정은 그대로, 그림만 수수해진다).
    [SerializeField] private GameObject superRocketPrefab;
    private const float SuperRocketScale = 2.6f;         // "거대한" 로켓 — 평소 미사일의 2.6배
    // 🔴 슈퍼 로켓은 다발 발사를 통째로 대체하므로 그 한 발이 곧 본체다 — 배율 1(2026-09-28).
    private const float SuperRocketDamageMult = 1f;
    private const float SuperRocketExplodeRadius = 5f;   // "주위 적들에게 큰 데미지"
    // 🔴 진화로 생기는 폭발 피해는 미사일 피해의 80%다(사용자 결정 2026-09-28) — 「묵직한 탄두」와 「슈퍼 로켓」이 같은 값을 썼다.
    //    슈퍼 로켓의 미사일 피해는 `Evo_Homing_R0_T2.baseDamage`가 단독으로 정하므로 폭발도 그 값의 80%다.
    public const float HomingExplodeRatio = 0.8f;
    // 🔴 1차 「묵직한 탄두」만 50%로 내렸다(2026-10-06 사용자 너프). 위 80%는 이제 2차 「슈퍼 로켓」 전용이다.
    private const float HeavyWarheadExplodeRatio = 0.5f;

    private void FireSuperRocket(float missileDamage, float critChance, EquippedSkill skill)
    {
        GameObject prefab = superRocketPrefab != null ? superRocketPrefab : homingMissilePrefab;
        if (prefab == null) return;

        GameObject obj = ObjectPool.Instance.Spawn(prefab, transform.position + Vector3.up * 0.2f, Quaternion.identity);
        obj.transform.localScale *= SuperRocketScale;
        HomingMissile m = obj.GetComponent<HomingMissile>();
        if (m == null) return;
        m.Damage = missileDamage * SuperRocketDamageMult;
        m.CritChance = critChance;
        m.Explode = true;
        m.ExplodeRadius = SuperRocketExplodeRadius * skill.SubScaleMult;
        m.ExplodeVfxMult = skill.SubScaleMult;
        m.ExplodeRatio = HomingExplodeRatio * skill.SubDamageMult;  // 레벨업 "폭발 피해"
        m.SpeedMultiplier = skill.ProjectileSpeedMultiplier;
        m.BossDamageBonus = skill.BossDamageBonus;                  // 레벨업 "보스 추가 피해"
        m.TargetHighestHealth = true;   // 체력 1위를 쫓는다
        m.Init(Vector2.left);
    }

    private bool FireHoming(float damage, float critChance, EquippedSkill skill)
    {
        if (homingMissilePrefab == null) return false;

        // 성장: 사용할수록 강해짐(이번 판 한정). 상한이 없어 판이 길수록 혼자 세진다 — 알려진 성질.
        // 0.08 → 0.05(사용자 결정 2026-09-18): 우주 어려움 한 판에 600회 안팎을 쏴서 판 끝 피해가 첫 발의 ×49까지 갔다.
        // 0.05 → 0.04(사용자 결정 2026-10-01 — 진화 전 스킬 고정 규칙의 예외로 허용. 3회차: 상한 없는 누적이 호밍 과점의 공통 원인).
        // 누적 상한은 두지 않는다(같은 날 사용자 "상한 스택은 별로") — 진화 레벨업 피해 카드도 같이 낮췄다(Evo_Homing_*).
        const float growthPerCast = 0.04f;
        skill.GrowthStacks++;
        float missileDamage = damage * (1f + growthPerCast * skill.GrowthStacks);

        // 🔴 R0 2차 「초강력 슈퍼 로켓」 — 노션 "**거대한 로켓 하나**를 날려서 엄청난 폭발을 일으킨다" +
        //    2026-09-19 사용자 "체력이 가장 높은 적을 추적해서 맞히고, 주위 적들에게 큰 데미지".
        //    평소 다발 발사를 **통째로 대체**한다(한 발이라 발당 피해에 전체 몫을 몰아준다).
        if (skill.PathTier[1] >= 3)
        {
            FireSuperRocket(missileDamage, critChance, skill);
            SkillSfx.Play("SuperRocket.fire"); // 다발이 아니라 거대 로켓 한 발 — 평소 시전음과 질감이 다르다
            TriggerAttackBody();
            return true;
        }

        // 레벨업 주 성장축: 미사일 수(레벨업=+1씩, 진화=R1이 배수로 얹힌다 — HomingMissileCount).
        int count = HomingMissileCount(skill, skill.ExtraProjectiles);
        float missileScale = skill.PathTier[2] >= 3 ? 0.45f : skill.PathTier[2] >= 2 ? 0.7f : 1f;
        // 스킬트리 "더 많은 폭격"(Homing_MissileNum) 해금 시에만: 30회 사용마다 미사일 +1발
        // (10 → 20, 사용자 결정 2026-09-18 — 10회일 땐 우주 어려움 판 끝에 +60발로 2차 진화 기본 60발을 두 배로 만들었다)
        // (20 → 30, 사용자 결정 2026-10-06 — 노드가 너무 강하다. 문구 tree.desc.Homing_MissileNum과 짝)
        if (MetaBonuses.HomingMissileGrowth) count += skill.GrowthStacks / 30;
        // R0 「묵직한 탄두」(path1): 1차 폭발, 2차 폭발 강화
        bool explode = skill.PathTier[1] >= 2;
        // 🔴 폭발 피해는 미사일 피해의 50%다(2026-10-06 사용자 너프 — 9/28엔 80%, 그 전엔 차수별 0.4·0.6).
        //    2차 슈퍼 로켓은 위에서 빠져나가므로 여기 오는 폭발은 1차 「묵직한 탄두」뿐이다.
        //    레벨업 "폭발 피해"(SubDamage)가 그 위에 곱해진다(2026-09-29).
        float explodeRatio = HeavyWarheadExplodeRatio * skill.SubDamageMult;
        // 레벨업 "크기" 스텝이 폭발 범위를 키운다(2026-09-18 — 폭발 계열은 범위가 성장축이어야 한다). 미사일 그림은 안 키운다.
        // 🔴 1차 「묵직한 탄두」 폭발 반경 1.5 → 2.25(2026-09-27 사용자 ×1.5). 그림도 같이 키워야 한다 —
        //    `Homing_Missile` 프리팹의 `explodeVfxScale`(2 → 3). Effect_Explosion 프리팹의 localScale은
        //    HomingMissile이 통째로 대입해 덮으므로 아무 효과가 없다.
        // 🔴 2026-09-29 사용자: 크기 카드를 빼고 "폭발 범위"를 따로 뒀다 — 반경이 SubScale로 옮겨졌다.
        //    종전엔 skill.Scale을 읽었고 카드 문구도 "폭발 범위"였다(같은 것을 다른 이름으로 부르던 상태).
        float explodeRadius = (skill.PathTier[1] >= 3 ? 2.5f : 2.25f) * skill.SubScaleMult;

        // 발사각을 매번 조금씩 흔든다 — 같은 부채꼴로만 나가면 여러 발이 한 줄처럼 보인다.
        // 🔴 R1 「소형 미사일 다발」은 부채꼴을 **더 넓게** 편다(2026-09-19 사용자: "미사일이 너무 뭉쳐나온다").
        //    개수가 2배·5배로 늘어나는 루트라 ±60°에 몰아넣으면 발수가 늘어도 한 덩어리로만 보였다.
        bool wideSpread = skill.PathTier[2] >= 2;
        float spreadHalfAngle = wideSpread ? HomingWideSpreadHalfAngle : HomingSpreadHalfAngle;
        float spreadJitter = wideSpread ? HomingWideSpreadJitter : HomingSpreadJitter;

        StartCoroutine(HomingVolley(count, missileDamage, critChance, missileScale, explode,
                                    explodeRadius, explodeRatio, skill.SubScaleMult, spreadHalfAngle, spreadJitter,
                                    skill.ProjectileSpeedMultiplier, skill.BossDamageBonus));
        TriggerAttackBody();
        return true;
    }

    // 🔴 한 프레임에 다 쏘지 않고 **위에서 아래로 0.2초 간격**으로 쏜다(2026-09-27 사용자 "다다다다다").
    //    i=0이 위쪽이다 — Vector2.left를 +각도로 돌리면 아래로 내려가므로 인덱스 순서가 곧 위→아래다.
    // 🔴 발수가 변수라 상한이 필요하다. HomingMissileCount는 레벨업 +1에 R1 진화의 ×2·×5가 얹혀 후반에 수십 발이 된다 —
    //    발당 0.2초를 고정하면 60발이 12초가 되어 쿨(6초)을 넘겨 다음 시전과 겹친다. 총 발사 시간을 묶고 간격을 줄인다.
    private const float HomingVolleyGap = 0.2f;
    private const float HomingVolleyMaxSpan = 1.2f;   // 쿨 6초의 20%
    private IEnumerator HomingVolley(int count, float missileDamage, float critChance, float missileScale,
                                    bool explode, float explodeRadius, float explodeRatio, float vfxMult,
                                    float spreadHalfAngle, float spreadJitter,
                                    float speedMult = 1f, float bossBonus = 0f)
    {
        float gap = Mathf.Min(HomingVolleyGap, HomingVolleyMaxSpan / Mathf.Max(1, count));
        for (int i = 0; i < count; i++)
        {
            float spread = count > 1 ? Mathf.Lerp(-spreadHalfAngle, spreadHalfAngle, i / (float)(count - 1)) : 0f;
            spread += Random.Range(-spreadJitter, spreadJitter);
            Vector2 dir = Quaternion.Euler(0f, 0f, spread) * Vector2.left; // 전방(-x) 부채꼴 — 적이 오는 쪽
            GameObject obj = ObjectPool.Instance.Spawn(homingMissilePrefab, transform.position + Vector3.up * 0.2f, Quaternion.identity);
            obj.transform.localScale *= missileScale;
            HomingMissile m = obj.GetComponent<HomingMissile>();
            m.Damage = missileDamage;
            m.CritChance = critChance;
            m.Explode = explode;
            m.ExplodeRadius = explodeRadius;
            m.ExplodeVfxMult = vfxMult; // 판정이 커진 만큼 폭발 그림도 같이 키운다
            m.ExplodeRatio = explodeRatio;
            m.SpeedMultiplier = speedMult;   // 레벨업 "미사일 속도"
            m.BossDamageBonus = bossBonus;   // 레벨업 "보스 추가 피해"
            m.TargetRank = i; // 미사일마다 다른 적을 노리게 하는 순번(비행 우선 → 가까운 순으로 i번째)
            // 폭발 VFX는 `Homing_Missile` 프리팹의 `explodeVfxPrefab`이 들고 있고, 그 대상은 **`Effect_Explosion`**이다 —
            // 씬의 `eagleBombVfxPrefab`(폭탄 독수리)과 **같은 에셋을 공유**한다(2026-09-19 확인, guid d25df58e…).
            // 여기서 스나이핑 이펙트를 물리지 않는다.
            m.Init(dir);
            if (i < count - 1) yield return new WaitForSeconds(gap);
        }
    }

    // ── 산탄 장착: 전방으로 산탄을 뿌리고, 동시에 5초간 타수 버프를 건다 ──
    // 예전엔 버프만 걸어서 **시전해도 화면에 아무 일도 안 일어나는** 유일한 스킬이었다.
    // 실제로 산탄을 쏘게 해 이름값을 하게 하고, 알 개수를 레벨업 주 성장축으로 삼는다.
    // R0 2차 「내 지휘를 따라!」 — 산탄 발사를 통째로 포기하는 대신 타수 버프를 **배수**로 키운다.
    // 🔴 더하기가 아니라 곱하기다(사용자 결정 2026-09-08): Lv.1에 2배, 만렙에 3배.
    private const float ShotgunMegaMultAtLv1 = 2f;
    private const float ShotgunMegaMultAtMax = 3f;

    private bool FireShotgun(float damage, float critChance, EquippedSkill skill)
    {
        // 🔴 R0 2차 「내 지휘를 따라!」 = **공격하는 대신** 엄청난 타수 버프를 건다(2026-09-08 명세).
        //    그래서 이 루트만 산탄을 아예 안 쏜다 — 화면에 알이 안 나가는 게 의도다.
        bool megaBuff = skill.PathTier[1] >= 3;
        if (!megaBuff) FireShotgunPellets(damage, critChance, skill);
        // 🔴 공격 모션은 `FireVolley` 안에 있다 — 발사를 건너뛰면 **시전해도 화면에 아무 일도 안 일어난다.**
        //    이 스킬이 예전에 겪었던 바로 그 문제라(위 주석) 여기서 모션만 따로 튼다.
        else TriggerAttackBody();

        // 🔴 **버프는 R0(보너스 탄환 장착) 루트를 골랐을 때만 켜진다**(사용자 결정 2026-09-02 — 8/27 QA "산탄 구조 개편").
        //    화면의 "루트 1"(= route 0, 스나이핑 연계)이 그 루트다.
        //    미진화 산탄과 R1(메카 버스터) 루트는 **순수 공격기**다 — 예전엔 공격+버프가 한 스킬에 섞여 있어서
        //    진화로 버프를 고를 이유가 없었다.
        if (skill.PathTier[1] <= 0) return true;

        // 루트를 탔으므로 +2초는 늘 붙는다(R1 루트는 위에서 이미 빠져나갔다).
        // 기본 7초(5+2) — 에셋 `Prog_Shotgun.baseDuration`이 있으면 그쪽이 이긴다.
        float duration = BaseDuration(skill.Id, 5f + 2f) + skill.ExtraShotgunDuration; // 레벨업 "지속시간" 스텝
        // 🔴 1차 = **모든 공격에 +1**(2026-09-08 명세). 스킬을 고르지도, 배수를 곱하지도 않는다.
        // 레벨업 "버프 타수"(BonusHits)도 여기 더해진다(2026-09-29 사용자).
        // (스킬트리 "추가 장전"이 여기 +1을 얹던 효과는 2026-09-30 폐지 — 지금은 산탄 발사 피해 +10%다. FireShotgunPellets)
        int bonus = 1 + skill.ExtraBonusHits;
        if (megaBuff)
        {
            float t = Mathf.InverseLerp(1f, BalanceConstants.MaxSkillLevel, skill.Level);
            bonus = Mathf.RoundToInt(bonus * Mathf.Lerp(ShotgunMegaMultAtLv1, ShotgunMegaMultAtMax, t));
        }

        shotgunTimer = duration;
        shotgunBonus = bonus;

        // R1(휘두르기 연계, path2)은 "화면 전체 5회 공격 + 기절"에서 **전방 집중 산탄**으로 바뀌었다(2026-08-06 명세).
        // 실제 변화는 전부 FireShotgunPellets(탄 수·발사 각도·피해)에 있다 — 여기서 따로 할 일이 없다.

        // HUD 버프 줄에 아이콘 + **남은 시간**만 뜬다. 타수 숫자는 안 붙인다(사용자 결정 2026-09-08) —
        // 스킬 칸에 두르던 노란 테를 걷어낸 자리를 숫자로 메우려던 것인데, 지속시간만으로 충분하다.
        BuffTracker.Set("Shotgun", Time.time + duration);
        return true;
    }

    // ── 휘두르기(파인애플 전용): 앞의 적을 돌망치로 후려쳐 뒤로 밀어낸다 ──
    // 딸기의 화살 쏘기 자리를 대신하는 주력기. 사거리가 짧은 대신 쿨이 짧고, 맞은 적을 왼쪽으로
    // 밀어내 방어선을 되돌린다(디펜스에서 시간을 버는 것이 이 스킬의 정체성).
    // 투사체가 아니라 즉발 판정이라 비행 적도 범위 안이면 같이 맞는다.
    // 🔴 2026-09-18 범위 상향(4.8→6.2 · 2.0→2.6): 파인애플이 9개 목표 중 8개에서 최약체였고,
    //    원인은 시작 스킬인 이 스킬의 **오버킬 78%**(피해의 78%가 이미 죽은 적에게 들어감)였다.
    //    오버킬은 *대상 하나당* 낭비라 피해를 올려도 낭비만 커진다 — 대신 **닿는 적 수**를 늘리면
    //    한 대상당 낭비는 그대로 두고 유효타 총량만 오른다. 그래서 피해가 아니라 범위를 키웠다.
    private const float SwingReach = 5.6f;       // 플레이어 앞(왼쪽) 사거리 = 판정의 **끝점**. 2026-09-20 사용자 "조금만 줄여" 6.2→5.6
    private const float SwingHalfHeight = 2.3f;  // 위아래 판정 반높이 — 비행 적까지 닿게 넉넉히. 2026-09-20 2.6→2.3
    private const float SwingKnockback = 2.4f;   // 밀어내는 거리
    // 판정의 **시작점** — 파인애플 몸통 바로 앞. 몸통을 덮던 예전 판정(등 뒤 0.5까지)을 앞으로 밀어낸 것.
    // ⚠️ 일부러 skill.Scale을 곱하지 않는다. 시작점은 "몸통 위치"라 크기 진화와 무관하게 고정돼야 한다
    //    — 곱하면 크기를 올릴수록 코앞이 비어서 근접 캐릭터가 눈앞의 적을 못 때린다.
    //    덕분에 크기 증가는 **앞쪽으로만** 자라고, 초반 화력은 유지되면서 면적 증가폭은 완만해진다.
    // (실측: 플레이어 pivot→앞 몸통 끝 0.75. 파인애플은 스프라이트 폭에 돌망치가 포함돼 더 넓지만
    //  "몸통"만 치면 딸기와 비슷하다 — 눈으로 보고 조정할 손잡이다.)
    private const float SwingNearOffset = 0.7f;
    // 1루트(힘 연계) 1차 진화가 판정에 곱하는 배율. **가로·세로에 같이 곱해져 면적은 이 값의 제곱**으로 커진다.
    // 2026-09-20 사용자: 1.45에서 1.2로(면적 2.10배 → 1.44배). SwingRoutine과 SwingRangeMult가 같이 쓴다 —
    // 예전엔 1.45가 두 곳에 흩어져 있어 한쪽만 고치면 그림자와 실제 판정이 어긋났다.
    private const float SwingRoute1ReachMult = 1.2f;
    // 판정 사각형의 **중심 높이**를 플레이어 y에서 위로 얼마나 올릴지. 0이면 위아래가 대칭이라
    // 아래쪽(땅 밑)이 남고 위쪽(비행 적)이 모자란다 — 돌망치를 머리 위로 휘두르는 그림과도 안 맞는다.
    // ⚠️ skill.Scale·reachMult를 곱하지 않는다. 몸통 기준 높이라 크기 진화와 무관하게 고정돼야 한다
    //    (곱하면 크기를 키울수록 판정이 통째로 떠올라 지상 적을 놓친다).
    private const float SwingCenterYOffset = 0.6f;

    // 피해는 캐스트 순간이 아니라 **돌망치가 땅에 꽂히는 4번째 프레임**에 들어간다
    // (그 프레임에 사용자가 충격파를 직접 그려 넣었다 — 그림과 판정이 같은 순간이어야 한다).
    // Pinapple_Attack = 5프레임: 1~4는 각 75ms, 마지막 5번째만 225ms(총 0.525초). 4번째 시작 = 0.225초.
    // ⚠️ 클립 타이밍을 바꾸면 이 값도 같이 고칠 것 — 어긋나면 휘두르기도 전에 적이 날아간다.
    private const float SwingImpactDelay = 0.225f;
    // 2루트 진화 충격파 — 본체보다 약하게 때리고 "살짝" 밀어낸다(기본 넉백 0.8의 절반 이하).
    private const float ShockwaveSpawnOffset = 1.6f;            // 내려찍은 지점에서 출발(앞쪽)
    // 땅을 타고 번지는 연출이라 플레이어 중심이 아니라 **발밑 높이**에서 나가야 한다.
    // (0이면 몸통 한가운데서 나가 공중에 뜬 것처럼 보인다.)
    // (실측: 플레이어 pivot→발밑 1.125, 충격파 스프라이트 반높이 약 0.3 → 발밑에 딱 얹으면 -0.85)
    // 다만 딱 얹으니 너무 낮아 보여 0.3 올렸다(9/18 사용자). 0은 몸통 한가운데라 위 주석대로 뜬 것처럼 보인다.
    // ⚠️ 이 값은 연출만이 아니다 — 충격파는 Collider2D 트리거로 때리므로(SwingShockwave.OnTriggerEnter2D)
    //    올릴수록 낮게 깔린 적을 덜 맞춘다. 더 올릴 땐 피해 대상이 같이 바뀌는 걸 감안할 것.
    private const float ShockwaveSpawnYOffset = -0.55f;
    private const float ShockwaveDamageRatio = 0.6f;            // 진화 1차: 본체 피해의 60%
    private const float ShockwaveEmpoweredDamageRatio = 1f;     // 진화 2차: 본체와 같은 피해
    private const float ShockwaveKnockback = 0.35f;
    private const float ShockwaveEmpoweredKnockback = 0.55f;
    // 🔴 R0 2차 「로열 팔라딘의 망치」 = 노션 "공격당한 적들이 **긴 시간** 동안 기절한다".
    //    0.5초는 포도 찌릿찌릿의 "**짧게** 기절"(GrapeStunDuration)과 **같은 값**이라 둘이 구분이 안 됐다.
    //    4배로 벌려 문구대로 "긴 시간"이 되게 한다. 보스·비행선은 CrowdControlScale이 따로 깎는다.
    //    2 → 1.4(2026-10-07 사용자 "스턴시간 30% 줄여줘"). 레벨업 "기절 시간" 카드(+0.2초 × 2)는 이 위에 더해진다.
    private const float SwingStunDuration = 1.4f;
    // 1루트 진화 1차: 맞은 적 **한 마리당** 초과체력 회복(독수리 "흡혈 군단"과 같은 방식).
    // 광역이라 여럿 맞히면 그만큼 크게 회복된다 — 근접으로 파고드는 위험의 보상.
    private const int SwingLifestealPerHit = 2;

    // ── 돌조각 파티클 ──
    // 노란 범위 사각형을 걷어낸 자리를 대신한다 — "어디까지 맞았는지"를 표시가 아니라 **맞은 적한테서** 보여준다.
    // 진화 루트와 무관하게 휘두르기에 맞으면 항상 튄다.
    private const int RockDebrisPerHit = 4;
    private const float RockDebrisSpeed = 4.5f;

    // ── 포도: 독성 포도알 ──
    // 적이 모인 자리에 포도알을 던져 독안개를 깔고, 안개에 닿은 적을 중독시킨다.
    // 진화 효과는 전부 여기서 PathTier를 실시간으로 읽는다 — 다만 **중독 틱 안에서 도는 것**
    // (폭발·기절)만은 Enemy가 읽어야 해서 static으로 넘긴다.
    private void FireGrapeToss(float damage, EquippedSkill skill)
    {
        TriggerAttackBody();
        SkillSfx.Play("GrapeToss.cast");

        int balls = GrapeBaseBalls + skill.ExtraProjectiles;
        if (skill.PathTier[2] >= 1) balls += 2;              // 찌릿찌릿 1차: 던지는 알이 늘어난다

        float radius = GrapeCloudRadius * skill.Scale;
        if (skill.PathTier[1] >= 1) radius *= 2.0f;          // 생화학 1차: 안개가 커진다(2026-09-30 사용자 "2배 정도" — 1.4→2.0, 넓이로 지금의 약 2배)

        // 도트 간격. 레벨업 "독 피해 주기"(SkillStat.TickRate)가 여기 곱해진다 — 회오리·독수리와 같은 축이다.
        // 🔴 예전엔 상수만 읽어서, Prog_GrapeToss에 TickRate 스텝을 넣어도 조용히 무시됐다(2026-09-27 배선).
        float interval = GrapePoisonInterval * skill.TickIntervalMult;
        if (skill.PathTier[1] >= 2) interval *= 0.5f;        // 생화학 1차(옛 T2): 도트 간격 절반
        interval = Mathf.Max(0.1f, interval);                // 너무 잦으면 틱마다 도는 판정이 통째로 비싸진다

        // 중독 틱이 읽을 값 — 진화 직후 첫 시전에 반영되고, 그 전에는 중독 자체가 없다.
        GrapePoisonExplodeOnDeath = skill.PathTier[1] >= 3;  // 생화학 2차
        // 🔴 전염 반경을 안개 크기에서 **뗐다**(2026-09-29 사용자). 전염은 중독된 적이 죽을 때 터지는 것이고
        //    안개와 메커니즘이 무관한데, 종전엔 둘 다 skill.Scale을 읽어 카드 하나가 둘을 같이 움직였다.
        GrapeExplodeRadiusMult = skill.SubScaleMult;         // 레벨업 "전염 범위"
        // 레벨업 "기절 주기"(StunInterval) — 음수라 몇 틱마다 기절하는지가 줄어든다.
        int stunEvery = skill.PathTier[2] >= 3 ? 2 : (skill.PathTier[2] >= 2 ? 3 : 0);
        if (stunEvery > 0) stunEvery = Mathf.Max(1, stunEvery + skill.StunIntervalBonus);
        GrapeStunEveryNPoisonTicks = stunEvery;
        GrapeStunAppliesVulnerable = skill.PathTier[2] >= 3; // 찌릿찌릿 2차

        // 🔴 적이 없으면 **그냥 랜덤한 자리에 던진다**(사용자 결정 2026-09-27).
        //    2026-09-10에는 반대로 정했었다 — 전방에 알을 띄워 두고 적이 나올 때까지 기다리는 방식.
        //    그때 문제였던 "안개가 빈 자리에 깔리고 쿨만 날아간다"는 이제 덜하다: 안개가 넓어지고
        //    오래 남으며 알끼리 벌어져 떨어지므로, 빈 자리에 깔려도 곧 걸어오는 적이 밟는다.
        Color cloudColor = GrapeCloudColorFor(skill);
        float crit = GetCritChance(skill);
        foreach (Vector3 spot in PickGrapeSpots(balls, radius))
        {
            float cloudBonus = skill.ExtraWhirlwindDuration;   // 레벨업 "안개 지속시간"
            GameObject ball = SpawnGrapeBall(transform.position + Vector3.up * 0.4f, skill);
            if (ball == null) { LandGrape(spot, radius, damage, interval, cloudColor, crit, cloudBonus); continue; }

            GrapeProjectile gp = ball.GetComponent<GrapeProjectile>();
            if (gp == null) gp = ball.AddComponent<GrapeProjectile>();
            gp.Init(spot, GrapeFlightTime, GrapeArcHeight,
                    landed => LandGrape(landed, radius, damage, interval, cloudColor, crit, cloudBonus));
        }
    }

    // 알 자체가 때리는 몫 — **안개 몇 틱분**인지로 정한다(사용자 지시 2026-09-27: "안개 2틱정도").
    private const int GrapeImpactTicks = 2;

    // 착탄 — 그 자리에 있던 적을 한 번 때리고 안개를 남긴다.
    // 🔴 터지는 이펙트는 뺐다(사용자 지시 2026-09-27) — 호밍의 `Effect_Explosion`을 돌려 쓰고 있어서
    //    포도를 쓰는데 호밍 폭발이 보였다. 소리(GrapeToss.pop)는 남긴다.
    // extraCloudDuration = 레벨업 "안개 지속시간"(Duration). 안개가 바닥에 남는 시간만 늘린다.
    private void LandGrape(Vector3 at, float radius, float damage, float interval, Color cloudColor, float critChance, float extraCloudDuration = 0f)
    {
        SkillSfx.Play("GrapeToss.pop");

        // 🔴 알 자체 피해는 **목적지에 도착한 순간 그 자리에 있는 적**에게만 들어간다(사용자 결정 2026-09-27).
        //    비행 중 충돌 판정이 아니다 — 알이 포물선으로 떠서 날아가 "가는 길에 스치는" 판정이 어색했다.
        //    가는 길에 아무도 못 만났으면 그냥 안개만 남는다.
        float impact = damage * GrapeImpactTicks;
        if (impact > 0f)
            using (Enemy.GetSnapshot(out List<Enemy> enemies))
                foreach (Enemy e in enemies)
                {
                    if (e == null || !e.IsAlive) continue;
                    if (Vector2.Distance(e.transform.position, at) > radius) continue;
                    e.TakeSkillHit(impact, critChance, ActiveSkillId.GrapeToss);
                }

        GameObject cloud = new GameObject("PoisonCloud");
        cloud.transform.position = at;
        // 스킬트리 「짙은 독안개」가 바닥에 남는 시간을 늘린다(안개 밖으로 나간 뒤 아픈 시간은 그대로).
        cloud.AddComponent<PoisonCloud>()
             .Init(radius, GrapeCloudDuration + MetaBonuses.GrapeCloudExtraSeconds + extraCloudDuration,
                   damage, GrapePoisonDuration, interval, cloudColor);
    }

    // 포도알 그림 = 기본 오브. 전용 프리팹이 배선돼 있으면 그쪽을 먼저 쓴다.
    // ── 포도알 그림 (2026-09-19 사용자 명세) ────────────────────────────────
    // 🔴 **1차 진화에서 그림이 바뀌고, 2차에서는 안 바뀐다.**
    //    미진화 = `Effect_GrapeBomb` · R0 1차 「생화학 포도알」 = `Sprite_GrapeBombGreen`
    //    · R1 1차 「찌릿찌릿 포도알」 = `Effect_GrapeBombElectric`
    // 각 세트는 한 애니메이션의 1·2프레임이다(화살 `Effect_ArrowR1/R2`와 같은 명명 방식).
    // 미배선이면 예전처럼 오브 그림을 복사해 보라색을 입힌다(폴백).
    [SerializeField] private Sprite[] grapeBallFrames;          // 미진화
    [SerializeField] private Sprite[] grapeBallBiohazardFrames; // R0(생화학)
    [SerializeField] private Sprite[] grapeBallElectricFrames;  // R1(찌릿찌릿)
    [SerializeField] private float grapeBallFps = 8f;

    private Sprite[] GrapeBallFramesFor(EquippedSkill skill)
    {
        if (skill != null && skill.PathTier[1] >= 1 && grapeBallBiohazardFrames != null && grapeBallBiohazardFrames.Length > 0)
            return grapeBallBiohazardFrames;
        if (skill != null && skill.PathTier[2] >= 1 && grapeBallElectricFrames != null && grapeBallElectricFrames.Length > 0)
            return grapeBallElectricFrames;
        return grapeBallFrames != null && grapeBallFrames.Length > 0 ? grapeBallFrames : null;
    }

    private GameObject SpawnGrapeBall(Vector3 from, EquippedSkill skill)
    {
        Sprite[] frames = GrapeBallFramesFor(skill);

        if (grapeBallPrefab != null)
        {
            GameObject obj = Instantiate(grapeBallPrefab, from, Quaternion.identity);
            ApplyGrapeBallFrames(obj, frames);
            return obj;
        }

        GameObject ball = new GameObject("GrapeBall", typeof(SpriteRenderer));
        ball.transform.position = from;
        ball.transform.localScale = Vector3.one * 0.6f;
        SpriteRenderer sr = ball.GetComponent<SpriteRenderer>();
        sr.sortingOrder = 120; // 던지는 알은 적보다 앞 — 궤적이 가려지면 어디 떨어질지 안 보인다

        if (frames != null)
        {
            sr.sprite = frames[0];
            if (frames.Length >= 2) ball.AddComponent<SpriteFlipbook>().Play(frames, grapeBallFps, true);
            return ball;
        }

        // 폴백: 오브 프리팹을 그대로 쓰면 Orb 로직(관통·슬로우)이 같이 붙는다 — **그림만** 떼어 온다.
        if (orbPrefab == null) { Destroy(ball); return null; }
        SpriteRenderer src = orbPrefab.GetComponentInChildren<SpriteRenderer>();
        if (src == null || src.sprite == null) { Destroy(ball); return null; }
        sr.sprite = src.sprite;
        sr.color = GrapeCloudColor;
        return ball;
    }

    private void ApplyGrapeBallFrames(GameObject ball, Sprite[] frames)
    {
        if (frames == null) return;
        SpriteRenderer sr = ball.GetComponentInChildren<SpriteRenderer>();
        if (sr == null) return;
        sr.sprite = frames[0];
        if (frames.Length < 2) return;
        SpriteFlipbook fb = sr.GetComponent<SpriteFlipbook>();
        if (fb == null) fb = sr.gameObject.AddComponent<SpriteFlipbook>();
        fb.Play(frames, grapeBallFps, true);
    }

    // 적이 모인 자리를 고른다 — **무리의 앞줄**(플레이어에 가까운 쪽)부터. 안개끼리 겹치면 넓이가 낭비되므로
    // 이미 고른 지점과는 떨어뜨린다.
    // ⚠️ 적이 없을 때의 폴백(아래 전방 허공)은 **평소엔 안 쓰인다** — FireGrapeToss가 그 경우를 대기로 가로챈다.
    //    대기 코루틴이 적을 확인한 뒤 부르므로 여기 오면 적이 있다. 다른 경로가 생길 때를 위해 남겨 둔 안전망이다.
    private List<Vector3> PickGrapeSpots(int count, float radius)
    {
        List<Vector3> spots = new List<Vector3>();
        List<Enemy> alive = new List<Enemy>();
        foreach (Enemy e in Enemy.Active)
            if (e != null && e.IsAlive) alive.Add(e);

        // 플레이어에 가까운 순 = 무리의 앞줄부터. 뒤에 던지면 앞줄은 이미 지나가 버려 아무도 안 맞는다.
        float playerX = transform.position.x;
        alive.Sort((a, b) => Mathf.Abs(a.transform.position.x - playerX)
                                 .CompareTo(Mathf.Abs(b.transform.position.x - playerX)));

        // 🔴 알 간격은 **안개 크기에 비례**한다(사용자 지시 2026-09-30: 안개가 커지면 간격도 커져야 한다 — 원이 점점 겹쳐 보인다).
        //    기본 반경에서는 포도 캐릭터 키(GrapeSpotMinGap)다. 2026-09-27에 비례를 걷어냈던 이유
        //    ("안개가 커질수록 알이 화면 밖까지 흩어진다")는 아래 ClampGrapeSpot이 화면 안으로 잘라서 막는다.
        float minGap = GrapeSpotMinGap * (radius / GrapeCloudRadius);
        Rect area = GrapeArea(radius, playerX);

        for (int i = 0; i < count; i++)
        {
            // 적이 하나도 없으면 전방 랜덤 — 그래도 서로는 벌린다(사용자 결정 2026-09-27: 대기하지 말고 던져라).
            if (alive.Count == 0)
            {
                spots.Add(SpreadGrapeSpot(
                    transform.position + new Vector3(Random.Range(-7f, -2f), Random.Range(-1.5f, 1.5f), 0f),
                    spots, minGap, area));
                continue;
            }

            // 🔴 **이미 고른 자리에서 가장 멀리 떨어진 후보**를 고른다(예전엔 "떨어진 첫 적"이라
            //    앞줄 몇 마리에 몰렸다). 여유가 있는 후보들 중에서는 앞줄(플레이어에 가까운 쪽)이 이긴다 —
            //    alive가 이미 그 순서로 정렬돼 있어 동점이면 먼저 나온 쪽이 남는다.
            Vector3 best = Vector3.zero;
            float bestClearance = float.NegativeInfinity;
            foreach (Enemy e in alive)
            {
                Vector3 cand = ClampGrapeSpot(LeadGrapeTarget(e, playerX), area);
                float clearance = float.PositiveInfinity;
                foreach (Vector3 s in spots)
                    clearance = Mathf.Min(clearance, Vector2.Distance(s, cand));
                if (clearance > bestClearance) { bestClearance = clearance; best = cand; }
            }

            // 다 붙어 있어 어디를 골라도 겹치면, 적이 없는 자리로 밀어낸다.
            // "적이 없는 곳이어도 괜찮으니 최대한 적과 가까우면서" — 가장 가까운 후보에서 최소한만 비켜선다.
            spots.Add(SpreadGrapeSpot(best, spots, minGap, area));
        }
        return spots;
    }

    private EnemySpawner laneSpawner; // 레인 기준선(스포너 y)을 읽으려고 둔다. 맵 배율이 스포너를 옮기므로 y는 던질 때마다 읽는다

    // 포도알이 터질 수 있는 칸 = **적이 있을 수 있는 칸**(사용자 지시 2026-10-01: 플레이어 오른쪽·레인 아래에서 터지지 않게).
    //   가로: 화면 왼쪽(여백 = 안개 반경의 절반) ~ 적 정지선(Enemy.ClampInsideArena와 같은 선 — 그 오른쪽엔 적이 못 간다)
    //   세로: 레인 기준선(지상 적이 서는 가장 낮은 높이) ~ 화면 위(여백). 날아다니는 적은 전부 기준선보다 위에 있다.
    // 간격이 안개 크기를 따라 커지면서 밀려난 알이 화면 밖에 떨어지는 것도 이걸로 막는다.
    // ⚠️ 막는 건 터지는 **자리**다 — 안개 원은 거기서 반경만큼 퍼지므로 아래쪽 절반은 여전히 레인 밑에 깔린다.
    private Rect GrapeArea(float radius, float playerX)
    {
        Camera cam = Camera.main;
        if (cam == null) return Rect.MinMaxRect(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);
        if (laneSpawner == null) laneSpawner = FindAnyObjectByType<EnemySpawner>();

        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        float margin = radius * 0.5f;
        Vector3 c = cam.transform.position;
        float xMin = c.x - halfW + margin;
        float xMax = Mathf.Max(xMin, Mathf.Min(c.x + halfW - margin, playerX - BalanceConstants.ContactStopDistance));
        float yMin = c.y - halfH + margin;
        if (laneSpawner != null) yMin = Mathf.Max(yMin, laneSpawner.transform.position.y - BalanceConstants.EnemySpawnYJitter);
        float yMax = Mathf.Max(yMin, c.y + halfH - margin);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static Vector3 ClampGrapeSpot(Vector3 p, Rect area)
    {
        p.x = Mathf.Clamp(p.x, area.xMin, area.xMax);
        p.y = Mathf.Clamp(p.y, area.yMin, area.yMax);
        return p;
    }

    private static bool GrapeSpotClear(Vector3 p, List<Vector3> taken, float minGap)
    {
        foreach (Vector3 t in taken)
            if (Vector2.Distance(t, p) < minGap) return false;
        return true;
    }

    // 이미 고른 자리들과 minGap만큼 떨어진 자리를 **칸 안에서** 찾는다. 옮기는 거리를 최소로 두어
    // 원래 노리던 지점(적 무리) 근처에 남는다.
    // 🔴 칸으로 자르는 건 **벌린 뒤가 아니라 벌리면서** 한다 — 벌린 뒤에 자르면 밀려난 알들이 정지선·레인 끝에
    //    도로 몰려 서로 겹친다(2026-10-01 실측: 자른 뒤 겹침이 매 투척에서 났다).
    private static Vector3 SpreadGrapeSpot(Vector3 want, List<Vector3> taken, float minGap, Rect area)
    {
        want = ClampGrapeSpot(want, area);
        if (GrapeSpotClear(want, taken, minGap)) return want;

        // 이미 고른 자리마다 그 둘레(minGap 원)를 돌며, 칸 안이면서 아무와도 안 겹치는 점 중 원래 자리에 가장 가까운 점.
        const int Steps = 24;
        bool found = false;
        Vector3 best = want;
        float bestDist = float.PositiveInfinity;
        foreach (Vector3 t in taken)
            for (int k = 0; k < Steps; k++)
            {
                float a = k * (Mathf.PI * 2f / Steps);
                Vector3 cand = t + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (minGap * 1.001f);
                if (!area.Contains(cand) || !GrapeSpotClear(cand, taken, minGap)) continue;
                float d = Vector2.Distance(cand, want);
                if (d < bestDist) { bestDist = d; best = cand; found = true; }
            }
        if (found) return best;

        // 칸이 꽉 차 minGap을 지킬 자리가 없으면(안개가 아주 클 때 — 칸 높이가 레인~화면 위 약 5유닛뿐이다)
        // 칸 안을 격자로 훑어 **이미 고른 자리들과 가장 멀리 떨어진 점**을 고른다. 겹침은 감수하되 한 점에 쌓이지 않게.
        // (밀어낸 뒤 칸으로 자르던 종전 방식은 칸 모서리에 알이 겹쳐 쌓였다 — 2026-10-01 실측 최소 간격 0.00)
        if (float.IsInfinity(area.width) || float.IsInfinity(area.height)) return want; // 카메라가 없어 칸이 무한 — 격자를 못 깐다
        const int GridX = 24, GridY = 12;
        float bestClear = float.NegativeInfinity;
        for (int ix = 0; ix <= GridX; ix++)
            for (int iy = 0; iy <= GridY; iy++)
            {
                Vector3 cand = new Vector3(Mathf.Lerp(area.xMin, area.xMax, ix / (float)GridX),
                                           Mathf.Lerp(area.yMin, area.yMax, iy / (float)GridY), want.z);
                float clear = float.PositiveInfinity;
                foreach (Vector3 t in taken) clear = Mathf.Min(clear, Vector2.Distance(t, cand));
                // 거의 같은 여유면 원래 노리던 자리에 가까운 쪽(적 무리 쪽)
                if (clear > bestClear + 0.01f || (clear > bestClear - 0.01f && Vector2.Distance(cand, want) < Vector2.Distance(best, want)))
                { bestClear = clear; best = cand; }
            }
        return best;
    }

    // 착탄 시점의 위치를 미리 짚는다 — 포도알은 GrapeFlightTime만큼 날아가는데 그동안 적이 걸어 나가
    // 지금 위치에 던지면 안개가 빈 자리에 깔린다. 적은 x축으로만 걸어오므로 x만 민다.
    private Vector3 LeadGrapeTarget(Enemy e, float playerX)
    {
        Vector3 p = e.transform.position;
        float dir = Mathf.Sign(playerX - p.x);   // 적은 늘 플레이어 쪽으로 온다
        // 🔴 비행 시간만 앞을 짚으면 **착지 순간**에만 맞는다 — 안개는 그 뒤로도 깔려 있는데
        //    적은 계속 걸어 나가므로 빠른 적(서핑 이속 6 · 라이더 4.5)은 안개를 통과해 버린다.
        //    안개 체류분(GrapeLeadDwell)만큼 더 밀어 **적이 안개로 걸어 들어오게** 한다(2026-09-20 사용자).
        p.x += dir * e.CurrentMoveSpeed * (GrapeFlightTime + GrapeLeadDwell);
        if ((playerX - p.x) * dir < 0f) p.x = playerX; // 플레이어를 지나쳐 뒤로는 안 던진다
        return p;
    }

    private void FireSwing(float damage, float critChance, EquippedSkill skill)
    {
        animator.SetTrigger("Attack");
        swingShadowUntil = Time.time + SwingImpactDelay + SwingShadowLinger; // 이 창 동안만 범위 그림자를 깐다
        StartCoroutine(SwingRoutine(damage, critChance, skill));
    }

    private IEnumerator SwingRoutine(float damage, float critChance, EquippedSkill skill)
    {
        // Route1(힘 연계, path1): 진화 1차 = 타격 범위 확대 / 2차 = 밀쳐진 적 기절.
        float reachMult = skill.PathTier[1] >= 2 ? SwingRoute1ReachMult : 1f;
        bool stun = skill.PathTier[1] >= 3;
        // R1 「지진파」(낙뢰 연계, path2): 진화 1차 = 맵 끝까지 가는 충격파 / 2차 「거대한 파도」 = 그 충격파가 강해진다.
        bool shockwave = skill.PathTier[2] >= 2;
        bool empoweredShock = skill.PathTier[2] >= 3;

        float reach = SwingReach * reachMult * skill.Scale;
        float halfHeight = SwingHalfHeight * reachMult * skill.Scale;

        yield return new WaitForSeconds(SwingImpactDelay); // 내려찍는 순간까지 기다린다

        // 타격 이펙트는 별도 VFX가 아니라 **4번째 프레임 그림에 그려져 있다**(사용자 아트).
        // 파티클을 겹쳐 봤지만 도트 그림을 가려서 뺐다 — 연출을 더하려면 그림 쪽을 먼저 볼 것.
        // 1루트 1차부터 흡혈이 붙는다 — 범위·피해만 늘던 루트에 "버티는" 성격을 준다.
        // 레벨업 "보호막 획득률"(Lifesteal) — 만피면 흡수분이 오버힐 보호막으로 넘어간다(2026-09-29 사용자).
        int lifesteal = skill.PathTier[1] >= 2 ? SwingLifestealPerHit + skill.ExtraLifesteal : 0;
        // 🔴 타격음은 **여기**다 — 시전 0.225초 뒤. 시전 순간에 때리는 소리를 넣으면 그림보다 빠르다.
        SkillSfx.Play("Swing.impact");
        ScreenShake.Shake(ScreenShake.SwingStrength, ScreenShake.SwingDuration); // 내려찍는 그 순간에 맞춰 흔든다
        // 배율 = 스킬트리 "휘두르기 넉백" / 기절 시간은 레벨업 "기절 시간"(StunDuration)이 늘린다.
        SwingHit(damage, critChance, reach, halfHeight, SwingKnockback * MetaBonuses.SwingKnockbackMult, stun, lifesteal,
                 SwingStunDuration + skill.ExtraStunDuration);

        if (shockwave) SpawnShockwave(damage, critChance, empoweredShock, skill.SubDamageMult, skill.SubScaleMult);
    }

    // ── 휘두르기 범위 보여주기 (사용자 요청 2026-09-18) ──────────────────────────────
    // ① 망치 그림이 **실제 판정 범위만큼** 커진다. ② 그 범위가 발밑에 둥근 그림자로 깔린다.
    // 판정은 SwingHit의 사각형(왼쪽으로 reach · 위아래 halfHeight)이고, 그림자는 그 **가로 폭**을 그린다.
    // ⚠️ 두 연출 다 여기 한 배율(SwingRangeMult)에서 나온다 — 범위 수식이 바뀌면 이것부터 맞출 것.
    // 가로 폭 대비 세로 — 바닥에 누운 타원으로 보이는 비율.
    // 2026-09-20 사용자: "가로로 길어야 하는데 아래로 너무 내려가 있다. 아주 길쭉한 원형으로" → 0.32에서 0.13으로.
    private const float SwingShadowFlatten = 0.13f;
    private const float SwingShadowAlpha = 0.22f;     // 적 그림자(0.35)보다 옅게 — 범위 표시지 물체가 아니다
    private static Sprite swingShadowSprite;
    private Transform hammerTr;
    private Vector3 hammerBaseScale;
    private Vector3 hammerBaseLocalPos;
    // 🔴 팔라딘 망치만 자리를 옮긴다(2026-09-20 사용자: "더 앞으로 당기고, 위로 올려야 할듯.
    //    팔라딘이 파인애플 약간 앞에 서야 해. 망치로 내려찍는 순간 그 내려찍은 부분이 지표면이어야").
    //    x 음수 = 적이 오는 쪽(왼쪽) = 앞. 그림이 1차보다 2배 커서 기준점이 그만큼 밀려 있다.
    //    y는 **캐릭터 발밑 투명 여백(0.70)** 만큼 더 올린다 — 안 그러면 내려찍은 머리가 지면 아래로 들어간다(실측 -0.26).
    private static readonly Vector3 PaladinHammerOffset = new Vector3(-1.0f, 1.25f, 0f);
    private Transform swingShadowTr;
    private SpriteRenderer swingShadowSr;
    private SpriteRenderer bodySrCache;
    // 🔴 범위 그림자는 **휘두르는 동안만** 깔린다(2026-09-20 사용자: "휘두를 때 원형으로 그림자 지라는 거지
    //    늘 나오라는 뜻이 아니다"). 종전엔 LateUpdate가 매 프레임 깔아서 들고만 있어도 항상 보였다.
    //    창 = 시전 시작 ~ 내려찍기(SwingImpactDelay) + 여운. 예비동작 동안 범위가 보여야 예고 역할을 한다.
    private const float SwingShadowLinger = 0.15f;
    private float swingShadowUntil = -1f;

    // 🔴 **휘두르기만 망치가 같이 움직인다**(2026-09-20 사용자: "휘두르기 쓸 때는 망치까지 움직이는 게 맞는데
    //    그 외 공격기엔 파인애플 애니메이션만"). 애니메이터 트리거가 둘로 갈린다:
    //      `Attack`     = Pinapple_Attack.anim      — 본체 + Hammer 곡선. **FireSwing만** 쏜다.
    //      `AttackBody` = Pinapple_AttackBody.anim  — 본체 곡선만. 나머지 스킬 전부가 쏜다.
    //    본체 곡선은 망치 변종 네 클립이 전부 같아서 본체 전용 클립 하나로 커버된다(진화해도 그대로).
    // 🔴 `AttackBody` 상태는 **Write Defaults를 꺼 둔다**(Pinapple.controller, 2026-10-06 사용자: "다른 스킬 시전할 때
    //    망치가 사라진다"). 그 클립엔 Hammer 곡선이 없어서, 켜 두면 망치 스프라이트가 씬 기본값(비어 있음)으로 덮여
    //    0.5초 동안 사라진다. 꺼 두면 직전 Idle의 망치 그림(진화 변종 포함)이 그대로 남는다.
    // 🔴 `AttackBody`는 **파인애플 컨트롤러에만 있다** — 딸기(Player.controller)·포도(Grape.controller)는
    //    `Attack` 하나뿐이라 없는 트리거를 쏘면 경고만 찍히고 공격 모션이 안 나온다. 없으면 `Attack`으로 대신 쏜다.
    private RuntimeAnimatorController attackBodyCheckedFor;
    private bool hasAttackBody;
    private void TriggerAttackBody()
    {
        RuntimeAnimatorController ctrl = animator.runtimeAnimatorController;
        if (ctrl != attackBodyCheckedFor) // 진화하면 컨트롤러가 바뀐다(망치 오버라이드) — 바뀔 때만 다시 센다
        {
            attackBodyCheckedFor = ctrl;
            hasAttackBody = false;
            foreach (AnimatorControllerParameter p in animator.parameters)
                if (p.name == "AttackBody") { hasAttackBody = true; break; }
        }
        animator.SetTrigger(hasAttackBody ? "AttackBody" : "Attack");
    }

    // 🔴 적 발밑 그림자(Enemy.BuildShadowSprite)는 16×8px · Point 필터다. 범위 그림자는 그걸 10배 이상
    //    늘려 쓰므로 픽셀이 그대로 커져 각져 보였다(2026-09-20 사용자: "완전 각져있잖아").
    //    그래서 전용 원형을 따로 굽는다 — 128×128, PPU를 크기와 같게 줘서 **bounds가 정확히 1×1유닛**이고
    //    (그래야 localScale이 곧 월드 크기가 된다), 가장자리 한 픽셀을 부드럽게 깎고 Bilinear로 읽는다.
    private static Sprite BuildSwingShadowSprite()
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float edge = 2f / S;                                  // 안티앨리어싱 폭(정규 좌표)
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float nx = (x + 0.5f) / S * 2f - 1f;
                float ny = (y + 0.5f) / S * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float a = Mathf.Clamp01((1f - r) / edge);     // 원 안 1 → 경계에서 0으로 매끄럽게
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, 0.5f), S);
    }

    // 기본 범위(SwingReach) 대비 지금 범위가 몇 배인가. FireSwing/SwingRoutine의 reach 계산과 같은 식이다.
    private static float SwingRangeMult(EquippedSkill swing) =>
        (swing.PathTier[1] >= 2 ? SwingRoute1ReachMult : 1f) * swing.Scale;

    // ── 하늘 파쇄기(화살 R1 2차)의 우주선 ────────────────────────────────────
    // 🔴 **화면 기준**이다(2026-09-19 사용자 지시). 맵마다 `cameraYLift`·카메라 배율이 달라서
    //    월드 좌표로 두면 농장·해변·우주에서 서로 다른 자리에 뜬다 — 매 프레임 카메라를 따라간다.
    //    스프라이트 중심을 화면 위 가장자리에 맞춰 **위 절반이 잘리고 아래 절반만** 보이게 한다.
    // ⚠️ `LateUpdate`에서 부른다 — `ScreenShake`가 카메라를 옮긴 **뒤**라야 배가 화면에 붙어 있다.
    private const int SkyShredderShipSortingOrder = 350;   // 화살(Projectile_BasicAttack = 300)보다 위
    private const float SkyShredderShipRefOrtho = 6.0625f;    // 농장 화면 반높이(5 × fieldScale 1.25 × 흔들림 여유 0.97)
    private const float SkyShredderShipDropPerOrtho = 0.5f;   // 화면 반높이가 1 늘 때마다 배를 이만큼 내린다
    private GameObject skyShredderShip;

    private void UpdateSkyShredderShip()
    {
        bool active = false;
        for (int i = 0; i < equippedSkills.Count; i++)      // LINQ는 매 프레임 열거자를 할당한다 — 쓰지 않는다
            if (equippedSkills[i].Id == ActiveSkillId.BasicAttack && equippedSkills[i].PathTier[2] >= 3)
            { active = true; break; }

        if (!active)
        {
            if (skyShredderShip != null) skyShredderShip.SetActive(false);
            return;
        }
        if (skyShredderShipPrefab == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        if (skyShredderShip == null)
        {
            skyShredderShip = Instantiate(skyShredderShipPrefab);
            foreach (SpriteRenderer sr in skyShredderShip.GetComponentsInChildren<SpriteRenderer>(true))
                sr.sortingOrder = SkyShredderShipSortingOrder;
        }
        if (!skyShredderShip.activeSelf) skyShredderShip.SetActive(true);

        Vector3 camPos = cam.transform.position;
        // 🔴 넓은 맵일수록 조금씩 내린다(2026-10-06 사용자 "해변 / 우주로 갈수록 조금씩 내려줘").
        //    배는 월드 크기가 고정이라 화면이 넓어질수록 작아 보이는데, 위 절반이 잘리는 건 그대로라 더 안 보였다.
        //    농장 화면(ortho 6.0625)을 기준으로 화면 반높이가 늘어난 만큼의 절반을 내린다 — 농장 0 · 해변 약 0.6 · 우주 약 1.3유닛.
        float drop = Mathf.Max(0f, cam.orthographicSize - SkyShredderShipRefOrtho) * SkyShredderShipDropPerOrtho;
        skyShredderShip.transform.position = new Vector3(camPos.x, camPos.y + cam.orthographicSize - drop, 0f);
    }

    private void LateUpdate()
    {
        UpdateSkyShredderShip();   // 휘두르기 조기 return보다 앞 — 휘두르기가 없어도 배는 떠 있어야 한다

        // Animator가 Hammer 자식의 **스프라이트만** 건드리므로(Pinapple_Attack*.anim) localScale은 여기서 줘도 안 덮인다.
        EquippedSkill swing = null;
        for (int i = 0; i < equippedSkills.Count; i++)          // LINQ는 매 프레임 열거자를 할당한다 — 쓰지 않는다
            if (equippedSkills[i].Id == ActiveSkillId.Swing) { swing = equippedSkills[i]; break; }

        if (swing == null)
        {
            if (swingShadowTr != null) swingShadowTr.gameObject.SetActive(false);
            return;
        }

        float mult = SwingRangeMult(swing);

        if (hammerTr == null && animator != null)
        {
            hammerTr = animator.transform.Find("Hammer");        // 망치 없는 캐릭터(딸기 등)는 null로 남는다
            if (hammerTr != null) { hammerBaseScale = hammerTr.localScale; hammerBaseLocalPos = hammerTr.localPosition; }
        }
        // 🔴 팔라딘 망치 그림은 308x187로 1차(150x96)의 **2.05배**다. 그대로 두면 화면 폭의 151%를 먹는다(실측).
        //    "크기 키우고 더 화려하게"(칸반)는 맞지만 화면을 넘기면 안 되므로, 그림이 커진 몫을 되돌리고
        //    1차 대비 의도한 배율(PaladinHammerLook)만 남긴다. 그림을 다시 그리면 이 상수만 고치면 된다.
        float look = swing.PathTier[1] >= 3 ? PaladinHammerArtComp : 1f;
        if (hammerTr != null)
        {
            // 🔴 **성장 몫만 키운다**(2026-09-27 사용자: "망치가 커지는 정도가 실제 공격 범위가 느는 것에 비해서 좀 적다").
            //    레벨 1(mult=1)은 그대로 두고, 1을 넘은 만큼만 HammerGrowthGain배로 부풀린다 —
            //    hammerBaseScale은 프리팹 원래 크기라 SwingReach와 대응하는 값이 아니고, 그래서 처음부터 있던
            //    격차가 mult와 함께 커져 레벨이 오를수록 눈에 띄었다.
            // 🔴 지진파 루트(path2)는 망치를 키우지 않는다(사용자 지시 2026-09-30) — 진화해도 Scale이 초기화되지 않아
            //    진화 전 휘두르기 시절의 크기를 물려받아 커져 있었다. 그림만 원래 크기로 두고 판정 범위는 그대로다.
            float grown = swing.PathTier[2] >= 2 ? 1f : 1f + (mult - 1f) * HammerGrowthGain;
            hammerTr.localScale = hammerBaseScale * grown * look;
            hammerTr.localPosition = hammerBaseLocalPos
                + (swing.PathTier[1] >= 3 ? PaladinHammerOffset : Vector3.zero);

        }

        // 휘두르는 창 밖에서는 그림자를 걷는다(망치 크기 배율은 위에서 계속 유지한다).
        if (Time.time > swingShadowUntil)
        {
            if (swingShadowTr != null && swingShadowTr.gameObject.activeSelf)
                swingShadowTr.gameObject.SetActive(false);
            return;
        }
        UpdateSwingShadow(SwingReach * mult);
    }

    // 판정 사각형의 가로 구간(플레이어 왼쪽 SwingNearOffset ~ reach)을 발밑 타원으로 깐다.
    // 플레이어의 자식으로 두면 프리팹 1.5배와 좌우 반전을 같이 물려받으므로 **월드에 따로 두고** 매 프레임 맞춘다.
    private void UpdateSwingShadow(float reach)
    {
        float width = reach - SwingNearOffset;
        if (width <= 0f) { if (swingShadowTr != null) swingShadowTr.gameObject.SetActive(false); return; }

        if (bodySrCache == null && animator != null) bodySrCache = animator.GetComponent<SpriteRenderer>();
        SpriteRenderer bodySr = bodySrCache;

        if (swingShadowTr == null)
        {
            if (swingShadowSprite == null) swingShadowSprite = BuildSwingShadowSprite();
            var go = new GameObject("SwingRangeShadow");
            swingShadowSr = go.AddComponent<SpriteRenderer>();
            swingShadowSr.sprite = swingShadowSprite;
            swingShadowSr.color = new Color(0f, 0f, 0f, SwingShadowAlpha);
            swingShadowTr = go.transform;
        }
        if (!swingShadowTr.gameObject.activeSelf) swingShadowTr.gameObject.SetActive(true);

        if (bodySr != null)
        {
            swingShadowSr.sortingLayerID = bodySr.sortingLayerID;
            swingShadowSr.sortingOrder = bodySr.sortingOrder - 2;   // 본체·적보다 뒤(바닥)
        }

        // BuildSwingShadowSprite는 bounds가 정확히 1×1유닛이라 localScale이 곧 월드 크기다(나눗셈 필요 없음).
        float height = width * SwingShadowFlatten;
        swingShadowTr.localScale = new Vector3(width, height, 1f);
        // 발밑 선에 중심을 두면 납작해도 절반이 아래로 빠진다 — 높이의 절반만 올려 **바닥선에 얹는다**.
        float groundY = bodySr != null ? bodySr.bounds.min.y + 0.05f : transform.position.y;
        swingShadowTr.position = new Vector3(transform.position.x - (reach + SwingNearOffset) * 0.5f,
                                             groundY + height * 0.5f, 0f);
    }

    // 내려찍은 자리에서 맵 끝까지 달려나가는 충격파. 본체보다 약하게 때리고 살짝만 밀어낸다.
    // subDamage / subScale = 레벨업 "파동 피해"·"파동 크기"(2026-09-29 사용자). 본체 휘두르기와 따로 큰다.
    private void SpawnShockwave(float damage, float critChance, bool empowered, float subDamage = 1f, float subScale = 1f)
    {
        if (swingShockwavePrefab == null) return;

        Vector3 pos = transform.position + Vector3.left * ShockwaveSpawnOffset + Vector3.up * ShockwaveSpawnYOffset;
        // 2차 「거대한 파도」는 전용 그림(바다망치/Effect_Wave). 미배선이면 1차 지진파 그림.
        GameObject wavePrefab = empowered && giantWavePrefab != null ? giantWavePrefab : swingShockwavePrefab;
        GameObject obj = Instantiate(wavePrefab, pos, Quaternion.identity);
        obj.transform.localScale *= subScale;
        SwingShockwave wave = obj.GetComponent<SwingShockwave>();
        wave.Damage = damage * (empowered ? ShockwaveEmpoweredDamageRatio : ShockwaveDamageRatio) * subDamage;
        wave.CritChance = critChance;
        wave.Knockback = empowered ? ShockwaveEmpoweredKnockback : ShockwaveKnockback;
        // 2차 「거대한 파도」만 취약을 건다 — 노션 "맞은 적들이 받는 피해가 증가한다".
        if (empowered) wave.VulnerableMultiplier = ShockwaveVulnerableMult;

        // 파도 그림은 350x300으로 1차 충격파(62x24)보다 훨씬 커서, 같은 스폰 높이에 두면
        // 화면 아래로 6유닛이나 내려간다(실측). **보이는 아래끝을 지면에** 맞춘다.
        // Effect_Wave 아래 투명 여백 = 52px = 2.44유닛(알파 실측).
        if (empowered && giantWavePrefab != null) AlignVisibleBottomToGround(obj, GiantWaveAlphaPad);
    }

    private const float GiantWaveAlphaPad = 2.44f;

    private const float ShockwaveVulnerableMult = 1.3f;   // 거대한 파도에 맞은 적이 받는 피해 배율

    private void SwingHit(float damage, float critChance, float reach, float halfHeight, float knockback, bool stun, int lifestealPerHit, float stunDuration = SwingStunDuration)
    {
        float px = transform.position.x;
        float py = transform.position.y + SwingCenterYOffset; // 판정 사각형의 세로 중심

        using (Enemy.GetSnapshot(out List<Enemy> enemies))
            foreach (Enemy e in enemies)
            {
                if (e == null || !e.IsAlive) continue;
                Vector3 p = e.transform.position;
                // 🔴 보스는 **몸(콜라이더)이 판정 사각형에 닿으면** 맞는다(사용자 지시 2026-09-30: 크라켄이 범위 그림자 안에
                //    들어왔는데 안 맞았다). 그림이 7.5유닛이라 중심점만 보면 몸 앞부분이 그림자에 들어와도 중심이 사거리 밖이다.
                //    일반 적은 중심점 그대로다 — 크기 0인 영역이라 같은 식이 예전 판정과 똑같이 돈다.
                Bounds b = e.IsBoss && e.TryGetComponent(out Collider2D bossCol) ? bossCol.bounds : new Bounds(p, Vector3.zero);
                if (px - b.min.x < SwingNearOffset || px - b.max.x > reach) continue;
                if (b.min.y > py + halfHeight || b.max.y < py - halfHeight) continue;

                e.TakeSkillHit(damage, critChance, ActiveSkillId.Swing, stun ? StatusIconLibrary.Stun : null);
                e.ApplyKnockback(knockback);
                if (stun) e.ApplyStun(stunDuration);
                if (lifestealPerHit > 0 && health != null) health.AddOverheal(lifestealPerHit);
                SpawnRockDebris(p);
            }
    }

    // 맞은 적 자리에서 돌조각이 위쪽 반원으로 튄다.
    private void SpawnRockDebris(Vector3 at)
    {
        if (rockDebrisPrefab == null) return;

        for (int i = 0; i < RockDebrisPerHit; i++)
        {
            GameObject go = ObjectPool.Instance.Spawn(rockDebrisPrefab, at, Quaternion.identity);
            RockDebris debris = go.GetComponent<RockDebris>();
            if (debris == null) continue;

            float deg = Random.Range(25f, 155f); // 위쪽 반원 — 아래로 파고드는 조각이 없게
            Vector2 dir = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
            debris.Launch(dir * Random.Range(RockDebrisSpeed * 0.6f, RockDebrisSpeed), Random.Range(-360f, 360f));
        }
    }

    private const float RewindVfxHeight = 1.9f; // 되감기 표식이 뜨는 머리 위 높이(유닛)

    // 되감기 진화 손잡이 — 문구가 말하는 것과 1:1로 붙여 둔다(§evo.active.desc.Rewind.*).
    private const float RewindAcceleratedMult = 1.6f;      // R1 1차 "되감기가 강력해지고"
    private const float RewindChargeDamageBonus = 0.5f;      // R0 1차 「충전 되감기」 다음 피해 +50% (0.3 → 0.5, 사용자 승인 2026-10-01 — 3회차 대변인 처방)
    private const float RewindOverchargeDamageBonus = 3.5f;  // R0 2차 「과충전」 다음 피해 +350%
    private const int RewindOverchargeBonusHits = 2;        // R0 2차 "타수 +2"
    private const float RewindOverchargeCooldownMult = 2f;  // R0 2차 "그 공격의 쿨타임 2배"
    // 레벨업 "쿨 페널티 감소"가 깎은 뒤의 실효 배수. 되감기를 쓸 때마다 다시 계산된다.
    private static float overchargeCooldownMult = RewindOverchargeCooldownMult;

    // ── 되감기: 다른 스킬의 쿨타임을 앞당긴다 ──
    private void FireRewind(EquippedSkill skill)
    {
        // 되감기 정도(앞당길 시간) — 레벨업 "되감기 시간" 스텝이 늘린다.
        // R1(가속 되감기, path2) 1차: **되감기 자체가 강력해진다**(2026-09-08 문구 "되감기가 강력해지고").
        //   예전엔 이 루트 1차에 GCD 절반뿐이라 "무엇이 강해졌는지"가 화면에 안 보였다.
        float amount = skill.RewindAmount * (skill.PathTier[2] >= 2 ? RewindAcceleratedMult : 1f);

        foreach (EquippedSkill s in equippedSkills)
            if (s != skill) s.CooldownTimer = Mathf.Max(0f, s.CooldownTimer - amount);

        // 스킬트리 "블루베리 둔화"(Rewind_Slow) 해금 시: 되감을 때 모든 적을 천천히 감아 둔화(50% 감속, 2초)
        if (MetaBonuses.RewindSlowAll)
            foreach (Enemy e in Enemy.Active) // 둔화만 건다(처치·스폰 없음) — 복사본 불필요
                if (e != null) e.ApplySlow(0.5f, 2f);

        // R0(충전 되감기, path1): 다음에 사용하는 스킬의 피해를 1회 증가 (ComputeBaseDamage가 소비)
        // 🔴 1차 +30% · 2차 「과충전」 +350%(사용자 결정 2026-09-28). 2차는 여기에 **타수 +2**를 얹고
        //    그 대신 그 스킬의 쿨이 2배가 된다(2026-09-08 사용자 지시).
        //    종전엔 값이 셋(0.3 · 0.6 · 0.5)이었는데 첫 0.3은 PathTier 1용이라 도달하지 않는 분기였다 —
        //    진화는 항상 PathTier 2로 건너뛴다(EvolutionRoutes.TargetPathTier).
        if (skill.PathTier[1] >= 1)
        {
            // 레벨업 "강화량"(EmpowerBonus)이 다음 공격에 실리는 보너스를 키운다(2026-09-29 사용자).
            nextSkillDamageBonus = (skill.PathTier[1] >= 3 ? RewindOverchargeDamageBonus : RewindChargeDamageBonus)
                                   * skill.EmpowerMult;
            nextSkillBonusHits = skill.PathTier[1] >= 3 ? RewindOverchargeBonusHits : 0;
            // 레벨업 "쿨 페널티 감소"(PenaltyReduction) — 과충전이 다음 공격에 물리는 쿨 2배를 깎는다.
            overchargeCooldownMult = 1f + (RewindOverchargeCooldownMult - 1f) * (1f - skill.PenaltyReduction);

            // 2차 「과충전」만 버프 아이콘을 띄운다 — **다음 스킬을 한 번 쓰면 사라진다**(2026-09-19 사용자 지시).
            // 끝나는 시각이 없는 상태라 무한으로 두고, 소비 지점(TryUseSkill)에서 Clear한다.
            if (skill.PathTier[1] >= 3)
            {
                BuffTracker.Set("RewindOvercharge", float.MaxValue, showTimer: false);
                SkillSfx.Play("Overcharge.apply"); // 거는 순간 — 소비는 TryUseSkill 쪽에서 따로 운다
            }
        }

        // 되감기는 여태 화면에 아무것도 안 나왔다 — 머리 위에 표식을 한 번 띄운다.
        // R0(충전 되감기)만 전용 그림이 있다. R1(가속 되감기)과 진화 전은 기본 그림.
        GameObject rewindVfx = rewindVfxPrefab;
        if (skill.PathTier[1] >= 1 && rewindRoute2VfxPrefab != null) rewindVfx = rewindRoute2VfxPrefab;
        if (rewindVfx != null)
            ObjectPool.Instance.Spawn(rewindVfx, transform.position + Vector3.up * RewindVfxHeight, Quaternion.identity);

        TriggerAttackBody();
    }

    // 되감기 R1(가속 되감기, path2)이 전역 쿨타임(GCD)을 줄인다. 1차 = 절반, 2차 = **아예 없앤다**.
    // ⚠️ 1차 = PathTier 2, 2차 = 3.
    private float GlobalCooldownScale()
    {
        foreach (EquippedSkill s in equippedSkills)
            if (s.Id == ActiveSkillId.Rewind && s.PathTier[2] >= 2)
                return s.PathTier[2] >= 3 ? 0f : 0.5f;   // 2차 「블루베리 절멸의 시간」 = GCD 소멸
        return 1f;
    }

    // 🔴 되감기 R1 2차만은 **진화가 쿨을 줄인다** — 2026-09-07의 "진화 쿨감 금지"에 대한 유일한 예외다
    //    (사용자 결정 2026-09-08: "기본 쿨감이 없단 소리지 쿨감이 진화효과면 하는 게 맞지").
    //    그게 이 루트의 정체이고, 문구도 "모든 스킬의 쿨타임이 감소하고"라고 말한다.
    //    ⚠️ 새 진화에 쿨감을 넣을 땐 여기 예외가 하나 더 느는 것임을 알고 넣을 것.
    // 🔴 0.7 → 0.5 (사용자 결정 2026-09-28). **곱연산**이라 다른 쿨감과 더해지지 않고 그 위에 곱해진다.
    //    그 대신 이 진화는 스킬 자체를 쓸 수 없게 됐다(IsPassiveState) — 가진 것만으로 효과가 난다.
    private const float RewindEndTimesCooldownMult = 0.5f;

    private float RewindEndTimesCooldownScale()
    {
        foreach (EquippedSkill s in equippedSkills)
            // 레벨업 "모든 쿨타임 감소"(AllCooldown)가 진화가 주는 0.5 위에 더 곱해진다(2026-09-29 사용자).
            if (s.Id == ActiveSkillId.Rewind && s.PathTier[2] >= 3)
                return RewindEndTimesCooldownMult * (1f - s.AllCooldownBonus);
        return 1f;
    }

    // 🔴 판 전체에 걸리는 적 둔화. 지금은 「블루베리 절멸의 시간」 레벨업 하나만 이 값을 움직인다.
    //    Enemy.MoveScale이 매 프레임 읽으므로 스킬 목록을 훑지 않고 **갱신 시점에 한 번만** 계산해 둔다.
    public static float GlobalEnemySlowMult { get; private set; } = 1f;

    private void RefreshGlobalEnemySlow()
    {
        float slow = 0f;
        foreach (EquippedSkill s in equippedSkills)
            if (s.Id == ActiveSkillId.Rewind && s.PathTier[2] >= 3) slow = s.EnemySlowBonus;
        GlobalEnemySlowMult = 1f - Mathf.Clamp01(slow);
    }

    private void FireWhirlwind(float damage, float critChance, EquippedSkill skill)
    {
        bool applySlow = skill.PathTier[2] >= 1;
        bool applyVulnerable = skill.PathTier[2] >= 3;

        // R0(화살 연계, path0): 본체를 **3개** 소환하고, 각각이 사라질 때 그 자리에 미니 회오리를 남긴다.
        // 예전엔 시전과 동시에 미니를 흩뿌렸다 — 이제는 "큰 게 수명을 다하면 새끼가 남는다"는 2단 구조다(2026-08-06 명세).
        // 2 → 3(사용자 지시 2026-10-01). 2차 「회오리 생성기」도 이 본체를 같이 쓴다.
        int mainCount = skill.PathTier[0] >= 2 ? 3 : 1;
        int miniOnExpire = skill.PathTier[0] >= 2 ? 2 : 0;
        float miniDamage = damage * MiniWhirlwindDamageRatio;   // 회오리 피해의 30%

        // R0 2차 「회오리 생성기」 — 캐릭터 살짝 뒤에 기계를 세우고 거기서 일정 시간마다 회오리가 나온다
        // (2026-09-19 사용자 명세 / 노션 "회오리를 끊임없이 생성하는 기계를 설치한다").
        // 🔴 예전 2차 효과(미니 2→3)를 **대체**한다 — 사용자가 "지금 2차 효과들은 다 임시"라고 확인했다.
        if (skill.PathTier[0] >= 3)
            SpawnTornadoMaker(skill, damage, critChance, applySlow, applyVulnerable);

        if (skill.PathTier[2] >= 2) // 오브 연계 path T2: 거대 회오리로 대체 (여러 개로 안 쪼개짐)
        {
            // R0과 R1은 배타적이라(한 스킬은 루트 하나만 밟는다) 여기서 miniOnExpire는 항상 0이다.
            Vector3 spawnPos = transform.position + Vector3.left * 0.6f + Vector3.down * 1.4f;
            SpawnBigTornado(spawnPos, damage, critChance, skill.Scale, applySlow, applyVulnerable, skill.ExtraWhirlwindDuration, skill.TickIntervalMult, skill.ProjectileSpeedMultiplier);
        }
        else
        {
            for (int i = 0; i < mainCount; i++)
            {
                // 여러 개일 때 좌우로 벌려 겹쳐 보이지 않게 한다. 가운데가 원래 자리, 이웃 간격 1.8
                // (종전 2개 ±0.7 = 1.4 — 2026-10-01 사용자 "둘이 너무 겹쳐서 나온다, 간격을 조금 늘려줘").
                float spread = mainCount > 1 ? (i - (mainCount - 1) * 0.5f) * WhirlwindMainSpacing : 0f;
                Vector3 spawnPos = transform.position + Vector3.left * (0.6f - spread) + Vector3.up * 0.6f;
                Whirlwind main = SpawnWhirlwind(spawnPos, damage, critChance, skill.Scale, applySlow, applyVulnerable, maxHitCount: 0, slowDuration: 3f, extraLifetime: skill.ExtraWhirlwindDuration, tickIntervalMult: skill.TickIntervalMult, speedMult: skill.ProjectileSpeedMultiplier);
                // R0 본체는 높이와 상관없이 표적을 쫓는다(하늘의 비행선까지). 미니는 소멸 자리에서 원래대로 떨어진다.
                if (main != null && skill.PathTier[0] >= 2) main.HomeInY = true;

                if (miniOnExpire > 0 && main != null)
                {
                    float miniScale = skill.Scale * MiniWhirlwindScale;
                    float miniTick = skill.TickIntervalMult;
                    float miniSpeed = skill.ProjectileSpeedMultiplier;
                    main.OnExpired = pos =>
                    {
                        if (this == null) return; // 플레이어가 먼저 파괴됐으면 코루틴/소환을 걸지 않는다
                        for (int m = 0; m < miniOnExpire; m++)
                        {
                            Vector3 at = pos + (Vector3)(Random.insideUnitCircle * 0.5f);
                            SpawnWhirlwind(at, miniDamage, critChance, miniScale, applySlow, applyVulnerable, maxHitCount: 6, slowDuration: 3f, isMini: true, tickIntervalMult: miniTick, speedMult: miniSpeed);
                        }
                    };
                }
            }
        }

    }

    // 낙뢰 버프(지속시간 표시)와, path2 T3에서만 뜨는 전체피해 보너스 버프(스택만 표시)를 함께 갱신
    private static void RefreshLightningBuffDisplay()
    {
        // 스택이 안 쌓이는 동안(진화 전)은 숫자를 띄우지 않는다 — 늘 1이라 "쌓인다"는 오해만 준다.
        System.Func<int> stacks = LightningStorm.StackingEnabled ? () => LightningStorm.ActiveStackCount : (System.Func<int>)null;
        BuffTracker.Set("Lightning", LightningStorm.LatestEndTime, stacks);
        if (LightningStorm.StackDamageEnabled)
            BuffTracker.Set("LightningDamageBuff", LightningStorm.LatestEndTime, stacks, showTimer: false);
        else
            BuffTracker.Clear("LightningDamageBuff");
    }

    // 자기 Fire/Spawn 안에서 시전음을 직접 울리는 스킬들 — FireSkill 끝의 공용 시전음을 건너뛴다.
    private static bool HasOwnCastSfx(ActiveSkillId id) =>
        id == ActiveSkillId.Whirlwind || id == ActiveSkillId.Orb
        || id == ActiveSkillId.EagleDrop || id == ActiveSkillId.GrapeToss;

    // tickIntervalMult: 레벨업 "타격 주기" 스텝. 예전엔 거대 회오리만 안 받아서 그 카드가 죽은 카드였다(2026-09-18).
    // ── 회오리 생성기(회오리 R0 2차) ────────────────────────────────────────
    // 캐릭터 **뒤쪽**에 세워 두는 설치물. 구조는 피뢰침(SpawnLightningRod)과 같다.
    // ⚠️ 시전할 때마다 **수명만 새로 채운다**(기계를 여러 대 세우지 않는다) — 쿨마다 쓰면 계속 서 있는 셈이 된다.
    private const float TornadoMakerDuration = 8f;       // 한 번 시전으로 서 있는 시간(초)
    private const float TornadoMakerInterval = 1.5f;     // 회오리가 나오는 주기(초)
    // 🔴 기계가 뽑는 것도 같은 회오리다 — 비율 1(사용자 결정 2026-09-28). 본체 회오리와 함께 나가므로
    //    에셋 보정 없이 그대로 올라간다(본체 피해를 낮추면 본체 회오리까지 같이 약해진다).
    private const float TornadoMakerDamageRatio = 1f;
    // 🔴 x는 **캐릭터와 같다**(2026-09-20 사용자). 캐릭터가 화면 오른쪽 끝에 붙어 서 있어서
    //    옆으로 밀면 기계가 화면 밖으로 나간다 — "뒤"는 **레이어**로만 표현하고 자리는 겹쳐 둔다.
    //    ⚠️ 크기로 풀지 말 것. 프리팹 localScale은 규격대로 1.5다(CLAUDE.md §5).
    private const float TornadoMakerBehind = 0f;
    private const int TornadoMakerSortingBelow = 2;      // 캐릭터 sortingOrder보다 이만큼 뒤에 그린다
    private const float TornadoMakerAlphaPad = 0.14f;    // Effect_TornadoMaker 아래 투명 여백 3px(알파 실측)

    [SerializeField] private GameObject tornadoMakerPrefab; // Effect_TornadoMaker 플립북. 미배선이면 기계 없이 회오리만 나온다
    private GameObject tornadoMaker;
    private float tornadoMakerUntil;
    private Coroutine tornadoMakerRoutine;

    private void SpawnTornadoMaker(EquippedSkill skill, float damage, float critChance, bool applySlow, bool applyVulnerable)
    {
        bool fresh = Time.time >= tornadoMakerUntil;
        // 레벨업 "기계 지속시간"(InstallDuration). 기계가 뽑는 회오리의 수명은 Duration 축이 따로 맡는다.
        tornadoMakerUntil = Time.time + TornadoMakerDuration + skill.ExtraInstallDuration;

        if (tornadoMakerPrefab != null && tornadoMaker == null)
        {
            // 회오리 R0 2차는 소환이 아니라 **기계 설치**다(구조가 피뢰침과 같다) — 나뭇잎 소리가 안 맞는 자리.
            SkillSfx.Play("TornadoMaker.install");
            Vector3 at = transform.position + Vector3.right * TornadoMakerBehind;
            tornadoMaker = Instantiate(tornadoMakerPrefab, at, Quaternion.identity);

            int baseOrder = 0;
            SpriteRenderer body = GetComponentInChildren<SpriteRenderer>();
            if (body != null) baseOrder = body.sortingOrder;
            foreach (SpriteRenderer sr in tornadoMaker.GetComponentsInChildren<SpriteRenderer>(true))
                sr.sortingOrder = baseOrder - TornadoMakerSortingBelow;

            // 🔴 **보이는 바닥면을 캐릭터의 보이는 밑면에 맞춘다**(2026-09-20 사용자).
            //    양쪽 다 투명 여백이 있어서 rect 경계로 맞추면 기계가 0.7유닛 파묻힌다.
            //    Effect_TornadoMaker의 아래 여백 = 3px = 0.14유닛(알파 실측).
            AlignVisibleBottomToGround(tornadoMaker, TornadoMakerAlphaPad);
        }

        // 수명이 끝난 직후 재시전하면 옛 루틴이 마지막 대기(최대 1.5초)에서 깨어나 갱신된 수명을 보고 계속 돈다 —
        // 그러면 기계 하나에서 회오리가 두 배로 나온다. 새로 돌리기 전에 옛 루틴을 멈춘다.
        if (fresh)
        {
            if (tornadoMakerRoutine != null) StopCoroutine(tornadoMakerRoutine);
            tornadoMakerRoutine = StartCoroutine(TornadoMakerRoutine(skill, damage, critChance, applySlow, applyVulnerable));
        }
    }

    private IEnumerator TornadoMakerRoutine(EquippedSkill skill, float damage, float critChance, bool applySlow, bool applyVulnerable)
    {
        float makerDamage = damage * TornadoMakerDamageRatio;
        while (Time.time < tornadoMakerUntil)
        {
            yield return new WaitForSeconds(TornadoMakerInterval);
            if (this == null) yield break;

            Vector3 from = tornadoMaker != null ? tornadoMaker.transform.position
                                                : transform.position + Vector3.right * TornadoMakerBehind;
            SpawnWhirlwind(from + Vector3.up * 0.4f, makerDamage, critChance, skill.Scale,
                           applySlow, applyVulnerable, maxHitCount: 0, slowDuration: 3f,
                           extraLifetime: skill.ExtraWhirlwindDuration, tickIntervalMult: skill.TickIntervalMult,
                           speedMult: skill.ProjectileSpeedMultiplier);
        }

        if (tornadoMaker != null) { Destroy(tornadoMaker); tornadoMaker = null; }
        tornadoMakerRoutine = null;
    }

    private void SpawnBigTornado(Vector3 position, float damage, float critChance, float scale, bool applySlow, bool applyVulnerable, float extraLifetime = 0f, float tickIntervalMult = 1f, float speedMult = 1f)
    {
        SkillSfx.Play("Whirlwind.cast");
        // 2차 「하늘의 울음」은 전용 그림(Effect_SuperTornado)이 있다. 미배선이면 1차 대회오리 그림으로 떨어진다.
        GameObject prefab = applyVulnerable && skyWailTornadoPrefab != null ? skyWailTornadoPrefab
                          : bigTornadoPrefab != null ? bigTornadoPrefab : whirlwindPrefab;
        GameObject obj = Instantiate(prefab, position, Quaternion.identity);
        obj.transform.localScale *= scale;
        Whirlwind whirlwind = obj.GetComponent<Whirlwind>();
        whirlwind.Damage = damage;
        whirlwind.ApplyGemSlow = applySlow;
        whirlwind.ApplyGemVulnerable = applyVulnerable;
        whirlwind.CritChance = critChance;
        whirlwind.SlowDuration = 4.5f;
        whirlwind.ExtraLifetime = extraLifetime;
        whirlwind.TickIntervalMult = tickIntervalMult;
        // 2차 「하늘의 울음」만 1차 대회오리보다 빠르다(2026-09-19 사용자: "속도가 약간 빨라지면 좋을듯").
        // applyVulnerable이 곧 2차 플래그다(PathTier[2] >= 3) — 같은 조건이라 인자를 늘리지 않는다.
        // 레벨업 "이동속도"(speedMult)가 그 위에 곱해진다(2026-09-29 사용자).
        whirlwind.SpeedMultiplier = (applyVulnerable ? SkyWailSpeedMult : 1f) * speedMult;
    }

    private const float SkyWailSpeedMult = 1.2f;   // 하늘의 울음 이동 속도 — "약간"으로 잡은 값

    // 총구 화염 손잡이. 알마다 하나씩이 아니라 **한 번 쏠 때(=볼리마다) 하나**다(사용자 명세 2026-09-02).
    private const float ScatterFireScale = 1.8f;      // 🔴 절대값이다 — 풀에서 재사용되므로 곱하면 매번 커진다
    private const float ScatterFireMuzzleGap = 0.75f; // 몸 중심에서 총구까지(유닛)

    // ── 전탄발사(산탄 R1: 메카 버스터 → 초강력 섬멸용 전탄발사) 손잡이 ──────────
    // 한 방으로 끝나던 산탄이 **여기서만** 일정 시간 전방을 훑는 연사가 된다
    // (사용자 명세 2026-09-02 — 메이플 메탈아머 전탄발사).
    // 기본 지속(초). 레벨업 "지속시간" 스텝이 더해진다.
    // 2026-09-20 사용자 "메카 버스터 1렙 지속시간 30% 정도 줄여" → 2 에서 1.4로.
    // ⚠️ 총 피해는 안 변한다 — Barrage가 perPelletDamage를 볼리 수로 나눠 총량을 보존한다. 같은 양이 더 짧게 몰릴 뿐이다.
    private const float BarrageBaseDuration = 1.4f;
    private const float BarrageBandHeight = 4.5f;      // 세로로 훑는 총 높이(유닛)
    private const float BarrageBandDown = 1.2f;        // 그중 발사점 **아래**로 내려가는 몫 — 지면 바로 위까지만

    // 🔴 2026-09-19 사용자 명세로 두 루트를 갈라 놨다.
    // 「메카 버스터」(R1 1차): "Y축 범위가 더 길어야 하고, 탄환을 한 번에 빵 발사하는 대신
    //                          **두두두두 무작위 위치에서 계속** 발사되면 좋겠어."
    //   → 세로 범위를 넓히고, 볼리 간격을 절반으로 줄이고, 한 볼리의 알 수를 나눠 연사처럼 보이게 한다.
    // 「초강력 섬멸용 전탄발사」(R1 2차): "같은 메커니즘이지만 **훨씬 넓은 범위**에서 탄환도
    //                          **거의 맵 중간까지** 날아가고 **훨씬 오래** 쏜다."
    private const float MechaBusterBandHeight = 7f;
    private const float MechaBusterBandDown = 2.4f;
    private const float MechaBusterVolleyInterval = 0.06f;
    private const int MechaBusterVolleySplit = 3;        // 한 볼리의 알을 이 수로 나눠 연사한다(최소 1발)

    // (전탄발사의 세로 구간은 상수가 아니라 **기계의 보이는 높이**에서 나온다 — FullBurstVisibleHeight·FullBurstBandDown())
    private const float FullBurstVolleyInterval = 0.05f;
    // "훨씬 오래" — 진화 직후 정확히 5초(2026-10-06 사용자: 기본 5초 · 만렙 7초쯤). 2차 지속 카드 +0.7초가 다 붙으면 7.5초다.
    //   종전 2.5배는 1차에서 넘어온 +0.7초와 겹쳐 5.25~7초였고, 풀트리 쿨감과 만나 가동률이 100%를 넘었다.
    private const float FullBurstDurationMult = 5f / BarrageBaseDuration;
    // 🔴 발당 피해를 정하는 기준 지속 배율 — 위 값을 바꿔도 **쏘는 동안의 화력은 그대로** 두려고 종전 2.5에 묶어 둔다.
    //    이걸 같이 올리면 발당 피해가 그만큼 줄어든다.
    private const float FullBurstDamageRefDurationMult = 2.5f;
    // 사거리는 알 수명이 정한다. 2026-09-20 사용자 "탄환 가는 범위가 30% 정도 짧아야" → 2.4 × 0.7.
    private const float FullBurstPelletLifetimeMult = 1.68f;
    // 관통 **무한**(사용자 결정 2026-09-02). 알은 사거리(=`Scatter_Pellet`의 lifetime)가 다할 때까지 뚫고 지나간다.
    // 같은 적을 두 번 때리지는 않는다(`SmallOrb.hitEnemies`), 방패 블루베리는 관통과 무관하게 끊는다.
    private const int BarragePierce = int.MaxValue;

    // 🔴 **미진화 산탄은 한 프레임에 전탄을 동시에** 내보낸다(사용자 명세, 8/25 빌드 검수).
    //    한 볼리 안에서 알을 시간차로 나눠 쏘면 산탄이 아니게 된다.
    //    🔴 대신 **그 볼리를 두 번** 쏜다 — "빵 빵"(사용자 명세 2026-09-17). 볼리마다 피해는 그대로다(총량 2배).
    //    🔴 2026-09-19 사용자: 볼리 수가 **레벨업 성장축**이 됐다(알 수 증가를 빼고 그 자리에 넣었다) —
    //       "투사체 1 늘어봐야 티도 안 난다"는 지적. 만렙에 2 → 4회가 된다.
    private const int ShotgunBaseVolleys = 2;
    // 🔴 R0 1차 「보너스 탄환 장착」은 **4회 고정**(사용자 결정 2026-09-30). 진화해도 묶음 수(ExtraVolleys)가 초기화되지 않아
    //    진화 전 카드로 쌓인 8회를 그대로 쐈다 — 이 루트의 본체는 타수 버프라 탄은 고정한다. 2차는 탄을 안 쏜다.
    private const int BonusBulletVolleys = 4;
    private static int ShotgunVolleyCount(EquippedSkill skill) =>
        skill.EvolutionStage >= 1 && skill.PathTier[1] > 0 ? BonusBulletVolleys
        : Mathf.Max(1, ShotgunBaseVolleys + skill.ExtraVolleys);
    private const float ShotgunVolleyGap = 0.25f; // 볼리 사이 간격(초)
    //    ⚠️ 이 규칙은 이제 **미진화 산탄에만** 적용된다. R1 메카 버스터(path2 T2+)는 2026-09-02에 연사로 바뀌었고,
    //       거기서는 애니메이션이 볼리마다 도는 것이 의도다(사용자 결정).
    //    빠르기·사거리는 `Scatter_Pellet.prefab`의 `moveSpeed`·`lifetime`이 정한다(게임에서 제일 빠른 투사체).
    // 알 수. 발사와 레벨업 카드가 같이 쓴다(extraProjectiles만 바꿔 넣어 증가량을 센다). R1(메카 버스터)은 1차 ×2 · 2차 ×3.
    private static int ShotgunPelletCount(EquippedSkill skill, int extraProjectiles)
    {
        int pellets = Mathf.Max(1, ShotgunBasePellets + extraProjectiles);
        if (skill.PathTier[2] >= 3) pellets *= 3;
        else if (skill.PathTier[2] >= 2) pellets *= 2;
        return pellets;
    }

    // 적이 오는 왼쪽으로 부채꼴 산탄. 알이 늘어도 각도는 그대로라 **촘촘해지는 것**이 눈에 보인다.
    private void FireShotgunPellets(float damage, float critChance, EquippedSkill skill)
    {
        if (shotgunPelletPrefab == null && scatterPelletPrefab == null) return;

        int pellets = ShotgunPelletCount(skill, skill.ExtraProjectiles); // 레벨업 주 성장축

        // R1(휘두르기 연계, path2): 탄이 많아지고(ShotgunPelletCount) **부채꼴 대신 전방으로 몰아 쏜다**. 피해도 오른다(2026-08-06 명세).
        // 각도를 0으로 좁히는 게 핵심 — 흩어지던 화력이 정면 한 줄기에 전부 실린다.
        float spreadDegrees = skill.PathTier[2] >= 2 ? 0f : BalanceConstants.ShotgunSpreadDegrees;
        // 🔴 R1 루트의 펠릿은 그 자체가 공격이므로 진화 배수(1.3·1.6)를 없앴다 — 값은 Evo_Shotgun_R1_*.baseDamage가 정한다(2026-09-28).
        // 스킬트리 "추가 장전"(shotgun_BonusHit) = 산탄 발사 피해 +10%. 부채꼴·전탄발사 둘 다 이 값을 쓴다.
        float pelletDamage = damage * (1f + MetaBonuses.ShotgunDamageBonus);

        // 🔴 2차 「초강력 섬멸용 전탄발사」는 **무조건 치명타로 명중한다**(노션 UI 문구). 확률을 1로 고정한다.
        if (skill.PathTier[2] >= 3) critChance = 1f;

        if (skill.PathTier[2] >= 2)
        {
            // 🔴 지속 중에 다시 쓰면 앞 연사를 끊고 새로 시작한다(2026-10-06 사용자 — 종전엔 둘이 겹쳐 돌았다).
            StopBarrage();
            barrageRoutine = StartCoroutine(Barrage(skill, pellets, pelletDamage, critChance));
            return;
        }

        StartCoroutine(ShotgunDoubleShot(skill, pellets, spreadDegrees, pelletDamage, critChance));
    }

    private IEnumerator ShotgunDoubleShot(EquippedSkill skill, int pellets, float spreadDegrees, float pelletDamage, float critChance)
    {
        int volleys = ShotgunVolleyCount(skill);
        for (int v = 0; v < volleys; v++)
        {
            if (v > 0) yield return new WaitForSeconds(ShotgunVolleyGap);
            // 🔴 관통 무한(2026-09-19 사용자 — "막히는 게 좀 어색해 보임"). 예전엔 0이라 첫 명중에 사라졌다.
            //    사거리는 여전히 `Scatter_Pellet`의 lifetime이 정한다(약 7유닛) — 화면을 끝까지 뚫진 않는다.
            FireVolley(skill, pellets, spreadDegrees, pelletDamage, critChance, pierce: int.MaxValue);
        }
    }

    // 전탄발사 — 같은 볼리를 지속시간 동안 되풀이한다.
    // 🔴 발당 피해는 **기본 지속시간 기준으로 고정**이다(사용자 결정 2026-09-29). 지속시간을 늘리면
    //    볼리가 더 나가 총량이 그만큼 는다 — 지속시간이 이 두 진화의 화력 성장축이다.
    //    (2026-09-02~09-28에는 총량 보존이었다. 그때는 지속 카드가 연출만 바꿨다.)
    private IEnumerator Barrage(EquippedSkill skill, int pellets, float pelletDamage, float critChance)
    {
        bool fullBurst = skill.PathTier[2] >= 3;

        float duration = (BarrageBaseDuration + skill.ExtraShotgunDuration) * (fullBurst ? FullBurstDurationMult : 1f);
        float interval = fullBurst ? FullBurstVolleyInterval : MechaBusterVolleyInterval;
        float bandHeight = fullBurst ? FullBurstVisibleHeight : MechaBusterBandHeight;
        float bandDown = fullBurst ? FullBurstBandDown() : MechaBusterBandDown;
        float lifetimeMult = fullBurst ? FullBurstPelletLifetimeMult : 1f;

        // 🔴 전탄발사 기계는 **판당 한 번**만 세운다(볼리마다 겹쳐 띄우면 프레임이 서로 다른 사본이 쌓인다).
        //    탄환은 아래 bandDown/bandHeight로 **이 기계의 보이는 세로 구간 안에서만** 나간다
        //    (2026-09-20 사용자 "기계 위에서도 아래에서도 나오면 안 된다").
        if (fullBurst) SpawnFullBurstMachine();
        // 설치음은 2차 「전탄발사」만 있다 — 기계를 세우기 때문이다. 1차 메카 버스터는 설치물이 없어 연사음만 운다.
        if (fullBurst) SkillSfx.Play("FullBurst.install");

        // 한 볼리를 통째로 쏘지 않고 잘게 나눠 **연사**로 만든다("두두두두"). 총 알 수는 그대로다 —
        // 볼리 수가 늘어난 만큼 볼리당 알 수와 발당 피해가 같이 줄어 총량이 보존된다.
        int perVolley = Mathf.Max(1, Mathf.CeilToInt(pellets / (float)MechaBusterVolleySplit));
        int volleys = Mathf.Max(1, Mathf.RoundToInt(duration / interval));

        // 🔴 2026-09-29 사용자 지시로 계산 방식을 바꿨다. 종전엔 총 피해를 **실제 볼리 수**로 나눠서,
        //    지속시간이 늘면 발당 피해가 그만큼 줄어 총량이 그대로였다 — 지속 카드가 화력이 아니었다.
        //    이제 발당 피해를 **기본 지속시간 기준**으로 굳힌다. 길어진 만큼 볼리가 더 나가고 총량이 는다.
        //    기본 지속에서의 출력은 종전과 완전히 같다(baseVolleys == volleys이므로 식이 일치한다).
        int baseVolleys = Mathf.Max(1, Mathf.RoundToInt(BarrageBaseDuration * (fullBurst ? FullBurstDamageRefDurationMult : 1f) / interval));
        float perPelletDamage = pelletDamage * pellets / (float)(baseVolleys * perVolley);

        activeMuzzleFire = null; // 이 연사가 쓸 불꽃은 첫 볼리에서 새로 띄운다
        // 🔴 연사음은 **볼리마다** 운다(2026-09-27 사용자: 메카 버스터 "볼리마다 뿅뿅뿅" · 전탄발사 "연사 내내").
        //    종전엔 시전 시작에 한 번뿐이라 1.4~3.5초 동안 화면만 쏟아지고 소리가 없었다.
        // ⚠️ 볼리 간격이 0.06(1차)·0.05초(2차)라 **1.4초에 23번 · 3.5초에 70번** 울린다.
        //    `AudioThrottle`은 프레임 단위라 이걸 전혀 안 묶어 준다 — 볼륨으로만 제어된다.
        //    그래서 두 슬롯의 기본 볼륨을 시전음보다 훨씬 낮게 잡아 뒀다(고르기 창에서 조절).
        string volleySfx = fullBurst ? "FullBurst.volley" : "MechaBuster.volley";
        for (int v = 0; v < volleys; v++)
        {
            FireVolley(skill, perVolley, 0f, perPelletDamage, critChance, BarragePierce,
                       bandHeight, bandDown, lifetimeMult, randomBand: true, spawnMuzzle: !fullBurst);
            SkillSfx.Play(volleySfx);
            yield return new WaitForSeconds(interval);
        }
        activeMuzzleFire = null; // 연사 종료 — 다음 시전은 새 불꽃부터
        DespawnFullBurstMachine();
        barrageRoutine = null;
    }

    private Coroutine barrageRoutine;

    // 도는 연사를 끊는다 — 재시전과 스테이지 종료(ClearInstallations)가 부른다. 이미 날아간 알은 그대로 간다.
    private void StopBarrage()
    {
        if (barrageRoutine != null) { StopCoroutine(barrageRoutine); barrageRoutine = null; }
        activeMuzzleFire = null;
        DespawnFullBurstMachine();
    }

    // 한 번의 발사. 총구 화염 · 알 · 공격 애니메이션이 한 세트다.
    // bandHeight/bandDown: 전방 집중(각도 0)일 때 발사 높이를 흩뿌리는 세로 구간. 기본값 = 미진화·기존 동작.
    // randomBand: true면 구간 안에서 **완전 무작위**로 고른다(연사 — 볼리당 알이 적어 층화가 뜻이 없다).
    // lifetimeMult: 알 수명 = 사거리 배율(전탄발사만 늘린다).
    // spawnMuzzle: 전탄발사는 기계를 **판당 한 번**만 세우므로 볼리마다 총구 연출을 띄우지 않는다.
    private void FireVolley(EquippedSkill skill, int pellets, float spreadDegrees, float pelletDamage, float critChance, int pierce,
                            float bandHeight = BarrageBandHeight, float bandDown = BarrageBandDown,
                            float lifetimeMult = 1f, bool randomBand = false, bool spawnMuzzle = true)
    {
        // 전용 그림이 배선돼 있으면 그쪽. 폴백(shotgunPelletPrefab)은 추적 오브와 공유하는 원본이라 그림이 오브다.
        GameObject pelletPrefab = scatterPelletPrefab != null ? scatterPelletPrefab : shotgunPelletPrefab;
        Vector3 origin = transform.position + Vector3.up * 0.2f;

        if (spawnMuzzle) SpawnMuzzleFire(origin);

        for (int i = 0; i < pellets; i++)
        {
            float t = pellets > 1 ? i / (float)(pellets - 1) : 0.5f;
            float angle = Mathf.Lerp(-spreadDegrees, spreadDegrees, t);
            Vector2 dir = Quaternion.Euler(0f, 0f, angle) * Vector2.left;

            // 전방 집중(각도 0)이면 전부 한 점에서 겹쳐 한 발처럼 보인다 — 출발 높이를 세로로 벌린다.
            // 🔴 균등 배치가 아니라 **칸을 나눠 그 칸 안에서만 흔든다**(층화 추출). 균등이면 볼리마다 같은 자리라
            //    격자무늬로 보이고, 완전 무작위면 뭉쳐서 빈 구간이 생긴다.
            Vector3 spawnAt = origin;
            if (spreadDegrees <= 0f)
            {
                // 연사(randomBand)일 땐 볼리당 알이 1~몇 발뿐이라 층화가 뜻이 없다 — 그냥 무작위로 흩는다
                // (사용자 명세: "무작위 위치에서 계속 발사"). 한 방에 다 쏘는 볼리는 층화 추출 그대로.
                float slot = randomBand ? Random.value : (i + Random.value) / pellets;
                spawnAt += Vector3.up * Mathf.Lerp(-bandDown, bandHeight - bandDown, slot);
            }

            // 알 그림은 왼쪽으로 날아가는 형태(궤적이 뒤로 뻗음)라 부채꼴 각도만큼 같이 돌려야 궤적이 진행 방향과 맞는다.
            GameObject obj = Instantiate(pelletPrefab, spawnAt, Quaternion.Euler(0f, 0f, angle));
            obj.transform.localScale *= skill.Scale;
            SmallOrb pellet = obj.GetComponent<SmallOrb>();
            if (pellet == null) continue;
            pellet.CritChance = critChance;
            // 🔴 SmallOrb의 기본 출처가 Orb다 — 안 갈아주면 산탄 피해가 **데미지 미터에 오브로 잡힌다**.
            pellet.Source = ActiveSkillId.Shotgun;
            // 🔴 2026-09-19 사용자 결정으로 **산탄 알은 언제나 무한 관통**이다("막히는 게 어색하다").
            //    예전엔 미진화만 관통 0이었고 그게 정체성이라고 적혀 있었다 — 그 결정은 폐기됐다.
            //    사거리 상한은 `Scatter_Pellet`의 lifetime(약 7유닛)이고, 방패는 관통과 무관하게 끊는다(SmallOrb).
            pellet.PierceRemaining = pierce;
            // 사거리 = 알 수명. 전탄발사만 늘려 "거의 맵 중간까지" 날아가게 한다(Start 전이라 먹는다).
            if (lifetimeMult != 1f) pellet.Lifetime *= lifetimeMult;
            pellet.Init(dir, pelletDamage, false);
        }

        TriggerAttackBody();
    }

    // 총구 화염. 알을 따라가지 않고 정면(왼쪽)을 향한다.
    // 🔴 회전은 0이다 — 불꽃 그림은 **이미 왼쪽(알이 날아가는 쪽)을 보고** 그려져 있다.
    //    예전 -90도 보정은 그림을 위로 세워서 "바닥에서 불이 솟는" 것처럼 보이게 하고 있었다.
    // ⚠️ 앵커를 총구보다 위·뒤에 두는 건 그림 탓이다: 96x96 캔버스의 pivot은 한가운데인데
    //    불꽃은 왼쪽 아래에 치우쳐 그려져 있어(pivot 기준 x -21~+16px · y -39~+3px) 그만큼 되민다.
    // 전탄발사 기계는 여기 오지 않는다 — `SpawnFullBurstMachine`이 판당 한 번 따로 세운다.
    private void SpawnMuzzleFire(Vector3 origin)
    {
        if (scatterFireVfxPrefab == null) return;

        float px = ScatterFireScale / 32f;   // 그림 1px이 몇 유닛인가(PPU 32)
        Vector3 muzzle = origin
            + Vector3.left * (ScatterFireMuzzleGap + 16.5f * px)
            + Vector3.up * (18f * px);

        // 🔴 살아 있는 불꽃이 있으면 **새로 띄우지 않고 위치만 옮긴다**(2026-09-27 사용자).
        //    메카 버스터는 볼리 간격이 0.06초인데 불꽃 애니메이션이 그보다 길어서, 매 볼리 새로 띄우면
        //    여러 장이 겹쳐 붉은 덩어리가 됐다. "두두두두" 느낌은 **발사 자체**(0.06초 간격)가 낸다 —
        //    볼리 간격은 일부러 절반으로 줄인 값이라 건드리지 않는다(MechaBusterVolleyInterval 주석).
        // ⚠️ 총구 높이가 볼리마다 달라지므로(randomBand) 위치 갱신만으로도 불꽃이 살아 움직인다.
        if (activeMuzzleFire != null && activeMuzzleFire.activeInHierarchy)
        {
            activeMuzzleFire.transform.position = muzzle;
            activeMuzzleFire.transform.localScale = Vector3.one * ScatterFireScale;
            return;
        }

        GameObject fire = ObjectPool.Instance.Spawn(scatterFireVfxPrefab, muzzle, Quaternion.identity);
        if (fire != null) fire.transform.localScale = Vector3.one * ScatterFireScale;
        activeMuzzleFire = fire;
    }

    // 지금 화면에 살아 있는 총구 불꽃 1장. 연사 중엔 이것을 옮겨 쓰고, 연사가 끝나면 비워 다음 시전이 새로 띄운다.
    private GameObject activeMuzzleFire;

    // ── 전탄발사 기계(FIRE!!!/fullburst) 자리 ────────────────────────────────
    // 알파 실측(173x144 · scale 1.8): 전체 9.73x8.10 · 아래 여백 0.34 · 위 여백 0.06 · **보이는 높이 7.71**
    private const float FullBurstVfxScale = 1.8f;
    private const float FullBurstAlphaPadBottom = 0.34f;
    private const float FullBurstVisibleHeight = 7.71f;
    private const int FullBurstSortingBelow = 2;     // 🔴 캐릭터보다 **뒤**에 선다(2026-09-20 사용자)

    // 지금 서 있는 기계. 🔴 풀에 "N초 뒤 회수"를 예약하지 않는다(2026-10-06) — 연사를 중간에 끊고 같은 풀 오브젝트를
    //    다시 꺼내 쓰면 옛 예약이 새 기계를 일찍 걷는다. 회수는 Barrage의 끝과 StopBarrage가 직접 한다.
    //    프리팹(Effect_FullBurst)의 플립북은 loop라 스스로 사라지지 않는다 — 종전엔 0.6초 한 번 돌고 회수됐다.
    private GameObject fullBurstMachine;

    private void DespawnFullBurstMachine()
    {
        if (fullBurstMachine == null) return;
        ObjectPool.Instance.Despawn(fullBurstMachine);
        fullBurstMachine = null;
    }

    // 기계를 판당 한 번 세운다. 보이는 아래끝을 지면에 맞추고, 캐릭터 뒤 레이어로 내린다.
    private void SpawnFullBurstMachine()
    {
        if (fullBurstVfxPrefab == null) return;

        float centerY = VisibleGroundY() - FullBurstAlphaPadBottom + FullBurstVisibleHeight * 0.5f;
        Vector3 at = new Vector3(transform.position.x, centerY, 0f);
        GameObject m = ObjectPool.Instance.Spawn(fullBurstVfxPrefab, at, Quaternion.identity);
        if (m == null) return;
        fullBurstMachine = m;
        m.transform.localScale = Vector3.one * FullBurstVfxScale;

        int baseOrder = 0;
        SpriteRenderer body = GetComponentInChildren<SpriteRenderer>();
        if (body != null) baseOrder = body.sortingOrder;
        foreach (SpriteRenderer sr in m.GetComponentsInChildren<SpriteRenderer>(true))
            sr.sortingOrder = baseOrder - FullBurstSortingBelow;
    }

    // 탄환이 나가는 세로 구간을 **기계의 보이는 구간**과 똑같이 맞춘다.
    // FireVolley는 origin 기준 [-bandDown, bandHeight-bandDown]에 뿌리므로, 아래쪽 기준을 지면에 붙인다.
    private float FullBurstBandDown() => (transform.position.y + 0.2f) - VisibleGroundY();

    // 반환값은 회오리 R0이 "사라질 때 미니를 남기는" 콜백을 배선하는 데 쓴다(그 외 호출부는 무시해도 된다).
    private Whirlwind SpawnWhirlwind(Vector3 position, float damage, float critChance, float scale, bool applySlow, bool applyVulnerable, int maxHitCount = 0, float slowDuration = 3f, float extraLifetime = 0f, bool isMini = false, float tickIntervalMult = 1f, float speedMult = 1f)
    {
        SkillSfx.Play("Whirlwind.cast");
        // 미니 전용 프리팹은 그림이 작은 만큼(48px vs 64px) localScale이 크고 콜라이더가 그만큼 작다 —
        // 월드 기준 화면 크기·판정·바닥선 보정이 전부 본체 축소판과 동일하게 나오도록 맞춰 둔 것이다. 한쪽만 고치지 말 것.
        GameObject prefab = isMini && miniWhirlwindPrefab != null ? miniWhirlwindPrefab : whirlwindPrefab;
        GameObject obj = Instantiate(prefab, position, Quaternion.identity);
        obj.transform.localScale *= scale;
        Whirlwind whirlwind = obj.GetComponent<Whirlwind>();
        whirlwind.Damage = damage;
        whirlwind.ApplyGemSlow = applySlow;
        whirlwind.ApplyGemVulnerable = applyVulnerable;
        whirlwind.CritChance = critChance;
        whirlwind.MaxHitCount = maxHitCount;
        whirlwind.SlowDuration = slowDuration;
        whirlwind.ExtraLifetime = extraLifetime;
        // 소용돌이 수명의 기본값도 에셋이 정할 수 있다(0이면 프리팹 값 그대로). 미니도 같은 축을 쓴다.
        whirlwind.BaseLifetimeOverride = BaseDuration(ActiveSkillId.Whirlwind, 0f);
        whirlwind.TickIntervalMult = tickIntervalMult; // 레벨업 보조축: 피해 주기
        whirlwind.SpeedMultiplier = speedMult;         // 레벨업 "이동속도"(하늘의 울음) — 기본 1이면 프리팹 속도 그대로

        // 미니는 크기가 작아 기본 groundY(피벗=중심)에 놓으면 지면 위로 떠 보인다 — 바닥선을 큰 회오리와 맞춘다.
        // 🔴 다만 바닥선을 정확히 맞추면 이번엔 땅에 파묻힌 것처럼 낮게 보인다(8/24 플레이스루 "스폰 포인트가 지나치게 낮음").
        //    보정을 절반만 먹여 큰 회오리 바닥선과 원래 높이(중심=0)의 중간에 놓는다. 이 값이 높이 손잡이다.
        if (isMini)
        {
            SpriteRenderer sr = obj.GetComponent<SpriteRenderer>();
            if (sr != null) whirlwind.GroundY = -sr.bounds.extents.y * (1f / MiniWhirlwindScale - 1f) * MiniWhirlwindGroundBlend;
        }

        return whirlwind;
    }

    // 대형 오브 스폰 높이 보정. 아래 가장자리를 "진화 직전 오브(미진화 만렙 크기 1.3)"의 아래 가장자리에 맞춘다.
    // 크기 1당 보이는 반높이 = 그림 1.91유닛(64px 중 61px 불투명, PPU32) ÷ 2 × 프리팹 1.5배 ≈ 1.43.
    private const float BigOrbLiftBaseScale = 1.3f;
    private const float BigOrbLiftPerScale = 1.43f;
    private const float HugeOrbExtraLift = 1.8f;   // 초대형 오브(96px)가 대형(64px)보다 커진 몫 — 실측 보정

    private void FireOrb(float damage, float critChance, EquippedSkill skill)
    {
        SkillSfx.Play("Orb.cast");

        // R1(호밍 연계, path2): 큰 오브 하나 대신 **작은 오브 여러 개**가 각자 적을 쫓는다(2026-08-06 명세).
        // 각 오브는 관통 3 — 때리고 나서 다시 다음 적을 찾아간다.
        if (skill.PathTier[2] >= 2)
        {
            SpawnHomingSmallOrbs(damage, critChance, skill);
            return;
        }

        // 지식 연계 path: T2부터 큰 오브, 2차 「초대형 오브」는 전용 그림(Effect_HugeOrb).
        // 미배선이면 한 단계씩 아래 그림으로 떨어진다.
        GameObject prefabToSpawn = skill.PathTier[1] >= 3 && hugeOrbPrefab != null ? hugeOrbPrefab
                                 : skill.PathTier[1] >= 2 && bigOrbPrefab != null ? bigOrbPrefab : orbPrefab;
        Vector3 spawnPos = transform.position + Vector3.up * 0.35f; // 너무 낮게 깔려 나가 보여 위로 올렸다(9/18 사용자)
        // 대형 오브 계통은 크기(진화 +0.3 · 레벨업 크기/범위 칸)가 커질수록 중심 기준으로 **아래로도** 커져 땅에 묻혀 보였다(9/18 사용자).
        // 기준 크기보다 커진 만큼 올려서 아래 가장자리를 고정한다 — 위로만 자라는 것처럼 보인다.
        if (skill.PathTier[1] >= 2)
            spawnPos.y += BigOrbLiftPerScale * Mathf.Max(0f, skill.Scale - BigOrbLiftBaseScale);
        // 초대형 오브 그림은 96px로 대형(64px)보다 1.5배 커서 같은 높이에 두면 아래로 1.8유닛 파묻힌다(실측).
        // 커진 몫만큼 더 올려 아래 가장자리를 대형 오브와 같은 자리에 둔다.
        if (skill.PathTier[1] >= 3) spawnPos.y += HugeOrbExtraLift;
        GameObject obj = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);
        obj.transform.localScale *= skill.Scale;
        Orb orb = obj.GetComponent<Orb>();
        orb.Damage = damage;
        orb.CritChance = critChance;
        orb.ApplyGemVulnerable = false;

        // 오브는 진화로 공중 추가 피해를 얻지 않는다(비행 적도 히트박스가 닿으면 그냥 맞는다).

        // 🔴 스킬트리 「끈적한 오브」(orb_BasicSlow)를 사야 오브가 둔화를 건다(프리팹 0.8배=20% 감속·2초). 기본 오브엔 둔화가 없다
        //    (사용자 결정 — 노션 「스킬트리 재설계」의 "오브: 기본 둔화"). 예전엔 기본 둔화를 **강화**하는 노드였다.
        orb.SlowsEnemies = MetaBonuses.OrbSlowUnlocked;

        // 지식 연계 path: 슬로우 강화 (T1, T3에서 각각) — 위 노드로 둔화가 켜져 있을 때만 체감된다.
        // 감속 폭은 기본 둔화와 함께 절반으로 줄였다(2026-09-14 사용자 결정: 둔화율이 너무 높다). 지속 보너스는 그대로.
        float slowMultBonus = 0f;
        float slowDurBonus = 0f;
        if (skill.PathTier[1] >= 1) { slowMultBonus += 0.05f; slowDurBonus += 0.5f; }
        orb.SlowMultiplierBonus = slowMultBonus;
        orb.SlowDurationBonus = slowDurBonus;
        // 🔴 관통 예산은 사라졌다(2026-09-27 사용자) — 모든 오브가 방패에 막히기 전까지 무한 관통이다.
        //    레벨업 성장축은 쿨타임·피해·크기로 옮겼다(Prog_Orb).
        // 🔴 방패 관통: 대형(1차)·초대형(2차) 진화가 주고, 스킬트리 orb_Pierce를 사면 **기본 오브부터** 뚫는다.
        orb.PiercesShields = skill.PathTier[1] >= 2 || MetaBonuses.OrbPiercesShields;

        // ── R0 2차 「초대형 오브」 ────────────────────────────────────────────
        // 🔴 2026-09-19 사용자 명세로 **슬로우 강화를 걷어내고** 통째로 바꿨다:
        //    "일정 시간마다 주변 적들을 오브 쪽으로 끌어당긴다 · 오브 자체는 엄청 천천히 움직이고 ·
        //     방패병도 관통 · 관통 무한 · 대신 지속시간이 있다"
        //    노션 UI 문구도 같다: "모든 것을 관통하는 초대형 오브를 소환해 주위 적들을 끌어당긴다".
        // 지속시간은 레벨업 "지속시간" 스텝이 늘린다 — `ExtraWhirlwindDuration`이 회오리 전용이 아니라
        // **스킬 공용 지속시간 칸**이다(ApplyStep의 SkillStat.Duration이 산탄만 따로 빼고 전부 여기로 넣는다).
        // 🔴 1차 「강력한 마력」은 오브가 커진 만큼 **느리게** 굴러간다(2026-09-29 사용자).
        //    2차 초대형은 아래에서 이 값을 다시 덮어써 더 느려진다.
        if (skill.PathTier[1] >= 2) orb.SpeedMultiplier = BigOrbSpeedMult;

        if (skill.PathTier[1] >= 3)
        {
            orb.SpeedMultiplier = HugeOrbSpeedMult;
            orb.LifetimeOverride = HugeOrbBaseLifetime + skill.ExtraWhirlwindDuration;
            // 레벨업 "빨아들이기 주기"(TickRate) — 작을수록 자주 끌어당긴다.
            orb.PullInterval = Mathf.Max(0.2f, HugeOrbPullInterval * skill.TickIntervalMult);
            orb.PullRadius = HugeOrbPullRadius;
            orb.PullDistance = HugeOrbPullDistance;
        }
    }

    // ── 초대형 오브(오브 R0 2차) 손잡이 ─────────────────────────────────────
    // 1차 「강력한 마력」의 이동속도(2026-09-29 사용자: 기본 오브보다 느리게). 초대형은 여기서 더 내려간다.
    private const float BigOrbSpeedMult = 0.6f;
    private const float HugeOrbSpeedMult = 0.25f;      // "엄청 천천히" — 기본 이동속도의 1/4
    private const float HugeOrbBaseLifetime = 6f;      // 레벨업 "지속시간"이 여기에 더해진다
    private const float HugeOrbPullInterval = 1.2f;    // 끌어당기기 주기(초)
    private const float HugeOrbPullRadius = 4.5f;      // 끌어당기는 반경(유닛)
    private const float HugeOrbPullDistance = 1.6f;    // 한 번에 끌려오는 거리(유닛) — 넉백과 같은 이징·저항을 탄다

    // ── 오브 R1(호밍 연계, path2): 작은 추적 오브 무리 ──
    // 산탄 알 프리팹(SmallOrb)을 재사용하되 추적·관통을 켠다. 알 하나당 관통 3 = 최대 4마리를 때린다.
    private const int HomingOrbPierce = 3;
    private const int JugglerPierce = 8;   // 2차 「저글러」: 알 하나당 최대 9마리(2026-10-07 사용자 — 종전 무한)
    // 🔴 추적 오브는 큰 오브를 대체하므로 작은 오브 한 개가 곧 본체다 — 비율 1, 저글러 배율 1.5도 제거(2026-09-28).
    private const float HomingOrbDamageRatio = 1f;
    // 🔴 추적 오브는 **기본 오브의 1/4 크기로 고정**한다(사용자 지시 2026-09-27).
    //    기본 오브 = 스프라이트 2유닛 × 프리팹 1.5배 = 3유닛 → 1/4 = 0.75유닛.
    //    ⚠️ 스케일 **배율**이 아니라 렌더 **크기**로 맞춘다 — 저글러는 전용 그림이라 원본 크기가 다르다.
    //    ⚠️ 레벨업 크기 축(skill.Scale)을 물려받지 않는다. 성장해도 알은 그대로 작다.
    private const float HomingOrbWorldSize = 0.75f;
    // 기본 개수(사용자 결정 2026-09-18: 6 → 10). 일반 오브는 관통 무한이라 "개수" 축은 추적 오브에만 있다.
    // 레벨업의 "타겟 수"는 이 위에 더해진다.
    private const int HomingOrbBaseCount = 10;
    // 산탄 알 프리팹의 수명(2초 × 속도 5 = 10유닛)으로는 화면 왼쪽에서 오는 적까지 가지도 못하고 사라졌다.
    // 호밍 미사일의 사거리(4초 × 속도 9 ≈ 36유닛)에 맞춘다 — 속도 5로 7초.
    private const float HomingOrbLifetime = 7f;

    // 🔴 2026-09-29 사용자 지시로 뒤집었다 — 유도 오브 개수가 레벨업 "동시 대상"(ExtraTargets)을 **물려받는다**.
    //    추적 오브·저글러는 크기 축을 안 쓰므로(알 크기가 HomingOrbWorldSize로 고정) 그 자리를 개수가 대신한다.
    //    종전 결정(2026-09-20 "물려받지 않는다")은 크기 카드가 살아 있다는 전제였다.
    private static int HomingOrbCount(EquippedSkill skill)
    {
        int count = HomingOrbBaseCount + skill.ExtraTargets;
        if (skill.PathTier[2] >= 3) count = Mathf.RoundToInt(count * 1.5f);
        return count;
    }

    // 🔴 오브를 한 프레임에 통째로 내보내지 않고 **간격을 두고 두다다다** 쏜다(2026-09-20 사용자).
    //    한 방에 나가면 개수가 늘어도 한 덩어리로 보인다 — 추적 오브·저글러 둘 다 해당.
    //
    // 값 근거(실측): 알 0.9유닛 ÷ 속도 5 = **0.18초가 스프라이트가 겹치기 시작하는 한계**.
    //   0.25초면 간격 1.25유닛(알 사이 0.35 여백)이라 하나씩 또렷이 끊겨 보인다.
    // ⚠️ 0.5초는 쓰면 안 된다 — 저글러 15발이 7초가 되어 **오브 쿨(7초)을 통째로 먹고** 다음 시전과 겹친다.
    private const float HomingOrbFireInterval = 0.25f;
    // 레벨업으로 알이 늘어도 총 발사시간이 쿨의 이 비율을 넘지 않게 간격을 줄인다(겹침 방지).
    private const float HomingOrbBurstMaxCooldownRatio = 0.55f;

    private void SpawnHomingSmallOrbs(float damage, float critChance, EquippedSkill skill)
        => StartCoroutine(SpawnHomingSmallOrbsRoutine(damage, critChance, skill));

    private IEnumerator SpawnHomingSmallOrbsRoutine(float damage, float critChance, EquippedSkill skill)
    {
        if (shotgunPelletPrefab == null) yield break; // 전용 그림이 나오면 여기만 교체하면 된다

        // 2차 「저글러」 = 부메랑. 무작위 적을 때린 뒤 근처의 적을 옮겨 다니다 **캐릭터에게 돌아온다**(2026-09-19 사용자 명세).
        // 🔴 2026-10-07 사용자: 관통 무한·첫 명중 즉시 복귀 → 관통 8(최대 9마리), 다 쓰거나 근처에 적이 없으면 복귀(SmallOrb).
        bool juggler = skill.PathTier[2] >= 3;

        int count = HomingOrbCount(skill);

        float orbDamage = damage * HomingOrbDamageRatio;
        Vector3 origin = transform.position + Vector3.down * 0.1f;

        // 알이 많아질수록 간격을 줄여 총 발사시간이 쿨을 넘지 않게 한다.
        float fireInterval = count > 1
            ? Mathf.Min(HomingOrbFireInterval, skill.Cooldown * HomingOrbBurstMaxCooldownRatio / (count - 1))
            : HomingOrbFireInterval;

        for (int i = 0; i < count; i++)
        {
            // 처음엔 부채꼴로 흩어져 나갔다가 각자 가까운 적을 찾아 휜다.
            float spread = count > 1 ? Mathf.Lerp(-55f, 55f, i / (float)(count - 1)) : 0f;
            Vector2 dir = Quaternion.Euler(0f, 0f, spread) * Vector2.left;

            // 2차 「저글러」는 전용 그림(Effect_Juggler). 미배선이면 1차와 같은 산탄 알 그림.
            GameObject orbPrefabToUse = juggler && jugglerOrbPrefab != null ? jugglerOrbPrefab : shotgunPelletPrefab;
            GameObject obj = Instantiate(orbPrefabToUse, origin, Quaternion.identity);
            // 크기는 **대입**한다(곱이 아니다) — 프리팹 스케일도 레벨업 크기도 안 물려받는다.
            SpriteRenderer ballSr = obj.GetComponentInChildren<SpriteRenderer>();
            float ballSpriteWidth = ballSr != null && ballSr.sprite != null ? ballSr.sprite.bounds.size.x : 1f;
            obj.transform.localScale = Vector3.one * (HomingOrbWorldSize / Mathf.Max(0.01f, ballSpriteWidth));
            SmallOrb orb = obj.GetComponent<SmallOrb>();
            if (orb == null) continue;
            orb.CritChance = critChance;
            orb.Homing = true;
            orb.TargetRank = i;                // 오브마다 다른 적을 노린다
            orb.Lifetime = HomingOrbLifetime;  // Start 전이라 먹는다
            orb.PierceRemaining = juggler ? JugglerPierce : HomingOrbPierce;
            orb.Source = ActiveSkillId.Orb;
            if (juggler)
            {
                orb.RandomTarget = true;       // 가까운 순이면 오브가 많아 앞줄에 전부 몰린다
                orb.ReturnTo = transform;      // 첫 명중 뒤 캐릭터에게 돌아온다
            }
            orb.Init(dir, orbDamage, applyVulnerable: juggler);

            // 마지막 발 뒤에는 기다리지 않는다(쿨과 겹쳐 늘어져 보인다).
            if (i < count - 1) yield return new WaitForSeconds(fireInterval);
        }
    }

    // ── 독수리의 비(R1 2차) ────────────────────────────────────────────────
    // 🔴 **"화면의 적 전원에게 동시에 N번"이 아니다**(사용자 지시 2026-09-08). 일정 시간 동안
    //    한 마리씩 **넓게 흩어져 두두두둑** 떨어진다 — 그래야 "비가 내린다"로 보인다.
    //    그래서 대상이 적이 아니라 **자리**다: 적 근처를 중심으로 좌우로 흩뿌리고, 떨어진 자리 반경만 때린다.
    private const float EagleRainDuration = 4f;
    private const float EagleRainInterval = 0.1f;     // 40마리
    // 🔴 독수리의 비는 평소 투하를 **통째로 대체**하므로(아래 yield break) 떨어지는 독수리 한 마리가 곧 본체다.
    //    그래서 비율을 1로 두고 피해는 `Evo_EagleDrop_R1_T2.baseDamage`가 단독으로 정한다(사용자 결정 2026-09-28).
    private const float EagleRainDamageRatio = 1f;
    // 🔴 **반경과 흩뿌림 폭은 짝이다 — 한쪽만 고치면 스킬이 조용히 약해진다.**
    //    흩뿌림 ±3.5에 반경 1.1이던 첫 판은 한 마리가 제 목표를 맞출 확률이 |dx|<1.1 / ±3.5 ≈ 0.3뿐이라
    //    40마리 중 12마리만 유효했다(= 2차가 1차보다 안 세지는 값). 반경을 키우고 폭을 좁혀 ≈0.65로 올린다.
    //    폭을 더 좁히면 "넓게 두두두둑"이라는 요구가 깨지므로, 손댈 땐 두 값을 같이 볼 것.
    private const float EagleRainRadius = 2f;         // 한 마리가 훑는 반경
    private const float EagleRainSpreadX = 3f;
    private const float EagleRainSpreadY = 1.2f;

    // 폭탄 독수리(R0) — 호밍 R0과 **같은 폭발 에셋**을 쓴다(사용자 지시). 반경 밖은 안 맞는다.
    private const float EagleBombVfxScale = 1.4f;

    // ── 독수리 R0 2차 「슈퍼 다이너마이트 독수리」 ──────────────────────────
    // 🔴 2026-09-19 사용자 명세: "시전하면 **오른쪽 위에서 슈퍼 독수리가 날아와서 중앙 바닥을 타격**한다.
    //    그럼 화면 **모든 적**에게 엄청난 데미지를 **엄청난 타수**로 입히고 **핵 이펙트**를 적용."
    //    노션 문구도 같다: "거대한 다이너마이트 독수리 한 마리가 떨어진다".
    // ⚠️ 쿨타임 20초는 **에셋**이 정한다 — `Assets/Data/Evolutions/Evo_EagleDrop_R0_T2.baseCooldown`.
    [SerializeField] private GameObject superEaglePrefab;  // SuperEagle/Effect_SuperEagle 플립북
    [SerializeField] private GameObject nuclearVfxPrefab;  // SuperEagle/Nuclear 플립북(핵 폭발)

    private const float SuperEagleDiveDuration = 0.55f;  // 오른쪽 위 → 중앙 바닥까지 걸리는 시간
    private const float SuperEagleScale = 3.5f;          // 화면을 채우는 "거대한" 크기
    private const float SuperEagleOffscreenMargin = 2f;
    // 🔴 12 → 3 (사용자 결정 2026-09-29). "엄청난 타수"는 코드 루프가 아니라 **타수**로 낸다 —
    //    `Evo_EagleDrop_R0_T2.baseHits = 6`이 한 번의 타격을 6개 숫자로 쪼갠다. 적당 숫자 3 × 6 = 18개다.
    //    ⚠️ 루프 횟수는 총 피해에 곱해지고 타수는 나누므로, 12 → 3으로 줄인 만큼 baseDamage를 4배로 올려 두었다.
    private const int SuperEagleHits = 3;
    private const float SuperEagleHitInterval = 0.08f;
    // 🔴 다이너마이트 독수리는 평소 투하를 대체하므로 그 타격이 곧 본체다 — 비율 1(2026-09-28).
    private const float SuperEagleDamageRatio = 1f;
    // 🔴 핵 폭발은 고정 배율이 아니라 **화면에 맞춘다**(2026-10-06 사용자 "폭발 이펙트가 화면을 벗어났잖아").
    //    예전엔 프리팹(1.5배)에 4배를 더 곱해 108×61유닛 — 우주 맵 화면(31×17.5)의 3.5배였고,
    //    캔버스 한가운데를 지면에 놓아 밑동이 땅속에 있었다. 맵마다 카메라 크기가 달라(fieldScale) 상수로는 못 맞춘다.
    //    아래 셋은 `Nuclear_1~14.png`(576×324)를 알파로 잰 값이다 — 그림을 다시 그리면 다시 재야 한다.
    private const float NuclearArtWidthPx = 496f;    // 14프레임 불투명 영역 합집합의 폭(x 49~545)
    private const float NuclearArtGroundPx = 75f;    // 캔버스 아래에서 지면선(버섯구름 밑동)까지
    private const float NuclearArtAbovePx = 230f;    // 지면선에서 불투명 영역 위끝(305)까지
    private const float NuclearScreenMargin = 0.3f;  // 화면 가장자리에 남기는 여유(유닛)
    private const float NuclearVfxLifetime = 2.5f;
    private const float SuperEagleShakeDuration = 0.5f;

    private IEnumerator SuperDynamiteEagleRoutine(float damage, float critChance, EquippedSkill skill)
    {
        Camera cam = Camera.main;
        Vector3 camPos = cam != null ? cam.transform.position : transform.position;
        float halfH = cam != null ? cam.orthographicSize : 5f;
        float halfW = cam != null ? halfH * cam.aspect : 9f;

        // 바닥은 피뢰침과 **같은 기준**을 쓴다(적 레인 발밑) — 맵마다 cameraYLift가 달라 카메라 y로 잡으면 떠 보인다.
        Vector3 landPos = new Vector3(camPos.x, VisibleGroundY(), 0f);   // 보이는 지면 기준(발밑 투명 여백 보정)
        Vector3 start = new Vector3(camPos.x + halfW + SuperEagleOffscreenMargin,
                                    camPos.y + halfH + SuperEagleOffscreenMargin, 0f);

        GameObject eagle = null;
        if (superEaglePrefab != null)
        {
            eagle = Instantiate(superEaglePrefab, start, Quaternion.identity);
            // 🔴 skill.Scale을 곱하지 않는다(2026-10-07 사용자 "독수리는 아직 크다") — 1차 폭탄 독수리의 "폭발 범위" 카드가
            //    그대로 넘어와 독수리가 최대 1.9배까지 커졌다. 폭발 그림은 10/6에 같은 이유로 이미 뺐다.
            eagle.transform.localScale *= SuperEagleScale;
        }

        float t = 0f;
        while (t < SuperEagleDiveDuration)
        {
            if (eagle != null) eagle.transform.position = Vector3.Lerp(start, landPos, t / SuperEagleDiveDuration);
            t += Time.deltaTime;
            yield return null;
        }
        if (eagle != null) Destroy(eagle);

        if (nuclearVfxPrefab != null)
        {
            GameObject nuke = Instantiate(nuclearVfxPrefab, landPos, Quaternion.identity);
            FitNuclearVfxToScreen(nuke, camPos, halfW, halfH, landPos.y);
            Destroy(nuke, NuclearVfxLifetime);
        }
        ScreenShake.Shake(ScreenShake.SwingStrength * 2f, SuperEagleShakeDuration);

        // 화면의 **모든** 적에게 여러 번. 매 타격마다 목록을 다시 뜬다 — 도중에 죽거나 새로 나온 적을 반영한다.
        float hitDamage = damage * SuperEagleDamageRatio;
        for (int h = 0; h < SuperEagleHits; h++)
        {
            superEagleTargets.Clear();
            IReadOnlyList<Enemy> active = Enemy.Active;
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null && active[i].IsAlive) superEagleTargets.Add(active[i]);

            for (int i = 0; i < superEagleTargets.Count; i++)
            {
                Enemy e = superEagleTargets[i];
                if (e != null && e.IsAlive) e.TakeSkillHit(hitDamage, critChance, ActiveSkillId.EagleDrop);
            }
            yield return new WaitForSeconds(SuperEagleHitInterval);
        }
    }

    // 버섯구름 전체가 화면 안에 들어오는 가장 큰 크기로 맞추고, 그림 안의 지면선을 groundY에 놓는다.
    // 스킬 크기 배율(skill.Scale)은 곱하지 않는다 — 화면이 기준이라 곱하면 다시 벗어난다(판정은 원래 화면 전체다).
    private void FitNuclearVfxToScreen(GameObject nuke, Vector3 camPos, float halfW, float halfH, float groundY)
    {
        SpriteRenderer sr = nuke.GetComponentInChildren<SpriteRenderer>(true);
        if (sr == null || sr.sprite == null) return;
        float ppu = sr.sprite.pixelsPerUnit;

        float fitWidth = (halfW * 2f - NuclearScreenMargin * 2f) / (NuclearArtWidthPx / ppu);
        float fitHeight = (camPos.y + halfH - NuclearScreenMargin - groundY) / (NuclearArtAbovePx / ppu);
        float scale = Mathf.Min(fitWidth, fitHeight);

        nuke.transform.localScale = Vector3.one * scale;
        float groundBelowPivot = (sr.sprite.pivot.y - NuclearArtGroundPx) / ppu * scale;
        nuke.transform.position = new Vector3(camPos.x, groundY + groundBelowPivot, 0f);
    }

    private readonly List<Enemy> superEagleTargets = new List<Enemy>(64);

    private IEnumerator EagleDropRoutine(float damage, float critChance, EquippedSkill skill)
    {
        SkillSfx.Play("EagleDrop.cast");

        // R0(산탄 연계, path1) = **폭탄 독수리**. 떨어진 자리에 폭발이 남는다(2026-09-08 명세).
        // ⚠️ 예전엔 이 루트가 "독수리 비"였다 — **그 기믹은 R1 2차로 옮겨갔다.** 문구와 코드를 같이 옮긴 것이라
        //    한쪽만 되돌리면 이름과 효과가 어긋난다.
        // ⚠️ 폭발은 **투하 1회당 적 수만큼** 터진다 — 적이 뭉쳐 있으면 서로의 폭발에 겹쳐 맞아 피해가 곱으로 불어난다.
        //    그래서 비율은 호밍 미사일 폭발과 같은 값(1차 0.4 · 2차 0.6)으로 맞춰 둔다. 올릴 땐 뭉친 판을 보고 정할 것.
        // R0 2차 「슈퍼 다이너마이트 독수리」 — 평소 투하를 **통째로 대체**한다(2026-09-19 사용자 명세).
        if (skill.PathTier[1] >= 3)
        {
            yield return StartCoroutine(SuperDynamiteEagleRoutine(damage, critChance, skill));
            yield break;
        }

        bool bombEagle = skill.PathTier[1] >= 2;
        const float bombRatio = 0.8f;   // 폭탄 독수리의 착탄 폭발 — 독수리 피해의 80%(사용자 결정 2026-09-28)
        // 레벨업 "크기" 스텝이 폭발 범위를 키운다(2026-09-18).
        float bombRadius = 1.8f * skill.Scale;

        // R1(회오리 연계, path2) 1차 = 낙하 자리에 미니 회오리 / 2차 = **독수리의 비**
        bool spawnMiniWhirlwind = skill.PathTier[2] >= 2;
        // 레벨업 "미니 회오리 수"(MaxTargets) — 착탄 한 번에 몇 개가 생기는지(2026-09-29 사용자).
        int miniWhirlwindCount = Mathf.Max(1, 1 + skill.ExtraTargets);
        // 🔴 독수리 피해의 30% — 차수별로 0.25·0.35였던 것을 회오리 쪽과 같은 상수로 맞췄다(사용자 결정 2026-09-28).
        float miniWhirlwindDamageMult = MiniWhirlwindDamageRatio;
        int miniWhirlwindMaxHits = skill.PathTier[2] >= 3 ? 8 : 5;
        if (skill.PathTier[2] >= 3)
        {
            // 🔴 비가 내리는 중에 다시 쓰면 앞 비를 끊고 새로 내린다(2026-10-06 사용자 — 종전엔 둘이 겹쳐 내렸다).
            //    이미 떨어지기 시작한 독수리는 그대로 떨어진다.
            if (eagleRainRoutine != null) StopCoroutine(eagleRainRoutine);
            eagleRainRoutine = StartCoroutine(EagleRainRoutine(damage, critChance, skill, miniWhirlwindDamageMult, miniWhirlwindMaxHits));
            yield break;
        }

        // 레벨업 보조축: 투하 횟수(ExtraProjectiles). 스킬트리 "독수리 투하수 +1"도 여기 더해진다.
        int dropCount = Mathf.Max(1, EagleBaseDrops + skill.ExtraProjectiles + MetaBonuses.EagleExtraDrops);
        // 레벨업 주 성장축: 투하 간격(TickIntervalMult)
        float interval = skill.TickIntervalMult;

        int epoch = eagleDropEpoch;   // 스테이지가 넘어가면 값이 바뀐다 — 남은 투하를 버린다
        for (int i = 0; i < dropCount && epoch == eagleDropEpoch; i++)
        {
            List<Enemy> enemies = new List<Enemy>(Enemy.Active); // 복사본 — 아래에서 피해를 주면 활성 목록이 바뀐다

            // 🔴 피해는 **독수리가 닿는 순간** 들어간다(2026-09-21 사용자: 예전엔 시전 즉시 맞고 그림만 늦게 떨어졌다).
            //    독수리는 떨어지는 내내 그 적을 따라간다 — 떨어지는 동안 적이 걸어가서 뒤에 꽂히던 것을 없앤다.
            foreach (Enemy enemy in enemies)
            {
                if (enemy == null || !enemy.IsAlive) continue;
                Enemy target = enemy;
                // 🔴 폭탄 독수리의 크기 축은 **폭발 범위**다 — 독수리 그림·착탄 이펙트에는 곱하지 않는다(2026-10-07 사용자:
                //    "폭발 반경을 넓히라 했는데 왜 독수리 크기가 커졌냐"). 폭발 반경과 폭발 그림만 skill.Scale을 따른다.
                float eagleScale = bombEagle ? 1f : skill.Scale;
                StartCoroutine(MeteorImpact(target.transform.position, eagleScale, target, (pos, stillOnTarget) =>
                {
                    if (stillOnTarget) target.TakeSkillHit(damage, critChance, ActiveSkillId.EagleDrop);
                    if (bombEagle) EagleBombExplode(pos, damage * bombRatio, critChance, bombRadius, target, skill.Scale);
                    if (spawnMiniWhirlwind)
                        for (int w = 0; w < miniWhirlwindCount; w++)
                        {
                            // 여러 개면 서로 겹쳐 한 덩어리로 보이므로 착탄점 주위로 조금 흩는다.
                            Vector3 at = miniWhirlwindCount > 1 ? pos + (Vector3)(Random.insideUnitCircle * 0.6f) : pos;
                            SpawnWhirlwind(at, damage * miniWhirlwindDamageMult, critChance, skill.Scale * MiniWhirlwindScale, false, false, maxHitCount: miniWhirlwindMaxHits, isMini: true);
                        }
                }));
            }

            yield return new WaitForSeconds(interval);
        }
    }

    // 떨어진 자리에서 터진다. 직격을 이미 맞은 적(origin)은 제외 — 호밍 미사일 폭발과 같은 규칙이다.
    // vfxMult: 레벨업 "폭발 범위" 몫 — 판정 반경과 같은 비율로 그림도 키운다(절대값 대입이라 풀 재사용에도 안 쌓인다).
    private void EagleBombExplode(Vector3 pos, float damage, float critChance, float radius, Enemy origin, float vfxMult = 1f)
    {
        GameObject vfx = eagleBombVfxPrefab != null ? eagleBombVfxPrefab : eagleImpactVfxPrefab;
        if (vfx != null)
        {
            GameObject go = ObjectPool.Instance.Spawn(vfx, pos, Quaternion.identity);
            go.transform.localScale = Vector3.one * EagleBombVfxScale * vfxMult;
            // Effect_Explosion은 4프레임 16fps(0.25초)에 despawnOnFinish가 꺼져 있다 — 재생 길이 바로 뒤에 회수한다.
            ObjectPool.Instance.Despawn(go, 0.3f);
        }
        using (Enemy.GetSnapshot(out List<Enemy> enemies))
            foreach (Enemy o in enemies)
                if (o != null && o != origin && o.IsAlive && Vector2.Distance(pos, o.transform.position) <= radius)
                    o.TakeSkillHit(damage, critChance, ActiveSkillId.EagleDrop);
    }

    private IEnumerator EagleRainRoutine(float damage, float critChance, EquippedSkill skill, float miniMult, int miniHits)
    {
        float dropDamage = damage * EagleRainDamageRatio;
        // 레벨업 "지속시간"(InstallDuration) — 비가 내리는 시간이 길어지면 떨어지는 독수리 수도 같이 는다.
        float rainDuration = EagleRainDuration + skill.ExtraInstallDuration;
        for (float elapsed = 0f; elapsed < rainDuration; elapsed += EagleRainInterval)
        {
            StartCoroutine(EagleRainStrike(PickEagleRainSpot(), dropDamage, critChance, skill, miniMult, miniHits));
            yield return new WaitForSeconds(EagleRainInterval);
        }
        eagleRainRoutine = null;
    }

    private Coroutine eagleRainRoutine;
    private int eagleDropEpoch;   // 연속 투하(EagleDropRoutine)를 끊는 표시 — ClearInstallations가 올린다

    // 한 마리가 떨어져 그 자리 반경을 때린다. 낙하 연출이 끝난 **뒤에** 판정한다(그림보다 먼저 죽으면 안 보인다).
    private IEnumerator EagleRainStrike(Vector3 pos, float damage, float critChance, EquippedSkill skill, float miniMult, int miniHits)
    {
        yield return StartCoroutine(MeteorImpact(pos, skill.Scale));

        using (Enemy.GetSnapshot(out List<Enemy> enemies))
            foreach (Enemy e in enemies)
                if (e != null && e.IsAlive && Vector2.Distance(pos, e.transform.position) <= EagleRainRadius * skill.Scale) // 레벨업 "낙하 범위"
                    e.TakeSkillHit(damage, critChance, ActiveSkillId.EagleDrop);

        // 1차(회오리 폭격)를 이어받는다 — 비가 오는 내내 자리마다 미니 회오리가 남는다.
        SpawnWhirlwind(pos, damage * miniMult, critChance,
            skill.Scale * MiniWhirlwindScale, false, false, maxHitCount: miniHits, isMini: true);
    }

    // 떨어질 자리 — 산 적 하나를 골라 그 주위로 흩뿌린다. 적이 없으면 플레이어 왼쪽(적이 오는 쪽)에 떨군다.
    private Vector3 PickEagleRainSpot()
    {
        List<Enemy> alive = new List<Enemy>();
        foreach (Enemy e in Enemy.Active)
            if (e != null && e.IsAlive) alive.Add(e);

        Vector3 center = alive.Count > 0
            ? alive[Random.Range(0, alive.Count)].transform.position
            : transform.position + Vector3.left * 4f;
        return center + new Vector3(Random.Range(-EagleRainSpreadX, EagleRainSpreadX),
                                    Random.Range(-EagleRainSpreadY, EagleRainSpreadY), 0f);
    }

    // 🧱 임시 프리미티브 — 전용 도트가 나오면 이 함수의 스프라이트만 교체하면 된다.
    //    휘두르기 범위 표시(SpawnSwingRange)와 같은 방식으로 흰 사각형 하나를 만들어 색만 입힌다.
    // ⚠️ 플레이어의 localScale이 1.5라 자식으로 붙이면 크기가 곱해진다 — 월드에 독립으로 둔다.
    // ── 낙뢰 R0 2차 「초대형 축적 번개」 손잡이 (2026-09-29) ──
    private const int HugeBoltBaseMaxStacks = 20;   // 발동 임계 15보다 높아야 임계를 넘긴 뒤에도 스택이 는다
    private const float HugeBoltBaseRadius = 4f;    // 레벨업 "크기"가 여기 곱해진다

    private const float LightningRodWidth = 0.35f;
    private const float LightningRodHeight = 3.2f;
    private static readonly Color LightningRodColor = new Color(0.62f, 0.66f, 0.72f, 1f); // 쇠기둥 회색
    private static readonly Color LightningRodTipColor = new Color(1f, 0.95f, 0.45f, 1f); // 끝에 노란 촉
    private static Sprite lightningRodSprite;

    // 2차는 제우스상이 따로 그려져 있어 **키우지 않는다**. 제우스상이 없을 때만 예전처럼 1차 기둥을 1.25배로.
    private GameObject RodPrefab(bool empowered) =>
        empowered && zeusStatuePrefab != null ? zeusStatuePrefab : lightningRodPrefab;

    private float RodScaleMult(bool empowered) =>
        empowered && zeusStatuePrefab == null ? 1.25f : 1f;

    // 기둥 그림의 월드 높이 — 그림을 다시 그려도 상수를 안 고치게 스프라이트에서 잰다(프리팹 1.5배 포함).
    private float RodHeight(bool empowered)
    {
        GameObject prefab = RodPrefab(empowered);
        SpriteRenderer sr = prefab != null ? prefab.GetComponent<SpriteRenderer>() : null;
        if (sr == null || sr.sprite == null) return LightningRodHeight * RodScaleMult(empowered);
        return sr.sprite.bounds.size.y * prefab.transform.localScale.y * RodScaleMult(empowered);
    }

    private GameObject SpawnLightningRod(Vector3 rodCenter, bool empowered)
    {
        // 전용 도트가 배선돼 있으면 그쪽. 프리팹이 이미 1.5배(다른 이펙트와 같은 픽셀 배율)라 여기서 더 곱하지 않는다.
        GameObject prefab = RodPrefab(empowered);
        if (prefab != null)
        {
            GameObject go = Instantiate(prefab, rodCenter, Quaternion.identity);
            go.transform.localScale *= RodScaleMult(empowered);
            return go;
        }

        if (lightningRodSprite == null)
        {
            Texture2D tex = Texture2D.whiteTexture;
            lightningRodSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        }

        float height = LightningRodHeight * (empowered ? 1.25f : 1f);

        GameObject rod = new GameObject("LightningRod(temp)", typeof(SpriteRenderer));
        SpriteRenderer body = rod.GetComponent<SpriteRenderer>();
        body.sprite = lightningRodSprite;
        body.color = LightningRodColor;
        body.sortingOrder = 50; // 배경(-100)보다 앞, 적(100+)보다 뒤
        rod.transform.position = rodCenter;
        rod.transform.localScale = new Vector3(LightningRodWidth, height, 1f);

        // 꼭대기의 촉 — 기둥 하나만 있으면 그냥 막대라, 번개를 받는 곳이 어딘지 보이게 한다.
        GameObject tip = new GameObject("Tip", typeof(SpriteRenderer));
        SpriteRenderer tipSr = tip.GetComponent<SpriteRenderer>();
        tipSr.sprite = lightningRodSprite;
        tipSr.color = LightningRodTipColor;
        tipSr.sortingOrder = 51;
        tip.transform.SetParent(rod.transform, worldPositionStays: false);
        // 부모의 비균등 스케일을 되돌려 정사각형으로 보이게 한다(부모 x 0.35 · y 3.2가 그대로 곱해지므로).
        tip.transform.localScale = new Vector3(1.6f, LightningRodWidth * 1.6f / height, 1f);
        tip.transform.localPosition = new Vector3(0f, 0.5f, 0f); // 부모 높이의 절반 = 꼭대기

        return rod;
    }

    // ── 낙뢰 R1(힘 연계, path1): 맵 중앙에 꽂는 피뢰침 ──
    // LightningRodDuration동안 LightningRodInterval마다(2차 제우스는 1초) 아주 넓은 범위를 내리쳐 큰 피해와 짧은 기절을 준다.
    // 🔴 이 루트를 고르면 **기본 낙뢰 버프는 사라진다**(FireSkill의 Lightning 분기) — 대체이고 덧붙임이 아니다.
    // ⚠️ 기둥은 **임시 프리미티브**다 — 코드로 만든 회색 사각형(SwingRange와 같은 방식, 별도 에셋 없음).
    //    전용 도트가 나오면 SpawnLightningRod의 스프라이트만 갈아끼우면 된다.
    // ⚠️ rollLightning:false — 이 타격이 다시 낙뢰 발동을 굴리면 피뢰침이 자기 자신을 증폭한다.
    // 🔴 지속시간 = 쿨타임 − 1.5초(2026-09-27 사용자). 쿨 12초 기준 10.5초 — 간격 1초 그대로라 타격이 6타 → 11타가 되어
    //    총 피해가 1.8배가 된다. **그 강화를 감수한 결정**이다(아래 낙뢰 버프 대체로 이 루트가 약해지기 때문).
    //    쿨이 지속보다 짧아져도 기둥은 겹치지 않는다 — 새로 꽂으면 이전 기둥이 철거된다(RemoveLightningRod).
    private const float LightningRodDuration = 10.5f;
    // 1 → 1.4(2026-10-01 사용자 "낙뢰 떨어지는 주기를 40% 늘려서 너프"). 기본 지속 10.5초에 11타 → 8타.
    // 1차 피뢰침만이다 — 2차 제우스상은 석상 애니메이션 한 바퀴(1초)에 타격을 맞춰 둬서(ZeusChargeDelay) 1초를 유지한다.
    private const float LightningRodInterval = 1.4f;
    private const float ZeusStrikeInterval = 1f;
    private const float LightningRodRadius = 9.1f;    // "아주 넓은 범위" — 2026-09-27 사용자 +30%(7 → 9.1)
    private const float LightningRodStun = 0.5f;
    // 🔴 피뢰침은 기본 낙뢰 버프를 대체하므로 내리치는 번개가 곧 본체다 — 비율 1(2026-09-28).
    private const float LightningRodDamageRatio = 1f;
    private const float LightningRodEmpoweredRatio = 1f;       // 2차도 같은 이유로 1
    private const float LightningRodEmpoweredRadiusMult = 1.3f;
    private const float BigThunderHalfHeight = 2.81f; // Effect_BigThunder 120px ÷ PPU32 × scale1.5 ÷ 2

    // 🔴 낙뢰는 **기둥 위가 아니라 기둥 둘레**에 떨어진다(8/25 빌드 검수). 기둥은 번개를 부르는 표지일 뿐이고,
    //    피해 범위가 반경 7유닛이라 한 줄기만 꽂히면 "넓게 때린다"는 게 화면에 안 보였다.
    // 🔴 2026-09-30 사용자: 피해가 반경 전원에서 줄기 단위로 바뀌면서 줄기 수가 곧 화력이 됐다 — 3 → 6.
    private const int LightningRodBoltsPerStrike = 6;
    private const float LightningRodBoltSpreadRatio = 0.7f; // 반경의 몇 %까지 흩뿌리나
    // 줄기 하나가 피해를 주는 가로 반경 = 피뢰침 반경 × 이 값(기본 9.1 × 0.15 ≈ 1.4). 세로로 내리꽂는 줄기라 가로 거리만 본다.
    // 0.25 → 0.15(사용자 승인 2026-10-01 — 3회차 판정: 적이 몰린 층에서 줄기가 겹쳐 맞아 1차 구간 지분 0.7~0.8. 폭만 줄여 밀집 층의 겹침만 덜어낸다)
    private const float LightningRodBoltRadiusRatio = 0.15f;
    private const float LightningRodBoltScale = 1.5f;       // 프리팹(1.5배) 위에 더 키운다 — 화면을 채우는 크기
    // 번개 아래끝을 발밑 바닥선보다 이만큼 **아래로** 내린다(2026-10-01 사용자 "번개 위치를 아래로 좀"). 기둥은 그대로 발밑에 선다.
    // 타격 프레임(Effect_BigThunder3~5)은 아래 투명 여백이 1~7px뿐이라 0이면 보이는 끝이 발밑과 같다. 판정은 가로만 봐서 피해와 무관하다.
    private const float LightningRodBoltDrop = 0.5f;
    private const float ZeusChargeDelay = 0.5f;             // Effect_ZeusStatue 6프레임 × fps6 = 1초. 내리치는 4프레임째가 0.5초
    private const float ZeusPullDistance = 1.2f;            // 낙뢰 한 틱마다 기둥 쪽으로 끌려오는 거리(유닛)
    // 석상을 조금 더 위로(2026-09-20 사용자). 발밑 투명 여백 0.70은 이제 VisibleGroundY가 처리하므로
    // 여기 남는 건 **그 위에 얹는 몫**만이다 — 예전 0.8은 두 번 더해져 과했다.
    private const float ZeusStatueLift = 0.1f;
    // 1차 피뢰침(테슬라 코일)은 반대로 조금 **내린다**(2026-09-27 사용자 "설치기 높이 내려줘").
    private const float LightningRodLift = -0.35f;

    // 🔴 **캐릭터 그림에는 발밑 투명 여백이 있다** — 파인애플 `Pinapple1`은 아래 15px(= 0.70유닛, PPU32·scale1.5).
    //    그래서 `bounds.min.y`는 **보이는 발바닥보다 0.70 아래**다. 여기에 바닥을 맞추면 그만큼 파묻혀 보인다
    //    (2026-09-20 사용자 "바닥 부분이랑 캐릭터 밑면이랑 같아지게").
    //    ⚠️ 그림을 다시 그리면 이 값을 다시 재야 한다 — 알파로 재는 게 유일한 방법이다(CLAUDE.md §5-1).
    private const float PlayerFootAlphaPad = 0.70f;
    private float VisibleGroundY() => LightningRodGroundY() + PlayerFootAlphaPad;

    // 이펙트의 **보이는 아래끝**을 지면에 맞춘다. alphaPad = 그 그림 아래쪽 투명 여백(유닛).
    private void AlignVisibleBottomToGround(GameObject obj, float alphaPad)
    {
        if (obj == null) return;
        SpriteRenderer sr = obj.GetComponentInChildren<SpriteRenderer>(true);
        if (sr == null) return;
        float visibleBottom = sr.bounds.min.y + alphaPad;
        obj.transform.position += Vector3.up * (VisibleGroundY() - visibleBottom);
    }
    // 🔴 기둥 밑동은 **플레이어 발밑**에 선다(사용자 결정 2026-09-19 — "주인공의 밑점이랑 피뢰침/피뢰침번개의 밑면이 같아야지").
    //    예전엔 카메라 중심에서 고정 오프셋(2.025 → 2.425)을 뺐는데, 맵마다 카메라와 플레이어의 상대 높이가 달라
    //    **우주에서만 떠 보였다.** 발밑을 기준으로 잡으면 맵이 늘어도 저절로 맞는다.
    //    기둥과 번개가 같이 이 선에 선다 — 둘의 밑면이 항상 같이 움직인다.
    private float LightningRodGroundY()
    {
        if (bodySrCache == null && animator != null) bodySrCache = animator.GetComponent<SpriteRenderer>();
        return bodySrCache != null ? bodySrCache.bounds.min.y : transform.position.y;
    }

    // scale: 레벨업 "피뢰침 범위" 스텝(2026-09-18). 피해 반경과 번개가 흩뿌려지는 폭이 같이 커진다.
    private IEnumerator LightningRodRoutine(float damage, float critChance, bool empowered, float scale,
                                            float extraDuration = 0f, int extraBolts = 0)
    {
        // 레벨업 "지속시간"(InstallDuration)과 "낙뢰 개수"(ProjectileCount) — 둘 다 2026-09-29에 붙인 축이다.
        float duration = LightningRodDuration + extraDuration;
        float interval = empowered ? ZeusStrikeInterval : LightningRodInterval;
        int boltsPerStrike = Mathf.Max(1, LightningRodBoltsPerStrike + extraBolts);
        float ratio = empowered ? LightningRodEmpoweredRatio : LightningRodDamageRatio;
        float radius = LightningRodRadius * (empowered ? LightningRodEmpoweredRadiusMult : 1f) * scale;

        Vector3 center = Camera.main != null ? Camera.main.transform.position : transform.position;
        center.z = 0f;

        // 🔴 기둥을 키울 땐 **밑동을 고정하고 위로** 키운다 — 중심을 고정하면 커진 만큼 땅에 파묻힌다.
        float rodHeight = RodHeight(empowered);
        float groundY = VisibleGroundY();   // 기둥·번개 밑동을 **보이는** 지면에 맞춘다(rect 기준이면 0.70 파묻힌다)
        // 2차 「제우스의 은총」의 석상만 조금 더 띄운다(2026-09-20 사용자 "석상 조금 위로").
        float rodLift = empowered ? ZeusStatueLift : LightningRodLift;
        lightningRod = SpawnLightningRod(new Vector3(center.x, groundY + rodHeight * 0.5f + rodLift, 0f), empowered);
        // 설치음 — 1차는 기둥을 꽂고, 2차(제우스)는 석상을 세운다. 시전 자체엔 소리가 따로 없다(낙뢰가 아직 안 떨어진다).
        SkillSfx.Play(empowered ? "Zeus.install" : "LightningRod.install");

        // 제우스상은 번개를 **모았다가(1~3프레임) 내리친다(4~6프레임)** — 1초 한 바퀴라 첫 타격을 모으는 반 바퀴만큼 늦춰
        // 이후 매 타격이 내리치는 프레임과 겹치게 한다. 타격 횟수·간격은 그대로다.
        if (empowered && zeusStatuePrefab != null) yield return new WaitForSeconds(ZeusChargeDelay);

        // 번개 아래끝은 같은 바닥선에서 LightningRodBoltDrop만큼 내린다(그림 pivot이 중앙이라 반높이만큼 올린다).
        float boltCenterY = groundY - LightningRodBoltDrop + BigThunderHalfHeight * LightningRodBoltScale;
        Vector3 boltScale = (bigThunderVfxPrefab != null ? bigThunderVfxPrefab.transform.localScale : Vector3.one)
                            * LightningRodBoltScale;

        // 이 캐스트 전용 버퍼. 인스턴스 필드로 두면 쿨감으로 두 피뢰침이 겹칠 때 서로의 목록을 밟는다.
        List<Enemy> inRange = new List<Enemy>();
        List<float> boltXs = new List<float>();
        float boltRadius = radius * LightningRodBoltRadiusRatio;

        for (float elapsed = 0f; elapsed < duration; elapsed += interval)
        {
            using (Enemy.GetSnapshot(out List<Enemy> enemies))
            {
                // 🔴 반경 안의 적을 **먼저** 모은다 — 번개 줄기를 그 적들 머리 위에 꽂기 위해서다
                //    (2026-09-27 사용자). 종전엔 줄기가 반경의 70% 안 **무작위 x**에 떨어져, 적이 없는
                //    허공에 번개가 치고 정작 맞는 적 위에는 아무것도 안 떴다.
                inRange.Clear();
                foreach (Enemy e in enemies)
                {
                    if (e == null || !e.IsAlive) continue;
                    // 🔴 2차 「제우스의 은총」은 **맵 전체**가 대상이다(2026-10-07 사용자 — 대신 시작 피해 −30%).
                    //    거리 제한만 없앤다. 줄기 폭(boltRadius)과 빈 줄기가 흩어지는 폭은 종전 반경에서 그대로 계산한다.
                    if (!empowered && Vector2.Distance(center, e.transform.position) > radius) continue;
                    inRange.Add(e);
                }

                // 타격음 — 번개가 실제로 내리치는 이 순간에만 울린다(시전 순간이 아니다).
                SkillSfx.Play(empowered ? "Zeus.strike" : "LightningRod.strike");

                // 줄기 자리를 먼저 정한다 — 연출이 없어도(프리팹 미배선) 판정은 이 자리로 한다.
                // ⚠️ 풀은 localScale을 되돌려 주지 않는다 — 재사용본이 옛 크기로 나오지 않게 매번 직접 넣는다.
                boltXs.Clear();
                for (int i = 0; i < boltsPerStrike; i++)
                {
                    float x;
                    if (i < inRange.Count)
                    {
                        // 부분 셔플 — 같은 적에 두 줄기가 겹쳐 꽂히지 않는다.
                        int pick = Random.Range(i, inRange.Count);
                        Enemy swap = inRange[i]; inRange[i] = inRange[pick]; inRange[pick] = swap;
                        x = inRange[i].transform.position.x;
                    }
                    else
                    {
                        // 적이 줄기 수보다 적으면 남는 줄기는 무작위 x로 — 빈 화면에 아무것도 안 뜨면
                        // 스킬이 안 나간 것처럼 보인다.
                        x = center.x + Random.Range(-1f, 1f) * radius * LightningRodBoltSpreadRatio;
                    }
                    boltXs.Add(x);
                    if (bigThunderVfxPrefab != null)
                    {
                        GameObject bolt = ObjectPool.Instance.Spawn(bigThunderVfxPrefab, new Vector3(x, boltCenterY, 0f), Quaternion.identity);
                        if (bolt != null) bolt.transform.localScale = boltScale;
                    }
                }

                // 🔴 판정은 **줄기가 떨어진 자리마다** 한다(사용자 결정 2026-09-30 — 9/27에 확인했던 "반경 안 전원 광역"을 대체).
                //    줄기 둘 이상의 범위에 겹친 적은 겹친 만큼 전부 맞는다(사용자 지시) — 낙뢰 수 카드가 곧 화력이다.
                //    피뢰침 반경 밖의 적은 줄기 범위 안이어도 맞지 않는다(inRange만 본다). 제우스는 반경 제한이 없어 전원이 inRange다.
                foreach (float bx in boltXs)
                {
                    for (int i = 0; i < inRange.Count; i++)
                    {
                        Enemy e = inRange[i];
                        if (e == null || !e.IsAlive) continue;
                        if (Mathf.Abs(e.transform.position.x - bx) > boltRadius) continue;

                        float hit = PlayerPassives.ApplyCrit(damage * ratio, critChance, out bool isCrit);
                        e.TakeDamage(hit, isLightningProc: true, isCrit: isCrit, source: ActiveSkillId.Lightning,
                                     rollLightning: false, statusIcon: StatusIconLibrary.Stun);
                        if (e != null && e.IsAlive) e.ApplyStun(LightningRodStun);
                        // 2차 「제우스의 은총」 — 노션 문구 "낙뢰를 떨굴 때마다 **피뢰침 쪽으로 적들을 끌어당긴다**".
                        if (empowered && e != null && e.IsAlive) e.ApplyPullTowardX(center.x, ZeusPullDistance);
                    }
                }
                inRange.Clear(); // 죽은 적 참조를 틱 사이에 붙들고 있지 않게
            }
            yield return new WaitForSeconds(interval);
        }

        // 지속이 끝나면 기둥도 같이 사라진다. 한 번에 하나만 돌므로 lightningRod는 곧 이 루틴의 기둥이다.
        if (lightningRod != null) { Destroy(lightningRod); lightningRod = null; }
        lightningRodRoutine = null;
    }

    // 지금 서 있는 피뢰침(제우스상)과 그 타격 루틴. 설치기는 한 번에 하나다(2026-10-01 사용자).
    private GameObject lightningRod;
    private Coroutine lightningRodRoutine;

    private void RemoveLightningRod()
    {
        if (lightningRodRoutine != null) { StopCoroutine(lightningRodRoutine); lightningRodRoutine = null; }
        if (lightningRod != null) { Destroy(lightningRod); lightningRod = null; }
    }

    // 🔴 스테이지가 끝나면 설치기를 전부 철거한다(2026-10-01 사용자). GameManager가 스테이지를 넘길 때 부른다.
    //    전탄발사 기계도 지속시간 내내 서 있으므로 연사와 함께 여기서 걷는다(2026-10-06).
    public void ClearInstallations()
    {
        RemoveLightningRod();
        StopBarrage();

        if (tornadoMakerRoutine != null) { StopCoroutine(tornadoMakerRoutine); tornadoMakerRoutine = null; }
        if (tornadoMaker != null) { Destroy(tornadoMaker); tornadoMaker = null; }
        tornadoMakerUntil = 0f;   // 다음 스테이지 첫 시전이 새 기계를 세우고 루틴을 새로 돌리게

        // 독수리 투하도 스테이지를 넘기면 끊는다(2026-10-07 사용자) — 연속 투하의 남은 횟수와 「독수리의 비」.
        // 이미 떨어지기 시작한 독수리는 그대로 떨어진다(재시전 때와 같은 규칙).
        eagleDropEpoch++;
        if (eagleRainRoutine != null) { StopCoroutine(eagleRainRoutine); eagleRainRoutine = null; }
    }

    private IEnumerator MiniEagleBonus(Enemy target, float damage, float critChance, float scale, ActiveSkillId source)
    {
        if (target == null) yield break;
        Vector3 pos = target.transform.position;
        yield return StartCoroutine(MeteorImpact(pos, scale));
        if (target == null) yield break;

        float hitDamage = PlayerPassives.ApplyCrit(damage, critChance, out bool isCrit);
        target.TakeDamage(hitDamage, isCrit: isCrit, source: source);
    }

    // ── 스나이핑 R0 2차 「독수리 특공대 지휘관」 ────────────────────────────
    // 🔴 2026-09-19 사용자 명세: "**마크를 그리는** 이펙트다. 마크가 처음 생성될 때 **약한 데미지**를 주고,
    //    마크가 **다 그려지면** 그때 독수리가 소환되어서 떨어져 피해를 입힌다."
    //    노션 문구: "가장 강력한 적을 조준해 독수리 특공대에게 지시를 내린다".
    [SerializeField] private GameObject snipingMarkVfxPrefab; // Effect_SnipingMark 플립북(5프레임, 비루프)
    private const float SnipingMarkDrawTime = 0.5f;  // 마크가 다 그려지는 데 걸리는 시간 — 플립북 길이와 맞출 것
    private const float SnipingMarkTickRatio = 0.2f; // 마크가 처음 생길 때 주는 "약한" 피해 비율

    private IEnumerator SnipingMarkStrike(Enemy target, float damage, float critChance, float scale, ActiveSkillId source)
    {
        if (target == null) yield break;
        Vector3 pos = target.transform.position;

        if (snipingMarkVfxPrefab != null)
            ObjectPool.Instance.SpawnTimed(snipingMarkVfxPrefab, pos, SnipingMarkDrawTime);

        // ① 마크가 생기는 순간 — 약한 피해
        if (target != null && target.IsAlive)
        {
            float weak = PlayerPassives.ApplyCrit(damage * SnipingMarkTickRatio, critChance, out bool weakCrit);
            target.TakeDamage(weak, isCrit: weakCrit, source: source);
        }

        // ② 다 그려질 때까지 기다렸다가 독수리를 떨군다
        yield return new WaitForSeconds(SnipingMarkDrawTime);

        // 마크는 **자리**에 찍힌 것이라 대상이 죽어도 그 자리에 떨어진다.
        Vector3 dropAt = target != null && target.IsAlive ? target.transform.position : pos;
        yield return StartCoroutine(MeteorImpact(dropAt, scale));

        if (target != null && target.IsAlive)
        {
            float hit = PlayerPassives.ApplyCrit(damage, critChance, out bool isCrit);
            target.TakeDamage(hit, isCrit: isCrit, source: source);
        }
    }

    // follow: 떨어지는 내내 그 적을 따라간다(독수리 투하 기본형). onLand(적 발밑 위치, 끝까지 그 적이었나)는 닿는 순간 불린다.
    // ⚠️ 적은 풀링된다 — 낙하 중에 한 번이라도 죽으면 추적을 끊는다. 같은 오브젝트가 새 적으로 재활용돼도 따라가지 않게.
    private IEnumerator MeteorImpact(Vector3 targetPos, float scale, Enemy follow = null, System.Action<Vector3, bool> onLand = null)
    {
        if (eagleDropPrefab == null)
        {
            onLand?.Invoke(targetPos, follow != null && follow.IsAlive);
            yield break;
        }

        const float fallAngleFromVertical = 15f;
        // x 0.4 = 떨어지는 동안 적이 걸어갈 몫을 앞질러 짚던 값. 따라가는 독수리는 앞지를 필요가 없다.
        Vector3 landOffset = new Vector3(follow != null ? 0f : 0.4f, 0.6f, 0f);
        Vector3 landPos = targetPos + landOffset;

        // 🔴 **낙하 시작점은 화면 위 바깥이어야 한다**(2026-09-19 사용자: "우주 같은 큰 맵에서는 하늘에서
        //    떨어지는 게 아니라 그냥 중간에 생성되어 떨어지는 것처럼 보인다").
        //    예전엔 착지점 위 **고정 6유닛**이었는데, 맵마다 카메라 크기가 다르다(`RunBootstrap`이
        //    `orthographicSize`에 배율을 곱한다) — 큰 맵에서는 6유닛 위가 이미 **화면 안**이라 허공에서 튀어나왔다.
        //    → 매번 카메라 위 가장자리에서 시작한다. 카메라가 없을 때만 옛 고정값으로 떨어진다.
        const float minFallHeight = 6f;       // 폴백 겸 하한 — 작은 맵에서도 이만큼은 떨어져야 낙하로 보인다
        const float offscreenMargin = 1.5f;   // 독수리 그림이 화면 밖에서 완전히 가려지도록
        Camera cam = Camera.main;
        float fallHeight = minFallHeight;
        if (cam != null)
        {
            float camTop = cam.transform.position.y + cam.orthographicSize;
            fallHeight = Mathf.Max(minFallHeight, camTop + offscreenMargin - landPos.y);
        }

        float horizontalOffset = fallHeight * Mathf.Tan(fallAngleFromVertical * Mathf.Deg2Rad);
        Vector3 start = landPos + new Vector3(horizontalOffset, fallHeight, 0f);
        GameObject eagle = Instantiate(eagleDropPrefab, start, Quaternion.identity);
        eagle.transform.localScale *= scale;

        // 낙하 **속도**를 고정한다(예전엔 0.3초 고정이라 높이가 달라지면 속도가 같이 달라졌다).
        // 20유닛/초 = 옛 값(6유닛 ÷ 0.3초) 그대로라 작은 맵의 손맛은 안 바뀐다.
        const float fallSpeed = 20f;
        const float maxFallDuration = 0.6f;   // 아주 큰 맵에서 타격이 늦어지지 않게 상한
        float duration = Mathf.Min(fallHeight / fallSpeed, maxFallDuration);
        float t = 0f;
        while (t < duration)
        {
            if (follow != null)
            {
                if (follow.IsAlive) targetPos = follow.transform.position;
                else follow = null; // 죽었으면 마지막 자리로 마저 떨어진다
                landPos = targetPos + landOffset;
            }
            eagle.transform.position = Vector3.Lerp(start, landPos, t / duration);
            t += Time.deltaTime;
            yield return null;
        }
        Destroy(eagle);

        if (eagleImpactVfxPrefab != null)
        {
            GameObject impact = ObjectPool.Instance.Spawn(eagleImpactVfxPrefab, landPos, Quaternion.identity);
            impact.transform.localScale = Vector3.one * 0.25f * scale;
            ObjectPool.Instance.Despawn(impact, 2f);
        }

        onLand?.Invoke(targetPos, follow != null && follow.IsAlive);
    }

    private static float GetDefaultCooldown(ActiveSkillId id)
    {
        SkillProgression p = Prog(id);
        return p != null ? p.baseCooldown : SkillProgression.DefaultBaseCooldown(id);
    }

    // ── 에셋(Prog_*)이 정하는 시작값들 ────────────────────────────────────────
    // 넷 다 **0이면 "안 쓴다"** 는 뜻이라 호출부의 코드 기본값이 그대로 남는다(에셋을 안 채운 스킬은 현행 유지).
    private static int GetBasePierce(ActiveSkillId id) => Prog(id) != null ? Prog(id).basePierce : 0;
    private static int GetBaseProjectiles(ActiveSkillId id) => Prog(id) != null ? Prog(id).baseProjectiles : 0;

    // 지속시간은 스킬마다 코드 기본값이 달라서 호출부가 자기 기본값을 넘긴다.
    public static float BaseDuration(ActiveSkillId id, float codeDefault)
    {
        SkillProgression p = Prog(id);
        return p != null && p.baseDuration > 0f ? p.baseDuration : codeDefault;
    }

    // 낙뢰만 고정 상수(BaseProcDamage)를 쓴다 — 실시간 ProcDamage는 배율이 적용된 '현재값'이라
    // 레벨업 성장 표시의 기준(기본값)으로 쓰면 부호가 뒤집힌다.
    private static float GetDefaultDamage(ActiveSkillId id)
    {
        if (id == ActiveSkillId.Lightning) return LightningStorm.BaseProcDamage;
        SkillProgression p = Prog(id);
        return p != null ? p.baseDamage : SkillProgression.DefaultBaseDamage(id);
    }
}
