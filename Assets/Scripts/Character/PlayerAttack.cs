using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家滑鼠左鍵手動攻擊：依 <see cref="isMeleeCharacter"/> 切換近戰範圍傷害或遠距能量彈；
/// 傷害、爆擊、攻速、吸血皆來自 <see cref="PlayerStats"/>（鋼索改為右鍵，與 <see cref="WardenWinchSystem"/> 分工）。
/// </summary>
public class PlayerAttack : MonoBehaviour
{
    [Header("參照")]
    [Tooltip("未指派時會於自身、雙親或場景尋找")]
    [SerializeField]
    private PlayerStats playerStats;

    [Tooltip("未指派時使用 Camera.main")]
    [SerializeField]
    private Camera playerCamera;

    [Tooltip("攻擊發射點（遠距彈體起點、近戰範圍球心）；未指派時使用本物件之世界座標位置")]
    [SerializeField]
    private Transform attackOrigin;

    [Header("流派設定")]
    [Tooltip("true = 近戰；false = 遠距")]
    [SerializeField]
    private bool isMeleeCharacter;

    [Header("遠距設定")]
    [SerializeField]
    private GameObject projectilePrefab;

    [SerializeField]
    private float projectileSpeed = 30f;

    [Tooltip("基礎冷卻（秒）；實際冷卻 = 本值 ÷ PlayerStats.AttackSpeed（預設略長利於感受冷卻）")]
    [SerializeField]
    private float attackCooldown = 1f;

    [Header("近戰設定")]
    [SerializeField]
    private float meleeRange = 5f;

    [SerializeField]
    private float meleeKnockback = 10f;

    [Tooltip("基礎冷卻（秒）；實際冷卻 = 本值 ÷ PlayerStats.AttackSpeed")]
    [SerializeField]
    private float meleeCooldown = 0.3f;

    [Header("近戰視覺")]
    [Tooltip("近戰成功命中至少一隻怪時，於攻擊原點生成的球形閃光／特效（可空）")]
    [SerializeField]
    private GameObject meleeEffectPrefab;

    [Tooltip("近戰特效存在時間（秒），到期後自動銷毀")]
    [SerializeField]
    private float meleeEffectDuration = 0.2f;

    [Header("視覺回饋")]
    [Tooltip("剩餘攻擊冷卻（秒），由執行期更新，僅供除錯／UI 參考")]
    [SerializeField]
    private float attackCooldownRemaining;

    [Tooltip("近戰 OverlapSphere 用；未指定時預設為全部圖層並以 MonsterBase 篩選")]
    [SerializeField]
    private LayerMask meleeHitLayers = ~0;

    private WardenHealthManager _healthManager;
    private bool _healthSearchExhausted;

    private void Awake()
    {
        ResolvePlayerStats();
        ResolvePlayerCamera();
        ResolveHealthManager();
    }

    private void Start()
    {
        ResolvePlayerStats();
        ResolvePlayerCamera();
        ResolveHealthManager();
    }

    private void Update()
    {
        ResolvePlayerStats();
        ResolvePlayerCamera();
        ResolveHealthManager();

        if (attackCooldownRemaining > 0f)
            attackCooldownRemaining -= Time.deltaTime;
        if (attackCooldownRemaining < 0f)
            attackCooldownRemaining = 0f;

        if (!Input.GetMouseButtonDown(0))
            return;
        if (attackCooldownRemaining > 0f)
            return;
        if (playerStats == null)
            return;

        if (isMeleeCharacter)
            PerformMeleeAttack();
        else
            PerformRangedAttack();
    }

    /// <summary>遠距：自發射點朝準心發射能量彈，傷害為一般 + 遠距段並套用爆擊。</summary>
    private void PerformRangedAttack()
    {
        if (projectilePrefab == null)
            return;

        Vector3 origin = GetAttackOriginPosition();
        Vector3 direction = GetAimDirection(origin);
        float damage = playerStats.Damage + playerStats.RangedDamage;
        damage = playerStats.ApplyCrit(damage);

        Quaternion rot = direction.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(direction)
            : transform.rotation;
        GameObject instance = Instantiate(projectilePrefab, origin, rot);

        Projectile projectile = instance.GetComponent<Projectile>();
        if (projectile == null)
        {
            Destroy(instance);
            return;
        }

        projectile.SetMoveSpeed(projectileSpeed);
        projectile.Launch(direction, damage, playerStats.Piercing);

        attackCooldownRemaining = attackCooldown / playerStats.AttackSpeed;
    }

    /// <summary>近戰：範圍內怪物各受一次傷（含爆擊），並吸血與擊退。</summary>
    private void PerformMeleeAttack()
    {
        Vector3 origin = GetAttackOriginPosition();
        float raw = playerStats.Damage + playerStats.MeleeDamage;
        float damage = playerStats.ApplyCrit(raw);

        Collider[] hits = Physics.OverlapSphere(origin, meleeRange, meleeHitLayers, QueryTriggerInteraction.Collide);
        float totalDamage = 0f;
        var struck = new HashSet<MonsterBase>();

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            if (col == null)
                continue;

            MonsterBase monster = col.GetComponentInParent<MonsterBase>();
            if (monster == null || monster.IsDead)
                continue;
            if (!struck.Add(monster))
                continue;

            Vector3 hitDir = (monster.transform.position - origin);
            if (hitDir.sqrMagnitude < 1e-6f)
                hitDir = transform.forward;
            else
                hitDir.Normalize();

            monster.TakeDamage(damage, hitDir);
            totalDamage += damage;
            ApplyMeleeKnockback(monster, origin);
        }

        // 至少命中一隻怪時，於攻擊原點生成短暫球形閃光（Prefab 可為粒子或帶縮放動畫之球體）
        if (struck.Count > 0 && meleeEffectPrefab != null)
        {
            GameObject effect = Instantiate(meleeEffectPrefab, origin, Quaternion.identity);
            Destroy(effect, Mathf.Max(0.01f, meleeEffectDuration));
        }

        if (_healthManager != null && totalDamage > 0f)
            _healthManager.Heal(playerStats.CalculateLifesteal(totalDamage));

        attackCooldownRemaining = meleeCooldown / playerStats.AttackSpeed;
    }

    private void ApplyMeleeKnockback(MonsterBase monster, Vector3 origin)
    {
        if (monster == null || !monster.CanBeKnockedBack || meleeKnockback <= 0f)
            return;

        Rigidbody rb = monster.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic)
            return;

        Vector3 away = monster.transform.position - origin;
        if (away.sqrMagnitude < 1e-6f)
            away = transform.forward;
        else
            away.Normalize();

        float resisted = Mathf.Clamp01(monster.KnockbackResistance);
        float force = meleeKnockback * (1f - resisted);
        rb.AddForce(away * force, ForceMode.Impulse);
    }

    private Vector3 GetAttackOriginPosition() =>
        attackOrigin != null ? attackOrigin.position : transform.position;

    private Vector3 GetAimDirection(Vector3 fromPosition)
    {
        if (playerCamera != null)
            return playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)).direction;

        return transform.forward;
    }

    private void ResolvePlayerStats()
    {
        if (playerStats != null)
            return;

        playerStats = GetComponent<PlayerStats>()
            ?? GetComponentInParent<PlayerStats>()
            ?? GetComponentInChildren<PlayerStats>(true);

        if (playerStats == null)
            playerStats = Object.FindFirstObjectByType<PlayerStats>();
    }

    private void ResolvePlayerCamera()
    {
        if (playerCamera != null)
            return;

        playerCamera = GetComponentInChildren<Camera>(true);
        if (playerCamera == null)
            playerCamera = Camera.main;
    }

    private void ResolveHealthManager()
    {
        if (_healthManager != null || _healthSearchExhausted)
            return;

        _healthManager = GetComponentInParent<WardenHealthManager>()
            ?? GetComponentInChildren<WardenHealthManager>(true);

        if (_healthManager == null)
            _healthManager = Object.FindFirstObjectByType<WardenHealthManager>();

        if (_healthManager == null)
            _healthSearchExhausted = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        projectileSpeed = Mathf.Max(0f, projectileSpeed);
        attackCooldown = Mathf.Max(0.01f, attackCooldown);
        meleeRange = Mathf.Max(0.01f, meleeRange);
        meleeKnockback = Mathf.Max(0f, meleeKnockback);
        meleeCooldown = Mathf.Max(0.01f, meleeCooldown);
        meleeEffectDuration = Mathf.Max(0.01f, meleeEffectDuration);
        attackCooldownRemaining = Mathf.Max(0f, attackCooldownRemaining);
    }
#endif
}
