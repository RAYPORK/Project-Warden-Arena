using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 死亡與重開：玩家 Y 低於門檻（掉落虛空）時凍結輸入、淡入結算面板；
/// 再試一次時重置統計與玩家狀態。
/// </summary>
[DefaultExecutionOrder(50)]
public class WardenDeathManager : MonoBehaviour
{
    [Header("核心參照")]
    [SerializeField] private WardenController playerController;
    [SerializeField] private WardenWinchSystem winchSystem;

    [Header("結算 UI（Canvas Group，勿用 SetActive）")]
    [SerializeField] private CanvasGroup gameOverPanel;

    [SerializeField] private TMP_Text survivalTimeText;
    [SerializeField] private TMP_Text slotSpinCountText;
    [SerializeField] private TMP_Text energyCollectedText;

    [Tooltip("死亡結算面板顯示最遠距離")]
    [SerializeField]
    private TMP_Text distanceText;

    [SerializeField] private UnityEngine.UI.Button tryAgainButton;

    [Header("結算 TMP 字型（選填）")]
    [Tooltip("LiberationSans SDF 不含中文。若場景中這些數值 TMP 曾填中文預設字，請改為純數字顯示或在此指派含 CJK 的 Font Asset（例如 Noto／思源黑體 SDF）。")]
    [SerializeField] private TMP_FontAsset statsFontWithCjkSupport;

    [Header("重開：玩家位置")]
    [Tooltip("用於掉落判定與再試一次時重置位置的 Transform（例如 PlayerRig）")]
    [SerializeField] private Transform playerRespawnRoot;

    [SerializeField] private Vector3 respawnWorldPosition = new Vector3(0f, 2f, 0f);

    [Header("淡入")]
    [SerializeField] private float gameOverFadeInSeconds = 0.5f;

    private bool _isDead;

    /// <summary>是否已進入死亡流程（結算期間含面板淡入前後）。暫停選單等可據此阻擋 ESC。</summary>
    public bool isDead => _isDead;

    private float _sessionStartTime;
    private int _slotSpinCount;
    private float _energyCollectedTotal;
    private Coroutine _fadeRoutine;

    /// <summary>改於 Awake：與其他系統一致，避免非同步載入後 <c>Start</c> 延遲導致從未執行。</summary>
    private void Awake()
    {
        _sessionStartTime = Time.time;

        ApplyStatsFontAndAsciiPlaceholders();

        if (gameOverPanel != null)
        {
            gameOverPanel.alpha = 0f;
            gameOverPanel.interactable = false;
            gameOverPanel.blocksRaycasts = false;
        }

        if (tryAgainButton != null)
            tryAgainButton.onClick.AddListener(OnDeathTryAgainClicked);
    }

    /// <summary>
    /// 避免場景預設中文與 LiberationSans 衝突：可選套用 CJK 字型，並將結算用數值 TMP 改為純 ASCII 佔位（僅數字與冒號）。
    /// </summary>
    private void ApplyStatsFontAndAsciiPlaceholders()
    {
        if (statsFontWithCjkSupport != null)
        {
            if (survivalTimeText != null)
                survivalTimeText.font = statsFontWithCjkSupport;
            if (slotSpinCountText != null)
                slotSpinCountText.font = statsFontWithCjkSupport;
            if (energyCollectedText != null)
                energyCollectedText.font = statsFontWithCjkSupport;
            if (distanceText != null)
                distanceText.font = statsFontWithCjkSupport;
        }

        ResetHudStatTextsToAsciiPlaceholders();
    }

    private void ResetHudStatTextsToAsciiPlaceholders()
    {
        if (survivalTimeText != null)
            survivalTimeText.text = "TIME: 00:00";
        if (slotSpinCountText != null)
            slotSpinCountText.text = "SLOTS: 0";
        if (energyCollectedText != null)
            energyCollectedText.text = "ENERGY: 0";
        if (distanceText != null)
            distanceText.text = "DISTANCE: 0m";
    }

    private void Update()
    {
        // 下方掉落死亡邏輯已移除（封閉競技場不使用死亡邊界）。
    }

    private void OnDestroy()
    {
        if (tryAgainButton != null)
            tryAgainButton.onClick.RemoveListener(OnDeathTryAgainClicked);
    }

    /// <summary>拉霸轉動次數統計（可由 UnityEvent 綁定）。</summary>
    public void RegisterSlotSpin()
    {
        if (_isDead)
            return;
        _slotSpinCount++;
    }

    /// <summary>收集能量統計（可由 UnityEvent 綁定）。</summary>
    public void RegisterEnergyCollected(float amount)
    {
        if (_isDead)
            return;
        if (amount <= 0f)
            return;
        _energyCollectedTotal += amount;
    }

    public void BeginDeathSequence()
    {
        _isDead = true;

        // 死亡時解鎖游標讓玩家能點擊 UI
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        float survivedSeconds = Mathf.Max(0f, Time.time - _sessionStartTime);
        PopulateResultTexts(survivedSeconds);

        if (playerController != null)
            playerController.enabled = false;
        if (winchSystem != null)
        {
            winchSystem.ForceDisconnectIfConnected();
            winchSystem.enabled = false;
        }

        if (_fadeRoutine != null)
            StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeInGameOverPanel());
    }

    private void PopulateResultTexts(float survivedSeconds)
    {
        int totalSec = Mathf.FloorToInt(survivedSeconds);
        int mm = totalSec / 60;
        int ss = totalSec % 60;

        if (survivalTimeText != null)
            survivalTimeText.text = $"TIME: {mm:00}:{ss:00}";
        if (slotSpinCountText != null)
            slotSpinCountText.text = $"SLOTS: {_slotSpinCount}";
        if (energyCollectedText != null)
            energyCollectedText.text = $"ENERGY: {Mathf.FloorToInt(_energyCollectedTotal)}";

        if (distanceText != null)
            distanceText.text = "DISTANCE: --";
    }

    private IEnumerator FadeInGameOverPanel()
    {
        if (gameOverPanel == null)
            yield break;

        float duration = Mathf.Max(0.01f, gameOverFadeInSeconds);
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            gameOverPanel.alpha = Mathf.Clamp01(t / duration);
            yield return null;
        }

        gameOverPanel.alpha = 1f;
        gameOverPanel.interactable = true;
        gameOverPanel.blocksRaycasts = true;
        _fadeRoutine = null;
    }

    private void OnDeathTryAgainClicked()
    {
        if (!_isDead)
            return;
        PerformRunRestart(hideDeathPanel: true);
    }

    /// <summary>
    /// 任務完成面板「再試一次」等外部呼叫：重產地圖、補滿能量、重置玩家與統計（不要求處於死亡狀態）。
    /// </summary>
    public void RestartRunAfterMissionComplete()
    {
        PerformRunRestart(hideDeathPanel: false);
    }

    private void PerformRunRestart(bool hideDeathPanel)
    {
        if (hideDeathPanel)
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (gameOverPanel != null)
            {
                gameOverPanel.alpha = 0f;
                gameOverPanel.interactable = false;
                gameOverPanel.blocksRaycasts = false;
            }
        }

        // 重開時補滿血量（Unity 6 使用 FindFirstObjectByType）
        WardenHealthManager healthManager = Object.FindFirstObjectByType<WardenHealthManager>();
        if (healthManager != null)
            healthManager.RestoreFullHealth();

        if (winchSystem != null)
            winchSystem.ForceDisconnectIfConnected();

        if (playerRespawnRoot != null)
        {
            playerRespawnRoot.position = respawnWorldPosition;
            if (playerRespawnRoot.TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        if (playerController != null)
            playerController.enabled = true;
        if (winchSystem != null)
            winchSystem.enabled = true;

        _slotSpinCount = 0;
        _energyCollectedTotal = 0f;
        _sessionStartTime = Time.time;
        _isDead = false;

        ResetHudStatTextsToAsciiPlaceholders();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        gameOverFadeInSeconds = Mathf.Max(0.01f, gameOverFadeInSeconds);
    }
#endif
}
