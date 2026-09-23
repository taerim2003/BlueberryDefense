#if UNITY_EDITOR || BOT_QA
using System;
using System.IO;
using UnityEngine;

// 봇 세션 설정. Claude(루프)가 `BotRuns/request.json`으로 쓰고, BotLauncher가 세션 폴더를 붙여
// `BotRuns/active/session.json`으로 옮긴 뒤 플레이모드에 들어간다. JsonUtility 형식이라 필드 이름이 곧 JSON 키다.
// 목표 수치·밸런스 판단은 여기 두지 않는다 — `Tools/BotPlaytest/targets.json`과 `balance` 스킬 소관.
[Serializable]
public class BotGoal
{
    public string map;      // MapDefinition 에셋 이름 (Map_BlueberryField / Map_Wide15 / Map_Wide20)
    public int ascension;   // 1 쉬움 · 2 보통 · 3 어려움
}

// 양보로 멈춘 세션을 이어가기 위한 진행 상태(세션 폴더의 resume.json). 판 하나가 **끝날 때마다** 저장한다 —
// 판 중간에 멈추면 그 판은 버리고 마지막 저장 지점부터 다시 한다.
[Serializable]
public class BotFirstClear
{
    public int goal, attempts, cumRuns, cumPicks, nodesOwned;
    public float cumGameTime;
}

[Serializable]
public class BotResumeState
{
    public string mode;
    public string campaignKeyPrefix;   // 최초 세션 id — 여러 세션에 걸친 캠페인을 분석기가 하나로 묶는 키
    public int nextProbeIndex;
    public int campaign;
    public bool campaignStarted;
    public int[] attempts;
    public int runs, cumPicks;
    public float cumGame;
    public string lastChar;
    public System.Collections.Generic.List<BotFirstClear> firstClears = new System.Collections.Generic.List<BotFirstClear>();
    public bool allNodesReached;
    public int allNodesRuns, allNodesPicks;
    public float allNodesGame;
}

[Serializable]
public class BotConfig
{
    public string label = "run";
    // campaign = 빈 세이브에서 정주행 · probe = 고정 트리에서 목표×캐릭터 측정 · audit = 쿨 감사만
    public string mode = "campaign";

    // ── campaign ──
    public int campaigns = 1;
    public int maxAttemptsPerGoal = 30;
    public int maxRunsPerCampaign = 200;
    // 목표 순서 = 사용자가 정한 난이도 서열(해금 순서와 호환). 비면 DefaultGoals.
    public BotGoal[] goals;
    // 캐릭터 순환 순서(에셋 이름). 해금된 것만 판마다 돌아가며 쓴다.
    public string[] characters = { "Char_Strawberry", "Char_Pineapple", "Char_Slot3" };

    // ── probe ──
    public float[] probeTreeRatios = { 0.3f, 0.6f };   // 보유 노드 레벨 비율(싼 것부터 삼). 1 = 풀트리
    public int probeRuns = 1;
    // 🔴 0 = 안 돈다 (2026-09-20 사용자 결정). 풀트리 판은 "아무 캐릭터나 집었을 때 깰 확률"을 재는데,
    //    게임은 **한 번만 깨면 되는 것**이라 그 평균이 의미가 없다. 정주행은 캐릭터를 돌아가며 시도하다
    //    한 명이라도 깨면 멈추므로 실제 플레이와 같은 질문을 묻는다 — 난이도는 정주행으로만 본다.
    //    ⚠️ 이 값을 되살리면 G4(풀트리 클리어율)가 다시 측정되지만, 캐릭터 셋 중 하나만 깨는 상태에서는
    //    정주행보다 항상 낮게 나온다(그게 낮아 보이던 이유이지 버그가 아니다).
    public int fullTreeRuns = 0;
    public string[] probeGoalFilter;                  // 비면 9개 전부. "Map_Wide20:3" 형식

    // ── 공통 ──
    public bool runAudit = true;          // 세션 시작에 쿨 감사(cooldowns.json)를 같이 뽑는다
    public float simStep = 1f / 30f;      // Time.captureDeltaTime — 프레임당 게임 시간 고정
    public int seed = 1234;
    public float stuckRealSeconds = 60f;  // 이 시간 동안 진행이 없으면 멈춘 것으로 본다
    public float maxRunRealSeconds = 3600f;

    // 양보(yield)로 멈춘 세션을 이어서 돌릴 때 그 세션 폴더 이름. 런처가 원래 config를 불러와 이 값만 얹는다.
    public string resumeFrom;

    // 런처가 채운다
    public string sessionDir;

    // ── QA 빌드(`Tools/QA/qa-runner.js`가 채운다 — 에디터 세션에서는 비어 있다) ──
    public string runsRoot;               // 세션 폴더들의 부모. resumeFrom을 여기서 찾는다. 비면 <프로젝트>/BotRuns
    public string kind;                   // "balance"(정책 그대로) | "chaos"(엣지 케이스 행동을 섞는다)
    public string instance;               // 러너의 인스턴스 번호 — 세이브 프로필 이름(save_qa_<instance>.json)
    public string buildId;                // QARuns/builds/<buildId> — runs·errors에 그대로 찍힌다
    public bool chaos;                    // BotChaos를 켠다
    public float chaosMinInterval = 3f;   // 전투 중 행동 간격(실시간 초)
    public float chaosMaxInterval = 20f;
    public bool skipEnding = true;        // false면 엔딩 연출까지 탄다(chaos)
    public bool qaSelfTest;               // 오류 수집 대조군: 세션 첫 판에 일부러 예외 1개·LogError 1개를 낸다
    public bool captureSelectScreens;     // 캐릭터·맵 선택 화면을 판마다 찍는다(sel_<화면>_<가로>x<세로>_<판>.png) — 해상도별 UI 확인용
    // 렉 원인 분리 실험(perf): 같은 시드로 하나씩 꺼 보고 렉이 사라지는지 본다. 평소 세션에서는 false.
    public bool noDamageNumbers;
    public bool noHitParticles;
    public int impactVfxPerFrame;        // 충돌 이펙트 프레임당 상한 덮어쓰기(0=게임 기본값 8). 상한별 화면 비교용
    public bool qaLightweight;            // 1초 검사·렉 상세 기록을 끈다(계측기 자신이 렉의 원인인지 가르기)

    public static readonly BotGoal[] DefaultGoals =
    {
        new BotGoal { map = "Map_BlueberryField", ascension = 1 },
        new BotGoal { map = "Map_BlueberryField", ascension = 2 },
        new BotGoal { map = "Map_Wide15", ascension = 1 },
        new BotGoal { map = "Map_Wide15", ascension = 2 },
        new BotGoal { map = "Map_Wide20", ascension = 1 },
        new BotGoal { map = "Map_BlueberryField", ascension = 3 },
        new BotGoal { map = "Map_Wide20", ascension = 2 },
        new BotGoal { map = "Map_Wide15", ascension = 3 },
        new BotGoal { map = "Map_Wide20", ascension = 3 },
    };

    public BotGoal[] Goals => goals != null && goals.Length > 0 ? goals : DefaultGoals;

    // ── 경로 ──
    public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
    // QA 빌드에서는 BotPilot.Boot이 설정 파일의 runsRoot/sessionDir로 덮어쓴다(빌드 폴더엔 BotRuns가 없다).
    public static string RunsRootOverride;
    public static string YieldPathOverride;
    public static string RunsRoot => RunsRootOverride ?? Path.Combine(ProjectRoot, "BotRuns");
    public static string ActivePath => Path.Combine(RunsRoot, "active", "session.json");
    public static string RequestPath => Path.Combine(RunsRoot, "request.json");
    // 🔴 교통정리: 다른 세션이 Unity가 필요하면 이 파일을 만든다. 봇은 판 경계에서 멈추고(내용에 "now"가 있으면 즉시)
    //    진행 상태를 resume.json에 남긴 채 Unity를 비운다. 쓰고 나면 만든 쪽이 지운다 → 루프가 이어서 돌린다.
    //    QA 빌드에서는 세션 폴더의 `stop` 파일이다(러너가 새 빌드로 갈아탈 때·마감 때 쓴다).
    public static string YieldPath => YieldPathOverride ?? Path.Combine(RunsRoot, "yield");

    public static BotConfig LoadActive()
    {
        if (!File.Exists(ActivePath)) return null;
        return LoadFrom(ActivePath);
    }

    public static BotConfig LoadFrom(string path)
    {
        try { return JsonUtility.FromJson<BotConfig>(File.ReadAllText(path)); }
        catch (Exception e) { Debug.LogWarning("[Bot] 설정 읽기 실패: " + path + " — " + e.Message); return null; }
    }
}
#endif
