using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

[RequireComponent(typeof(RectTransform))]
public class JuicyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    [SerializeField] private RectTransform visualRoot;

    [SerializeField] private float hoverScale = 1.1f;
    [SerializeField] private float squashX = 1.2f;
    [SerializeField] private float squashDuration = 0.1f;
    [SerializeField] private float hoverDuration = 0.15f;
    [SerializeField] private float pressScale = 0.93f;
    [SerializeField] private float pressDuration = 0.08f;

    [Header("Hover Shake")]
    [SerializeField] private bool shakeOnHover = false;
    [SerializeField] private float shakeStrength = 10f;

    [Header("Color")]
    [SerializeField] private bool colorOnHover = false;
    [SerializeField] private Graphic targetGraphic;
    [SerializeField] private float colorDuration = 0.15f;
    [SerializeField] private Color hoverColor = new Color(1f, 1f, 0.85f);
    [SerializeField] private Color pressColor = new Color(0.75f, 0.75f, 0.75f);

    [Header("Events")]
    [SerializeField] private UnityEvent onClick;

    private RectTransform _rect;
    private RectTransform _animTarget;
    private Vector3 _originalScale;
    private Vector3 _originalEulerAngles;
    private bool _isHovering;
    private Color _originalColor;

    private Tween _scaleTween;
    private Tween _colorTween;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _animTarget = visualRoot != null ? visualRoot : _rect;
        _originalScale = Vector3.one;
        _originalEulerAngles = _animTarget.localEulerAngles;
        if (targetGraphic != null) _originalColor = targetGraphic.color;

#if UNITY_EDITOR
        if (shakeOnHover && visualRoot == null)
            Debug.LogWarning($"[JuicyButton] {name}: shakeOnHover은 visualRoot가 필요합니다. Root RectTransform을 rotate하면 히트박스가 틀어집니다.", this);
#endif
    }

    private void OnDisable()
    {
        _scaleTween?.Kill();
        _colorTween?.Kill();
        _animTarget.localScale = _originalScale;
        if (CanRotate) _animTarget.localEulerAngles = _originalEulerAngles;
        if (colorOnHover && targetGraphic != null) targetGraphic.color = _originalColor;
        _isHovering = false;
    }

    // rotation은 visualRoot(child)에만 적용 — root _rect를 rotate하면 hitbox가 어긋남
    private bool CanRotate => visualRoot != null;

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovering = true;
        _scaleTween?.Kill();

        var seq = DOTween.Sequence();
        seq.Append(_animTarget.DOScaleX(_originalScale.x * squashX, squashDuration).SetEase(Ease.OutBack));
        if (shakeOnHover && CanRotate)
            seq.Join(_animTarget.DOLocalRotate(new Vector3(0f, 0f, shakeStrength), squashDuration).SetEase(Ease.OutBack));
        seq.Append(_animTarget.DOScaleX(_originalScale.x * hoverScale, hoverDuration).SetEase(Ease.OutBack));
        seq.Join(_animTarget.DOScaleY(_originalScale.y * hoverScale, hoverDuration).SetEase(Ease.OutBack));
        if (shakeOnHover && CanRotate)
            seq.Join(_animTarget.DOLocalRotate(_originalEulerAngles, hoverDuration * 0.67f).SetEase(Ease.OutBack));
        _scaleTween = seq;

        if (colorOnHover && targetGraphic != null)
        {
            _colorTween?.Kill();
            _colorTween = DOTween.To(() => targetGraphic.color, x => targetGraphic.color = x, hoverColor, colorDuration);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovering = false;
        _scaleTween?.Kill();

        var seq = DOTween.Sequence();
        seq.Append(_animTarget.DOScale(_originalScale, hoverDuration).SetEase(Ease.OutCubic));
        if (CanRotate)
            seq.Join(_animTarget.DOLocalRotate(_originalEulerAngles, hoverDuration).SetEase(Ease.OutCubic));
        _scaleTween = seq;

        if (colorOnHover && targetGraphic != null)
        {
            _colorTween?.Kill();
            _colorTween = DOTween.To(() => targetGraphic.color, x => targetGraphic.color = x, _originalColor, colorDuration);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _scaleTween?.Kill();
        var pressSeq = DOTween.Sequence();
        pressSeq.Append(_animTarget.DOScale(_originalScale * pressScale, pressDuration).SetEase(Ease.OutCubic));
        if (CanRotate)
            pressSeq.Join(_animTarget.DOLocalRotate(_originalEulerAngles, pressDuration).SetEase(Ease.OutCubic));
        _scaleTween = pressSeq;

        if (colorOnHover && targetGraphic != null)
        {
            _colorTween?.Kill();
            _colorTween = DOTween.To(() => targetGraphic.color, x => targetGraphic.color = x, pressColor, pressDuration);
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _scaleTween?.Kill();
        float target = _isHovering ? hoverScale : 1f;
        var upSeq = DOTween.Sequence();
        upSeq.Append(_animTarget.DOScale(_originalScale * target, hoverDuration).SetEase(Ease.OutBack));
        if (CanRotate)
            upSeq.Join(_animTarget.DOLocalRotate(_originalEulerAngles, hoverDuration).SetEase(Ease.OutBack));
        _scaleTween = upSeq;

        if (colorOnHover && targetGraphic != null)
        {
            _colorTween?.Kill();
            _colorTween = DOTween.To(() => targetGraphic.color, x => targetGraphic.color = x, _isHovering ? hoverColor : _originalColor, colorDuration);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        onClick?.Invoke();
    }
}
