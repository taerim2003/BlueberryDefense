using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 타이틀 화면 우측 하단의 "세이브 초기화" 버튼.
// 빌드를 남에게 보냈을 때 예전 빌드의 저장값(PlayerPrefs)이 남아 이상하게 보이는 걸 플레이어가 직접 풀 수 있게 하는 용도.
// 되돌릴 수 없으므로 한 번 더 눌러야 실제로 지운다(4초 안에 확인 없으면 원래 문구로 복귀).
public class SaveResetButton : MonoBehaviour
{
    private const float ConfirmWindow = 4f;

    private const string IdleText = "세이브 초기화";
    private const string ConfirmText = "정말? 한 번 더 클릭";
    private const string DoneText = "초기화 완료";

    private static readonly Color IdleColor = new Color(0.22f, 0.16f, 0.18f, 0.9f);
    private static readonly Color ConfirmColor = new Color(0.62f, 0.16f, 0.18f, 0.95f);

    [SerializeField] private Canvas canvas; // 미지정이면 씬에서 찾아 붙는다

    private Image background;
    private TMP_Text label;
    private float confirmUntil;

    private void Start() => Build();

    private void Update()
    {
        // 확인 대기 시간이 지나면 실수 방지를 위해 기본 상태로 되돌린다
        if (confirmUntil > 0f && Time.unscaledTime > confirmUntil) SetIdle();
    }

    private void Build()
    {
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        var go = new GameObject("Btn_세이브초기화", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(canvas.transform, false);

        // 전체화면 패널(SkillTreeRoot 등)보다 앞 형제로 넣어야 패널이 열렸을 때 위로 튀어나오지 않는다
        Transform firstPanel = canvas.transform.Find("SkillTreeRoot");
        if (firstPanel != null) go.transform.SetSiblingIndex(firstPanel.GetSiblingIndex());

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f); // 우측 하단
        rt.anchoredPosition = new Vector2(-28f, 28f);
        rt.sizeDelta = new Vector2(240f, 56f);

        background = go.GetComponent<Image>();
        background.color = IdleColor;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = textRt.offsetMax = Vector2.zero;

        label = textGo.GetComponent<TextMeshProUGUI>();
        label.font = FindSceneFont();
        label.fontSize = 24;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.92f, 0.88f, 0.88f);
        label.raycastTarget = false;
        label.text = IdleText;

        go.GetComponent<Button>().onClick.AddListener(OnClick);
    }

    // 씬에 이미 쓰이는 TMP 폰트를 그대로 사용(PauseMenu와 같은 방식) — 한글이 깨지지 않게
    private static TMP_FontAsset FindSceneFont()
    {
        foreach (var t in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.font != null) return t.font;
        return null;
    }

    private void OnClick()
    {
        if (confirmUntil <= 0f) // 1단계: 확인 요청
        {
            confirmUntil = Time.unscaledTime + ConfirmWindow;
            label.text = ConfirmText;
            background.color = ConfirmColor;
            return;
        }

        // 2단계: 실제 삭제. 예전 빌드가 남긴 이름 모를 키까지 확실히 지우려고 통째로 비운다
        // (정수·스킬트리 해금·승천 해금이 전부 PlayerPrefs에 있음).
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        confirmUntil = 0f;
        label.text = DoneText;
        background.color = IdleColor;
    }

    private void SetIdle()
    {
        confirmUntil = 0f;
        label.text = IdleText;
        background.color = IdleColor;
    }
}
