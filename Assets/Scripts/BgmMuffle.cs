using UnityEngine;

// "옆방에서 들리는" BGM 변조. 저역통과 + 음량 덕킹 두 가지로 만든다.
//
// 수치를 여기 모아 두는 이유: 타이틀 화면의 패널(스킬트리·옵션 등)과 전투의 종료 화면(클리어/게임오버)이
// **같은 소리**를 내야 한다. 인스펙터에 흩어 두면 반드시 한쪽만 손보게 되고 화면마다 다르게 들린다.
// 감을 조절할 땐 이 파일의 상수만 만지면 전 화면에 걸린다.
//
// Reverb는 안 쓴다 — 프리셋 방식이라 값을 보간할 수 없어서 전환 순간에 튄다.
public static class BgmMuffle
{
    public const float OpenCutoff = 22000f;       // 사실상 필터가 안 걸린 상태
    public const float MuffledCutoff = 900f;      // 낮출수록 먹먹해진다
    public const float MuffledVolumeScale = 0.7f; // UI 효과음이 들려야 하므로 조금만 낮춘다
    public const float MuffleSeconds = 0.7f;      // 뒤로 물러나고 돌아오는 데 걸리는 시간
    public const float CrossfadeSeconds = 0.9f;   // 곡이 바뀔 때 겹쳐 넘기는 시간

    // 씬 전환용 전역 배율. SceneFade가 검은 화면의 알파와 반대로 몰아준다(화면이 어두워지는 만큼 BGM도 물러남).
    // 이게 없으면 음악이 만땅으로 울리다가 씬이 언로드되는 순간 뚝 끊긴다.
    // 🔴 BGM 음량을 정하는 모든 곳이 이걸 곱해야 한다 — 하나라도 빼먹으면 그 소스만 끊기는 소리로 남는다.
    public static float SceneFadeGain = 1f;

    // 도메인 리로드를 끈 설정에서 이전 실행의 값(전환 도중 멈췄다면 0)이 남아 무음으로 시작하는 것을 막는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetSceneFade() => SceneFadeGain = 1f;

    // ⚠️ unscaledDeltaTime을 쓴다 — 게임오버/클리어는 Time.timeScale=0이라
    //    deltaTime으로는 진행도가 영영 안 움직인다(연출이 통째로 죽는다).
    public static float Advance(float blend, bool muffled)
        => Mathf.MoveTowards(blend, muffled ? 1f : 0f, Time.unscaledDeltaTime / MuffleSeconds);

    public static float AdvanceCrossfade(float fade)
        => Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / CrossfadeSeconds);

    // 선형 진행도를 귀에 자연스러운 곡선으로 바꾼다(smoothstep). 시작과 끝이 완만해져
    // "물러나기 시작하는 순간"과 "다 물러난 순간"의 각이 사라진다.
    public static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // 주파수는 로그 스케일로 들린다 — 선형 보간하면 앞부분에서만 확 변한다.
    public static float CutoffAt(float blend)
        => Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoff), Mathf.Log(MuffledCutoff), Ease(blend)));

    // 변조 정도에 따른 음량 배율(1 = 그대로, MuffledVolumeScale = 다 물러남).
    public static float GainAt(float blend)
        => Mathf.Lerp(1f, MuffledVolumeScale, Ease(blend));

    // 등파워 크로스페이드. 선형으로 겹치면 중간에서 음량이 푹 꺼진다(두 소리의 합이 -3dB로 내려감).
    // sin/cos 쌍은 제곱합이 항상 1이라 겹치는 내내 체감 음량이 유지된다.
    public static float FadeInGain(float t) => Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI * 0.5f);
    public static float FadeOutGain(float t) => Mathf.Cos(Mathf.Clamp01(t) * Mathf.PI * 0.5f);

    // 곡이 바뀌지 않는 곳(전투 종료 화면)용 단축 경로.
    public static void Apply(AudioSource source, AudioLowPassFilter lowPass, float blend)
    {
        if (lowPass != null) lowPass.cutoffFrequency = CutoffAt(blend);
        if (source != null) source.volume = VolumeSettings.BgmVolume * GainAt(blend) * SceneFadeGain;
    }
}
