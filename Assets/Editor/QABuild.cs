using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

// QA 빌드. `QARuns/build.request` 파일이 생기면 여기서 받아 `QARuns/builds/<buildId>/`에 플레이어를 뽑는다.
// 뽑힌 빌드는 `Tools/QA/qa-runner.js`가 여러 개 띄워 봇을 돌린다. 절차는 `.claude/skills/qa-loop`.
//
// 🔴 **릴리스 빌드와 섞이지 않게** 세 가지를 바꾼 채로만 뽑는다:
//    · `extraScriptingDefines = BOT_QA` — 봇 코드가 들어간다. 프로젝트 define은 안 건드려서 에디터가 재컴파일하지 않는다.
//    · `productName = BlueberryDefense_QA` — LocalLow 폴더·레지스트리 키가 따로 생긴다. 세이브·해상도·옵션 초기화가
//      사용자의 진짜 빌드(과 Steam Cloud 사본)에 닿을 길이 없다. 끝나면 **finally에서 되돌린다.**
//    · `Development` — 스택 트레이스에 줄 번호가 찍혀야 오류를 짚을 수 있다.
// 🔴 봇 세션(BotRuns/active)이 돌고 있으면 `BotRuns/yield`를 쓰고 비워질 때까지 기다린다 — 빌드는 에디터를 오래 붙잡는다.
//    빌드가 끝나면 **자기가 쓴 yield만** 지운다(다른 세션이 쓴 것은 건드리지 않는다).
// 🔴 모달 API 금지(unity-mcp 스킬) — 무인 실행에서 대화상자가 뜨면 밤새 멈춘다.
[InitializeOnLoad]
public static class QABuild
{
    private const string QAProductName = "BlueberryDefense_QA";
    private const string YieldTag = "qa-build";
    private const int KeepBuilds = 3;
    private static double nextPoll;
    private static bool refreshed;
    private static int refreshWaitPolls;

    public static string QARoot => Path.Combine(BotConfig.ProjectRoot, "QARuns");
    private static string RequestPath => Path.Combine(QARoot, "build.request");
    private static string StatusPath => Path.Combine(QARoot, "build.status.json");
    private static string BuildsRoot => Path.Combine(QARoot, "builds");

    static QABuild()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 5.0;
        if (!File.Exists(RequestPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        // 봇 세션이 Unity를 쓰는 중 — 판 경계에서 비켜 달라고 하고 기다린다.
        if (File.Exists(BotConfig.ActivePath))
        {
            if (!File.Exists(BotConfig.YieldPath))
            {
                File.WriteAllText(BotConfig.YieldPath, YieldTag + " · QA 빌드");
                WriteStatus("waiting", null, "봇 세션이 판을 끝내고 비킬 때까지 대기");
            }
            return;
        }

        // 🔴 빌드 전에 디스크 변경을 반영한다. 에디터에 포커스가 없으면 자동 임포트가 안 돌아서, 고친 셰이더·에셋이
        //    **옛 임포트 결과로** 빌드에 들어간다(스크립트는 빌드가 다시 컴파일하지만 셰이더·에셋은 아니다).
        //    Refresh가 컴파일을 걸면 도메인 리로드 뒤 이 폴링이 다시 돈다 — 요청 파일은 그때까지 남겨 둔다.
        if (!refreshed)
        {
            AssetDatabase.Refresh();
            refreshed = true;
            refreshWaitPolls = 1;
            return;
        }
        if (refreshWaitPolls-- > 0) return;
        refreshed = false;

        // 요청 파일에 "nodev"가 들어 있으면 Development를 끄고 뽑는다(프로파일러 연결이 프레임을 끊는지 가르는 실험용).
        bool dev = true;
        try { dev = !File.ReadAllText(RequestPath).Contains("nodev"); } catch (Exception) { }
        try { File.Delete(RequestPath); } catch (Exception) { }
        try { Build(dev); }
        finally
        {
            if (File.Exists(BotConfig.YieldPath) && File.ReadAllText(BotConfig.YieldPath).StartsWith(YieldTag))
                File.Delete(BotConfig.YieldPath);
        }
    }

    [MenuItem("Window/Blueberry Defense/QA/QA 빌드 요청")]
    private static void Request()
    {
        Directory.CreateDirectory(QARoot);
        File.WriteAllText(RequestPath, "menu " + DateTime.Now.ToString("o"));
        Debug.Log("[QABuild] 요청 작성: " + RequestPath + " — 5초 안에 시작한다(봇 세션이 돌고 있으면 비킨 뒤)");
    }

    private static void Build(bool development = true)
    {
        string fingerprint = BotLauncher.Fingerprint();
        string fpShort;
        using (SHA1 sha = SHA1.Create())
            fpShort = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint))).Replace("-", "").Substring(0, 6).ToLowerInvariant();
        string buildId = DateTime.Now.ToString("yyyyMMdd-HHmm") + "_" + fpShort + (development ? "" : "_nodev");
        string outDir = Path.Combine(BuildsRoot, buildId);
        Directory.CreateDirectory(outDir);
        WriteStatus("building", buildId, null);
        var clock = Stopwatch.StartNew();

        // 현지화 테이블은 Addressables다. 플레이어 빌드가 같이 빌드하는지는 사용자 환경설정에 달려 있어서 직접 먼저 뽑는다.
        AddressableAssetSettings.BuildPlayerContent(out var addrResult);
        if (!string.IsNullOrEmpty(addrResult.Error))
        {
            Fail(buildId, outDir, "Addressables 빌드 실패: " + addrResult.Error);
            return;
        }

        string prevProduct = PlayerSettings.productName;
        // Frame Timing Stats를 켜야 빌드에서 CPU·GPU·제출 대기 시간이 채워진다(QAFrameStats). QA 빌드에만 켜고 finally에서 되돌린다.
        bool prevFrameTiming = PlayerSettings.enableFrameTimingStats;
        BuildReport report;
        try
        {
            PlayerSettings.productName = QAProductName;
            PlayerSettings.enableFrameTimingStats = true;
            var opts = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(outDir, "BlueberryDefense.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = new[] { "BOT_QA" },
            };
            report = BuildPipeline.BuildPlayer(opts);
        }
        finally
        {
            PlayerSettings.productName = prevProduct;
            PlayerSettings.enableFrameTimingStats = prevFrameTiming;
        }

        if (report.summary.result != BuildResult.Succeeded)
        {
            // ⚠️ 스크립트 컴파일 실패는 steps.messages가 비어 있고 result만 Unknown으로 온다 — 그때는 에디터 로그 꼬리를 같이 싣는다
            //    (2026-09-23: "Unknown — "만 남아서 원인을 로그에서 따로 찾아야 했다. 원인은 새 .cs가 아직 임포트 전이었던 것).
            string errs = string.Join("\n", report.steps.SelectMany(s => s.messages)
                .Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(20));
            if (string.IsNullOrWhiteSpace(errs)) errs = "(빌드 리포트에 메시지 없음 — 대개 스크립트 컴파일 실패다) " + EditorLogTail(12);
            Fail(buildId, outDir, report.summary.result + " · 오류 " + report.summary.totalErrors + "건 — " + errs);
            return;
        }

        var sb = new StringBuilder("{");
        sb.Append("\"buildId\":\"").Append(buildId).Append("\",");
        sb.Append("\"fingerprintShort\":\"").Append(fpShort).Append("\",");
        sb.Append("\"builtUtc\":\"").Append(DateTime.UtcNow.ToString("o")).Append("\",");
        sb.Append("\"durationSec\":").Append((int)clock.Elapsed.TotalSeconds).Append(',');
        sb.Append("\"unity\":\"").Append(Application.unityVersion).Append("\",");
        sb.Append("\"gitHead\":\"").Append(Git("rev-parse HEAD").Trim()).Append("\",");
        sb.Append("\"gitDirtyFiles\":").Append(Git("status --porcelain").Split('\n').Count(l => l.Trim().Length > 0)).Append(',');
        sb.Append("\"fingerprint\":").Append(fingerprint);
        sb.Append('}');
        File.WriteAllText(Path.Combine(outDir, "build.json"), sb.ToString());
        File.WriteAllText(Path.Combine(BuildsRoot, "latest.txt"), buildId);
        WriteStatus("done", buildId, null);
        Debug.Log("[QABuild] 완료: " + outDir + " (" + (int)clock.Elapsed.TotalSeconds + "초)");
        Prune();
    }

    // 에디터 로그의 마지막 오류 줄들(빌드 리포트가 비었을 때의 유일한 단서).
    private static string EditorLogTail(int lines)
    {
        try
        {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");
            using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                var keep = new Queue<string>();
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Contains("error CS") || line.Contains("Exception"))
                    {
                        keep.Enqueue(line);
                        if (keep.Count > lines) keep.Dequeue();
                    }
                }
                return string.Join(" | ", keep);
            }
        }
        catch (Exception e) { return "로그 읽기 실패: " + e.Message; }
    }

    private static void Fail(string buildId, string outDir, string error)
    {
        WriteStatus("failed", buildId, error);
        Debug.LogError("[QABuild] 실패: " + error);
        try { Directory.Delete(outDir, true); } catch (Exception) { }
    }

    // 성공한 빌드(build.json이 있는 폴더)를 최근 KeepBuilds개만 남긴다. 러너가 아직 쓰는 폴더는 잠겨 있어 지우기가 실패하는데, 그러면 둔다.
    private static void Prune()
    {
        var dirs = Directory.GetDirectories(BuildsRoot)
            .Where(d => File.Exists(Path.Combine(d, "build.json")))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).Skip(KeepBuilds);
        foreach (string d in dirs)
        {
            try { Directory.Delete(d, true); }
            catch (Exception e) { Debug.LogWarning("[QABuild] 옛 빌드 삭제 보류(사용 중?): " + d + " — " + e.Message); }
        }
    }

    private static void WriteStatus(string state, string buildId, string note)
    {
        Directory.CreateDirectory(QARoot);
        string esc(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
        File.WriteAllText(StatusPath, "{\"state\":" + esc(state) + ",\"buildId\":" + esc(buildId) + ",\"note\":" + esc(note)
            + ",\"utc\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
    }

    private static string Git(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = BotConfig.ProjectRoot,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                return o;
            }
        }
        catch (Exception) { return ""; }
    }
}
