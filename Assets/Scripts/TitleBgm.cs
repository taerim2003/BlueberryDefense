using UnityEngine;

// 타이틀 화면 BGM. 곡이 둘이다 — 게임을 켜고 **처음** 온 사람과 한 판 치르고 **돌아온** 사람에게 다른 곡을 튼다.
// 판정은 RunConfig.HasPlayedThisSession(static)이라 씬 전환은 넘어가고 게임을 껐다 켜면 초기화된다(= 세션 기준).
// 승패는 안 가린다 — 클리어든 게임오버든 "한 판 겪고 돌아온 것"으로 본다(사용자 결정).
//
// 패널이 열리면 곡을 바꾸거나 뒤로 물린다. panelBgm의 각 칸이 화면 하나를 맡고, 곡 칸을 비우면 방식이 갈린다:
//  · 곡이 있으면  → 그 곡으로 크로스페이드
//  · 곡이 비었으면 → 타이틀곡을 그대로 이어가며 LowPass + 덕킹으로 "옆방에서 들리는" 변조본
//
// 🔴 AudioSource가 **둘**인 이유: 하나로는 곡을 갈아끼울 때 반드시 끊긴다(clip 교체는 즉시 반영된다).
//    한쪽을 재우며 다른 쪽을 깨우는 등파워 크로스페이드라야 이어 들린다. 필터가 GameObject 단위로 걸리므로
//    두 소스를 **각자 자식 오브젝트**에 둬서 서로의 필터에 영향을 주지 않게 한다.
//
// ⚠️ 두 소스 모두 VolumeSettings.RegisterBgm으로 등록해야 옵션의 배경음 슬라이더가 먹는다.
//    등록 후에도 Update가 매 프레임 volume을 덮으므로 VolumeSettings.BgmVolume을 곱해 쓴다.
public class TitleBgm : MonoBehaviour
{
    [System.Serializable]
    public struct PanelBgm
    {
        [Tooltip("이 오브젝트가 켜져 있는 동안. ⚠️ 항상 켜져 있는 루트가 아니라 실제로 토글되는 오브젝트를 넣을 것")]
        public GameObject panel;
        [Tooltip("틀 곡. 비우면 타이틀곡을 이어가며 LowPass 변조본으로 쓴다")]
        public AudioClip clip;
    }

    [Header("타이틀 곡")]
    [Tooltip("게임을 켜고 처음 타이틀에 왔을 때")]
    [SerializeField] private AudioClip firstVisitClip;
    [Tooltip("한 판 치르고 돌아왔을 때 (승패 무관)")]
    [SerializeField] private AudioClip afterRunClip;

    [Header("패널별 곡 — 위에서부터 먼저 켜져 있는 칸이 이긴다")]
    [SerializeField] private PanelBgm[] panelBgm;

    private readonly AudioSource[] sources = new AudioSource[2];
    private readonly AudioLowPassFilter[] filters = new AudioLowPassFilter[2];

    private AudioClip titleClip;
    private float titleTime;   // 패널을 여닫을 때마다 타이틀곡이 처음부터 다시 시작하지 않도록 재생 위치를 기억
    private int active;        // 지금 올라오고 있는 소스
    private float fade = 1f;   // active 쪽으로의 크로스페이드 진행도 (1 = 전환 끝)
    private float blend;       // 0 = 평소, 1 = 옆방
    private AudioClip current; // 지금 목표로 하는 곡

    private void Awake()
    {
        titleClip = RunConfig.HasPlayedThisSession ? afterRunClip : firstVisitClip;
        if (titleClip == null) titleClip = firstVisitClip; // 슬롯을 아직 안 채웠으면 있는 쪽으로 폴백
        if (titleClip == null) return;                     // 둘 다 비면 무음(현행과 동일)

        for (int i = 0; i < 2; i++)
        {
            GameObject go = new GameObject("Bgm" + i);
            go.transform.SetParent(transform, false);

            AudioSource s = go.AddComponent<AudioSource>();
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            sources[i] = s;

            AudioLowPassFilter f = go.AddComponent<AudioLowPassFilter>();
            f.cutoffFrequency = BgmMuffle.OpenCutoff;
            filters[i] = f;

            VolumeSettings.RegisterBgm(s);
        }

        current = titleClip;
        sources[active].clip = titleClip;
        sources[active].Play();
    }

    private void Update()
    {
        if (sources[0] == null) return;

        AudioSource front = sources[active];
        AudioSource back = sources[1 - active];

        if (front.clip == titleClip) titleTime = front.time;

        bool panelOpen = TryGetOpenPanel(out AudioClip panelClip);
        AudioClip desired = panelClip != null ? panelClip : titleClip;

        // 목표 곡이 바뀌면 쉬고 있던 소스에서 새 곡을 깨우고 크로스페이드를 시작한다.
        if (desired != current)
        {
            current = desired;
            active = 1 - active;
            front = sources[active];
            back = sources[1 - active];

            front.clip = desired;
            // 타이틀곡으로 돌아올 땐 나갔던 지점부터 — 패널을 여닫을 때마다 곡이 처음부터 다시 시작하면 어색하다.
            front.time = desired == titleClip ? Mathf.Min(titleTime, desired.length - 0.01f) : 0f;
            front.volume = 0f;
            front.Play();
            fade = 0f;
        }

        fade = BgmMuffle.AdvanceCrossfade(fade);
        blend = BgmMuffle.Advance(blend, panelOpen);

        // 곡을 바꾸는 패널에서는 저역통과를 걸지 않는다(곡 자체가 이미 바뀌었으므로).
        // 음량 덕킹은 어느 쪽이든 걸어서 UI 효과음이 묻히지 않게 한다.
        float cutoff = panelClip != null ? BgmMuffle.OpenCutoff : BgmMuffle.CutoffAt(blend);
        filters[0].cutoffFrequency = cutoff;
        filters[1].cutoffFrequency = cutoff;

        float bed = VolumeSettings.BgmVolume * BgmMuffle.GainAt(blend) * BgmMuffle.SceneFadeGain;
        front.volume = bed * BgmMuffle.FadeInGain(fade);
        back.volume = bed * BgmMuffle.FadeOutGain(fade);

        if (fade >= 1f && back.isPlaying) back.Stop();
    }

    // 켜져 있는 첫 패널을 찾아 그 칸의 곡을 넘긴다. 곡 칸이 비어 있으면 clip은 null(= 변조본 방식).
    private bool TryGetOpenPanel(out AudioClip clip)
    {
        clip = null;
        if (panelBgm == null) return false;

        for (int i = 0; i < panelBgm.Length; i++)
        {
            GameObject go = panelBgm[i].panel;
            if (go != null && go.activeInHierarchy) { clip = panelBgm[i].clip; return true; }
        }
        return false;
    }
}
