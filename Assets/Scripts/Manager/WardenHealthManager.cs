using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 玩家血量：上限與承傷計算來自 <see cref="PlayerStats"/>（可選 Inspector 指派，未指派則自動尋找）；
/// 岩漿等來源可綁定 <see cref="TakeDamage"/>；<see cref="onHealthChanged"/> 供 HUD；
/// 歸零觸發 <see cref="onDeath"/>（通常於 Inspector 連至 <see cref="WardenDeathManager.BeginDeathSequence"/>）；
/// <see cref="RestoreFullHealth"/> 用於重開補滿；<see cref="SetInvincible"/> 暫停扣血（仍會自然回血）。
/// </summary>
public class WardenHealthManager : MonoBehaviour
{
    [Header("數值來源")]
    [Tooltip("未指派時會於 Start／受傷／回血前自動在自身、雙親、子物件或場景尋找 PlayerStats")]
    [SerializeField]
    private PlayerStats playerStats;

    [Header("血量設定")]
    [Tooltip("無 PlayerStats 時使用的血量上限；有 PlayerStats 時於 Start 會改為讀取其 MaxHealth")]
    [SerializeField]
    private float maxHealth = 100f;

    [Tooltip("回合開始時的初始血量（會夾在 0 與當時上限之間）")]
    [SerializeField]
    private float startingHealth = 100f;

    [Header("事件")]
    [Tooltip("血量變更時帶入「目前血量」，可綁定 HUD Slider")]
    [SerializeField]
    private WardenHealthFloatUnityEvent onHealthChanged = new WardenHealthFloatUnityEvent();

    [Tooltip("血量歸零時觸發（同一輪僅觸發一次）")]
    [SerializeField]
    private UnityEvent onDeath = new UnityEvent();

    [Tooltip("迴避成功（實際傷害為 0）時觸發，可綁定飄字「Miss」等")]
    [SerializeField]
    private UnityEvent onDamageDodged = new UnityEvent();

    // 目前血量（不低於 0）
    private float _currentHealth;
    // 本輪是否已觸發過 onDeath（歸零僅觸發一次；RestoreFullHealth 會清除）
    private bool _deathTriggered;

    // 無敵時略過扣血（例如過載無敵，由外部呼叫 SetInvincible）
    private bool _isInvincible = false;

    // 已嘗試場景搜尋仍無 PlayerStats 時為 true，避免 Update 每幀 FindFirstObjectByType
    private bool _playerStatsSearchExhausted;

    private void Awake()
    {
        ClampSettings();
        _currentHealth = Mathf.Clamp(startingHealth, 0f, maxHealth);
        RaiseHealthChanged();
    }

    private void Start()
    {
        ResolvePlayerStats();
        if (playerStats != null)
            maxHealth = playerStats.MaxHealth;

        _currentHealth = Mathf.Clamp(_currentHealth, 0f, GetMaxHealth());
        RaiseHealthChanged();
    }

    private void Update()
    {
        if (_deathTriggered)
            return;

        ResolvePlayerStats();
        if (playerStats != null && playerStats.HealthRegen > 0f)
            Heal(playerStats.HealthRegen * Time.deltaTime);
    }

    /// <summary>取得目前血量上限（有 PlayerStats 時以其 MaxHealth 為準）。</summary>
    private float GetMaxHealth() => playerStats != null ? playerStats.MaxHealth : maxHealth;

    /// <summary>若未指派 PlayerStats，依序於自身、雙親、子物件與場景尋找（場景搜尋僅執行一次）。</summary>
    private void ResolvePlayerStats()
    {
        if (playerStats != null)
            return;
        if (_playerStatsSearchExhausted)
            return;

        playerStats = GetComponent<PlayerStats>()
            ?? GetComponentInParent<PlayerStats>()
            ?? GetComponentInChildren<PlayerStats>(true);

        if (playerStats != null)
            return;

        playerStats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
        if (playerStats == null)
            _playerStatsSearchExhausted = true;
    }

    /// <summary>造成傷害（例如岩漿 UnityEvent）；無敵時不扣血、不判定迴避。</summary>
    public void TakeDamage(float amount)
    {
        if (_isInvincible)
            return;

        if (amount <= 0f || _deathTriggered)
            return;

        ResolvePlayerStats();

        float actualDamage = playerStats != null
            ? playerStats.CalculateIncomingDamage(amount)
            : amount;

        // 有傷害意圖但結算為 0：迴避成功（PlayerStats 內以擲骰判定）
        if (actualDamage <= 0f)
        {
            if (amount > 0f && playerStats != null)
                onDamageDodged?.Invoke();
            return;
        }

        _currentHealth = Mathf.Max(0f, _currentHealth - actualDamage);
        RaiseHealthChanged();

        if (_currentHealth <= 0f)
        {
            _deathTriggered = true;
            onDeath?.Invoke();
        }
    }

    /// <summary>回復血量（不超過目前上限）；已死亡觸發過時不回血。</summary>
    public void Heal(float amount)
    {
        if (amount <= 0f || _deathTriggered)
            return;

        ResolvePlayerStats();

        float cap = GetMaxHealth();
        float next = Mathf.Min(cap, _currentHealth + amount);
        if (Mathf.Approximately(next, _currentHealth))
            return;

        _currentHealth = next;
        RaiseHealthChanged();
    }

    /// <summary>設定無敵：為 true 時 <see cref="TakeDamage"/> 直接返回（不扣血、不觸發死亡）。</summary>
    public void SetInvincible(bool invincible)
    {
        _isInvincible = invincible;
    }

    /// <summary>是否處於無敵（其他系統可據此略過非扣血類懲罰，例如冰面減速）。</summary>
    public bool IsInvincible => _isInvincible;

    /// <summary>補滿血量並重置死亡標記（重開／再試一次時呼叫）。</summary>
    public void RestoreFullHealth()
    {
        _deathTriggered = false;
        _playerStatsSearchExhausted = false;
        ResolvePlayerStats();
        if (playerStats != null)
            maxHealth = playerStats.MaxHealth;

        _currentHealth = Mathf.Max(0f, GetMaxHealth());
        RaiseHealthChanged();
    }

    private void RaiseHealthChanged()
    {
        onHealthChanged?.Invoke(_currentHealth);
    }

    private void ClampSettings()
    {
        maxHealth = Mathf.Max(0f, maxHealth);
        startingHealth = Mathf.Clamp(startingHealth, 0f, maxHealth);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxHealth = Mathf.Max(0f, maxHealth);
        startingHealth = Mathf.Clamp(startingHealth, 0f, maxHealth);
    }
#endif
}

/// <summary>Inspector 可序列化的 float UnityEvent。</summary>
[Serializable]
public class WardenHealthFloatUnityEvent : UnityEvent<float> { }
