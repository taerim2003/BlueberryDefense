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
