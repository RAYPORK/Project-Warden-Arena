using UnityEngine;

/// <summary>
/// 砲台：與玩家保持理想距離、持續朝向玩家，並依固定間隔發射飛彈（飛彈可追蹤玩家）。
/// </summary>
[DisallowMultipleComponent]
public class Turret : MonsterBase
{
    private const float UnarmedNextFire = -1f;

    [Header("目標")]
    [Tooltip("玩家 Transform；未指派時會以 Tag「Player」自動尋找")]
    [SerializeField]
    private Transform playerTransform;

    [Header("移動")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float idealDistance = 20f;
    [SerializeField] private float distanceTolerance = 3f;

    [Header("發射")]
    [Tooltip("飛彈 Prefab（須掛 Missile 腳本）")]
    [SerializeField]
    private GameObject missilePrefab;

    [Tooltip("發射間隔（秒）")]
    [Range(1f, 10f)]
    [SerializeField]
    private float fireInterval = 3f;

    [Tooltip("每次進入偵測範圍後，第一發發射前的延遲（秒）")]
    [SerializeField]
    private float firstShotDelay = 1f;

    [Tooltip("發射點；未指派則使用本物件位置")]
    [SerializeField]
    private Transform firePoint;

    /// <summary>
    /// 下一發允許發射的時間（<see cref="Time.time"/>）；為 <see cref="UnarmedNextFire"/> 表示未排程（玩家不在範圍內）。
    /// </summary>
    private float _nextFireTime = UnarmedNextFire;

    protected override void Awake()
    {
        base.Awake();
    }

    private void Start()
    {
        EnsurePlayerReference();
    }

    private void Update()
    {
        if (IsDead)
        {
            _nextFireTime = UnarmedNextFire;
            return;
        }

        EnsurePlayerReference();

        if (playerTransform == null)
        {
            _nextFireTime = UnarmedNextFire;
            return;
        }

        Vector3 toPlayer = playerTransform.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;

        if (toPlayer.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(toPlayer.normalized);

        // 與玩家保持理想距離：太近後退、太遠靠近、誤差範圍內不動。
        float nearThreshold = Mathf.Max(0f, idealDistance - distanceTolerance);
        float farThreshold = idealDistance + Mathf.Max(0f, distanceTolerance);
        if (dist < nearThreshold)
        {
            Vector3 away = -toPlayer.normalized;
            transform.position += away * moveSpeed * Time.deltaTime;
        }
        else if (dist > farThreshold)
        {
            Vector3 toward = toPlayer.normalized;
            transform.position += toward * moveSpeed * Time.deltaTime;
        }

        // 已移除「進入紅圈才發射」：只要有玩家目標就維持發射節奏。
        if (_nextFireTime < 0f)
            _nextFireTime = Time.time + Mathf.Max(0f, firstShotDelay);

        if (Time.time >= _nextFireTime)
        {
            TryFire();
            _nextFireTime = Time.time + Mathf.Max(0.01f, fireInterval);
        }
    }

    /// <summary>若未指派玩家，以 Tag「Player」尋找場景中的玩家根物件。</summary>
    private void EnsurePlayerReference()
    {
        if (playerTransform != null)
            return;

        GameObject go = GameObject.FindGameObjectWithTag("Player");
        if (go != null)
            playerTransform = go.transform;
    }

    private void TryFire()
    {
        if (missilePrefab == null)
            return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        Quaternion spawnRot = transform.rotation;

        GameObject instance = Instantiate(missilePrefab, spawnPos, spawnRot);
        Missile missile = instance.GetComponent<Missile>();
        if (missile == null)
        {
            Destroy(instance);
            return;
        }

        missile.Launch(transform.forward, playerTransform);
    }

    /// <summary>死亡時停止發射流程並交由基底處理掉落與銷毀。</summary>
    protected override void Die()
    {
        _nextFireTime = UnarmedNextFire;
        base.Die();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        fireInterval = Mathf.Clamp(fireInterval, 1f, 10f);
        firstShotDelay = Mathf.Max(0f, firstShotDelay);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        idealDistance = Mathf.Max(0f, idealDistance);
        distanceTolerance = Mathf.Max(0f, distanceTolerance);
    }
#endif
}
