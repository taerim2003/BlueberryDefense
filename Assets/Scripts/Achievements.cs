using System.Collections.Generic;
using UnityEngine;

// Steam 업적의 단일 창구. 게임 코드는 여기만 부르고, Steamworks API는 SteamBootstrap만 만진다.
//
// 🔴 설계 원칙 — **엔딩까지 정상 진행하면 전부 깨진다.** 노려야 하는 업적·놓칠 수 있는 업적·수집 100%를 넣지 않는다.
//    누적 기준값(처치 수·진화 종 수)은 봇 정주행 50회의 **최솟값보다 낮게** 잡았다(2026-09-22 실측:
//    엔딩까지 처치 최소 20,633 · 중앙 35,495 / 진화 발견 최소 14 · 중앙 23).
//
// 🔴 API 이름은 Steamworks > 실적 페이지에 **이 문자열 그대로** 등록해야 한다. 한 글자만 달라도 조용히 무시된다.
//
// 소급 판정: 세이브로 알 수 있는 것은 SyncSave가, 에셋이 있어야 알 수 있는 것은 SyncTree(트리)·SyncCharacters(캐릭터)가
// 매번 다시 훑는다. 그래서 패치 전에 진행한 세이브도, Steam이 꺼진 채 깬 것도 다음 실행에 받는다.
public static class Achievements
{
    public const string FirstLaunch = "ACH_FIRST_LAUNCH";
    public const string FirstRun = "ACH_FIRST_RUN";
    public const string Treasure = "ACH_TREASURE";
    public const string MaxLevel = "ACH_MAX_LEVEL";
    public const string FullSlots = "ACH_FULL_SLOTS";
    public const string Evolve1 = "ACH_EVOLVE_1";
    public const string Evolve2 = "ACH_EVOLVE_2";
    public const string EvolveMany = "ACH_EVOLVE_N";
    public const string Ending = "ACH_ENDING";            // 블루베리 군집체 처치
    public const string TreeFirst = "ACH_TREE_FIRST";
    public const string TreeEvo = "ACH_TREE_EVO";
    public const string TreeSkills = "ACH_TREE_SKILLS";
    public const string TreeAll = "ACH_TREE_ALL";
    public const string Kills1 = "ACH_KILLS_1";
    public const string Kills2 = "ACH_KILLS_2";

    private const int EvolveManyCount = 10;
    private const int Kills1Count = 1000;
    private const int Kills2Count = 10000;

    // 키 = 맵 에셋 이름(MapClearSave와 같은 키). 값 = 쉬움·보통·어려움 순.
    private static readonly Dictionary<string, string[]> MapClears = new Dictionary<string, string[]>
    {
        { "Map_BlueberryField", new[] { "ACH_CLEAR_FARM_EASY", "ACH_CLEAR_FARM_NORMAL", "ACH_CLEAR_FARM_HARD" } },
        { "Map_Wide15", new[] { "ACH_CLEAR_COAST_EASY", "ACH_CLEAR_COAST_NORMAL", "ACH_CLEAR_COAST_HARD" } },
        { "Map_Wide20", new[] { "ACH_CLEAR_SPACE_EASY", "ACH_CLEAR_SPACE_NORMAL", "ACH_CLEAR_SPACE_HARD" } },
    };

    // 키 = 캐릭터 에셋 이름. 처음부터 열린 캐릭터(딸기)는 없다.
    private static readonly Dictionary<string, string> CharacterUnlocks = new Dictionary<string, string>
    {
        { "Char_Pineapple", "ACH_UNLOCK_PINEAPPLE" },
        { "Char_Slot3", "ACH_UNLOCK_GRAPE" },
    };

    private const string RunsKey = "stats.runs";
    private const string KillsKey = "stats.kills";
    private const string EndingKey = "stats.ending";

    // 이번 실행에서 이미 보낸 것. Steam이 이미 깬 업적은 SteamBootstrap이 한 번 더 걸러낸다.
    private static readonly HashSet<string> sent = new HashSet<string>();

    // 이번 판 처치 수. 처치마다 파일에 쓰지 않고 판이 끝날 때 한 번 더한다.
    private static int runKills;

    public static void Unlock(string api)
    {
        if (!sent.Add(api)) return;
        if (SteamBootstrap.Running) SteamBootstrap.SetAchievement(api);
        else Debug.Log("[ACH] " + api + " (Steam 꺼짐 — 기록만)");
    }

    // ── 전투 중 ──
    public static void OnEnemyKilled(bool isTreasure)
    {
        runKills++;
        if (isTreasure) Unlock(Treasure);
    }

    // ── 판 종료(승패 무관, 판마다 1회) ──
    public static void OnRunEnd()
    {
        SaveStore.SetInt(RunsKey, SaveStore.GetInt(RunsKey, 0) + 1);
        SaveStore.SetInt(KillsKey, SaveStore.GetInt(KillsKey, 0) + runKills);
        SaveStore.Save();
        runKills = 0;
        SyncSave();
    }

    // 엔딩: 블루베리 군집체를 쓰러뜨린 순간.
    public static void OnEndingBossKilled()
    {
        SaveStore.SetInt(EndingKey, 1);
        SaveStore.Save();
        Unlock(Ending);
    }

    // ── 세이브만으로 판정되는 것 (Steam 초기화 직후 · 판 종료 · 맵 클리어 기록 후) ──
    public static void SyncSave()
    {
        if (SaveStore.GetInt(RunsKey, 0) > 0) Unlock(FirstRun);

        int kills = SaveStore.GetInt(KillsKey, 0);
        if (kills >= Kills1Count) Unlock(Kills1);
        if (kills >= Kills2Count) Unlock(Kills2);

        foreach (var kv in MapClears)
        {
            int cleared = MapClearSave.ClearedAscension(kv.Key);
            for (int i = 0; i < kv.Value.Length && i < cleared; i++) Unlock(kv.Value[i]);
        }

        // 패치 전에 엔딩을 본 세이브엔 EndingKey가 없다 — 우주 어려움 클리어 기록으로 대신 판정한다
        // (클리어는 군집체 등장 **전에** 기록되지만, 거기서 끄는 경우는 무시할 만큼 드물다).
        if (SaveStore.GetInt(EndingKey, 0) == 1 || MapClearSave.HasCleared("Map_Wide20", 3)) Unlock(Ending);

        if (CollectionSave.CountEvolutions(1) > 0) Unlock(Evolve1);
        if (CollectionSave.CountEvolutions(2) > 0) Unlock(Evolve2);
        if (CollectionSave.CountEvolutions(1) >= EvolveManyCount) Unlock(EvolveMany);
    }

    // ── 스킬트리 (판 시작 · 노드 구매 후) ──
    public static void SyncTree(SkillTreeData tree)
    {
        if (tree == null) return;
        var levels = SkillTreeSave.Levels();
        bool any = false, all = true, allSkills = true;
        foreach (SkillNode n in tree.nodes)
        {
            levels.TryGetValue(n.id, out int saved);
            int lv = SkillTreeSave.EffectiveLevel(n, saved);
            if (lv > 0) any = true;
            if (lv < SkillTreeSave.MaxLevelOf(n)) all = false;
            if (n.type == SkillNodeType.SkillUnlock && lv < 1) allSkills = false;
        }
        if (any) Unlock(TreeFirst);
        if (levels.TryGetValue("New_Evolution2", out int evo2) && evo2 >= 1) Unlock(TreeEvo);
        if (allSkills) Unlock(TreeSkills);
        if (all) Unlock(TreeAll);
    }

    // ── 캐릭터 해금 (타이틀에 들어올 때마다) ──
    public static void SyncCharacters(CharacterDefinition[] characters)
    {
        if (characters == null) return;
        foreach (var c in characters)
            if (c != null && c.IsUnlocked && CharacterUnlocks.TryGetValue(c.name, out string api)) Unlock(api);
    }

    // ── 스킬 (판 중) ──
    public static void OnSkillMaxLevel() => Unlock(MaxLevel);
    public static void OnActiveSlotsFull() => Unlock(FullSlots);
    public static void OnEvolved(int tier)
    {
        Unlock(tier >= 2 ? Evolve2 : Evolve1);
        if (CollectionSave.CountEvolutions(1) >= EvolveManyCount) Unlock(EvolveMany);
    }
}
