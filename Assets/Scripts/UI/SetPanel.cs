using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SetPanel : BasePanel
{
    [Header("Buttons")]
    public Button btnClose;

    // 新增：返回开始界面按钮（仅从游戏界面打开时显示）
    public Button btnBackToBegin;

    [Header("Toggles")]
    public Toggle toggleMusic;   // true = 开启声音
    public Toggle toggleSound;   // true = 开启音效(此项目 AudioMgr 用同一个 isMusicOn 控制 PlaySound)

    [Header("Sliders")]
    public Slider sliderMusic;   // 0~1
    public Slider sliderSound;   // 0~1

    private const string PrefMusicOn = "SET_MUSIC_ON";
    private const string PrefSoundOn = "SET_SOUND_ON";
    private const string PrefMusicVol = "SET_MUSIC_VOL";
    private const string PrefSoundVol = "SET_SOUND_VOL";

    private bool inited;

    // 新增：记录打开来源（决定是否显示返回按钮）
    private bool openedFromGame;

    public override void Init()
    {
        if (inited) return;
        inited = true;

        BindButton(btnClose, OnClickClose);
        BindButton(btnBackToBegin, OnClickBackToBegin);

        BindToggle(toggleMusic, OnToggleMusic);
        BindToggle(toggleSound, OnToggleSound);

        BindSlider(sliderMusic, OnMusicVolumeChanged);
        BindSlider(sliderSound, OnSoundVolumeChanged);

        LoadAndApplyToUI();
        ApplyToAudioMgr();
        RefreshBackButtonVisible();
    }

    protected override void OnShowAnimation()
    {
        base.OnShowAnimation();

        // 每次打开都刷新（因为 Init 只会执行一次，而面板会被缓存复用）
        RefreshBackButtonVisible();

        // 可选：确保当前音量/静音状态一致
        ApplyToAudioMgr();
    }

    /// <summary>
    /// 由外部在打开面板后调用：标记是否从游戏界面打开。
    /// - true：显示"返回开始界面"
    /// - false：隐藏该按钮
    /// </summary>
    public void SetOpenFromGame(bool fromGame)
    {
        openedFromGame = fromGame;
        RefreshBackButtonVisible();
    }

    private void RefreshBackButtonVisible()
    {
        if (btnBackToBegin != null)
            btnBackToBegin.gameObject.SetActive(openedFromGame);
    }

    private static void BindButton(Button btn, UnityEngine.Events.UnityAction onClick)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(onClick);
    }

    private static void BindToggle(Toggle toggle, UnityEngine.Events.UnityAction<bool> onValueChanged)
    {
        if (toggle == null) return;
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(onValueChanged);
    }

    private static void BindSlider(Slider slider, UnityEngine.Events.UnityAction<float> onValueChanged)
    {
        if (slider == null) return;
        slider.onValueChanged.RemoveAllListeners();
        slider.onValueChanged.AddListener(onValueChanged);
    }

    private void OnClickClose()
    {
        // 播放普通点击音效
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayClickSfx();

        HideMe();
    }

    private void OnClickBackToBegin()
    {
        // 播放打开面板的特殊音效
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayPanelOpenSfx();

        // 仅在 openedFromGame=true 时按钮才会显示；这里再做一次保护
        if (!openedFromGame)
        {
            HideMe();
            return;
        }

        // 1) 先关掉游戏面板
        UIMgr.Instance.HidePanel<MainGamePanel>(isDestroy: false);

        // 2) 打开开始面板
        UIMgr.Instance.ShowPanel<BeginPanel>();

        // 3) 重置 GameLoop，使得再次点击"开始"能重新进游戏
        if (GameLoop.Instance != null)
            GameLoop.Instance.ReturnToBegin();

        // 4) 关闭设置自己
        HideMe();
    }

    private void OnToggleMusic(bool isOn)
    {
        // 播放普通点击音效
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayClickSfx();

        SavePrefs();
        ApplyToAudioMgr();
    }

    private void OnToggleSound(bool isOn)
    {
        // 播放普通点击音效
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayClickSfx();

        SavePrefs();
        ApplyToAudioMgr();
    }

    private void OnMusicVolumeChanged(float value)
    {
        SavePrefs();
        ApplyToAudioMgr();
    }

    private void OnSoundVolumeChanged(float value)
    {
        SavePrefs();
        ApplyToAudioMgr();
    }

    private void LoadAndApplyToUI()
    {
        bool musicOn = PlayerPrefs.GetInt(PrefMusicOn, 1) == 1;
        bool soundOn = PlayerPrefs.GetInt(PrefSoundOn, 1) == 1;

        float musicVol = PlayerPrefs.GetFloat(PrefMusicVol, 1f);
        float soundVol = PlayerPrefs.GetFloat(PrefSoundVol, 1f);

        if (toggleMusic != null) toggleMusic.isOn = musicOn;
        if (toggleSound != null) toggleSound.isOn = soundOn;

        if (sliderMusic != null) sliderMusic.value = Mathf.Clamp01(musicVol);
        if (sliderSound != null) sliderSound.value = Mathf.Clamp01(soundVol);
    }

    private void SavePrefs()
    {
        if (toggleMusic != null) PlayerPrefs.SetInt(PrefMusicOn, toggleMusic.isOn ? 1 : 0);
        if (toggleSound != null) PlayerPrefs.SetInt(PrefSoundOn, toggleSound.isOn ? 1 : 0);

        if (sliderMusic != null) PlayerPrefs.SetFloat(PrefMusicVol, Mathf.Clamp01(sliderMusic.value));
        if (sliderSound != null) PlayerPrefs.SetFloat(PrefSoundVol, Mathf.Clamp01(sliderSound.value));

        PlayerPrefs.Save();
    }

    private void ApplyToAudioMgr()
    {
        bool musicOn = toggleMusic == null || toggleMusic.isOn;
        bool soundOn = toggleSound == null || toggleSound.isOn;

        float musicVol = sliderMusic != null ? Mathf.Clamp01(sliderMusic.value) : 1f;
        float soundVol = sliderSound != null ? Mathf.Clamp01(sliderSound.value) : 1f;

        if (AudioMgr.Instance != null)
        {
            AudioMgr.Instance.SetVolume(musicVol, soundVol);
            AudioMgr.Instance.SetMusicMute(!musicOn);
            AudioMgr.Instance.SetSfxMute(!soundOn);
        }
    }
}
