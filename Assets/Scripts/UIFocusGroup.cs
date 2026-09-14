using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 모달 UI를 키보드로 고르게 하는 공용 포커스 커서. 나중에 게임패드를 붙일 때도 여기 한 곳만 늘리면 된다.
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

    private readonly List<Button> items = new List<Button>();
    private readonly List<Image> glows = new List<Image>();
    private int focus = -1;
    private bool open;

    public int FocusIndex => focus;
    public Button Focused => (focus >= 0 && focus < items.Count) ? items[focus] : null;

    private bool IsTop => stack.Count > 0 && stack[stack.Count - 1] == this;

    // 패널을 열 때 부른다. 같은 그룹을 다시 열면 목록만 갈아끼운다.
    public void Open(IList<Button> buttons, int initialIndex = 0)
    {
        Clear();
        for (int i = 0; i < buttons.Count; i++)
            if (buttons[i] != null) items.Add(buttons[i]);

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
        pointerDropped = false;
    }

    // 각 UI의 Update가 매 프레임 부른다. timeScale 0에서도 Update는 돌기 때문에 모달에서 그대로 쓸 수 있다.
    public void Tick()
    {
        if (!open || !IsTop) return;
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        Vector2 dir = Vector2.zero;
        if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) dir = Vector2.up;
        else if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) dir = Vector2.down;
        else if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) dir = Vector2.left;
        else if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) dir = Vector2.right;

        if (dir != Vector2.zero)
        {
            // 🔴 키보드로 옮기면 커서 호버 연출을 끈다. 커서가 가만히 있으면 Exit가 안 와서
            //    창이 뜰 때 커서 밑에 깔린 칸이 포커스를 떠난 뒤에도 밝고 큰 채로 남는다.
            DropPointerHover();
            Move(dir);
            return;
        }

        if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            Submit();

        // 키보드로 옮긴 뒤 마우스를 **움직이면** 커서 밑 칸으로 포커스를 되돌린다.
        // 호버를 꺼 둔 칸 위에서 커서를 살짝 움직이면 Enter가 다시 오지 않아서, 이게 없으면
        // 커서와 포커스가 서로 다른 칸을 가리킨 채로 남는다(아래 FocusFromPointer의 원칙).
        if (pointerDropped)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0f)
            {
                pointerDropped = false;
                FocusUnderPointer(mouse.position.ReadValue());
            }
        }
    }

    private bool pointerDropped;

    private void DropPointerHover()
    {
        for (int i = 0; i < items.Count; i++)
        {
            var juicy = items[i] != null ? items[i].GetComponent<JuicyButton>() : null;
            if (juicy != null) juicy.CancelHover();
        }
        pointerDropped = true;
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
                SetFocus(i);
                return;
            }
        }
    }

    private void Submit()
    {
        Button b = Focused;
        if (b == null || !b.IsInteractable() || !b.gameObject.activeInHierarchy) return;
        b.onClick.Invoke();
    }

    // 지금 칸에서 dir 방향으로 가장 "그럴듯한" 칸을 고른다.
    // 진행 거리(along)에 축을 벗어난 거리(perp)를 가중해서 더한다 — 벌점이 없으면
    // 바로 옆이 아니라 대각선 멀리 있는 칸으로 튄다.
    private void Move(Vector2 dir)
    {
        if (focus < 0) { SetFocus(NextUsable(-1, +1, true)); return; }

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

    private static Vector2 Center(Button b)
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

    private void SetFocus(int index)
    {
        if (index == focus) return;
        if (focus >= 0) Paint(focus, false);
        focus = index;
        if (focus >= 0) Paint(focus, true);
    }

    private void Paint(int i, bool on)
    {
        if (i < 0 || i >= items.Count || items[i] == null) return;

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
    private static Image EnsureGlow(Button b)
    {
        Transform found = UITreeUtil.FindDeep(b.transform, "SelectGlow");
        if (found != null) return found.GetComponent<Image>();

        Image src = b.GetComponent<Image>();
        Material mat = UISkin.SelectOutline;
        if (src == null || src.sprite == null || mat == null) return null;

        var go = new GameObject("SelectGlow", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(b.transform, false);
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
    private static readonly HashSet<Button> hooked = new HashSet<Button>();

    private void HookPointer(Button b)
    {
        if (!hooked.Add(b)) return;

        var trigger = b.GetComponent<EventTrigger>();
        if (trigger == null) trigger = b.gameObject.AddComponent<EventTrigger>();

        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entry.callback.AddListener(_ => FocusFromPointer(b));
        trigger.triggers.Add(entry);
    }

    private static void FocusFromPointer(Button b)
    {
        if (stack.Count == 0) return;
        UIFocusGroup top = stack[stack.Count - 1];
        int i = top.items.IndexOf(b);
        if (i >= 0 && top.Usable(i)) top.SetFocus(i);
    }
}
