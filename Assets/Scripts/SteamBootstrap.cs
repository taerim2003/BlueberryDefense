using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

// Steam 연동의 단일 창구. 하는 일 = **업적 전달** + 이 게임의 Steam 언어 읽기(`GameLanguage`).
// 어떤 업적을 언제 깨는지는 `Achievements`가 정한다.
//
// 🔴 세이브는 여기를 지나지 않는다. 클라우드 동기화는 Steamworks 쪽 **Auto-Cloud 설정**이 하고,
//    게임은 그냥 `SaveStore`로 파일을 쓸 뿐이다(`SaveStore.cs` 머리말 참고). SDK는 업적 때문에만 들어왔다.
//
// 🔴 **에디터에서는 켜지 않는다**(`RunInEditor`). 봇 플레이테스트가 한 판에 수십 번 드나드는데
//    Steam이 안 떠 있으면 Init이 매번 실패하고, 떠 있으면 테스트가 진짜 업적을 건드린다.
//    검증은 빌드로 한다 — 심사에서 보는 것도 빌드다.
//
// 배선 없음: `RuntimeInitializeOnLoadMethod`가 스스로 오브젝트를 만든다. 씬에 넣지 말 것.
public class SteamBootstrap : MonoBehaviour
{
    // Steamworks 앱 ID. 0이면 `RestartAppIfNecessary`를 건너뛴다 —
    // **Steam으로 실행하면 Steam이 앱 ID를 알려주므로 0이어도 업적은 정상 동작한다.**
    // 채워 넣으면 exe를 직접 켠 사람도 Steam을 거쳐 다시 켜진다(권장).
    private const uint AppId = 0;

    private const bool RunInEditor = false;

    private static SteamBootstrap instance;

#if !DISABLESTEAMWORKS
    private static bool running;
    private static bool storePending;   // 한 프레임에 여러 업적이 풀려도 StoreStats는 한 번만(소급 판정이 한꺼번에 푼다)
    public static bool Running => running;
#else
    public static bool Running => false;
#endif

    // Steam 라이브러리에서 이 게임에 고른 언어("koreana"·"english"·"schinese"…). Steam이 안 떴으면 null.
    // 여기선 기억만 한다 — 적용은 `Loc.RestoreSaved`가 **저장된 언어가 없을 때만** 한다(사용자 선택이 이긴다).
    public static string GameLanguage { get; private set; }

    // Achievements.Unlock만 부른다.
    public static void SetAchievement(string api)
    {
#if !DISABLESTEAMWORKS
        if (!running) return;
        // 이미 깬 사람에게 다시 쏘지 않는다 — Steam이 알림을 또 띄운다.
        if (SteamUserStats.GetAchievement(api, out bool achieved) && achieved) return;
        if (SteamUserStats.SetAchievement(api)) storePending = true;
        else Debug.LogWarning("[Steam] 업적 '" + api + "' 설정 실패 — Steamworks에 그 API 이름이 있는지 확인.");
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor && !RunInEditor) return;
        if (instance != null) return;

        var go = new GameObject("[SteamBootstrap]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SteamBootstrap>();
    }

#if DISABLESTEAMWORKS
    private void Awake() => Destroy(gameObject);
#else
    private void Awake()
    {
        // 🔴 통째로 감싼다 — `steam_api64.dll`이 없거나 백신에 막히면 여기서 DllNotFoundException이 난다.
        //    그걸 놓치면 **게임이 시작부터 죽고**, 그건 심사 확정 반려다. 업적은 없어도 되지만 실행은 돼야 한다.
        try
        {
            if (AppId != 0 && SteamAPI.RestartAppIfNecessary(new AppId_t(AppId)))
            {
                Application.Quit();   // Steam이 우리를 다시 켠다
                return;
            }

            // Steam이 안 떠 있거나 사용자가 소유하지 않았으면 여기서 false. 게임은 그대로 굴러가야 한다.
            running = SteamAPI.Init();
        }
        catch (System.Exception e)
        {
            running = false;
            Debug.LogWarning("[Steam] 초기화 중 예외 — 업적 없이 계속합니다.\n" + e);
        }

        if (!running)
        {
            Debug.LogWarning("[Steam] 초기화 실패 — 업적 없이 계속합니다(Steam 미실행 또는 앱 ID 불일치).");
            enabled = false;
            return;
        }

        GameLanguage = SteamApps.GetCurrentGameLanguage();

        // 실적은 따로 요청하지 않는다 — Steam 클라이언트가 **게임 프로세스가 뜨기 전에** 이미 동기화해 둔다
        // (`RequestCurrentStats`는 최신 SDK에서 없어졌다). 그래서 Init 직후 바로 읽고 쓸 수 있다.
        Achievements.Unlock(Achievements.FirstLaunch);
        Achievements.SyncSave();   // 소급 — 패치 전 세이브·Steam 없이 깬 것
    }

    private void Update()
    {
        if (!running) return;
        SteamAPI.RunCallbacks();
        if (storePending)
        {
            storePending = false;
            SteamUserStats.StoreStats();   // 저장해야 Steam 서버로 올라가고 알림이 뜬다
        }
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        if (!running) return;
        running = false;
        SteamAPI.Shutdown();
    }
#endif
}
