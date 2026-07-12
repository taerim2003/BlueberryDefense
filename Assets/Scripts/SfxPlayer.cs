using UnityEngine;

// AudioSource 하나에서 PlayOneShot으로 계속 겹쳐 재생하면, 짧은 간격으로 몰리는 원샷 보이스를
// 그 소스 혼자 스케줄링하다가 가끔 출력이 안 나가는 경우가 있었다. 전용 AudioSource 풀을
// 라운드로빈으로 돌려 재생 요청마다 항상 자기 채널을 갖게 한다.
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
