using UnityEngine;

// AudioSource.PlayClipAtPoint는 호출마다 새 GameObject를 만들고, 그 AudioSource가 Unity 기본값인
// spatialBlend=1(3D)로 생성돼 카메라와의 거리에 따라 소리가 작아지거나, 연타 시 임시 오브젝트가
// 계속 쌓여 재생이 불안정해지는 문제가 있었다. 상시 하나의 2D AudioSource에서 PlayOneShot으로
// 겹쳐 재생해 거리 감쇠와 오브젝트 생성/파괴를 모두 없앤다.
public static class SfxPlayer
{
    public static float MasterVolume = 1.3f;

    private static AudioSource source;
    private static AudioSource Source
    {
        get
        {
            if (source == null)
            {
                GameObject obj = new GameObject("SfxPlayer");
                Object.DontDestroyOnLoad(obj);
                source = obj.AddComponent<AudioSource>();
                source.spatialBlend = 0f;
                source.playOnAwake = false;
            }
            return source;
        }
    }

    public static void Play(AudioClip clip, float volume)
    {
        if (clip == null) return;
        Source.PlayOneShot(clip, volume * MasterVolume);
    }
}
