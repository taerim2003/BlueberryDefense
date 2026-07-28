using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// 보물 획득 패널 전용 연출. 이 컴포넌트가 붙은 오브젝트(패널 딤과 카드 사이 레이어)에서
// 정수 스프라이트가 빙글빙글 돌며 위에서 쏟아져 내리고, 좌측 하단 보물상자 블루베리가
// 팡 튀어나와 살랑살랑 흔들리는 연출을 준다. 일반 레벨업 패널과 시각적으로 구분하기 위함.
//
// 모달이 timeScale 0으로 멈추므로 모든 연출은 unscaled 시간으로 구동한다.
// 정수 조각은 Awake에서 자식으로 한 번만 생성해두고 재활용한다(런타임 Instantiate 없음).
[DisallowMultipleComponent]
public class TreasureRewardDecor : MonoBehaviour
{
    [Header("정수 비")]
    [SerializeField] private Sprite essenceSprite;
    [SerializeField] private int count = 16;
    [SerializeField] private Vector2 sizeRange = new Vector2(45f, 110f);
    [SerializeField] private Vector2 fallSpeedRange = new Vector2(150f, 360f);
    [SerializeField] private Vector2 spinSpeedRange = new Vector2(120f, 400f); // deg/sec
    [SerializeField] private Vector2 swayRange = new Vector2(20f, 60f);         // 좌우 흔들림 진폭

    [Header("보물상자")]
    [SerializeField] private RectTransform chest; // 좌측 하단 보물상자(별도 오브젝트, 카드 위 레이어)

    private RectTransform area;
    private RectTransform[] pieces;
    private Image[] images;
    private float[] fallSpeed, spinSpeed, swayAmp, swayFreq, phase, baseX;
    private Vector3 chestBaseScale = Vector3.one;

    private void Awake()
    {
        area = (RectTransform)transform;
        if (chest != null) chestBaseScale = chest.localScale;
        BuildPieces();
    }

    private void BuildPieces()
    {
        pieces = new RectTransform[count];
        images = new Image[count];
        fallSpeed = new float[count];
        spinSpeed = new float[count];
        swayAmp = new float[count];
        swayFreq = new float[count];
        phase = new float[count];
        baseX = new float[count];

        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject("Essence" + i, typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(area, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            Image img = go.GetComponent<Image>();
            img.sprite = essenceSprite;
            img.raycastTarget = false;
            img.preserveAspect = true;
            pieces[i] = rt;
            images[i] = img;
        }
    }

    private void OnEnable()
    {
        for (int i = 0; i < count; i++) Spawn(i, true);
        if (chest != null) AnimateChest();
    }

    private void OnDisable()
    {
        if (chest == null) return;
        chest.DOKill();
        chest.localScale = chestBaseScale;
        chest.localRotation = Quaternion.identity;
    }

    // 보물상자: 팡 튀어오른 뒤 좌우로 살랑살랑 흔들리는 무한 루프.
    private void AnimateChest()
    {
        chest.DOKill();
        chest.localScale = chestBaseScale * 0.4f;
        chest.localRotation = Quaternion.identity;
        chest.DOScale(chestBaseScale, 0.5f).SetEase(Ease.OutBack).SetUpdate(true).OnComplete(() =>
        {
            chest.DOLocalRotate(new Vector3(0f, 0f, 6f), 1.6f)
                 .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
        });
    }

    // 조각 하나를 (재)배치. anywhere=true면 화면 아무 높이(첫 등장 시 화면을 채우려고),
    // false면 화면 위쪽 바깥에서 새로 떨어지기 시작.
    private void Spawn(int i, bool anywhere)
    {
        RectTransform rt = pieces[i];
        float hw = area.rect.width * 0.5f;
        float hh = area.rect.height * 0.5f;
        float size = Random.Range(sizeRange.x, sizeRange.y);
        rt.sizeDelta = new Vector2(size, size);
        baseX[i] = Random.Range(-hw, hw);
        float y = anywhere ? Random.Range(-hh, hh) : hh + size;
        rt.anchoredPosition = new Vector2(baseX[i], y);
        rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        fallSpeed[i] = Random.Range(fallSpeedRange.x, fallSpeedRange.y);
        spinSpeed[i] = Random.Range(spinSpeedRange.x, spinSpeedRange.y) * (Random.value < 0.5f ? -1f : 1f);
        swayAmp[i] = Random.Range(swayRange.x, swayRange.y);
        swayFreq[i] = Random.Range(0.4f, 1.3f);
        phase[i] = Random.Range(0f, 10f);
        // 큰 조각일수록 진하게 — 원근감(가까운 조각이 크고 선명).
        float t = Mathf.InverseLerp(sizeRange.x, sizeRange.y, size);
        images[i].color = new Color(1f, 1f, 1f, Mathf.Lerp(0.45f, 1f, t));
    }

    private void Update()
    {
        if (pieces == null) return;
        float dt = Time.unscaledDeltaTime;
        float hh = area.rect.height * 0.5f;
        for (int i = 0; i < count; i++)
        {
            RectTransform rt = pieces[i];
            Vector2 p = rt.anchoredPosition;
            p.y -= fallSpeed[i] * dt;
            phase[i] += dt;
            p.x = baseX[i] + Mathf.Sin(phase[i] * swayFreq[i] * Mathf.PI * 2f) * swayAmp[i];
            rt.anchoredPosition = p;
            rt.Rotate(0f, 0f, spinSpeed[i] * dt);
            if (p.y < -hh - rt.sizeDelta.y) Spawn(i, false); // 바닥 아래로 나가면 위에서 재투입
        }
    }
}
