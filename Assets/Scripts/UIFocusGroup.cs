using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 모달 UI를 키보드·게임패드로 고르게 하는 공용 포커스 커서. 입력은 GameInput에서 읽는다.
// 항목은 Button만이 아니라 Slider(좌우 = 값 조절)·Toggle(확정 = 켜고 끄기)도 받는다(설정 창).
//
// 왜 축(가로/세로)을 인자로 받지 않고 **좌표로 판정**하나 — 화면마다 배치가 제각각이라서다.
// 레벨업은 세로 3장 + 오른쪽에 리롤 1장이고, 진화 창은 2×2인데 **행이 루트·열이 티어**다(실측).
// 축을 손으로 적으면 화면이 바뀔 때마다 같이 틀어지지만, rect 위치로 "그 방향에서 가장 가까운 것"을
// 고르면 배치를 옮겨도 저절로 맞는다(Unity 기본 Navigation.Automatic과 같은 원리).
//
// 🔴 **MonoBehaviour가 아니다.** 각 UI가 필드로 하나 들고 자기 Update에서 Tick()을 부른다 —
//    씬에 컴포넌트를 6개 배선하지 않으려고 이렇게 뒀다.
public class UIFocusGroup
{
    // 모달은 겹친다(갈림길 → 진화). **맨 위 하나만** 입력을 먹어야 두 창이 같이 움직이지 않는다.
    private static readonly List<UIFocusGroup> stack = new List<UIFocusGroup>();

    private readonly List<Selectable> items = new List<Selectable>();
    private readonly List<Image> glows = new List<Image>();
    private int focus = -1;
    private bool open;
    private Button cancel;

    public Selectable Focused => (focus >= 0 && focus < items.Count) ? items[focus] : null;

    private bool IsTop => stack.Count > 0 && stack[stack.Count - 1] == this;

    // 이 그룹이 지금 입력을 받는 맨 위 그룹인가 — 화면이 스틱 스크롤 같은 자기 입력을 그룹과 같은 조건으로 거를 때 쓴다.
    public bool IsActive => open && IsTop;

    // 패널을 열 때 부른다. 같은 그룹을 다시 열면 목록만 갈아끼운다.
    // cancel = ESC·패드 B가 누를 버튼(뒤로·닫기). 없으면 취소 입력을 먹지 않는다(그 창이 직접 처리하거나 취소가 없는 창).
    public void Open(IEnumerable<Selectable> selectables, int initialIndex = 0, Button cancelButton = null)
    {
        // 🔴 닫지 않고 다시 여는 화면이 있다(레벨업 리롤). 지난 목록의 테·선택됨을 끄지 않고 비우면
        //    그때 포커스였던 칸(리롤)이 노랑 테와 밝은 상태를 단 채 남고, 이후 아무도 그 칸을 끄지 않는다.
        for (int i = 0; i < items.Count; i++) Paint(i, false);
        Clear();
        foreach (Selectable s in selectables)
            if (s != null) items.Add(s);
        cancel = cancelButton;

        hooked.RemoveWhere(x => x == null);   // 지난 판에서 파괴된 버튼을 흘려보낸다
        for (int i = 0; i < items.Count; i++)
        {
            glows.Add(EnsureGlow(items[i]));
            HookPointer(items[i]);
        }

        if (!open) { stack.Add(this); open = true; }
        SetFocus(NextUsable(initialIndex - 1, +1, true));
    }

    public void Close()
    {
        for (int i = 0; i < items.Count; i++) Paint(i, false);
        if (open) { stack.Remove(this); open = false; }
        Clear();
    }

    private void Clear()
    {
        items.Clear();
        glows.Clear();
        focus = -1;
        cancel = null;
    }

    // 씬이 바뀌면 스택을 비운다. 씬 전환으로 닫히지 못한 그룹(맵 선택 → 전투 로드, 결과 → 다시하기)이
    // 파괴된 버튼을 든 채 쌓인다. 씬을 넘어 사는 UI 소유자는 없다(DontDestroyOnLoad는 효과음·페이드·봇뿐).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookSceneReset()
    {
        SceneManager.sceneLoaded -= ResetStack;
        SceneManager.sceneLoaded += ResetStack;
    }

    private static void ResetStack(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        foreach (UIFocusGroup g in stack) g.open = false;
        stack.Clear();
    }

    // 테를 보일지가 바뀌면(패드 ↔ 마우스) 열린 창의 포커스 칸을 다시 칠한다.
    // FocusChanged도 다시 알린다 — 카드 화면은 포커스 칸의 SelectGlow를 "고른 카드" 표시와 같이 쓰는데,
    // 테를 끄는 Paint가 그 표시까지 지우므로 화면이 다시 칠해야 한다(viaPointer = true라 카드를 새로 고르지는 않는다).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookFocusVisible()
    {
        GameInput.FocusVisibleChanged -= RepaintOpen; // 도메인 리로드를 끈 설정에서 중복 구독 방지
        GameInput.FocusVisibleChanged += RepaintOpen;
    }

    private static void RepaintOpen()
    {
        foreach (UIFocusGroup g in stack.ToArray())
        {
            if (g.focus < 0) continue;
            g.Paint(g.focus, true);
            g.FocusChanged?.Invoke(g.Focused, true);
        }

        // 커서가 다시 켜졌으면(마우스를 움직였다) 커서 밑 칸으로 포커스·호버를 되돌린다.
        // 숨긴 동안 막아 둔 Enter는 다시 오지 않아서, 이게 없으면 커서와 포커스가 서로 다른 칸을 가리킨 채로 남는다.
        Mouse mouse = Mouse.current;
        if (!GameInput.FocusVisible && mouse != null && stack.Count > 0)
            stack[stack.Count - 1].FocusUnderPointer(mouse.position.ReadValue());
    }

    // 각 UI의 Update가 매 프레임 부른다. timeScale 0에서도 Update는 돌기 때문에 모달에서 그대로 쓸 수 있다.
    // 이동·확정·취소를 먹으면 GameInput.Consume — 확정으로 열린 다음 창이 같은 프레임에 같은 입력을 또 읽지 않게.
    public void Tick()
    {
        if (!open || !IsTop) return;

        Vector2 dir = GameInput.NavPressed();
        if (dir != Vector2.zero)
        {
            GameInput.Consume();
            GameInput.SetFocusVisible(true); // 키보드로 옮겨도 테가 보여야 한다(패드는 GameInput이 이미 켰다)
            // 🔴 키보드로 옮기면 커서 호버 연출을 끈다. 커서가 가만히 있으면 Exit가 안 와서
            //    창이 뜰 때 커서 밑에 깔린 칸이 포커스를 떠난 뒤에도 밝고 큰 채로 남는다.
            DropPointerHover();
            if (!AdjustSlider(dir)) Move(dir);
            return;
        }

        if (GameInput.SubmitPressed())
        {
            GameInput.Consume();
            Submit();
        }
        else if (cancel != null && cancel.IsInteractable() && cancel.gameObject.activeInHierarchy // 꺼진 X(진화 대상 선택의 되돌아가기 등)는 취소를 먹지 않는다
                 && GameInput.CancelPressed())
        {
            GameInput.Consume();
            cancel.onClick.Invoke();
        }
    }

    // ── 조합키를 누른 채 들어온 Enter·Space는 "누르기"가 아니다 ─────────────────────
    // 🔴 Alt+Enter(전체화면 전환)를 눌렀더니 일시정지 창의 **포기**가 눌려 게임오버가 됐다(2026-10-01 사용자).
    //    커서가 포기 위에 있으면 포커스가 거기로 옮겨 가 있고, Windows가 전체화면을 바꾸는 동안 Enter는 게임에도 들어온다.
    //    Alt·Ctrl·Win은 OS·앱 단축키로 쓰이는 키라(Alt+Enter·Alt+Space·Win+Space…) 누르고 있는 동안은 확정 입력을 버린다.
    public static bool ShortcutModifierHeld(Keyboard kb) =>
        kb != null && (kb.altKey.isPressed || kb.ctrlKey.isPressed || kb.leftMetaKey.isPressed || kb.rightMetaKey.isPressed);

    // 유니티 기본 UI 입력(EventSystem·DefaultInputActions)도 Enter를 Submit으로 받는다 — 마우스로 누른 버튼이
    // **선택된 채 남아** 있다가 그 창이 다시 열리면 Enter가 그 버튼을 누른다. 그래서 조합키를 누르는 동안엔
    // 입력 갱신 직후(EventSystem이 처리하기 전)에 선택을 비운다. 이 게임은 EventSystem의 선택·내비게이션을
    // 쓰지 않고(키보드 이동은 이 클래스가 한다) 글자 입력칸도 없어서 비워도 잃는 게 없다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookShortcutGuard()
    {
        InputSystem.onAfterUpdate -= ClearSelectionWhileModifierHeld; // 도메인 리로드를 끈 설정에서 중복 구독 방지
        InputSystem.onAfterUpdate += ClearSelectionWhileModifierHeld;
    }

    private static void ClearSelectionWhileModifierHeld()
    {
        EventSystem es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null && ShortcutModifierHeld(Keyboard.current))
            es.SetSelectedGameObject(null);
    }

    private void DropPointerHover()
    {
        for (int i = 0; i < items.Count; i++)
        {
            var juicy = items[i] != null ? items[i].GetComponent<JuicyButton>() : null;
            if (juicy != null) juicy.CancelHover();
        }
    }

    private void FocusUnderPointer(Vector2 screenPos)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (!Usable(i)) continue;
            Canvas canvas = items[i].GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)items[i].transform, screenPos, cam))
            {
                SetFocus(i, viaPointer: true);
                var juicy = items[i].GetComponent<JuicyButton>();
                if (juicy != null) juicy.BeginHover();
                return;
            }
        }
    }

    private void Submit()
    {
        Selectable s = Focused;
        if (s == null || !s.IsInteractable() || !s.gameObject.activeInHierarchy) return;
        if (s is Button b) b.onClick.Invoke();
        else if (s is Toggle t) t.isOn = !t.isOn;
    }

    // 슬라이더에 포커스가 있으면 좌우는 칸 이동이 아니라 값 조절이다(한 번에 전체의 10%). 위아래는 그대로 칸 이동.
    private bool AdjustSlider(Vector2 dir)
    {
        if (dir.x == 0f || !(Focused is Slider sl) || !sl.IsInteractable()) return false;
        float step = (sl.maxValue - sl.minValue) * 0.1f;
        if (sl.wholeNumbers) step = Mathf.Max(1f, Mathf.Round(step));
        bool reversed = sl.direction == Slider.Direction.RightToLeft;
        sl.value = Mathf.Clamp(sl.value + Mathf.Sign(dir.x) * (reversed ? -step : step), sl.minValue, sl.maxValue);
        return true;
    }

    // 지금 칸에서 dir 방향으로 가장 "그럴듯한" 칸을 고른다.
    // 진행 거리(along)에 축을 벗어난 거리(perp)를 가중해서 더한다 — 벌점이 없으면
    // 바로 옆이 아니라 대각선 멀리 있는 칸으로 튄다.
    // 세로 한 줄짜리 목록 화면(설정 창)은 좌표 대신 **목록 순서**로 위아래를 옮긴다. 좌우는 슬라이더 조절만 하고 칸은 안 옮긴다.
    // 좌표 판정은 가운데 열에 홀로 있는 칸(전체화면 토글)을 건너뛴다 — 옆 열의 ◀가 더 가깝게 나온다(실측).
    public bool ListOrderVertical;

    private void Move(Vector2 dir)
    {
        if (focus < 0) { SetFocus(NextUsable(-1, +1, true)); return; }

        if (ListOrderVertical)
        {
            if (dir.y == 0f) return;
            int next = NextUsable(focus, dir.y < 0f ? +1 : -1, false);
            if (next >= 0) SetFocus(next);                   // 끝이면 그대로 머문다
            return;
        }

        Vector2 from = Center(items[focus]);
        Vector2 perpAxis = new Vector2(-dir.y, dir.x);
        int best = -1;
        float bestScore = float.MaxValue;

        for (int i = 0; i < items.Count; i++)
        {
            if (i == focus || !Usable(i)) continue;
            Vector2 d = Center(items[i]) - from;
            float along = Vector2.Dot(d, dir);
            if (along <= 1f) continue;                       // 그 방향이 아니면 후보가 아니다
            float perp = Mathf.Abs(Vector2.Dot(d, perpAxis));
            float score = along + perp * 2f;
            if (score < bestScore) { bestScore = score; best = i; }
        }

        if (best >= 0) SetFocus(best);                       // 끝에 닿으면 그대로 머문다(순환하지 않는다)
    }

    private static Vector2 Center(Selectable b)
    {
        var rt = (RectTransform)b.transform;
        Vector3[] c = new Vector3[4];
        rt.GetWorldCorners(c);
        return (c[0] + c[2]) * 0.5f;
    }

    // 꺼져 있거나 못 누르는 칸은 건너뛴다 — 진화 창의 잠긴 2차 칸, 리롤이 0회 남았을 때 등.
    private bool Usable(int i) =>
        i >= 0 && i < items.Count && items[i] != null
        && items[i].gameObject.activeInHierarchy && items[i].IsInteractable();

    private int NextUsable(int from, int step, bool wrap)
    {
        for (int n = 0; n < items.Count; n++)
        {
            int i = from + step * (n + 1);
            if (wrap) i = ((i % items.Count) + items.Count) % items.Count;
            else if (i < 0 || i >= items.Count) break;
            if (Usable(i)) return i;
        }
        return -1;
    }

    // 포커스가 옮겨 간 뒤 알린다. viaPointer = 마우스가 올라가서 옮겨진 것(키보드·패드·창 열기가 아님).
    // 카드 화면은 키보드·패드로 카드에 오면 그 카드를 고르고(마우스 호버로는 고르지 않는다),
    // 스킬트리는 노드 툴팁을 띄운다.
    public event System.Action<Selectable, bool> FocusChanged;

    private void SetFocus(int index, bool viaPointer = false)
    {
        if (index == focus) return;
        if (focus >= 0) Paint(focus, false);
        focus = index;
        if (focus >= 0) Paint(focus, true);
        FocusChanged?.Invoke(Focused, viaPointer);
    }

    private void Paint(int i, bool on)
    {
        if (i < 0 || i >= items.Count || items[i] == null) return;

        // 마우스로 쓰는 동안엔 포커스 칸을 표시하지 않는다(GameInput.FocusVisible 주석). 포커스 자체는 계속 따라간다 —
        // 패드를 잡는 순간 RepaintOpen이 지금 칸에 테를 켠다.
        on = on && GameInput.FocusVisible;

        // JuicyButton이 있으면 그쪽 "선택됨"(맵 선택이 쓰는 것)을 그대로 빌린다.
        // ⚠️ 진화 창의 노드 4칸에는 JuicyButton이 **없다**(실측) — 그 칸은 아웃라인만으로 표시된다.
        var juicy = items[i].GetComponent<JuicyButton>();
        if (juicy != null) juicy.SetSelected(on);

        if (i < glows.Count && glows[i] != null)
            glows[i].color = on ? UISkin.Highlight : UISkin.Transparent;
    }

    // 맵 선택 카드와 **같은 방식**의 테. 판 그림을 한 장 더 깔고 UI/SelectOutline 머티리얼로
    // 실루엣 바깥에만 테를 그린다(텍스처 색은 안 쓴다). 이미 있으면 그걸 쓴다.
    // ⚠️ 테두리 그림을 색으로 물들이는 방식은 안 된다 — 그 그림들은 불투명 픽셀이 전부 순수 검정이다.
    // 자기 그림이 없는 칸(슬라이더·토글)은 targetGraphic(체크 박스), 그것도 없으면 그림이 있는 첫 자식(슬라이더의
    // Background 바 — 설정 창 슬라이더엔 손잡이도 targetGraphic도 없다, 실측)에 테를 두른다.
    private static Image EnsureGlow(Selectable b)
    {
        Transform found = UITreeUtil.FindDeep(b.transform, "SelectGlow");
        if (found != null) return found.GetComponent<Image>();

        Image src = b.GetComponent<Image>();
        if (src != null && src.sprite == null) src = null;
        if (src == null && b.targetGraphic is Image tg && tg.sprite != null) src = tg;
        if (src == null)
            foreach (Image child in b.GetComponentsInChildren<Image>(true))
                if (child.sprite != null) { src = child; break; }
        Transform parent = src != null ? src.transform : b.transform;
        Material mat = UISkin.SelectOutline;
        if (src == null || src.sprite == null || mat == null) return null;

        var go = new GameObject("SelectGlow", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsFirstSibling();   // 판 뒤에 깔린다 — 글자·아이콘을 가리면 안 된다

        Image img = go.GetComponent<Image>();
        img.sprite = src.sprite;
        img.type = Image.Type.Simple;         // §5-1: 늘리지 않는다
        img.preserveAspect = src.preserveAspect;
        img.raycastTarget = false;            // 켜면 버튼이 자기 테에 가려 클릭을 잃는다
        img.material = mat;
        img.color = UISkin.Transparent;
        return img;
    }

    // 마우스를 올리면 포커스가 따라간다 — 두 입력이 서로 다른 칸을 가리키면 무엇이 선택될지 알 수 없다.
    //
    // 🔴 훅은 버튼당 **한 번만** 건다. 패널은 판마다 여러 번 열리는데 열 때마다 걸면 EventTrigger 항목이
    //    쌓여서 한 번의 마우스 진입에 콜백이 수십 번 돈다. 인덱스를 캡처하지 않고 **버튼으로 되찾는** 것도
    //    같은 이유다 — 목록이 갈리면 예전에 캡처한 인덱스는 엉뚱한 칸을 가리킨다.
    //
    // 🔴 EventTrigger를 쓰지 않는다. EventTrigger는 드래그·휠 핸들러까지 전부 구현하고 있어서, 붙이는 순간
    //    그 칸 위에서 시작한 드래그·휠이 부모로 올라가지 않는다 — 스킬트리 노드 위에서 끌면 트리가 안 움직인다.
    //    진입 이벤트만 받는 UIFocusPointerHook을 붙인다.
    private static readonly HashSet<Selectable> hooked = new HashSet<Selectable>();

    private void HookPointer(Selectable b)
    {
        if (!hooked.Add(b)) return;
        b.gameObject.AddComponent<UIFocusPointerHook>().Entered = () => FocusFromPointer(b);
    }

    private static void FocusFromPointer(Selectable b)
    {
        if (GameInput.FocusVisible) return; // 커서를 숨긴 동안엔 커서가 포커스를 끌고 가지 않는다(GameInput.SetFocusVisible 주석)
        if (stack.Count == 0) return;
        UIFocusGroup top = stack[stack.Count - 1];
        int i = top.items.IndexOf(b);
        if (i >= 0 && top.Usable(i)) top.SetFocus(i, viaPointer: true);
    }
}

// UIFocusGroup이 런타임에 붙인다(씬/프리팹에 저장되지 않아 같은 파일 OK). 마우스 진입만 받는다 — 위 HookPointer 주석.
public class UIFocusPointerHook : MonoBehaviour, IPointerEnterHandler
{
    public System.Action Entered;
    public void OnPointerEnter(PointerEventData e) => Entered?.Invoke();
}
