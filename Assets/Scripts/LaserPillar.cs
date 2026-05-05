using UnityEngine;

/// <summary>
/// 雷射柱：開局隨機選擇六向之一發射軸（未由外部覆寫時），循環「冷卻 → 預警（細紅線）→ 發射（粗白線）」；
/// 長度預設為 <see cref="maxLaserLength"/>；外部可呼叫 <see cref="SetDirectionAndLength"/> 指定方向與長度。
/// 發射期間以膠囊體積與玩家重疊時觸發 <see cref="WardenDeathManager.BeginDeathSequence"/>（無敵時略過）。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public class LaserPillar : MonoBehaviour
{
    private static readonly Vector3[] Directions =
    {
        Vector3.up,
        Vector3.down,
        Vector3.left,
        Vector3.right,
        Vector3.forward,
        Vector3.back,
    };

    private enum LaserState
    {
        /// <summary>雷射關閉，等待冷卻結束。</summary>
        Cooldown,

        /// <summary>顯示細紅線預警，不造成死亡。</summary>
        Warning,

        /// <summary>粗白線；每幀偵測玩家重疊即觸發死亡。</summary>
        Firing,
    }

    [Header("時序（秒）")]
    [Tooltip("預警階段持續時間（細紅線）")]
    [SerializeField]
    private float warningDuration = 1.5f;

    [Tooltip("發射階段持續時間（粗白線）")]
    [SerializeField]
    private float firingDuration = 0.8f;

    [Tooltip("冷卻階段持續時間（雷射關閉）")]
    [SerializeField]
    private float cooldownDuration = 2f;

    [Header("雷射幾何")]
    [Tooltip("Awake 時預設雷射長度（公尺）；可由 SetDirectionAndLength 覆寫）")]
    [SerializeField]
    private float maxLaserLength = 80f;

    [Tooltip("預警時 LineRenderer 寬度")]
    [SerializeField]
    private float warningWidth = 0.1f;

    [Tooltip("發射時 LineRenderer 寬度（亦用於 OverlapCapsule 半徑推算）")]
    [SerializeField]
    private float firingWidth = 2f;

    [Header("視覺")]
    [Tooltip("預警線顏色（預設半透明紅）")]
    [SerializeField]
    private Color warningColor = new Color(1f, 0f, 0f, 150f / 255f);

    [Tooltip("發射線顏色（預設不透明白）")]
    [SerializeField]
    private Color firingColor = new Color(1f, 1f, 1f, 1f);

    [Tooltip("LineRenderer 繪製排序，數值較大較靠前")]
    [SerializeField]
    private int laserSortingOrder = 100;

    [Header("死亡結算")]
    [Tooltip("未指派時於執行時尋找 WardenDeathManager")]
    [SerializeField]
    private WardenDeathManager deathManager;

    private LineRenderer _lineRenderer;
    private Vector3 _fireDirection;
    /// <summary>實際雷射長度（Awake 預設為 <see cref="maxLaserLength"/>，生成器可透過 <see cref="SetDirectionAndLength"/> 覆寫）。</summary>
    private float _laserLength;

    private LaserState _state = LaserState.Cooldown;
    private float _stateTimeRemaining;

    private void Awake()
    {
        _fireDirection = Directions[Random.Range(0, Directions.Length)].normalized;
        _laserLength = maxLaserLength;

        _lineRenderer = GetComponent<LineRenderer>();
        if (_lineRenderer == null)
            _lineRenderer = gameObject.AddComponent<LineRenderer>();

        _lineRenderer.positionCount = 2;
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.numCornerVertices = 4;
        _lineRenderer.numCapVertices = 2;
        _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lineRenderer.receiveShadows = false;
        _lineRenderer.sortingOrder = laserSortingOrder;

        SetupLineMaterial();

        UpdateLineEndpoints();
        ApplyCooldownVisual();

        // 初始為冷卻，並加入隨機偏移避免多座雷射柱同幀切狀態
        _state = LaserState.Cooldown;
        _stateTimeRemaining = cooldownDuration + Random.Range(0f, cooldownDuration);
    }

    /// <summary>
    /// 由關卡生成器設定發射方向與線段長度（例如貼邊由上往下、長度等於空間高度）。
    /// </summary>
    /// <param name="direction">世界空間發射方向（會正規化）</param>
    /// <param name="length">沿該方向的雷射長度（公尺）</param>
    public void SetDirectionAndLength(Vector3 direction, float length)
    {
        _fireDirection = direction.normalized;
        _laserLength = length;
        UpdateLineEndpoints();
    }

    private void Update()
    {
        UpdateLineEndpoints();

        if (_state == LaserState.Firing)
            TryLaserKill();

        _stateTimeRemaining -= Time.deltaTime;
        if (_stateTimeRemaining > 0f)
            return;

        AdvanceStateMachine();
    }

    /// <summary>建立 URP Unlit 材質供 LineRenderer 使用（失敗時退回 Sprites/Default）。</summary>
    private void SetupLineMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", warningColor);
        if (mat.HasProperty("_Color"))
            mat.color = warningColor;

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        }

        _lineRenderer.material = mat;
    }

    private void UpdateLineEndpoints()
    {
        Vector3 start = transform.position;
        Vector3 end = start + _fireDirection * _laserLength;
        _lineRenderer.SetPosition(0, start);
        _lineRenderer.SetPosition(1, end);
    }

    private void AdvanceStateMachine()
    {
        switch (_state)
        {
            case LaserState.Cooldown:
                _state = LaserState.Warning;
                _stateTimeRemaining = warningDuration;
                ApplyWarningVisual();
                break;

            case LaserState.Warning:
                _state = LaserState.Firing;
                _stateTimeRemaining = firingDuration;
                ApplyFiringVisual();
                break;

            case LaserState.Firing:
                _state = LaserState.Cooldown;
                _stateTimeRemaining = cooldownDuration;
                ApplyCooldownVisual();
                break;
        }
    }

    private void ApplyCooldownVisual()
    {
        _lineRenderer.enabled = false;
    }

    private void ApplyWarningVisual()
    {
        _lineRenderer.enabled = true;
        _lineRenderer.startWidth = warningWidth;
        _lineRenderer.endWidth = warningWidth;
        SetLineColor(warningColor);
    }

    private void ApplyFiringVisual()
    {
        _lineRenderer.enabled = true;
        _lineRenderer.startWidth = firingWidth;
        _lineRenderer.endWidth = firingWidth;
        SetLineColor(firingColor);
    }

    private void SetLineColor(Color c)
    {
        Material mat = _lineRenderer.material;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color"))
            mat.color = c;
        _lineRenderer.startColor = c;
        _lineRenderer.endColor = c;
    }

    /// <summary>發射期間以膠囊體（兩端為雷射起訖）偵測與玩家重疊；無敵時不觸發死亡。</summary>
    private void TryLaserKill()
    {
        EnsureDeathManagerReference();
        if (deathManager == null || deathManager.isDead)
            return;

        Vector3 p0 = transform.position;
        Vector3 p1 = p0 + _fireDirection * _laserLength;
        float radius = Mathf.Max(0.01f, firingWidth * 0.5f);

        Collider[] hits = Physics.OverlapCapsule(
            p0,
            p1,
            radius,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            if (col == null)
                continue;

            if (!IsUnderPlayerHierarchy(col.transform))
                continue;

            WardenHealthManager health = col.GetComponentInParent<WardenHealthManager>();
            if (health != null && health.IsInvincible)
                return;

            deathManager.BeginDeathSequence();
            return;
        }
    }

    private void EnsureDeathManagerReference()
    {
        if (deathManager != null)
            return;
        deathManager = Object.FindFirstObjectByType<WardenDeathManager>();
    }

    private static bool IsUnderPlayerHierarchy(Transform t)
    {
        while (t != null)
        {
            if (t.CompareTag("Player"))
                return true;
            t = t.parent;
        }

        return false;
    }

    private void OnDestroy()
    {
        if (_lineRenderer != null && _lineRenderer.material != null)
            Destroy(_lineRenderer.material);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        warningDuration = Mathf.Max(0.01f, warningDuration);
        firingDuration = Mathf.Max(0.01f, firingDuration);
        cooldownDuration = Mathf.Max(0.01f, cooldownDuration);
        maxLaserLength = Mathf.Max(0.1f, maxLaserLength);
        warningWidth = Mathf.Max(0.001f, warningWidth);
        firingWidth = Mathf.Max(0.01f, firingWidth);
    }
#endif
}
