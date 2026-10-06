using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

// 트리 팬(드래그) + 줌(휠) — 뷰포트에 부착, content를 이동/확대축소.
// 패드: 오른쪽 스틱 = 이동, LT/RT = 축소/확대(PadTick). 포커스한 노드가 화면 밖이면 그쪽으로 옮긴다(Reveal).
public class TreePanDrag : MonoBehaviour, IDragHandler, IScrollHandler
{
    [SerializeField] private RectTransform content;
    [SerializeField] private float minZoom = 0.45f;
    [SerializeField] private float maxZoom = 1.6f;
    [SerializeField] private float zoomSpeed = 0.1f;

    private const float PadPanSpeed = 1100f;   // 스틱을 끝까지 기울였을 때 초당 px
    private const float PadZoomSpeed = 1.2f;   // 트리거를 끝까지 눌렀을 때 초당 배율 변화(곱)
    private const float RevealMargin = 140f;   // 노드가 뷰포트 가장자리에서 이만큼 안쪽에 오도록

    public void OnDrag(PointerEventData e)
    {
        if (content == null) return;
        content.DOKill();
        content.anchoredPosition += e.delta;
    }

    public void OnScroll(PointerEventData e)
    {
        if (content == null) return;
        float z = content.localScale.x * (1f + e.scrollDelta.y * zoomSpeed);
        z = Mathf.Clamp(z, minZoom, maxZoom);
        content.localScale = new Vector3(z, z, 1f);
    }

    // SkillTreeUI가 자기 포커스 그룹이 입력을 받는 동안에만 매 프레임 부른다. 타이틀은 timeScale이 0일 수 있어 실시간으로 센다.
    public void PadTick()
    {
        if (content == null) return;
        float dt = Time.unscaledDeltaTime;

        Vector2 pan = GameInput.PanStick();
        if (pan != Vector2.zero)
        {
            content.DOKill(); // Reveal 이동 중이면 손이 이긴다
            content.anchoredPosition -= pan * PadPanSpeed * dt; // 오른쪽으로 밀면 오른쪽을 본다 = 내용이 왼쪽으로
        }

        float zoom = GameInput.ZoomAxis();
        if (Mathf.Abs(zoom) > 0.05f)
        {
            float z = Mathf.Clamp(content.localScale.x * Mathf.Pow(1f + PadZoomSpeed, zoom * dt), minZoom, maxZoom);
            content.localScale = new Vector3(z, z, 1f);
        }
    }

    // 노드가 뷰포트 밖(가장자리 RevealMargin 안쪽 기준)이면 그 노드가 들어오도록 content를 옮긴다.
    public void Reveal(RectTransform target)
    {
        if (content == null || target == null) return;
        var view = (RectTransform)transform;
        Vector2 p = view.InverseTransformPoint(target.TransformPoint(target.rect.center));
        Rect r = view.rect;
        Vector2 shift = Vector2.zero;
        if (p.x < r.xMin + RevealMargin) shift.x = r.xMin + RevealMargin - p.x;
        else if (p.x > r.xMax - RevealMargin) shift.x = r.xMax - RevealMargin - p.x;
        if (p.y < r.yMin + RevealMargin) shift.y = r.yMin + RevealMargin - p.y;
        else if (p.y > r.yMax - RevealMargin) shift.y = r.yMax - RevealMargin - p.y;
        if (shift == Vector2.zero) return;

        // content의 부모가 뷰포트라서 뷰포트 로컬 거리 = anchoredPosition 거리다.
        content.DOKill();
        content.DOAnchorPos(content.anchoredPosition + shift, 0.18f).SetEase(Ease.OutCubic).SetUpdate(true);
    }
}
