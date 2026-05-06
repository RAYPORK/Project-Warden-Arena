using UnityEngine;

/// <summary>
/// 玩家數值的單一資料來源：基礎值 + 倍率與固定加成後的最終值，
/// 供攻擊、防禦、肉鴿能力升級等系統讀寫。
/// 最終值公式（有倍率者）：基礎值 × 倍率 + 固定加成；其餘為基礎值 + 加成。
/// </summary>
public class PlayerStats : MonoBehaviour
{
    [Header("移動")]
    [SerializeField] private float baseMoveSpeed = 5f;
    private float bonusMoveSpeed;
    private float moveSpeedMultiplier = 1f;

    [SerializeField] private float baseGrappleRange = 40f;
    private float bonusGrappleRange;
    private float grappleRangeMultiplier = 1f;

    [SerializeField] private float baseGrappleCooldown;
    private float bonusGrappleCooldown;

    [Header("防禦")]
    [SerializeField] private float baseMaxHealth = 100f;
    private float bonusMaxHealth;

    [Tooltip("傷害減免比例，0.3 = 減少 30% 傷害")]
    [SerializeField] private float baseArmor;
    private float bonusArmor;

    [Tooltip("每秒回血")]
    [SerializeField] private float baseHealthRegen;
    private float bonusHealthRegen;

    [Tooltip("造成傷害轉為回血的比例，0.1 = 10%")]
    [SerializeField] private float baseLifesteal;
    private float bonusLifesteal;

    [Tooltip("迴避率，0.2 = 20%")]
    [SerializeField] private float baseDodgeChance;
    private float bonusDodgeChance;

    [Header("攻擊")]
    [SerializeField] private float baseDamage = 10f;
    private float bonusDamage;
    private float damageMultiplier = 1f;

    [Tooltip("近戰額外傷害（最終近戰傷害 = Damage + MeleeDamage）")]
    [SerializeField] private float baseMeleeDamage;
    private float bonusMeleeDamage;
    private float meleeDamageMultiplier = 1f;

    [Tooltip("遠距額外傷害（最終遠距傷害 = Damage + RangedDamage）")]
    [SerializeField] private float baseRangedDamage;
    private float bonusRangedDamage;
    private float rangedDamageMultiplier = 1f;

    [Tooltip("攻擊速度倍率，1 = 正常，2 = 兩倍速")]
    [SerializeField] private float baseAttackSpeed = 1f;
    private float bonusAttackSpeed;

    [SerializeField] private float baseCritChance;
    private float bonusCritChance;

    [SerializeField] private float baseCritMultiplier = 2f;
    private float bonusCritMultiplier;

    [SerializeField] private float baseAoeRadius;
    private float bonusAoeRadius;

    [SerializeField] private int basePiercing;
    private int bonusPiercing;

    [Header("特殊")]
    [Tooltip("幸運值，影響稀有能力出現機率")]
    [SerializeField] private float baseLuck;
    private float bonusLuck;

    /// <summary>最終移動速度 = 基礎 × 倍率 + 固定加成。</summary>
    public float MoveSpeed => baseMoveSpeed * moveSpeedMultiplier + bonusMoveSpeed;

    /// <summary>最終鉤索距離 = 基礎 × 倍率 + 固定加成。</summary>
    public float GrappleRange => baseGrappleRange * grappleRangeMultiplier + bonusGrappleRange;

    /// <summary>鉤索冷卻（秒）；加成可為負以縮短冷卻。</summary>
    public float GrappleCooldown => baseGrappleCooldown + bonusGrappleCooldown;

    /// <summary>最大生命；至少為 1。</summary>
    public float MaxHealth => Mathf.Max(1f, baseMaxHealth + bonusMaxHealth);

    /// <summary>護甲（傷害減免 %）；合計後限制在 0～0.9。</summary>
    public float Armor => Mathf.Clamp(baseArmor + bonusArmor, 0f, 0.9f);

    public float HealthRegen => baseHealthRegen + bonusHealthRegen;

    public float Lifesteal => baseLifesteal + bonusLifesteal;

    /// <summary>迴避率；合計後限制在 0～0.75。</summary>
    public float DodgeChance => Mathf.Clamp(baseDodgeChance + bonusDodgeChance, 0f, 0.75f);

    /// <summary>一般傷害基礎段：基礎 × 倍率 + 固定加成。</summary>
    public float Damage => baseDamage * damageMultiplier + bonusDamage;

    /// <summary>近戰額外段：基礎 × 倍率 + 固定加成。</summary>
    public float MeleeDamage => baseMeleeDamage * meleeDamageMultiplier + bonusMeleeDamage;

    /// <summary>遠距額外段：基礎 × 倍率 + 固定加成。</summary>
    public float RangedDamage => baseRangedDamage * rangedDamageMultiplier + bonusRangedDamage;

    /// <summary>攻擊速度倍率；合計後至少 0.1。</summary>
    public float AttackSpeed => Mathf.Max(0.1f, baseAttackSpeed + bonusAttackSpeed);

    /// <summary>爆擊率；合計後限制在 0～1。</summary>
    public float CritChance => Mathf.Clamp(baseCritChance + bonusCritChance, 0f, 1f);

    public float CritMultiplier => baseCritMultiplier + bonusCritMultiplier;

    public float AoeRadius => baseAoeRadius + bonusAoeRadius;

    public int Piercing => Mathf.Max(0, basePiercing + bonusPiercing);

    public float Luck => baseLuck + bonusLuck;

    /// <summary>依目前爆擊率擲骰是否爆擊。</summary>
    public bool RollCrit() => Random.value < CritChance;

    /// <summary>對已結算傷害套用爆擊倍率（內部重新擲骰）。</summary>
    public float ApplyCrit(float damage) => RollCrit() ? damage * CritMultiplier : damage;

    /// <summary>傳入原始傷害，經迴避與護甲後的實際承受值。</summary>
    public float CalculateIncomingDamage(float rawDamage)
    {
        if (Random.value < DodgeChance)
            return 0f;
        return rawDamage * (1f - Armor);
    }

    /// <summary>依造成傷害與吸血比例計算回血量。</summary>
    public float CalculateLifesteal(float damageDealt) => damageDealt * Lifesteal;

    /// <summary>固定傷害加成（累加於 bonusDamage）。</summary>
    public void AddBonusDamage(float amount) => bonusDamage += amount;

    /// <summary>固定移速加成（累加於 bonusMoveSpeed）。</summary>
    public void AddBonusMoveSpeed(float amount) => bonusMoveSpeed += amount;

    public void AddBonusMaxHealth(float amount) => bonusMaxHealth += amount;

    public void AddBonusArmor(float amount) => bonusArmor += amount;

    public void AddBonusHealthRegen(float amount) => bonusHealthRegen += amount;

    public void AddBonusLifesteal(float amount) => bonusLifesteal += amount;

    public void AddBonusDodgeChance(float amount) => bonusDodgeChance += amount;

    public void AddBonusCritChance(float amount) => bonusCritChance += amount;

    public void AddBonusCritMultiplier(float amount) => bonusCritMultiplier += amount;

    public void AddBonusAttackSpeed(float amount) => bonusAttackSpeed += amount;

    public void AddBonusAoeRadius(float amount) => bonusAoeRadius += amount;

    public void AddBonusPiercing(int amount) => bonusPiercing += amount;

    public void AddBonusLuck(float amount) => bonusLuck += amount;

    public void AddBonusGrappleRange(float amount) => bonusGrappleRange += amount;

    /// <summary>冷卻加成；負值縮短冷卻。</summary>
    public void AddBonusGrappleCooldown(float amount) => bonusGrappleCooldown += amount;

    public void AddBonusMeleeDamage(float amount) => bonusMeleeDamage += amount;

    public void AddBonusRangedDamage(float amount) => bonusRangedDamage += amount;

    /// <summary>乘上傷害倍率（肉鴿「傷害 +X%」類可呼叫）。</summary>
    public void MultiplyDamage(float multiplier) => damageMultiplier *= multiplier;

    /// <summary>乘上移速倍率。</summary>
    public void MultiplyMoveSpeed(float multiplier) => moveSpeedMultiplier *= multiplier;

    /// <summary>乘上鉤索距離倍率。</summary>
    public void MultiplyGrappleRange(float multiplier) => grappleRangeMultiplier *= multiplier;

    /// <summary>乘上近戰額外傷害段倍率。</summary>
    public void MultiplyMeleeDamage(float multiplier) => meleeDamageMultiplier *= multiplier;

    /// <summary>乘上遠距額外傷害段倍率。</summary>
    public void MultiplyRangedDamage(float multiplier) => rangedDamageMultiplier *= multiplier;

    /// <summary>新局開始時重置所有加成與倍率係數（基礎值不變）。</summary>
    public void ResetBonuses()
    {
        bonusMoveSpeed = 0f;
        bonusGrappleRange = 0f;
        bonusGrappleCooldown = 0f;
        bonusMaxHealth = 0f;
        bonusArmor = 0f;
        bonusHealthRegen = 0f;
        bonusLifesteal = 0f;
        bonusDodgeChance = 0f;
        bonusDamage = 0f;
        bonusMeleeDamage = 0f;
        bonusRangedDamage = 0f;
        bonusAttackSpeed = 0f;
        bonusCritChance = 0f;
        bonusCritMultiplier = 0f;
        bonusAoeRadius = 0f;
        bonusPiercing = 0;
        bonusLuck = 0f;

        moveSpeedMultiplier = 1f;
        grappleRangeMultiplier = 1f;
        damageMultiplier = 1f;
        meleeDamageMultiplier = 1f;
        rangedDamageMultiplier = 1f;
    }
}
