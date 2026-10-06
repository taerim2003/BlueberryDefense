using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

// 키보드·마우스·게임패드 입력을 읽는 창구. UI 이동·확정·취소·일시정지·스킬 버튼을 장치와 상관없이 같은 질문으로 묻는다.
// 지금까지의 스타일(장치 API를 직접 읽기)을 그대로 따른다 — .inputactions·PlayerInput은 쓰지 않는다.
//
// 🔴 모든 공개 멤버가 Keyboard.current·Gamepad.current가 null이어도 견뎌야 한다.
//    무인 봇(batchmode)엔 패드가 없고 키보드도 BotPilot이 넣은 가상 장치뿐이다.
public static class GameInput
{
    public enum Mode { KeyboardMouse, Pad }
    public enum PadButton { South, East, West, North, LeftShoulder, RightShoulder, LeftTrigger, RightTrigger, Start }

    // 스킬 슬롯(Q·W·E·R 순) → 패드 버튼. 주 배치(HUD·튜토리얼 표기)는 얼굴 버튼 X·Y·B·A다(사용자 결정 2026-10-01 —
    // 패드 게임의 주 행동은 얼굴 버튼이라 처음 잡아도 찾는다). 왼쪽부터 시계 방향이 HUD 슬롯 순서와 같다.
    // LT·LB·RB·RT도 보조로 같이 받는다 — 검지로 누르고 싶은 사람 몫이다.
    private static readonly PadButton[] SlotMain = { PadButton.West, PadButton.North, PadButton.East, PadButton.South };
    private static readonly PadButton[] SlotAlt = { PadButton.LeftTrigger, PadButton.LeftShoulder, PadButton.RightShoulder, PadButton.RightTrigger };

    private const float StickThreshold = 0.5f;
    private const float RepeatDelay = 0.35f;   // 스틱·D패드를 누르고 있으면 이만큼 뒤부터
    private const float RepeatInterval = 0.12f; // 이 간격으로 한 칸씩 더 간다

    public static Mode Current { get; private set; } = Mode.KeyboardMouse;
    public static bool IsPad => Current == Mode.Pad;
    public static event Action ModeChanged;

    // 포커스 테(노랑)를 보이는가. 패드를 만지거나 키보드로 칸을 옮기면 켜지고, 마우스를 쓰면 꺼진다
    // (사용자 결정 2026-10-02 — 마우스로 쓰는 동안엔 테가 없어야 한다). 키보드로 옮기는 건 UIFocusGroup.Tick이 켠다.
    // Mode와 따로 두는 이유: 키보드와 마우스는 같은 Mode인데 테는 갈라야 한다.
    public static bool FocusVisible { get; private set; }
    public static event Action FocusVisibleChanged;

    // 테가 보이는 동안(키보드·패드로 고르는 중)엔 커서를 숨기고 호버도 막는다(사용자 결정 2026-10-06) —
    // 숨기기만 하면 새로 뜬 창의 칸이 가만히 있는 커서 밑에 깔려 호버되고 포커스까지 끌려간다.
    // 마우스를 움직이거나 누르면 TrackMode가 테를 끄면서 커서가 돌아온다.
    public static void SetFocusVisible(bool on)
    {
        if (on == FocusVisible) return;
        FocusVisible = on;
        Cursor.visible = !on;
        JuicyButton.HoverBlocked = on;
        FocusVisibleChanged?.Invoke();
    }

    // 키보드는 배틀 씬에서만 받는다(사용자 결정 2026-10-02 — 메뉴는 마우스·패드로 충분하다).
    // 메뉴의 ESC 닫기·Enter 확정·방향키 이동도 같이 꺼진다. 스킬 키는 배틀에서만 묻는다.
    private const string KeyboardSceneName = "Battle";
    private static bool keyboardAllowed;
    private static Keyboard Kb => keyboardAllowed ? Keyboard.current : null;

    // ── 프레임 상태 ─────────────────────────────────────────────────────────────
    // 입력 갱신 직후 프레임당 한 번 계산한다. 스틱 반복은 "이전 프레임에 무엇을 쥐고 있었나"를 알아야 해서
    // 묻는 쪽(UI의 Update)이 그 프레임에 묻든 안 묻든 매 프레임 돌아야 한다.
    private static int stepFrame = -1;
    private static Vector2 navThisFrame;
    private static Vector2 heldDir;
    private static float nextRepeat;
    private static int consumedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        InputSystem.onAfterUpdate -= Step; // 도메인 리로드를 끈 설정에서 중복 구독 방지
        InputSystem.onAfterUpdate += Step;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Current = Mode.KeyboardMouse;
        FocusVisible = false;
        Cursor.visible = true;
        JuicyButton.HoverBlocked = false;
        keyboardAllowed =SceneManager.GetActiveScene().name == KeyboardSceneName;
        stepFrame = -1;
        heldDir = Vector2.zero;
    }

    // 유니티 기본 UI 내비게이션(EventSystem)을 끈다. 이 게임은 그걸 쓰지 않고 UIFocusGroup이 이동·확정을 한다.
    // 켜 두면 마우스로 누른 버튼이 선택된 채 남아 있다가, 패드 A·스틱이 그 버튼을 **한 번 더** 누르거나 옮긴다.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) keyboardAllowed = scene.name == KeyboardSceneName;

        EventSystem es = EventSystem.current;
        if (es != null) es.sendNavigationEvents = false;
    }

    private static void Step()
    {
        if (!Application.isPlaying) return;
        int f = Time.frameCount;
        if (f == stepFrame) return; // onAfterUpdate는 한 프레임에 여러 번 올 수 있다
        stepFrame = f;

        Keyboard kb = Kb;
        Gamepad pad = Gamepad.current;
        navThisFrame = Vector2.zero;

        if (kb != null)
        {
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) navThisFrame = Vector2.up;
            else if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) navThisFrame = Vector2.down;
            else if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) navThisFrame = Vector2.left;
            else if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) navThisFrame = Vector2.right;
        }

        // 패드: D패드가 있으면 D패드, 없으면 왼쪽 스틱. 처음 기울이면 한 칸, 계속 쥐고 있으면 반복한다.
        Vector2 dir = Vector2.zero;
        if (pad != null)
        {
            dir = Quantize(pad.dpad.ReadValue());
            if (dir == Vector2.zero) dir = Quantize(pad.leftStick.ReadValue());
        }
        float now = Time.unscaledTime;
        if (dir == Vector2.zero) heldDir = Vector2.zero;
        else if (dir != heldDir)
        {
            heldDir = dir;
            nextRepeat = now + RepeatDelay;
            if (navThisFrame == Vector2.zero) navThisFrame = dir;
        }
        else if (now >= nextRepeat)
        {
            nextRepeat = now + RepeatInterval;
            if (navThisFrame == Vector2.zero) navThisFrame = dir;
        }

        TrackMode(kb, pad);
    }

    // 가장 많이 기운 축 하나로 자른다 — 대각선으로 기울이면 두 칸이 한꺼번에 움직이면 안 된다.
    private static Vector2 Quantize(Vector2 v)
    {
        if (v.sqrMagnitude < StickThreshold * StickThreshold) return Vector2.zero;
        return Mathf.Abs(v.x) >= Mathf.Abs(v.y)
            ? new Vector2(Mathf.Sign(v.x), 0f)
            : new Vector2(0f, Mathf.Sign(v.y));
    }

    // 마지막으로 만진 장치를 따라간다. 표기(LT/Q)를 바꾼다. 커서는 SetFocusVisible이 숨기고 켠다.
    private static void TrackMode(Keyboard kb, Gamepad pad)
    {
        Mode next = Current;
        if (pad != null && PadTouched(pad))
        {
            next = Mode.Pad;
            SetFocusVisible(true);
        }
        else if (kb != null && kb.anyKey.wasPressedThisFrame) next = Mode.KeyboardMouse;
        else
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f
                                  || mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                                  || mouse.scroll.ReadValue().sqrMagnitude > 0f))
            {
                next = Mode.KeyboardMouse;
                SetFocusVisible(false);
            }
        }
        if (next == Current) return;

        Current = next;
        ModeChanged?.Invoke();
    }

    private static bool PadTouched(Gamepad pad) =>
        pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
        || pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame
        || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame
        || pad.leftTrigger.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame
        || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame
        || pad.dpad.ReadValue().sqrMagnitude > 0.25f
        || pad.leftStick.ReadValue().sqrMagnitude > StickThreshold * StickThreshold
        || pad.rightStick.ReadValue().sqrMagnitude > StickThreshold * StickThreshold;

    // ── 한 번 누름 입력 ─────────────────────────────────────────────────────────
    // 🔴 한 입력은 한 곳만 먹는다. 확정으로 새 창이 열리면 그 창의 Tick이 **같은 프레임에** 같은 A를 또 읽어
    //    두 단계가 한꺼번에 넘어간다(타이틀 "플레이" → 캐릭터 카드까지 확정). 먹은 쪽이 Consume을 부르면
    //    그 프레임의 나머지 질문은 전부 false가 된다.
    public static void Consume() => consumedFrame = Time.frameCount;
    private static bool Consumed => consumedFrame == Time.frameCount;

    public static Vector2 NavPressed()
    {
        Step();
        return Consumed ? Vector2.zero : navThisFrame;
    }

    // 조합키(Alt·Ctrl·Win)를 누른 채 들어온 Enter·Space는 확정이 아니다(UIFocusGroup.ShortcutModifierHeld 주석).
    public static bool SubmitPressed()
    {
        if (Consumed) return false;
        Keyboard kb = Kb;
        if (kb != null && !UIFocusGroup.ShortcutModifierHeld(kb)
            && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            return true;
        Gamepad pad = Gamepad.current;
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }

    public static bool CancelPressed()
    {
        if (Consumed) return false;
        Keyboard kb = Kb;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
        Gamepad pad = Gamepad.current;
        return pad != null && pad.buttonEast.wasPressedThisFrame;
    }

    public static bool PausePressed()
    {
        if (Consumed) return false;
        Keyboard kb = Kb;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
        Gamepad pad = Gamepad.current;
        return pad != null && pad.startButton.wasPressedThisFrame;
    }

    // ── 스킬 ───────────────────────────────────────────────────────────────────
    // 이 프레임에 **눌렀는가**(누르고 있는가가 아니다 — 꾹 누르기 재발동은 없앴다, PlayerSkills.Update 주석).
    // 스킬 슬롯은 EquippedSkill.Key(Q·W·E·R)로 묻는다 — 슬롯 번호는 PlayerSkills.SlotKeys에서의 위치다.
    // UI 쪽 Consume과 무관하다 — 모달이 떠 있는 동안 PlayerSkills가 아예 묻지 않는다.
    public static bool SkillPressed(Key key)
    {
        Keyboard kb = Kb;
        if (kb != null && key != Key.None && kb[key].wasPressedThisFrame) return true;
        Gamepad pad = Gamepad.current;
        int slot = Array.IndexOf(PlayerSkills.SlotKeys, key);
        if (pad == null || slot < 0) return false;
        return Control(pad, SlotMain[slot]).wasPressedThisFrame || Control(pad, SlotAlt[slot]).wasPressedThisFrame;
    }

    public static Vector2 PanStick()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null) return Vector2.zero;
        Vector2 v = pad.rightStick.ReadValue();
        return v.sqrMagnitude < 0.04f ? Vector2.zero : v; // 스틱 쏠림 흡수
    }

    // 세로 스크롤(크레딧). 양쪽 스틱 중 더 기운 쪽의 위아래. 위 = +.
    public static float ScrollAxis()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null) return 0f;
        float l = pad.leftStick.ReadValue().y, r = pad.rightStick.ReadValue().y;
        float v = Mathf.Abs(l) >= Mathf.Abs(r) ? l : r;
        return Mathf.Abs(v) < 0.2f ? 0f : v;
    }

    // RT = 확대(+), LT = 축소(−).
    public static float ZoomAxis()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null) return 0f;
        return pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
    }

    // ── 표기 ───────────────────────────────────────────────────────────────────
    // Xbox 이름 텍스트로 쓴다(가정 — 스팀 덱·Steam Input이 Xbox 배치로 보고하고, 버튼 그림이 없다).
    public static string Label(PadButton b) => b switch
    {
        PadButton.South => "A",
        PadButton.East => "B",
        PadButton.West => "X",
        PadButton.North => "Y",
        PadButton.LeftShoulder => "LB",
        PadButton.RightShoulder => "RB",
        PadButton.LeftTrigger => "LT",
        PadButton.RightTrigger => "RT",
        PadButton.Start => "START",
        _ => "",
    };

    // HUD 슬롯 글자. 패드 모드면 주 배치 버튼 이름, 아니면 키 이름.
    public static string SkillLabel(Key key)
    {
        int slot = Array.IndexOf(PlayerSkills.SlotKeys, key);
        return IsPad && slot >= 0 ? Label(SlotMain[slot]) : key.ToString();
    }

    // 문구 키에 키보드/패드 판을 고른다 — 패드 판은 "<키>.pad"로 둔다. 패드 판이 없는 문구는 원래 키 그대로.
    public static string LocKey(string key) => IsPad && Loc.Has(key + ".pad") ? key + ".pad" : key;

    private static ButtonControl Control(Gamepad pad, PadButton b) => b switch
    {
        PadButton.South => pad.buttonSouth,
        PadButton.East => pad.buttonEast,
        PadButton.West => pad.buttonWest,
        PadButton.North => pad.buttonNorth,
        PadButton.LeftShoulder => pad.leftShoulder,
        PadButton.RightShoulder => pad.rightShoulder,
        PadButton.LeftTrigger => pad.leftTrigger,
        PadButton.RightTrigger => pad.rightTrigger,
        _ => pad.startButton,
    };
}
