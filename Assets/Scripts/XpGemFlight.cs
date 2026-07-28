using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// 적 처치 경험치를 "경험치 바로 빨려들어가는 보석"으로 시각화한다.
// 목표 지점은 플레이어가 아니라 **경험치 바의 현재 차 있는 끝 지점**(fillAmount 위치) —
// 30% 차 있으면 바의 30% 지점으로 빨려들어간다. 바가 차오를수록 도착 지점도 오른쪽으로 밀린다.
//
// XP는 처치 순간이 아니라 **보석이 도착하는 순간** 적립된다. 그래야 "보석이 꽂히니까 바가 오른다"는
// 인과가 화면에 보인다. (부작용으로 보물상자 킬의 AddXP/ShowTreasureReward 순서 경합도 자연히 사라진다.)
//
// 씬 배선: Canvas 아래 전체 스트레치 RectTransform(pivot 0.5)에 붙인다. 보석은 이 오브젝트의 자식으로 런타임 생성.
public class XpGemFlight : MonoBehaviour
{
    public static XpGemFlight Instance { get; private set; }

    [SerializeField] private Image expFill;          // 경험치 바 Fill — 도착 지점 계산용
    [SerializeField] private RectTransform barPunch;  // 도착 시 튕길 바 루트(선택, 미할당이면 생략)
    [SerializeField] private Sprite gemSprite;
    [SerializeField] private Color gemColor = new Color(1f, 0.85f, 0.15f, 1f); // 임시 노란 사각형
    [SerializeField] private Vector2 gemSize = new Vector2(44f, 44f);

    [Header("연출")]
    [SerializeField] private float scatterTime = 0.14f;    // 처치 지점에서 툭 튀어나오는 구간
    [SerializeField] private float scatterDistance = 55f;  // 튀어나가는 거리(캔버스 px)
    [SerializeField] private float homingSpeed = 420f;     // 빨려들어가기 시작 속도(px/s)
    [SerializeField] private float homingAccel = 2400f;    // 가속(px/s²) — 끝에서 확 빨려드는 느낌
    [SerializeField] private float arriveDistance = 14f;
    [SerializeField] private int maxLiveGems = 120;        // 물량이 몰릴 때 상한(넘으면 연출 생략·즉시 적립)

    private RectTransform selfRect;
    private readonly List<Gem> live = new List<Gem>();
    private readonly Stack<Gem> pool = new Stack<Gem>();

    private class Gem
    {
        public RectTransform rect;
        public int xp;
        public float t;
        public Vector2 from;
        public Vector2 scatterTo;
        public float speed;
    }

    private void Awake()
    {
        Instance = this;
        selfRect = (RectTransform)transform;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 처치 지점(월드)에서 보석을 띄운다. 연출이 불가능한 상황이면 false를 돌려주고,
    // 호출부가 기존대로 XP를 즉시 적립한다(HUD 없는 씬 단독 실행 등에서 XP가 증발하지 않게).
    public static bool TrySpawn(Vector3 worldPos, int xp)
    {
        return Instance != null && xp > 0 && Instance.SpawnGems(worldPos, xp);
    }

    private bool SpawnGems(Vector3 worldPos, int xp)
    {
        if (expFill == null || gemSprite == null || Camera.main == null) return false;
        if (live.Count >= maxLiveGems) return false;

        Vector2 screen = Camera.main.WorldToScreenPoint(worldPos);
        // Canvas가 Screen Space - Overlay라 카메라 인자는 null.
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(selfRect, screen, null, out Vector2 origin))
            return false;

        // XP가 클수록(보스·보물상자) 여러 개로 쪼개 쏟아지게 — 잡몹은 1개, 큰 건 최대 5개.
        int count = Mathf.Clamp(1 + xp / 10, 1, 5);
        int each = xp / count;

        for (int i = 0; i < count; i++)
        {
            int share = i == count - 1 ? xp - each * (count - 1) : each;
            if (share <= 0) continue;

            Gem g = Rent();
            g.xp = share;
            g.t = 0f;
            g.speed = homingSpeed;
            g.from = origin;
            float angle = Random.Range(0f, Mathf.PI * 2f);
            g.scatterTo = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                                   * (scatterDistance * Random.Range(0.5f, 1f));
            g.rect.anchoredPosition = origin;
            g.rect.localScale = Vector3.zero;
            live.Add(g);
        }
        return true;
    }

    private void Update()
    {
        if (live.Count == 0) return;

        Vector2 target = selfRect.InverseTransformPoint(FillEndWorld());
        float dt = Time.deltaTime; // 모달(timeScale=0) 중엔 같이 멈춘다 — 게임 전체가 멈춘 것과 일관

        for (int i = live.Count - 1; i >= 0; i--)
        {
            Gem g = live[i];
            g.t += dt;

            if (g.t < scatterTime)
            {
                float k = g.t / scatterTime;
                g.rect.anchoredPosition = Vector2.Lerp(g.from, g.scatterTo, 1f - (1f - k) * (1f - k)); // ease out
                g.rect.localScale = Vector3.one * Mathf.Min(1f, k * 2f);
                continue;
            }

            g.rect.localScale = Vector3.one;
            g.speed += homingAccel * dt;
            Vector2 pos = Vector2.MoveTowards(g.rect.anchoredPosition, target, g.speed * dt);
            g.rect.anchoredPosition = pos;

            if (Vector2.Distance(pos, target) <= arriveDistance)
            {
                Arrive(g);
                live.RemoveAt(i);
            }
        }
    }

    // 경험치 바에서 "지금 차 있는 끝" 지점의 월드 좌표. fillAmount 0.3이면 바 폭의 30% 지점.
    private Vector3 FillEndWorld()
    {
        RectTransform rt = expFill.rectTransform;
        Rect r = rt.rect;
        float x = r.xMin + r.width * Mathf.Clamp01(expFill.fillAmount);
        return rt.TransformPoint(new Vector3(x, r.center.y, 0f));
    }

    private void Arrive(Gem g)
    {
        PlayerExperience.Instance?.AddXP(g.xp);

        if (barPunch != null)
        {
            barPunch.DOKill(true); // 연속 도착 시 직전 펀치를 완료 처리하고 다시 — 스케일이 누적되지 않게
            barPunch.DOPunchScale(new Vector3(0.03f, 0.3f, 0f), 0.22f, 6, 0.6f);
        }

        Return(g);
    }

    private Gem Rent()
    {
        if (pool.Count > 0)
        {
            Gem reused = pool.Pop();
            reused.rect.gameObject.SetActive(true);
            return reused;
        }

        GameObject go = new GameObject("XpGem", typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(selfRect, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = gemSize;

        Image image = go.GetComponent<Image>();
        image.sprite = gemSprite;
        image.color = gemColor;
        image.raycastTarget = false;

        return new Gem { rect = rect };
    }

    private void Return(Gem g)
    {
        g.rect.gameObject.SetActive(false);
        pool.Push(g);
    }
}
