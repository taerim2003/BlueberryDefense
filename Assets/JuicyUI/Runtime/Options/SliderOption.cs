using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class SliderOption : MonoBehaviour, IResettable
{
    [Header("References")]
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Text valueText;

    [Header("Settings")]
    [SerializeField] private string label = "Volume";
    [SerializeField] private float minValue = 0f;
    [SerializeField] private float maxValue = 1f;
    [SerializeField] private float defaultValue = 1f;
    [SerializeField] private bool wholeNumbers = false;

    [Header("Events")]
    [SerializeField] private UnityEvent<float> onValueChanged;

    private void Awake() => SetupLayout(slider.gameObject);

    private void Start()
    {
        if (labelText != null) labelText.text = label;

        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.wholeNumbers = wholeNumbers;
        slider.value = defaultValue;

        UpdateValueText(defaultValue);
        slider.onValueChanged.AddListener(OnSliderChanged);
    }

    public void ResetToDefault() => slider.value = defaultValue;

    private void OnSliderChanged(float value)
    {
        UpdateValueText(value);
        onValueChanged?.Invoke(value);
    }

    private void UpdateValueText(float value)
    {
        if (valueText == null) return;
        valueText.text = wholeNumbers ? $"{(int)value}" : $"{Mathf.RoundToInt(value * 100)}%";
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
