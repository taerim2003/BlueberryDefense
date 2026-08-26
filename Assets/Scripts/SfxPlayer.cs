using UnityEngine;

// AudioSource 하나에서 PlayOneShot으로 계속 겹쳐 재생하면, 짧은 간격으로 몰리는 원샷 보이스를
// 그 소스 혼자 스케줄링하다가 가끔 출력이 안 나가는 경우가 있었다. 전용 AudioSource 풀을
// 라운드로빈으로 돌려 재생 요청마다 항상 자기 채널을 갖게 한다.
//
// 🧰 사운드 배선 소유권 — **한 곳이 아니다. 새 소리를 꽂기 전에 셋 다 볼 것:**
//  - 일반 효과음: `Assets/Resources/SfxLibrary.asset`. ⚠️ 꽂아둔 건 후보지 확정이 아니다(듣고 거슬리는 것만 교체).
//  - 스킬 캐스트음 9종: `SfxLibrary`가 아니라 **`PlayerSkills` 인스펙터**에 슬롯이 따로 있다(중복 배선 금지).
//  - BGM: `MapDefinition.bgm`.
//  ⚠️ `JuicyButton`은 별도 어셈블리(`JuicyUI.Runtime`)라 이 클래스를 **못 부른다.** 어셈블리 참조가 단방향이라
//     `JuicyButton.Clicked` static 이벤트를 이쪽에서 **역방향으로 구독**한다 — 직접 호출로 되돌리면 컴파일이 깨진다.
//  ⚠️ 승리는 `GameOver()`를 안 거친다 — `GameClear()`에 따로 배선돼 있다. 한쪽만 고치면 승리 때 소리가 안 난다.
public static class SfxPlayer
{
    public static float MasterVolume = 1.3f;

    private const int PoolSize = 12;

    private static AudioSource[] pool;
    private static int nextIndex;

    private static void EnsurePool()
    {
        if (pool != null) return;

        GameObject root = new GameObject("SfxPlayer");
        Object.DontDestroyOnLoad(root);

        pool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            AudioSource src = root.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.priority = 0; // 발사/캐스트음은 핵심 게임플레이 피드백이라 가상화되지 않도록 최우선순위 고정
            pool[i] = src;
        }
    }

    // UI 버튼 클릭음. JuicyButton은 별도 어셈블리(JuicyUI.Runtime)라 SfxPlayer를 못 부르므로,
    // 그쪽이 알려주는 이벤트를 이쪽에서 구독한다. 씬의 버튼 19개가 전부 그 컴포넌트를 쓰므로 배선은 이 한 곳뿐.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookButtonClick()
    {
        JuicyButton.Clicked -= PlayButtonClick; // 도메인 리로드를 끈 설정에서 중복 구독되지 않게
        JuicyButton.Clicked += PlayButtonClick;
        JuicyButton.Hovered -= PlayButtonHover;
        JuicyButton.Hovered += PlayButtonHover;
    }

    private static void PlayButtonClick() => Play(SfxId.ButtonClick);
    private static void PlayButtonHover() => Play(SfxId.ButtonHover);

    // 게임 사건용 재생. 클립은 SfxLibrary 에셋이 쥐고 있어서 호출부는 무슨 소리인지만 말하면 된다.
    // 라이브러리가 없거나 슬롯이 비어 있으면 조용히 넘어간다 — 음원을 아직 안 채운 상태에서도 게임이 정상 동작한다.
    public static void Play(SfxId id)
    {
        SfxLibrary library = SfxLibrary.Instance;
        if (library == null) return;

        SfxSlot slot = library.Get(id);
        if (slot.clip == null) return;
        if (!AudioThrottle.TryConsume(slot.clip)) return; // 같은 프레임에 몰린 요청은 한 번만

        Play(slot.clip, slot.volume);
    }

    public static void Play(AudioClip clip, float volume)
    {
        if (clip == null) return;
        EnsurePool();

        AudioSource src = pool[nextIndex];
        nextIndex = (nextIndex + 1) % PoolSize;

        src.Stop();
        src.clip = clip;
        src.volume = volume * MasterVolume;
        src.Play();
    }
}
