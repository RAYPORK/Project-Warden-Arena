using UnityEngine;

/// <summary>
/// 氣流怪：固定位置、緩慢朝向玩家，持續往前噴氣流推開玩家。
/// </summary>
public class AirVentMonster : MonsterBase
{
    [Header("旋轉")]
    [SerializeField] private float rotateSpeed = 60f;

    [Header("氣流")]
    [SerializeField] private float pushForce = 80f;
    [SerializeField] private float triggerRadius = 8f;
    [SerializeField] private float airflowOriginForwardOffset = 1f;
    [Tooltip("噴射距離倍率滑桿（1 = 原距離，2 = 兩倍距離）。")]
    [SerializeField] private float airflowRangeMultiplier = 1f;
    [Tooltip("氣流有效半角（度）。玩家需位於前方扇形內才會被吹。")]
    [SerializeField] private float pushConeHalfAngle = 40f;

    [Header("參照")]
    [SerializeField] private Transform playerTransform;

    private SphereCollider _ventTrigger;
    private Rigidbody _playerRigidbody;

    private void Start()
    {
        EnsurePlayerReference();
        BuildVentTrigger();
    }

    private void Update()
    {
        if (IsDead)
            return;

        EnsurePlayerReference();
        if (playerTransform == null)
            return;

        Vector3 toPlayer = playerTransform.position - transform.position;
        if (toPlayer.sqrMagnitude <= 1e-8f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            rotateSpeed * Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (IsDead)
            return;

        EnsurePlayerReference();
        if (playerTransform == null || _playerRigidbody == null)
            return;

        Vector3 airflowOrigin = transform.position + transform.forward * airflowOriginForwardOffset;
        Vector3 toPlayer = _playerRigidbody.worldCenterOfMass - airflowOrigin;
        float dist = toPlayer.magnitude;
        Vector3 toPlayerHorizontal = Vector3.ProjectOnPlane(toPlayer, Vector3.up);
        float horizontalDist = toPlayerHorizontal.magnitude;
        float effectiveRange = (triggerRadius + airflowOriginForwardOffset) * airflowRangeMultiplier;
        if (dist <= 1e-5f || horizontalDist > effectiveRange)
            return;

        Vector3 dirToPlayer = toPlayer / dist;

        // 只在噴口前方錐形範圍內施力：側邊或背後不會被吹。
        float minDot = Mathf.Cos(pushConeHalfAngle * Mathf.Deg2Rad);
        float dot = Vector3.Dot(transform.forward, dirToPlayer);
        if (dot < minDot)
            return;

        // 近強遠弱：讓起始點前移（例如 2m）會更有體感差異。
        float distanceFactor = 1f - Mathf.Clamp01(horizontalDist / Mathf.Max(0.01f, effectiveRange));
        float coneFactor = Mathf.InverseLerp(minDot, 1f, dot);
        // 保留最小推力，避免在邊界因衰減過大看起來像完全沒風。
        float finalForce = pushForce * Mathf.Clamp01(Mathf.Max(0.25f, distanceFactor * coneFactor));
        if (finalForce <= 0f)
            return;

        _playerRigidbody.AddForce(transform.forward * finalForce, ForceMode.Acceleration);
    }

    protected override void Die()
    {
        if (_ventTrigger != null)
            _ventTrigger.enabled = false;
        base.Die();
    }

    /// <summary>未指派玩家時，以 Tag「Player」自動尋找。</summary>
    private void EnsurePlayerReference()
    {
        if (playerTransform != null)
            return;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            _playerRigidbody = playerObj.GetComponent<Rigidbody>();
            if (_playerRigidbody == null)
                _playerRigidbody = playerObj.GetComponentInChildren<Rigidbody>();
        }
    }

    /// <summary>動態建立氣流觸發器（球形 Trigger）。</summary>
    private void BuildVentTrigger()
    {
        _ventTrigger = GetComponent<SphereCollider>();
        if (_ventTrigger == null)
            _ventTrigger = gameObject.AddComponent<SphereCollider>();

        _ventTrigger.isTrigger = true;
        _ventTrigger.radius = triggerRadius;
        _ventTrigger.center = Vector3.zero;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        rotateSpeed = Mathf.Max(0f, rotateSpeed);
        pushForce = Mathf.Max(0f, pushForce);
        triggerRadius = Mathf.Max(0.01f, triggerRadius);
        airflowOriginForwardOffset = Mathf.Max(0f, airflowOriginForwardOffset);
        airflowRangeMultiplier = Mathf.Max(0.1f, airflowRangeMultiplier);
        pushConeHalfAngle = Mathf.Clamp(pushConeHalfAngle, 1f, 89f);

        if (_ventTrigger != null)
        {
            _ventTrigger.isTrigger = true;
            _ventTrigger.radius = triggerRadius;
            _ventTrigger.center = Vector3.zero;
        }
    }
#endif
}
