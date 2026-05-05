using UnityEngine;

/// <summary>
/// 開發用飛行模式：F9 切換；維持 Rigidbody 動態模擬（非 Kinematic）、關閉重力，
/// 以 <see cref="Rigidbody.linearVelocity"/> 施加飛行意圖速度，並保留上一幀以來物理／氣流等造成的速度差，
/// 使 <c>AirVent</c> 等於 Trigger 內 <c>AddForce</c> 仍可疊加測試。不修改無敵、圖層碰撞或 Collider 啟用狀態。
/// 掛在 PlayerRig 上；可由 UI 呼叫 <see cref="ToggleFly"/>。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class WardenDevFlyMode : MonoBehaviour
{
    /// <summary>其他腳本（移動／鋼索）據此略過一般地面移動邏輯。</summary>
    public static bool IsFlying { get; private set; }

    /// <summary>
    /// Play 進入或 Domain Reload 後重置靜態狀態，避免殘留 true 造成移動/跳躍被整段略過。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticFlyState()
    {
        IsFlying = false;
    }

    [Header("切換")]
    [Tooltip("按下此鍵切換飛行模式開／關（預設 F9）")]
    [SerializeField]
    private KeyCode toggleFlyKey = KeyCode.F9;

    [Tooltip("飛行時是否解鎖並顯示游標（方便切回編輯器或點 UI）")]
    [SerializeField]
    private bool showCursorWhileFlying = true;

    [Header("飛行速度")]
    [Tooltip("WASD 水平速度（公尺／秒）；方向依主攝影機水平前／右，滑鼠控制視角即改變前進方向）")]
    [SerializeField]
    private float flyHorizontalSpeed = 22f;

    [Tooltip("Space 上升／Shift 下降（公尺／秒）")]
    [SerializeField]
    private float flyVerticalSpeed = 18f;

    [Tooltip("若留空則使用 Camera.main")]
    [SerializeField]
    private Camera flyCamera;

    private Rigidbody _rb;
    private WardenWinchSystem _winch;
    private bool _flyActive;
    private bool _savedUseGravity;

    /// <summary>上一個飛行 FixedUpdate 結束時寫入的 linearVelocity，用於計算外部力造成的增量。</summary>
    private Vector3 _lastVelocityWeSet;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _winch = GetComponent<WardenWinchSystem>();
        if (flyCamera == null)
            flyCamera = Camera.main;

        // 若本元件尚未進入飛行模式，確保全域狀態為 false。
        if (!_flyActive)
            IsFlying = false;
    }

    private void OnDestroy()
    {
        if (_flyActive)
            ExitFly();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleFlyKey))
            ToggleFly();
    }

    private void FixedUpdate()
    {
        if (!_flyActive)
            return;

        ApplyFlyMovement();
    }

    /// <summary>供 UI Button 等呼叫：在飛行／一般模式之間切換。</summary>
    public void ToggleFly()
    {
        if (_flyActive)
            ExitFly();
        else
            EnterFly();
    }

    private void EnterFly()
    {
        _winch?.ForceDisconnectIfConnected();

        _savedUseGravity = _rb.useGravity;
        _rb.useGravity = false;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _lastVelocityWeSet = Vector3.zero;

        _flyActive = true;
        IsFlying = true;

        if (showCursorWhileFlying)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void ExitFly()
    {
        _rb.useGravity = _savedUseGravity;

        _flyActive = false;
        IsFlying = false;
        _lastVelocityWeSet = Vector3.zero;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ApplyFlyMovement()
    {
        Camera c = flyCamera != null ? flyCamera : Camera.main;
        if (c == null)
            return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        float y = 0f;
        if (Input.GetKey(KeyCode.Space))
            y += 1f;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            y -= 1f;

        Vector3 forward = c.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 1e-6f)
            forward.Normalize();
        else
            forward = Vector3.forward;

        Vector3 right = c.transform.right;
        right.y = 0f;
        if (right.sqrMagnitude > 1e-6f)
            right.Normalize();
        else
            right = Vector3.right;

        Vector3 flat = forward * v + right * h;
        if (flat.sqrMagnitude > 1f)
            flat.Normalize();

        // 飛行意圖速度（不使用 AddForce／不直接改 transform.position）
        Vector3 targetFlyVelocity = flat * flyHorizontalSpeed + Vector3.up * (y * flyVerticalSpeed);

        // 保留自上次寫入後，氣流 AddForce、碰撞等對 linearVelocity 造成的變化（例如 AirVent）
        Vector3 externalDelta = _rb.linearVelocity - _lastVelocityWeSet;
        Vector3 merged = targetFlyVelocity + externalDelta;
        _rb.linearVelocity = merged;
        _lastVelocityWeSet = merged;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        flyHorizontalSpeed = Mathf.Max(0f, flyHorizontalSpeed);
        flyVerticalSpeed = Mathf.Max(0f, flyVerticalSpeed);
    }
#endif
}
