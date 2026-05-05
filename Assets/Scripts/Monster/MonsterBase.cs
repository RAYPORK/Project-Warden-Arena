using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 所有怪物的基礎類別：處理生命值、受擊、死亡與能量掉落。
/// </summary>
public class MonsterBase : MonoBehaviour
{
    private static readonly List<MonsterBase> AllMonsters = new List<MonsterBase>();

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

    [Header("勾索相容")]
    [Tooltip("可選：若怪物帶 Rigidbody，勾索時可被甩動。")]
    [SerializeField] private Rigidbody optionalRigidbody;

    [Header("事件")]
    [SerializeField] private UnityEvent onDeath = new UnityEvent();
    [SerializeField] private UnityEvent<float> onDamageTaken = new UnityEvent<float>();

    [Header("接觸玩家傷害")]
    [Tooltip("怪物碰到玩家時造成的傷害。")]
    [SerializeField] private float contactDamage = 10f;
    [Tooltip("持續接觸時的最短受傷間隔（秒）。")]
    [SerializeField] private float contactDamageInterval = 0.5f;

    private Coroutine _hitFlashRoutine;
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private bool _isDead;
    private float _nextContactDamageTime;

    /// <summary>是否已死亡。</summary>
    public bool IsDead => _isDead;
    /// <summary>是否可被擊退（供子類別或攻擊系統查詢）。</summary>
    public bool CanBeKnockedBack => canBeKnockedBack;
    /// <summary>擊退抗性：0 = 完整擊退，1 = 完全免疫。</summary>
    public float KnockbackResistance => knockbackResistance;

    protected virtual void Awake()
    {
        if (!AllMonsters.Contains(this))
            AllMonsters.Add(this);

        currentHealth = maxHealth;
        _isDead = false;
        EnsureGrappleCompatibility(allowAddComponent: true);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryDealContactDamage(collision.collider);
    }

    private void OnCollisionStay(Collision collision)
    {
        TryDealContactDamage(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryDealContactDamage(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryDealContactDamage(other);
    }

    protected virtual void OnDestroy()
    {
        AllMonsters.Remove(this);
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

        if (currentHealth <= 0f)
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
        _isDead = true;
        currentHealth = 0f;
        onDeath?.Invoke();
        SpawnEnergyOrbs();
        gameObject.SetActive(false);
    }

    /// <summary>由重生系統呼叫：回滿血並重新啟用怪物。</summary>
    public void Revive()
    {
        if (!_isDead)
            return;

        currentHealth = maxHealth;
        _isDead = false;
        gameObject.SetActive(true);
        OnRevived();
    }

    /// <summary>重生後鉤點，供子類別恢復協程/特效/碰撞器狀態。</summary>
    protected virtual void OnRevived()
    {
        _nextContactDamageTime = 0f;
    }

    /// <summary>一鍵重生所有已死亡敵人。</summary>
    public static void ReviveAllDeadMonsters()
    {
        for (int i = 0; i < AllMonsters.Count; i++)
        {
            MonsterBase monster = AllMonsters[i];
            if (monster == null || !monster._isDead)
                continue;
            monster.Revive();
        }
    }

    private void SpawnEnergyOrbs()
    {
        if (energyOrbPrefab == null)
            return;

        int count = Mathf.Max(0, energyDropCount);
        float radius = Mathf.Max(0f, energyDropRadius);

        for (int i = 0; i < count; i++)
        {
            Vector2 circle = UnityEngine.Random.insideUnitCircle * radius;
            Vector3 spawnPos = transform.position + new Vector3(circle.x, 0f, circle.y);
            Instantiate(energyOrbPrefab, spawnPos, Quaternion.identity);
        }
    }

    /// <summary>
    /// 與玩家接觸時扣血（含碰撞與 Trigger）。
    /// 透過間隔避免單幀內重複扣血過多。
    /// </summary>
    private void TryDealContactDamage(Collider other)
    {
        if (IsDead || other == null)
            return;
        if (contactDamage <= 0f)
            return;
        if (Time.time < _nextContactDamageTime)
            return;

        WardenHealthManager health = other.GetComponentInParent<WardenHealthManager>();
        if (health == null)
            return;

        health.TakeDamage(contactDamage);
        _nextContactDamageTime = Time.time + Mathf.Max(0.01f, contactDamageInterval);
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

    /// <summary>確保怪物可被鋼索系統視為可勾目標（自動補 PlatformType=Concrete）。</summary>
    private void EnsureGrappleCompatibility(bool allowAddComponent)
    {
        optionalRigidbody = GetComponent<Rigidbody>();

        PlatformType platform = GetComponent<PlatformType>();
        if (platform == null && allowAddComponent)
            platform = gameObject.AddComponent<PlatformType>();
        if (platform == null)
            return;
        platform.type = MaterialType.Concrete;
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
        contactDamage = Mathf.Max(0f, contactDamage);
        contactDamageInterval = Mathf.Max(0.01f, contactDamageInterval);
        optionalRigidbody = GetComponent<Rigidbody>();
    }
#endif
}
