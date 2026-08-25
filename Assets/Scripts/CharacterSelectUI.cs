using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 맵 선택 화면 위에 뜨는 캐릭터 선택 팝업. MapSelectUI의 "캐릭터 변경" 버튼이 연다.
// MapSelectUI(맵 선택)와 대칭인 카드 UI지만, 씬을 로드하지 않고 선택값만 갱신한 뒤 팝업을 닫는다.
// 카드 클릭 = 즉시 선택 후 닫힘. 선택이 바뀌면 OnSelectionChanged로 MapSelectUI 표시를 갱신.
public class CharacterSelectUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private CharacterDefinition[] characters;
    [SerializeField] private Sprite lockIcon; // 비우면 코드로 구운 자물쇠를 쓴다

    [Header("Shell")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private UITransition panelTransition; // 있으면 열고 닫을 때 팝 연출을 대신 태운다
    [SerializeField] private PanelSplitTransition splitTransition; // 위아래로 갈라지는 화면 전환(우선)
    [SerializeField] private Transform cardContainer;   // 카드들이 담기는 컨테이너
    [SerializeField] private GameObject cardTemplate;    // 비활성 카드 원본. 자식: Thumb(Image)/Name(TMP_Text)/Frame(Image)

    [Header("Actions")]
    [SerializeField] private Button backButton;
    [SerializeField] private Button confirmButton; // 이걸 눌러야 다음 단계(맵 선택)로 넘어간다

    [Header("시작 스킬 미리보기 (선택한 캐릭터의 Q)")]
    [SerializeField] private Sprite[] skillIcons;   // ActiveSkillId 순서로 넣는다
    [SerializeField] private Image skillIcon;
    [SerializeField] private TMP_Text skillNameText;
    [SerializeField] private TMP_Text skillDescText;

    // 선택이 바뀌면 발생. MapSelectUI가 구독해 현재 캐릭터 표시를 갱신한다.
    public event Action OnSelectionChanged;

    // "선택" 버튼으로 확정했을 때 발생. 다음 단계(맵 선택)가 이걸 듣고 열린다.
    public event Action OnConfirmed;

    // 잠긴 캐릭터 카드의 초상화 색 — 거의 검은 실루엣만 남긴다.
    private static readonly Color LockedSilhouette = new Color(0.08f, 0.08f, 0.1f, 0.85f);

    // 고른 카드 뒤에 깔리는 노란 테(카드의 `SelectGlow`). 없는 카드는 null.
    private readonly List<Image> cardGlows = new List<Image>();
    private readonly List<JuicyButton> cardJuicy = new List<JuicyButton>();  // 고른 카드만 원본 크기·색으로 남긴다
    private readonly List<Image> cardThumbs = new List<Image>();             // 클릭 시 공격 모션을 여기서 돌린다
    private int selectedIndex;   // 기본 0 = 로스터 첫 캐릭터(= 프리팹 기본값과 동일)
    private bool built;
    private Coroutine attackRoutine;
    private Image attackThumb;        // 지금 공격 모션이 도는 썸네일(끊겼을 때 되돌리려고 들고 있는다)
    private Sprite attackPortrait;
    private Vector2 attackHome;
    private Vector3 attackHomeScale;

    // 현재 선택된 캐릭터. 로스터가 비어 있으면 null(→ RunConfig.Character=null → 프리팹 기본값).
    public CharacterDefinition Selected =>
        characters != null && selectedIndex >= 0 && selectedIndex < characters.Length
            ? characters[selectedIndex] : null;

    private void Awake()
    {
        RestoreSelection(); // 카드를 짓기 전에 복원해야 Open() 없이도 Selected가 옳다(MapSelectUI가 바로 읽는다)
        if (backButton != null) backButton.onClick.AddListener(Close);
        if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
        if (cardTemplate != null) cardTemplate.SetActive(false);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // 지난 판에서 고른 캐릭터를 되살린다. 저장된 이름이 로스터에 없거나(에셋 개명) 아직 잠겨 있으면
    // 조용히 첫 캐릭터로 돌아간다 — 선택 불가 캐릭터로 판이 시작되는 것보다 낫다.
    private void RestoreSelection()
    {
        string saved = CharacterSave.Selected;
        if (string.IsNullOrEmpty(saved) || characters == null) return;

        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] == null || characters[i].name != saved) continue;
            if (!characters[i].IsUnlocked) return; // 잠긴 상태면 폴백(selectedIndex=0 유지)
            selectedIndex = i;
            return;
        }
    }

    public void Open()
    {
        if (!built) BuildCards();
        if (splitTransition != null) splitTransition.Show();
        else if (panelTransition != null) panelTransition.Show();
        else if (panelRoot != null) panelRoot.SetActive(true);
        Highlight(selectedIndex);
    }

    public void Close()
    {
        if (splitTransition != null) splitTransition.Hide();
        else if (panelTransition != null) panelTransition.Hide();
        else if (panelRoot != null) panelRoot.SetActive(false);
    }

    // "선택" 버튼: 여기서만 다음 단계로 넘어간다(카드 클릭은 고르기까지만).
    private void Confirm()
    {
        Close();
        OnConfirmed?.Invoke();
    }

    private void BuildCards()
    {
        built = true;
        if (characters == null || cardTemplate == null || cardContainer == null) return;

        for (int i = 0; i < characters.Length; i++)
        {
            var chr = characters[i];
            var card = Instantiate(cardTemplate, cardContainer);
            card.SetActive(true);

            bool locked = chr != null && !chr.IsUnlocked;

            var thumb = FindDeep(card.transform, "Thumb")?.GetComponent<Image>();
            if (thumb != null)
            {
                bool hasPortrait = chr != null && chr.portrait != null;
                if (hasPortrait) thumb.sprite = chr.portrait;
                thumb.enabled = hasPortrait; // 초상화 없으면 빈 박스 대신 숨김(이름만 표시)
                // 잠긴 캐릭터는 실루엣으로만 보여준다 — 뭐가 있는지는 알되 누군지는 모르게.
                thumb.color = locked ? LockedSilhouette : Color.white;
            }
            cardThumbs.Add(thumb);

            if (locked) LockBadge.Add(card, lockIcon);

            var nameText = FindDeep(card.transform, "Name")?.GetComponent<TMP_Text>();
            if (nameText != null && chr != null)
                nameText.text = locked ? "???" : chr.Name;

            // 🔴 맵 선택 화면과 같은 사정 — `Frame` 자식이 사라져 하이라이트가 죽어 있었다.
            //    테두리 그림(..._투명)은 순수 검정이라 물들일 수 없어서, `SelectGlow`가 그 선화를
            //    마스크로 쓰고 그 안의 `Fill`을 노랗게 켠다(맵 선택 화면과 같은 구조).
            cardGlows.Add(FindDeep(card.transform, "Fill")?.GetComponent<Image>());

            int idx = i; // 클로저 캡처
            var juicy = card.GetComponent<JuicyButton>();
            cardJuicy.Add(juicy);

            var btn = card.GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = !locked; // 잠긴 카드는 눌러도 선택되지 않는다
                btn.onClick.AddListener(() => Pick(idx));

                // JuicyButton은 Button.interactable을 보지 않는다 — 끄지 않으면 잠긴 카드도 호버에 반응한다.
                if (juicy != null) juicy.enabled = !locked;
            }

        }
    }

    // 카드 속 부품을 깊이와 상관없이 찾는다(MapSelectUI와 같은 이유 — 씬에서 Thumb을
    // Bg 아래로 옮기면 Transform.Find는 조용히 null을 준다).
    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // 카드 클릭: 선택 확정 → 표시 갱신 알림 → 팝업 닫기.
    private void Pick(int index)
    {
        if (characters != null && index >= 0 && index < characters.Length
            && characters[index] != null && !characters[index].IsUnlocked) return; // 잠긴 캐릭터는 선택 불가

        selectedIndex = index;
        if (characters[index] != null) CharacterSave.Save(characters[index].name); // 다음 판에도 이 캐릭터로 시작한다
        Highlight(index);
        PlayAttack(index);
        OnSelectionChanged?.Invoke();
        // 여기서 닫지 않는다 — 확정은 "선택" 버튼이 한다.
    }

    // 고른 카드의 그림이 공격 모션을 한 번 훑고 초상화로 돌아온다.
    // 카드를 연달아 누르면 앞의 재생을 끊고 그 카드의 초상화를 되돌려 놓는다.
    private void PlayAttack(int index)
    {
        if (attackRoutine != null) { StopCoroutine(attackRoutine); attackRoutine = null; }
        EndAttackFraming(); // 끊겼든 끝났든 썸네일을 원래 그림·크기·자리로

        var chr = characters != null && index >= 0 && index < characters.Length ? characters[index] : null;
        if (chr == null || chr.attackFrames == null || chr.attackFrames.Length == 0) return;
        if (index >= cardThumbs.Count || cardThumbs[index] == null) return;

        attackRoutine = StartCoroutine(AttackRoutine(cardThumbs[index], chr));
    }

    private IEnumerator AttackRoutine(Image thumb, CharacterDefinition chr)
    {
        BeginAttackFraming(thumb, chr);

        float step = Mathf.Max(0.02f, chr.attackFrameSeconds);
        foreach (var frame in chr.attackFrames)
        {
            if (frame != null) thumb.sprite = frame;
            // 이 화면은 timeScale이 0일 수 있다(모달 위에서 열린다) — 실시간으로 센다.
            yield return new WaitForSecondsRealtime(step);
        }

        attackRoutine = null;
        EndAttackFraming();
    }

    // 공격 캔버스가 초상화보다 크면 몸통이 작아 보인다 — 재생하는 동안만 키우고 민다.
    private void BeginAttackFraming(Image thumb, CharacterDefinition chr)
    {
        attackThumb = thumb;
        attackPortrait = chr.portrait;
        var rt = thumb.rectTransform;
        attackHome = rt.anchoredPosition;
        attackHomeScale = rt.localScale;

        if (Mathf.Approximately(chr.attackFrameScale, 1f) && chr.attackFrameOffset == Vector2.zero) return;
        Rect r = rt.rect;
        rt.localScale = attackHomeScale * chr.attackFrameScale;
        rt.anchoredPosition = attackHome
            + new Vector2(chr.attackFrameOffset.x * r.width, chr.attackFrameOffset.y * r.height);
    }

    private void EndAttackFraming()
    {
        if (attackThumb == null) return;
        var rt = attackThumb.rectTransform;
        if (attackPortrait != null) attackThumb.sprite = attackPortrait;
        rt.anchoredPosition = attackHome;
        rt.localScale = attackHomeScale;
        attackThumb = null;
        attackPortrait = null;
    }

    private void Highlight(int index)
    {
        RefreshFrames();
        RefreshSkillPreview(index);
    }

    // 고른 카드는 뒤에 깔린 노란 테를 켜서 표시한다(맵 선택 화면과 같은 방식).
    // 크기·색 강조는 JuicyButton이 따로 맡는다.
    private void RefreshFrames()
    {
        for (int i = 0; i < cardGlows.Count; i++)
            if (cardGlows[i] != null) cardGlows[i].color = i == selectedIndex ? UISkin.Highlight : UISkin.Transparent;
        for (int i = 0; i < cardJuicy.Count; i++)
            if (cardJuicy[i] != null) cardJuicy[i].SetSelected(i == selectedIndex);
    }

    // 고른 캐릭터가 어떤 스킬로 시작하는지 아이콘·이름·설명으로 보여준다.
    // 이름과 설명은 인게임과 같은 출처를 쓴다(문구가 두 벌로 갈리지 않게).
    private void RefreshSkillPreview(int index)
    {
        var chr = characters != null && index >= 0 && index < characters.Length ? characters[index] : null;
        if (chr == null) return;

        ActiveSkillId id = chr.startingSkill;

        if (skillIcon != null)
        {
            Sprite sp = skillIcons != null && (int)id < skillIcons.Length ? skillIcons[(int)id] : null;
            skillIcon.sprite = sp;
            skillIcon.enabled = sp != null; // 아이콘이 없으면 흰 사각형 대신 숨긴다
        }
        if (skillNameText != null) skillNameText.text = PlayerSkills.GetActiveSkillName(id);
        if (skillDescText != null) skillDescText.text = LevelUpUI.GetActiveSkillDescription(id);
    }
}
