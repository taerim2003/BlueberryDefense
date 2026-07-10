using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;

// 슬라이더 Handle 오브젝트에 추가
public class JuicySliderHandle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform visual;
    [SerializeField] private float hoverScale = 1.35f;
    [SerializeField] private float pressScale = 0.75f;
    [SerializeField] private float duration = 0.12f;

    private RectTransform _target;
    private Tween _tween;

    private void Awake()
    {
        _target = visual != null ? visual : GetComponent<RectTransform>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _tween?.Kill();
        _tween = _target.DOScale(Vector3.one * hoverScale, duration).SetEase(Ease.OutBack);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _tween?.Kill();
        _tween = _target.DOScale(Vector3.one, duration).SetEase(Ease.OutCubic);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _tween?.Kill();
        _tween = _target.DOScale(Vector3.one * pressScale, duration * 0.7f).SetEase(Ease.OutCubic);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _tween?.Kill();
        _tween = _target.DOScale(Vector3.one * hoverScale, duration).SetEase(Ease.OutBack);
    }
}
