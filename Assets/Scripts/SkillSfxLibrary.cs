using System;
using UnityEngine;

// 스킬 효과음 배선의 단일 소스(2026-09-27 사용자: "스킬 - 효과음 배선을 다 SO로 뺀 다음에,
// 각 슬롯마다 4개의 효과음을 배정해. 내가 후보군까지 다 듣고 최종적으로 쓰고 싶은 효과음에 체크를 치든 …").
//
// 슬롯 하나 = "소리가 나는 한 순간". 후보를 4개 담아 두고 `chosen`으로 고른다 —
// 고르는 것은 에디터 창(`Window > Blueberry Defense > 스킬 효과음 고르기`)에서 들어 보고 한다.
//
// 🔴 볼륨 상한이 0.7인 이유: `SfxPlayer.MasterVolume`(1.3)이 전부에 곱해진다. 0.7이면 실효 0.91이다.
// ⚠️ `AudioThrottle`은 **프레임 단위** 디바운스다(같은 프레임의 중복만 막는다). 0.06초 연사 같은 건
//    전혀 안 묶이므로, 연사 슬롯은 볼륨을 크게 낮춰서 쌓이는 소리를 견딜 수 있게 한다.
[CreateAssetMenu(fileName = "SkillSfxLibrary", menuName = "BlueberryDefense/Skill Sfx Library")]
public class SkillSfxLibrary : ScriptableObject
{
    public const int CandidateCount = 4;
    public const float MaxVolume = 0.7f;

    [Serializable]
    public class Slot
    {
        public string id;                 // "Shotgun.cast" · "LightningRod.install" · "Swing.impact"
        public string label;              // 에디터 창에 보이는 이름 ("산탄 — 시전")
        [TextArea] public string note;    // 언제 울리는지 · 고를 때 알아야 할 함정
        public AudioClip[] candidates = new AudioClip[CandidateCount];
        [Range(0, CandidateCount - 1)] public int chosen;
        [Range(0f, MaxVolume)] public float volume = 0.5f;

        public AudioClip Clip =>
            candidates != null && chosen >= 0 && chosen < candidates.Length ? candidates[chosen] : null;
    }

    public Slot[] slots = new Slot[0];

    public Slot Find(string id)
    {
        if (slots == null) return null;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null && slots[i].id == id) return slots[i];
        return null;
    }
}

// 런타임 창구. `SfxLibrary`와 같은 Resources 싱글톤 패턴이다.
// 🔴 빈 슬롯(후보를 안 꽂았거나 고른 칸이 비었을 때)은 **조용히 넘어간다** — 아직 안 고른 슬롯 때문에
//    게임이 멈추면 안 된다. 대신 에디터 창이 "안 채운 슬롯 N개"를 세어 보여준다.
public static class SkillSfx
{
    private const string ResourcePath = "SkillSfxLibrary";
    private static SkillSfxLibrary cached;
    private static bool loaded;

    public static SkillSfxLibrary Library
    {
        get
        {
            if (!loaded) { cached = Resources.Load<SkillSfxLibrary>(ResourcePath); loaded = true; }
            return cached;
        }
    }

    // 에디터에서 에셋을 갈아끼웠을 때 캐시를 비운다(플레이 재진입 없이 반영되게).
    public static void ClearCache() { cached = null; loaded = false; }

    public static void Play(string id)
    {
        SkillSfxLibrary lib = Library;
        SkillSfxLibrary.Slot slot = lib != null ? lib.Find(id) : null;
        AudioClip clip = slot != null ? slot.Clip : null;
        if (clip == null) return;
        if (!AudioThrottle.TryConsume(clip)) return;
        SfxPlayer.Play(clip, slot.volume);
    }
}
