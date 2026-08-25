using System.Collections;
using UnityEngine;
using DG.Tweening;

// 타이틀 메뉴 버튼이 화면 오른쪽 밖에서 하나씩 미끄러져 들어오는 등장 연출.
// 이 컴포넌트는 버튼들의 **부모**(Canvas/Layout)에 붙인다 — 자식 순서가 곧 등장 순서다.
//
// 🔴 버튼들은 VerticalLayoutGroup 아래라 제자리(x)를 레이아웃이 정한다. Awake·OnEnable 시점엔
//    아직 안 정해져 있어서 그때 읽으면 0이 나온다(UIFloat이 같은 함정에 빠져 버튼 4개가
//    한자리에 겹쳤던 자리다). 두 프레임 보낸 뒤에 읽고, 그 사이엔 CanvasGroup으로 가려 둔다 —
//    안 가리면 제자리에 한 번 번쩍 보였다가 화면 밖으로 튄다.
//
// UIFloat(둥둥 뜨는 연출)과는 안 싸운다: 저쪽은 y만, 이쪽은 x만 만진다.
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(CanvasGroup))]
public class TitleMenuIntro : MonoBehaviour
{
    [SerializeField] private float distance = 420f;  // 오른쪽 화면 밖 시작점까지 (버튼 오른끝 1901 + 여유 > 1920)
    [SerializeField] private float stagger = 0.2f;   // 버튼 사이 시간차
    [SerializeField] private float duration = 0.45f;

    private CanvasGroup _group;
    private Sequence _seq;

    private void Awake()
    {
        _group = GetComponent<CanvasGroup>();
        _group.alpha = 0f;
    }

    private void OnEnable() => StartCoroutine(PlayAfterLayout());

    private void OnDisable()
    {
        _seq?.Kill();
        _seq = null;
    }

    private IEnumerator PlayAfterLayout()
    {
        yield return null;
        yield return null; // 캔버스 레이아웃이 x를 정한 뒤

        int n = transform.childCount;
        var items = new RectTransform[n];
        var homeX = new float[n];
        for (int i = 0; i < n; i++)
        {
            items[i] = (RectTransform)transform.GetChild(i);
            homeX[i] = items[i].anchoredPosition.x;
            SetX(items[i], homeX[i] + distance);
        }

        _group.alpha = 1f;

        _seq = DOTween.Sequence().SetUpdate(true);
        for (int i = 0; i < n; i++)
        {
            RectTransform rt = items[i];
            float target = homeX[i];
            _seq.Insert(i * stagger,
                DOTween.To(() => rt.anchoredPosition.x, x => SetX(rt, x), target, duration)
                       .SetEase(Ease.OutCubic));
        }
    }

    private static void SetX(RectTransform rt, float x)
    {
        Vector2 p = rt.anchoredPosition;
        p.x = x;
        rt.anchoredPosition = p;
    }
}
