using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ToggleOption : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private JuicyToggle toggle;

    [Header("Settings")]
    [SerializeField] private string label = "Fullscreen";

    private void Awake() => SetupLayout(toggle.gameObject);

    private void Start()
    {
        if (labelText != null) labelText.text = label;
    }

    private void SetupLayout(GameObject control)
    {
        var hg = GetComponent<HorizontalLayoutGroup>() ?? gameObject.AddComponent<HorizontalLayoutGroup>();
        hg.childAlignment        = TextAnchor.MiddleLeft;
        hg.childControlWidth     = true;
        hg.childControlHeight    = false;
        hg.childForceExpandWidth = false;
        hg.spacing               = 16;

        if (labelText != null)
            LE(labelText.gameObject).flexibleWidth = 1;

        LE(control).preferredWidth = control.GetComponent<RectTransform>().sizeDelta.x;
    }

    private static LayoutElement LE(GameObject go)
        => go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
}
