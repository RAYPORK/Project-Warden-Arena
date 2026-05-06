using UnityEngine;

/// <summary>
/// 單筆能力資料（ScriptableObject），供升級選卡或掉落套用至 <see cref="PlayerStats"/>。
/// </summary>
[CreateAssetMenu(fileName = "AbilityData", menuName = "Warden Arena/Ability Data")]
public class AbilityData : ScriptableObject
{
    [Tooltip("能力顯示名稱")]
    [SerializeField]
    private string abilityName;

    [Tooltip("能力說明文字")]
    [SerializeField]
    [TextArea(2, 6)]
    private string description;

    [Tooltip("卡片圖示；可留空")]
    [SerializeField]
    private Sprite icon;

    [Tooltip("影響的屬性種類")]
    [SerializeField]
    private AbilityType abilityType;

    [Tooltip("數值；Piercing 會轉成 int 加成")]
    [SerializeField]
    private float value;

    [Tooltip("true = 使用倍率管道（乘上現有倍率）；false = 固定加成（加在 bonus 上）。僅部分屬性支援倍率。")]
    [SerializeField]
    private bool isMultiplier;

    public string AbilityName => abilityName;
    public string Description => description;
    public Sprite Icon => icon;
    public AbilityType Type => abilityType;
    public float Value => value;
    public bool IsMultiplier => isMultiplier;

    /// <summary>
    /// 依 <see cref="abilityType"/> 將此能力套用至玩家數值。
    /// 無內建倍率欄位的屬性一律走固定加成（忽略 <see cref="isMultiplier"/>）。
    /// </summary>
    public void Apply(PlayerStats stats)
    {
        if (stats == null)
            return;

        switch (abilityType)
        {
            case AbilityType.MoveSpeed:
                if (isMultiplier)
                    stats.MultiplyMoveSpeed(value);
                else
                    stats.AddBonusMoveSpeed(value);
                break;

            case AbilityType.GrappleRange:
                if (isMultiplier)
                    stats.MultiplyGrappleRange(value);
                else
                    stats.AddBonusGrappleRange(value);
                break;

            case AbilityType.MaxHealth:
                stats.AddBonusMaxHealth(value);
                break;

            case AbilityType.Armor:
                stats.AddBonusArmor(value);
                break;

            case AbilityType.HealthRegen:
                stats.AddBonusHealthRegen(value);
                break;

            case AbilityType.Lifesteal:
                stats.AddBonusLifesteal(value);
                break;

            case AbilityType.DodgeChance:
                stats.AddBonusDodgeChance(value);
                break;

            case AbilityType.Damage:
                if (isMultiplier)
                    stats.MultiplyDamage(value);
                else
                    stats.AddBonusDamage(value);
                break;

            case AbilityType.MeleeDamage:
                if (isMultiplier)
                    stats.MultiplyMeleeDamage(value);
                else
                    stats.AddBonusMeleeDamage(value);
                break;

            case AbilityType.RangedDamage:
                if (isMultiplier)
                    stats.MultiplyRangedDamage(value);
                else
                    stats.AddBonusRangedDamage(value);
                break;

            case AbilityType.AttackSpeed:
                stats.AddBonusAttackSpeed(value);
                break;

            case AbilityType.CritChance:
                stats.AddBonusCritChance(value);
                break;

            case AbilityType.CritMultiplier:
                stats.AddBonusCritMultiplier(value);
                break;

            case AbilityType.AoeRadius:
                stats.AddBonusAoeRadius(value);
                break;

            case AbilityType.Piercing:
                stats.AddBonusPiercing(Mathf.RoundToInt(value));
                break;

            case AbilityType.Luck:
                stats.AddBonusLuck(value);
                break;

            default:
                Debug.LogWarning($"[AbilityData] 未定義的 AbilityType：{abilityType}");
                break;
        }
    }
}

/// <summary>能力影響的屬性列舉。</summary>
public enum AbilityType
{
    MoveSpeed,
    GrappleRange,
    MaxHealth,
    Armor,
    HealthRegen,
    Lifesteal,
    DodgeChance,
    Damage,
    MeleeDamage,
    RangedDamage,
    AttackSpeed,
    CritChance,
    CritMultiplier,
    AoeRadius,
    Piercing,
    Luck
}
