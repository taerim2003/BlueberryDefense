using UnityEngine;
using UnityEngine.EventSystems;

// 가만히 있을 때 위아래로 아주 조금 떠다니는 UI 상자. 호버 중에는 제자리로 돌아와 멈춘다
// (JuicyButton의 확대 연출과 겹치지 않게 — 저쪽은 scale, 이쪽은 위치라 서로 건드리지 않는다).
[RequireComponent(typeof(RectTransform))]
public class UIFloat : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private float amplitude = 4f;  // 위아래로 흔들리는 폭(px)
    [SerializeField] private float period = 2.6f;   // 한 번 오르내리는 데 걸리는 시간(초)
    [SerializeField] private float follow = 10f;    // 목표 위치를 따라가는 속도(클수록 즉각적)

    private RectTransform _rect;
    private float _baseY;
    private float _phase;
    private bool _hovering;
    private bool _homeReady;   // 제자리를 잡았나 (레이아웃 그룹 아래면 Awake 시점엔 아직 아니다)

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _phase = Random.value * Mathf.PI * 2f; // 전부 같은 박자로 흔들리면 기계적으로 보인다
        CaptureHome();
    }

    // 🔴 부모가 LayoutGroup이면 **Awake 시점엔 아직 배치 전**이라 anchoredPosition이 엉뚱하다
    //    (타이틀 메뉴 버튼 5개가 VerticalLayoutGroup 아래다). 레이아웃이 한 번 돈 뒤에 제자리를 잡는다.
    //    레이아웃 그룹이 없으면 지금 값이 곧 제자리이므로 그대로 쓴다.
    private void OnEnable()
    {
        if (!_homeReady) CaptureHome();
    }

    private void CaptureHome()
    {
        if (_rect == null) _rect = (RectTransform)transform;
        bool underLayout = _rect.parent != null
                        && _rect.parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null;
        if (underLayout)
        {
            // 레이아웃이 정한 뒤에 읽는다. 그 전까지는 흔들지 않는다(_homeReady=false).
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_rect.parent as RectTransform);
        }
        _baseY = _rect.anchoredPosition.y;
        _homeReady = true;
    }

    // 제자리를 바깥에서 알려준다. Awake가 읽은 값은 믿을 수 없다 —
    // PanelSplitTransition이 같은 활성화 타이밍에 이 오브젝트를 화면 밖으로 밀어두기 때문에,
    // 컴포넌트 Awake 순서에 따라 "밀려난 위치"가 제자리로 굳어 버린다(실제로 CharacterHeader가
    // _baseY=1160으로 잡혀 화면 위로 빠져나가 있었다). 전환을 소유한 쪽이 정답을 안다.
    public void SetHomeY(float y)
    {
        if (_rect == null) _rect = (RectTransform)transform;
        _baseY = y;
    }

    private void OnDisable()
    {
        _hovering = false;
        if (_rect != null) SetY(_baseY);
    }

    private void Update()
    {
        if (!_homeReady) return; // 제자리를 모르는 채 흔들면 그 자리가 제자리로 굳는다

        // unscaledTime — 이 화면은 timeScale이 0인 상태에서도 열려 있을 수 있다.
        float target = _hovering
            ? _baseY
            : _baseY + Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / period) + _phase) * amplitude;

        // 곧바로 스냅하면 호버가 시작·해제될 때 툭 튄다.
        SetY(Mathf.Lerp(_rect.anchoredPosition.y, target, Time.unscaledDeltaTime * follow));
    }

    private void SetY(float y)
    {
        Vector2 p = _rect.anchoredPosition;
        p.y = y;
        _rect.anchoredPosition = p;
    }

    public void OnPointerEnter(PointerEventData eventData) => _hovering = true;
    public void OnPointerExit(PointerEventData eventData) => _hovering = false;
}
