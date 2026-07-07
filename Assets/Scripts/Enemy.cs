using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Enemy : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private int damage = 10;
    [SerializeField] private float maxHealth = 20f;
    [SerializeField] private int xpValue = 5;
    [SerializeField] private bool isTreasure;
    [SerializeField] private GameObject damageNumberPrefab;
    [SerializeField] private GameObject lightningVfxPrefab;
    [SerializeField] private GameObject deathVfxPrefab;

    private float currentHealth;
    private float slowMultiplier = 1f;
    private float slowTimer;
    private float vulnerableMultiplier = 1f;
    private float vulnerableTimer;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    private void Update()
    {
        if (slowTimer > 0f)
        {
            slowTimer -= Time.deltaTime;
            if (slowTimer <= 0f)
                slowMultiplier = 1f;
        }

        if (vulnerableTimer > 0f)
        {
            vulnerableTimer -= Time.deltaTime;
            if (vulnerableTimer <= 0f)
                vulnerableMultiplier = 1f;
        }

        transform.Translate(Vector2.right * moveSpeed * slowMultiplier * Time.deltaTime);
    }

    public void ApplySlow(float multiplier, float duration)
    {
        slowMultiplier = multiplier;
        slowTimer = duration;
    }

    public void ApplyVulnerable(float multiplier, float duration)
    {
        vulnerableMultiplier = multiplier;
        vulnerableTimer = duration;
    }

    public void TakeDamage(float amount, bool isLightningProc = false)
    {
        float actualDamage = amount * vulnerableMultiplier;
        currentHealth -= actualDamage;
        SpawnDamageNumber(actualDamage);

        if (isLightningProc && lightningVfxPrefab != null)
            Destroy(Instantiate(lightningVfxPrefab, transform.position, Quaternion.identity), 2f);

        if (currentHealth <= 0f)
        {
            if (deathVfxPrefab != null)
            {
                GameObject deathVfx = Instantiate(deathVfxPrefab, transform.position, Quaternion.identity);
                deathVfx.transform.localScale = Vector3.one * 0.2f;
                Destroy(deathVfx, 2f);
            }

            PlayerExperience.Instance?.AddXP(xpValue);
            if (isTreasure) LevelUpUI.Instance.ShowTreasureReward();
            Destroy(gameObject);
            return;
        }

        if (!isLightningProc && Time.time < LightningStorm.ActiveUntil && Random.value < LightningStorm.ProcChance)
            TakeDamage(LightningStorm.ProcDamage, isLightningProc: true);
    }

    private void SpawnDamageNumber(float amount)
    {
        if (damageNumberPrefab == null) return;

        GameObject obj = Instantiate(damageNumberPrefab, transform.position, Quaternion.identity);
        obj.GetComponent<DamageNumber>().Init(amount);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth == null) return;

        playerHealth.TakeDamage(damage);
        Destroy(gameObject);
    }
}

public class DamageNumber : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float lifetime = 0.8f;

    private TMPro.TextMeshPro text;
    private float timer;
    private Color startColor;

    private void Awake()
    {
        text = GetComponent<TMPro.TextMeshPro>();
        startColor = text.color;
    }

    public void Init(float damage)
    {
        text.text = Mathf.RoundToInt(damage).ToString();
    }

    private void Update()
    {
        transform.Translate(Vector3.up * moveSpeed * Time.deltaTime);
        timer += Time.deltaTime;

        Color c = startColor;
        c.a = Mathf.Lerp(1f, 0f, timer / lifetime);
        text.color = c;

        if (timer >= lifetime) Destroy(gameObject);
    }
}
