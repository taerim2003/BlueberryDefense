using System.Collections.Generic;
using UnityEngine;

// 동일 효과음이 한 프레임에 수십 개씩 겹쳐 재생되며 생기는 렉/음량 과부하를 막기 위한 디바운스.
public static class AudioThrottle
{
    private const float MinInterval = 0.08f;

    private static readonly Dictionary<AudioClip, float> lastPlayTime = new Dictionary<AudioClip, float>();

    public static bool TryConsume(AudioClip clip)
    {
        if (clip == null) return true;

        float last = lastPlayTime.GetValueOrDefault(clip, -999f);
        if (Time.unscaledTime - last < MinInterval) return false;

        lastPlayTime[clip] = Time.unscaledTime;
        return true;
    }
}
