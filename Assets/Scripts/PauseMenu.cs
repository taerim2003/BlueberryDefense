using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

// ESC로 게임 일시정지 + 현재 획득한 스킬/패시브의 레벨·진화 효과 요약 표시.
// 씬에 배치할 필요 없이 게임 시작 시 자동 부트스트랩되어 자체 Canvas/UI를 런타임 생성한다.
public class PauseMenu : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("PauseMenu");
        DontDestroyOnLoad(go);
        go.AddComponent<PauseMenu>();
    }

    private GameObject panel;
    private TMP_Text bodyText;
    private bool paused;

    private void Awake() => BuildUI();

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
        if (paused) Resume();
        else if (CanPause()) PauseAndShow();
    }

    private bool CanPause()
    {
        if (Object.FindAnyObjectByType<PlayerSkills>() == null) return false; // 인게임(플레이어 존재)에서만
        var gm = GameManager.Instance;
        return gm == null || (!gm.IsGameOver && !gm.IsGameClear);
    }

    private void PauseAndShow()
    {
        paused = true;
        ModalPause.Push();
        bodyText.text = BuildSummary();
        panel.SetActive(true);
    }

    private void Resume()
    {
        paused = false;
        panel.SetActive(false);
        ModalPause.Pop();
    }

    // ── 스킬/패시브 요약 ──
    private static string BuildSummary()
    {
        var sb = new StringBuilder();
        var skills = Object.FindAnyObjectByType<PlayerSkills>();
        var passives = Object.FindAnyObjectByType<PlayerPassives>();

        sb.AppendLine("<b>[ 액티브 스킬 ]</b>");
        if (skills != null)
            foreach (var s in skills.EquippedSkills)
            {
                sb.AppendLine($"<b>{PlayerSkills.GetActiveSkillName(s.Id)}</b>  <color=#AECBFF>Lv.{s.Level}</color>");
                AppendPaths(sb, s.PathTier, (p, t) => PlayerSkills.GetPathTierTitle(s.Id, p, t));
            }

        sb.AppendLine();
        sb.AppendLine("<b>[ 패시브 ]</b>");
        if (passives != null)
            foreach (var pv in passives.EquippedPassives)
            {
                sb.AppendLine($"<b>{PlayerSkills.GetPassiveSkillName(pv.Id)}</b>  <color=#AECBFF>Lv.{pv.Level}</color>");
                AppendPaths(sb, pv.PathTier, (p, t) => PlayerPassives.GetPathTierTitle(pv.Id, p, t));
            }

        return sb.ToString();
    }

    private static void AppendPaths(StringBuilder sb, int[] pathTier, System.Func<int, int, string> titleFn)
    {
        for (int p = 0; p < pathTier.Length; p++)
        {
            int tier = pathTier[p];
            if (tier <= 0) continue;
            var parts = new List<string>();
            for (int t = 1; t <= tier; t++)
            {
                string title = titleFn(p, t);
                if (!string.IsNullOrEmpty(title)) parts.Add(title);
            }
            if (parts.Count > 0) sb.AppendLine("    <color=#FFC864>▸</color> " + string.Join(", ", parts));
        }
    }

    // ── 런타임 UI 생성 ──
    private void BuildUI()
    {
        TMP_FontAsset font = null;
        foreach (var t in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.font != null) { font = t.font; break; }

        var canvasGo = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        panel = NewUI("Panel", canvasGo.transform);
        Stretch(panel);
        AddImage(panel, new Color(0f, 0f, 0f, 0.8f), true);

        var box = NewUI("Box", panel.transform);
        Center(box, new Vector2(1000, 760));
        AddImage(box, new Color(0.06f, 0.06f, 0.1f, 0.98f), true);

        var title = NewUI("Title", box.transform);
        Top(title, new Vector2(0, -22), new Vector2(960, 64));
        AddText(title, font, "일시정지", 46, TextAlignmentOptions.Center, Color.white);

        var body = NewUI("Body", box.transform);
        var bodyRt = body.GetComponent<RectTransform>();
        bodyRt.anchorMin = Vector2.zero; bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = new Vector2(48, 70); bodyRt.offsetMax = new Vector2(-48, -104);
        bodyText = AddText(body, font, "", 24, TextAlignmentOptions.TopLeft, new Color(0.9f, 0.9f, 0.9f));
        bodyText.enableWordWrapping = true;

        var hint = NewUI("Hint", box.transform);
        Bottom(hint, new Vector2(0, 18), new Vector2(960, 40));
        AddText(hint, font, "ESC — 계속하기", 22, TextAlignmentOptions.Center, new Color(0.7f, 0.8f, 1f));

        panel.SetActive(false);
    }

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
    private static TMP_Text AddText(GameObject go, TMP_FontAsset font, string txt, int size, TextAlignmentOptions align, Color c)
    {
        var t = go.AddComponent<TextMeshProUGUI>();
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
}
