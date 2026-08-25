using UnityEngine;

// 게임 전역 효과음의 단일 소스. 음원을 구해 오면 이 에셋 하나에 드래그하는 것으로 끝난다.
//
// ⚠️ 스킬 캐스트음(whirlwindCastSfx 등)은 PlayerSkills 인스펙터가 따로 쥔다 — 여기 중복해 넣지 말 것.
//    VFX 프리팹에 딸려 붙은 AudioSource도 별개다(ObjectPool이 재생).
//
// 배선이 따로 없다: Assets/Resources/SfxLibrary.asset 에 두면 SfxPlayer가 알아서 찾는다.
// 비어 있어도 게임은 정상 동작한다(clip이 null이면 조용히 넘어감) — 음원이 나오는 대로 하나씩 채우면 된다.
public enum SfxId
{
    None = 0,

    EnemyHit,        // 적을 때림 — 가장 자주 난다
    EnemyDeath,      // 적 사망
    PlayerHit,       // 플레이어 피격
    LevelUp,         // 레벨업 화면 등장
    EssencePickup,   // 정수 획득
    HeartPickup,     // 하트 획득
    ButtonClick,     // UI 버튼 클릭 (전 화면 공용)
    ButtonHover,     // UI 버튼 호버 (전 화면 공용)
    Evolution,       // 진화 확정
    TreasureOpen,    // 보물상자 열림
    WaveWarning,     // 중간 소환 예고
    StageClear,      // 스테이지 클리어
    Victory,         // 최종 승리
    GameOver,        // 패배
    SkillTreeNode,   // 스킬트리 노드 찍기
}

[System.Serializable]
public struct SfxSlot
{
    public AudioClip clip;
    [Range(0f, 1.5f)] public float volume;
}

[CreateAssetMenu(fileName = "SfxLibrary", menuName = "BlueberryDefense/Sfx Library")]
public class SfxLibrary : ScriptableObject
{
    // Resources에서 한 번만 찾아 캐시한다. 에셋이 없으면 null이고, 그 경우 재생 요청은 전부 무시된다.
    private static SfxLibrary cached;
    private static bool searched;

    public static SfxLibrary Instance
    {
        get
        {
            if (!searched)
            {
                searched = true;
                cached = Resources.Load<SfxLibrary>("SfxLibrary");
            }
            return cached;
        }
    }

    [Header("전투")]
    [Tooltip("적을 때릴 때. 초당 여러 번 나므로 짧고 가벼운 것으로")]
    public SfxSlot enemyHit;
    public SfxSlot enemyDeath;
    [Tooltip("플레이어가 맞을 때")]
    public SfxSlot playerHit;

    [Header("성장")]
    public SfxSlot levelUp;
    public SfxSlot evolution;
    public SfxSlot skillTreeNode;

    [Header("획득")]
    public SfxSlot essencePickup;
    public SfxSlot heartPickup;
    public SfxSlot treasureOpen;

    [Header("판 진행")]
    [Tooltip("중간 소환 예고")]
    public SfxSlot waveWarning;
    public SfxSlot stageClear;
    public SfxSlot victory;
    public SfxSlot gameOver;

    [Header("UI")]
    [Tooltip("버튼 19개가 전부 이 하나를 쓴다")]
    public SfxSlot buttonClick;
    [Tooltip("커서를 올릴 때. 클릭음보다 훨씬 작고 짧아야 한다 — 화면을 훑기만 해도 연달아 난다")]
    public SfxSlot buttonHover;

    public SfxSlot Get(SfxId id) => id switch
    {
        SfxId.EnemyHit => enemyHit,
        SfxId.EnemyDeath => enemyDeath,
        SfxId.PlayerHit => playerHit,
        SfxId.LevelUp => levelUp,
        SfxId.EssencePickup => essencePickup,
        SfxId.HeartPickup => heartPickup,
        SfxId.ButtonClick => buttonClick,
        SfxId.ButtonHover => buttonHover,
        SfxId.Evolution => evolution,
        SfxId.TreasureOpen => treasureOpen,
        SfxId.WaveWarning => waveWarning,
        SfxId.StageClear => stageClear,
        SfxId.Victory => victory,
        SfxId.GameOver => gameOver,
        SfxId.SkillTreeNode => skillTreeNode,
        _ => default,
    };

    // 구조체 기본값이 volume 0이라 그냥 만들면 전 슬롯이 무음이 된다 — 만들 때 한 번 기본값을 깔아 준다.
    // 에셋 생성 메뉴(SfxLibrarySetup)도 이걸 부르므로 public이다.
    private void Reset() => ApplyDefaultVolumes();

    [ContextMenu("볼륨을 기본값으로")]
    public void ApplyDefaultVolumes()
    {
        enemyHit.volume = 0.6f;   // 가장 자주 나므로 기본을 낮게 잡는다
        enemyDeath.volume = 0.8f;
        playerHit.volume = 1f;
        levelUp.volume = 1f;
        evolution.volume = 1f;
        skillTreeNode.volume = 0.8f;
        essencePickup.volume = 0.5f;
        heartPickup.volume = 0.8f;
        treasureOpen.volume = 1f;
        waveWarning.volume = 1f;
        stageClear.volume = 1f;
        victory.volume = 1f;
        gameOver.volume = 1f;
        buttonClick.volume = 0.7f;
    }
}
