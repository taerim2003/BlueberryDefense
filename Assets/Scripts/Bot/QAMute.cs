#if (BOT_QA && !UNITY_EDITOR) || UNITY_EDITOR
using UnityEngine;

// 🔴 QA 빌드는 **켜지는 순간 무조건 음소거**(사용자 결정 2026-09-22 — "켤 때 무조건 소리 꺼").
//    봇 설정(-botConfig)이 있든 없든, 설정이 깨져서 봇이 안 떴든 상관없다. 봇에 묶어 두면
//    설정 한 줄이 틀렸을 때 평범한 게임으로 떠서 소리가 난다(실제로 그랬다).
//    첫 씬 로드 **전에** 끄고, VolumeSettings가 옵션을 적용할 때마다 볼륨을 다시 세우므로 매 프레임 끝에 다시 누른다.
//
// 🔴 **에디터 플레이모드도 음소거한다**(사용자 지시 2026-09-27 — 무인 측정이 밤에 소리를 냈다).
//    `EditorUtility.audioMasterMute`(Game 뷰의 Mute Audio)로는 안 막힌다 — `VolumeSettings.Apply`가
//    `AudioListener.volume`을 저장값으로 **되세우기** 때문이다(VolumeSettings.cs의 마스터 처리).
//    그래서 QA 빌드와 **같은 방식**(매 프레임 끝에 0으로 누르기)을 에디터에도 쓴다. 사본을 만들지 않으려고 한 파일에 둔다.
//    소리를 듣고 싶으면 메뉴 `Window > Blueberry Defense > 에디터 소리` 를 켠다(EditorPrefs에 남는다).
//
// 릴리스 빌드에는 이 파일이 아예 없다.
public class QAMute : MonoBehaviour
{
#if UNITY_EDITOR
    public const string EditorSoundPref = "BBD.EditorSoundOn";

    [UnityEditor.MenuItem("Window/Blueberry Defense/에디터 소리 켜기 · 끄기 토글")]
    private static void ToggleEditorSound()
    {
        bool on = !UnityEditor.EditorPrefs.GetBool(EditorSoundPref, false);
        UnityEditor.EditorPrefs.SetBool(EditorSoundPref, on);
        UnityEditor.EditorUtility.audioMasterMute = !on;
        Debug.Log("[QAMute] 에디터 소리 " + (on ? "켜짐 — 다음 플레이모드부터 들린다" : "꺼짐"));
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        // 손으로 띄워 눈으로 볼 때 쓰는 인자(봇과 무관):
        //   -impactVfxCap <n>  충돌 이펙트 프레임당 상한(0=게임 기본값 8, 99999=사실상 무제한)
        //   -sound             음소거 해제
        string[] args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-impactVfxCap");
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int cap)) BotInput.ImpactVfxPerFrameOverride = cap;
        if (System.Array.IndexOf(args, "-sound") >= 0) return;
#if UNITY_EDITOR
        if (UnityEditor.EditorPrefs.GetBool(EditorSoundPref, false)) return;
        UnityEditor.EditorUtility.audioMasterMute = true;   // Game 뷰 토글도 같이 — 눈으로 확인되게
#endif
        AudioListener.volume = 0f;
        var go = new GameObject("[QAMute]");
        DontDestroyOnLoad(go);
        go.AddComponent<QAMute>();
    }

    private void LateUpdate() => AudioListener.volume = 0f;
}
#endif
