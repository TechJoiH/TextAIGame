using System;
using System.Collections.Generic;
using UnityEngine;
using StateData.Role;
using Logic.Memory;

/// <summary>
/// 存档槽位摘要（用于列表显示）
/// </summary>
[Serializable]
public class SaveSlotHeader
{
    public string saveId;       // 唯一ID
    public string timeDisplay;  // 显示时间
    public string summary;      // 内容摘要
}

/// <summary>
/// 完整存档数据
/// </summary>
[Serializable]
public class FullSaveData
{
    public RoleState roleState;
    public MemorySnapshot memorySnapshot;
}

/// <summary>
/// 存档清单
/// </summary>
[Serializable]
public class SaveManifest
{
    public List<SaveSlotHeader> slots = new List<SaveSlotHeader>();
}

/// <summary>
/// 存档管理器（支持角色状态+记忆快照）
/// </summary>
public class GameSaveMgr : MonoSingleton<GameSaveMgr>
{
    private const string MANIFEST_NAME = "save_manifest";
    private const int MAX_SAVE_SLOTS = 20;
    
    private SaveManifest manifest;

    protected override void Awake()
    {
        base.Awake();
        LoadManifest();
    }

    private void LoadManifest()
    {
        manifest = JsonMgr.Instance.LoadData<SaveManifest>(MANIFEST_NAME, JsonType.LitJson);

        // 适配 JsonMgr：不存在文件时会返回 new SaveManifest()，但字段仍可能被序列化成 null
        if (manifest == null)
            manifest = new SaveManifest();

        if (manifest.slots == null)
            manifest.slots = new List<SaveSlotHeader>();
    }

    /// <summary>
    /// 创建新的存档节点（包含记忆快照）
    /// </summary>
    public void CreateCheckpoint(RoleState currentState, string userLastInput)
    {
        if (currentState == null) return;

        string id = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // 1) 构建完整存档数据
        var fullSave = new FullSaveData
        {
            roleState = currentState,
            memorySnapshot = MemoryManager.Instance.GetSnapshot()
        };
        
        JsonMgr.Instance.SaveData($"save_{id}", fullSave, JsonType.LitJson);

        // 2) 更新清单
        string summary = userLastInput ?? "";
        summary = summary.Length > 15 ? summary.Substring(0, 15) + "..." : summary;

        SaveSlotHeader header = new SaveSlotHeader
        {
            saveId = id,
            timeDisplay = DateTime.Now.ToString("MM/dd HH:mm"),
            summary = summary
        };

        manifest.slots.Add(header);
        
        // 限制存档数量
        while (manifest.slots.Count > MAX_SAVE_SLOTS)
        {
            manifest.slots.RemoveAt(0);
        }
        
        JsonMgr.Instance.SaveData(MANIFEST_NAME, manifest, JsonType.LitJson);

        Debug.Log($"[GameSaveMgr] 存档点已创建: {id}");
    }

    /// <summary>
    /// 加载指定存档（返回完整数据）
    /// </summary>
    public FullSaveData LoadCheckpointFull(string saveId)
    {
        if (string.IsNullOrEmpty(saveId)) return null;
        return JsonMgr.Instance.LoadData<FullSaveData>($"save_{saveId}", JsonType.LitJson);
    }

    /// <summary>
    /// 加载指定存档（仅角色状态，兼容旧接口）
    /// </summary>
    public RoleState LoadCheckpoint(string saveId)
    {
        var fullData = LoadCheckpointFull(saveId);
        return fullData?.roleState;
    }

    /// <summary>
    /// 获取所有存档点（最新的在前）
    /// </summary>
    public List<SaveSlotHeader> GetAllCheckpoints()
    {
        if (manifest == null || manifest.slots == null)
            return new List<SaveSlotHeader>();

        List<SaveSlotHeader> reversed = new List<SaveSlotHeader>(manifest.slots);
        reversed.Reverse();
        return reversed;
    }

    /// <summary>
    /// 删除指定存档
    /// </summary>
    public void DeleteCheckpoint(string saveId)
    {
        if (string.IsNullOrEmpty(saveId)) return;
        
        manifest.slots.RemoveAll(s => s.saveId == saveId);
        JsonMgr.Instance.SaveData(MANIFEST_NAME, manifest, JsonType.LitJson);
        
        // 注意：实际文件删除需要额外实现
        Debug.Log($"[GameSaveMgr] 存档已删除: {saveId}");
    }
}