using UnityEngine;

/// <summary>
/// 場上能量球：可被 <see cref="EnergyManager"/> 以範圍吸引飛向玩家，
/// 以 Trigger 碰觸玩家時將能量交給 <see cref="EnergyManager"/> 並自毀。
/// Trigger 可掛在子物件；Awake 會為子階層 Trigger 自動掛載轉發元件。
/// </summary>
public class EnergyOrb : MonoBehaviour
{
    [Header("能量")]
    [Tooltip("被收集時提供給 EnergyManager 的能量量")]
    [SerializeField]
    private float energyValue = 10f;

    [Tooltip("存在時間（秒），逾時未被收集則自動銷毀")]
    [SerializeField]
    private float lifeTime = 10f;

    private bool _isBeingAttracted;
    private Vector3 _attractTarget;
    private float _attractSpeed;
    private bool _collected;
    private Rigidbody _rb;

    /// <summary>此顆球提供的能量值。</summary>
    public float EnergyValue => energyValue;

    /// <summary>是否正被 EnergyManager 以範圍吸引。</summary>
    public bool IsBeingAttracted => _isBeingAttracted;

    private void Awake()
    {
        // 3D 物理下 Trigger 回調通常需要「至少一方」有 Rigidbody；若 Prefab 未掛則自動補 Kinematic
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.isKinematic = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // 子物件上的 Trigger 預設不會呼叫本腳本的 OnTriggerEnter，改由轉發元件處理
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider c = cols[i];
            if (c == null || !c.isTrigger || c.gameObject == gameObject)
                continue;

            EnergyOrbTriggerForward forward = c.GetComponent<EnergyOrbTriggerForward>();
            if (forward == null)
                forward = c.gameObject.AddComponent<EnergyOrbTriggerForward>();
            forward.Init(this);
        }
    }

    private void Start()
    {
        Destroy(gameObject, Mathf.Max(0.01f, lifeTime));
    }

    private void Update()
    {
        if (!_isBeingAttracted || _attractSpeed <= 0f)
            return;

        Vector3 to = _attractTarget - transform.position;
        float dist = to.magnitude;
        if (dist < 1e-4f)
            return;

        Vector3 dir = to / dist;
        Vector3 delta = dir * Mathf.Min(_attractSpeed * Time.deltaTime, dist);

        if (_rb != null)
            _rb.MovePosition(_rb.position + delta);
        else
            transform.position += delta;
    }

    /// <summary>由 <see cref="EnergyManager"/> 呼叫，朝目標位置以指定速度飛行。</summary>
    public void AttractToward(Vector3 target, float speed)
    {
        _isBeingAttracted = true;
        _attractTarget = target;
        _attractSpeed = Mathf.Max(0f, speed);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollectFromPlayerCollider(other);
    }

    /// <summary>供子物件 <see cref="EnergyOrbTriggerForward"/> 轉發 Trigger。</summary>
    internal void TryCollectFromPlayerCollider(Collider other)
    {
        if (_collected || other == null)
            return;

        if (!IsPlayerCollider(other))
            return;

        EnergyManager manager = Object.FindFirstObjectByType<EnergyManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogWarning("[EnergyOrb] 場景中找不到 EnergyManager，無法加能量。");
            return;
        }

        CompleteCollection(manager, "Trigger 碰觸玩家");
    }

    /// <summary>由 <see cref="EnergyManager"/> 在極近距離時強制吸收（不依賴 Trigger）。</summary>
    public void AbsorbFromProximity(EnergyManager manager)
    {
        if (_collected || manager == null)
            return;

        CompleteCollection(manager, "距離吸收");
    }

    private void CompleteCollection(EnergyManager manager, string reason)
    {
        if (_collected)
            return;

        _collected = true;
        Debug.Log($"[EnergyOrb] {reason}，收集 {energyValue:F1} 能量並消失");
        manager.AddEnergy(energyValue);
        Destroy(gameObject);
    }

    private static bool IsPlayerCollider(Collider other)
    {
        try
        {
            if (other.CompareTag("Player"))
                return true;
            if (other.transform.root.CompareTag("Player"))
                return true;
        }
        catch (UnityException)
        {
            // 專案未定義 "Player" Tag 時 CompareTag 可能丟例外
        }

        if (other.GetComponentInParent<WardenHealthManager>() != null)
            return true;
        if (other.GetComponentInParent<WardenController>() != null)
            return true;
        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        energyValue = Mathf.Max(0f, energyValue);
        lifeTime = Mathf.Max(0.01f, lifeTime);
    }
#endif
}

/// <summary>掛在能量球子物件 Trigger 上，將碰撞轉給 <see cref="EnergyOrb"/>。</summary>
[DisallowMultipleComponent]
public class EnergyOrbTriggerForward : MonoBehaviour
{
    private EnergyOrb _owner;

    public void Init(EnergyOrb owner) => _owner = owner;

    private void OnTriggerEnter(Collider other)
    {
        if (_owner != null)
            _owner.TryCollectFromPlayerCollider(other);
    }
}
