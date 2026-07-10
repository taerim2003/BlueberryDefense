using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using DG.Tweening;

public class Tooltip : MonoBehaviour
{
    public static Tooltip Instance { get; private set; }

    [Header("References")]
    [SerializeField] private UITransition transition;
    [SerializeField] private TMP_Text text;
    [SerializeField] private RectTransform panel;

    [Header("Settings")]
    [SerializeField] private float showDelay = 0.3f;
    [SerializeField] private float verticalOffset = 12f;

    [Header("Rotation")]
    [SerializeField] private float rotationAngle = 6f;
    [SerializeField] private float rotationDuration = 0.4f;

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private RectTransform _rect;
    private Tween _delayTween;

    private void Awake()
    {
        Instance = this;
        _rect = GetComponent<RectTransform>();
        _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0f); // 오브젝트 위에 붙도록 하단 중앙 기준

        _canvas = GetComponentInParent<Canvas>().rootCanvas;
        _canvasRect = _canvas.GetComponent<RectTransform>();
        transform.SetParent(_canvas.transform, false);
        transform.SetAsLastSibling();

        gameObject.SetActive(false);
    }

    public void Show(string content, RectTransform source)
    {
        _delayTween?.Kill();
        _delayTween = DOVirtual.DelayedCall(showDelay, () =>
        {
            text.text = content;
            PositionAbove(source);
            transition.Show();

            if (panel != null)
            {
                panel.DOKill();
                panel.localRotation = Quaternion.identity;
                panel.DOPunchRotation(new Vector3(0f, 0f, rotationAngle), rotationDuration, 2, 0.5f);
            }
        });
    }

    public void Hide()
    {
        _delayTween?.Kill();
        gameObject.SetActive(false);
    }

    private void PositionAbove(RectTransform source)
    {
        var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        // source 상단 중앙을 screen 좌표로 변환
        var corners = new Vector3[4];
        source.GetWorldCorners(corners);
        var topCenter = (corners[1] + corners[2]) * 0.5f;
        var screenPos = RectTransformUtility.WorldToScreenPoint(camera, topCenter);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect, screenPos, camera, out var localPos);

        _rect.anchoredPosition = localPos + Vector2.up * verticalOffset;
    }
}
