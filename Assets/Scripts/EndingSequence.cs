using System.Collections;
using TMPro;
using UnityEngine;

// 엔딩 — 광활한 우주 **어려움**(25판)을 깨면 클리어 화면 대신 이게 돈다(GameManager.GameClear).
//  ① 3초 정적(스킬 봉인 · BGM 끔) → ② 왼쪽 확대, 블루베리 군집체가 굴러 나옴 → 원래 화면
//  ③ "모든 블루베리의 결집체가…" + 스킬 해제 → ④ 군집체 격파 → 사방으로 터지며 암전
//  ⑤ 이야기 두 줄 → "Game Cleared!" + BGM 다시 → ⑥ 크레딧(제목·이름 쌍 하나씩) → 감사 인사 → 타이틀(암전 풀림)
// 문구·배치는 Resources/EndingSequence 프리팹에서, 크레딧 명단은 CreditsContent 프리팹에서 고친다(타이틀 크레딧 창과 공유).
public class EndingSequence : MonoBehaviour
{
    private const string ResourcePath = "EndingSequence";
    private const string EndingMapName = "Map_Wide20"; // 광활한 우주
    private const int EndingAscension = 3;             // 어려움(25판)
    private const float LaneFeetDepth = 0.56f;         // 레인 y에서 기본 블루베리 발밑까지(24px ÷ PPU32 × 1.5배 ÷ 2)

    [Header("배선")]
    [SerializeField] private GameObject clusterPrefab;
    [SerializeField] private GameObject creditsContentPrefab; // 타이틀 크레딧 창과 같은 명단(Heading_* · Names_* 쌍)
    [SerializeField] private CanvasGroup black;
    [SerializeField] private CanvasGroup intro;
    [SerializeField] private CanvasGroup story1;
    [SerializeField] private CanvasGroup story2;
    [SerializeField] private CanvasGroup cleared;
    [SerializeField] private CanvasGroup creditGroup;
    [SerializeField] private TMP_Text creditHeading;
    [SerializeField] private TMP_Text creditNames;

    [Header("시간(초)")]
    [SerializeField] private float silence = 3f;
    [SerializeField] private float zoomIn = 0.8f;
    [SerializeField] private float roll = 2.4f;
    [SerializeField] private float zoomHold = 0.6f;
    [SerializeField] private float zoomOut = 0.8f;
    [SerializeField] private float introHold = 3.5f;
    [SerializeField] private float afterBurst = 3f;          // 격파 후 암전까지 — 터지는 여운을 본다
    [SerializeField] private float blackout = 1.5f;
    [SerializeField] private float storyHold = 4f;
    [SerializeField] private float clearedHold = 4.5f;
    [SerializeField] private float creditHold = 3.5f;
    [SerializeField] private float thanksHold = 4.5f;
    [SerializeField] private float fade = 0.9f;

    [Header("연출")]
    [SerializeField] private float zoomScale = 0.7f;          // 확대 시 화면 높이 배율
    [SerializeField] private float clusterStopRatio = 0.22f;   // 군집체가 멈추는 자리 = 화면 왼쪽에서 폭의 이 비율
    [SerializeField] private int burstChunks = 40;

    public static bool ShouldPlay()
    {
        return RunConfig.Map != null && RunConfig.Map.name == EndingMapName
            && RunConfig.AscensionLevel >= EndingAscension
            && !BotInput.SkipEnding; // 봇 측정은 판 결과만 필요하다(BotPilot은 릴리스 빌드에 없어서 플래그로 본다)
    }

    public static void Play()
    {
        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogError("[Ending] Resources/" + ResourcePath + " 프리팹이 없다 — 타이틀로 보낸다");
            SceneFade.LoadScene("Title");
            return;
        }
        Instantiate(prefab).GetComponent<EndingSequence>().StartCoroutine("Run");
    }

    private void Awake()
    {
        foreach (var g in new[] { black, intro, story1, story2, cleared, creditGroup })
            if (g != null) g.alpha = 0f;
    }

    private void OnDestroy() => PlayerSkills.Sealed = false;

    private IEnumerator Run()
    {
        var boot = FindAnyObjectByType<RunBootstrap>();

        // ① 정적
        PlayerSkills.Sealed = true;
        if (boot != null) boot.SetBgmPlaying(false);
        yield return new WaitForSeconds(silence);

        // ② 왼쪽 확대 → 굴러 나옴 → 원래 화면
        Camera cam = Camera.main;
        Vector3 pos0 = cam.transform.position;
        float size0 = cam.orthographicSize;
        float halfW0 = size0 * cam.aspect;
        float left0 = pos0.x - halfW0, bottom0 = pos0.y - size0;

        var spawner = FindAnyObjectByType<EnemySpawner>();
        float laneY = spawner != null ? spawner.transform.position.y : 0f;
        float stopX = left0 + 2f * halfW0 * clusterStopRatio;

        Enemy enemy = Enemy.Spawn(clusterPrefab, new Vector3(left0 - 20f, laneY, 0f));
        var cluster = enemy.GetComponent<BlueberryCluster>();
        enemy.MarkAsBoss();
        if (spawner != null) spawner.ApplyCurrentStageScaling(enemy);
        float r = cluster.Radius;
        Vector3 mp = enemy.transform.position;
        mp.x = left0 - r * 1.5f;
        mp.y = laneY - LaneFeetDepth + r; // 군집체는 가운데 기준점이라 반지름만큼 올려야 발이 레인에 닿는다
        enemy.transform.position = mp;

        float size1 = size0 * zoomScale;
        float halfW1 = size1 * cam.aspect;
        Vector3 pos1 = new Vector3(
            Mathf.Clamp(stopX, left0 + halfW1, pos0.x + halfW0 - halfW1),
            Mathf.Clamp(mp.y, bottom0 + size1, pos0.y + size0 - size1), // 공 한가운데 — 땅과 공 전체가 같이 보이게
            pos0.z);

        yield return Lerp(zoomIn, k => { cam.orthographicSize = Mathf.Lerp(size0, size1, k); cam.transform.position = Vector3.Lerp(pos0, pos1, k); });
        yield return cluster.RollIn(mp.x, stopX, roll);
        ScreenShake.Shake(0.15f, 0.25f);
        yield return new WaitForSeconds(zoomHold);
        yield return Lerp(zoomOut, k => { cam.orthographicSize = Mathf.Lerp(size1, size0, k); cam.transform.position = Vector3.Lerp(pos1, pos0, k); });
        cam.orthographicSize = size0;
        cam.transform.position = pos0;

        // ③ 등장 문구 + 스킬 해제
        PlayerSkills.Sealed = false;
        yield return Fade(intro, 1f);
        yield return new WaitForSeconds(introHold);
        yield return Fade(intro, 0f);

        // ④ 격파까지 대기 → 사방으로 터짐 → 암전
        while (enemy.IsAlive) yield return null;
        Achievements.OnEndingBossKilled();   // 블루베리 군집체 처치
        PlayerSkills.Sealed = true;
        cluster.Burst(burstChunks);
        ScreenShake.Shake(0.35f, 0.6f);
        yield return new WaitForSeconds(afterBurst);
        yield return Fade(black, 1f, blackout);

        // ⑤ 이야기 → Game Cleared! (+ BGM)
        // 두 줄은 한 화면이 아니라 차례로 넘어가는 두 화면이다.
        yield return Fade(story1, 1f);
        yield return new WaitForSeconds(storyHold);
        yield return Fade(story1, 0f);
        yield return Fade(story2, 1f);
        yield return new WaitForSeconds(storyHold);
        yield return Fade(story2, 0f);

        if (boot != null) boot.SetBgmPlaying(true);
        yield return Fade(cleared, 1f);
        yield return new WaitForSeconds(clearedHold);
        yield return Fade(cleared, 0f);

        // ⑥ 크레딧 — 제목(+이름) 한 쌍씩. 이름 없는 마지막 제목이 감사 인사다.
        if (creditsContentPrefab != null)
        {
            Transform list = creditsContentPrefab.transform;
            for (int i = 0; i < list.childCount; i++)
            {
                Transform h = list.GetChild(i);
                if (!h.name.StartsWith("Heading_") || !h.TryGetComponent(out TMP_Text ht)) continue;
                Transform n = i + 1 < list.childCount ? list.GetChild(i + 1) : null;
                bool hasNames = n != null && n.name.StartsWith("Names_") && n.TryGetComponent(out TMP_Text _);

                // 프리팹 원본을 읽으므로 LocalizedTmp가 돌지 않는다 — 키가 있으면 여기서 번역한다.
                creditHeading.text = h.TryGetComponent(out LocalizedTmp lt) && !string.IsNullOrEmpty(lt.key) ? Loc.T(lt.key) : ht.text;
                creditNames.text = hasNames ? n.GetComponent<TMP_Text>().text : "";
                creditNames.gameObject.SetActive(hasNames); // 이름이 없으면 제목 혼자 화면 한가운데(레이아웃 그룹이 가운데로 모은다)
                yield return Fade(creditGroup, 1f);
                yield return new WaitForSeconds(hasNames ? creditHold : thanksHold);
                yield return Fade(creditGroup, 0f);
            }
        }

        SceneFade.LoadScene("Title"); // 이미 검은 화면 → 타이틀이 열리며 밝아진다
    }

    private IEnumerator Fade(CanvasGroup g, float to) => Fade(g, to, fade);

    private IEnumerator Fade(CanvasGroup g, float to, float duration)
    {
        if (g == null) yield break;
        float from = g.alpha;
        yield return Lerp(duration, k => g.alpha = Mathf.Lerp(from, to, k));
        g.alpha = to;
    }

    // 스케일 시간 — 일시정지(ESC)·레벨업 창이 뜨면 연출도 같이 멈춘다.
    private static IEnumerator Lerp(float duration, System.Action<float> step)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            step(k * k * (3f - 2f * k)); // smoothstep
            yield return null;
        }
        step(1f);
    }
}
