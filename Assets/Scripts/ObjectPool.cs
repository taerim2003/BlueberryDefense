using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPool : MonoBehaviour
{
    private static ObjectPool instance;
    public static ObjectPool Instance
    {
        get
        {
            if (instance == null)
                instance = new GameObject("ObjectPool").AddComponent<ObjectPool>();
            return instance;
        }
    }

    private readonly Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        => Spawn(prefab, position, rotation, out _);

    // reused = 풀에서 꺼내 재사용한 것(true) / 새로 Instantiate한 것(false).
    // 재사용은 **Awake가 다시 돌지 않으므로**, 스폰마다 초기화가 필요한 쪽(Enemy.Spawn)이 이 값을 보고
    // 직접 초기화 함수를 부른다. VFX처럼 상태가 없는 것들은 신경 쓸 필요 없다.
    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, out bool reused)
    {
        reused = false;
        if (prefab == null) return null;

        if (!pools.TryGetValue(prefab, out Queue<GameObject> queue))
        {
            queue = new Queue<GameObject>();
            pools[prefab] = queue;
        }

        GameObject obj;
        PooledInstance pooled;
        if (queue.Count > 0)
        {
            reused = true;
            obj = queue.Dequeue();
            obj.transform.SetPositionAndRotation(position, rotation);
            obj.SetActive(true);
            pooled = obj.GetComponent<PooledInstance>();
        }
        else
        {
            obj = Instantiate(prefab, position, rotation);
            pooled = obj.AddComponent<PooledInstance>();
            pooled.SourcePrefab = prefab;
            pooled.CacheEffects();
            obj.transform.SetParent(transform, true);
        }

        foreach (ParticleSystem ps in pooled.Particles)
        {
            ps.Clear(true);
            ps.Play(true);
        }

        for (int i = 0; i < pooled.Sources.Length; i++)
        {
            AudioSource source = pooled.Sources[i];
            if (source.clip == null) continue;
            // 원본 VFX 팩 클립이 대부분 0dBFS 근처로 마스터링돼 있어 볼륨을 더 올리면 클리핑이 난다.
            // 브릭월 리미터를 걸어서 순간 피크만 눌러주고 게인은 더 높게 잡을 수 있게 한다.
            if (source.GetComponent<SfxLimiter>() == null) source.gameObject.AddComponent<SfxLimiter>();
            // 프리팹에 직접 붙은 소스라 SfxPlayer를 안 거친다 — 여기서 "효과음" 슬라이더를 직접 곱해 준다.
            // ⚠️ 스폰 시점에만 곱하므로 이미 재생 중인 루프 사운드는 슬라이더를 움직여도 안 바뀐다(다음 스폰부터 반영).
            source.volume = pooled.SourceBaseVolumes[i] * VolumeSettings.Sfx;
            if (AudioThrottle.TryConsume(source.clip)) source.Play();
            else source.Stop();
        }

        return obj;
    }

    public void Despawn(GameObject obj, float delay = 0f)
    {
        if (obj == null) return;

        if (delay > 0f)
        {
            StartCoroutine(DespawnRoutine(obj, delay));
            return;
        }

        PooledInstance pooled = obj.GetComponent<PooledInstance>();
        if (pooled == null || pooled.SourcePrefab == null)
        {
            Destroy(obj);
            return;
        }

        obj.SetActive(false);
        pools[pooled.SourcePrefab].Enqueue(obj);
    }

    private IEnumerator DespawnRoutine(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        Despawn(obj, 0f);
    }
}

public class PooledInstance : MonoBehaviour
{
    public GameObject SourcePrefab;

    // 스폰마다 GetComponentsInChildren을 돌면 그때마다 배열이 새로 할당된다. 적 물량(한 판 100마리 이상 +
    // UFO 투하)에선 그게 그대로 GC 부담이라, 생성 시 한 번만 캐시해 둔다. 적 프리팹은 보통 둘 다 비어 있어
    // 재사용 스폰의 이펙트 처리 비용이 사실상 0이 된다.
    public ParticleSystem[] Particles;
    public AudioSource[] Sources;

    // 프리팹이 원래 갖고 있던 볼륨. 스폰마다 여기에 "효과음" 슬라이더를 곱하므로,
    // 곱한 결과를 source.volume에 덮어쓰고 나면 원본값을 알 길이 없어져 따로 기억해 둔다.
    public float[] SourceBaseVolumes;

    public void CacheEffects()
    {
        Particles = GetComponentsInChildren<ParticleSystem>();
        Sources = GetComponentsInChildren<AudioSource>();

        SourceBaseVolumes = new float[Sources.Length];
        for (int i = 0; i < Sources.Length; i++) SourceBaseVolumes[i] = Sources[i].volume;
    }
}
