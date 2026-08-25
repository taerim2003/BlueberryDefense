using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// 게임의 모든 UI를 "캐릭터/맵 선택 화면" 스타일로 맞추는 도구.
// 그 두 화면이 스타일의 원본이라 여기 상수는 전부 거기서 실측해 뽑은 값이다(세션41).
//
// 사용자 피드백으로 톤을 바꿀 때 **여기 위쪽 토큰만 고치고 메뉴를 다시 돌리면** 전 화면이 따라온다.
// 씬을 손으로 고치면 다음 실행에 되돌아가므로, 예외를 두고 싶으면 Skip 목록에 넣을 것.
public static class UISkinApply
{
    // ── 스킨 토큰 (원본: Title 씬 CharacterSelectRoot / MapSelectRoot) ──
    public static readonly Color Skin      = Hex("6B7BE8"); // 판·버튼 바탕색(블루베리 보라파랑)
    public static readonly Color Highlight = Hex("FFE04D"); // 선택 프레임·꺾쇠
    public static readonly Color DimRgb    = Hex("08050D"); // 전체 화면 딤의 색조
    public const float FullDimAlpha = 0.961f;               // 뒤가 안 보여도 되는 화면(F5)

    // ── 박스 그림 (세션41에 UI_*_Unclean에서 전면 교체. 테두리가 더 얇은 새 세트) ──
    // `_색칠` = 속이 흰색이라 색을 곱해 쓰는 판 / `_투명` = 속이 비어 덮어씌우는 테두리.
    // 옆 숫자는 원본 크기와 가로세로 비 — **칸의 비율에 가장 가까운 것**을 골라야 9-slice가 덜 늘어난다.
    const string SBarWide = "가로길쭉길쭉이_색칠";        // 561x145 (3.87) 아주 넓은 버튼
    const string SBar     = "가로길쭉이_색칠";            // 361x103 (3.50) 기본 버튼·이름표
    const string SBarShort= "가로안길쭉이_색칠";          // 227x118 (1.92) 짧고 통통한 버튼
    const string SPillow  = "베개같이생긴네모_색칠";      // 373x195 (1.91) 중간 판
    const string SBigBox  = "개큰네모_색칠";              // 721x289 (2.49) 넓은 판
    const string SSquare  = "정사각형_색칠";              // 372x372 (1.00) 정사각 판
    const string SAngular = "각진정사각형_색칠";          // 203x221 (0.92) 세로로 선 작은 판
    const string SIconBox = "스킬아이콘하기좋은네모_색칠"; // 143x141 (1.01) 아이콘 칸
    const string SRound   = "동그라미_색칠";              // 145x135 (1.07) 작고 둥근 버튼
    const string SGauge   = "경치바_색칠";                // 967x81 (11.94) 경험치 바
    const string SHealth  = "체력바_색칠";                // 387x101 (3.83) 체력 바
    const string SSolidFill = "UI_SolidFill";             // 게이지 채움용 단색 1장 (Multiple이라 LoadSub로 집는다)
    // 게이지 3겹 — 2026-08-25에 사용자가 그려 넣은 세트. 바탕(_색칠) → 채움(_내용물) → 테두리(_투명) 순으로 겹친다.
    const string SHealthFill  = "체력바_내용물";
    const string SHealthOuter = "체력바_투명";
    const string OSquare  = "정사각형_투명";              // 속 빈 테두리 — 선택 하이라이트
    const string OPillow  = "베개같이생긴네모_투명";

    // UI 그림은 2026-08-24에 Assets/Sprites/UI/ 로 모았다(29장 = 위 틀 22 + UI_* 5 + IconFrame/IconMask).
    // 스킬 아이콘(Icon_*)과 이펙트(Effect_*)는 루트에 그대로다 — 그쪽 경로는 SkillIconLibraryBake/EvolutionIconWiring가 따로 갖는다.
    static string SpritePath(string name) => "Assets/Sprites/UI/" + name + ".png";

    const string PixelFont  = "Assets/Fonts/pixelroborobo SDF.asset";
    const string BodyFont   = "Assets/Fonts/Pretendard-Bold SDF.asset";
    // 글자 머티리얼 — 23pt 이상/이하로 갈린다(세션38에 만든 두 벌).
    const string MatBig     = " - WhitePurple";
    const string MatSmall   = " - WhitePurple Small";
    const int    SmallCut   = 23;

    struct Target
    {
        public string path;
        public string sprite;   // null이면 딤(스프라이트 없이 색만)
        public float alpha;     // 딤 전용. <0 = 전체 딤(FullDimAlpha)
        public Color tint;      // 기본은 Skin. 게이지 트랙처럼 어두워야 하는 것만 따로 준다
        public bool back;       // 형제 맨 앞으로 = 뒤에 깔린다
    }

    static Target S(string path, string sprite) =>
        new Target { path = path, sprite = sprite, alpha = -1f, tint = Skin };
    static Target S(string path, string sprite, Color tint) =>
        new Target { path = path, sprite = sprite, alpha = -1f, tint = tint };
    static Target Back(string path, string sprite, Color tint) =>
        new Target { path = path, sprite = sprite, alpha = -1f, tint = tint, back = true };
    static Target D(string path, float alpha) =>
        new Target { path = path, sprite = null, alpha = alpha, tint = Skin };
    static Target DFull(string path) =>
        new Target { path = path, sprite = null, alpha = -1f, tint = Skin };

    // 게이지 트랙(체력·경험치)은 판이 아니라 홈이라 어둡게 깔고 그 위에 막대가 찬다.
    static readonly Color TrackTint = Hex("2A2440");

    // ── 무엇을 무엇으로 (경로는 씬 루트부터) ──
    static readonly Target[] TitleTargets =
    {
        // 🔴 그림은 비율이 아니라 **속이 얼마나 남나**로 고른다. 칸 높이 90에서는 어떤 그림도 42pt 라벨을 못 담는다
        //    (제일 얇은 경치바조차 속 36). 가로길쭉길쭉이는 속이 14px이라 글자가 테두리를 28px 침범하고 있었다
        //    = 8/24 플레이스루의 "텍스트 위아래 여백 부족". 아래 TitleRectFixes가 칸을 340x112로 잡아 속 281x54를 만든다.
        S("Canvas/Btn_플레이",     SBar),       // 361x103, 테두리 세로 58 → 340x112 칸에서 속 281x54
        S("Canvas/Btn_업그레이드", SBar),
        S("Canvas/Btn_컬렉션",     SBar),
        S("Canvas/Btn_설정",       SBar),
        S("Canvas/Btn_종료",       SBar),

        DFull("Canvas/SkillTreeRoot"),
        S("Canvas/SkillTreeRoot/CloseButton",        SBarShort), // 120x56 (2.14)
        S("Canvas/SkillTreeRoot/Tooltip",            SBigBox),   // 400x150 (2.67)
        S("Canvas/SkillTreeRoot/UnlockPoster/Panel", SAngular),  // 230x280 (0.82)

        // ── 캐릭터 선택 ──
        S("Canvas/CharacterSelectRoot/CardContainer/CardTemplate/Bg",     SSquare),  // 240x246
        S("Canvas/CharacterSelectRoot/CardContainer/CardTemplate/Border", OSquare),  // 그림이 테두리를 넘지 않게 덮는 겹
        // 🔴 호버 표시는 **카드 뒤에 까는 노란 판**이다. _투명 그림은 순수 검정이라 색을 곱해도
        //    노랗게 물들지 않는다(옛 UI_CornerBracket은 흰 부분이 있어 물들었다).
        Back("Canvas/CharacterSelectRoot/CardContainer/CardTemplate/Frame", SSquare, Highlight),
        S("Canvas/CharacterSelectRoot/CardContainer/CardTemplate/NameBox", SBar),    // 200x54
        S("Canvas/CharacterSelectRoot/BackButton",      SBar),      // 185x54
        S("Canvas/CharacterSelectRoot/CharacterHeader", SBar),      // 300x87
        S("Canvas/CharacterSelectRoot/SkillIconBox",    SIconBox),  // 130x134
        S("Canvas/CharacterSelectRoot/SelectButton",    SBar),      // 267x78
        // 460x308 (1.49)에 베개(373x195)를 쓰면 가로 1.23·세로 1.58배 **확대**라 테두리가 뭉갠다.
        // 개큰네모(721x289)면 가로는 축소·세로만 1.07배라 확대량이 거의 없다 — 비율은 조금 멀어져도 이쪽이 낫다.
        S("Canvas/CharacterSelectRoot/SkillBox",        SBigBox),   // 460x308 (1.49)

        // ── 맵 선택 ──
        S("Canvas/MapSelectRoot/MainPanel",                             SPillow),  // 1590x1010 (1.57)
        S("Canvas/MapSelectRoot/MainPanel/StageTab",                    SBar),     // 261x76
        S("Canvas/MapSelectRoot/MainPanel/CardContainer/CardTemplate/Bg",      SPillow), // 340x228
        S("Canvas/MapSelectRoot/MainPanel/CardContainer/CardTemplate/Border",  OPillow),
        Back("Canvas/MapSelectRoot/MainPanel/CardContainer/CardTemplate/Frame", SPillow, Highlight),
        S("Canvas/MapSelectRoot/MainPanel/CardContainer/CardTemplate/NameBox", SBar),
        S("Canvas/MapSelectRoot/MainPanel/AscensionRow",         SPillow),  // 460x308
        S("Canvas/MapSelectRoot/MainPanel/AscensionRow/AscPrev", SRound),   // 54x56
        S("Canvas/MapSelectRoot/MainPanel/AscensionRow/AscNext", SRound),
        S("Canvas/MapSelectRoot/MainPanel/StartButton", SBar),
        S("Canvas/MapSelectRoot/MainPanel/MapHeader",   SBar),
        S("Canvas/MapSelectRoot/MainPanel/BackButton",  SBar),
        S("Canvas/MapSelectRoot/ChangeCharButton",      SBar),
    };

    static readonly Target[] GameTargets =
    {
        D("Canvas/LevelUpPanel", 0.70f),
        S("Canvas/LevelUpPanel/Dialog",                SSquare),   // 1060x840 (1.26)
        S("Canvas/LevelUpPanel/Dialog/DamageButton",   SBarWide),  // 900x170 (5.29)
        S("Canvas/LevelUpPanel/Dialog/CooldownButton", SBarWide),
        S("Canvas/LevelUpPanel/Dialog/HealthButton",   SBarWide),
        S("Canvas/LevelUpPanel/Dialog/RerollButton",   SBarWide),  // 340x70

        // 게이지 둘은 전용 그림이 따로 왔다. 막대가 테두리를 덮지 않도록 채움을 안쪽으로 밀어 넣는다.
        S("Canvas/HUD/HealthPanel", SHealth, TrackTint), // 320x54
        S("Canvas/HUD/ExpBar",      SGauge,  TrackTint), // 900x40

        // ⚠️ Window는 1920×1080 전체 화면이다 — 판 스프라이트를 입히면 테두리가 화면 가장자리에 붙고
        //    게임 화면이 통째로 가려진다. 선택 화면처럼 **딤은 딤으로 두고 노드만 판**으로 세운다.
        D("Canvas/EvolutionPanel",        0.55f),
        D("Canvas/EvolutionPanel/Window", 0.75f),

        D("Canvas/DamageMeterPanel", 0.70f),
        S("Canvas/DamageMeterPanel/Dialog",              SSquare),  // 780x700 (1.11)
        S("Canvas/DamageMeterPanel/ReturnToTitleButton", SBar),     // 260x66

        D("Canvas/TreasurePanel", 0.82f),
    };

    // localScale로 키운 UI는 픽셀이 통째로 늘어나 테두리가 뭉갠다(FitSlice가 최종 크기를 못 본다).
    // 여백은 sizeDelta로 내고 스케일은 1로 돌린다. 위 RectFix가 크기를 잡은 **뒤에** 돌아야 한다.
    static readonly string[] ScaleResetPaths =
    {
        "Canvas/MapSelectRoot/MainPanel/CardContainer/CardTemplate/Border",
    };

    static void ResetScale(GameObject go, StringBuilder sb)
    {
        if (go == null) return;
        var rt = (RectTransform)go.transform;
        if (Mathf.Abs(rt.localScale.x - 1f) < 0.001f && Mathf.Abs(rt.localScale.y - 1f) < 0.001f) return;
        Undo.RecordObject(rt, "UI Skin");
        sb.AppendLine("  스케일 " + go.name + " " + rt.localScale.x.ToString("0.00") + "," + rt.localScale.y.ToString("0.00") + " → 1,1");
        rt.localScale = Vector3.one;
        EditorUtility.SetDirty(rt);
    }

    // 선택 하이라이트의 네 모서리 꺾쇠(UI_CornerBracket) — 새 세트엔 대응 그림이 없다.
    // 속 빈 테두리(_투명) 한 겹이 그 역할을 하므로 **끄기만** 한다(지우지 않아 되돌릴 수 있다).
    static readonly string[] CornerBracketParents =
    {
        "Canvas/CharacterSelectRoot/CardContainer/CardTemplate/Frame",
        "Canvas/MapSelectRoot/MainPanel/CardContainer/CardTemplate/Frame",
    };

    // 게이지: 채움(Filled) 자식을 테두리 두께만큼 안으로 밀어 넣는다.
    static readonly string[] GaugePaths = { "Canvas/HUD/HealthPanel", "Canvas/HUD/ExpBar" };

    // 진화 노드 6칸은 이름이 규칙적이라 표에 늘어놓지 않고 접두사로 잡는다.
    const string EvoNodePrefix = "Canvas/EvolutionPanel/Window/Node_";

    // 판을 손그림 스프라이트로 갈면 **테두리(좌우 50·위 55·아래 45px)만큼 안쪽이 좁아진다.**
    // 창 크기를 그대로 두면 제목이 위 테두리에 잘리고 아래 버튼이 창 밖으로 튀어나온다
    // (실제로 레벨업 "레벨 업!"과 "다시 뽑기"가 그랬다). 안에 든 것을 옮기는 대신 창을 그만큼 키운다.
    struct RectFix
    {
        public string path; public Vector2 size; public Vector2 pos; public bool movePos;
        public RectFix(string p, float w, float h) { path = p; size = new Vector2(w, h); pos = Vector2.zero; movePos = false; }
        public RectFix(string p, float w, float h, float x, float y)
        { path = p; size = new Vector2(w, h); pos = new Vector2(x, y); movePos = true; }
    }

    // 호버 하이라이트 판은 카드보다 그만큼 커야 테두리처럼 삐져나온다.
    // 새 그림엔 투명 여백이 8%쯤 있어서(정사각형_색칠은 372 중 29px) 여유를 더 준다.
    static readonly RectFix[] TitleRectFixes =
    {
        // ── 캐릭터 카드: 145x149는 너무 작아 초상화가 109x113로 쪼그라들어 있었다
        //    (플레이스루 지적 "호버 하이라이트가 지나치게 두껍고 이미지가 너무 작음").
        //    CardContainer(880폭)에 HorizontalLayoutGroup spacing=56이라 3장이면 240폭까지 들어간다
        //    (240*3 + 56*2 = 832 ≤ 880). 정사각형_색칠 원본이 372x372라 240x246은 여전히 **축소**다.
        //    Bg 안쪽 = 가로 240-37-33=170 · 세로 246-20-66=160 → 그 칸을 초상화가 그대로 쓴다.
        //    🔴 Bg·Thumb·Border·Frame은 **stretch 앵커**(0,0)-(1,1)라 sizeDelta가 크기가 아니라
        //       **카드 대비 여백**이다. Bg·Border는 0,0이라 카드만 키우면 저절로 따라온다 — 표에 올리지 않는다.
        //    카드가 커진 만큼 세로 자리를 만들어야 한다. CharacterHeader 하단 356 ~ SkillIconBox 상단 97 =
        //    259px뿐인데 카드(246) + 이름표(4+54)가 304px이라 그대로 두면 위아래가 겹친다.
        //    카드 블록을 헤더 바로 아래(상단 350)에 놓고 아래 셋을 그만큼 내린다.
        //    이름표(NameBox)는 카드 **아래로 걸쳐 나오는** 띠라(앵커 하단·pivot 위) 카드 높이에 그 60px이 더 붙는다.
        //    카드 블록의 실제 바닥 = 카드하단 - 4 - 60. 그 아래 셋을 차례로 밀어 1px도 안 겹치게 맞췄다.
        new RectFix("Canvas/CharacterSelectRoot/CardContainer",  880, 250,  0,  227),
        new RectFix("Canvas/CharacterSelectRoot/SkillIconBox",   130, 134,  0,  -40),
        new RectFix("Canvas/CharacterSelectRoot/SkillBox",       460, 308,  0, -265),
        new RectFix("Canvas/CharacterSelectRoot/SelectButton",   267,  78,  0, -470),
        new RectFix("Canvas/CharacterSelectRoot/CardContainer/CardTemplate",        240, 246),
        // 초상화는 Bg 테두리 안쪽(170x160)에 딱 맞춘다: 여백 = 170-240, 160-246. 중심은 안쪽 중심(2, 23).
        new RectFix("Canvas/CharacterSelectRoot/CardContainer/CardTemplate/Bg/Thumb", -70, -86, 2, 23),
        // NameBox는 앵커가 카드 하단(0.5,0)·pivot 위쪽이라 카드가 커져도 저절로 하단에 붙는다 — 크기만 키운다.
        // 가로길쭉이는 아래 그림자 띠가 두꺼워(ppu 보정 후 22px) 54면 글자가 들어갈 안쪽이 21px뿐이다 — 60으로.
        new RectFix("Canvas/CharacterSelectRoot/CardContainer/CardTemplate/NameBox", 200,  60),
        // 호버 판은 카드 뒤에 깔린다. 여백 49 → rect 289, 그림의 투명 여백 8%를 빼면 보이는 판이 266이라
        // 카드 밖으로 13px만 나오는 얇은 테두리가 된다. 옛 70(=rect 215, 밖으로 26px)이 "두껍다"는 지적을 받았다.
        new RectFix("Canvas/CharacterSelectRoot/CardContainer/CardTemplate/Frame", 49, 49),
        new RectFix("Canvas/MapSelectRoot/MainPanel/CardContainer/CardTemplate/Frame", 74, 70),
        // 이 Border는 카드보다 살짝 커야 테두리처럼 보이는데, 그 여백을 **localScale 1.04/1.06으로** 내고 있었다.
        // 스케일은 픽셀을 통째로 늘려 9-slice 재단을 무력화한다 — 같은 여백을 sizeDelta로 낸다(340x228 → 354x242).
        // (회의록 "모든 UI 패널·버튼의 스케일 변경 금지". UI 전체를 훑어 localScale != 1인 건 이 하나뿐이었다.)
        new RectFix("Canvas/MapSelectRoot/MainPanel/CardContainer/CardTemplate/Border", 14, 14),

        // 메인 메뉴 5칸: 440x90 → 340x112. 폭은 "가로로 지나치게 길다"를, 높이는 "위아래 여백 부족"을 푼다.
        // 세로 간격 130(틈 18). 위로는 TitleImage 아래끝 174에서 18px, 아래로는 화면 바닥까지 64px 남는다.
        new RectFix("Canvas/Btn_플레이",     340, 112, 0,  100),
        new RectFix("Canvas/Btn_업그레이드", 340, 112, 0,  -30),
        new RectFix("Canvas/Btn_컬렉션",     340, 112, 0, -160),
        new RectFix("Canvas/Btn_설정",       340, 112, 0, -290),
        new RectFix("Canvas/Btn_종료",       340, 112, 0, -420),
    };

    static readonly RectFix[] GameRectFixes =
    {
        new RectFix("Canvas/LevelUpPanel/Dialog",      1060, 840), // 1000×760
        new RectFix("Canvas/DamageMeterPanel/Dialog",   780, 700), // 700×600

        // 테두리를 입히면 40px 중 24px을 테두리가 먹어 막대가 16px만 남는다 — 체력바와 같은 높이로.
        new RectFix("Canvas/HUD/ExpBar", 900, 54),

        // ── 진화창: 쓰는 건 2×2인데 좌표가 3×3 시절 그대로라 **왼쪽 위로 쏠려** 있었다.
        //    (열 -460/0 · 행 220/-40 → 오른쪽 3분의 1과 아래 3분의 1이 통째로 빈다.)
        //    쓰는 4칸만 화면 중앙으로 옮기고, 남는 폭만큼 칸을 키워 설명이 덜 감기게 한다.
        //    ⚠️ 안 쓰는 T3 열·P2 행은 건드리지 않는다 — 3티어로 되돌릴 때 좌표가 남아 있어야 한다.
        new RectFix("Canvas/EvolutionPanel/Window/SkillIcon",     130, 130,    0,  400),
        new RectFix("Canvas/EvolutionPanel/Window/SkillNameText", 800,  55,    0,  305),
        new RectFix("Canvas/EvolutionPanel/Window/SubInfoText",   900,  40,    0,  258),

        // 2026-08-25: 개큰네모를 620x290으로 **줄여 Sliced**로 쓰던 것을 원본 721x289 + Simple로 되돌렸다
        // (CLAUDE.md §5-1). x는 ±410 — 721폭 두 장 사이에 화살표(80)가 들어갈 99px을 남긴 값이다.
        new RectFix("Canvas/EvolutionPanel/Window/Node_P0T1", 721, 289, -410,   45),
        new RectFix("Canvas/EvolutionPanel/Window/Node_P0T2", 721, 289,  410,   45),
        new RectFix("Canvas/EvolutionPanel/Window/Arrow_P0_0", 80,  60,    0,   45),
        new RectFix("Canvas/EvolutionPanel/Window/Node_P1T1", 721, 289, -410, -285),
        new RectFix("Canvas/EvolutionPanel/Window/Node_P1T2", 721, 289,  410, -285),
        new RectFix("Canvas/EvolutionPanel/Window/Arrow_P1_0", 80,  60,    0, -285),
    };

    // 노드 안쪽(아이콘·제목·설명)은 4칸 모두 같은 배치라 접두사로 한 번에 잡는다.
    //
    // 🔴 옛 배치(Icon@92 · Title@32 · Desc 150@-60)는 **위아래로 테두리를 뚫고 있었다.**
    //    노드 620x290에 개큰네모(721x289, 테두리 좌25·아래88·우23·위34)라 안쪽은 y[-57, +111] = 168px뿐인데,
    //    아이콘이 위로 21px · 설명이 아래로 78px 넘어갔다(플레이스루 지적 "아이콘이 패널 밖으로 나간다").
    //    아래 테두리 88px이 높이의 30%를 먹는 게 원인이라 세로로 셋을 쌓으면 어떻게 해도 안 들어간다.
    //
    // → 아이콘을 제목 **옆**으로 올려 한 줄을 아꼈다. 그래서 설명이 108px(4줄)을 쓴다.
    //    진화 설명 144건을 전수로 재보니 평균 35.6자 · 최장 81자 = 570px/22pt 기준 **최대 4줄**이라 4줄이면 족하다.
    //    (노드 4칸의 위치·크기와 2루트x2티어 구조는 그대로 둔다 — 여기서 고치는 건 칸 **안쪽**뿐이다.)
    static readonly RectFix[] EvoNodeChildFixes =
    {
        new RectFix("Icon",         56,  56, -250,  83),
        new RectFix("Title",       460,  36,   40,  83),
        new RectFix("Description", 570, 108,    0,  -3),
    };

    static readonly string[] EvoUsedNodes =
        { "Node_P0T1", "Node_P0T2", "Node_P1T1", "Node_P1T2" };

    // 글자 머티리얼을 건드리면 안 되는 것 — 전용 셰이더를 쓰는 데미지 숫자 계열.
    static readonly string[] FontMatSkip = { "pixelroborobo Num" };

    [MenuItem("Window/Blueberry Defense/UI 스킨 적용 (열린 씬 전체)")]
    static void Run()
    {
        Debug.Log(Apply());
    }

    [MenuItem("Window/Blueberry Defense/UI 스킨 에셋 만들기")]
    static void RunCreateAsset()
    {
        Debug.Log(CreateAsset());
    }

    // 런타임 UI(설정·일시정지)가 집어 갈 `Assets/Resources/UISkin.asset`을 굽는다.
    // 경로·파일명이 곧 배선이라 손으로 만들지 말 것 — 여기 값이 위 토큰과 같아야 두 쪽이 같은 옷이 된다.
    public static string CreateAsset()
    {
        const string dir = "Assets/Resources";
        const string path = dir + "/UISkin.asset";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Resources");

        var skin = AssetDatabase.LoadAssetAtPath<UISkin>(path);
        bool isNew = skin == null;
        if (isNew) skin = ScriptableObject.CreateInstance<UISkin>();

        // 런타임 코드 UI(설정·일시정지·갈림길 세로 카드·컬렉션)가 쓰는 5종.
        skin.panel = Load(SpritePath(SPillow));  // 설정 980x850 · 일시정지 1760x940
        skin.bar = Load(SpritePath(SBar));
        skin.box = Load(SpritePath(SSquare));
        // 컬렉션 화면은 판을 원본 크기 위로 늘리지 않는 게 규칙이라, 넓은 칸/아이콘 칸용 그림이 따로 필요하다.
        skin.barWide = Load(SpritePath(SBarWide));
        skin.iconBox = Load(SpritePath(SIconBox));
        skin.bigBox  = Load(SpritePath(SBigBox));   // 여러 행을 묶는 그룹 상자(설정 화면)
        // 볼륨 슬라이더처럼 "차오르는 바"가 쓴다. 셋 다 Multiple이라 LoadSub로 집는다.
        skin.gaugeTrack = Load(SpritePath(SHealth)) ?? LoadSub(SpritePath(SHealth));
        skin.gaugeFill  = LoadSub(SpritePath(SHealthFill));
        skin.gaugeOuter = LoadSub(SpritePath(SHealthOuter));
        skin.skin = Skin;
        skin.highlight = Highlight;
        skin.dim = new Color(DimRgb.r, DimRgb.g, DimRgb.b, 0.8f);
        skin.pixelFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PixelFont);
        skin.bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFont);
        skin.pixelBig = Mat(PixelFont, MatBig);
        skin.pixelSmall = Mat(PixelFont, MatSmall);
        skin.bodyBig = Mat(BodyFont, MatBig);
        skin.bodySmall = Mat(BodyFont, MatSmall);

        if (isNew) AssetDatabase.CreateAsset(skin, path);
        EditorUtility.SetDirty(skin);
        AssetDatabase.SaveAssets();

        // 조용한 실패를 막으려고 되읽어 확인한다(코드로 만든 에셋의 참조가 안 들어간 적이 있다).
        var back = AssetDatabase.LoadAssetAtPath<UISkin>(path);
        return (isNew ? "생성" : "갱신") + " " + path
            + " | panel=" + N(back.panel) + " bar=" + N(back.bar) + " box=" + N(back.box)
            + " barWide=" + N(back.barWide) + " iconBox=" + N(back.iconBox)
            + " | pixel=" + N(back.pixelFont) + "/" + N(back.pixelBig) + "," + N(back.pixelSmall)
            + " body=" + N(back.bodyFont) + "/" + N(back.bodyBig) + "," + N(back.bodySmall);
    }

    static Material Mat(string fontAssetPath, string suffix)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(fontAssetPath);
        return AssetDatabase.LoadAssetAtPath<Material>("Assets/Fonts/" + name + suffix + ".mat");
    }

    static string N(Object o) => o != null ? o.name : "NULL";

    // 열려 있는 모든 씬에 적용하고 결과를 문자열로 돌려준다.
    // (console-get-logs를 못 쓰므로 검증은 이 반환값으로 한다.)
    public static string Apply()
    {
        var sb = new StringBuilder();
        int graphics = 0, texts = 0, buttons = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            Target[] table = scene.name == "Title" ? TitleTargets
                           : scene.name == "Battle" ? GameTargets : null;
            if (table == null) { sb.AppendLine("[" + scene.name + "] 표가 없어 건너뜀"); continue; }

            sb.AppendLine("[" + scene.name + "]");

            // 크기 보정이 먼저다 — FitSlice가 최종 크기를 봐야 테두리 배율이 맞는다.
            if (scene.name == "Title")
            {
                foreach (var f in TitleRectFixes) ApplyRect(FindByPath(scene, f.path), f, sb);
                foreach (var p in ScaleResetPaths) ResetScale(FindByPath(scene, p), sb);
            }

            if (scene.name == "Battle")
            {
                foreach (var f in GameRectFixes) ApplyRect(FindByPath(scene, f.path), f, sb);
                foreach (var n in EvoUsedNodes)
                {
                    var node = FindByPath(scene, "Canvas/EvolutionPanel/Window/" + n);
                    if (node == null) continue;
                    foreach (var f in EvoNodeChildFixes)
                        ApplyRect(node.transform.Find(f.path)?.gameObject, f, sb);
                    OpaqueWhenDisabled(node, sb);
                }
            }

            foreach (var t in table)
            {
                var go = FindByPath(scene, t.path);
                if (go == null) { sb.AppendLine("  ?? 없음: " + t.path); continue; }
                if (SkinOne(go, t, sb)) graphics++;
            }

            if (scene.name == "Battle")
            {
                foreach (var go in FindByPrefix(scene, EvoNodePrefix))
                    if (SkinOne(go, S(go.name, SBigBox), sb)) graphics++;  // 620x290 (2.14)
                InsetGaugeFills(scene, sb);
            }

            if (scene.name == "Title")
            {
                EnsureWallpaper(scene, sb);
                RemoveCornerBrackets(scene, sb);
            }

            texts += SkinAllText(scene, sb);
            buttons += TuneAllButtons(scene, sb);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        SkinSkillNodePrefab(sb);

        sb.AppendLine("── 바탕 " + graphics + "개 · 글자 " + texts + "개 · 버튼 " + buttons + "개 적용");
        return sb.ToString();
    }

    // ── 손맛: 씬의 모든 JuicyButton을 캐릭터 선택 화면 값으로 ──
    // 값 자체는 JuicyTuning이 단독 소유한다(런타임 생성 버튼도 같은 것을 쓴다).
    // 인스펙터 필드가 전부 private이라 리플렉션으로 넣고, 씬에 저장되도록 SerializedObject로 되받는다.
    static int TuneAllButtons(Scene scene, StringBuilder sb)
    {
        int n = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var jb in root.GetComponentsInChildren<JuicyButton>(true))
            {
                Undo.RecordObject(jb, "UI Skin");
                JuicyTuning.Apply(jb);
                // ⚠️ 리플렉션 대입은 SerializedObject가 모른다 — 이걸 빼면 씬을 저장해도 옛 값이 남는다.
                var so = new SerializedObject(jb);
                so.Update();
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(jb);
                n++;
            }
        sb.AppendLine("  버튼 손맛 " + n + "개 (hover " + JuicyTuning.HoverScale + " · 흔들림 " + JuicyTuning.ShakeOnHover + ")");
        return n;
    }

    // 스킬트리 노드는 씬이 아니라 프리팹에서 런타임 복제된다 — 씬만 훑으면 여기만 옛 모양으로 남는다.
    // ⚠️ BG의 **색**은 손대지 않는다. SkillTreeUI가 노드 종류(일반/해금/강화)마다 다른 색을 덮어쓴다.
    const string SkillNodePrefab = "Assets/SkillTree/UI/SkillNode.prefab";

    static void SkinSkillNodePrefab(StringBuilder sb)
    {
        var box = Load(SpritePath(SBarShort)); // 노드는 150x64 (2.34) — 정사각이 아니라 가로형이다
        var root = PrefabUtility.LoadPrefabContents(SkillNodePrefab);
        if (root == null) { sb.AppendLine("[프리팹] ?? 없음: " + SkillNodePrefab); return; }

        // Ring(테두리)과 BG(속)를 **둘 다** 갈아야 한다 — 한쪽만 바꾸면 둥근 손그림 속에 각진 사각이
        // 튀어나오거나 그 반대가 된다(첫 시도에 BG만 바꿔 알약이 사각 안에 뜬 모양이 나왔다).
        var ring = root.transform.Find("Ring")?.GetComponent<Image>();
        if (ring != null) { ring.sprite = box; UISkin.FitSlice(ring); }

        var bg = root.transform.Find("BG")?.GetComponent<Image>();
        if (bg != null) { bg.sprite = box; UISkin.FitSlice(bg); }

        var label = root.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            var pixel = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PixelFont);
            if (pixel != null) label.font = pixel;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Fonts/" + label.font.name + (label.fontSize >= SmallCut ? MatBig : MatSmall) + ".mat");
            if (mat != null) label.fontSharedMaterial = mat;
        }

        // ⚠️ 보고 문구는 Unload **전에** 뽑는다 — 언로드하면 위 참조가 파괴돼 전부 null로 읽힌다.
        string report = "[프리팹] SkillNode — BG=" + (bg != null && bg.sprite != null ? bg.sprite.name : "?")
                      + " Label=" + (label != null && label.fontSharedMaterial != null ? label.fontSharedMaterial.name : "?");

        PrefabUtility.SaveAsPrefabAsset(root, SkillNodePrefab);
        PrefabUtility.UnloadPrefabContents(root);
        sb.AppendLine(report);
    }

    // 🔴 잠긴 진화 노드가 **반투명**해 배경이 그대로 비치던 원인.
    // uGUI `Selectable`은 interactable=false일 때 targetGraphic에 `disabledColor`를 곱하는데
    // 기본값이 알파 0.5다. 잠김은 이미 어두운 판 색·회색 글자로 말하고 있으므로 알파는 1로 되돌린다.
    // (맵·캐릭터의 잠긴 카드는 실루엣 연출이 의도라 건드리지 않는다.)
    static void OpaqueWhenDisabled(GameObject go, StringBuilder sb)
    {
        var btn = go.GetComponent<Button>();
        if (btn == null) return;
        var c = btn.colors;
        if (Mathf.Approximately(c.disabledColor.a, 1f)) return;
        Undo.RecordObject(btn, "UI Skin");
        c.disabledColor = new Color(c.disabledColor.r, c.disabledColor.g, c.disabledColor.b, 1f);
        btn.colors = c;
        EditorUtility.SetDirty(btn);
        sb.AppendLine("  잠김 알파 " + go.name + " → 1.0");
    }

    static void ApplyRect(GameObject go, RectFix f, StringBuilder sb)
    {
        if (go == null) { sb.AppendLine("  ?? 없음: " + f.path); return; }
        var rt = (RectTransform)go.transform;
        bool sizeSame = rt.sizeDelta == f.size;
        bool posSame = !f.movePos || rt.anchoredPosition == f.pos;
        if (sizeSame && posSame) return;

        Undo.RecordObject(rt, "UI Skin");
        sb.AppendLine("  칸 " + go.name + " " + rt.sizeDelta + (f.movePos ? "@" + rt.anchoredPosition : "")
                      + " → " + f.size + (f.movePos ? "@" + f.pos : ""));
        rt.sizeDelta = f.size;
        if (f.movePos) rt.anchoredPosition = f.pos;
        EditorUtility.SetDirty(rt);
    }

    // ── 바탕(Image) 한 개 ──
    static bool SkinOne(GameObject go, Target t, StringBuilder sb)
    {
        var img = go.GetComponent<Image>();
        if (img == null) { sb.AppendLine("  ?? Image 없음: " + go.name); return false; }
        Undo.RecordObject(img, "UI Skin");

        if (t.sprite == null)
        {
            // 딤은 스프라이트 없이 색만. 인게임 모달은 뒤에 판이 비쳐야 해서 투명도를 표에 적어 둔다.
            float a = t.alpha >= 0f ? t.alpha : FullDimAlpha;
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.color = new Color(DimRgb.r, DimRgb.g, DimRgb.b, a);
            sb.AppendLine("  딤 " + go.name + " a=" + a.ToString("0.00"));
            EditorUtility.SetDirty(img);
            return true;
        }

        var s = Load(SpritePath(t.sprite));
        if (s == null) { sb.AppendLine("  ?? 그림 없음: " + t.sprite); return false; }

        img.sprite = s;
        // 표에 올린 것은 "그려져야 하는 바탕"이다. 옛 선택 프레임처럼 **Image가 꺼진 채 껍데기로만**
        // 쓰이던 오브젝트가 있어서(보이는 건 꺾쇠 4개뿐이었다) 켜 주지 않으면 조용히 안 그려진다.
        img.enabled = true;
        // _투명은 순수 검정 테두리다 — 색을 곱하면 그대로 검정이라 원래 색(흰색)을 지킨다.
        img.color = t.sprite.EndsWith("_투명") ? Color.white : t.tint;
        UISkin.FitSlice(img); // 테두리 두께 규칙은 런타임 UI와 한 벌만 쓴다
        if (t.back) go.transform.SetAsFirstSibling(); // 뒤에 깔리는 판(호버 하이라이트)
        sb.AppendLine("  " + go.name + " ← " + s.name + " (ppu×" + img.pixelsPerUnitMultiplier.ToString("0.00") + ")");
        EditorUtility.SetDirty(img);
        return true;
    }

    // 선택 하이라이트의 꺾쇠 4개(UI_CornerBracket)를 **지운다**.
    // 새 세트엔 대응 그림이 없고, 속 빈 테두리(_투명) 한 겹이 같은 역할을 한다.
    // ⚠️ 되돌리려면: Frame 아래 TL/TR/BL/BR 네 개를 만들어 `UI_CornerBracket`을 색 #FFE04D로 깔고
    //    캐릭터 카드는 57x57, 맵 카드는 71x71로 네 모서리에 앉히면 된다.
    static void RemoveCornerBrackets(Scene scene, StringBuilder sb)
    {
        foreach (string parent in CornerBracketParents)
        {
            var frame = FindByPath(scene, parent);
            if (frame == null) continue;
            var doomed = new List<GameObject>();
            foreach (Transform c in frame.transform)
            {
                var img = c.GetComponent<Image>();
                if (img != null && img.sprite != null && img.sprite.name.StartsWith("UI_")) doomed.Add(c.gameObject);
            }
            foreach (var go in doomed) Undo.DestroyObjectImmediate(go);
            if (doomed.Count > 0) sb.AppendLine("  꺾쇠 " + doomed.Count + "개 삭제 (" + frame.name + ")");
        }
    }

    // 게이지의 채움 막대를 테두리 안쪽으로 밀어 넣는다.
    // ⚠️ 이걸 안 하면 막대가 칸을 거의 다 덮어 **새로 입힌 테두리가 그 아래로 숨는다**
    //    (세션41에 게이지만 스프라이트를 못 입히던 이유였다).
    static void InsetGaugeFills(Scene scene, StringBuilder sb)
    {
        foreach (string path in GaugePaths)
        {
            var go = FindByPath(scene, path);
            if (go == null) continue;
            var img = go.GetComponent<Image>();
            if (img == null || img.sprite == null) continue;

            // 9-slice 테두리는 원본 픽셀 ÷ 배율만큼 화면에 그려진다(스프라이트 PPU 100 = 캔버스 기준).
            Vector4 b = img.sprite.border / Mathf.Max(0.0001f, img.pixelsPerUnitMultiplier);
            foreach (Transform c in go.transform)
            {
                var fill = c.GetComponent<Image>();
                if (fill == null || fill.type != Image.Type.Filled) continue;
                var rt = (RectTransform)c;
                Undo.RecordObject(rt, "UI Skin");
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(b.x, b.y);
                rt.offsetMax = new Vector2(-b.z, -b.w);
                EditorUtility.SetDirty(rt);

                // 🔴 채움 막대에서 스프라이트를 떼면 fillAmount가 통째로 무시된다 — 바가 항상 가득 차 보인다.
                // Image.OnPopulateMesh가 activeSprite == null이면 Type.Filled 분기에 가기 전에
                // Graphic의 기본 사각형을 그려버리기 때문. (8/24 플레이스루의 "체력·경험치 바가 안 움직임"이 이것)
                // 그래서 단색 1장을 반드시 물려 둔다. 색은 Image.color가 그대로 정한다.
                Sprite solid = LoadSub(SpritePath(SSolidFill));
                if (solid != null && fill.sprite != solid)
                {
                    Undo.RecordObject(fill, "UI Skin");
                    fill.sprite = solid;
                    EditorUtility.SetDirty(fill);
                }
            }
            sb.AppendLine("  게이지 " + go.name + " 채움 안쪽으로 (" + b.x.ToString("0") + "," + b.y.ToString("0")
                          + "," + b.z.ToString("0") + "," + b.w.ToString("0") + ")");
        }
    }

    // ── 글자: 씬의 모든 TMP를 스킨 폰트·머티리얼로 ──
    static int SkinAllText(Scene scene, StringBuilder sb)
    {
        var pixel = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PixelFont);
        var body  = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFont);
        int n = 0;

        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.font == null) continue;
                if (System.Array.Exists(FontMatSkip, p => t.font.name.StartsWith(p))) continue;

                Undo.RecordObject(t, "UI Skin");
                // 본문용 Regular은 WhitePurple 머티리얼이 없다 — 선택 화면과 같은 Bold로 옮긴다.
                if (t.font.name.StartsWith("Pretendard") && body != null) t.font = body;
                else if (!t.font.name.StartsWith("Pretendard") && pixel != null) t.font = pixel;

                string matPath = "Assets/Fonts/" + t.font.name +
                                 (t.fontSize >= SmallCut ? MatBig : MatSmall) + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { sb.AppendLine("  ?? 머티리얼 없음: " + matPath); continue; }

                if (t.fontSharedMaterial != mat) t.fontSharedMaterial = mat;

                // 🔴 합성 Bold를 끈다. TMP의 Bold는 면을 한 번 더 부풀리는데, 스킨 머티리얼은 이미
                //    `_FaceDilate`로 부풀려 놓은 상태라 둘이 겹치면 **한글 자모 사이가 메워져 덩어리로 보인다**
                //    (44pt "레벨 업!"의 '레'가 실제로 흰 블록이 됐다).
                //    스타일 원본인 캐릭터/맵 선택 화면에는 Bold가 한 글자도 없다 — 거기를 따른다.
                t.fontStyle &= ~FontStyles.Bold;
                // 어두운 판 위로 옮겨왔으므로 검정 글자는 안 보인다. 의미색은 그대로 둔다.
                if (t.color.r < 0.15f && t.color.g < 0.15f && t.color.b < 0.15f)
                    t.color = new Color(1f, 1f, 1f, t.color.a);

                EditorUtility.SetDirty(t);
                n++;
            }
        return n;
    }

    // 스킬트리 창에도 선택 화면과 같은 흐르는 벽지를 깐다(딤 바로 위·내용 아래).
    static void EnsureWallpaper(Scene scene, StringBuilder sb)
    {
        var rootGo = FindByPath(scene, "Canvas/SkillTreeRoot");
        if (rootGo == null) return;
        if (rootGo.transform.Find("Wallpaper") != null) { sb.AppendLine("  벽지 이미 있음"); return; }

        // 새로 짓지 않고 선택 화면 것을 복제한다 — 속도·타일 크기까지 원본과 같아야 한 화면처럼 보인다.
        var source = FindByPath(scene, "Canvas/CharacterSelectRoot/Wallpaper");
        if (source == null) { sb.AppendLine("  ?? 복제할 벽지 원본을 못 찾음"); return; }

        var go = Object.Instantiate(source, rootGo.transform, false);
        go.name = "Wallpaper";
        Undo.RegisterCreatedObjectUndo(go, "UI Skin Wallpaper");
        go.transform.SetAsFirstSibling();

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        go.GetComponent<RawImage>().raycastTarget = false;
        sb.AppendLine("  벽지 복제 완료");
    }

    // ── 잡동사니 ──
    static Sprite Load(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

    // Multiple로 임포트된 PNG는 메인 에셋이 Texture2D라 위 Load가 null을 준다 — 서브 스프라이트를 직접 집는다.
    static Sprite LoadSub(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is Sprite s) return s;
        return null;
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    static GameObject FindByPath(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
            if (t != null) return t.gameObject;
        }
        return null;
    }

    static List<GameObject> FindByPrefix(Scene scene, string prefix)
    {
        var list = new List<GameObject>();
        int cut = prefix.LastIndexOf('/');
        var parent = FindByPath(scene, prefix.Substring(0, cut));
        if (parent == null) return list;
        string namePrefix = prefix.Substring(cut + 1);
        foreach (Transform c in parent.transform)
            if (c.name.StartsWith(namePrefix)) list.Add(c.gameObject);
        return list;
    }
}
