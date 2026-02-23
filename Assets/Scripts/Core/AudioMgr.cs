using UnityEngine;

public class AudioMgr : MonoSingleton<AudioMgr>
{
    [Header("BGM")]
    public AudioClip bgmClip;           // 在 Inspector 拖入 BGM

    [Header("音效")]
    public AudioClip clickSfx;          // 普通点击音效
    public AudioClip panelOpenSfx;      // 打开面板的特殊点击音效
    public AudioClip pageTurnSfx;       // 翻页音效（AI生成文字后播放）

    private AudioSource bgmSource;
    private AudioSource sfxSource;      // 复用的音效播放器

    private float bgmVolume = 1f;
    private float sfxVolume = 1f;
    private bool isMusicOn = true;
    private bool isSfxOn = true;

    protected override void Awake()
    {
        base.Awake();
        // 初始化 BGM 音源
        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;

        // 初始化 SFX 音源
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
    }

    public void InitData(bool isMusicOn, bool isSfxOn, float bgmVol, float sfxVol)
    {
        this.isMusicOn = isMusicOn;
        this.isSfxOn = isSfxOn;
        this.bgmVolume = bgmVol;
        this.sfxVolume = sfxVol;
        UpdateBGMStatus();
    }

    /// <summary>
    /// 播放背景音乐（游戏启动时调用）
    /// </summary>
    public void PlayBGM()
    {
        if (bgmClip == null) return;
        if (bgmSource.clip == bgmClip && bgmSource.isPlaying) return;

        bgmSource.clip = bgmClip;
        bgmSource.volume = bgmVolume;
        bgmSource.Play();
    }

    /// <summary>
    /// 播放背景音乐（通过路径加载）
    /// </summary>
    public void PlayMusic(string path)
    {
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip == null) return;

        if (bgmSource.clip == clip) return;

        bgmSource.clip = clip;
        bgmSource.volume = bgmVolume;
        bgmSource.Play();
    }

    /// <summary>
    /// 播放普通点击音效
    /// </summary>
    public void PlayClickSfx()
    {
        PlaySfx(clickSfx);
    }

    /// <summary>
    /// 播放打开面板的特殊点击音效
    /// </summary>
    public void PlayPanelOpenSfx()
    {
        PlaySfx(panelOpenSfx);
    }

    /// <summary>
    /// 播放翻页音效（AI生成文字后）
    /// </summary>
    public void PlayPageTurnSfx()
    {
        PlaySfx(pageTurnSfx);
    }

    /// <summary>
    /// 播放指定音效
    /// </summary>
    private void PlaySfx(AudioClip clip)
    {
        if (!isSfxOn || clip == null) return;

        sfxSource.PlayOneShot(clip, sfxVolume);
    }

    /// <summary>
    /// 播放一次性音效（通过路径加载）
    /// </summary>
    public void PlaySound(string path, Vector3 pos = default(Vector3))
    {
        if (!isSfxOn) return;

        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip == null) return;

        // 创建临时 GameObject 播放声音
        GameObject soundObj = new GameObject("SFX_" + clip.name);
        soundObj.transform.position = pos;
        AudioSource source = soundObj.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = sfxVolume;
        source.Play();

        Destroy(soundObj, clip.length);
    }

    // 更改设置
    public void SetVolume(float bgm, float sfx)
    {
        bgmVolume = bgm;
        sfxVolume = sfx;
        bgmSource.volume = bgm;
    }

    public void SetMusicMute(bool isMute)
    {
        isMusicOn = !isMute;
        UpdateBGMStatus();
    }

    public void SetSfxMute(bool isMute)
    {
        isSfxOn = !isMute;
    }

    private void UpdateBGMStatus()
    {
        bgmSource.mute = !isMusicOn;
    }
}