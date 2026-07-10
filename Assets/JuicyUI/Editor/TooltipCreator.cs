#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class TooltipCreator
{
    [MenuItem("JuicyUI/Create Tooltip")]
    public static void CreateTooltip()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[JuicyUI] Canvas를 찾을 수 없습니다.");
            return;
        }

        // ── Root ──────────────────────────────────────────────
        var root = UI("Tooltip", canvas.transform);
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.sizeDelta = Vector2.zero;
        root.AddComponent<CanvasGroup>();
        var tooltip = root.AddComponent<Tooltip>();

        // ── Panel ─────────────────────────────────────────────
        var panel = UI("Panel", root.transform);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = Vector2.zero;

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);

        var layout = panel.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 8, 8);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        var csf = panel.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // ── Text ──────────────────────────────────────────────
        var textObj = UI("Text", panel.transform);
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text      = "Tooltip";
        tmp.fontSize  = 14;
        tmp.color     = new Color(0.9f, 0.9f, 0.9f);
        tmp.alignment = TextAlignmentOptions.MidlineLeft;

        var textLE = textObj.AddComponent<LayoutElement>();
        textLE.flexibleWidth = 1;

        // ── Tooltip 레퍼런스 연결 ─────────────────────────────
        var so = new SerializedObject(tooltip);
        so.FindProperty("canvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
        so.FindProperty("panel").objectReferenceValue       = panel.GetComponent<RectTransform>();
        so.FindProperty("text").objectReferenceValue        = tmp;
        so.ApplyModifiedProperties();

        // ── 마무리 ────────────────────────────────────────────
        root.transform.SetAsLastSibling();
        Selection.activeGameObject = root;
        Undo.RegisterCreatedObjectUndo(root, "Create Tooltip");

        Debug.Log("[JuicyUI] Tooltip 생성 완료! TooltipTrigger를 원하는 오브젝트에 추가하세요.");
    }

    private static GameObject UI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }
}
#endif
