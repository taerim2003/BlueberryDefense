using UnityEngine;
using UnityEngine.EventSystems;

// 트리 팬(드래그) + 줌(휠) — 뷰포트에 부착, content를 이동/확대축소.
public class TreePanDrag : MonoBehaviour, IDragHandler, IScrollHandler
{
    [SerializeField] private RectTransform content;
    [SerializeField] private float minZoom = 0.45f;
    [SerializeField] private float maxZoom = 1.6f;
    [SerializeField] private float zoomSpeed = 0.1f;

    public void OnDrag(PointerEventData e)
    {
        if (content != null) content.anchoredPosition += e.delta;
    }

    public void OnScroll(PointerEventData e)
    {
        if (content == null) return;
        float z = content.localScale.x * (1f + e.scrollDelta.y * zoomSpeed);
        z = Mathf.Clamp(z, minZoom, maxZoom);
        content.localScale = new Vector3(z, z, 1f);
    }
}
