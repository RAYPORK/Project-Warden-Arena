using UnityEngine;

/// <summary>
/// 第一人稱角色控制器：地面 WASD 移動、跳躍、滑鼠視角。
/// 空中不施加地面移動力，與 <see cref="WardenWinchSystem"/> 的空中 AddForce 分工。
/// 請掛在 PlayerRig（含 Rigidbody）上，並在 Inspector 指派 Main Camera。
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody))]
public class WardenController : MonoBehaviour
{
    private const float AwakeGroundSnapProbeHeight = 2f;
    private const float AwakeGroundSnapProbeDistance = 200f;
    [Header("參照")]
    [Tooltip("第一人稱主攝影機（俯仰角施加於此 Transform）")]
    [SerializeField] private Camera mainCamera;

    [Header("移動")]
    [Tooltip("地面移動時的水平加速度量級（ForceMode.Acceleration）；水泥基準，岩漿／冰再乘以下方係數")]
    [SerializeField] private float moveSpeed = 7f;

    /// <summary>地面移動速度（拉霸等大獎效果可暫時修改）。</summary>
    public float MoveSpeed
    {
        get => moveSpeed;
        set => moveSpeed = value;
    }

    [Tooltip("跳躍瞬間施加於 Y 軸的力道（ForceMode.Impulse）")]
    [SerializeField] private float jumpForce = 12f;

    [Header("材質地面修正（依 PlatformType.type）")]
    [Tooltip("站在岩漿上時，水平移動力乘以本值（小於 1 變慢）。")]
    [SerializeField, Range(0.05f, 1f)] private float lavaGroundMoveSpeedMultiplier = 0.42f;

    [Tooltip("站在冰上時，水平移動力乘以本值（可略大於 1 略增加速；主要滑感來自下方「鬆鍵減速」）。")]
    [SerializeField, Range(0.2f, 1.5f)] private float iceGroundMoveSpeedMultiplier = 1f;

    [Tooltip("水泥上鬆開 WASD 時，每秒水平速度衰減係數（0 = 不額外處理，維持只靠物理摩擦）。")]
    [SerializeField] private float concreteNoInputHorizontalBrakePerSecond = 0f;

    [Tooltip("岩漿上鬆開 WASD 時水平速度每秒衰減（略大於冰可模擬腳下遲滯）。")]
    [SerializeField] private float lavaNoInputHorizontalBrakePerSecond = 3.5f;

    [Tooltip("冰上鬆開 WASD 時水平速度每秒衰減（數值愈小愈滑、愈難煞停）。")]
    [SerializeField] private float iceNoInputHorizontalBrakePerSecond = 0.85f;

    [Header("岩漿血量")]
    [Tooltip("站在岩漿上時扣血所呼叫的血量管理器")]
    [SerializeField] private WardenHealthManager healthManager;

    [Tooltip("站在岩漿上時每秒承受傷害（與 Time.fixedDeltaTime 相乘；Inspector 可調）")]
    [SerializeField] private float lavaDamagePerSecond = 10f;

    [Header("地面偵測")]
    [Tooltip("射線起點：相對於 PlayerRig 的局部座標，應在角色底部附近")]
    [SerializeField] private Vector3 groundRayLocalStart = new Vector3(0f, -0.9f, 0f);

    [Tooltip("向下射線長度（公尺）")]
    [SerializeField] private float groundRayLength = 0.2f;
    [Tooltip("腳底 SphereCast 半徑（比單線 Raycast 更不容易漏判著地）。")]
    [SerializeField] private float groundProbeRadius = 0.25f;

    [Tooltip("腳底射線主要偵測的 Layer（通常含 Ground）")]
    [SerializeField] private LayerMask groundLayer;

    [Tooltip("若熔岩／冰塊平台不在 Ground Layer，請在此額外勾選其所在 Layer，否則會判定懸空而無法移動。")]
    [SerializeField] private LayerMask extraFootContactLayers;

    [Header("跳躍容錯")]
    [Tooltip("按下跳躍後可緩衝的秒數（提早按也能起跳）。")]
    [SerializeField] private float jumpBufferSeconds = 0.12f;
    [Tooltip("離地後仍可起跳的寬限秒數（coyote time）。")]
    [SerializeField] private float coyoteTimeSeconds = 0.1f;

    [Header("視角")]
    [Tooltip("滑鼠靈敏度乘數")]
    [SerializeField] private float mouseSensitivity = 2f;

    /// <summary>滑鼠視角靈敏度（可由暫停選單或 PlayerPrefs 同步）。</summary>
    public float MouseSensitivity
    {
        get => mouseSensitivity;
        set => mouseSensitivity = value;
    }

    [Tooltip("俯仰角下限（抬頭）")]
    [SerializeField] private float pitchMin = -80f;

    [Tooltip("俯仰角上限（低頭）")]
    [SerializeField] private float pitchMax = 80f;

    private Rigidbody _rb;
    private float _pitchAngle;

    /// <summary>主攝影機未套用碎裂抖動前的局部座標（避免每幀累加後無法還原）。</summary>
    private Vector3 _cameraBaseLocalPosition;

    /// <summary>腳底射線使用的完整遮罩（主 Ground + 額外可立足表面）。</summary>
    private int FootSurfaceMask => groundLayer.value | extraFootContactLayers.value;
    private float _jumpBufferTimer;
    private float _coyoteTimer;

    /// <summary>若未在 Inspector 指派血量管理器，於執行時尋找場景中的實例（Unity 6：FindFirstObjectByType）。</summary>
    private void EnsureHealthManagerReference()
    {
        if (healthManager != null)
            return;
        healthManager = UnityEngine.Object.FindFirstObjectByType<WardenHealthManager>();
    }

    /// <summary>初始化 Rigidbody、鎖定游標，並快取攝影機初始俯仰。</summary>
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        // 由程式控制旋轉，避免物理扭力與視角打架。
        _rb.freezeRotation = true;

        TrySnapToGroundOnAwake();

        EnsureHealthManagerReference();

        if (mainCamera != null)
        {
            _cameraBaseLocalPosition = mainCamera.transform.localPosition;
            Vector3 euler = mainCamera.transform.localEulerAngles;
            _pitchAngle = euler.x;
            if (_pitchAngle > 180f)
                _pitchAngle -= 360f;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

    }

    private void TrySnapToGroundOnAwake()
    {
        Vector3 desired = transform.position;
        Vector3 origin = desired + Vector3.up * AwakeGroundSnapProbeHeight;
        if (!Physics.SphereCast(
                origin,
                0.35f,
                Vector3.down,
                out RaycastHit hit,
                AwakeGroundSnapProbeDistance,
                ~0,
                QueryTriggerInteraction.Ignore))
        {
            return;
        }

        if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
            return;

        float snappedY = hit.point.y + 1.0f;
        if (Mathf.Abs(snappedY - desired.y) < 0.01f)
            return;

        transform.position = new Vector3(desired.x, snappedY, desired.z);
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    /// <summary>滑鼠視角在 Update 處理，與幀率無關的體感較穩定。</summary>
    private void Update()
    {
        ApplyMouseLook();
        if (WardenDevFlyMode.IsFlying)
            return;
        if (Input.GetKeyDown(KeyCode.Space))
            _jumpBufferTimer = jumpBufferSeconds;
    }

    /// <summary>地面移動使用固定時間步，與物理一致。</summary>
    private void FixedUpdate()
    {
        if (WardenDevFlyMode.IsFlying)
            return;
        bool grounded = IsGrounded();
        if (grounded)
            _coyoteTimer = coyoteTimeSeconds;
        else
            _coyoteTimer = Mathf.Max(0f, _coyoteTimer - Time.fixedDeltaTime);

        _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - Time.fixedDeltaTime);
        TryConsumeBufferedJump();

        ApplyGroundMovement();
        ApplyGroundHorizontalBrakingWhenNoInput();
    }

    /// <summary>視角更新後將主鏡頭局部位置還原為 Awake 快取基準，避免位移累積漂移。</summary>
    private void LateUpdate()
    {
        if (mainCamera == null)
            return;
        mainCamera.transform.localPosition = _cameraBaseLocalPosition;
    }

    /// <summary>
    /// 水平旋轉施加於 PlayerRig（Y 軸無限制）；
    /// 垂直旋轉施加於 Main Camera 本體（X 軸限制 pitchMin～pitchMax）。
    /// </summary>
    private void ApplyMouseLook()
    {
        if (mainCamera == null)
            return;

        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // 左右：整個 Rig 繞世界 Y 旋轉。
        transform.Rotate(0f, mx, 0f, Space.World);

        // 上下：僅攝影機局部 X，並限制俯仰角。
        _pitchAngle -= my;
        _pitchAngle = Mathf.Clamp(_pitchAngle, pitchMin, pitchMax);
        mainCamera.transform.localRotation = Quaternion.Euler(_pitchAngle, 0f, 0f);
    }

    /// <summary>僅在地面時依 Rig 的水平 forward／right 施加移動力；岩漿／冰依材質調整。</summary>
    private void ApplyGroundMovement()
    {
        if (!TryGetGroundMaterial(out MaterialType surface))
            return;

        // Inspector 未指派時自動尋找（與 WardenEnergyPickup 等一致），避免岩漿不扣血
        EnsureHealthManagerReference();

        // 腳底為岩漿時持續扣血（含站立不動），與每秒傷害及物理固定時間步一致
        if (surface == MaterialType.Lava && healthManager != null)
            healthManager.TakeDamage(lavaDamagePerSecond * Time.fixedDeltaTime);

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        if (Mathf.Approximately(h, 0f) && Mathf.Approximately(v, 0f))
            return;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = transform.right;
        right.y = 0f;
        right.Normalize();

        Vector3 wish = (forward * v + right * h).normalized;
        float mult = moveSpeed;
        if (surface == MaterialType.Lava)
            mult *= lavaGroundMoveSpeedMultiplier;
        else if (surface == MaterialType.Ice)
            mult *= iceGroundMoveSpeedMultiplier;

        _rb.AddForce(wish * mult, ForceMode.Acceleration);
    }

    /// <summary>鬆開 WASD 時依材質對水平速度做指數衰減（冰面低係數＝較滑）。</summary>
    private void ApplyGroundHorizontalBrakingWhenNoInput()
    {
        if (!TryGetGroundMaterial(out MaterialType surface))
            return;

        // 無敵時略過冰面鬆鍵水平減速（與過載無敵等玩法一致）；岩漿扣血仍由 TakeDamage 內部處理。
        if (surface == MaterialType.Ice)
        {
            EnsureHealthManagerReference();
            if (healthManager != null && healthManager.IsInvincible)
                return;
        }

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        if (!Mathf.Approximately(h, 0f) || !Mathf.Approximately(v, 0f))
            return;

        float brakePerSecond = surface switch
        {
            MaterialType.Ice => iceNoInputHorizontalBrakePerSecond,
            MaterialType.Lava => lavaNoInputHorizontalBrakePerSecond,
            _ => concreteNoInputHorizontalBrakePerSecond
        };

        if (brakePerSecond <= 0f)
            return;

        Vector3 vel = _rb.linearVelocity;
        Vector3 horiz = new Vector3(vel.x, 0f, vel.z);
        float magSq = horiz.sqrMagnitude;
        if (magSq < 1e-8f)
            return;

        horiz *= Mathf.Exp(-brakePerSecond * Time.fixedDeltaTime);
        _rb.linearVelocity = new Vector3(horiz.x, vel.y, horiz.z);
    }

    /// <summary>消耗跳躍緩衝與 coyote time，成功時起跳一次。</summary>
    private void TryConsumeBufferedJump()
    {
        if (_jumpBufferTimer <= 0f)
            return;
        if (_coyoteTimer <= 0f)
            return;

        Vector3 v = _rb.linearVelocity;
        v.y = 0f;
        _rb.linearVelocity = v;
        _rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        _jumpBufferTimer = 0f;
        _coyoteTimer = 0f;
    }

    /// <summary>自角色底部向下短射線；遮罩為 Ground + Extra，以便岩漿／冰與水泥同一套著地判定。</summary>
    private bool IsGrounded()
    {
        return ProbeFoot(out _);
    }

    private bool ProbeFoot(out RaycastHit hit)
    {
        Vector3 origin = transform.TransformPoint(groundRayLocalStart);
        bool didHit = Physics.SphereCast(
            origin,
            groundProbeRadius,
            Vector3.down,
            out hit,
            groundRayLength,
            FootSurfaceMask,
            QueryTriggerInteraction.Ignore);
        if (!didHit)
        {
            // Fallback：若地面層配置不一致，改用全層掃描，僅排除自身碰撞器。
            bool fallbackHit = Physics.SphereCast(
                origin,
                groundProbeRadius,
                Vector3.down,
                out RaycastHit fallback,
                groundRayLength,
                ~0,
                QueryTriggerInteraction.Ignore);
            bool fallbackIsSelfChild = fallbackHit && fallback.collider != null && fallback.collider.transform.IsChildOf(transform);
            if (fallbackHit && fallback.collider != null && !fallback.collider.transform.IsChildOf(transform))
            {
                hit = fallback;
                didHit = true;
            }

            if (!didHit)
            {
                float diagnosticLength = groundRayLength + 1.0f;
                bool extendedHit = Physics.SphereCast(
                    origin,
                    groundProbeRadius,
                    Vector3.down,
                    out RaycastHit extended,
                    diagnosticLength,
                    ~0,
                    QueryTriggerInteraction.Ignore);
                bool extendedIsSelfChild = extendedHit && extended.collider != null && extended.collider.transform.IsChildOf(transform);

                if (extendedHit && extended.collider != null && !extendedIsSelfChild)
                {
                    hit = extended;
                    didHit = true;
                }

                if (!extendedHit)
                {
                    Collider[] overlaps = Physics.OverlapSphere(
                        origin,
                        groundProbeRadius + 0.02f,
                        ~0,
                        QueryTriggerInteraction.Ignore);
                    bool overlapGrounded = false;
                    for (int i = 0; i < overlaps.Length; i++)
                    {
                        Collider c = overlaps[i];
                        if (c == null)
                            continue;
                        if (c.transform.IsChildOf(transform))
                            continue;
                        overlapGrounded = true;
                        break;
                    }
                    if (overlapGrounded)
                    {
                        didHit = true;
                    }
                }
            }
        }
        return didHit;
    }

    /// <summary>取得腳下平台材質；無命中或無 PlatformType 時視為水泥。</summary>
    private bool TryGetGroundMaterial(out MaterialType surface)
    {
        if (!ProbeFoot(out RaycastHit hit))
        {
            surface = MaterialType.Concrete;
            return false;
        }

        if (hit.collider == null)
        {
            surface = MaterialType.Concrete;
            return true;
        }

        PlatformType platform = hit.collider.GetComponentInParent<PlatformType>();
        surface = platform != null ? platform.type : MaterialType.Concrete;
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector3 origin = transform.TransformPoint(groundRayLocalStart);
        Gizmos.DrawWireSphere(origin, groundProbeRadius);
        Gizmos.DrawLine(origin, origin + Vector3.down * groundRayLength);
        Gizmos.DrawWireSphere(origin + Vector3.down * groundRayLength, groundProbeRadius);
    }

    private void OnValidate()
    {
        lavaNoInputHorizontalBrakePerSecond = Mathf.Max(0f, lavaNoInputHorizontalBrakePerSecond);
        iceNoInputHorizontalBrakePerSecond = Mathf.Max(0f, iceNoInputHorizontalBrakePerSecond);
        concreteNoInputHorizontalBrakePerSecond = Mathf.Max(0f, concreteNoInputHorizontalBrakePerSecond);
        jumpBufferSeconds = Mathf.Max(0f, jumpBufferSeconds);
        coyoteTimeSeconds = Mathf.Max(0f, coyoteTimeSeconds);
        groundProbeRadius = Mathf.Max(0.01f, groundProbeRadius);
        groundRayLength = Mathf.Max(0.01f, groundRayLength);
    }
#endif
}
