#if BOT_QA && !UNITY_EDITOR
using UnityEngine;

// 🔴 QA 빌드는 **켜지는 순간 무조건 음소거**(사용자 결정 2026-09-22 — "켤 때 무조건 소리 꺼").
//    봇 설정(-botConfig)이 있든 없든, 설정이 깨져서 봇이 안 떴든 상관없다. 봇에 묶어 두면
//    설정 한 줄이 틀렸을 때 평범한 게임으로 떠서 소리가 난다(실제로 그랬다).
//    첫 씬 로드 **전에** 끄고, VolumeSettings가 옵션을 적용할 때마다 볼륨을 다시 세우므로 매 프레임 끝에 다시 누른다.
//    릴리스 빌드·에디터에는 이 파일이 아예 없다.
public class QAMute : MonoBehaviour
{
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

        AudioListener.volume = 0f;
        var go = new GameObject("[QAMute]");
        DontDestroyOnLoad(go);
        go.AddComponent<QAMute>();
    }

    private void LateUpdate() => AudioListener.volume = 0f;
}
#endif
