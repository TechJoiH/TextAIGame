using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D; // 引入图集支持
using UnityEngine.Events; // 引入委托支持

/// <summary>
/// 资源管理器 (核心框架层)
/// 职责：统一管理 Resources 文件夹下的所有资源加载与卸载，提供缓存机制。
/// </summary>
public class ResMgr : MonoSingleton<ResMgr>
{
    // ==========================================
    // 1. 缓存容器 (核心资产)
    // ==========================================

    // 通用资源缓存 (Key: 路径, Value: 资源对象)
    // 涵盖: GameObject(预制体), AudioClip, TextAsset, ScriptableObject, Texture 等
    private Dictionary<string, Object> assetCache = new Dictionary<string, Object>();

    // 图集专用缓存 (专门服务于 UI 图片)
    private Dictionary<string, SpriteAtlas> atlasCache = new Dictionary<string, SpriteAtlas>();


    // ==========================================
    // 2. 同步加载接口 (适用于小资源，如音效、配置)
    // ==========================================

    /// <summary>
    /// 同步加载资源
    /// </summary>
    /// <typeparam name="T">资源类型 (如 GameObject, AudioClip)</typeparam>
    /// <param name="path">Resources下的相对路径</param>
    /// <returns>加载成功的资源，失败返回 null</returns>
    public T Load<T>(string path) where T : Object
    {
        // A. 先检查缓存
        if (assetCache.ContainsKey(path))
        {
            // 检查缓存中的类型是否匹配（防止同名不同类型资源冲突）
            if (assetCache[path] is T result)
                return result;
            else
            {
                Debug.LogError($"[ResMgr] 类型不匹配: 路径[{path}] 缓存是[{assetCache[path].GetType()}] 但请求是[{typeof(T)}]");
                return null;
            }
        }

        // B. 缓存没有，执行硬盘读取
        T res = Resources.Load<T>(path);

        // C. 加载成功，存入缓存
        if (res != null)
        {
            assetCache.Add(path, res);
        }
        else
        {
            Debug.LogError($"[ResMgr] 同步加载失败，路径不存在或资源损坏: {path}");
        }

        return res;
    }


    // ==========================================
    // 3. 异步加载接口 (适用于大资源，如UI面板、特效)
    // ==========================================

    /// <summary>
    /// 异步加载资源 (防止主线程卡顿)
    /// </summary>
    /// <param name="callback">资源加载完成后的回调函数</param>
    public void LoadAsync<T>(string path, UnityAction<T> callback) where T : Object
    {
        // A. 如果缓存里已经有了，直接回调（伪异步）
        if (assetCache.ContainsKey(path))
        {
            // 为了逻辑统一，通常建议延后一帧回调，但直接回调响应更快，视项目需求而定
            // 这里直接回调：
            if (assetCache[path] is T result)
                callback?.Invoke(result);
            else
                Debug.LogError($"[ResMgr] 异步加载类型错乱: {path}");

            return;
        }

        // B. 开启协程进行后台加载
        StartCoroutine(DoLoadAsync(path, callback));
    }

    // 具体的协程逻辑
    private IEnumerator DoLoadAsync<T>(string path, UnityAction<T> callback) where T : Object
    {
        // 发起异步请求
        ResourceRequest request = Resources.LoadAsync<T>(path);

        // 等待加载完成 (不会卡死主线程)
        yield return request;

        // 加载完成，检查结果
        if (request.asset != null && request.asset is T res)
        {
            // 再次检查缓存 (防止在等待过程中被其他逻辑同步加载了)
            if (!assetCache.ContainsKey(path))
            {
                assetCache.Add(path, res);
            }
            // 执行回调，通知调用者“货到了”
            callback?.Invoke(res);
        }
        else
        {
            Debug.LogError($"[ResMgr] 异步加载彻底失败: {path}");
            callback?.Invoke(null); // 即使失败也要回调，防止逻辑断链
        }
    }


    // ==========================================
    // 4. 图集与图片加载 (UI优化专用)
    // ==========================================

    /// <summary>
    /// 从图集中获取单张 Sprite
    /// </summary>
    /// <param name="atlasPath">图集在Resources下的路径</param>
    /// <param name="spriteName">图集里的图片名字</param>
    public Sprite LoadSpriteFromAtlas(string atlasPath, string spriteName)
    {
        // 1. 获取图集 (复用上面的 Load 逻辑，自动走缓存)
        SpriteAtlas atlas = Load<SpriteAtlas>(atlasPath); // 这里巧妙复用了 Load<T>

        // 2. 如果图集加载成功，取图片
        if (atlas != null)
        {
            return atlas.GetSprite(spriteName);
        }

        return null;
    }


    // ==========================================
    // 5. 内存管理 (切换场景时调用)
    // ==========================================

    /// <summary>
    /// 清空所有缓存，释放内存
    /// </summary>
    public void Clear()
    {
        // 1. 清空字典引用
        assetCache.Clear();
        atlasCache.Clear();

        // 2. 卸载未使用的资源 (Unity API)
        Resources.UnloadUnusedAssets();

        // 3. 强制垃圾回收 
        System.GC.Collect();

        Debug.Log("[ResMgr] 缓存已清空，内存已释放。");
    }
}