using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

[RequireComponent(typeof(RectTransform))]
public class JuicyHealthBar : MonoBehaviour
{
    [SerializeField] private RectTransform barVisuals;
    [SerializeField] private Image fillImage;
    [SerializeField] private Image ghostImage;

    [Header("Ghost")]
    [SerializeField] private float ghostDelay = 0.4f;
    [SerializeField] private float ghostDuration = 0.5f;
    [SerializeField] private Ease ghostEase = Ease.OutCubic;

    [Header("Heal")]
    [SerializeField] private float healDuration = 0.35f;
    [SerializeField] private Ease healEase = Ease.OutBack;

    [Header("Shake")]
    [SerializeField] private float shakeDuration = 0.3f;
    [SerializeField] private float shakeStrength = 8f;
    [SerializeField] private int shakeVibrato = 15;

    [Header("Damage Chunk")]
    [SerializeField] private Image chunkImage;
    [SerializeField] private float chunkDuration = 0.45f;
    [SerializeField] private Ease chunkEase = Ease.OutCubic;

    private RectTransform _rect;
    private RectTransform _squashTarget;
    private Vector2 _originalPos;
    private float _currentValue;
    private Tween _fillTween;
    private Tween _ghostDelay;
    private Tween _ghostFollow;
    private Tween _shakeTween;
    private Tween _squashTween;
    private Tween _chunkTween;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _squashTarget = barVisuals != null ? barVisuals : _rect;
        _originalPos = _squashTarget.anchoredPosition;
        _currentValue = fillImage != null ? fillImage.rectTransform.anchorMax.x : 1f;
        if (ghostImage != null) SetAnchorX(ghostImage, _currentValue);
        if (chunkImage != null) SetAlpha(chunkImage, 0f);
    }

    private void OnDisable()
    {
        _fillTween?.Kill();
        _ghostDelay?.Kill();
        _ghostFollow?.Kill();
        _shakeTween?.Kill();
        _squashTween?.Kill();
        _chunkTween?.Kill();
    }

    public void SetValue(float normalizedValue, bool animate = true)
    {
        normalizedValue = Mathf.Clamp01(normalizedValue);
        float previousValue = _currentValue;
        bool isDamage = normalizedValue < _currentValue;
        _currentValue = normalizedValue;

        if (!animate)
        {
            _fillTween?.Kill();
            _ghostDelay?.Kill();
            _ghostFollow?.Kill();
            _chunkTween?.Kill();
            if (fillImage != null) SetAnchorX(fillImage, normalizedValue);
            if (ghostImage != null) SetAnchorX(ghostImage, normalizedValue);
            if (chunkImage != null) SetAlpha(chunkImage, 0f);
            return;
        }

        if (isDamage)
        {
            _fillTween?.Kill();
            if (fillImage != null) SetAnchorX(fillImage, normalizedValue);

            _ghostDelay?.Kill();
            _ghostFollow?.Kill();
            float ghostTarget = normalizedValue;
            _ghostDelay = DOVirtual.DelayedCall(ghostDelay, () =>
            {
                if (ghostImage == null) return;
                var rt = ghostImage.rectTransform;
                _ghostFollow = DOTween.To(
                    () => rt.anchorMax.x,
                    x => SetAnchorX(ghostImage, x),
                    ghostTarget,
                    ghostDuration
                ).SetEase(ghostEase);
            });

            _shakeTween?.Kill();
            _squashTarget.anchoredPosition = _originalPos;
            _shakeTween = DOTween.Shake(
                () => (Vector3)_squashTarget.anchoredPosition,
                x => _squashTarget.anchoredPosition = new Vector2(x.x, x.y),
                shakeDuration, shakeStrength, shakeVibrato);

            _squashTween?.Kill();
            _squashTarget.localScale = Vector3.one;
            var squashSeq = DOTween.Sequence();
            squashSeq.Append(_squashTarget.DOScaleY(0.88f, 0.08f).SetEase(Ease.OutQuad));
            squashSeq.Join(_squashTarget.DOScaleX(1.06f, 0.08f).SetEase(Ease.OutQuad));
            squashSeq.Append(_squashTarget.DOScale(Vector3.one, 0.22f).SetEase(Ease.OutBack));
            _squashTween = squashSeq;

            PlayChunk(normalizedValue, previousValue);
        }
        else
        {
            _ghostDelay?.Kill();
            _ghostFollow?.Kill();
            if (ghostImage != null) SetAnchorX(ghostImage, normalizedValue);

            _fillTween?.Kill();
            if (fillImage != null)
            {
                var rt = fillImage.rectTransform;
                _fillTween = DOTween.To(
                    () => rt.anchorMax.x,
                    x => SetAnchorX(fillImage, x),
                    normalizedValue,
                    healDuration
                ).SetEase(healEase);
            }
        }
    }

    public void SetHealth(float current, float max)
    {
        SetValue(max > 0f ? current / max : 0f);
    }

    // 피해량만큼의 구간을 anchor로 잡아 위로 날려 보냄
    private void PlayChunk(float newValue, float prevValue)
    {
        if (chunkImage == null) return;

        _chunkTween?.Kill();

        var rt = chunkImage.rectTransform;
        rt.anchorMin = new Vector2(newValue, rt.anchorMin.y);
        rt.anchorMax = new Vector2(prevValue, rt.anchorMax.y);
        rt.sizeDelta = new Vector2(0f, rt.sizeDelta.y);
        rt.pivot = new Vector2(0f, rt.pivot.y);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        chunkImage.color = Color.white;

        var seq = DOTween.Sequence();
        seq.Append(rt.DOScaleY(1.6f, chunkDuration).SetEase(chunkEase));
        seq.Join(rt.DOScaleX(0.6f, chunkDuration).SetEase(chunkEase));
        seq.Join(DOTween.To(() => chunkImage.color.a, x => { var c = chunkImage.color; c.a = x; chunkImage.color = c; }, 0f, chunkDuration).SetEase(Ease.InQuad));
        _chunkTween = seq;
    }

    private static void SetAnchorX(Image image, float x)
    {
        var rt = image.rectTransform;
        rt.anchorMax = new Vector2(x, rt.anchorMax.y);
    }

    private static void SetAlpha(Image image, float a)
    {
        var c = image.color;
        image.color = new Color(c.r, c.g, c.b, a);
    }
}
