using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;         

/// <summary>
/// 场景管理器
/// 职责：提供同步/异步加载接口，并抛出进度回调供 UI 显示进度条
/// </summary>
public class SceneMgr : MonoSingleton<SceneMgr>
{
    /// <summary>
    /// 同步加载场景 (会卡顿，只适用于极小的场景或切回主菜单)
    /// </summary>
    /// <param name="sceneName">场景名</param>
    /// <param name="callBack">加载结束后的回调</param>
    public void LoadScene(string sceneName, UnityAction callBack = null)
    {
        SceneManager.LoadScene(sceneName);
        // 同步加载是立即执行的，所以直接调用回调
        callBack?.Invoke();
    }

    /// <summary>
    /// 异步加载场景 (核心功能)
    /// </summary>
    /// <param name="sceneName">场景名</param>
    /// <param name="onProgress">进度回调 (0.0 - 1.0)，UI面板监听这个来更新进度条</param>
    /// <param name="onComplete">加载完成回调，可以在这里初始化新场景的数据</param>
    public void LoadSceneAsync(string sceneName, UnityAction<float> onProgress = null, UnityAction onComplete = null)
    {
        StartCoroutine(DoLoadSceneAsync(sceneName, onProgress, onComplete));
    }

    /// <summary>
    /// 协程处理异步加载逻辑
    /// </summary>
    private IEnumerator DoLoadSceneAsync(string sceneName, UnityAction<float> onProgress, UnityAction onComplete)
    {
        AsyncOperation ao = SceneManager.LoadSceneAsync(sceneName);
        ao.allowSceneActivation = false; // 加载完不立刻跳转

        float targetProgress = 0;
        float visualProgress = 0;

        while (ao.progress < 0.9f)
        {
            targetProgress = ao.progress;
            // 让视觉进度慢慢追赶真实进度
            while (visualProgress < targetProgress)
            {
                visualProgress += Time.deltaTime; // 模拟速度
                onProgress?.Invoke(visualProgress);
                yield return null;
            }
            yield return null;
        }

        // 此时真实进度已经是 0.9 了，让视觉进度跑完最后一段
        targetProgress = 1f;
        while (visualProgress < targetProgress)
        {
            visualProgress += Time.deltaTime;
            onProgress?.Invoke(visualProgress);
            yield return null;
        }

        // 允许跳转
        ao.allowSceneActivation = true;

        // 确保进度条显示满
        onProgress?.Invoke(1.0f);

        yield return null; // 等待场景初始化
        onComplete?.Invoke();
    }

    /// <summary>
    /// 获取当前场景名字
    /// </summary>
    public string GetActiveSceneName()
    {
        return SceneManager.GetActiveScene().name;
    }
}