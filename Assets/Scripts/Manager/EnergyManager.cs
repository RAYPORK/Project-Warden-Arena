using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 管理玩家收集的能量：達門檻觸發 <see cref="onReadyToLevelUp"/>（能力選擇），
/// 並以 <see cref="onEnergyChanged"/> 回報 0～1 進度；於 <see cref="Update"/> 以範圍吸引能量球飛向玩家。
/// </summary>
[DefaultExecutionOrder(-20)]
public class EnergyManager : MonoBehaviour
{
    [Header("能量設定")]
    [Tooltip("升級（觸發能力選擇）所需能量")]
    [SerializeField]
    private float energyToLevelUp = 100f;

    [Tooltip("目前能量；執行期更新，僅供除錯／UI 參考")]
    [SerializeField]
    private float currentEnergy;

    [Tooltip("以玩家為球心，此半徑內的能量球會被吸引飛向玩家")]
    [SerializeField]
    private float pickupRadius = 5f;

    [Tooltip("呼叫 EnergyOrb.AttractToward 時傳入的飛行速度")]
    [SerializeField]
    private float attractSpeed = 10f;

    [Tooltip("與玩家中心距離小於此值時直接吸收（不依賴 Trigger，避免未裝 Rigidbody 時無回調）")]
    [SerializeField]
    private float absorbRadius = 2f;

    [Header("參照")]
    [Tooltip("玩家根節點或吸收判定中心；未指派時嘗試以 Tag Player 尋找")]
    [SerializeField]
    private Transform playerTransform;

    [Tooltip("可選：僅允許此 LayerMask 內之「能量球根物件」被吸引；為 Nothing（0）則不過濾圖層。若 Mask 含玩家所在層，執行時會自動停用過濾（避免只掃到 PlayerRig）；建議能量球用專用 Layer，此欄只勾該層")]
    [SerializeField]
    private LayerMask energyOrbLayer = ~0;

    [Header("事件")]
    [Tooltip("能量變化時呼叫，參數為目前能量佔升級門檻的比例（0～1）")]
    [SerializeField]
    private UnityEvent<float> onEnergyChanged = new UnityEvent<float>();

    [Tooltip("能量達門檻並結算一次升級時呼叫（觸發能力選擇）；可能連續觸發多次")]
    [SerializeField]
    private UnityEvent onReadyToLevelUp = new UnityEvent();

    private float _nextPlayerMissingLogTime;

    /// <summary>增加能量；可連續升級多段，每次門檻達成扣減並觸發 <see cref="onReadyToLevelUp"/>。</summary>
    public void AddEnergy(float amount)
    {
        if (amount <= 0f)
            return;

        float threshold = Mathf.Max(0.01f, energyToLevelUp);
        currentEnergy += amount;
        Debug.Log($"[EnergyManager] 吸收能量 +{amount:F1}，目前 {currentEnergy:F1} / {threshold:F1}");

        while (currentEnergy >= threshold)
        {
            currentEnergy -= threshold;
            onReadyToLevelUp?.Invoke();
            Debug.Log($"[EnergyManager] 達門檻，觸發升級；剩餘能量 {currentEnergy:F1}");
        }

        onEnergyChanged?.Invoke(Mathf.Clamp01(currentEnergy / threshold));
    }

    /// <summary>新局開始時清空能量並回報進度 0。</summary>
    public void ResetEnergy()
    {
        currentEnergy = 0f;
        onEnergyChanged?.Invoke(0f);
    }

    private void Awake()
    {
        ResolvePlayerTransform(force: true);
    }

    private void Start()
    {
        ResolvePlayerTransform(force: true);
        Debug.Log($"[EnergyManager] 已啟動。玩家參照={(playerTransform != null ? playerTransform.name : "null")}，吸引半徑={pickupRadius}，吸收半徑={absorbRadius}");
        onEnergyChanged?.Invoke(Mathf.Clamp01(currentEnergy / Mathf.Max(0.01f, energyToLevelUp)));
    }

    private void Update()
    {
        ResolvePlayerTransform(force: false);
        if (playerTransform == null)
        {
            if (Time.unscaledTime >= _nextPlayerMissingLogTime)
            {
                _nextPlayerMissingLogTime = Time.unscaledTime + 3f;
                Debug.LogWarning("[EnergyManager] 找不到玩家 Transform：請在 Inspector 指派 playerTransform，或確保場景有 Tag「Player」／WardenHealthManager／WardenController。");
            }

            return;
        }

        Vector3 playerPos = playerTransform.position;
        float radius = Mathf.Max(0.01f, pickupRadius);
        float innerAbsorb = Mathf.Max(0.05f, absorbRadius);

        bool useOrbLayerFilter = energyOrbLayer.value != 0;
        // 若 Mask 含玩家層，無法同時當「只掃能量球」用；改為不過濾圖層（仍只處理帶 EnergyOrb 的物件）。
        if (useOrbLayerFilter && ((1 << playerTransform.gameObject.layer) & energyOrbLayer.value) != 0)
            useOrbLayerFilter = false;

        // 先以全部圖層掃描，再以 EnergyOrb 與可選 energyOrbLayer 過濾。
        const int allLayersMask = ~0;
        Collider[] hits = Physics.OverlapSphere(
            playerPos,
            radius,
            allLayersMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            if (col == null)
                continue;

            EnergyOrb orb = ResolveEnergyOrbFromCollider(col);
            if (orb == null)
            {
                if (IsPlayerHierarchyCollider(col))
                    continue;

                continue;
            }

            if (useOrbLayerFilter)
            {
                int orbLayer = orb.gameObject.layer;
                if (((1 << orbLayer) & energyOrbLayer.value) == 0)
                    continue;
            }

            Vector3 orbPos = orb.transform.position;
            float d = Vector3.Distance(orbPos, playerPos);

            if (d <= innerAbsorb)
                orb.AbsorbFromProximity(this);
            else
                orb.AttractToward(playerPos, attractSpeed);
        }
    }

    /// <summary>
    /// 未指派玩家時依序嘗試：Tag「Player」→ <see cref="WardenHealthManager"/> → <see cref="WardenController"/>。
    /// </summary>
    private void ResolvePlayerTransform(bool force)
    {
        if (!force && playerTransform != null)
            return;

        if (force && playerTransform != null)
            return;

        GameObject playerObj = null;
        try
        {
            playerObj = GameObject.FindGameObjectWithTag("Player");
        }
        catch (UnityException)
        {
            // 專案未定義 "Player" Tag 時 FindGameObjectWithTag 可能丟例外
        }

        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            return;
        }

        WardenHealthManager health = UnityEngine.Object.FindFirstObjectByType<WardenHealthManager>(FindObjectsInactive.Include);
        if (health != null)
        {
            playerTransform = health.transform;
            return;
        }

        WardenController controller = UnityEngine.Object.FindFirstObjectByType<WardenController>(FindObjectsInactive.Include);
        if (controller != null)
            playerTransform = controller.transform;
    }

    /// <summary>
    /// 由命中 Collider 尋找 <see cref="EnergyOrb"/>：本體／祖先／子樹；
    /// 若為「根名含 EnergyOrb」Prefab（腳本與 Trigger 為兄弟），則自 root 子樹搜尋。
    /// </summary>
    private static EnergyOrb ResolveEnergyOrbFromCollider(Collider col)
    {
        if (col == null)
            return null;

        EnergyOrb orb = col.GetComponent<EnergyOrb>()
            ?? col.GetComponentInParent<EnergyOrb>()
            ?? col.GetComponentInChildren<EnergyOrb>(true);
        if (orb != null)
            return orb;

        Transform root = col.transform.root;
        if (root != null && root.name.IndexOf("EnergyOrb", StringComparison.OrdinalIgnoreCase) >= 0)
            return root.GetComponentInChildren<EnergyOrb>(true);

        return null;
    }

    private static bool IsPlayerHierarchyCollider(Collider col)
    {
        if (col == null)
            return false;
        if (col.GetComponentInParent<WardenController>() != null)
            return true;
        if (col.GetComponentInParent<WardenHealthManager>() != null)
            return true;
        try
        {
            if (col.CompareTag("Player"))
                return true;
            if (col.transform.root.CompareTag("Player"))
                return true;
        }
        catch (UnityException)
        {
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        energyToLevelUp = Mathf.Max(0.01f, energyToLevelUp);
        currentEnergy = Mathf.Max(0f, currentEnergy);
        pickupRadius = Mathf.Max(0.01f, pickupRadius);
        attractSpeed = Mathf.Max(0f, attractSpeed);
        absorbRadius = Mathf.Max(0.05f, absorbRadius);
    }
#endif
}
