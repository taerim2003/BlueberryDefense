using UnityEngine;

// 원본 VFX 팩 사운드가 이미 0dBFS 근처로 마스터링된 게 많아서, AudioSource.volume만 올리면
// 오브/회오리 때처럼 순간 피크가 찢어진다(클리핑). 오디오 스레드에서 직접 피크를 눌러주는
// 심플한 브릭월 리미터를 걸어서, 게인을 더 올려도 찢어지지 않고 체감 음량(RMS)만 커지게 한다.
// attack은 즉시(샘플 단위로 바로 억제), release는 부드럽게 풀어서 펌핑 아티팩트를 줄인다.
[RequireComponent(typeof(AudioSource))]
public class SfxLimiter : MonoBehaviour
{
    [SerializeField] private float threshold = 0.92f;
    [SerializeField] private float releaseSamples = 512f;

    private float envelope = 1f;

    private void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i++)
        {
            float sample = data[i];
            float abs = Mathf.Abs(sample);
            float targetGain = abs > threshold ? threshold / abs : 1f;
            envelope = targetGain < envelope ? targetGain : envelope + (targetGain - envelope) / releaseSamples;
            data[i] = sample * envelope;
        }
    }
}
