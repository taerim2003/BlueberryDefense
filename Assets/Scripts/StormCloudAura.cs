using System.Collections.Generic;
using UnityEngine;

// 낙뢰 버프 스택 수만큼 머리 위에 뇌운을 띄운다. 스택이 늘고 줄 때마다 개수를 맞춘다.
// ⚠️ 플레이어의 localScale이 1.5라 자식으로 붙이면 구름 크기가 곱해진다 — 월드에 독립으로 두고 매 프레임 따라간다.
// 떠오르는 등·퇴장 연출은 RiseFade가 소유하므로 여기서는 Anchor(기준 위치)만 갱신한다.
public class StormCloudAura : MonoBehaviour
{
    [SerializeField] private GameObject cloudPrefab;
    [SerializeField] private float height = 2.2f;    // 머리 위 높이(유닛)
    [SerializeField] private float spacingX = 0.5f;  // 구름끼리 가로 간격 — 그림 폭(3유닛)보다 훨씬 좁아 서로 겹친다
    [SerializeField] private float stagger = 0.22f;  // 홀수번째를 살짝 올려 겹친 게 보이게
    [SerializeField] private int maxClouds = 6;

    private readonly List<RiseFade> clouds = new List<RiseFade>();

    private void LateUpdate()
    {
        if (cloudPrefab == null) return;

        int want = Mathf.Clamp(LightningStorm.ActiveStackCount, 0, maxClouds);

        while (clouds.Count < want)
        {
            GameObject go = ObjectPool.Instance.Spawn(cloudPrefab, transform.position + Vector3.up * height, Quaternion.identity);
            RiseFade rf = go.GetComponent<RiseFade>();
            if (rf == null) { ObjectPool.Instance.Despawn(go); return; } // 프리팹 배선이 틀린 것 — 무한 스폰을 막는다
            clouds.Add(rf);
        }

        while (clouds.Count > want)
        {
            int last = clouds.Count - 1;
            RiseFade rf = clouds[last];
            clouds.RemoveAt(last);
            // 목록에서 빠진 뒤에는 Anchor 갱신을 못 받는다 — 사라지는 구름은 그 자리에서 위로 빠진다(의도).
            if (rf != null) rf.BeginExit();
        }

        if (clouds.Count == 0) return;

        // 가로로 중앙 정렬해 늘어놓는다 — 스택이 늘면 양옆으로 벌어지며 뭉게구름처럼 두꺼워진다.
        Vector3 basePos = transform.position + Vector3.up * height;
        for (int i = 0; i < clouds.Count; i++)
        {
            if (clouds[i] == null) continue;
            float x = (i - (clouds.Count - 1) * 0.5f) * spacingX;
            float y = (i % 2 == 0) ? 0f : stagger;
            clouds[i].Anchor = basePos + new Vector3(x, y, 0f);
        }
    }

    private void OnDisable()
    {
        // 게임오버·씬 전환으로 꺼질 때 구름만 화면에 남지 않게 한다(연출 없이 즉시 반납).
        foreach (RiseFade rf in clouds)
            if (rf != null) ObjectPool.Instance.Despawn(rf.gameObject);
        clouds.Clear();
    }
}
