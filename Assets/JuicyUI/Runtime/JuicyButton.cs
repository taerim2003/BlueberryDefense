using System.Collections.Generic;
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
    [SerializeField] private float hoverReturnDuration = 0.15f; // 커진 뒤 원래 크기로 돌아오는 시간
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

    // 평소에는 살짝 작고 어둡게 깔아두고, 커서가 올라가거나 "선택됨"일 때만 원본으로 돌아온다.
    // 고른 것과 안 고른 것을 화면에서 확실히 갈라놓기 위한 것 — 전 버튼 공통.
    [Header("Idle Dim (비호버·비선택일 때 죽여 둔다)")]
    [SerializeField] private bool idleDim = true;
    [SerializeField] private float idleScale = 0.92f;
    [SerializeField] private Color idleTint = new Color(0.55f, 0.55f, 0.62f, 1f); // 곱해지는 값. 알파는 원본 유지
    [SerializeField] private float dimDuration = 0.15f;

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

    private bool _selected;
    private Graphic[] _dimGraphics;
    private Color[] _dimColors;
    private float _dim;          // 0 = 원본, 1 = 죽인 상태
    private Tween _dimTween;

    private bool Highlighted => _isHovering || _selected;

    // 연출이 끝난 뒤 눌러앉을 크기. 호버·프레스 중이 아닐 때의 "제자리"다.
    private Vector3 RestScale => (!idleDim || Highlighted) ? _originalScale : _originalScale * idleScale;

    // 카드처럼 "지금 고른 것"이 있는 화면이 알려준다(맵·캐릭터 선택).
    // 선택된 버튼은 커서가 없어도 원본 크기·색으로 남는다.
    public void SetSelected(bool on)
    {
        if (_selected == on) return;
        _selected = on;
        if (!idleDim) return;

        TweenDim(Highlighted ? 0f : 1f);
        if (_isHovering) return; // 호버 연출이 이미 크기를 몰고 있다

        _scaleTween?.Kill();
        _scaleTween = _animTarget.DOScale(RestScale, hoverDuration).SetEase(Ease.OutCubic);
    }

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

    // 원본 색은 Awake가 아니라 Start에서 잡는다 — 카드를 런타임에 복제하는 화면들이
    // Instantiate 직후(=Awake 뒤, Start 앞)에 썸네일·이름 색을 덮어쓰기 때문이다.
    // Awake에서 잡으면 그 덮어쓰기 전의 색을 "원본"으로 기억해, 호버가 풀릴 때 되돌려 버린다.
    private void Start()
    {
        if (!idleDim) return;

        var list = new List<Graphic>();
        foreach (var g in GetComponentsInChildren<Graphic>(true))
        {
            if (g == null) continue;
            if (colorOnHover && g == targetGraphic) continue; // 그쪽은 hoverColor/pressColor가 이미 몰고 있다
            list.Add(g);
        }
        _dimGraphics = list.ToArray();
        _dimColors = new Color[_dimGraphics.Length];
        for (int i = 0; i < _dimGraphics.Length; i++) _dimColors[i] = _dimGraphics[i].color;

        _dim = Highlighted ? 0f : 1f;
        ApplyDim();
        if (!_isHovering) _animTarget.localScale = RestScale;
    }

    private void ApplyDim()
    {
        if (_dimGraphics == null) return;
        for (int i = 0; i < _dimGraphics.Length; i++)
        {
            var g = _dimGraphics[i];
            if (g == null) continue;
            Color o = _dimColors[i];
            // 알파는 건드리지 않는다 — 반투명하게 만드는 게 아니라 어둡게 덮는 느낌이라야 한다.
            var dark = new Color(o.r * idleTint.r, o.g * idleTint.g, o.b * idleTint.b, o.a);
            g.color = Color.Lerp(o, dark, _dim);
        }
    }

    private void TweenDim(float target)
    {
        if (_dimGraphics == null) return; // Start 전 — Start가 최종 상태로 맞춘다
        _dimTween?.Kill();
        _dimTween = DOTween.To(() => _dim, x => { _dim = x; ApplyDim(); }, target, dimDuration);
    }

    private void OnDisable()
    {
        _scaleTween?.Kill();
        _colorTween?.Kill();
        _dimTween?.Kill();
        _isHovering = false;
        _exitSuppressed = false;
        _animTarget.localScale = RestScale;
        if (CanRotate) _animTarget.localEulerAngles = _originalEulerAngles;
        if (colorOnHover && targetGraphic != null) targetGraphic.color = _originalColor;
        _dim = Highlighted ? 0f : 1f;
        ApplyDim();
    }

    // rotation은 visualRoot(child)에만 적용 — root _rect를 rotate하면 hitbox가 어긋남
    private bool CanRotate => visualRoot != null;

    public void OnPointerEnter(PointerEventData eventData)
    {
        RememberPointer(eventData);
        if (_isHovering) return; // 회전이 밀어낸 뒤 다시 들어온 것 — 연출을 재시작하지 않는다

        _isHovering = true;
        TweenDim(0f); // 커서가 올라간 동안은 원본 색·크기 — 하이라이트된 것처럼 보여야 한다
        _scaleTween?.Kill();

        var seq = DOTween.Sequence();
        seq.Append(_animTarget.DOScaleX(_originalScale.x * squashX, squashDuration).SetEase(Ease.OutBack));
        if (shakeOnHover && CanRotate)
            seq.Join(_animTarget.DOLocalRotate(new Vector3(0f, 0f, shakeStrength), squashDuration).SetEase(Ease.OutBack));
        seq.Append(_animTarget.DOScaleX(_originalScale.x * hoverScale, hoverDuration).SetEase(Ease.OutBack));
        seq.Join(_animTarget.DOScaleY(_originalScale.y * hoverScale, hoverDuration).SetEase(Ease.OutBack));
        if (shakeOnHover && CanRotate)
            seq.Join(_animTarget.DOLocalRotate(_originalEulerAngles, hoverDuration * 0.67f).SetEase(Ease.OutBack));
        // 커진 채로 굳지 않는다 — 한 번 커졌다가 원래 크기로 부드럽게 돌아온다.
        seq.Append(_animTarget.DOScale(RestScale, hoverReturnDuration).SetEase(Ease.OutSine));
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
        TweenDim(Highlighted ? 0f : 1f); // 선택된 버튼은 커서가 떠나도 밝게 남는다

        var seq = DOTween.Sequence();
        seq.Append(_animTarget.DOScale(RestScale, hoverDuration).SetEase(Ease.OutCubic));
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
        // 커서가 아직 위에 있어도 원래 크기로 돌아온다 — hoverScale로 되돌리면 손을 뗀 뒤 커진 채 굳는다.
        var upSeq = DOTween.Sequence();
        upSeq.Append(_animTarget.DOScale(RestScale, hoverReturnDuration).SetEase(Ease.OutSine));
        if (CanRotate)
            upSeq.Join(_animTarget.DOLocalRotate(_originalEulerAngles, hoverReturnDuration).SetEase(Ease.OutSine));
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
        _animTarget.localScale = RestScale; // 확대·축소 연출이 끝났을 때 실제로 차지하는 크기
        _animTarget.localRotation = Quaternion.Euler(_originalEulerAngles);

        bool inside = RectTransformUtility.RectangleContainsScreenPoint(_rect, screenPos, cam);

        _animTarget.localScale = scale;
        _animTarget.localRotation = rot;
        return inside;
    }
}
