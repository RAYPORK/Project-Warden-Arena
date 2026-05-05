using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 所有怪物的基礎類別：處理生命值、受擊、死亡與能量掉落。
/// </summary>
public class MonsterBase : MonoBehaviour
{
    [Header("生命值")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;

    [Header("能量掉落")]
    [SerializeField] private GameObject energyOrbPrefab;
    [SerializeField] private int energyDropCount = 1;
    [SerializeField] private float energyDropRadius = 1f;

    [Header("擊退")]
    [SerializeField] private bool canBeKnockedBack = true;
    [SerializeField] private float knockbackResistance = 0f;

    [Header("視覺回饋")]
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private float hitFlashDuration = 0.1f;

    [Header("事件")]
    [SerializeField] private UnityEvent onDeath = new UnityEvent();
    [SerializeField] private UnityEvent<float> onDamageTaken = new UnityEvent<float>();

    private Coroutine _hitFlashRoutine;
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    /// <summary>是否已死亡。</summary>
    public bool IsDead => currentHealth <= 0f;
    /// <summary>是否可被擊退（供子類別或攻擊系統查詢）。</summary>
    public bool CanBeKnockedBack => canBeKnockedBack;
    /// <summary>擊退抗性：0 = 完整擊退，1 = 完全免疫。</summary>
    public float KnockbackResistance => knockbackResistance;

    protected virtual void Awake()
    {
        currentHealth = maxHealth;
    }

    /// <summary>
    /// 怪物受傷（含方向）。
    /// </summary>
    public void TakeDamage(float damage, Vector3 hitDirection)
    {
        if (IsDead)
            return;

        if (damage <= 0f)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - damage);
        onDamageTaken?.Invoke(damage);
        OnTakeDamage(damage, hitDirection);

        if (renderers != null && renderers.Length > 0)
        {
            if (_hitFlashRoutine != null)
                StopCoroutine(_hitFlashRoutine);
            _hitFlashRoutine = StartCoroutine(HitFlashCoroutine());
        }

        if (IsDead)
            Die();
    }

    /// <summary>
    /// 怪物受傷（無方向）。
    /// </summary>
    public void TakeDamage(float damage)
    {
        TakeDamage(damage, Vector3.zero);
    }

    /// <summary>
    /// 受擊後鉤點，供子類別覆寫（例如播放特殊音效、套用狀態）。
    /// </summary>
    protected virtual void OnTakeDamage(float damage, Vector3 direction)
    {
    }

    /// <summary>
    /// 死亡邏輯：觸發事件、掉落能量球、銷毀物件。
    /// </summary>
    protected virtual void Die()
    {
        onDeath?.Invoke();
        SpawnEnergyOrbs();
        Destroy(gameObject);
    }

    private void SpawnEnergyOrbs()
    {
        if (energyOrbPrefab == null)
            return;

        int count = Mathf.Max(0, energyDropCount);
        float radius = Mathf.Max(0f, energyDropRadius);

        for (int i = 0; i < count; i++)
        {
            Vector2 circle = Random.insideUnitCircle * radius;
            Vector3 spawnPos = transform.position + new Vector3(circle.x, 0f, circle.y);
            Instantiate(energyOrbPrefab, spawnPos, Quaternion.identity);
        }
    }

    /// <summary>
    /// 受擊閃白：短暫改色後恢復原色。
    /// </summary>
    private IEnumerator HitFlashCoroutine()
    {
        int rendererCount = renderers.Length;
        Material[] mats = new Material[rendererCount];
        Color[] originalColors = new Color[rendererCount];
        bool[] changed = new bool[rendererCount];

        for (int i = 0; i < rendererCount; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            Material mat = r.material;
            if (mat == null || !mat.HasProperty(ColorPropertyId))
                continue;

            mats[i] = mat;
            originalColors[i] = mat.GetColor(ColorPropertyId);
            mat.SetColor(ColorPropertyId, Color.white);
            changed[i] = true;
        }

        yield return new WaitForSeconds(Mathf.Max(0f, hitFlashDuration));

        for (int i = 0; i < rendererCount; i++)
        {
            if (!changed[i] || mats[i] == null)
                continue;
            mats[i].SetColor(ColorPropertyId, originalColors[i]);
        }

        _hitFlashRoutine = null;
    }

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        energyDropCount = Mathf.Max(0, energyDropCount);
        energyDropRadius = Mathf.Max(0f, energyDropRadius);
        knockbackResistance = Mathf.Clamp01(knockbackResistance);
        hitFlashDuration = Mathf.Max(0f, hitFlashDuration);
    }
#endif
}
