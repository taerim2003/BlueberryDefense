using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 2차 진화 22칸을 자동으로 한 장씩 찍고 **수치까지 같이 뽑는** 검수 리그.
//
// 🔴 왜 에디터 코드인가: 플레이모드를 **프레임에 걸쳐** 몰아야 하는데 `script-execute`는 호출마다
//    독립 어셈블리라 프레임 간 상태를 못 넘긴다(unity-mcp 스킬). `EditorApplication.update`는 플레이 중에도 돈다.
//    런타임 어셈블리에 두면 빌드에 딸려 나가므로 Assets/Editor에 둔다.
// 🔴 **플레이모드 진입은 도메인 리로드를 부른다 — static이 전부 날아간다.**
//    그래서 Start()는 **이미 플레이 중일 때만** 건다. 진입은 바깥에서 따로 한다.
//
// v2에서 고친 것(1차 촬영에서 드러난 결함):
//   ① 진화가 **누적**돼 앞 칸 연출이 계속 화면에 남았다 → 칸마다 모든 스킬의 PathTier를 0으로 되돌리고
//      상주 설치물(우주선·회오리 생성기)을 파괴한다.
//   ② 레벨업 패널이 **페이드 도중**에 찍혀 반투명 글자가 유령처럼 남았다 → 모달을 닫고 충분히 기다린다.
//   ③ 벙커·마크처럼 **수명이 짧은 VFX**가 촬영 전에 사라졌다 → 촬영 직전 프레임에 발동시킨다.
public static class EvoCaptureRig
{
    public struct Shot
    {
        public ActiveSkillId Id; public int Route; public string Name;
        public Shot(ActiveSkillId id, int route, string name) { Id = id; Route = route; Name = name; }
    }

    public static readonly Shot[] Shots =
    {
        new Shot(ActiveSkillId.BasicAttack, 0, "01_블루베리사냥꾼"),
        new Shot(ActiveSkillId.BasicAttack, 1, "02_하늘파쇄기"),
        new Shot(ActiveSkillId.Whirlwind,   0, "03_회오리생성기"),
        new Shot(ActiveSkillId.Whirlwind,   1, "04_하늘의울음"),
        new Shot(ActiveSkillId.Orb,         0, "05_초대형오브"),
        new Shot(ActiveSkillId.Orb,         1, "06_저글러"),
        new Shot(ActiveSkillId.Lightning,   0, "07_초대형축적번개"),
        new Shot(ActiveSkillId.Lightning,   1, "08_제우스의은총"),
        new Shot(ActiveSkillId.EagleDrop,   0, "09_슈퍼다이너마이트독수리"),
        new Shot(ActiveSkillId.EagleDrop,   1, "10_독수리의비"),
        new Shot(ActiveSkillId.Sniping,     0, "11_독수리특공대지휘관"),
        new Shot(ActiveSkillId.Sniping,     1, "12_사이버네틱벙커"),
        new Shot(ActiveSkillId.Homing,      0, "13_초강력슈퍼로켓"),
        new Shot(ActiveSkillId.Homing,      1, "14_저하늘의별처럼"),
        new Shot(ActiveSkillId.Shotgun,     0, "15_내지휘를따라"),
        new Shot(ActiveSkillId.Shotgun,     1, "16_초강력섬멸용전탄발사"),
        new Shot(ActiveSkillId.Rewind,      0, "17_과충전"),
        new Shot(ActiveSkillId.Rewind,      1, "18_블루베리절멸의시간"),
        new Shot(ActiveSkillId.Swing,       0, "19_로열팔라딘의망치"),
        new Shot(ActiveSkillId.Swing,       1, "20_거대한파도"),
        new Shot(ActiveSkillId.GrapeToss,   0, "21_재앙의역병"),
        new Shot(ActiveSkillId.GrapeToss,   1, "22_메두사의간식"),
    };

    // 칸마다 "이 스프라이트가 화면에 있어야 한다"는 기대치. 비어 있으면 그림 검사를 건너뛴다.
    private static readonly Dictionary<string, string> Expect = new Dictionary<string, string>
    {
        { "02_하늘파쇄기", "skyshredder" },
        { "03_회오리생성기", "Effect_TornadoMaker" },
        { "04_하늘의울음", "Effect_SuperTornado" },
        { "05_초대형오브", "Effect_HugeOrb" },
        { "06_저글러", "Effect_Juggler" },
        { "07_초대형축적번개", "Effect_BigThunder_" },
        { "08_제우스의은총", "Effect_Jeus" },
        { "09_슈퍼다이너마이트독수리", "Effect_SuperEagle" },
        { "11_독수리특공대지휘관", "Effect_SnipingMark" },
        { "12_사이버네틱벙커", "Bunker_" },
        { "13_초강력슈퍼로켓", "Effect_SuperMissile" },
        { "16_초강력섬멸용전탄발사", "fullburst" },
        { "19_로열팔라딘의망치", "Effect_PaladinHammer" },
        { "20_거대한파도", "Effect_Wave_" },
        { "21_재앙의역병", "Sprite_GrapeBombGreen" },
        { "22_메두사의간식", "Effect_GrapeBombElectric" },
    };

    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;

    private static string outDir, logPath;
    private static int index, phase, frames, waited;
    private static bool running;
    private static readonly StringBuilder report = new StringBuilder();

    public static string Status() => "running=" + running + " index=" + index + "/" + Shots.Length
        + " phase=" + phase + " frames=" + frames;

    public static string Report() => report.ToString();

    private static void Trace(string s)
    {
        report.AppendLine(s);
        try { if (logPath != null) File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss") + " " + s + "\n"); } catch { }
    }

    public static string Start(string directory)
    {
        if (!EditorApplication.isPlaying) return "FAIL: 플레이 중이 아니다. 진입을 먼저 하고 다시 부를 것(도메인 리로드로 static이 날아간다).";
        if (running) return "이미 실행 중 — " + Status();
        outDir = directory;
        Directory.CreateDirectory(outDir);
        logPath = Path.Combine(outDir, "_progress.log");
        try { File.WriteAllText(logPath, "시작 " + DateTime.Now + "\n"); } catch { }
        report.Clear();
        index = 0; phase = 1; frames = 0; waited = 0; running = true;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        return "시작함 → " + outDir;
    }

    public static void Stop(string why)
    {
        running = false;
        EditorApplication.update -= Tick;
        Trace("종료: " + why);
    }

    private static object GetF(object o, string n) { var f = o.GetType().GetField(n, F); return f == null ? null : f.GetValue(o); }
    private static void SetF(object o, string n, object v) { var f = o.GetType().GetField(n, F); if (f != null) f.SetValue(o, v); }
    private static object Call(object o, string n, params object[] a) { var m = o.GetType().GetMethod(n, F); return m == null ? null : m.Invoke(o, a); }

    private static bool DismissModals()
    {
        bool any = false;
        for (int i = 0; i < 8; i++)
        {
            var b = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .FirstOrDefault(x => x != null && x.isActiveAndEnabled && x.interactable);
            if (b == null) break;
            b.onClick.Invoke(); any = true;
        }
        return any;
    }

    private static void ForceEvolve(PlayerSkills ps, ActiveSkillId id, int route, int stage)
    {
        var skill = ps.EquippedSkills.FirstOrDefault(s => s.Id == id);
        if (skill == null) return;
        int path = EvolutionRoutes.RoutePath(id, route);
        var apply = typeof(PlayerSkills).GetMethod("ApplyPathTierEffect", FS);
        for (int tier = 1; tier <= stage; tier++)
            foreach (int legacy in EvolutionRoutes.LegacyTiersFor(tier))
            { skill.PathTier[path] = legacy; apply.Invoke(null, new object[] { skill, path, legacy }); }
        skill.PathTier[path] = EvolutionRoutes.TargetPathTier(stage);
        skill.Route = route; skill.EvolutionStage = stage;
    }

    // 앞 칸의 연출이 다음 칸에 남지 않게 전부 되돌린다.
    private static void ResetAll(PlayerSkills ps)
    {
        // 🔴 **슬롯을 비운다.** 액티브 슬롯은 4칸뿐이라(SlotKeys) 그냥 쌓으면 5번째부터
        //    `AcquireSkill`이 `HasMaxSkills`에 걸려 **조용히 아무것도 안 한다** — 3차 촬영에서
        //    06번 이후 10칸이 통째로 빈 화면으로 찍힌 원인이 이것이었다.
        var field = typeof(PlayerSkills).GetField("equippedSkills", F);
        if (field != null)
        {
            var list = field.GetValue(ps) as System.Collections.IList;
            if (list != null) list.Clear();
        }

        foreach (var s in ps.EquippedSkills)
        {
            for (int i = 0; i < s.PathTier.Length; i++) s.PathTier[i] = 0;
            s.Route = -1; s.EvolutionStage = 0; s.CooldownTimer = 9999f;
        }
        // 상주 설치물은 PathTier를 내려도 이미 만들어진 오브젝트가 남는다 — 직접 지운다.
        var ship = typeof(PlayerSkills).GetField("skyShredderShip", F);
        if (ship != null)
        {
            var go = ship.GetValue(ps) as GameObject;
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            ship.SetValue(ps, null);
        }
        var maker = typeof(PlayerSkills).GetField("tornadoMaker", F);
        if (maker != null)
        {
            var go = maker.GetValue(ps) as GameObject;
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            maker.SetValue(ps, null);
        }
        var until = typeof(PlayerSkills).GetField("tornadoMakerUntil", F);
        if (until != null) until.SetValue(ps, 0f);

        // 떠 있는 이펙트 오브젝트도 정리(피뢰침·회오리·오브 등)
        foreach (var w in UnityEngine.Object.FindObjectsByType<Whirlwind>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (w != null) UnityEngine.Object.DestroyImmediate(w.gameObject);
        foreach (var o in UnityEngine.Object.FindObjectsByType<Orb>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (o != null) UnityEngine.Object.DestroyImmediate(o.gameObject);
    }

    private static void Tick()
    {
        if (!running) return;
        frames++;
        if (frames > 200000) { Stop("상한 프레임 초과 — 무한 루프 방지"); return; }   // 종료 조건(CLAUDE.md §4)
        if (!EditorApplication.isPlaying) { Stop("플레이모드가 꺼졌다"); return; }

        try
        {
            switch (phase)
            {
                case 1: DriveTitle(); break;
                case 2: WaitBattle(); break;
                case 3: Prepare(); break;
                case 4: Settle(); break;
                case 5: ShootNow(); break;
            }
        }
        catch (Exception e) { Trace("예외: " + e.Message); Stop("예외"); }
    }

    private static void DriveTitle()
    {
        var title = UnityEngine.Object.FindAnyObjectByType<TitleController>();
        if (title == null || SceneManager.GetActiveScene().name != "Title") return;

        (GetF(title, "playButton") as Button).onClick.Invoke();
        var cs = GetF(title, "characterSelect") as CharacterSelectUI;
        var chars = GetF(cs, "characters") as CharacterDefinition[];
        Call(cs, "Pick", Array.FindIndex(chars, c => c != null && c.name == "Char_Pineapple"));
        (GetF(cs, "confirmButton") as Button).onClick.Invoke();
        var ms = GetF(title, "mapSelect") as MapSelectUI;
        Call(ms, "Select", 0);
        SetF(ms, "ascensionLevel", 0);
        Call(ms, "RefreshAscension");
        var start = GetF(ms, "startButton") as Button;
        if (start != null && start.interactable) start.onClick.Invoke();
        Trace("타이틀 흐름 통과(파인애플 · 밭)");
        phase = 2; waited = 0;
    }

    private static void WaitBattle()
    {
        if (++waited > 900) { Stop("전투 로드 대기 초과"); return; }
        if (UnityEngine.Object.FindAnyObjectByType<PlayerSkills>() == null) return;
        if (SceneManager.GetActiveScene().name != "Battle") return;
        Trace("전투 로드됨");
        phase = 3; waited = 0;
    }

    private static void Prepare()
    {
        var ps = UnityEngine.Object.FindAnyObjectByType<PlayerSkills>();
        if (ps == null) { Stop("PlayerSkills 사라짐"); return; }
        DismissModals();

        var xp = UnityEngine.Object.FindAnyObjectByType<PlayerExperience>();
        if (xp != null) xp.enabled = false;
        var spawner = UnityEngine.Object.FindAnyObjectByType<EnemySpawner>();
        if (spawner != null) spawner.enabled = true;

        ResetAll(ps);

        var shot = Shots[index];
        ps.AcquireSkill(shot.Id);
        ForceEvolve(ps, shot.Id, shot.Route, 2);

        // 🔴 낙뢰는 **스스로 피해를 주지 않는다** — 다른 공격이 적을 때려야 낙뢰가 발동한다.
        //    낙뢰만 들려 두면 초대형 번개가 영영 안 뜬다(3·4차 촬영에서 07번이 계속 비었던 이유).
        if (shot.Id == ActiveSkillId.Lightning) ps.AcquireSkill(ActiveSkillId.Swing);

        foreach (var s in ps.EquippedSkills) s.CooldownTimer = 0f;
        BotInput.HoldSkills = true;

        Trace(shot.Name + " 준비");
        phase = 4; waited = 0;
    }

    // 칸마다 "발동 후 몇 프레임 뒤에 찍을까". 짧은 연출은 그 연출이 화면에 있는 순간을 노린다.
    private static int FireDelayFor(Shot shot)
    {
        if (shot.Id == ActiveSkillId.EagleDrop && shot.Route == 0) return 18;  // 슈퍼 독수리 강하 0.55초 중간
        if (shot.Id == ActiveSkillId.Sniping && shot.Route == 0) return 20;    // 마크 그리는 0.5초 중간
        if (shot.Id == ActiveSkillId.Sniping && shot.Route == 1) return 12;    // 벙커 1.2초
        if (shot.Id == ActiveSkillId.Homing && shot.Route == 0) return 14;     // 로켓이 날아가는 중
        if (shot.Id == ActiveSkillId.Lightning && shot.Route == 0) return 10;  // 초대형 번개(쿨 0.5초)
        return 30;
    }

    private static void Settle()
    {
        DismissModals();
        KeepAlive();
        if (++waited < 150) return;      // 연출이 자리를 잡고 모달 페이드가 끝날 시간
        phase = 5; waited = 0;
    }

    // 🔴 플레이어가 죽으면 씬이 바뀌어 리그가 통째로 끊긴다(1차에 17칸에서 멈췄다). 매 프레임 체력을 채운다.
    private static void KeepAlive()
    {
        var hp = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
        if (hp == null) return;
        var heal = hp.GetType().GetMethod("Heal", F);
        if (heal != null && hp.CurrentHealth < hp.MaxHealth)
        {
            var pars = heal.GetParameters();
            if (pars.Length == 1) heal.Invoke(hp, new object[] { Convert.ChangeType(hp.MaxHealth, pars[0].ParameterType) });
        }
    }

    private static void ShootNow()
    {
        DismissModals();
        KeepAlive();
        var shot = Shots[index];
        var ps = UnityEngine.Object.FindAnyObjectByType<PlayerSkills>();
        if (ps == null) { Stop("PlayerSkills 사라짐"); return; }

        // waited == 0 프레임에 **발동**시키고, FireDelay 프레임 뒤에 찍는다.
        if (waited == 0)
        {
            foreach (var s in ps.EquippedSkills) if (s.Id == shot.Id) s.CooldownTimer = 0f;

            if (shot.Id == ActiveSkillId.Sniping && shot.Route == 1)
            {
                var bReady = typeof(PlayerSkills).GetField("bunkerReadyAt", F);
                if (bReady != null) bReady.SetValue(ps, 0f);   // 자체 쿨(5초)을 풀어 확실히 발동시킨다
                var hp = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
                if (hp != null) hp.TakeDamage(1);
            }
            if (shot.Id == ActiveSkillId.Lightning && shot.Route == 0)
            {
                // 초대형 번개는 **20스택**이 조건이라 짧은 촬영에선 절대 안 뜬다 — 스택을 채워 준다.
                LightningStorm.StackingEnabled = true;
                for (int i = 0; i < LightningStorm.HugeBoltStackThreshold + 2; i++) LightningStorm.AddStack(30f);
            }
            if (shot.Id == ActiveSkillId.Rewind)
                foreach (var s in ps.EquippedSkills) if (s.Id != ActiveSkillId.Rewind) s.CooldownTimer = 0f;
        }

        // 팔라딘 망치는 내려찍는 프레임에 고정해서 찍는다
        var anim = ps.GetComponent<Animator>();
        if (anim != null)
        {
            if (shot.Id == ActiveSkillId.Swing && shot.Route == 0)
            { anim.speed = 0f; anim.Play("Attack", 0, 0.62f); anim.Update(0f); }
            else anim.speed = 1f;
        }

        waited++;
        // 🔴 **고정 프레임 대기로는 못 잡는다.** 에디터 프레임률이 들쭉날쭉해서 짧은 연출(마크 0.5초·로켓·독수리 강하)이
        //    이미 끝난 뒤를 찍게 된다(3·4차 촬영에서 11·13번이 계속 비었던 이유).
        //    → **기대한 그림이 화면에 나타나는 순간** 찍는다. 최소 대기는 연출이 자리를 잡을 만큼만 준다.
        if (waited < FireDelayFor(shot)) return;
        string want;
        if (Expect.TryGetValue(shot.Name, out want))
        {
            bool present = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                           .Any(s => s.sprite != null && s.sprite.name.Contains(want));
            if (!present)
            {
                // 아직 안 나왔으면 계속 쏘게 두고 기다린다. 상한에 닿으면 그냥 찍고(빈 화면으로) 넘어간다.
                foreach (var s in ps.EquippedSkills) s.CooldownTimer = 0f;
                if (waited < 900) return;
            }
        }

        MeasureAndLog(shot);
        string path = Path.Combine(outDir, shot.Name + ".png");
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);

        index++;
        if (index >= Shots.Length) { Stop("22칸 완료"); return; }
        phase = 3; waited = 0;
    }

    // 🔴 화면만 보고 판정하지 않는다 — 기대한 그림이 **실제로 씬에 있는지**와 화면 안에 들어오는지를 값으로 같이 남긴다.
    private static void MeasureAndLog(Shot shot)
    {
        var sb = new StringBuilder();
        sb.Append(shot.Name).Append(" : ");
        string key;
        if (!Expect.TryGetValue(shot.Name, out key)) { sb.Append("(기대 그림 없음 — 스탯/HUD형)"); Trace(sb.ToString()); return; }

        var cam = Camera.main;
        float halfW = cam.orthographicSize * cam.aspect, halfH = cam.orthographicSize;
        float left = cam.transform.position.x - halfW, right = cam.transform.position.x + halfW;
        float bottom = cam.transform.position.y - halfH, top = cam.transform.position.y + halfH;

        // 캐릭터 발밑 y — "바닥면이 캐릭터 밑면과 같아야" 하는 칸들의 기준선이다.
        float footY = float.NaN;
        var psRef = UnityEngine.Object.FindAnyObjectByType<PlayerSkills>();
        if (psRef != null)
        {
            var anim = psRef.GetComponent<Animator>();
            var bodySr = anim != null ? anim.GetComponent<SpriteRenderer>() : psRef.GetComponentInChildren<SpriteRenderer>();
            if (bodySr != null) footY = bodySr.bounds.min.y;
        }

        var hits = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                   .Where(s => s.sprite != null && s.sprite.name.Contains(key)).ToArray();
        if (hits.Length == 0) { sb.Append("✘ 그림 없음 (" + key + ") 발밑y=" + footY.ToString("0.00")); Trace(sb.ToString()); return; }

        var r = hits[0];
        var b = r.bounds;
        bool inside = b.max.x <= right + 0.01f && b.min.x >= left - 0.01f && b.max.y <= top + 0.01f && b.min.y >= bottom - 0.01f;
        sb.Append("✔ " + r.sprite.name + " x" + hits.Length)
          .Append(" 크기=").Append(b.size.x.ToString("0.0")).Append("x").Append(b.size.y.ToString("0.0"))
          .Append(" 범위x=").Append(b.min.x.ToString("0.0")).Append("~").Append(b.max.x.ToString("0.0"))
          .Append(" y=").Append(b.min.y.ToString("0.0")).Append("~").Append(b.max.y.ToString("0.0"))
          .Append(" 화면x=").Append(left.ToString("0.0")).Append("~").Append(right.ToString("0.0"))
          .Append(" y=").Append(bottom.ToString("0.0")).Append("~").Append(top.ToString("0.0"))
          .Append(" order=").Append(r.sortingOrder)
          .Append(" 발밑y=").Append(footY.ToString("0.00"))
          .Append(" 바닥차=").Append((b.min.y - footY).ToString("+0.00;-0.00"))
          .Append(inside ? " [화면안]" : " [잘림]");
        Trace(sb.ToString());
    }
}
