using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 升級／波次暫停時顯示三張能力卡供玩家選擇，套用後恢復時間並通知 <see cref="WaveManager"/>。
/// </summary>
public class AbilitySelectionUI : MonoBehaviour
{
    [Tooltip("整個選擇面板根物件")]
    [SerializeField]
    private GameObject selectionPanel;

    [Tooltip("三張能力卡 UI（固定三個）")]
    [SerializeField]
    private AbilityCardUI[] cards = new AbilityCardUI[3];

    [Tooltip("所有可抽選的能力資料")]
    [SerializeField]
    private AbilityData[] abilityPool;

    [Tooltip("要套用加成的玩家數值")]
    [SerializeField]
    private PlayerStats playerStats;

    [Tooltip("選完後呼叫以進入下一波")]
    [SerializeField]
    private WaveManager waveManager;

    /// <summary>本輪抽出的三張卡對應資料（與 <see cref="cards"/> 索引對齊）。</summary>
    private readonly AbilityData[] _currentOffer = new AbilityData[3];

    /// <summary>
    /// 暫停遊戲時間、從池中隨機抽出三張不重複能力（池不足時允許重複填滿），並顯示面板。
    /// 幸運值權重預留擴充。
    /// </summary>
    public void ShowSelection()
    {
        Time.timeScale = 0f;

        // 選卡 UI 需使用滑鼠點擊
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        PickThreeOffer();

        if (cards != null)
        {
            for (int i = 0; i < cards.Length && i < _currentOffer.Length; i++)
            {
                if (cards[i] == null)
                    continue;

                int idx = i;
                cards[i].Setup(_currentOffer[i], () => OnCardSelected(idx));
            }
        }

        if (selectionPanel != null)
            selectionPanel.SetActive(true);
    }

    /// <summary>
    /// 套用所選能力、關閉面板、恢復時間並請波次管理繼續。
    /// </summary>
    /// <param name="index">0～2，對應其中一張卡</param>
    public void OnCardSelected(int index)
    {
        if (index < 0 || index >= _currentOffer.Length)
            return;

        AbilityData chosen = _currentOffer[index];
        if (chosen != null && playerStats != null)
            chosen.Apply(playerStats);

        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        Time.timeScale = 1f;

        // 回到第一人稱操作
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (waveManager != null)
            waveManager.ResumeNextWave();
    }

    /// <summary>
    /// 從 <see cref="abilityPool"/> 抽出最多三筆不重複非 null 項目；池內不足時以隨機補滿三格。
    /// 之後可在此依 <see cref="PlayerStats.Luck"/> 調整稀有能力權重。
    /// </summary>
    private void PickThreeOffer()
    {
        for (int i = 0; i < _currentOffer.Length; i++)
            _currentOffer[i] = null;

        if (abilityPool == null || abilityPool.Length == 0)
            return;

        var validIndices = new List<int>();
        for (int i = 0; i < abilityPool.Length; i++)
        {
            if (abilityPool[i] != null)
                validIndices.Add(i);
        }

        if (validIndices.Count == 0)
            return;

        // 預留：依 playerStats.Luck 調整稀有能力權重（目前僅均勻隨機）。
        var fallbackBag = new List<int>(validIndices);

        int slot = 0;
        while (slot < 3 && validIndices.Count > 0)
        {
            int pick = Random.Range(0, validIndices.Count);
            int poolIndex = validIndices[pick];
            validIndices.RemoveAt(pick);
            _currentOffer[slot] = abilityPool[poolIndex];
            slot++;
        }

        while (slot < 3 && fallbackBag.Count > 0)
        {
            int poolIndex = fallbackBag[Random.Range(0, fallbackBag.Count)];
            _currentOffer[slot] = abilityPool[poolIndex];
            slot++;
        }
    }
}
