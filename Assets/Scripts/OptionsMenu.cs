using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

// 설정 패널 — 음량 3종 · 화면(전체화면/해상도) · 세이브 초기화.
// PauseMenu와 같은 방식으로 씬 배치 없이 런타임에 자체 Canvas를 만들어 두 씬 모두에서 열린다.
// 진입점: 타이틀 Btn_설정(TitleController) · 인게임 ESC 일시정지 창의 "설정" 버튼(PauseMenu).
//
// 아트가 없어 전부 프리미티브(단색 Image + TMP)로 그린다 — 스프라이트가 나오면 위쪽 색 상수만 갈면 된다.
// 연출은 JuicyUI의 UITransition 대신 DOTween을 직접 쓴다: UITransition은 타입·visualRoot가
// private SerializeField라 인스펙터 없이는 설정할 수 없고, 기본값(Slide)이면 딤 배경까지 같이 밀려
// 화면 가장자리가 빈다. 여기선 딤=페이드 / 박스=팝으로 나눠 건다.
public class OptionsMenu : MonoBehaviour
{
    public static OptionsMenu Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("OptionsMenu");
        DontDestroyOnLoad(go);
        go.AddComponent<OptionsMenu>();
    }

    // ── 색·치수 ──
    // 판·버튼 바탕은 UISkin이 스프라이트째로 덮어쓴다(캐릭터/맵 선택 화면과 같은 옷).
    // 아래 색은 스킨 에셋이 없을 때의 폴백 + 스프라이트에 곱해질 색을 겸한다.
    // 큰 판을 없앴으니 딤이 배경을 가리는 유일한 수단이다. 0.8로는 배경이 비쳐 글자가 묻혔다 —
    // 스킬트리 화면과 같은 0.96(뒤가 안 보여도 되는 화면의 값)으로 올린다.
    private static readonly Color DimColor = new Color(0.031f, 0.020f, 0.051f, 0.96f);
    private static readonly Color LabelColor = Color.white;
    private static readonly Color TrackColor = new Color(0.06f, 0.05f, 0.10f, 0.9f);
    private static readonly Color FillColor = new Color(1f, 0.878f, 0.302f, 1f);   // 선택 화면의 강조 노랑
    private static readonly Color HandleColor = Color.white;
    private static readonly Color ButtonColor = new Color(0.420f, 0.482f, 0.910f, 1f);
    private static readonly Color DangerColor = new Color(0.55f, 0.20f, 0.28f, 1f);
    private static readonly Color DangerArmedColor = new Color(0.85f, 0.28f, 0.32f, 1f);

    // ── 그룹 상자 ──────────────────────────────────────────────
    // 라벨마다 판을 씌우는 대신 **여러 행을 상자 하나에 묶는다**(소리 3행 / 화면·언어 3행).
    // 개큰네모 721x289를 **원본 크기 그대로** 쓴다 — 테두리(좌25·아래88·우23·위34)라 안쪽이 673x167이고,
    // 거기에 행 51 + 간격 4로 3행이 정확히 들어간다(마지막 행 하단 -195 > 안쪽 하단 -201).
    private const float GroupW = 721f;
    private const float GroupH = 289f;
    private const float GroupFirstY = -34f;  // 상자 상단 기준 첫 행 y (= 안쪽 상단)
    private const float GroupRowStep = 55f;

    private const float RowWidth = 620f;     // 상자 안쪽(673)보다 조금 작게
    private const float RowHeight = 51f;
    private const float RowStep = GroupRowStep;
    private const float LabelWidth = 170f;   // **위젯이 시작하는 x** (라벨 글자 자리 뒤)

    // 볼륨 바는 게이지 3겹 그림을 **원본 크기 그대로** 쓴다(CLAUDE.md §5-1). 그래서 행이 그만큼 두꺼워진다.
    // ⚠️ 세 장의 원본 크기가 서로 다르다(바탕 387x101 · 채움 356x57 · 테두리 370x76).
    //    Battle 씬의 체력바가 **테두리(370x76)를 기준 칸**으로 잡고 바탕을 거기 맞춰 쓰므로 같은 방식을 따른다.
    // 상자 안쪽 행 높이(51)에 맞춰 원본(370x76)을 0.68배로 줄인다 — Simple+PA라 비율·그림자가 그대로다.
    private const float GaugeW = 250f;
    private const float GaugeH = 51f;
    private const float GaugeFillW = 241f;  // 356 x (250/370)
    private const float GaugeFillH = 38f;   // 57 x (51/76)
    private const float GaugeStep = GroupRowStep;

    private GameObject panel;
    private CanvasGroup group;
    private RectTransform box;
    private TMP_FontAsset font;

    private TMP_Text resolutionLabel;
    private TMP_Text languageLabel;
    private GameObject saveResetRow;
    private Image saveResetBg;
    private TMP_Text saveResetLabel;

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
        BuildUI();
        Loc.LocaleChanged += Rebuild;
    }

    private void OnDestroy()
    {
        Loc.LocaleChanged -= Rebuild;
        if (Instance == this) Instance = null;
    }

    // 언어를 바꾸면 이 패널은 **자기 자신이 열려 있는 채로** 글자가 바뀌어야 한다.
    // 런타임에 코드로 지은 UI라 TMP를 하나씩 찾아 고치는 대신 통째로 다시 짓는다(행이 20개도 안 된다).
    private void Rebuild()
    {
        bool wasOpen = isOpen;
        showTween?.Kill();
        if (panel != null) Destroy(panel);

        // 캔버스는 남기고 내용만 다시 — BuildUI가 캔버스부터 만들므로 옛 캔버스도 같이 지운다.
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);

        BuildUI();
        if (wasOpen) { isOpen = false; Open(); }
    }

    public bool IsOpen => isOpen;

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;

        // 세이브 초기화는 타이틀에서만 — 플레이 중 오발로 판을 날리지 않게 숨긴다.
        bool inGame = FindAnyObjectByType<PlayerSkills>() != null;
        if (saveResetRow != null) saveResetRow.SetActive(!inGame);
        SetResetIdle();
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

    // ── UI 생성 ──
    private void BuildUI()
    {
        font = FindSceneFont();

        var canvasGo = new GameObject("OptionsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1100; // PauseMenu(1000)보다 위 — 일시정지 창 위에서 열린다
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        panel = NewUI("Panel", canvasGo.transform);
        Stretch(panel);
        UISkin.Dim(AddImage(panel, DimColor, true), DimColor.a);
        group = panel.AddComponent<CanvasGroup>();

        var boxGo = NewUI("Box", panel.transform);
        // 🔴 큰 판을 깔지 않는다. 세트에 980x850짜리 그림이 없어서 373x195를 2.6x4.4배로 늘려 쓰고 있었고,
        //    9-slice는 테두리를 원본 픽셀로 그리므로 **아래 그림자만 통째로 두꺼워졌다**(CLAUDE.md §5-1).
        //    결과창(DamageMeterPanel)과 같은 방식으로 딤 위에 요소만 올린다 — Box는 배치용 빈 칸으로만 남는다.
        // 판이 없으니 이건 배치 기준 칸일 뿐이다 — 화면 전체를 좌표계로 쓴다(요소는 절대 y로 놓는다).
        Center(boxGo, new Vector2(1920, 1080));
        box = (RectTransform)boxGo.transform;

        var title = NewUI("Title", boxGo.transform);
        Anchored(title, new Vector2(0.5f, 0.5f), new Vector2(0f, 430f), new Vector2(RowWidth, 60));
        AddText(title, font, Loc.T("ui.options.title"), 46, TextAlignmentOptions.Center, Color.white);

        // 소리 3행을 상자 하나에 묶는다.
        var soundBox = MakeGroupBox(boxGo.transform, "SoundBox", 200f);
        float sy = GroupFirstY;
        MakeSliderRow(soundBox.transform, ref sy, Loc.T("ui.options.master"), VolumeSettings.Master, VolumeSettings.SetMaster);
        MakeSliderRow(soundBox.transform, ref sy, Loc.T("ui.options.bgm"), VolumeSettings.Bgm, VolumeSettings.SetBgm);
        MakeSliderRow(soundBox.transform, ref sy, Loc.T("ui.options.sfx"), VolumeSettings.Sfx, VolumeSettings.SetSfx);

        // 화면·언어 3행. 해상도가 전체화면보다 위다(사용자 지시).
        var screenBox = MakeGroupBox(boxGo.transform, "ScreenBox", -130f);
        float py = GroupFirstY;
        MakeResolutionRow(screenBox.transform, ref py);
        MakeToggleRow(screenBox.transform, ref py, Loc.T("ui.options.fullscreen"), Screen.fullScreen, SetFullscreen);
        MakeLanguageRow(screenBox.transform, ref py);

        MakeSaveResetRow(boxGo.transform, -350f); // 상자 밖

        var close = MakeButton(boxGo.transform, Loc.T("ui.options.close"), ButtonColor, Close);
        // 칸 비율을 그림(가로길쭉이 3.50)에 맞춘다 — Simple+PA에서 비율이 어긋나면 남는 쪽이 빈다.
        Anchored(close, new Vector2(0.5f, 0.5f), new Vector2(0f, -455f), new Vector2(260, 74));
        JuicyTuning.CenterPivot(close);

        UISkin.Refit(panel); // 크기가 다 정해진 뒤에 9-slice 테두리를 다시 재단
        panel.SetActive(false);
    }

    // ── 행 ──

    private void MakeSliderRow(Transform parent, ref float y, string label, float value, System.Action<float> onChanged)
    {
        var row = MakeRow(parent, ref y, label, RowHeight, GaugeStep);

        var valueGo = NewUI("Value", row.transform);
        Anchored(valueGo, new Vector2(0f, 0.5f), new Vector2(LabelWidth + GaugeW + 20f, 0f), new Vector2(120, 44));
        var valueText = AddText(valueGo, font, Percent(value), 26, TextAlignmentOptions.MidlineLeft, LabelColor);

        var sliderGo = NewUI("Slider", row.transform);
        Anchored(sliderGo, new Vector2(0f, 0.5f), new Vector2(LabelWidth, 0f), new Vector2(GaugeW, GaugeH));
        var slider = BuildSlider(sliderGo, value);
        slider.onValueChanged.AddListener(v =>
        {
            valueText.text = Percent(v);
            onChanged(v);
        });
    }

    private void MakeToggleRow(Transform parent, ref float y, string label, bool value, System.Action<bool> onChanged)
    {
        var row = MakeRow(parent, ref y, label);

        var toggleGo = NewUI("Toggle", row.transform);
        Anchored(toggleGo, new Vector2(0f, 0.5f), new Vector2(LabelWidth, 0f), new Vector2(40, 40));
        var toggle = BuildToggle(toggleGo, value);
        toggle.onValueChanged.AddListener(v => onChanged(v));
    }

    private void MakeResolutionRow(Transform parent, ref float y)
    {
        var row = MakeRow(parent, ref y, Loc.T("ui.options.resolution"));

        // 드롭다운(TMP_Dropdown) 대신 좌우 화살표 선택기 — 프리미티브만으로 조립할 수 있고 항목이 적다.
        resolutions = Screen.resolutions
            .Select(r => new Vector2Int(r.width, r.height))
            .Distinct()
            .OrderBy(r => r.x).ThenBy(r => r.y)
            .ToArray();
        if (resolutions.Length == 0) resolutions = new[] { new Vector2Int(Screen.width, Screen.height) };

        resolutionIndex = System.Array.FindIndex(resolutions, r => r.x == Screen.width && r.y == Screen.height);
        if (resolutionIndex < 0) resolutionIndex = resolutions.Length - 1;

        var prev = MakeButton(row.transform, "◀", ButtonColor, () => StepResolution(-1), true);
        Anchored(prev, new Vector2(0f, 0.5f), new Vector2(LabelWidth, 0f), new Vector2(44, 41));
        JuicyTuning.CenterPivot(prev);

        var valueGo = NewUI("ResolutionValue", row.transform);
        Anchored(valueGo, new Vector2(0f, 0.5f), new Vector2(LabelWidth + 54f, 0f), new Vector2(175, 50));
        UISkin.BarTinted(AddImage(valueGo, TrackColor, false), TrackColor);
        // "1920 x 1080"은 11자라 28pt면 175폭 칸에서 두 줄로 감긴다.
        resolutionLabel = AddText(valueGo, font, "", 19, TextAlignmentOptions.Center, LabelColor);
        RefreshResolutionLabel();

        var next = MakeButton(row.transform, "▶", ButtonColor, () => StepResolution(1), true);
        Anchored(next, new Vector2(0f, 0.5f), new Vector2(LabelWidth + 249f, 0f), new Vector2(44, 41));
        JuicyTuning.CenterPivot(next);
    }

    // 해상도 행과 같은 ◀▶ 선택기. 드롭다운을 안 쓰는 이유도 같다(프리미티브만으로 조립 가능·항목이 적음).
    private void MakeLanguageRow(Transform parent, ref float y)
    {
        var row = MakeRow(parent, ref y, Loc.T("ui.options.language"));

        var prev = MakeButton(row.transform, "◀", ButtonColor, () => StepLanguage(-1), true);
        Anchored(prev, new Vector2(0f, 0.5f), new Vector2(LabelWidth, 0f), new Vector2(44, 41));
        JuicyTuning.CenterPivot(prev);

        var valueGo = NewUI("LanguageValue", row.transform);
        Anchored(valueGo, new Vector2(0f, 0.5f), new Vector2(LabelWidth + 54f, 0f), new Vector2(175, 50));
        UISkin.BarTinted(AddImage(valueGo, TrackColor, false), TrackColor);
        languageLabel = AddText(valueGo, font, CurrentLanguageName(), 28, TextAlignmentOptions.Center, LabelColor);

        var next = MakeButton(row.transform, "▶", ButtonColor, () => StepLanguage(1), true);
        Anchored(next, new Vector2(0f, 0.5f), new Vector2(LabelWidth + 249f, 0f), new Vector2(44, 41));
        JuicyTuning.CenterPivot(next);
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
        // 라벨 갱신은 안 한다 — SetLocale이 LocaleChanged를 쏘고 Rebuild가 패널을 통째로 다시 짓는다.
    }

    private void MakeSaveResetRow(Transform parent, float centerY)
    {
        saveResetRow = NewUI("SaveResetRow", parent);
        Anchored(saveResetRow, new Vector2(0.5f, 0.5f), new Vector2(0f, centerY), new Vector2(RowWidth, 103f));

        var btn = MakeButton(saveResetRow.transform, Loc.T("ui.options.reset"), DangerColor, OnSaveResetClicked);
        Anchored(btn, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360, 103));
        saveResetBg = btn.GetComponent<Image>();
        saveResetLabel = btn.GetComponentInChildren<TMP_Text>();
    }

    // 여러 행을 묶는 상자. 개큰네모(721x289)를 **원본 크기 그대로** 쓴다.
    private GameObject MakeGroupBox(Transform parent, string name, float centerY)
    {
        var go = NewUI(name, parent);
        Anchored(go, new Vector2(0.5f, 0.5f), new Vector2(0f, centerY), new Vector2(GroupW, GroupH));
        UISkin.BigBox(AddImage(go, ButtonColor, true)); // 스킨이 없으면 버튼 색 폴백
        return go;
    }

    // 라벨 자리를 가진 한 행. y를 다음 행 위치로 진행시킨다.
    private GameObject MakeRow(Transform parent, ref float y, string label)
        => MakeRow(parent, ref y, label, RowHeight, RowStep);

    private GameObject MakeRow(Transform parent, ref float y, string label, float height, float step)
    {
        var row = NewUI("Row_" + label, parent);
        Anchored(row, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(RowWidth, height));
        y -= step;

        // 배경은 그룹 상자가 맡으므로 라벨은 글자만 둔다(라벨마다 판을 씌우면 상자와 겹쳐 어수선해진다).
        var labelGo = NewUI("Label", row.transform);
        Anchored(labelGo, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(LabelWidth - 20f, height));
        AddText(labelGo, font, label, 26, TextAlignmentOptions.MidlineLeft, LabelColor);
        return row;
    }

    // ── 위젯 조립 (uGUI 표준 구조를 코드로) ──

    private Slider BuildSlider(GameObject go, float value)
    {
        var slider = go.AddComponent<Slider>();

        // ── 게이지 3겹 (CLAUDE.md §5-1) ──
        // 바탕(체력바_색칠)과 테두리(체력바_투명)는 **원본 크기 그대로** 겹치고, 그 사이에서 채움만 좌우로 찬다.
        // 스킨 에셋이 없으면 예전 단색 막대로 폴백한다.
        Sprite trackSp = UISkin.GaugeTrackSprite;
        Sprite fillSp  = UISkin.GaugeFillSprite;
        Sprite outerSp = UISkin.Instance != null ? UISkin.Instance.gaugeOuter : null;

        var background = NewUI("Background", go.transform);
        Center(background, new Vector2(GaugeW, GaugeH));
        // 🔴 `_색칠` 그림은 속이 흰색이라 **색을 곱해** 쓴다. white를 주면 그냥 흰 판이 된다.
        var bgImg = AddImage(background, TrackColor, true);
        // 바탕만 원본(387x101)이 기준 칸(370x76)보다 커서 Battle 씬 체력바와 같이 Sliced로 맞춘다.
        if (trackSp != null) { bgImg.sprite = trackSp; UISkin.FitSlice(bgImg); }

        var fillArea = NewUI("Fill Area", go.transform);
        Center(fillArea, new Vector2(GaugeFillW, GaugeFillH));

        var fill = NewUI("Fill", fillArea.transform);
        var fillRt = (RectTransform)fill.transform;
        // Slider는 fillRect의 anchor를 0..value로 움직인다 — 부모(Fill Area) 안에서 좌→우로 찬다.
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fillImg = AddImage(fill, FillColor, true); // 채움도 곱해 쓰는 그림 — 선택 화면의 강조 노랑
        if (fillSp != null) { fillImg.sprite = fillSp; fillImg.type = Image.Type.Simple; }

        // 테두리는 채움 **위에** 덮는다(마지막에 만든 자식이 맨 위) — 그래야 채움이 테두리를 넘지 않는다.
        if (outerSp != null)
        {
            var outerGo = NewUI("Outer", go.transform);
            Center(outerGo, new Vector2(GaugeW, GaugeH));
            var oImg = AddImage(outerGo, Color.white, false); // 클릭은 아래 슬라이더가 받아야 한다
            oImg.sprite = outerSp; oImg.type = Image.Type.Simple; oImg.preserveAspect = true;
        }

        var handleArea = NewUI("Handle Slide Area", go.transform);
        Center(handleArea, new Vector2(GaugeFillW, GaugeH));

        var handle = NewUI("Handle", handleArea.transform);
        var handleRt = (RectTransform)handle.transform;
        handleRt.anchorMin = new Vector2(0f, 0f);
        handleRt.anchorMax = new Vector2(0f, 1f);
        handleRt.offsetMin = Vector2.zero;
        handleRt.offsetMax = Vector2.zero;
        handleRt.sizeDelta = new Vector2(26f, -34f); // 게이지 위라 얇고 짧게
        var handleImg = AddImage(handle, HandleColor, true);

        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(value);
        return slider;
    }

    private Toggle BuildToggle(GameObject go, bool value)
    {
        var toggle = go.AddComponent<Toggle>();

        var background = NewUI("Background", go.transform);
        StretchWithAnchors(background, Vector2.zero, Vector2.one);
        // 토글 칸은 버튼 색으로 칠한다 — TrackColor(거의 검정)면 어두운 딤 위에서 통째로 묻힌다.
        var bgImg = AddImage(background, ButtonColor, true);
        UISkin.BoxTinted(bgImg, ButtonColor); // 정사각 토글이라 가로 바가 아니라 사각 스프라이트

        var check = NewUI("Checkmark", background.transform);
        StretchWithAnchors(check, Vector2.zero, Vector2.one);
        var checkRt = (RectTransform)check.transform;
        checkRt.offsetMin = new Vector2(9f, 9f);
        checkRt.offsetMax = new Vector2(-9f, -9f);
        var checkImg = AddImage(check, FillColor, false);

        toggle.targetGraphic = bgImg;
        toggle.graphic = checkImg;
        toggle.SetIsOnWithoutNotify(value);
        checkImg.enabled = value;
        return toggle;
    }

    private GameObject MakeButton(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick, bool bodyFont = false)
    {
        var go = NewUI("Button_" + text, parent);
        var btn = go.AddComponent<Button>();
        var bg = AddImage(go, color, true);
        UISkin.BarTinted(bg, color); // 색은 호출측 것(위험 버튼은 빨강), 모양만 스킨
        btn.targetGraphic = bg;
        btn.onClick.AddListener(onClick);

        var label = NewUI("Label", go.transform);
        Stretch(label);
        AddText(label, font, text, 28, TextAlignmentOptions.Center, LabelColor, bodyFont);

        JuicyTuning.Attach(go); // 호버 스쿼시 + 클릭 눌림 (손맛 값은 JuicyTuning이 단일 소스)
        return go;
    }

    // ── 동작 ──

    private void SetFullscreen(bool on) => Screen.fullScreen = on;

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

    // 씬에 이미 쓰이는 TMP 폰트를 그대로 사용(PauseMenu와 같은 방식) — 한글이 깨지지 않게
    private static TMP_FontAsset FindSceneFont()
    {
        foreach (var t in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.font != null) return t.font;
        return null;
    }

    // ── RectTransform 헬퍼 ──

    private static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image AddImage(GameObject go, Color c, bool raycast)
    {
        var img = go.AddComponent<Image>();
        img.color = c; img.raycastTarget = raycast;
        return img;
    }

    // Image가 이미 붙은 오브젝트면 텍스트를 자식으로 깐다(한 오브젝트에 Graphic 둘은 불가).
    // body=true면 본문 폰트(Pretendard). ⚠️ ◀▶ 같은 기호는 픽셀 폰트에 글리프가 없어 □로 나온다 —
    // 맵 선택 화면의 승천 화살표가 본문 폰트를 쓰는 것도 같은 이유다.
    private static TMP_Text AddText(GameObject go, TMP_FontAsset font, string txt, int size, TextAlignmentOptions align, Color c, bool body = false)
    {
        var host = go.GetComponent<Graphic>() != null ? NewUI("Text", go.transform) : go;
        if (host != go) Stretch(host);

        var t = host.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = txt; t.fontSize = size; t.alignment = align; t.color = c; t.raycastTarget = false;
        UISkin.Text(t, body); // 폰트·아웃라인 머티리얼을 선택 화면과 같게 (fontSize를 정한 뒤라야 크기별 머티리얼이 갈린다)
        return t;
    }

    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void StretchWithAnchors(GameObject go, Vector2 min, Vector2 max)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void Center(GameObject go, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
    }

    private static void Top(GameObject go, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    private static void Bottom(GameObject go, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    // anchor 한 점에 붙여 배치. pivot을 anchor와 같게 잡아 위치 계산을 단순하게 유지한다.
    private static void Anchored(GameObject go, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }
}
