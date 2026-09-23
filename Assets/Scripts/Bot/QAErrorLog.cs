#if UNITY_EDITOR || BOT_QA
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

// 봇 세션 중 나는 Exception·Error·Assert를 `<세션>/errors.jsonl`에 한 줄씩 남긴다. 분석은 `Tools/QA/qa-analyze.js`.
// 에디터 봇 세션(밸런스 루프)에서도 같이 돈다 — 평범하게 플레이할 때 나는 오류도 오류다.
//
// 🔴 `sig`가 같은 오류를 하나로 묶는 키다. 메시지 첫 줄의 **숫자를 지우고**, 스택의 **첫 게임 프레임에서 줄 번호를 뺀 것**을 합쳐 해시한다.
//    숫자를 남기면 "index 3" / "index 4"가 다른 오류로 갈라지고, 줄 번호를 남기면 근처를 고칠 때마다 sig가 바뀌어
//    "고쳤다 → 사라졌다" 판정이 거짓으로 난다.
// 🔴 로그 콜백은 아무 스레드에서나 온다(`logMessageReceivedThreaded`). 큐에만 넣고 파일 쓰기·스크린샷은 Update에서 한다.
public class QAErrorLog : MonoBehaviour
{
    private struct Entry
    {
        public string message, stack, type;
        public string utc;
    }

    private const int MaxWritesPerSigPerRun = 100; // 넘으면 개수만 센다(한 프레임마다 나는 오류가 파일을 채우지 않게)

    private readonly ConcurrentQueue<Entry> queue = new ConcurrentQueue<Entry>();
    private readonly HashSet<string> shotTaken = new HashSet<string>();
    private readonly Dictionary<string, int> runCounts = new Dictionary<string, int>();
    private BotConfig cfg;
    private BotPilot pilot;
    private string path;

    public void Init(BotConfig config, BotPilot owner)
    {
        cfg = config;
        pilot = owner;
        path = Path.Combine(cfg.sessionDir, "errors.jsonl");
        Application.logMessageReceivedThreaded += OnLog;
    }

    private void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= OnLog;
        Flush();
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Log || type == LogType.Warning) return;
        queue.Enqueue(new Entry { message = message, stack = stack, type = type.ToString(), utc = DateTime.UtcNow.ToString("o") });
    }

    private void Update() => Flush();

    // 판 하나에서 난 sig별 개수(runs.jsonl의 qa.errors). 부르면 비운다.
    public Dictionary<string, object> TakeRunCounts()
    {
        Flush();
        var d = runCounts.ToDictionary(k => k.Key, k => (object)k.Value);
        runCounts.Clear();
        return d;
    }

    // 로그를 거치지 않는 판정(멈춤·시간 초과)을 같은 목록에 올린다.
    public void Record(string type, string message)
    {
        queue.Enqueue(new Entry { message = message, stack = "", type = type, utc = DateTime.UtcNow.ToString("o") });
        Flush();
    }

    private void Flush()
    {
        while (queue.TryDequeue(out Entry e)) Write(e);
    }

    private void Write(Entry e)
    {
        string sig = Signature(e.message, e.stack, out string frame);
        runCounts.TryGetValue(sig, out int n);
        runCounts[sig] = ++n;
        if (n > MaxWritesPerSigPerRun) return;

        var d = BotJson.Obj();
        d["utc"] = e.utc;
        d["sig"] = sig;
        d["type"] = e.type;
        d["source"] = e.message.Contains("[Bot]") ? "bot" : e.message.StartsWith("[QA-INV]") ? "invariant" : "game";
        d["message"] = Clip(e.message, 2000);
        d["frame"] = frame;
        d["stack"] = Clip(e.stack, 4000);
        d["kind"] = cfg.kind ?? "editor";
        d["buildId"] = cfg.buildId;
        d["instance"] = cfg.instance;
        d["session"] = Path.GetFileName(cfg.sessionDir);
        Dictionary<string, object> h = pilot != null ? pilot.RunHeader : null;
        d["run"] = h != null && h.TryGetValue("run", out object run) ? run : null;
        d["map"] = h != null && h.TryGetValue("map", out object map) ? map : null;
        d["ascension"] = h != null && h.TryGetValue("ascension", out object asc) ? asc : null;
        d["character"] = h != null && h.TryGetValue("character", out object ch) ? ch : null;
        d["stage"] = GameManager.Instance != null ? GameManager.Instance.CurrentStage : 0;
        d["gameTime"] = pilot != null ? pilot.RunGameTime : 0f;
        d["scene"] = SceneManager.GetActiveScene().name;
        d["timeScale"] = Time.timeScale;
        d["modalPaused"] = ModalPause.IsPaused;
        d["lastActions"] = pilot != null && pilot.Chaos != null ? pilot.Chaos.LastActions : null;

        // 세션 안에서 sig마다 첫 번째만 찍는다. 그래픽 장치가 없으면(-nographics) 찍을 수 없다.
        string shot = null;
        if (shotTaken.Add(sig) && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            shot = "err_" + sig + ".png";
            try { ScreenCapture.CaptureScreenshot(Path.Combine(cfg.sessionDir, shot)); } catch (Exception) { shot = null; }
        }
        d["screenshot"] = shot;

        try { File.AppendAllText(path, BotJson.Write(d) + "\n"); }
        catch (Exception) { } // 여기서 로그를 남기면 자기 자신을 다시 부른다
    }

    private static string Clip(string s, int max) => s == null ? null : s.Length <= max ? s : s.Substring(0, max);

    private static readonly Regex Digits = new Regex(@"\d+");
    private static readonly Regex LineNo = new Regex(@":\d+\)?\s*$");

    // 메시지 첫 줄(숫자 제거) + 첫 게임 프레임(줄 번호 제거).
    public static string Signature(string message, string stack, out string frame)
    {
        string first = (message ?? "").Split('\n')[0].Trim();
        first = Digits.Replace(first, "#");
        if (first.Length > 200) first = first.Substring(0, 200);

        frame = "";
        foreach (string raw in (stack ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("UnityEngine.Debug") || line.StartsWith("UnityEngine.Logger")
                || line.StartsWith("UnityEngine.DebugLogHandler") || line.StartsWith("UnityEngine.StackTraceUtility")) continue;
            frame = LineNo.Replace(line, ")");
            break;
        }

        using (SHA1 sha = SHA1.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(first + "|" + frame));
            return BitConverter.ToString(h, 0, 5).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
