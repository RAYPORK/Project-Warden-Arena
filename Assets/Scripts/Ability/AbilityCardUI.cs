using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 單張能力卡 UI：顯示名稱、描述、圖示，並透過按鈕回報選取。
/// </summary>
public class AbilityCardUI : MonoBehaviour
{
    [Tooltip("能力名稱文字")]
    [SerializeField]
    private TextMeshProUGUI nameText;

    [Tooltip("能力描述文字")]
    [SerializeField]
    private TextMeshProUGUI descriptionText;

    [Tooltip("能力圖示")]
    [SerializeField]
    private Image iconImage;

    [Tooltip("點擊後選取此卡")]
    [SerializeField]
    private Button button;

    /// <summary>
    /// 綁定資料與選取回呼；重設按鈕監聽。
    /// </summary>
    /// <param name="data">要顯示的能力資料</param>
    /// <param name="onSelected">按下按鈕時呼叫（通常由選擇面板傳入）</param>
    public void Setup(AbilityData data, Action onSelected)
    {
        if (data == null)
        {
            if (nameText != null)
                nameText.text = string.Empty;
            if (descriptionText != null)
                descriptionText.text = string.Empty;
            if (iconImage != null)
                iconImage.sprite = null;
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.interactable = false;
            }

            return;
        }

        if (nameText != null)
            nameText.text = data.AbilityName;
        if (descriptionText != null)
            descriptionText.text = data.Description;

        if (iconImage != null)
        {
            iconImage.sprite = data.Icon;
            iconImage.enabled = data.Icon != null;
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = true;
            if (onSelected != null)
                button.onClick.AddListener(() => onSelected());
        }
    }
}
