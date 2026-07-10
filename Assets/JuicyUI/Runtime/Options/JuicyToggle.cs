using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

public class JuicyToggle : MonoBehaviour, IPointerClickHandler, IResettable
{
    [Header("References")]
    [SerializeField] private Image track;
    [SerializeField] private RectTransform handle;
    [SerializeField] private Image handleImage;

    [Header("Settings")]
    [SerializeField] private bool isOn = false;

    [Header("Colors")]
    [SerializeField] private Color onColor = new Color(0.2f, 0.78f, 0.42f);
    [SerializeField] private Color offColor = new Color(0.55f, 0.55f, 0.55f);
    [SerializeField] private Color handleOnColor = Color.white;
    [SerializeField] private Color handleOffColor = Color.white;

    [Header("Animation")]
    [SerializeField] private float duration = 0.25f;

    [Header("Events")]
    [SerializeField] private UnityEvent<bool> onValueChanged;

    private float _onX;
    private float _offX;
    private bool _defaultValue;

    private void Start()
    {
        _defaultValue = isOn;
        _onX = 0f;
        _offX = -handle.rect.width;

        handle.anchoredPosition = new Vector2(isOn ? _onX : _offX, 0f);
        if (track != null) track.color = isOn ? onColor : offColor;
        if (handleImage != null) handleImage.color = isOn ? handleOnColor : handleOffColor;
    }

    public void ResetToDefault() => SetValue(_defaultValue);

    public void OnPointerClick(PointerEventData eventData)
    {
        SetValue(!isOn);
    }

    public void SetValue(bool value)
    {
        isOn = value;
        float targetX = isOn ? _onX : _offX;

        handle.DOKill();
        DOTween.To(() => handle.anchoredPosition, x => handle.anchoredPosition = x, new Vector2(targetX, handle.anchoredPosition.y), duration).SetEase(Ease.InOutCubic);

        if (track != null)
            DOTween.To(() => track.color, x => track.color = x, isOn ? onColor : offColor, duration * 0.6f);
        if (handleImage != null)
            DOTween.To(() => handleImage.color, x => handleImage.color = x, isOn ? handleOnColor : handleOffColor, duration * 0.6f);

        onValueChanged?.Invoke(isOn);
    }
}
