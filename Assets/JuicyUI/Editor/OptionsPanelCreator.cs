#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class OptionsPanelCreator
{
    [MenuItem("JuicyUI/Create Options Panel")]
    public static void CreateOptionsPanel()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        Transform parent = canvas != null ? canvas.transform : null;

        // ── Root ──────────────────────────────────────────────
        var root = UI("OptionsPanel", parent);
        FullScreen(root);
        root.AddComponent<CanvasGroup>();
        root.AddComponent<UITransition>();
        root.AddComponent<OptionsPanel>();

        // ── Overlay ───────────────────────────────────────────
        var overlay = UI("Overlay", root.transform);
        FullScreen(overlay);
        overlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        // ── Panel Box ─────────────────────────────────────────
        var box = UI("PanelBox", root.transform);
        Rect(box, Vector2.zero, new Vector2(520f, 560f));
        box.AddComponent<Image>().color = new Color(0.13f, 0.13f, 0.16f);

        var vLayout = box.AddComponent<VerticalLayoutGroup>();
        vLayout.padding = new RectOffset(28, 28, 24, 24);
        vLayout.spacing = 10;
        vLayout.childControlWidth  = true;
        vLayout.childControlHeight = false;
        vLayout.childForceExpandWidth  = true;
        vLayout.childForceExpandHeight = false;

        // ── Header ────────────────────────────────────────────
        var header = UI("Header", box.transform);
        LE(header, height: 56);
        var hLayout = header.AddComponent<HorizontalLayoutGroup>();
        hLayout.childAlignment       = TextAnchor.MiddleLeft;
        hLayout.childControlHeight   = true;
        hLayout.childControlWidth    = false;
        hLayout.childForceExpandWidth  = false;
        hLayout.childForceExpandHeight = true;

        var titleObj = UI("Title", header.transform);
        LE(titleObj, flexW: true);
        var title = titleObj.AddComponent<TextMeshProUGUI>();
        title.text      = "Settings";
        title.fontSize  = 26;
        title.fontStyle = FontStyles.Bold;
        title.color     = Color.white;
        title.alignment = TextAlignmentOptions.MidlineLeft;

        var closeBtn = UI("CloseButton", header.transform);
        LE(closeBtn, width: 48, height: 48);
        closeBtn.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
        closeBtn.AddComponent<JuicyButton>();
        TMPLabel(closeBtn, "✕", 20, new Color(0.75f, 0.75f, 0.75f));

        // ── Separator ─────────────────────────────────────────
        Separator(box);

        // ── Content (옵션을 여기에 배치) ──────────────────────
        var content = UI("Content", box.transform);
        LE(content, height: 360);
        var cLayout = content.AddComponent<VerticalLayoutGroup>();
        cLayout.spacing            = 8;
        cLayout.childControlWidth  = true;
        cLayout.childControlHeight = false;
        cLayout.childForceExpandWidth  = true;
        cLayout.childForceExpandHeight = false;

        // ── Separator ─────────────────────────────────────────
        Separator(box);

        // ── Footer ────────────────────────────────────────────
        var footer = UI("Footer", box.transform);
        LE(footer, height: 44);
        var fLayout = footer.AddComponent<HorizontalLayoutGroup>();
        fLayout.childAlignment       = TextAnchor.MiddleRight;
        fLayout.childControlHeight   = true;
        fLayout.childControlWidth    = false;
        fLayout.childForceExpandWidth  = false;
        fLayout.childForceExpandHeight = true;

        var resetBtn = UI("ResetButton", footer.transform);
        LE(resetBtn, width: 190, height: 40);
        resetBtn.AddComponent<Image>().color = new Color(0.28f, 0.28f, 0.33f);
        resetBtn.AddComponent<JuicyButton>();
        TMPLabel(resetBtn, "Reset to Defaults", 14, Color.white);

        // ── 마무리 ────────────────────────────────────────────
        Selection.activeGameObject = root;
        Undo.RegisterCreatedObjectUndo(root, "Create Options Panel");

        Debug.Log("[JuicyUI] OptionsPanel 생성 완료!\n" +
                  "• CloseButton onClick → UITransition.Hide()\n" +
                  "• ResetButton onClick → OptionsPanel.ResetToDefaults()\n" +
                  "• Content 안에 SliderOption / ToggleOption / DropdownOption 배치");
    }

    // ── 헬퍼 ──────────────────────────────────────────────────

    private static GameObject UI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void FullScreen(GameObject go)
    {
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.sizeDelta = Vector2.zero;
        r.anchoredPosition = Vector2.zero;
    }

    private static void Rect(GameObject go, Vector2 pos, Vector2 size)
    {
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    private static void LE(GameObject go, float width = -1, float height = -1, bool flexW = false)
    {
        var le = go.AddComponent<LayoutElement>();
        if (width  >= 0) le.preferredWidth  = width;
        if (height >= 0) le.preferredHeight = height;
        if (flexW) le.flexibleWidth = 1;
    }

    private static void Separator(GameObject parent)
    {
        var sep = UI("Separator", parent.transform);
        LE(sep, height: 1);
        sep.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
    }

    private static void TMPLabel(GameObject parent, string text, float size, Color color)
    {
        var label = UI("Text", parent.transform);
        FullScreen(label);
        var tmp = label.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Center;
    }
}
#endif
