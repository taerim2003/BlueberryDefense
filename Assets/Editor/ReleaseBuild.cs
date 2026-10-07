using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 테스트용 릴리스 빌드 — `/build` 스킬이 MCP로 `ReleaseBuild.Queue()`를 부른다. 절차(zip·노션 업로드)는 `.claude/skills/build`.
//
// 빌드는 몇 분 걸려서 MCP 호출 안에서 끝까지 돌리지 않는다 — `delayCall`로 넘기고 바로 돌아온 뒤,
// 진행은 `Builds/Release/status.txt`에 한 줄씩 적는다. 마지막 줄이 `DONE ...` 또는 `FAILED ...`면 끝난 것이다.
// ⚠️ Queue 전에 `assets-refresh`를 끝내 둘 것 — Queue 뒤에 컴파일이 걸리면 도메인 리로드가 delayCall을 지운다.
//
// QA 빌드(QABuild)와 다른 점: Development 끔 · BOT_QA 없음 · productName 그대로. 즉 사용자가 받는 빌드와 같다.
public static class ReleaseBuild
{
    private static string Root => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "Release");
    public static string OutDir => Path.Combine(Root, "Build");
    public static string StatusPath => Path.Combine(Root, "status.txt");

    public static string Queue()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return "busy — playing=" + EditorApplication.isPlaying + " compiling=" + EditorApplication.isCompiling + " updating=" + EditorApplication.isUpdating;

        Directory.CreateDirectory(Root);
        File.WriteAllText(StatusPath, "queued " + DateTime.Now.ToString("HH:mm:ss") + "\n");
        EditorApplication.delayCall += Run;
        return "queued → " + StatusPath;
    }

    private static void Log(string line) => File.AppendAllText(StatusPath, line + " " + DateTime.Now.ToString("HH:mm:ss") + "\n");

    private static void Run()
    {
        try
        {
            // 지난 빌드를 지우고 뽑는다 — 덮어쓰면 지난 빌드에만 있던 파일이 남아 zip에 섞인다.
            if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true);

            // 현지화 테이블은 Addressables다(QABuild와 같은 이유로 먼저 뽑는다).
            Log("addressables");
            AddressableAssetSettings.BuildPlayerContent(out var addr);
            if (!string.IsNullOrEmpty(addr.Error)) { Log("FAILED addressables: " + addr.Error); return; }

            // 🔴 DX11 고정을 빌드 직전에 API로 다시 건다. 활성 빌드 프로필(`Assets/Settings/Build Profiles/Windows.asset`)이
            //    플레이어 설정 사본을 따로 갖고 `ProjectSettings.asset`을 덮는다 — 거기가 "자동(DX12, DX11)"이라
            //    `ProjectSettings.asset`만 고친 10/6 13:16 빌드가 DX12로 나갔고 10/7에 또 크래시했다. 이 API는 프로필 쪽에 쓴다.
            //    ⚠️ 빌드가 어느 API로 도는지는 `-batchmode`로 재지 말 것(배치모드는 DX12 빌드도 DX11로 뜬다). 창으로 띄운 `Player.log`를 본다.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            Log("gfx auto=" + PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64)
                + " apis=" + string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64)));

            // 스플래시 끔 · 중복 실행 방지(2026-10-07 사용자). 그래픽 API와 같은 이유로 빌드 직전에 API로 건다 —
            // 활성 빌드 프로필이 플레이어 설정 사본을 따로 갖고 있어 `ProjectSettings.asset`만 고치면 빌드에 안 들어간다.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.forceSingleInstance = true;
            Log("splash=" + PlayerSettings.SplashScreen.show + " singleInstance=" + PlayerSettings.forceSingleInstance);

            Log("player");
            var opts = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(OutDir, "BlueberryDefense.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport r = BuildPipeline.BuildPlayer(opts);
            string errs = string.Join(" | ", r.steps.SelectMany(s => s.messages)
                .Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(10));
            if (r.summary.result != BuildResult.Succeeded) { Log("FAILED " + r.summary.result + " errors=" + r.summary.totalErrors + " " + errs); return; }
            Log("DONE result=Succeeded size=" + r.summary.totalSize + " sec=" + (int)r.summary.totalTime.TotalSeconds);
        }
        catch (Exception e) { Log("FAILED exception " + e.Message.Replace("\n", " ")); }
    }
}
