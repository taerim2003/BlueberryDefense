using System.Collections.Generic;
using UnityEngine;

// 만화식 의성어(BAM!/BOOM!/SMASH!)를 터뜨려 "쓸어버렸다"는 순간에 시각적 방점을 찍는다.
//
// 발동 지점은 셋:
//  1) 멀티킬 — comboWindow 안에 누적된 처치 수가 3/6/10을 넘을 때마다 한 단계씩 승급하며 터진다.
//     한 콤보에서 최대 3번(BAM→BOOM→SMASH)까지 단계적으로 올라가므로 대규모 쓸이가 그대로 연출이 된다.
//  2) 보스(대왕 블루베리) 처치 — 무조건 SMASH!, 전역 간격 무시.
//  3) UFO 수송선을 부대 투하 전에 격추 — BOOM!. 대공 플레이에 대한 즉각 보상.
//
// 매 타격마다 띄우면 화면이 글자로 뒤덮이므로 minInterval(전역 간격)로 조인다.
// 콤보 상태는 인스턴스 필드 — 씬과 함께 사라져서 판 간 static 누수가 없다.
public class ComicBurst : MonoBehaviour
{
    public static ComicBurst Instance { get; private set; }

    public enum Word { Baam, Boom, Smash }

    [SerializeField] private Sprite baamSprite;
    [SerializeField] private Sprite boomSprite;
    [SerializeField] private Sprite smashSprite;

    [Header("크기 (월드 스케일)")]
    [SerializeField] private float baamScale = 1.1f;
    [SerializeField] private float boomScale = 1.35f;
    [SerializeField] private float smashScale = 1.7f;

    [Header("연출")]
    [SerializeField] private float lifetime = 0.6f;
    [SerializeField] private float riseSpeed = 1.1f;
    [SerializeField] private float tiltDegrees = 12f;   // 좌우 랜덤 기울기(만화 느낌)
    [SerializeField] private float minInterval = 0.35f; // 연속으로 터지지 않게 하는 전역 간격

    [Header("멀티킬 기준 (comboWindow 안 누적 처치 수)")]
    [SerializeField] private float comboWindow = 0.6f;
    [SerializeField] private int baamKills = 3;
    [SerializeField] private int boomKills = 6;
    [SerializeField] private int smashKills = 10;

    private const int SortingOrder = 600; // 적(100 + x*10)보다 확실히 위

    private readonly List<Burst> bursts = new List<Burst>();
    private float nextAllowedTime;

    // 멀티킬 콤보 누적
    private int comboKills;
    private float comboExpireTime;
    private int comboTierFired;      // 이 콤보에서 이미 터뜨린 단계(0=없음, 1=BAM, 2=BOOM, 3=SMASH)
    private Vector3 comboPositionSum;

    private class Burst
    {
        public Transform tr;
        public SpriteRenderer sr;
        public bool active;
        public float t;
        public float baseScale;
        public Vector3 origin;
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── 외부 진입점 ────────────────────────────────────────────────

    // 잡몹 처치 1회 — 멀티킬 누적에만 기여한다(단독으로는 아무것도 안 터짐).
    public static void NotifyKill(Vector3 worldPos)
    {
        if (Instance != null) Instance.RegisterKill(worldPos);
    }

    // 보스 처치 등 단독으로 확실히 터뜨려야 하는 순간. ignoreInterval=true면 전역 간격을 무시한다.
    public static void Pop(Vector3 worldPos, Word word, bool ignoreInterval = false)
    {
        if (Instance != null) Instance.Fire(worldPos, word, ignoreInterval);
    }

    // ── 멀티킬 누적 ────────────────────────────────────────────────

    private void RegisterKill(Vector3 worldPos)
    {
        if (Time.time > comboExpireTime)
        {
            comboKills = 0;
            comboTierFired = 0;
            comboPositionSum = Vector3.zero;
        }

        comboExpireTime = Time.time + comboWindow;
        comboKills++;
        comboPositionSum += worldPos;
    }

    // 누적 처치 수를 프레임 끝에 한 번만 평가한다. 여기서 즉시 터뜨리지 않고 모아서 보는 이유:
    // 대형 광역기가 한 프레임에 12마리를 지우면 3·6·10 단계를 동시에 넘는데, 그때마다 터뜨리면
    // 글자 3개가 겹치거나 전역 간격에 막혀 **가장 작은 BAM만** 남는다(정반대 결과).
    // 그래서 한 프레임에 여러 단계를 넘겼으면 가장 높은 단계 하나만 터뜨린다.
    private void EvaluateCombo()
    {
        if (comboKills == 0) return;

        int tier = comboKills >= smashKills ? 3
                 : comboKills >= boomKills ? 2
                 : comboKills >= baamKills ? 1
                 : 0;

        if (tier <= comboTierFired) return;

        comboTierFired = tier;
        Vector3 centroid = comboPositionSum / comboKills; // 쓸어버린 무리의 한가운데서 터진다
        // BOOM·SMASH는 진짜 승급이라 전역 간격을 무시하고 반드시 터진다(콤보당 최대 1회씩).
        Fire(centroid, tier == 3 ? Word.Smash : tier == 2 ? Word.Boom : Word.Baam, ignoreInterval: tier >= 2);
    }

    // ── 연출 ──────────────────────────────────────────────────────

    private void Fire(Vector3 worldPos, Word word, bool ignoreInterval)
    {
        Sprite sprite = word switch
        {
            Word.Smash => smashSprite,
            Word.Boom => boomSprite,
            _ => baamSprite,
        };
        if (sprite == null) return;
        if (!ignoreInterval && Time.time < nextAllowedTime) return;

        nextAllowedTime = Time.time + minInterval;

        Burst b = Rent();
        b.sr.sprite = sprite;
        b.baseScale = word switch
        {
            Word.Smash => smashScale,
            Word.Boom => boomScale,
            _ => baamScale,
        };
        b.origin = ClampToView(worldPos);
        b.t = 0f;
        b.tr.position = b.origin;
        b.tr.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-tiltDegrees, tiltDegrees));
        b.tr.localScale = Vector3.zero;
        b.active = true;
    }

    // 화면 밖에서 터지면 안 보이므로 카메라 뷰 안으로 당긴다.
    private Vector3 ClampToView(Vector3 worldPos)
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic) return worldPos;

        float halfH = cam.orthographicSize - 1f; // 글자 크기만큼 여유
        float halfW = halfH * cam.aspect;
        Vector3 c = cam.transform.position;
        return new Vector3(
            Mathf.Clamp(worldPos.x, c.x - halfW, c.x + halfW),
            Mathf.Clamp(worldPos.y, c.y - halfH, c.y + halfH),
            worldPos.z);
    }

    private void Update()
    {
        EvaluateCombo();

        for (int i = 0; i < bursts.Count; i++)
        {
            Burst b = bursts[i];
            if (!b.active) continue;

            b.t += Time.deltaTime;
            if (b.t >= lifetime)
            {
                b.active = false;
                b.tr.gameObject.SetActive(false);
                continue;
            }

            // 튀어나왔다가(0.10초) 살짝 되돌아오는(0.08초) 만화식 팝
            float pop = b.t < 0.10f ? Mathf.Lerp(0.35f, 1.18f, b.t / 0.10f)
                      : b.t < 0.18f ? Mathf.Lerp(1.18f, 1f, (b.t - 0.10f) / 0.08f)
                      : 1f;
            b.tr.localScale = Vector3.one * (b.baseScale * pop);
            b.tr.position = b.origin + Vector3.up * (riseSpeed * b.t);

            float fadeStart = lifetime * 0.6f;
            Color col = b.sr.color;
            col.a = b.t < fadeStart ? 1f : 1f - (b.t - fadeStart) / (lifetime - fadeStart);
            b.sr.color = col;
        }
    }

    private Burst Rent()
    {
        for (int i = 0; i < bursts.Count; i++)
        {
            if (bursts[i].active) continue;
            bursts[i].tr.gameObject.SetActive(true);
            return bursts[i];
        }

        GameObject go = new GameObject("ComicBurst", typeof(SpriteRenderer));
        go.transform.SetParent(transform, false);
        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        sr.sortingOrder = SortingOrder;

        Burst created = new Burst { tr = go.transform, sr = sr };
        bursts.Add(created);
        return created;
    }
}
