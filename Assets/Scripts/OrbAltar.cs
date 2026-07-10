using System.Collections;
using UnityEngine;

public class OrbAltar : MonoBehaviour
{
    [SerializeField] private GameObject smallOrbPrefab;
    [SerializeField] private float lifetime = 15f;
    [SerializeField] private float orbInterval = 2f;
    [SerializeField] private float lightningInterval = 2.5f;
    [SerializeField] private int orbDirectionCount = 8;

    public float OrbDamage { get; set; }
    public float LightningDamage { get; set; }
    public bool ApplyVulnerable { get; set; }
    public bool IsCrit { get; set; }

    private void Start()
    {
        StartCoroutine(OrbRoutine());
        StartCoroutine(LightningRoutine());
        Destroy(gameObject, lifetime);
    }

    private IEnumerator OrbRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(orbInterval);
            FireOrbBurst();
        }
    }

    private void FireOrbBurst()
    {
        if (smallOrbPrefab == null) return;

        for (int i = 0; i < orbDirectionCount; i++)
        {
            float angle = i * (360f / orbDirectionCount) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            GameObject obj = Instantiate(smallOrbPrefab, transform.position, Quaternion.identity);
            obj.transform.localScale *= 0.5f;
            SmallOrb orb = obj.GetComponent<SmallOrb>();
            orb.Init(dir, OrbDamage, ApplyVulnerable);
            orb.IsCrit = IsCrit;
        }
    }

    private IEnumerator LightningRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(lightningInterval);
            foreach (Enemy enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                enemy.TakeDamage(LightningDamage, isLightningProc: true, isCrit: IsCrit);
        }
    }
}
