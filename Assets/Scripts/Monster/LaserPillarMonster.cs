using System.Collections;
using UnityEngine;

/// <summary>
/// 雷射怪：固定位置、緩慢面向玩家，預警後發射一次雷射並在結束後自毀。
/// </summary>
public class LaserPillarMonster : MonsterBase
{
    [Header("旋轉")]
    [SerializeField] private float rotateSpeed = 45f;

    [Header("雷射時序")]
    [SerializeField] private float warningDuration = 2f;
    [SerializeField] private float firingDuration = 1f;
    [SerializeField] private float initialDelay = 0f;

    [Header("雷射幾何")]
    [SerializeField] private float laserLength = 60f;
    [SerializeField] private float warningWidth = 0.05f;
    [SerializeField] private float firingWidth = 2f;

    [Header("視覺")]
    [SerializeField] private Color warningColor = new Color(1f, 0f, 0f, 150f / 255f);
    [SerializeField] private Color firingColor = Color.white;

    [Header("參照")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private WardenDeathManager deathManager;

    private LineRenderer _lineRenderer;
    private Vector3 _fireDirection = Vector3.forward;
    private bool _isFiring;

    protected override void Awake()
    {
        base.Awake();
        BuildLineRenderer();
    }

    private void Start()
    {
        EnsurePlayerReference();
        EnsureDeathManagerReference();
        StartCoroutine(LaserSequence());
    }

    private void Update()
    {
        if (IsDead)
            return;

        if (_lineRenderer != null && _lineRenderer.enabled)
            UpdateLinePositions();
    }

    /// <summary>一次性雷射流程：隨機延遲 → 預警 → 發射 → 自毀。</summary>
    private IEnumerator LaserSequence()
    {
        // 依設定做初始隨機延遲（0 表示不延遲）。
        float startDelay = initialDelay > 0f ? Random.Range(0f, initialDelay) : 0f;
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        if (IsDead)
            yield break;

        // Warning：紅色細線，持續面向玩家。
        SetLaserVisual(active: true, warningColor, warningWidth);
        float warningElapsed = 0f;
        while (warningElapsed < warningDuration)
        {
            if (IsDead)
                yield break;

            RotateTowardPlayer();
            _fireDirection = transform.forward;
            UpdateLinePositions();

            warningElapsed += Time.deltaTime;
            yield return null;
        }

        if (IsDead)
            yield break;

        // Firing：鎖定發射方向，白色粗線，不再旋轉。
        _isFiring = true;
        _fireDirection = transform.forward;
        SetLaserVisual(active: true, firingColor, firingWidth);
        float firingElapsed = 0f;
        while (firingElapsed < firingDuration)
        {
            if (IsDead)
                yield break;

            UpdateLinePositions();
            TryKillPlayerInLaser();

            firingElapsed += Time.deltaTime;
            yield return null;
        }

        _isFiring = false;
        Die();
    }

    /// <summary>僅在預警階段旋轉朝向玩家（360 度，含俯仰）。</summary>
    private void RotateTowardPlayer()
    {
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

    /// <summary>發射期間用 OverlapCapsule 掃描雷射體積，命中玩家即觸發死亡流程。</summary>
    private void TryKillPlayerInLaser()
    {
        if (!_isFiring)
            return;

        EnsurePlayerReference();
        EnsureDeathManagerReference();
        if (playerTransform == null || deathManager == null || deathManager.isDead)
            return;

        float radius = Mathf.Max(0.01f, firingWidth * 0.5f);
        Vector3 start = transform.position;
        Vector3 end = transform.position + _fireDirection * laserLength;
        Collider[] hits = Physics.OverlapCapsule(start, end, radius, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null)
                continue;
            if (c.transform == playerTransform || c.transform.IsChildOf(playerTransform))
            {
                deathManager.BeginDeathSequence();
                return;
            }
        }
    }

    private void BuildLineRenderer()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        if (_lineRenderer == null)
            _lineRenderer = gameObject.AddComponent<LineRenderer>();

        _lineRenderer.positionCount = 2;
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.enabled = false;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Unlit/Color");
        Material mat = new Material(shader);
        _lineRenderer.material = mat;
    }

    private void SetLaserVisual(bool active, Color color, float width)
    {
        if (_lineRenderer == null)
            return;

        _lineRenderer.enabled = active;
        _lineRenderer.startColor = color;
        _lineRenderer.endColor = color;
        _lineRenderer.startWidth = width;
        _lineRenderer.endWidth = width;

        // 某些材質設定會覆蓋 LineRenderer 的頂點色，這裡同步寫入材質顏色確保預警紅線可見。
        Material mat = _lineRenderer.material;
        if (mat != null)
        {
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
        }
    }

    private void UpdateLinePositions()
    {
        if (_lineRenderer == null)
            return;

        Vector3 start = transform.position;
        Vector3 end = transform.position + _fireDirection * laserLength;
        _lineRenderer.SetPosition(0, start);
        _lineRenderer.SetPosition(1, end);
    }

    private void EnsurePlayerReference()
    {
        if (playerTransform != null)
            return;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerTransform = playerObj.transform;
    }

    private void EnsureDeathManagerReference()
    {
        if (deathManager != null)
            return;
        deathManager = Object.FindFirstObjectByType<WardenDeathManager>();
    }

    protected override void Die()
    {
        StopAllCoroutines();
        if (_lineRenderer != null)
            _lineRenderer.enabled = false;
        base.Die();
    }

    protected override void OnRevived()
    {
        EnsurePlayerReference();
        EnsureDeathManagerReference();
        _isFiring = false;
        if (_lineRenderer != null)
            _lineRenderer.enabled = false;
        StopAllCoroutines();
        StartCoroutine(LaserSequence());
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        rotateSpeed = Mathf.Max(0f, rotateSpeed);
        warningDuration = Mathf.Max(0f, warningDuration);
        firingDuration = Mathf.Max(0.01f, firingDuration);
        initialDelay = Mathf.Max(0f, initialDelay);
        laserLength = Mathf.Max(0.01f, laserLength);
        warningWidth = Mathf.Max(0.001f, warningWidth);
        firingWidth = Mathf.Max(warningWidth, firingWidth);
    }
#endif
}
