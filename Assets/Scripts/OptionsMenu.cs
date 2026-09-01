using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

// 설정 패널 — 음량 3종 · 화면(전체화면/해상도/언어) · 세이브 초기화.
// 진입점: 타이틀 Btn_설정(TitleController) · 인게임 ESC 일시정지 창의 "설정" 버튼(PauseMenu).
//
// 🔴 **화면은 이 코드가 아니라 프리팹이 갖는다** — Assets/Prefabs/UI/OptionsPanel.prefab.
//    Title·Battle 두 씬에 그 프리팹 인스턴스가 놓여 있고(둘 다 Canvas 밖 최상위), 여기서는
//    참조를 받아 값을 채우고 리스너를 걸 뿐이다. 위치·크기·색·스프라이트는 전부 인스펙터에서 고친다.
//    (예전엔 이 클래스가 BuildUI()로 Canvas부터 통째로 지어서 인스펙터에 아무것도 안 보였다.)
//    판·글자 규격은 CLAUDE.md §5-1 표가 원본이다 — 여기 옮겨 적지 않는다.
//
// ⚠️ 프리팹에 구워진 값은 "구울 당시"의 것이라 믿으면 안 된다(슬라이더 1.00 · 해상도 640x480).
//    현재 상태를 보여줘야 하는 것은 Awake/Open에서 반드시 다시 채운다.
//
// 연출은 JuicyUI의 UITransition 대신 DOTween을 직접 쓴다: UITransition은 타입·visualRoot가
// private SerializeField라 인스펙터 없이는 설정할 수 없고, 기본값(Slide)이면 딤 배경까지 같이 밀려
// 화면 가장자리가 빈다. 여기선 딤=페이드 / 박스=팝으로 나눠 건다.
public class OptionsMenu : MonoBehaviour
{
    public static OptionsMenu Instance { get; private set; }

    // 세이브 초기화 버튼만 런타임에 색이 바뀐다(평상시 ↔ 확인 대기). 나머지 색은 프리팹이 갖는다.
    private static readonly Color DangerColor = new Color(0.55f, 0.20f, 0.28f, 1f);
    private static readonly Color DangerArmedColor = new Color(0.85f, 0.28f, 0.32f, 1f);

    [Header("골격")]
    [SerializeField] private GameObject panel;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform box;

    [Header("소리")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private TMP_Text masterValue;
    [SerializeField] private TMP_Text bgmValue;
    [SerializeField] private TMP_Text sfxValue;

    [Header("화면 · 언어")]
    [SerializeField] private Button resolutionPrev;
    [SerializeField] private Button resolutionNext;
    [SerializeField] private TMP_Text resolutionLabel;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Button languagePrev;
    [SerializeField] private Button languageNext;
    [SerializeField] private TMP_Text languageLabel;

    [Header("세이브 초기화 · 닫기")]
    [SerializeField] private GameObject saveResetRow;
    [SerializeField] private Button saveResetButton;
    [SerializeField] private Image saveResetBg;
    [SerializeField] private TMP_Text saveResetLabel;
    [SerializeField] private Button closeButton;

    [Header("언어가 바뀌면 다시 채우는 글자")]
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text masterRowLabel;
    [SerializeField] private TMP_Text bgmRowLabel;
    [SerializeField] private TMP_Text sfxRowLabel;
    [SerializeField] private TMP_Text resolutionRowLabel;
    [SerializeField] private TMP_Text fullscreenRowLabel;
    [SerializeField] private TMP_Text languageRowLabel;
    [SerializeField] private TMP_Text closeLabel;

    private Vector2Int[] resolutions;
    private int resolutionIndex;

    private Tween showTween;
    private float confirmUntil;   // 세이브 초기화 2단계 확인 마감 시각
    private bool isOpen;

    private const float ConfirmWindow = 4f;

    private void Awake()
    {
        Instance = this;
        VolumeSettings.EnsureLoaded();

        InitResolutions();
        BindListeners();
        SyncFromState();
        ApplyText();

        panel.SetActive(false);
        Loc.LocaleChanged += ApplyText;
    }

    private void OnDestroy()
    {
        Loc.LocaleChanged -= ApplyText;
        if (Instance == this) Instance = null;
    }

    // ── 배선 ──
    // 클로저를 쓰는 리스너는 프리팹에 직렬화되지 않는다(인스펙터의 OnClick 목록은 비어 있는 게 정상).
    // 그래서 여기서 건다. 프리팹을 다시 구워도 이 함수는 그대로 돈다.
    private void BindListeners()
    {
        BindSlider(masterSlider, masterValue, VolumeSettings.SetMaster);
        BindSlider(bgmSlider, bgmValue, VolumeSettings.SetBgm);
        BindSlider(sfxSlider, sfxValue, VolumeSettings.SetSfx);

        if (resolutionPrev != null) resolutionPrev.onClick.AddListener(() => StepResolution(-1));
        if (resolutionNext != null) resolutionNext.onClick.AddListener(() => StepResolution(1));
        if (languagePrev != null) languagePrev.onClick.AddListener(() => StepLanguage(-1));
        if (languageNext != null) languageNext.onClick.AddListener(() => StepLanguage(1));

        if (fullscreenToggle != null) fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
        if (saveResetButton != null) saveResetButton.onClick.AddListener(OnSaveResetClicked);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    private void BindSlider(Slider slider, TMP_Text valueText, System.Action<float> onChanged)
    {
        if (slider == null) return;
        slider.onValueChanged.AddListener(v =>
        {
            if (valueText != null) valueText.text = Percent(v);
            onChanged(v);
        });
    }

    // 프리팹에 굳어 있는 값을 지금 상태로 덮는다.
    private void SyncFromState()
    {
        SetSliderSilently(masterSlider, masterValue, VolumeSettings.Master);
        SetSliderSilently(bgmSlider, bgmValue, VolumeSettings.Bgm);
        SetSliderSilently(sfxSlider, sfxValue, VolumeSettings.Sfx);
        if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
        RefreshResolutionLabel();
    }

    private static void SetSliderSilently(Slider slider, TMP_Text valueText, float value)
    {
        if (slider != null) slider.SetValueWithoutNotify(value);
        if (valueText != null) valueText.text = Percent(value);
    }

    // 언어를 바꾸면 이 패널은 **자기 자신이 열려 있는 채로** 글자가 바뀌어야 한다.
    // 예전엔 패널을 통째로 파괴하고 다시 지었지만, 이제 씬 오브젝트라 그러면 인스턴스가 사라진다.
    // 글자만 다시 채운다.
    private void ApplyText()
    {
        SetText(titleLabel, "ui.options.title");
        SetText(masterRowLabel, "ui.options.master");
        SetText(bgmRowLabel, "ui.options.bgm");
        SetText(sfxRowLabel, "ui.options.sfx");
        SetText(resolutionRowLabel, "ui.options.resolution");
        SetText(fullscreenRowLabel, "ui.options.fullscreen");
        SetText(languageRowLabel, "ui.options.language");
        SetText(closeLabel, "ui.options.close");

        if (languageLabel != null) languageLabel.text = CurrentLanguageName();
        SetResetIdle();
    }

    private static void SetText(TMP_Text t, string key)
    {
        if (t != null) t.text = Loc.T(key);
    }

    // ── 열고 닫기 ──

    public bool IsOpen => isOpen;

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;

        // 세이브 초기화는 타이틀에서만 — 플레이 중 오발로 판을 날리지 않게 숨긴다.
        bool inGame = FindAnyObjectByType<PlayerSkills>() != null;
        if (saveResetRow != null) saveResetRow.SetActive(!inGame);
        SetResetIdle();

        // 창 밖에서 바뀌었을 수 있는 것들 — 해상도·전체화면은 이 창 말고도 바뀐다(빌드 설정·Alt+Enter).
        if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
        RefreshResolutionLabel();

        panel.SetActive(true);
        PlayShow();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        PlayHide();
    }

    private void Update()
    {
        if (confirmUntil > 0f && Time.unscaledTime > confirmUntil) SetResetIdle();
    }

    // 일시정지(timeScale 0) 위에서 열리므로 전부 SetUpdate(true).
    private void PlayShow()
    {
        showTween?.Kill();
        group.alpha = 0f;
        box.localScale = Vector3.one * 0.78f;

        var seq = DOTween.Sequence().SetUpdate(true);
        seq.Append(group.DOFade(1f, 0.09f));
        seq.Join(box.DOScale(1f, 0.2f).SetEase(Ease.OutBack));
        showTween = seq;
    }

    private void PlayHide()
    {
        showTween?.Kill();

        var seq = DOTween.Sequence().SetUpdate(true);
        seq.Append(box.DOScale(0.78f, 0.1f).SetEase(Ease.InBack));
        seq.Join(group.DOFade(0f, 0.1f));
        seq.OnComplete(() => panel.SetActive(false));
        showTween = seq;
    }

    // ── 동작 ──

    // 🔴 `Screen.fullScreen = on` 한 줄로는 **해상도가 창 모드의 것 그대로 남는다.**
    //    전체화면 창은 그걸 모니터 크기로 늘려 그리므로 1280×720짜리 버퍼가 1920×1080으로 퍼져
    //    도트가 뭉갠다("전체 화면 시 화질 저하", 8/27 빌드 QA — 창 모드에서만 선명했던 이유).
    //    전체화면으로 갈 땐 모니터 네이티브 해상도를 함께 지정해 1:1로 그리게 한다.
    private void SetFullscreen(bool on)
    {
        if (on)
        {
            // 창 크기를 되돌아올 자리로 기억해 둔다 — 안 하면 창 모드 복귀 때 모니터 크기 창이 뜬다.
            windowedSize = new Vector2Int(Screen.width, Screen.height);
            var native = Screen.currentResolution;
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
            SyncResolutionIndex(native.width, native.height);
        }
        else
        {
            var w = windowedSize.x > 0 ? windowedSize : new Vector2Int(Screen.width, Screen.height);
            Screen.SetResolution(w.x, w.y, FullScreenMode.Windowed);
            SyncResolutionIndex(w.x, w.y);
        }
        RefreshResolutionLabel();
    }

    private Vector2Int windowedSize; // 전체화면 직전의 창 크기(복귀용)

    // 해상도 라벨이 실제 화면과 어긋나지 않게 인덱스를 맞춘다.
    // ⚠️ `Screen.SetResolution` 직후에 `Screen.width`를 읽으면 **아직 옛 값**이라(다음 프레임에 반영)
    //    실제 화면이 아니라 **방금 지정한 값**으로 찾아야 한다.
    private void SyncResolutionIndex(int w, int h)
    {
        if (resolutions == null || resolutions.Length == 0) return;
        int i = System.Array.FindIndex(resolutions, r => r.x == w && r.y == h);
        if (i >= 0) resolutionIndex = i;
    }

    private void InitResolutions()
    {
        resolutions = Screen.resolutions
            .Select(r => new Vector2Int(r.width, r.height))
            .Distinct()
            .OrderBy(r => r.x).ThenBy(r => r.y)
            .ToArray();
        if (resolutions.Length == 0) resolutions = new[] { new Vector2Int(Screen.width, Screen.height) };

        resolutionIndex = System.Array.FindIndex(resolutions, r => r.x == Screen.width && r.y == Screen.height);
        if (resolutionIndex < 0) resolutionIndex = resolutions.Length - 1;
    }

    private void StepResolution(int delta)
    {
        if (resolutions == null || resolutions.Length == 0) return;
        resolutionIndex = Mathf.Clamp(resolutionIndex + delta, 0, resolutions.Length - 1);
        var r = resolutions[resolutionIndex];
        Screen.SetResolution(r.x, r.y, Screen.fullScreen);
        RefreshResolutionLabel();
    }

    private void RefreshResolutionLabel()
    {
        if (resolutionLabel == null || resolutions == null || resolutions.Length == 0) return;
        var r = resolutions[Mathf.Clamp(resolutionIndex, 0, resolutions.Length - 1)];
        resolutionLabel.text = $"{r.x} x {r.y}";
    }

    // 언어 이름은 **그 언어로** 보여준다("한국어"/"English") — 못 읽는 언어로 적히면 되돌아올 수가 없다.
    private static string CurrentLanguageName()
    {
        var ls = Loc.Locales;
        if (ls.Count == 0) return "?";
        var l = ls[Mathf.Clamp(Loc.CurrentIndex, 0, ls.Count - 1)];
        return string.IsNullOrEmpty(l.LocaleName) ? l.Identifier.Code : l.LocaleName;
    }

    private void StepLanguage(int delta)
    {
        int count = Loc.Locales.Count;
        if (count == 0) return;
        // 순환시킨다 — 언어가 둘뿐이라 끝에서 막히면 왕복이 어색하다(해상도와 다른 점).
        Loc.SetLocale(((Loc.CurrentIndex + delta) % count + count) % count);
        // 라벨 갱신은 안 한다 — SetLocale이 LocaleChanged를 쏘고 ApplyText가 languageLabel까지 채운다.
    }

    // 되돌릴 수 없으므로 두 번 눌러야 실제로 지운다(4초 안에 확인 없으면 원래 문구로 복귀).
    private void OnSaveResetClicked()
    {
        if (confirmUntil <= 0f)
        {
            confirmUntil = Time.unscaledTime + ConfirmWindow;
            saveResetLabel.text = Loc.T("ui.options.reset_confirm");
            saveResetBg.color = DangerArmedColor;
            return;
        }

        // 예전 빌드가 남긴 이름 모를 키까지 확실히 지우려고 통째로 비운다
        // (정수·스킬트리 해금·승천 해금이 전부 PlayerPrefs에 있음).
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        confirmUntil = 0f;
        saveResetLabel.text = Loc.T("ui.options.reset_done");
        saveResetBg.color = DangerColor;
    }

    private void SetResetIdle()
    {
        confirmUntil = 0f;
        if (saveResetLabel != null) saveResetLabel.text = Loc.T("ui.options.reset");
        if (saveResetBg != null) saveResetBg.color = DangerColor;
    }

    private static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";
}
