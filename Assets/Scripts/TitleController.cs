using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 타이틀 씬의 메인 메뉴. Play/업그레이드/컬렉션/설정/종료 + 크레딧(블루베리 버튼).
public class TitleController : MonoBehaviour
{
    [SerializeField] private Button playButton;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private Button collectionButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button creditsButton;
    [SerializeField] private SkillTreeUI skillTree;
    [SerializeField] private CreditsUI credits;
    [SerializeField] private MapSelectUI mapSelect;
    [SerializeField] private CharacterSelectUI characterSelect;

    // 게임오버 화면의 "업그레이드"가 세워두는 표시. 타이틀이 뜨면 스킬트리를 바로 연다.
    // static이라 씬 전환을 넘어가고, 한 번 쓰면 여기서 지운다(다음에 타이틀로 와도 안 열리게).
    public static bool OpenSkillTreeOnStart;

    private void Start()
    {
        if (playButton != null) playButton.onClick.AddListener(Play);
        if (upgradeButton != null) upgradeButton.onClick.AddListener(OpenUpgrade);
        if (collectionButton != null) collectionButton.onClick.AddListener(OpenCollection);
        if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        if (quitButton != null) quitButton.onClick.AddListener(Quit);
        if (creditsButton != null && credits != null) creditsButton.onClick.AddListener(credits.Open);

        ApplyCollectionGate();

        if (OpenSkillTreeOnStart)
        {
            OpenSkillTreeOnStart = false;
            OpenUpgrade();
        }
    }

    // ── 컬렉션(도감) 게이팅 ────────────────────────────────────────────────
    // 🔴 1차 진화 노드를 사기 전에는 컬렉션이 잠긴다(2026-09-27 사용자) — 도감 내용이 전부 진화라
    //    사기 전에 열면 빈 칸만 보인다. 사고 나서 메뉴에 처음 들어오면 한 번 축하 연출을 띄운다.
    // 🔴 판정은 SkillTreeSave로 한다. MetaBonuses.EvolutionUnlocked는 **쓰면 안 된다** —
    //    트리에 노드가 없으면 "개방"으로 보는 페일세이프이고(Meta.cs) 기본값이 true이며 판 시작 시에만 채워진다.
    private const string CollectionCelebratedKey = "tutorial.collection";

    private void ApplyCollectionGate()
    {
        if (collectionButton == null) return;
        bool unlocked = SkillTreeSave.IsUnlocked(SkillEffects.EvolutionNodeId);
        JuicyButton juicy = collectionButton.GetComponent<JuicyButton>();

        if (!unlocked)
        {
            // 🔴 두 줄이 짝이다 — JuicyButton은 Button.interactable을 안 본다(CharacterSelectUI와 같은 규칙).
            //    interactable만 끄면 잠긴 버튼이 호버에 반응해 "눌릴 것처럼" 보인다.
            // ⚠️ SetActive(false)로 숨기지 말 것 — TitleMenuIntro가 Layout의 자식을 순회해 시차 등장시킨다.
            collectionButton.interactable = false;
            if (juicy != null) juicy.enabled = false;
            return;
        }

        if (SaveStore.GetInt(CollectionCelebratedKey) != 0) return;
        // 띄운 순간 본 것으로 친다(TutorialHint와 같은 규칙) — 중간에 나가도 다시 안 뜨게.
        SaveStore.SetInt(CollectionCelebratedKey, 1);
        SaveStore.Save();
        StartCoroutine(CelebrateCollectionUnlock(juicy));
    }

    // 커짐 → 커진 채로 좌우 흔들림 → 제자리. 합계 약 2초.
    private IEnumerator CelebrateCollectionUnlock(JuicyButton juicy)
    {
        // 메뉴 등장 연출(TitleMenuIntro: 시차 0.2 × 순번 + 0.45초)이 끝난 뒤에 시작한다 —
        // 그 전엔 버튼이 화면 밖에서 미끄러져 들어오는 중이라 커져도 안 보인다.
        yield return new WaitForSeconds(1.1f);

        RectTransform rt = (RectTransform)collectionButton.transform;
        // 🔴 제자리는 Vector3.one이 아니다 — 이 버튼은 JuicyButton idleScale(0.92)로 평소 조금 작다.
        //    지금 localScale이 곧 그 "제자리"다(JuicyButton이 이미 적용해 둔 값).
        Vector3 rest = rt.localScale;
        CanvasGroup layout = collectionButton.GetComponentInParent<CanvasGroup>();

        if (juicy != null) juicy.enabled = false;   // 스케일 채널을 비운다
        if (layout != null) layout.interactable = false; // 연출 중 입력 차단
        // ⚠️ ModalPause는 타이틀에서 쓰면 안 된다 — QAInvariants가 title-paused 오류를 올린다.
        //    alpha도 건드리지 말 것(TitleMenuIntro 소유).
        rt.DOKill();

        Sequence s = DOTween.Sequence().SetUpdate(true);
        s.Append(rt.DOScale(rest * 1.55f, 0.28f).SetEase(Ease.OutBack, 2.2f)); // 세게 튀어 들어온다
        s.AppendInterval(0.12f);                                               // 커진 채 한 박자
        // 흔들림은 **회전**으로만 준다. JuicyButton.shakeOnHover를 켜면 히트박스가 돌아 Enter/Exit가 무한 반복한다.
        // 감쇠 없이 각도만 줄여 또렷하게 흔든다(DOPunchRotation의 자동 감쇠보다 선명하다).
        s.Append(rt.DOLocalRotate(new Vector3(0f, 0f, 9f), 0.11f).SetEase(Ease.InOutSine));
        s.Append(rt.DOLocalRotate(new Vector3(0f, 0f, -9f), 0.16f).SetEase(Ease.InOutSine));
        s.Append(rt.DOLocalRotate(new Vector3(0f, 0f, 6f), 0.14f).SetEase(Ease.InOutSine));
        s.Append(rt.DOLocalRotate(new Vector3(0f, 0f, -4f), 0.13f).SetEase(Ease.InOutSine));
        s.Append(rt.DOLocalRotate(Vector3.zero, 0.12f).SetEase(Ease.InOutSine));
        s.AppendInterval(0.15f);
        s.Append(rt.DOScale(rest, 0.45f).SetEase(Ease.OutCubic));              // 천천히 제자리
        s.OnComplete(() =>
        {
            // 회전을 반드시 0으로 되돌린다 — 남으면 기울어진 히트박스로 호버가 튄다.
            rt.localRotation = Quaternion.identity;
            rt.localScale = rest;
            if (juicy != null) juicy.enabled = true;
            if (layout != null) layout.interactable = true;
        });
    }

    // OptionsMenu는 씬에 프리팹 인스턴스로 놓여 있다 — 자기 Instance를 세우므로 클릭 시점에 찾는다.
    private void OpenSettings()
    {
        if (OptionsMenu.Instance != null) OptionsMenu.Instance.Open();
    }

    // CollectionUI도 같은 방식(씬의 프리팹 인스턴스)이라 클릭 시점에 찾는다.
    private void OpenCollection()
    {
        // 이중 방어 — 버튼을 잠그는 것만으로는 부족하다(MapSelectUI와 같은 규칙).
        if (!SkillTreeSave.IsUnlocked(SkillEffects.EvolutionNodeId)) return;
        if (CollectionUI.Instance != null) CollectionUI.Instance.Open();
    }

    private void Play()
    {
        // 캐릭터 → 맵 순으로 두 단계다. 캐릭터를 고르면 MapSelectUI가 이어받아 맵 화면을 연다.
        if (characterSelect != null) characterSelect.Open();
        else if (mapSelect != null) mapSelect.Open(); // 캐릭터 화면이 없으면 곧장 맵으로
    }

    private void OpenUpgrade()
    {
        if (skillTree != null) skillTree.Open();
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
