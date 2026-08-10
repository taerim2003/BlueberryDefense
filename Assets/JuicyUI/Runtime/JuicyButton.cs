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

    // visualRoot에 버튼 자신을 넣어 쓰면(JuicyTuning) 회전이 히트박스를 통째로 돌린다.
    // 그러면 커서가 가만히 있어도 "회전 → 커서가 rect 밖 → Exit → 회전 복귀 → 커서가 안 → Enter"가
    // 무한 반복돼 버튼이 덜덜 떤다(가로로 긴 버튼일수록 심하다 — 900x170 카드는 모서리가 5px 가까이 벗어난다).
    // 회전이 도는 동안 들어온 Exit는 버튼이 스스로 밀어낸 것으로 보고 미뤄뒀다가,
    // 회전이 원각도로 돌아온 뒤(=히트박스가 정상인 시점) 실제 커서 위치로 한 번만 판정한다.
    private Vector2 _lastPointerPos;
    private Camera _lastPointerCam;
    private bool _exitSuppressed;

    private bool RotationInProgress =>
        shakeOnHover && CanRotate && _scaleTween != null && _scaleTween.IsActive() && _scaleTween.IsPlaying();

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
        _exitSuppressed = false;
    }

    // rotation은 visualRoot(child)에만 적용 — root _rect를 rotate하면 hitbox가 어긋남
    private bool CanRotate => visualRoot != null;

    public void OnPointerEnter(PointerEventData eventData)
    {
        RememberPointer(eventData);
        if (_isHovering) return; // 회전이 밀어낸 뒤 다시 들어온 것 — 연출을 재시작하지 않는다

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
        seq.OnComplete(ResolveSuppressedExit);
        _scaleTween = seq;

        if (colorOnHover && targetGraphic != null)
        {
            _colorTween?.Kill();
            _colorTween = DOTween.To(() => targetGraphic.color, x => targetGraphic.color = x, hoverColor, colorDuration);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        RememberPointer(eventData);
        if (RotationInProgress) { _exitSuppressed = true; return; }
        EndHover();
    }

    private void EndHover()
    {
        _isHovering = false;
        _exitSuppressed = false;
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
        pressSeq.OnComplete(ResolveSuppressedExit);
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
        upSeq.OnComplete(ResolveSuppressedExit);
        _scaleTween = upSeq;

        if (colorOnHover && targetGraphic != null)
        {
            _colorTween?.Kill();
            _colorTween = DOTween.To(() => targetGraphic.color, x => targetGraphic.color = x, _isHovering ? hoverColor : _originalColor, colorDuration);
        }
    }

    // 클릭음처럼 게임 쪽이 버튼 전체에 붙이고 싶은 반응을 위한 훅.
    // ⚠️ 이 어셈블리(JuicyUI.Runtime)는 asmdef가 있어 기본 어셈블리(SfxPlayer 등)를 참조할 수 없다 —
    //    그래서 여기서 부르지 않고 이렇게 알리기만 하고, 듣는 쪽이 SfxPlayer에 있다.
    public static event System.Action Clicked;

    public void OnPointerClick(PointerEventData eventData)
    {
        Clicked?.Invoke();
        onClick?.Invoke();
    }

    private void RememberPointer(PointerEventData eventData)
    {
        if (eventData == null) return;
        _lastPointerPos = eventData.position;
        _lastPointerCam = eventData.enterEventCamera != null ? eventData.enterEventCamera : eventData.pressEventCamera;
    }

    // 회전이 원각도로 돌아온 시점 = 히트박스가 다시 정상이다. 미뤄둔 Exit를 여기서 한 번만 판정한다.
    private void ResolveSuppressedExit()
    {
        if (!_exitSuppressed) return;
        _exitSuppressed = false;
        if (!PointerInsideBaseRect(_lastPointerPos, _lastPointerCam))
            EndHover();
    }

    // 호버 연출(확대·회전)을 잠시 걷어내고 **원래 크기·각도**의 사각형으로 판정한다.
    // 확대된 rect(1.15배)로 재판정하면 커서가 이미 버튼을 벗어났는데도 "아직 안"으로 읽혀
    // 호버가 커진 채로 굳는다 — 버튼 사이를 빠르게 지나갈 때 실제로 났다.
    private bool PointerInsideBaseRect(Vector2 screenPos, Camera cam)
    {
        Vector3 scale = _animTarget.localScale;
        Quaternion rot = _animTarget.localRotation;
        _animTarget.localScale = _originalScale;
        _animTarget.localRotation = Quaternion.Euler(_originalEulerAngles);

        bool inside = RectTransformUtility.RectangleContainsScreenPoint(_rect, screenPos, cam);

        _animTarget.localScale = scale;
        _animTarget.localRotation = rot;
        return inside;
    }
}
