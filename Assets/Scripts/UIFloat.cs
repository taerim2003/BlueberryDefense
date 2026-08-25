using UnityEngine;
using UnityEngine.EventSystems;

// 가만히 있을 때 위아래로 아주 조금 떠다니는 UI 상자. 호버 중에는 제자리로 돌아와 멈춘다
// (JuicyButton의 확대 연출과 겹치지 않게 — 저쪽은 scale, 이쪽은 위치라 서로 건드리지 않는다).
[RequireComponent(typeof(RectTransform))]
public class UIFloat : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // 2026-08-25에 폭·속도를 각각 20% 낮췄다(사용자 요청). 폭 4→3.2 / 주기 2.6→3.25(=속도 0.8배).
    // ⚠️ 이건 **새로 붙이는 인스턴스의 기본값일 뿐**이다. 씬에 이미 있는 것들은 각자 직렬화된 값을
    //    들고 있으므로 여기만 고치면 안 바뀐다 — 씬 값도 같이 옮겨야 한다.
    [SerializeField] private float amplitude = 3.2f;  // 위아래로 흔들리는 폭(px)
    [SerializeField] private float period = 3.25f;    // 한 번 오르내리는 데 걸리는 시간(초)
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

    // 부모가 LayoutGroup이면 Awake 시점엔 아직 배치 전이라 제자리를 모른다(_homeReady=false).
    // 그 경우만 코루틴으로 미뤄 잡는다. 레이아웃 그룹이 없으면 Awake의 값이 곧 제자리다.
    private void OnEnable()
    {
        if (!_homeReady) StartCoroutine(CaptureHomeAfterLayout());
    }

    private void CaptureHome()
    {
        if (_rect == null) _rect = (RectTransform)transform;
        bool underLayout = _rect.parent != null
                        && _rect.parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null;
        if (underLayout)
        {
            // 🔴 여기서 ForceRebuildLayoutImmediate로 당겨 읽으면 안 된다.
            //    Awake·OnEnable은 캔버스가 첫 레이아웃을 돌기 전이라 그 자리에서 강제로 돌려도
            //    anchoredPosition이 0으로 나온다. 그 0이 제자리로 굳어 **타이틀 메뉴 버튼 5개 중
            //    4개가 y=0 한자리에 겹쳐** 로고 위로 올라갔다(2026-08-25에 실제로 그랬다).
            //    레이아웃이 진짜로 한 번 돈 뒤에 잡는다 — 그때까지 _homeReady=false라 흔들지 않는다.
            _homeReady = false;
            return;
        }
        _baseY = _rect.anchoredPosition.y;
        _homeReady = true;
    }

    // 캔버스 레이아웃은 LateUpdate 뒤(willRenderCanvases)에 돈다 — 코루틴이 재개되는 시점보다 늦다.
    // 그래서 한 프레임이 아니라 **두 프레임**을 보낸 뒤에 읽는다(그 사이엔 안 흔들리므로 티가 안 난다).
    private System.Collections.IEnumerator CaptureHomeAfterLayout()
    {
        yield return null;
        yield return null;
        if (_homeReady) yield break; // 그 사이 SetHomeY로 정답을 받았으면 그쪽이 이긴다
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
        _homeReady = true;
    }

    private void OnDisable()
    {
        _hovering = false;
        // 제자리를 아직 모르면 되돌리지 않는다 — _baseY(=0)로 스냅하면 그게 곧 사고다.
        if (_homeReady && _rect != null) SetY(_baseY);
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
