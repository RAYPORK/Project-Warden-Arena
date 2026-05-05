using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 第一人稱鋼索／捲揚：由主攝影機發射 Raycast，擊中具 <see cref="PlatformType"/> 的水泥／岩漿／冰表面後以 SpringJoint 連線並收線，錨點在命中點附近（沿法線略外推）；
/// 連線在岩漿上可透過事件扣血、在冰上可減水平速（皆可於 Inspector 調整）。連線期間不套用空中 WASD 加力，僅懸掛與收線。
/// 所有 <see cref="Physics.Raycast"/> 皆帶 <see cref="QueryTriggerInteraction.Ignore"/>，避免僅作觸發用的 Collider（例如風扇、能量方塊）擋住鋼索或著地射線。
/// </summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Rigidbody))]
public class WardenWinchSystem : MonoBehaviour
{
    [Header("參照")]
    [Tooltip("捲揚出口：LineRenderer 起點與 Joint 在本體上的錨點參考")]
    [SerializeField] private Transform winchExitPoint;

    [Tooltip("繪製鋼索視覺線段（啟用／停用由本腳本控制）")]
    [SerializeField] private LineRenderer lineRenderer;

    [Tooltip("若留空則使用 Camera.main")]
    [SerializeField] private Camera playerCamera;

    [Header("碰撞／判定")]
    [Tooltip("僅與 Ground、Wall 等可勾層碰撞")]
    [SerializeField] private LayerMask grappleLayers;
    [Tooltip("可勾住且連線期間允許玩家身體穿越的層（例如 GrappleOnly）。")]
    [SerializeField] private LayerMask grapplePassthroughLayers;

    [Tooltip("用於偵測是否在空中（空氣中才套用 WASD 視角加速）")]
    [SerializeField] private LayerMask groundCheckLayers;

    [Tooltip("自腳底向下的射線長度，命中地面視為著地")]
    [SerializeField] private float groundCheckDistance = 1.1f;

    [Header("鋼索長度")]
    [SerializeField] private float minAttachDistanceFromPlayer = 3f;

    [SerializeField] private float maxRopeLength = 20f;

    /// <summary>繩索最大長度（拉霸等大獎效果可暫時修改）。</summary>
    public float MaxRopeLength
    {
        get => maxRopeLength;
        set => maxRopeLength = value;
    }

    [SerializeField] private float minRopeLength = 2f;

    [Tooltip("自動收線速率（公尺／秒）")]
    [SerializeField] private float ropeShortenMetersPerSecond = 12f;

    [Header("錨點")]
    [Tooltip("錨點沿命中法線偏移距離（公尺）；設為 0 時錨點貼在命中點。")]
    [SerializeField] private float anchorOffsetAlongHitNormal = 0f;

    [Tooltip("連線期間是否忽略與被勾平台碰撞；封閉競技場建議關閉，避免被拉進牆後直接穿出場外。")]
    [SerializeField] private bool ignoreCollisionWithGrappledCollider = false;

    [Header("Spring Joint")]
    [SerializeField] private float jointSpring = 150f;

    [SerializeField] private float jointDamper = 18f;

    [Header("收繩推進（Apex 風格）")]
    [Tooltip("按住收繩時，額外沿繩方向加速推進（m/s^2）。0 表示只靠縮短繩長。")]
    [SerializeField] private float reelInAcceleration = 45f;

    [Tooltip("按住收繩時，沿繩方向最大速度（m/s）。達上限後停止額外推進，避免失控。0 表示不限制。")]
    [SerializeField] private float reelInMaxSpeed = 28f;

    [Header("空中操控")]
    [Tooltip("未連鋼索且判定為空中時，對 Rigidbody 施加的 WASD 力道。")]
    [SerializeField] private float airControlForce = 15f;
    [Tooltip("連線勾索期間的 WASD 力道滑桿；設為 0 代表連線時不給空中操控。")]
    [SerializeField] private float grappleAirControlForce = 8f;

    [Header("速度上限（可選，0 = 不限制）")]
    [Tooltip("大於 0 且判定為空中時，每個物理幀將「水平」線速度壓在此值（m/s），減輕擺盪／鋼索累積過大側向動能。")]
    [SerializeField] private float maxAirHorizontalSpeed = 0f;

    [Tooltip("大於 0 時，鬆開鋼索當下若合速度超過此值則等比例縮放，避免一放線就飛出地圖（仍保留方向與部分動量）。")]
    [SerializeField] private float maxSpeedOnGrappleRelease = 0f;

    [Header("鬆線手感")]
    [Tooltip(
        "鬆開左鍵時是否抑制沿繩分量（WinchExitPoint→Anchor）。" +
        "若你想保留 Apex 風格飛出慣性可關閉，或搭配下方比例僅做輕微抑制。")]
    [SerializeField] private bool retainTangentialVelocityOnRelease = false;

    [Tooltip("鬆線時保留多少沿繩速度：1 = 完整保留慣性、0 = 完全移除沿繩分量。")]
    [Range(0f, 1f)]
    [SerializeField] private float radialVelocityRetentionOnRelease = 1f;

    [Header("收盡繩後漸停")]
    [Tooltip("繩長已收到最短且未操作空中 WASD 時，對線速度做指數衰減，讓殘餘擺動慢慢停下。")]
    [SerializeField] private bool slowdownWhenRopeFullyRetracted = true;

    [Tooltip("視為「已收到底」：與最短繩長的差距在此值以內即套用漸停（公尺）。")]
    [SerializeField] private float fullRetractRopeLengthTolerance = 0.05f;

    [Tooltip("每秒衰減係數（越大停得越快）。約 1～3 為緩慢停下。")]
    [SerializeField] private float fullRetractVelocityDecayPerSecond = 1.5f;

    [Tooltip("線速度小於此值（m/s）時直接歸零，避免無限微幅殘留。")]
    [SerializeField] private float fullRetractSnapToRestSpeed = 0.08f;

    [Header("FOV")]
    [SerializeField] private float fovIdle = 85f;

    [SerializeField] private float fovSwing = 90f;

    [Tooltip("速度低於此值時 FOV 維持 fovIdle")]
    [SerializeField] private float fovSpeedThreshold = 5f;

    [Tooltip("速度達此值時 FOV 目標為 fovSwing（其間線性插值）")]
    [SerializeField] private float fovSpeedUpper = 15f;

    [Tooltip("FOV 朝目標逼近的平滑係數（每秒）")]
    [SerializeField] private float fovLerpSpeed = 8f;

    [Header("鋼索事件")]
    [Tooltip("鋼索成功發射並建立連線後觸發（可綁定音效／特效等）")]
    [SerializeField] private UnityEvent onGrappleLaunched = new UnityEvent();

    [Header("物理攻擊")]
    [SerializeField] private float attackDamageMultiplier = 1f;
    [SerializeField] private float minAttackDamage = 10f;
    [SerializeField] private float bounceForce = 5f;
    [Tooltip("高速撞擊怪物後，該怪物在此秒數內不可再次被勾中。")]
    [SerializeField] private float monsterRegrappleCooldownSeconds = 0.2f;
    [Tooltip("勾住怪物時的持續撞擊傷害最短間隔（秒），避免單幀重複觸發。")]
    [SerializeField] private float connectedMonsterRamDamageInterval = 0.12f;

    [Header("繩索材質效果（連線中）")]
    [Tooltip("鋼索錨在岩漿表面時，每秒基準傷害（會乘上 FixedUpdate 步長後傳給下方事件）。")]
    [SerializeField] private float lavaGrappleDamagePerSecond = 8f;

    [Tooltip("鋼索連在岩漿上時每物理幀呼叫；參數為本幀傷害量，可綁定血量／UI。")]
    [SerializeField] private UnityEvent<float> onLavaGrappleDamage = new UnityEvent<float>();

    [Tooltip("鋼索錨在冰表面時，水平線速度每秒額外衰減係數（愈大愈黏、愈難甩盪）。")]
    [SerializeField] private float iceGrappleHorizontalSlowdownPerSecond = 4f;

    [Header("血量（無敵判定）")]
    [Tooltip("冰面鋼索水平減速是否略過；未指派時於執行時尋找 WardenHealthManager")]
    [SerializeField]
    private WardenHealthManager healthManager;

    private Rigidbody _rb;
    private SpringJoint _joint;
    private GameObject _anchorObject;
    private float _currentRopeLength;
    private bool _connected;
    private bool _skipSurfacePenetrationResolve;
    private MonsterBase _connectedMonster;
    private Collider _connectedMonsterCollider;
    private float _nextConnectedMonsterRamDamageTime;
    private MonsterBase _recentlyImpactDamagedMonster;
    private float _recentMonsterNoGrappleUntilTime;
    private Collider[] _playerColliders;
    private Collider[] _ignoredSurfaceColliders;
    private Collider[] _grappledSurfaceColliders;
    private MaterialType _grappleSurfaceMaterial;

    /// <summary>若未在 Inspector 指派血量管理器，於執行時尋找（與 WardenController 一致）。</summary>
    private void EnsureHealthManagerReference()
    {
        if (healthManager != null)
            return;
        healthManager = UnityEngine.Object.FindFirstObjectByType<WardenHealthManager>();
    }

    /// <summary>快取 Rigidbody／主攝影機，並先關閉繩索視覺。</summary>
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (lineRenderer != null)
            lineRenderer.enabled = false;

        _playerColliders = GetComponentsInChildren<Collider>(false);
    }

    private void OnDisable()
    {
        EndIgnoreGrappleCollider();
    }

    private void OnDestroy()
    {
        EndIgnoreGrappleCollider();
    }

    /// <summary>處理舊版輸入與 FOV（非物理）。</summary>
    private void Update()
    {
        if (WardenDevFlyMode.IsFlying)
            return;
        HandleGrappleInput();
        UpdateCameraFov();
    }

    /// <summary>物理步：收線、同步 Joint、空中施力。</summary>
    private void FixedUpdate()
    {
        if (WardenDevFlyMode.IsFlying)
            return;
        // 按住左鍵期間持續收線（不需額外輸入）。
        if (_connected && Input.GetMouseButton(0))
            ApplyRopeShortening();

        if (_connected && _joint != null)
            SyncJointToRopeLength();

        if (_connected && Input.GetMouseButton(0))
            ApplyReelInAcceleration();

        if (_connected && !_skipSurfacePenetrationResolve)
            ResolveGrappledSurfacePenetration();

        TryApplyConnectedMonsterRamDamage();

        ApplyAirMovement();
        ClampAirHorizontalSpeedIfNeeded();
        ApplyFullRetractSlowdown();
        ApplyGrappleSurfaceEffects();
    }

    /// <summary>攝影機動完後再畫線，避免繩索跟不上視角。</summary>
    private void LateUpdate()
    {
        if (WardenDevFlyMode.IsFlying)
            return;
        UpdateLineRenderer();
    }

    /// <summary>供 <see cref="WardenDevFlyMode"/> 進入飛行時強制斷鋼索並還原碰撞。</summary>
    public void ForceDisconnectIfConnected()
    {
        if (_connected)
            DisconnectGrapple();
    }

    /// <summary>滑鼠左鍵：按下發射、按住維持連線並收線、放開切斷並保留動量。</summary>
    private void HandleGrappleInput()
    {
        if (Input.GetMouseButtonDown(0) && !_connected)
            TryFireGrapple();

        if (Input.GetMouseButtonUp(0) && _connected)
            DisconnectGrapple();
    }

    /// <summary>左鍵按下時嘗試發射：命中具 PlatformType 的物件（含怪物）即可建立錨點與 Joint。</summary>
    private void TryFireGrapple()
    {
        if (playerCamera == null || winchExitPoint == null)
            return;

        // 自視野中心發射，最大距離即初始繩長上限。Ignore Trigger：不命中 Is Trigger 碰撞器，避免風扇等障礙誤擋鋼索。
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        // 使用 RaycastAll 取得最近命中；GrappleOnly 這類目標可被勾住，穿透效果於連線後處理。
        RaycastHit[] allHits = Physics.RaycastAll(ray, maxRopeLength, grappleLayers, QueryTriggerInteraction.Ignore);
        RaycastHit hit = default;
        float nearest = float.MaxValue;
        bool foundValidHit = false;
        for (int i = 0; i < allHits.Length; i++)
        {
            RaycastHit candidate = allHits[i];
            if (candidate.distance < nearest)
            {
                hit = candidate;
                nearest = candidate.distance;
                foundValidHit = true;
            }
        }

        if (!foundValidHit)
            return;

        MonsterBase hitMonster = hit.collider.GetComponentInParent<MonsterBase>();
        if (hitMonster != null &&
            hitMonster == _recentlyImpactDamagedMonster &&
            Time.time < _recentMonsterNoGrappleUntilTime)
        {
            return;
        }

        // 須有 PlatformType；水泥／岩漿／冰皆可勾（其餘材質不建立連線）。
        PlatformType platform = hit.collider.GetComponentInParent<PlatformType>();
        if (platform == null || !IsGrappleAllowedOn(platform.type))
            return;

        Vector3 playerPos = _rb.position;
        if (Vector3.Distance(playerPos, hit.point) < minAttachDistanceFromPlayer)
            return;

        Vector3 anchorWorld = ComputeAnchorWorldPosition(hit);
        if (Vector3.Distance(playerPos, anchorWorld) < minAttachDistanceFromPlayer)
            return;

        bool allowBodyPassthrough = IsLayerInMask(hit.collider.gameObject.layer, grapplePassthroughLayers);
        _skipSurfacePenetrationResolve = allowBodyPassthrough;
        if (allowBodyPassthrough)
            _grappledSurfaceColliders = null;
        else
            CacheGrappledSurfaceColliders(hit.collider);
        BeginIgnoreGrappleCollider(hit.collider, allowBodyPassthrough);

        float ropeLen = Vector3.Distance(winchExitPoint.position, anchorWorld);
        ropeLen = Mathf.Clamp(ropeLen, minRopeLength, maxRopeLength);

        CreateAnchor(anchorWorld, hit.collider.transform);

        AttachSpringJoint(ropeLen, hit);
        _currentRopeLength = ropeLen;
        _connected = true;
        _grappleSurfaceMaterial = platform.type;
        _connectedMonster = hitMonster;
        _connectedMonsterCollider = hit.collider;

        if (lineRenderer != null)
            lineRenderer.enabled = true;

        // 連線已建立：通知監聽者（例如 WardenAudioManager.PlayGrappleLaunch）。
        onGrappleLaunched?.Invoke();
    }

    private static bool IsGrappleAllowedOn(MaterialType type)
    {
        return type == MaterialType.Concrete || type == MaterialType.Lava || type == MaterialType.Ice;
    }

    private static bool IsLayerInMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    /// <summary>在擊中點建立空物件作為錨點（視覺與邏輯參考）；可選掛在命中表面下以利碎裂平台等邏輯。</summary>
    private void CreateAnchor(Vector3 worldHit, Transform surfaceParent)
    {
        if (_anchorObject != null)
            Destroy(_anchorObject);

        _anchorObject = new GameObject("WinchAnchor");
        _anchorObject.transform.position = worldHit;
        if (surfaceParent != null)
            _anchorObject.transform.SetParent(surfaceParent, true);
    }

    /// <summary>於角色上新增 SpringJoint：連至場景錨點，並套用指定 Spring／Damper／碰撞。</summary>
    private void AttachSpringJoint(float initialLength, in RaycastHit hit)
    {
        if (_joint != null)
            Destroy(_joint);

        _joint = gameObject.AddComponent<SpringJoint>();
        _joint.autoConfigureConnectedAnchor = false;
        // 錨點跟隨 WinchExitPoint（本體局部座標），另一端為場景上的 Anchor 世界座標。
        _joint.anchor = _rb.transform.InverseTransformPoint(winchExitPoint.position);
        Rigidbody targetRb = hit.rigidbody;
        if (targetRb != null)
        {
            _joint.connectedBody = targetRb;
            _joint.connectedAnchor = targetRb.transform.InverseTransformPoint(_anchorObject.transform.position);
        }
        else
        {
            _joint.connectedBody = null;
            _joint.connectedAnchor = _anchorObject.transform.position;
        }

        _joint.spring = jointSpring;
        _joint.damper = jointDamper;
        _joint.enableCollision = true;

        float len = Mathf.Clamp(initialLength, minRopeLength, maxRopeLength);
        _joint.minDistance = minRopeLength;
        _joint.maxDistance = len;
    }

    /// <summary>每固定幀依固定速率縮短目標繩長（捲揚），Clamp 防止過短／NaN 類異常。</summary>
    private void ApplyRopeShortening()
    {
        _currentRopeLength -= ropeShortenMetersPerSecond * Time.fixedDeltaTime;
        _currentRopeLength = Mathf.Clamp(_currentRopeLength, minRopeLength, maxRopeLength);
    }

    /// <summary>將 Joint 的繩長目標同步為目前捲揚長度，並更新身體側錨點。</summary>
    private void SyncJointToRopeLength()
    {
        if (_joint == null || winchExitPoint == null || _anchorObject == null)
            return;

        // 角色旋轉時仍從出口點連線至 Anchor。
        _joint.anchor = _rb.transform.InverseTransformPoint(winchExitPoint.position);
        if (_joint.connectedBody != null)
            _joint.connectedAnchor = _joint.connectedBody.transform.InverseTransformPoint(_anchorObject.transform.position);
        else
            _joint.connectedAnchor = _anchorObject.transform.position;
        float len = Mathf.Clamp(_currentRopeLength, minRopeLength, maxRopeLength);
        _joint.minDistance = minRopeLength;
        _joint.maxDistance = len;
    }

    /// <summary>左鍵放開：先依設定修正鬆線速度，再移除 Joint／Anchor（可選合速度上限）。</summary>
    private void DisconnectGrapple()
    {
        // 須在刪除 Anchor 前計算沿繩方向。
        ApplyTangentialVelocityOnRelease();
        ClampSpeedOnGrappleReleaseIfNeeded();

        if (_joint != null)
        {
            Destroy(_joint);
            _joint = null;
        }

        if (_anchorObject != null)
        {
            Destroy(_anchorObject);
            _anchorObject = null;
        }

        _connected = false;
        _skipSurfacePenetrationResolve = false;
        _grappleSurfaceMaterial = MaterialType.Concrete;
        _connectedMonster = null;
        _connectedMonsterCollider = null;
        _nextConnectedMonsterRamDamageTime = 0f;
        _grappledSurfaceColliders = null;

        if (lineRenderer != null)
            lineRenderer.enabled = false;

        // 鬆線後恢復與平台的碰撞（須在銷毀 Joint 之後仍執行）。
        EndIgnoreGrappleCollider();
    }

    /// <summary>撞擊怪物即造成傷害（不設速度下限）。</summary>
    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.contactCount <= 0)
            return;

        MonsterBase monster = collision.collider.GetComponentInParent<MonsterBase>();
        if (monster == null || monster.IsDead)
            return;

        bool isRammingConnectedMonster = _connected && _connectedMonster == monster;
        float speed = isRammingConnectedMonster ? _rb.linearVelocity.magnitude : collision.relativeVelocity.magnitude;

        Vector3 hitDirection = collision.relativeVelocity.sqrMagnitude > 1e-8f
            ? collision.relativeVelocity.normalized
            : -collision.contacts[0].normal;
        float damage = minAttackDamage + speed * attackDamageMultiplier;
        monster.TakeDamage(damage, hitDirection);
        _recentlyImpactDamagedMonster = monster;
        _recentMonsterNoGrappleUntilTime = Time.time + monsterRegrappleCooldownSeconds;

        _rb.AddForce(-collision.contacts[0].normal * bounceForce, ForceMode.Impulse);

        if (_connected && _connectedMonster == monster)
            DisconnectGrapple();
    }

    /// <summary>勾住怪物時主動檢查重疊碰撞，避免某些情況下 OnCollisionEnter 未觸發而漏傷害。</summary>
    private void TryApplyConnectedMonsterRamDamage()
    {
        if (!_connected || _connectedMonster == null || _connectedMonster.IsDead || _connectedMonsterCollider == null)
            return;
        if (Time.time < _nextConnectedMonsterRamDamageTime)
            return;
        if (_playerColliders == null)
            return;

        bool overlapping = false;
        Vector3 hitNormal = Vector3.zero;
        for (int i = 0; i < _playerColliders.Length; i++)
        {
            Collider playerCol = _playerColliders[i];
            if (playerCol == null || playerCol.isTrigger || !playerCol.enabled)
                continue;

            if (!Physics.ComputePenetration(
                    playerCol,
                    playerCol.transform.position,
                    playerCol.transform.rotation,
                    _connectedMonsterCollider,
                    _connectedMonsterCollider.transform.position,
                    _connectedMonsterCollider.transform.rotation,
                    out Vector3 dir,
                    out float dist))
            {
                continue;
            }

            if (dist <= 0.0001f)
                continue;

            overlapping = true;
            hitNormal = -dir;
            break;
        }

        if (!overlapping)
            return;

        float speed = _rb.linearVelocity.magnitude;
        float damage = minAttackDamage + speed * attackDamageMultiplier;
        Vector3 hitDirection = _rb.linearVelocity.sqrMagnitude > 1e-8f
            ? _rb.linearVelocity.normalized
            : (hitNormal.sqrMagnitude > 1e-8f ? -hitNormal.normalized : transform.forward);
        _connectedMonster.TakeDamage(damage, hitDirection);
        _recentlyImpactDamagedMonster = _connectedMonster;
        _recentMonsterNoGrappleUntilTime = Time.time + monsterRegrappleCooldownSeconds;
        _nextConnectedMonsterRamDamageTime = Time.time + connectedMonsterRamDamageInterval;

        if (hitNormal.sqrMagnitude > 1e-8f)
            _rb.AddForce(-hitNormal.normalized * bounceForce, ForceMode.Impulse);

        DisconnectGrapple();
    }

    /// <summary>連線在岩漿／冰上時的持續效果（之後若要改為「繩段掃過才觸發」可改寫此處）。</summary>
    private void ApplyGrappleSurfaceEffects()
    {
        if (!_connected)
            return;

        switch (_grappleSurfaceMaterial)
        {
            case MaterialType.Lava:
                if (lavaGrappleDamagePerSecond > 0f)
                    onLavaGrappleDamage.Invoke(lavaGrappleDamagePerSecond * Time.fixedDeltaTime);
                break;
            case MaterialType.Ice:
                if (iceGrappleHorizontalSlowdownPerSecond > 0f)
                    ApplyIceGrappleHorizontalSlowdown();
                break;
        }
    }

    /// <summary>冰上連線時對水平速度做指數衰減，模擬繩與冰面阻力。</summary>
    private void ApplyIceGrappleHorizontalSlowdown()
    {
        // 無敵時略過冰面水平減速；岩漿傷害仍由 onLavaGrappleDamage → TakeDamage 內部處理。
        EnsureHealthManagerReference();
        if (healthManager != null && healthManager.IsInvincible)
            return;

        Vector3 v = _rb.linearVelocity;
        Vector3 horiz = new Vector3(v.x, 0f, v.z);
        if (horiz.sqrMagnitude < 1e-8f)
            return;

        float factor = Mathf.Exp(-iceGrappleHorizontalSlowdownPerSecond * Time.fixedDeltaTime);
        horiz *= factor;
        _rb.linearVelocity = new Vector3(horiz.x, v.y, horiz.z);
    }

    /// <summary>錨點從命中點沿表面法線偏移；偏移為 0 時貼在命中點。</summary>
    private Vector3 ComputeAnchorWorldPosition(in RaycastHit hit)
    {
        return hit.point + hit.normal * anchorOffsetAlongHitNormal;
    }

    /// <summary>收繩時施加沿繩推進，加強「被拉向錨點」手感。</summary>
    private void ApplyReelInAcceleration()
    {
        if (reelInAcceleration <= 0f || winchExitPoint == null || _anchorObject == null)
            return;

        Vector3 toAnchor = _anchorObject.transform.position - winchExitPoint.position;
        float magSq = toAnchor.sqrMagnitude;
        if (magSq < 1e-6f)
            return;

        Vector3 dir = toAnchor * (1f / Mathf.Sqrt(magSq));

        if (reelInMaxSpeed > 0f)
        {
            float alongRopeSpeed = Vector3.Dot(_rb.linearVelocity, dir);
            if (alongRopeSpeed >= reelInMaxSpeed)
                return;
        }

        _rb.AddForce(dir * reelInAcceleration, ForceMode.Acceleration);
    }

    /// <summary>快取本次勾索目標的碰撞器，用於連線期間防止高速擺盪時穿透牆面。</summary>
    private void CacheGrappledSurfaceColliders(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            _grappledSurfaceColliders = null;
            return;
        }

        PlatformType platformRoot = hitCollider.GetComponentInParent<PlatformType>();
        Transform scope = platformRoot != null ? platformRoot.transform : hitCollider.transform;
        _grappledSurfaceColliders = scope.GetComponentsInChildren<Collider>(true);
    }

    /// <summary>若玩家與被勾牆面重疊，立即去穿透並移除朝牆內的速度分量，降低高速穿牆。</summary>
    private void ResolveGrappledSurfacePenetration()
    {
        if (_playerColliders == null || _grappledSurfaceColliders == null)
            return;

        Vector3 correction = Vector3.zero;

        for (int i = 0; i < _playerColliders.Length; i++)
        {
            Collider playerCol = _playerColliders[i];
            if (playerCol == null || playerCol.isTrigger || !playerCol.enabled)
                continue;

            for (int j = 0; j < _grappledSurfaceColliders.Length; j++)
            {
                Collider surfaceCol = _grappledSurfaceColliders[j];
                if (surfaceCol == null || surfaceCol.isTrigger || !surfaceCol.enabled)
                    continue;

                if (!Physics.ComputePenetration(
                        playerCol,
                        playerCol.transform.position,
                        playerCol.transform.rotation,
                        surfaceCol,
                        surfaceCol.transform.position,
                        surfaceCol.transform.rotation,
                        out Vector3 separationDir,
                        out float separationDist))
                {
                    continue;
                }

                if (separationDist <= 0.0001f)
                    continue;

                Vector3 push = separationDir * (separationDist + 0.005f);
                correction += push;
            }
        }

        if (correction.sqrMagnitude <= 1e-8f)
            return;

        _rb.position += correction;

        Vector3 correctionDir = correction.normalized;
        Vector3 v = _rb.linearVelocity;
        float inwardSpeed = Vector3.Dot(v, -correctionDir);
        if (inwardSpeed > 0f)
            _rb.linearVelocity = v + correctionDir * inwardSpeed;
    }

    private void BeginIgnoreGrappleCollider(Collider hitCollider, bool forceIgnore)
    {
        if ((!ignoreCollisionWithGrappledCollider && !forceIgnore) || hitCollider == null || _playerColliders == null)
            return;

        EndIgnoreGrappleCollider();

        // 以含 PlatformType 的平台物件為範圍（同張地圖多平台時不會誤傷），否則退回命中物本身及其子階層。
        PlatformType platformRoot = hitCollider.GetComponentInParent<PlatformType>();
        Transform scope = platformRoot != null ? platformRoot.transform : hitCollider.transform;
        _ignoredSurfaceColliders = scope.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < _playerColliders.Length; i++)
        {
            Collider playerCol = _playerColliders[i];
            if (playerCol == null || playerCol.isTrigger)
                continue;

            for (int j = 0; j < _ignoredSurfaceColliders.Length; j++)
            {
                Collider surfaceCol = _ignoredSurfaceColliders[j];
                if (surfaceCol == null || surfaceCol.isTrigger)
                    continue;
                Physics.IgnoreCollision(playerCol, surfaceCol, true);
            }
        }
    }

    private void EndIgnoreGrappleCollider()
    {
        if (_ignoredSurfaceColliders == null || _playerColliders == null)
            return;

        for (int i = 0; i < _playerColliders.Length; i++)
        {
            Collider playerCol = _playerColliders[i];
            if (playerCol == null || playerCol.isTrigger)
                continue;

            for (int j = 0; j < _ignoredSurfaceColliders.Length; j++)
            {
                Collider surfaceCol = _ignoredSurfaceColliders[j];
                if (surfaceCol == null || surfaceCol.isTrigger)
                    continue;
                Physics.IgnoreCollision(playerCol, surfaceCol, false);
            }
        }

        _ignoredSurfaceColliders = null;
    }

    /// <summary>
    /// 依設定削減鬆線時的沿繩速度分量。
    /// 0 = 全扣（只留切向）；1 = 完整保留（Apex 風格慣性）。
    /// </summary>
    private void ApplyTangentialVelocityOnRelease()
    {
        if (!retainTangentialVelocityOnRelease || winchExitPoint == null || _anchorObject == null)
            return;

        Vector3 rope = _anchorObject.transform.position - winchExitPoint.position;
        float magSq = rope.sqrMagnitude;
        if (magSq < 1e-6f)
            return;

        Vector3 ropeUnit = rope * (1f / Mathf.Sqrt(magSq));
        Vector3 v = _rb.linearVelocity;
        float radial = Vector3.Dot(v, ropeUnit);
        float removal = 1f - Mathf.Clamp01(radialVelocityRetentionOnRelease);
        v -= radial * removal * ropeUnit;
        _rb.linearVelocity = v;
    }

    /// <summary>收線到最短且無 WASD 輸入時，對線速度做指數衰減，讓殘餘晃動慢慢停下。</summary>
    private void ApplyFullRetractSlowdown()
    {
        if (!slowdownWhenRopeFullyRetracted || !_connected)
            return;

        if (_currentRopeLength > minRopeLength + fullRetractRopeLengthTolerance)
            return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        if (!Mathf.Approximately(h, 0f) || !Mathf.Approximately(v, 0f))
            return;

        Vector3 vel = _rb.linearVelocity;
        float mag = vel.magnitude;
        if (mag < fullRetractSnapToRestSpeed)
        {
            _rb.linearVelocity = Vector3.zero;
            return;
        }

        float decay = Mathf.Exp(-fullRetractVelocityDecayPerSecond * Time.fixedDeltaTime);
        _rb.linearVelocity = vel * decay;
    }

    /// <summary>空中時可選擇壓低水平速度上限，避免 SpringJoint＋收線把側向速度疊到失控。</summary>
    private void ClampAirHorizontalSpeedIfNeeded()
    {
        if (maxAirHorizontalSpeed <= 0f || !IsAirborne())
            return;

        Vector3 v = _rb.linearVelocity;
        Vector3 horizontal = new Vector3(v.x, 0f, v.z);
        float mag = horizontal.magnitude;
        if (mag <= maxAirHorizontalSpeed || mag < 0.0001f)
            return;

        horizontal *= maxAirHorizontalSpeed / mag;
        _rb.linearVelocity = new Vector3(horizontal.x, v.y, horizontal.z);
    }

    /// <summary>鬆線瞬間可選擇壓低合速度，避免單次擺盪速度過大直接飛出場景。</summary>
    private void ClampSpeedOnGrappleReleaseIfNeeded()
    {
        if (maxSpeedOnGrappleRelease <= 0f)
            return;

        Vector3 v = _rb.linearVelocity;
        float mag = v.magnitude;
        if (mag <= maxSpeedOnGrappleRelease || mag < 0.0001f)
            return;

        _rb.linearVelocity = v * (maxSpeedOnGrappleRelease / mag);
    }

    /// <summary>空中 WASD 操控：未連線與連線可用不同力道，皆依主攝影機方向施力。</summary>
    private void ApplyAirMovement()
    {
        if (!IsAirborne())
            return;

        if (playerCamera == null)
            return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        if (Mathf.Approximately(h, 0f) && Mathf.Approximately(v, 0f))
            return;

        float controlForce = _connected ? grappleAirControlForce : airControlForce;
        if (controlForce <= 0f)
            return;

        Vector3 forward = playerCamera.transform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = playerCamera.transform.right;
        right.y = 0f;
        right.Normalize();

        Vector3 wish = (forward * v + right * h).normalized;
        _rb.AddForce(wish * controlForce, ForceMode.Acceleration);
    }

    /// <summary>向下短射線未撞到地面層則視為空中；同樣忽略 Trigger，以免空中判定被觸發體誤判為著地。</summary>
    private bool IsAirborne()
    {
        Vector3 origin = _rb.position;
        return !Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundCheckLayers, QueryTriggerInteraction.Ignore);
    }

    /// <summary>鋼索頂點：WinchExitPoint → Anchor。</summary>
    private void UpdateLineRenderer()
    {
        if (lineRenderer == null || !_connected || winchExitPoint == null || _anchorObject == null)
            return;

        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, winchExitPoint.position);
        lineRenderer.SetPosition(1, _anchorObject.transform.position);
    }

    /// <summary>速度低於門檻維持較低 FOV；高於門檻時線性插值至較高 FOV，再以 Lerp 平滑。</summary>
    private void UpdateCameraFov()
    {
        if (playerCamera == null)
            return;

        float speed = _rb.linearVelocity.magnitude;
        float targetFov = speed <= fovSpeedThreshold
            ? fovIdle
            : Mathf.Lerp(fovIdle, fovSwing, Mathf.InverseLerp(fovSpeedThreshold, fovSpeedUpper, speed));

        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        minRopeLength = Mathf.Max(0.01f, minRopeLength);
        maxRopeLength = Mathf.Max(minRopeLength, maxRopeLength);
        minAttachDistanceFromPlayer = Mathf.Max(0f, minAttachDistanceFromPlayer);
        airControlForce = Mathf.Max(0f, airControlForce);
        grappleAirControlForce = Mathf.Max(0f, grappleAirControlForce);
        maxAirHorizontalSpeed = Mathf.Max(0f, maxAirHorizontalSpeed);
        maxSpeedOnGrappleRelease = Mathf.Max(0f, maxSpeedOnGrappleRelease);
        fullRetractRopeLengthTolerance = Mathf.Max(0f, fullRetractRopeLengthTolerance);
        fullRetractVelocityDecayPerSecond = Mathf.Max(0f, fullRetractVelocityDecayPerSecond);
        fullRetractSnapToRestSpeed = Mathf.Max(0f, fullRetractSnapToRestSpeed);
        anchorOffsetAlongHitNormal = Mathf.Max(0f, anchorOffsetAlongHitNormal);
        reelInAcceleration = Mathf.Max(0f, reelInAcceleration);
        reelInMaxSpeed = Mathf.Max(0f, reelInMaxSpeed);
        radialVelocityRetentionOnRelease = Mathf.Clamp01(radialVelocityRetentionOnRelease);
        attackDamageMultiplier = Mathf.Max(0f, attackDamageMultiplier);
        minAttackDamage = Mathf.Max(0f, minAttackDamage);
        bounceForce = Mathf.Max(0f, bounceForce);
        monsterRegrappleCooldownSeconds = Mathf.Max(0f, monsterRegrappleCooldownSeconds);
        connectedMonsterRamDamageInterval = Mathf.Max(0.01f, connectedMonsterRamDamageInterval);
        lavaGrappleDamagePerSecond = Mathf.Max(0f, lavaGrappleDamagePerSecond);
        iceGrappleHorizontalSlowdownPerSecond = Mathf.Max(0f, iceGrappleHorizontalSlowdownPerSecond);
    }
#endif
}
