using UnityEngine;
using System.Collections;

public class AudioMgr : MonoSingleton<AudioMgr>
{
    private AudioSource bgmSource;
    private float bgmVolume = 1f;
    private float sfxVolume = 1f;
    private bool isMusicOn = true;

    protected override void Awake()
    {
        base.Awake();
        // 初始化 BGM 音源
        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
    }

    public void InitData(bool isMusicOn, float bgmVol, float sfxVol)
    {
        this.isMusicOn = isMusicOn;
        this.bgmVolume = bgmVol;
        this.sfxVolume = sfxVol;
        UpdateBGMStatus();
    }

    // 播放背景音乐
    public void PlayMusic(string path)
    {
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip == null) return;

        if (bgmSource.clip == clip) return; // 已经是这首了

        bgmSource.clip = clip;
        bgmSource.volume = bgmVolume;
        bgmSource.Play();
    }

    // 播放一次性音效 (你的 PlaySound 逻辑优化版)
    public void PlaySound(string path, Vector3 pos = default(Vector3))
    {
        if (!isMusicOn) return;

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

    public void SetMute(bool isMute)
    {
        isMusicOn = !isMute;
        UpdateBGMStatus();
    }

    private void UpdateBGMStatus()
    {
        bgmSource.mute = !isMusicOn;
    }
}