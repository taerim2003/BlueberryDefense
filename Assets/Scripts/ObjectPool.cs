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
    {
        if (prefab == null) return null;

        if (!pools.TryGetValue(prefab, out Queue<GameObject> queue))
        {
            queue = new Queue<GameObject>();
            pools[prefab] = queue;
        }

        GameObject obj;
        if (queue.Count > 0)
        {
            obj = queue.Dequeue();
            obj.transform.SetPositionAndRotation(position, rotation);
            obj.SetActive(true);
        }
        else
        {
            obj = Instantiate(prefab, position, rotation);
            obj.AddComponent<PooledInstance>().SourcePrefab = prefab;
            obj.transform.SetParent(transform, true);
        }

        foreach (ParticleSystem ps in obj.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Clear(true);
            ps.Play(true);
        }

        foreach (AudioSource source in obj.GetComponentsInChildren<AudioSource>())
        {
            if (source.clip == null) continue;
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
}
