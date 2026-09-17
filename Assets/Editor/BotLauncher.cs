using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 봇 플레이테스트 런처. Claude(루프)가 `BotRuns/request.json`을 쓰면 여기서 받아 플레이모드에 들어간다.
// → 에셋을 파일로만 고쳐 놓아도(Unity 창에 포커스가 없어도) Refresh·컴파일 대기 후 반영된 상태로 판이 돈다.
//
// 🔴 모달 API 금지(unity-mcp 스킬) — 무인 실행에서 대화상자가 뜨면 밤새 멈춘다.
// 🔴 열린 씬은 건드리지 않는다: 시작 씬은 `playModeStartScene`으로만 바꾸고 끝나면 되돌린다.
// 절차·판단은 `.claude/skills/balance-loop`.
[InitializeOnLoad]
public static class BotLauncher
{
    private const string TitleScenePath = "Assets/Scenes/Title.unity";
    private const string CompileDuringPlayPref = "ScriptCompilationDuringPlay"; // Preferences > General > Script Changes While Playing
    private const string PrevCompileKey = "BotLauncher.PrevCompileDuringPlay";
    private const string PrevCompileSavedKey = "BotLauncher.PrevCompileSaved";
    private static double nextPoll;
    private static int refreshWaitPolls = -1;

    static BotLauncher()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2.0;

        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(BotConfig.RequestPath)) { refreshWaitPolls = -1; return; }
        if (File.Exists(BotConfig.ActivePath)) return; // 이전 세션 정리 전
        if (File.Exists(BotConfig.YieldPath)) return;  // 다른 세션이 Unity를 쓰는 중(양보 요청) — 파일이 지워질 때까지 시작하지 않는다

        // ① 에셋·코드 변경을 먼저 반영한다. Refresh가 컴파일을 걸면 도메인 리로드 뒤 이 폴링이 다시 돈다.
        if (refreshWaitPolls < 0)
        {
            AssetDatabase.Refresh();
            refreshWaitPolls = 2;
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { refreshWaitPolls = 2; return; }
        if (refreshWaitPolls-- > 0) return;
        refreshWaitPolls = -1;

        Launch();
    }

    private static void Launch()
    {
        BotConfig cfg;
        try { cfg = JsonUtility.FromJson<BotConfig>(File.ReadAllText(BotConfig.RequestPath)); }
        catch (Exception e)
        {
            Debug.LogError("[BotLauncher] request.json 파싱 실패 — 파일을 BotRuns/request.bad.json 으로 옮김: " + e.Message);
            MoveOver(BotConfig.RequestPath, Path.Combine(BotConfig.RunsRoot, "request.bad.json"));
            return;
        }

        // 양보로 멈춘 세션 이어 돌리기: 원래 세션의 config를 그대로 쓰고 label·resumeFrom만 요청 값으로 바꾼다.
        if (!string.IsNullOrEmpty(cfg.resumeFrom))
        {
            string origPath = Path.Combine(BotConfig.RunsRoot, cfg.resumeFrom, "config.json");
            if (!File.Exists(origPath))
            {
                Debug.LogError("[BotLauncher] 재개할 세션이 없다: " + origPath + " — 요청을 request.bad.json 으로 옮김");
                MoveOver(BotConfig.RequestPath, Path.Combine(BotConfig.RunsRoot, "request.bad.json"));
                return;
            }
            BotConfig orig = JsonUtility.FromJson<BotConfig>(File.ReadAllText(origPath));
            orig.resumeFrom = cfg.resumeFrom;
            orig.runAudit = false;
            if (!string.IsNullOrEmpty(cfg.label)) orig.label = cfg.label;
            cfg = orig;
        }

        string label = string.IsNullOrEmpty(cfg.label) ? "run" : cfg.label;
        foreach (char c in Path.GetInvalidFileNameChars()) label = label.Replace(c, '_');
        cfg.sessionDir = Path.Combine(BotConfig.RunsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + label);
        Directory.CreateDirectory(cfg.sessionDir);
        Directory.CreateDirectory(Path.GetDirectoryName(BotConfig.ActivePath));

        File.WriteAllText(Path.Combine(cfg.sessionDir, "config.json"), JsonUtility.ToJson(cfg, true));
        File.WriteAllText(Path.Combine(cfg.sessionDir, "fingerprint.json"), Fingerprint());
        if (cfg.runAudit) BotCooldownAudit.Write(Path.Combine(cfg.sessionDir, "cooldowns.json"));
        File.WriteAllText(BotConfig.ActivePath, JsonUtility.ToJson(cfg, true));
        File.Delete(BotConfig.RequestPath);

        if (cfg.mode == "audit")
        {
            File.WriteAllText(Path.Combine(cfg.sessionDir, "status.json"), "{\"state\":\"done\",\"mode\":\"audit\"}");
            File.Delete(BotConfig.ActivePath);
            Debug.Log("[BotLauncher] 쿨 감사만 기록: " + cfg.sessionDir);
            return;
        }

        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScenePath);
        // 🔴 세션 도중 누가 .cs를 저장해도 **플레이가 끝난 뒤에** 재컴파일되게 한다. 기본값(재컴파일 후 계속 플레이)이면
        //    도메인 리로드로 봇·게임의 static이 날아가 판이 멈춘다(2026-09-18 실제로 멈췄다). 끝나면 원래 값으로 되돌린다.
        if (!SessionState.GetBool(PrevCompileSavedKey, false))
        {
            SessionState.SetInt(PrevCompileKey, EditorPrefs.GetInt(CompileDuringPlayPref, 0));
            SessionState.SetBool(PrevCompileSavedKey, true);
        }
        EditorPrefs.SetInt(CompileDuringPlayPref, 1); // 0 계속 플레이 · 1 플레이 끝난 뒤 · 2 플레이 중단 후
        Debug.Log("[BotLauncher] 봇 세션 시작: " + cfg.sessionDir);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode) return;
        if (!File.Exists(BotConfig.ActivePath)) return;

        // 봇 세션이 끝났다(정상 종료든 사람이 멈췄든). 시작 씬·세이브 프로필·재컴파일 설정을 원래대로.
        EditorSceneManager.playModeStartScene = null;
        if (SessionState.GetBool(PrevCompileSavedKey, false))
        {
            EditorPrefs.SetInt(CompileDuringPlayPref, SessionState.GetInt(PrevCompileKey, 0));
            SessionState.SetBool(PrevCompileSavedKey, false);
        }
        SaveStore.UseProfile(null);
        Time.captureDeltaTime = 0f;

        BotConfig cfg = null;
        try { cfg = JsonUtility.FromJson<BotConfig>(File.ReadAllText(BotConfig.ActivePath)); } catch (Exception) { }
        File.Delete(BotConfig.ActivePath);

        if (cfg == null || string.IsNullOrEmpty(cfg.sessionDir)) return;
        string statusPath = Path.Combine(cfg.sessionDir, "status.json");
        string text = File.Exists(statusPath) ? File.ReadAllText(statusPath) : "";
        // 봇이 done/error를 못 쓰고 끝났으면(사람이 플레이를 끔, 크래시) 중단으로 표시해 루프가 기다리지 않게 한다.
        if (!text.Contains("\"state\":\"done\"") && !text.Contains("\"state\":\"error\"") && !text.Contains("\"state\":\"yielded\""))
            File.WriteAllText(statusPath, "{\"state\":\"aborted\",\"note\":\"플레이모드가 봇 종료 처리 없이 끝났다\",\"heartbeatUtc\":\""
                + DateTime.UtcNow.ToString("o") + "\"}");
        Debug.Log("[BotLauncher] 봇 세션 종료: " + cfg.sessionDir);
    }

    // 밸런스에 영향을 주는 파일의 해시. 세션 사이에 무엇이 바뀌었는지 분석기가 diff한다.
    private static string Fingerprint()
    {
        string root = BotConfig.ProjectRoot;
        var files = Directory.GetFiles(Path.Combine(root, "Assets", "Data"), "*.asset", SearchOption.AllDirectories)
            .Concat(new[]
            {
                Path.Combine(root, "Assets", "SkillTree", "MainSkillTree.asset"),
                Path.Combine(root, "Assets", "Scripts", "BalanceConstants.cs"),
                Path.Combine(root, "Assets", "Scripts", "SkillTreeData.cs"),
                Path.Combine(root, "Assets", "Scripts", "PlayerSkills.cs"),
                Path.Combine(root, "Assets", "Scripts", "PlayerPassives.cs"),
                Path.Combine(root, "Assets", "Scripts", "Enemy.cs"),
            })
            .Where(File.Exists)
            .OrderBy(p => p, StringComparer.Ordinal);

        var sb = new StringBuilder("{");
        bool first = true;
        using (SHA1 sha = SHA1.Create())
        {
            foreach (string f in files)
            {
                string rel = f.Substring(root.Length + 1).Replace('\\', '/');
                string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(f))).Replace("-", "").Substring(0, 12).ToLowerInvariant();
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(rel).Append("\":\"").Append(hash).Append('"');
            }
        }
        return sb.Append('}').ToString();
    }

    private static void MoveOver(string from, string to)
    {
        if (File.Exists(to)) File.Delete(to);
        File.Move(from, to);
    }

    // ── 사람이 직접 돌릴 때 ──
    [MenuItem("Window/Blueberry Defense/Bot Playtest/스모크 1판 요청")]
    private static void RequestSmoke()
    {
        Directory.CreateDirectory(BotConfig.RunsRoot);
        var cfg = new BotConfig { label = "smoke", mode = "campaign", campaigns = 1, maxAttemptsPerGoal = 1, maxRunsPerCampaign = 1 };
        File.WriteAllText(BotConfig.RequestPath, JsonUtility.ToJson(cfg, true));
        Debug.Log("[BotLauncher] 요청 작성: " + BotConfig.RequestPath);
    }

    [MenuItem("Window/Blueberry Defense/Bot Playtest/쿨 감사만")]
    private static void RequestAudit()
    {
        Directory.CreateDirectory(BotConfig.RunsRoot);
        File.WriteAllText(BotConfig.RequestPath, JsonUtility.ToJson(new BotConfig { label = "audit", mode = "audit" }, true));
    }

    [MenuItem("Window/Blueberry Defense/Bot Playtest/세션 중단")]
    private static void Stop()
    {
        if (File.Exists(BotConfig.RequestPath)) File.Delete(BotConfig.RequestPath);
        if (EditorApplication.isPlaying && File.Exists(BotConfig.ActivePath)) EditorApplication.isPlaying = false;
    }
}
