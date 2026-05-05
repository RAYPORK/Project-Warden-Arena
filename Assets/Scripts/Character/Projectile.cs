using UnityEngine;

/// <summary>
/// 能量彈：直線飛行，命中怪物扣血並依穿透次數決定是否繼續存在，
/// 命中牆面 Layer 或飛行距離超過上限時銷毀。
/// </summary>
public class Projectile : MonoBehaviour
{
    [Header("飛行距離")]
    [Tooltip("自發射點起算的最大飛行距離（公尺），超過即銷毀")]
    [SerializeField]
    private float maxDistance = 60f;

    [Header("穿透")]
    [Tooltip("可額外穿透的怪物次數；0 = 命中第一隻怪即銷毀")]
    [SerializeField]
    private int piercing = 0;

    [Header("視覺（可空）")]
    [SerializeField]
    private TrailRenderer trail;

    [Header("移動")]
    [Tooltip("飛行速度（公尺／秒）；可由 SetMoveSpeed 在發射前覆寫")]
    [SerializeField]
    private float moveSpeed = 30f;

    [Header("牆面")]
    [Tooltip("碰到此 LayerMask 內任一 Layer 的碰撞器時立即銷毀（請含場景牆面）")]
    [SerializeField]
    private LayerMask wallLayers;

    private Vector3 _direction;
    private float _damage;
    private int _piercingRemaining;
    private float _distanceTraveled;
    private bool _launched;
    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.useGravity = false;
            _rb.isKinematic = true;
        }

        if (wallLayers.value == 0)
        {
            int wallLayer = LayerMask.NameToLayer("Wall");
            if (wallLayer >= 0)
                wallLayers = 1 << wallLayer;
        }
    }

    /// <summary>發射前由外部設定飛行速度（例如 <see cref="PlayerAttack"/> 的 projectileSpeed）。</summary>
    public void SetMoveSpeed(float speed) => moveSpeed = Mathf.Max(0f, speed);

    /// <summary>
    /// 啟用飛行：方向會正規化；<paramref name="piercing"/> 為剩餘可穿透怪物次數
    /// （0 表示命中第一隻怪後即銷毀）。
    /// </summary>
    public void Launch(Vector3 direction, float damage, int piercing)
    {
        _direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : transform.forward;
        _damage = Mathf.Max(0f, damage);
        _piercingRemaining = Mathf.Max(0, piercing);
        _distanceTraveled = 0f;
        _launched = true;
        transform.rotation = Quaternion.LookRotation(_direction);
    }

    private void Update()
    {
        if (!_launched)
            return;

        float step = moveSpeed * Time.deltaTime;
        if (_rb != null)
            _rb.MovePosition(_rb.position + _direction * step);
        else
            transform.position += _direction * step;

        _distanceTraveled += step;
        if (_distanceTraveled >= maxDistance)
            DestroyProjectile();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_launched || other == null)
            return;

        if (other.CompareTag("Player"))
            return;

        if (IsInWallLayers(other.gameObject.layer))
        {
            DestroyProjectile();
            return;
        }

        MonsterBase monster = other.GetComponentInParent<MonsterBase>();
        if (monster == null || monster.IsDead)
            return;

        monster.TakeDamage(_damage, _direction);

        if (_piercingRemaining > 0)
        {
            _piercingRemaining--;
            return;
        }

        DestroyProjectile();
    }

    private bool IsInWallLayers(int layer)
    {
        if (wallLayers.value == 0)
            return false;
        return (wallLayers.value & (1 << layer)) != 0;
    }

    private void DestroyProjectile()
    {
        if (trail != null)
            trail.emitting = false;

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxDistance = Mathf.Max(0.01f, maxDistance);
        piercing = Mathf.Max(0, piercing);
        moveSpeed = Mathf.Max(0f, moveSpeed);
    }
#endif
}
