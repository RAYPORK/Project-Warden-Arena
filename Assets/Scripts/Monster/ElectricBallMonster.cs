using System.Collections;
using UnityEngine;

/// <summary>
/// 電球怪：漂浮追蹤玩家並週期性施放電擊。
/// </summary>
public class ElectricBallMonster : MonsterBase
{
    [Header("移動")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float hoverHeight = 0f;
    [SerializeField] private float stoppingDistance = 3f;
    [SerializeField] private float separationStrength = 1.5f;
    [SerializeField] private float separationPadding = 0.5f;
    [SerializeField] private float expectedArenaHalfExtent = 28f;
    [SerializeField] private float arenaMinY = 2f;
    [SerializeField] private float arenaMaxY = 58f;

    [Header("電擊攻擊")]
    [Tooltip("外層電擊球半徑（公尺）。")]
    [SerializeField] private float attackRange = 5f;
    [Tooltip("內層安全半徑（公尺）；玩家在此半徑內不會被電。")]
    [SerializeField] private float innerSafeRange = 1.5f;
    [Tooltip("每秒電擊傷害（DPS）。")]
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackWindup = 0.5f;

    [Header("視覺")]
    [Tooltip("外層電擊球視覺物件（常駐顯示）。")]
    [SerializeField] private GameObject electricOuterShellVisual;
    [Tooltip("外層電擊球碰撞器（會自動設為 Trigger，確保可穿透）。")]
    [SerializeField] private Collider electricOuterShellCollider;

    [SerializeField] private Transform playerTransform;

    private WardenHealthManager _healthManager;
    private Collider _playerCollider;
    private float _spawnY;

    /// <summary>供生成系統使用：電擊球外圈半徑（避免多顆重疊）。</summary>
    public float OuterShellRadius => Mathf.Max(0.1f, attackRange);

    private void Start()
    {
        _spawnY = transform.position.y;
        ConfigureOuterShellForPassthrough();
        SetOuterShellVisible(true);

        EnsurePlayerReference();
        EnsureHealthManagerReference();
        StartCoroutine(AttackCoroutine());
    }

    private void Update()
    {
        if (IsDead)
            return;
        EnsurePlayerReference();
        if (playerTransform == null)
            return;

        Vector3 targetPos = playerTransform.position;
        if (!Mathf.Approximately(hoverHeight, 0f))
            targetPos.y = _spawnY + hoverHeight;

        Vector3 toTarget = targetPos - transform.position;
        float distance = toTarget.magnitude;
        float desiredStoppingDistance = Mathf.Max(stoppingDistance, innerSafeRange);
        if (distance > desiredStoppingDistance && distance > 0.0001f)
        {
            Vector3 separationDir = ComputeSeparationDirection();
            Vector3 desiredDir = toTarget.normalized;
            if (separationDir.sqrMagnitude > 1e-8f)
                desiredDir = (desiredDir + separationDir * separationStrength).normalized;

            Vector3 step = desiredDir * moveSpeed * Time.deltaTime;
            if (step.magnitude > distance)
                step = toTarget;
            transform.position += step;
        }

        ResolveLocalOverlaps();
        ClampInsideArena();

        Vector3 lookTarget = playerTransform.position;
        lookTarget.y = transform.position.y;
        Vector3 lookDir = lookTarget - transform.position;
        if (lookDir.sqrMagnitude > 1e-8f)
            transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

    }

    private IEnumerator AttackCoroutine()
    {
        float windupRemaining = 0f;
        bool windupStarted = false;

        while (!IsDead)
        {
            EnsurePlayerReference();
            EnsureHealthManagerReference();
            if (playerTransform == null || _healthManager == null)
            {
                windupRemaining = 0f;
                windupStarted = false;
                yield return null;
                continue;
            }

            if (!IsPlayerInAttackRange())
            {
                windupRemaining = 0f;
                windupStarted = false;
                yield return null;
                continue;
            }

            // 進入環帶後先經過預備時間，再開始持續傷害。
            if (!windupStarted)
            {
                windupRemaining = attackWindup;
                windupStarted = true;
            }

            if (windupRemaining > 0f)
            {
                windupRemaining -= Time.deltaTime;
                yield return null;
                continue;
            }

            _healthManager.TakeDamage(attackDamage * Time.deltaTime);
            yield return null;
        }
    }

    protected override void Die()
    {
        StopAllCoroutines();
        base.Die();
    }

    protected override void OnRevived()
    {
        EnsurePlayerReference();
        EnsureHealthManagerReference();
        StopAllCoroutines();
        StartCoroutine(AttackCoroutine());
    }

    private void EnsurePlayerReference()
    {
        if (playerTransform != null)
            return;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            _playerCollider = playerObj.GetComponentInChildren<Collider>();
        }
    }

    private void EnsureHealthManagerReference()
    {
        if (_healthManager != null)
            return;
        _healthManager = UnityEngine.Object.FindFirstObjectByType<WardenHealthManager>();
    }

    /// <summary>以玩家中心點判定是否位於外層球內、內層球外（電擊環帶區）。</summary>
    private bool IsPlayerInAttackRange()
    {
        if (playerTransform == null)
            return false;

        float centerDistance = Vector3.Distance(transform.position, playerTransform.position);
        return centerDistance <= attackRange && centerDistance >= innerSafeRange;
    }

    /// <summary>控制外層電擊球可見性；目前需求為常駐顯示。</summary>
    private void SetOuterShellVisible(bool active)
    {
        if (electricOuterShellVisual != null)
            electricOuterShellVisual.SetActive(active);
    }

    /// <summary>外層電擊球一律可穿透（Trigger），避免阻擋玩家與鋼索。</summary>
    private void ConfigureOuterShellForPassthrough()
    {
        if (electricOuterShellCollider == null && electricOuterShellVisual != null)
            electricOuterShellCollider = electricOuterShellVisual.GetComponent<Collider>();
        if (electricOuterShellCollider != null)
            electricOuterShellCollider.isTrigger = true;
        SetOuterShellVisible(true);
    }

    /// <summary>
    /// 計算同類電擊球的分離方向，避免追蹤玩家時互相疊在一起。
    /// </summary>
    private Vector3 ComputeSeparationDirection()
    {
        ElectricBallMonster[] all = UnityEngine.Object.FindObjectsByType<ElectricBallMonster>(FindObjectsSortMode.None);
        Vector3 separation = Vector3.zero;
        float desiredMin = OuterShellRadius * 2f + Mathf.Max(0f, separationPadding);

        for (int i = 0; i < all.Length; i++)
        {
            ElectricBallMonster other = all[i];
            if (other == null || other == this || other.IsDead)
                continue;

            Vector3 delta = transform.position - other.transform.position;
            float dist = delta.magnitude;
            if (dist < 1e-4f || dist >= desiredMin)
                continue;

            float overlapRatio = (desiredMin - dist) / desiredMin;
            separation += delta.normalized * overlapRatio;
        }

        return separation;
    }

    private void ResolveLocalOverlaps()
    {
        ElectricBallMonster[] all = UnityEngine.Object.FindObjectsByType<ElectricBallMonster>(FindObjectsSortMode.None);
        float desiredMin = OuterShellRadius * 2f + Mathf.Max(0f, separationPadding);
        Vector3 correction = Vector3.zero;

        for (int i = 0; i < all.Length; i++)
        {
            ElectricBallMonster other = all[i];
            if (other == null || other == this || other.IsDead)
                continue;

            Vector3 delta = transform.position - other.transform.position;
            float dist = delta.magnitude;
            if (dist >= desiredMin)
                continue;

            Vector3 dir = dist > 1e-4f ? delta / dist : UnityEngine.Random.onUnitSphere;
            float penetration = desiredMin - Mathf.Max(dist, 1e-4f);
            correction += dir * penetration;
        }

        if (correction.sqrMagnitude > 1e-8f)
            transform.position += correction;
    }

    private void ClampInsideArena()
    {
        Vector3 p = transform.position;
        float clampedX = Mathf.Clamp(p.x, -expectedArenaHalfExtent, expectedArenaHalfExtent);
        float clampedY = Mathf.Clamp(p.y, arenaMinY, arenaMaxY);
        float clampedZ = Mathf.Clamp(p.z, -expectedArenaHalfExtent, expectedArenaHalfExtent);

        if (!Mathf.Approximately(p.x, clampedX) || !Mathf.Approximately(p.y, clampedY) || !Mathf.Approximately(p.z, clampedZ))
            transform.position = new Vector3(clampedX, clampedY, clampedZ);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        moveSpeed = Mathf.Max(0f, moveSpeed);
        stoppingDistance = Mathf.Max(0f, stoppingDistance);
        separationStrength = Mathf.Max(0f, separationStrength);
        separationPadding = Mathf.Max(0f, separationPadding);
        expectedArenaHalfExtent = Mathf.Max(1f, expectedArenaHalfExtent);
        arenaMaxY = Mathf.Max(arenaMinY, arenaMaxY);
        attackRange = Mathf.Max(0f, attackRange);
        innerSafeRange = Mathf.Clamp(innerSafeRange, 0f, attackRange);
        attackDamage = Mathf.Max(0f, attackDamage);
        // 預備時間需求：最長 0.5 秒（可更短）。
        attackWindup = Mathf.Clamp(attackWindup, 0f, 0.5f);
    }
#endif
}
