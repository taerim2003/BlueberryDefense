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

    // ── 색·치수 (아트 교체 시 여기만) ──
    private static readonly Color BoxColor = new Color(0.06f, 0.06f, 0.1f, 0.98f);
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.8f);
    private static readonly Color LabelColor = new Color(0.92f, 0.92f, 0.92f);
    private static readonly Color TrackColor = new Color(0.18f, 0.18f, 0.24f, 1f);
    private static readonly Color FillColor = new Color(0.45f, 0.62f, 0.95f, 1f);
    private static readonly Color HandleColor = new Color(0.85f, 0.9f, 1f, 1f);
    private static readonly Color ButtonColor = new Color(0.24f, 0.24f, 0.32f, 1f);
    private static readonly Color DangerColor = new Color(0.34f, 0.11f, 0.15f, 1f);
    private static readonly Color DangerArmedColor = new Color(0.62f, 0.16f, 0.18f, 1f);

    private const float RowWidth = 900f;
    private const float RowHeight = 60f;
    private const float RowStep = 76f;      // 행 간격
    private const float LabelWidth = 300f;  // 행 안에서 라벨이 차지하는 폭
    private const float SliderWidth = 440f;

    private GameObject panel;
    private CanvasGroup group;
    private RectTransform box;
    private TMP_FontAsset font;

    private TMP_Text resolutionLabel;
    private GameObject saveResetRow;
    private Image saveResetBg;
    private TMP_Text saveResetLabel;

    private Vector2Int[] resolutions;
    private int resolutionIndex;

    private Tween showTween;
    private float confirmUntil;   // 세이브 초기화 2단계 확인 마감 시각
    private bool isOpen;

    private const float ConfirmWindow = 4f;
    private const string ResetIdleText = "세이브 초기화";
    private const string ResetConfirmText = "정말? 한 번 더 클릭";
    private const string ResetDoneText = "초기화 완료";

    private void Awake()
    {
        Instance = this;
        VolumeSettings.EnsureLoaded();
        BuildUI();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

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
        AddImage(panel, DimColor, true);
        group = panel.AddComponent<CanvasGroup>();

        var boxGo = NewUI("Box", panel.transform);
        Center(boxGo, new Vector2(980, 720));
        AddImage(boxGo, BoxColor, true);
        box = (RectTransform)boxGo.transform;

        var title = NewUI("Title", boxGo.transform);
        Top(title, new Vector2(0, -28), new Vector2(RowWidth, 60));
        AddText(title, font, "설정", 46, TextAlignmentOptions.Center, Color.white);

        float y = -130f;
        MakeSliderRow(boxGo.transform, ref y, "전체 음량", VolumeSettings.Master, VolumeSettings.SetMaster);
        MakeSliderRow(boxGo.transform, ref y, "배경음", VolumeSettings.Bgm, VolumeSettings.SetBgm);
        MakeSliderRow(boxGo.transform, ref y, "효과음", VolumeSettings.Sfx, VolumeSettings.SetSfx);

        y -= 20f;
        MakeToggleRow(boxGo.transform, ref y, "전체화면", Screen.fullScreen, SetFullscreen);
        MakeResolutionRow(boxGo.transform, ref y);

        y -= 20f;
        MakeSaveResetRow(boxGo.transform, ref y);

        var close = MakeButton(boxGo.transform, "닫기", ButtonColor, Close);
        Bottom(close, new Vector2(0, 30), new Vector2(260, 60));
        JuicyTuning.CenterPivot(close);

        panel.SetActive(false);
    }

    // ── 행 ──

    private void MakeSliderRow(Transform parent, ref float y, string label, float value, System.Action<float> onChanged)
    {
        var row = MakeRow(parent, ref y, label);

        var valueGo = NewUI("Value", row.transform);
        Anchored(valueGo, new Vector2(0f, 0.5f), new Vector2(LabelWidth + SliderWidth + 20f, 0f), new Vector2(120, 44));
        var valueText = AddText(valueGo, font, Percent(value), 26, TextAlignmentOptions.MidlineLeft, LabelColor);

        var sliderGo = NewUI("Slider", row.transform);
        Anchored(sliderGo, new Vector2(0f, 0.5f), new Vector2(LabelWidth, 0f), new Vector2(SliderWidth, 36));
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
        Anchored(toggleGo, new Vector2(0f, 0.5f), new Vector2(LabelWidth, 0f), new Vector2(52, 52));
        var toggle = BuildToggle(toggleGo, value);
        toggle.onValueChanged.AddListener(v => onChanged(v));
    }

    private void MakeResolutionRow(Transform parent, ref float y)
    {
        var row = MakeRow(parent, ref y, "해상도");

        // 드롭다운(TMP_Dropdown) 대신 좌우 화살표 선택기 — 프리미티브만으로 조립할 수 있고 항목이 적다.
        resolutions = Screen.resolutions
            .Select(r => new Vector2Int(r.width, r.height))
            .Distinct()
            .OrderBy(r => r.x).ThenBy(r => r.y)
            .ToArray();
        if (resolutions.Length == 0) resolutions = new[] { new Vector2Int(Screen.width, Screen.height) };

        resolutionIndex = System.Array.FindIndex(resolutions, r => r.x == Screen.width && r.y == Screen.height);
        if (resolutionIndex < 0) resolutionIndex = resolutions.Length - 1;

        var prev = MakeButton(row.transform, "◀", ButtonColor, () => StepResolution(-1));
        Anchored(prev, new Vector2(0f, 0.5f), new Vector2(LabelWidth, 0f), new Vector2(56, 52));
        JuicyTuning.CenterPivot(prev);

        var valueGo = NewUI("ResolutionValue", row.transform);
        Anchored(valueGo, new Vector2(0f, 0.5f), new Vector2(LabelWidth + 64f, 0f), new Vector2(300, 52));
        AddImage(valueGo, TrackColor, false);
        resolutionLabel = AddText(valueGo, font, "", 28, TextAlignmentOptions.Center, LabelColor);
        RefreshResolutionLabel();

        var next = MakeButton(row.transform, "▶", ButtonColor, () => StepResolution(1));
        Anchored(next, new Vector2(0f, 0.5f), new Vector2(LabelWidth + 372f, 0f), new Vector2(56, 52));
        JuicyTuning.CenterPivot(next);
    }

    private void MakeSaveResetRow(Transform parent, ref float y)
    {
        saveResetRow = NewUI("SaveResetRow", parent);
        Anchored(saveResetRow, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(RowWidth, RowHeight));
        y -= RowStep;

        var btn = MakeButton(saveResetRow.transform, ResetIdleText, DangerColor, OnSaveResetClicked);
        Anchored(btn, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360, 56));
        saveResetBg = btn.GetComponent<Image>();
        saveResetLabel = btn.GetComponentInChildren<TMP_Text>();
    }

    // 라벨 자리를 가진 한 행. y를 다음 행 위치로 진행시킨다.
    private GameObject MakeRow(Transform parent, ref float y, string label)
    {
        var row = NewUI("Row_" + label, parent);
        Anchored(row, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(RowWidth, RowHeight));
        y -= RowStep;

        var labelGo = NewUI("Label", row.transform);
        Anchored(labelGo, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(LabelWidth, 44));
        AddText(labelGo, font, label, 30, TextAlignmentOptions.MidlineLeft, LabelColor);
        return row;
    }

    // ── 위젯 조립 (uGUI 표준 구조를 코드로) ──

    private Slider BuildSlider(GameObject go, float value)
    {
        var slider = go.AddComponent<Slider>();

        var background = NewUI("Background", go.transform);
        StretchWithAnchors(background, new Vector2(0f, 0.28f), new Vector2(1f, 0.72f));
        AddImage(background, TrackColor, true);

        var fillArea = NewUI("Fill Area", go.transform);
        StretchWithAnchors(fillArea, new Vector2(0f, 0.28f), new Vector2(1f, 0.72f));
        var fillAreaRt = (RectTransform)fillArea.transform;
        fillAreaRt.offsetMin = Vector2.zero;
        fillAreaRt.offsetMax = new Vector2(-20f, 0f);

        var fill = NewUI("Fill", fillArea.transform);
        var fillRt = (RectTransform)fill.transform;
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        fillRt.sizeDelta = new Vector2(20f, 0f);
        AddImage(fill, FillColor, true);

        var handleArea = NewUI("Handle Slide Area", go.transform);
        StretchWithAnchors(handleArea, Vector2.zero, Vector2.one);
        var handleAreaRt = (RectTransform)handleArea.transform;
        handleAreaRt.offsetMin = new Vector2(10f, 0f);
        handleAreaRt.offsetMax = new Vector2(-10f, 0f);

        var handle = NewUI("Handle", handleArea.transform);
        var handleRt = (RectTransform)handle.transform;
        handleRt.anchorMin = new Vector2(0f, 0f);
        handleRt.anchorMax = new Vector2(0f, 1f);
        handleRt.offsetMin = Vector2.zero;
        handleRt.offsetMax = Vector2.zero;
        handleRt.sizeDelta = new Vector2(32f, 0f);
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
        var bgImg = AddImage(background, TrackColor, true);

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

    private GameObject MakeButton(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var go = NewUI("Button_" + text, parent);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = AddImage(go, color, true);
        btn.onClick.AddListener(onClick);

        var label = NewUI("Label", go.transform);
        Stretch(label);
        AddText(label, font, text, 28, TextAlignmentOptions.Center, LabelColor);

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
            saveResetLabel.text = ResetConfirmText;
            saveResetBg.color = DangerArmedColor;
            return;
        }

        // 예전 빌드가 남긴 이름 모를 키까지 확실히 지우려고 통째로 비운다
        // (정수·스킬트리 해금·승천 해금이 전부 PlayerPrefs에 있음).
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        confirmUntil = 0f;
        saveResetLabel.text = ResetDoneText;
        saveResetBg.color = DangerColor;
    }

    private void SetResetIdle()
    {
        confirmUntil = 0f;
        if (saveResetLabel != null) saveResetLabel.text = ResetIdleText;
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
    private static TMP_Text AddText(GameObject go, TMP_FontAsset font, string txt, int size, TextAlignmentOptions align, Color c)
    {
        var host = go.GetComponent<Graphic>() != null ? NewUI("Text", go.transform) : go;
        if (host != go) Stretch(host);

        var t = host.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = txt; t.fontSize = size; t.alignment = align; t.color = c; t.raycastTarget = false;
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
