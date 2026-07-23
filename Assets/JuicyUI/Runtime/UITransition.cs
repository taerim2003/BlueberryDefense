using UnityEngine;
using DG.Tweening;

[RequireComponent(typeof(CanvasGroup))]
public class UITransition : MonoBehaviour
{
    public enum TransitionType { Slide, Pop }
    public enum SlideDirection { None, Up, Down, Left, Right }

    [SerializeField] private RectTransform visualRoot;

    [SerializeField] private TransitionType transitionType = TransitionType.Slide;
    [SerializeField] private float delay = 0f;
    [SerializeField] private float duration = 0.45f;
    [SerializeField] private Ease ease = Ease.OutBack;

    [Header("Pop")]
    [SerializeField] private float popStartScale = 0f;

    [Header("Slide")]
    [SerializeField] private SlideDirection slideFrom = SlideDirection.Down;
    [SerializeField] private float slideDistance = 50f;

    [Header("Wobble")]
    [SerializeField] private bool wobbleOnShow = false;
    [SerializeField] private float wobbleAngle = 8f;

    private CanvasGroup _canvasGroup;
    private RectTransform _rect;
    private RectTransform _animTarget;
    private Vector2 _targetPos;
    private Vector3 _targetScale;
    private Vector3 _targetRotation;
    private Sequence _hideSeq; // 진행 중인 닫힘 연출. 다시 열릴 때 죽이지 않으면 완료 콜백이 새로 연 패널을 꺼버린다.

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _rect = GetComponent<RectTransform>();
        _animTarget = visualRoot != null ? visualRoot : _rect;
        _targetPos = _animTarget.anchoredPosition;
        _targetScale = _animTarget.localScale;
        _targetRotation = _animTarget.localEulerAngles;
        _canvasGroup.alpha = 0f;
    }

    private void OnEnable()
    {
        PlayShow();
    }

    public void Show()
    {
        if (gameObject.activeSelf)
            PlayShow();
        else
            gameObject.SetActive(true); // OnEnable → PlayShow()
    }

    public void Hide()
    {
        _animTarget.DOKill();
        _canvasGroup.DOKill();
        _hideSeq?.Kill();

        var seq = DOTween.Sequence();

        if (transitionType == TransitionType.Pop)
        {
            seq.Append(_animTarget.DOScale(_targetScale * popStartScale, duration).SetEase(Ease.InBack));
            seq.Join(DOTween.To(() => _canvasGroup.alpha, x => _canvasGroup.alpha = x, 0f, duration * 0.5f).SetEase(Ease.InQuad));
        }
        else
        {
            seq.Append(DOTween.To(() => _animTarget.anchoredPosition, x => _animTarget.anchoredPosition = x, _targetPos + GetSlideOffset(), duration).SetEase(Ease.InBack));
            seq.Join(DOTween.To(() => _canvasGroup.alpha, x => _canvasGroup.alpha = x, 0f, duration * 0.6f).SetEase(Ease.InQuad));
        }

        seq.OnComplete(() => gameObject.SetActive(false));
        _hideSeq = seq;
    }

    private void PlayShow()
    {
        _animTarget.DOKill();
        _canvasGroup.DOKill();
        _hideSeq?.Kill(); // 닫히는 중에 다시 열렸다면 완료 콜백(SetActive(false))이 돌지 않게 취소
        _hideSeq = null;

        _canvasGroup.alpha = 0f;
        _animTarget.localEulerAngles = _targetRotation;

        var seq = DOTween.Sequence();
        if (delay > 0f) seq.AppendInterval(delay);

        if (transitionType == TransitionType.Pop)
        {
            _animTarget.anchoredPosition = _targetPos;
            _animTarget.localScale = _targetScale * popStartScale;
            seq.Append(_animTarget.DOScale(_targetScale, duration).SetEase(ease));
            seq.Join(DOTween.To(() => _canvasGroup.alpha, x => _canvasGroup.alpha = x, 1f, duration * 0.5f).SetEase(Ease.OutQuad));
        }
        else
        {
            _animTarget.anchoredPosition = _targetPos + GetSlideOffset();
            _animTarget.localScale = _targetScale;
            seq.Append(DOTween.To(() => _animTarget.anchoredPosition, x => _animTarget.anchoredPosition = x, _targetPos, duration).SetEase(ease));
            seq.Join(DOTween.To(() => _canvasGroup.alpha, x => _canvasGroup.alpha = x, 1f, duration * 0.6f).SetEase(Ease.OutQuad));
        }

        if (wobbleOnShow)
        {
            float peakTime = delay + duration * 0.35f;
            var peakRot = _targetRotation + new Vector3(0f, 0f, wobbleAngle);
            seq.Insert(delay, _animTarget.DOLocalRotate(peakRot, duration * 0.35f).SetEase(Ease.OutBack));
            seq.Insert(peakTime, _animTarget.DOLocalRotate(_targetRotation, duration * 0.4f).SetEase(Ease.OutBack));
        }
    }

    private Vector2 GetSlideOffset()
    {
        return slideFrom switch
        {
            SlideDirection.Up    => new Vector2(0,  slideDistance),
            SlideDirection.Down  => new Vector2(0, -slideDistance),
            SlideDirection.Left  => new Vector2(-slideDistance, 0),
            SlideDirection.Right => new Vector2( slideDistance, 0),
            _                    => Vector2.zero,
        };
    }
}
