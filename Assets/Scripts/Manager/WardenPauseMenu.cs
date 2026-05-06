using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 遊戲內暫停選單：ESC 開關面板；調整 SFX／BGM／滑鼠靈敏度；繼續或返回主選單。
/// 「繼續／主選單」請於各 Button 的 Inspector → OnClick 綁定本元件的 <see cref="Resume"/>、<see cref="ReturnToMainMenu"/>（勿在程式內 AddListener）。
/// 死亡結算顯示期間不處理暫停。
/// </summary>
public class WardenPauseMenu : MonoBehaviour
{
    private const float MinVolume = 0.001f;
    private const float MaxVolume = 1f;
    private const float MinSensitivity = 0.5f;
    private const float MaxSensitivity = 5f;

    private const float DefaultSfxLinear = 0.8f;
    private const float DefaultBgmLinear = 0.6f;
    private const float DefaultMouseSensitivity = 2f;

    private const string PrefsKeySfxLinear = "Warden_SFX_Linear";
    private const string PrefsKeyBgmLinear = "Warden_BGM_Linear";
    private const string PrefsKeyMouseSensitivity = "MouseSensitivity";

    [Header("暫停面板")]
    [Tooltip("以 Alpha（與 interactable／blocksRaycasts）控制顯示")]
    [SerializeField]
    private CanvasGroup pausePanel;

    [Header("音量與靈敏度")]
    [SerializeField]
    private Slider sfxSlider;

    [SerializeField]
    private Slider bgmSlider;

    [SerializeField]
    private Slider sensitivitySlider;

    [Tooltip("須暴露參數 SFX_Volume、BGM_Volume（對數分貝）")]
    [SerializeField]
    private AudioMixer wardenAudioMixer;

    [Header("玩家")]
    [SerializeField]
    private WardenController playerController;

    [Header("主選單")]
    [Tooltip("Build Settings 中建置索引（通常主選單為 0）")]
    [SerializeField]
    private int mainMenuSceneIndex = 0;

    [Header("阻擋暫停的狀態（可留空並於執行時自動尋找）")]
    [SerializeField]
    private WardenDeathManager deathManager;

    [Header("能力選擇")]
    [Tooltip("與 AbilitySelectionUI 的選擇面板為同一個根物件；顯示中時按 ESC 不開暫停選單")]
    [SerializeField]
    private GameObject abilitySelectionPanel;

    private bool _menuPauseActive;

    private void Awake()
    {
        if (deathManager == null)
            deathManager = Object.FindFirstObjectByType<WardenDeathManager>();

        InitializeSlidersFromPlayerPrefs();

        ApplyPausePanelVisible(false);
    }

    private void OnDestroy()
    {
        if (sfxSlider != null)
            sfxSlider.onValueChanged.RemoveListener(SetSFXVolume);
        if (bgmSlider != null)
            bgmSlider.onValueChanged.RemoveListener(SetBGMVolume);
        if (sensitivitySlider != null)
            sensitivitySlider.onValueChanged.RemoveListener(SetMouseSensitivity);
    }

    private void Update()
    {
        // 能力三選一顯示中：不處理暫停（避免與選卡操作衝突）
        if (abilitySelectionPanel != null && abilitySelectionPanel.activeSelf)
            return;

        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        if (ShouldIgnoreEscapeForBlockingPanels())
            return;

        TogglePause();
    }

    /// <summary>死亡結算中時，不處理 ESC（亦不開暫停）。</summary>
    private bool ShouldIgnoreEscapeForBlockingPanels()
    {
        return deathManager != null && deathManager.isDead;
    }

    /// <summary>是否不允許「開啟」暫停（死亡結算中）。</summary>
    private bool CannotOpenPauseMenu()
    {
        return ShouldIgnoreEscapeForBlockingPanels();
    }

    /// <summary>從 PlayerPrefs 讀取並套用到 Slider、Mixer、控制器。</summary>
    private void InitializeSlidersFromPlayerPrefs()
    {
        float sfx = Mathf.Clamp(PlayerPrefs.GetFloat(PrefsKeySfxLinear, DefaultSfxLinear), MinVolume, MaxVolume);
        float bgm = Mathf.Clamp(PlayerPrefs.GetFloat(PrefsKeyBgmLinear, DefaultBgmLinear), MinVolume, MaxVolume);
        float sens = Mathf.Clamp(PlayerPrefs.GetFloat(PrefsKeyMouseSensitivity, DefaultMouseSensitivity), MinSensitivity, MaxSensitivity);

        if (sfxSlider != null)
        {
            sfxSlider.minValue = MinVolume;
            sfxSlider.maxValue = MaxVolume;
            sfxSlider.wholeNumbers = false;
            sfxSlider.onValueChanged.RemoveListener(SetSFXVolume);
            sfxSlider.SetValueWithoutNotify(sfx);
            sfxSlider.onValueChanged.AddListener(SetSFXVolume);
        }
        SetSFXVolume(sfx);

        if (bgmSlider != null)
        {
            bgmSlider.minValue = MinVolume;
            bgmSlider.maxValue = MaxVolume;
            bgmSlider.wholeNumbers = false;
            bgmSlider.onValueChanged.RemoveListener(SetBGMVolume);
            bgmSlider.SetValueWithoutNotify(bgm);
            bgmSlider.onValueChanged.AddListener(SetBGMVolume);
        }
        SetBGMVolume(bgm);

        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = MinSensitivity;
            sensitivitySlider.maxValue = MaxSensitivity;
            sensitivitySlider.wholeNumbers = false;
            sensitivitySlider.onValueChanged.RemoveListener(SetMouseSensitivity);
            sensitivitySlider.SetValueWithoutNotify(sens);
            sensitivitySlider.onValueChanged.AddListener(SetMouseSensitivity);
        }
        SetMouseSensitivity(sens);
    }

    /// <summary>ESC：若已由本選單暫停則繼續，否則在允許時開啟暫停。</summary>
    public void TogglePause()
    {
        if (_menuPauseActive)
        {
            Resume();
            return;
        }

        if (CannotOpenPauseMenu())
            return;

        _menuPauseActive = true;
        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (playerController != null)
            playerController.enabled = false;

        ApplyPausePanelVisible(true);
    }

    /// <summary>關閉暫停、恢復時間與操作。建議由 Resume 按鈕的 OnClick 在 Inspector 綁定至此方法。</summary>
    public void Resume()
    {
        if (!_menuPauseActive)
            return;

        _menuPauseActive = false;
        Time.timeScale = 1f;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (playerController != null)
            playerController.enabled = true;

        ApplyPausePanelVisible(false);
    }

    /// <summary>恢復時間比例後載入主選單場景。建議由 MainMenu 按鈕的 OnClick 在 Inspector 綁定至此方法。</summary>
    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        _menuPauseActive = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (playerController != null)
            playerController.enabled = true;

        ApplyPausePanelVisible(false);

        if (mainMenuSceneIndex < 0 || mainMenuSceneIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError("[Pause] mainMenuSceneIndex 超出建置清單範圍: " + mainMenuSceneIndex);
            return;
        }

        SceneManager.LoadScene(mainMenuSceneIndex, LoadSceneMode.Single);
    }

    /// <summary>SFX 線性音量寫入 PlayerPrefs 並以 dB 寫入 Mixer。</summary>
    public void SetSFXVolume(float value)
    {
        float v = Mathf.Clamp(value, MinVolume, MaxVolume);
        PlayerPrefs.SetFloat(PrefsKeySfxLinear, v);
        PlayerPrefs.Save();

        if (wardenAudioMixer != null)
            wardenAudioMixer.SetFloat("SFX_Volume", Mathf.Log10(v) * 20f);
    }

    /// <summary>BGM 線性音量寫入 PlayerPrefs 並以 dB 寫入 Mixer。</summary>
    public void SetBGMVolume(float value)
    {
        float v = Mathf.Clamp(value, MinVolume, MaxVolume);
        PlayerPrefs.SetFloat(PrefsKeyBgmLinear, v);
        PlayerPrefs.Save();

        if (wardenAudioMixer != null)
            wardenAudioMixer.SetFloat("BGM_Volume", Mathf.Log10(v) * 20f);
    }

    /// <summary>滑鼠靈敏度寫入 PlayerPrefs 並套用到角色控制器。</summary>
    public void SetMouseSensitivity(float value)
    {
        float v = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
        PlayerPrefs.SetFloat(PrefsKeyMouseSensitivity, v);
        PlayerPrefs.Save();

        if (playerController != null)
            playerController.MouseSensitivity = v;
    }

    private void ApplyPausePanelVisible(bool visible)
    {
        if (pausePanel == null)
            return;

        pausePanel.alpha = visible ? 1f : 0f;
        pausePanel.interactable = visible;
        pausePanel.blocksRaycasts = visible;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        mainMenuSceneIndex = Mathf.Max(0, mainMenuSceneIndex);
    }
#endif
}
