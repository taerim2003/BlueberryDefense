using System.Collections.Generic;
using UnityEngine;

// 동일 효과음이 한 프레임에 수십 개씩 겹쳐 재생되며 생기는 렉/음량 과부하를 막기 위한 디바운스.
// 프레임 단위로만 중복을 막는다 — 시간(초) 기반으로 하면 화살 연사나 몹 무더기 처치처럼
// 몇 프레임 간격으로 연달아 터지는 정상적인 이벤트까지 죄다 묵음 처리되어 "소리가 씹힌다"는
// 문제가 생긴다 (원래 목적은 같은 프레임 내 중복 스폰 방지일 뿐, 그 이상은 다 들려야 한다).
public static class AudioThrottle
{
    private static readonly Dictionary<AudioClip, int> lastPlayFrame = new Dictionary<AudioClip, int>();

    public static bool TryConsume(AudioClip clip)
    {
        if (clip == null) return true;

        int last = lastPlayFrame.GetValueOrDefault(clip, -1);
        if (last == Time.frameCount) return false;

        lastPlayFrame[clip] = Time.frameCount;
        return true;
    }
}
