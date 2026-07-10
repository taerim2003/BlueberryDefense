using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class DropdownOption : MonoBehaviour, IResettable
{
    [Header("References")]
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private TMP_Dropdown dropdown;

    [Header("Settings")]
    [SerializeField] private string label = "Option";
    [SerializeField] private List<string> options = new();
    [SerializeField] private int defaultIndex = 0;

    [Header("Events")]
    [SerializeField] private UnityEvent<int> onValueChanged;

    private void Awake() => SetupLayout(dropdown.gameObject);

    public void ResetToDefault() => dropdown.value = defaultIndex;

    private void Start()
    {
        if (labelText != null) labelText.text = label;

        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        dropdown.value = defaultIndex;
        dropdown.onValueChanged.AddListener(v => onValueChanged?.Invoke(v));
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
