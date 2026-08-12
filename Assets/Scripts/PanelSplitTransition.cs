using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// 화면을 넘길 때 패널 내용을 위아래로 갈라 밀어내는 연출.
// 화면 중앙보다 위에 있는 요소는 위로, 아래에 있는 요소는 아래로 빠지고, 들어올 땐 그 반대다.
//
// UITransition(패널을 통째로 미는 쪽)과 달리 자식을 하나씩 움직인다. 그래서 같은 자리를 만지는
// UIFloat(둥둥 뜨는 연출)과 싸우게 되므로, 전환이 도는 동안에는 UIFloat을 꺼 둔다.
[RequireComponent(typeof(CanvasGroup))]
public class PanelSplitTransition : MonoBehaviour
{
    [SerializeField] private RectTransform contentRoot;  // 비우면 자기 자신 — 이 아래 직속 자식들이 갈라진다
    [SerializeField] private RectTransform[] ignore;     // 배경처럼 제자리에 둘 것
    [SerializeField] private float distance = 760f;      // 화면 밖으로 밀어내는 거리
    [SerializeField] private float duration = 0.36f;

    private CanvasGroup _cg;
    private RectTransform[] _items;
    private Vector2[] _home;
    private UIFloat[] _floats;
    private Sequence _seq;

    private void Awake()
    {
        _cg = GetComponent<CanvasGroup>();
        Cache();
        _cg.alpha = 0f;
    }

    private void OnEnable() => Show();

    private void Cache()
    {
        RectTransform root = contentRoot != null ? contentRoot : (RectTransform)transform;
        var items = new List<RectTransform>();
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i) as RectTransform;
            if (c == null) continue;
            if (ignore != null && System.Array.IndexOf(ignore, c) >= 0) continue;
            items.Add(c);
        }
        _items = items.ToArray();
        _home = new Vector2[_items.Length];
        _floats = new UIFloat[_items.Length];
        for (int i = 0; i < _items.Length; i++)
        {
            _home[i] = _items[i].anchoredPosition;
            _floats[i] = _items[i].GetComponent<UIFloat>();
        }
    }

    // 위에 있는 것은 위로, 아래에 있는 것은 아래로. 정확히 중앙이면 위로 보낸다.
    private Vector2 Offset(int i) => new Vector2(0f, _home[i].y >= 0f ? distance : -distance);

    public void Show()
    {
        if (_items == null) Cache();
        if (!gameObject.activeSelf) { gameObject.SetActive(true); return; } // OnEnable이 다시 부른다

        Kill();
        SetFloats(false);

        var seq = DOTween.Sequence();
        for (int i = 0; i < _items.Length; i++)
        {
            var rt = _items[i];
            Vector2 home = _home[i];
            rt.anchoredPosition = home + Offset(i);
            seq.Join(DOTween.To(() => rt.anchoredPosition, x => rt.anchoredPosition = x, home, duration).SetEase(Ease.OutCubic));
        }
        _cg.alpha = 0f;
        seq.Join(DOTween.To(() => _cg.alpha, x => _cg.alpha = x, 1f, duration * 0.7f).SetEase(Ease.OutQuad));
        seq.OnComplete(() => { SnapHome(); SetFloats(true); });
        _seq = seq;
    }

    public void Hide()
    {
        if (_items == null) Cache();
        Kill();
        SetFloats(false);

        var seq = DOTween.Sequence();
        for (int i = 0; i < _items.Length; i++)
        {
            var rt = _items[i];
            Vector2 away = _home[i] + Offset(i);
            seq.Join(DOTween.To(() => rt.anchoredPosition, x => rt.anchoredPosition = x, away, duration).SetEase(Ease.InCubic));
        }
        seq.Join(DOTween.To(() => _cg.alpha, x => _cg.alpha = x, 0f, duration * 0.8f).SetEase(Ease.InQuad));
        seq.OnComplete(() =>
        {
            SnapHome();          // 다음에 열릴 때를 위해 제자리로 되돌려 둔다
            gameObject.SetActive(false);
        });
        _seq = seq;
    }

    private void Kill()
    {
        _seq?.Kill();
        _seq = null;
        if (_items == null) return;
        for (int i = 0; i < _items.Length; i++) _items[i].DOKill();
        _cg.DOKill();
    }

    private void SnapHome()
    {
        for (int i = 0; i < _items.Length; i++) _items[i].anchoredPosition = _home[i];
    }

    // 켜고 끄는 김에 "제자리"도 같이 넘긴다. UIFloat이 자기 Awake에서 읽은 값은
    // 이 전환이 이미 화면 밖으로 밀어둔 좌표일 수 있어서(활성화 순서가 보장되지 않는다),
    // 여기서 덮어쓰지 않으면 그 오브젝트가 화면 밖에 눌러앉는다.
    private void SetFloats(bool on)
    {
        if (_floats == null) return;
        for (int i = 0; i < _floats.Length; i++)
        {
            if (_floats[i] == null) continue;
            _floats[i].SetHomeY(_home[i].y);
            _floats[i].enabled = on;
        }
    }
}
