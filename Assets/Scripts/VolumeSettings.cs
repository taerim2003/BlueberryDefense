using System.Collections.Generic;
using UnityEngine;

// 음량의 단일 소스. 옵션 패널이 값을 바꾸면 여기서 실제 오디오 경로에 반영한다.
//
// 음량이 걸리는 경로는 셋이다:
//  · 마스터 → AudioListener.volume  (모든 소리에 확실히 걸린다)
//  · 배경음 → RunBootstrap이 만든 BGM AudioSource (Register로 등록받는다 — 씬마다 새로 생기므로)
//  · 효과음 → SfxPlayer.MasterVolume (스킬 캐스트음 + SfxLibrary의 게임 사건음)
//           → VFX 프리팹에 직접 붙은 AudioSource는 ObjectPool.Spawn이 스폰 시점에 Sfx를 곱한다
//
// ⚠️ 그 VFX 소스만은 스폰 시점 곱셈이라, 이미 재생 중인 루프 사운드는 슬라이더를 움직여도 안 바뀐다
//    (다음 스폰부터 반영). 대부분 1~2초 원샷이라 실사용에선 곧 따라온다.
public static class VolumeSettings
{
    private const string MasterKey = "option.volume.master";
    private const string BgmKey = "option.volume.bgm";
    private const string SfxKey = "option.volume.sfx";

    // SfxPlayer가 원래 쓰던 값. 사용자 설정(0~1)을 여기에 곱한다.
    public const float SfxBaseGain = 1.3f;

    public static float Master { get; private set; } = 1f;
    public static float Bgm { get; private set; } = 1f;
    public static float Sfx { get; private set; } = 1f;

    // 씬이 바뀌면 이전 BGM 소스는 파괴되므로 null을 걸러내며 쓴다.
    private static readonly List<AudioSource> bgmSources = new List<AudioSource>();

    private static bool loaded;

    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        Master = PlayerPrefs.GetFloat(MasterKey, 1f);
        Bgm = PlayerPrefs.GetFloat(BgmKey, 1f);
        Sfx = PlayerPrefs.GetFloat(SfxKey, 1f);
        Apply();
    }

    public static void SetMaster(float v) { EnsureLoaded(); Master = Mathf.Clamp01(v); Apply(); Save(); }
    public static void SetBgm(float v) { EnsureLoaded(); Bgm = Mathf.Clamp01(v); Apply(); Save(); }
    public static void SetSfx(float v) { EnsureLoaded(); Sfx = Mathf.Clamp01(v); Apply(); Save(); }

    // BGM AudioSource는 판마다 새로 생긴다 — 만든 쪽(RunBootstrap)이 등록해 주면 현재 음량이 즉시 반영된다.
    public static void RegisterBgm(AudioSource source)
    {
        if (source == null) return;
        EnsureLoaded();
        if (!bgmSources.Contains(source)) bgmSources.Add(source);
        source.volume = Bgm; // 마스터는 AudioListener가 이미 곱한다
    }

    private static void Apply()
    {
        AudioListener.volume = Master;
        SfxPlayer.MasterVolume = SfxBaseGain * Sfx;

        for (int i = bgmSources.Count - 1; i >= 0; i--)
        {
            if (bgmSources[i] == null) { bgmSources.RemoveAt(i); continue; }
            bgmSources[i].volume = Bgm;
        }
    }

    private static void Save()
    {
        PlayerPrefs.SetFloat(MasterKey, Master);
        PlayerPrefs.SetFloat(BgmKey, Bgm);
        PlayerPrefs.SetFloat(SfxKey, Sfx);
        PlayerPrefs.Save();
    }

    // 게임 시작 시 저장값을 실제 오디오에 반영 (옵션 패널을 한 번도 안 열어도 적용되도록).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap() => EnsureLoaded();
}
