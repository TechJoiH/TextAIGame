using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using StateData.Role;
using StateData.Environment;
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
    public EnvironmentState environmentState;
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
    private bool manifestLoaded = false;

    protected override void Awake()
    {
        base.Awake();
        LoadManifest();
    }

    private void LoadManifest()
    {
        string path = Application.persistentDataPath + "/" + MANIFEST_NAME + ".json";
        bool fileExists = File.Exists(path);
        Debug.Log($"[GameSaveMgr] 加载清单: {path}, 文件存在: {fileExists}");
        
        if (fileExists)
        {
            try
            {
                string jsonStr = File.ReadAllText(path);
                Debug.Log($"[GameSaveMgr] 清单JSON内容: {jsonStr.Substring(0, Mathf.Min(200, jsonStr.Length))}...");
                manifest = LitJson.JsonMapper.ToObject<SaveManifest>(jsonStr);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameSaveMgr] 清单解析失败: {e.Message}");
                manifest = null;
            }
        }

        if (manifest == null)
            manifest = new SaveManifest();

        if (manifest.slots == null)
            manifest.slots = new List<SaveSlotHeader>();
            
        manifestLoaded = true;
        Debug.Log($"[GameSaveMgr] 清单加载完成，存档数量: {manifest.slots.Count}");
    }

    /// <summary>
    /// 创建新的存档节点（包含环境状态和记忆快照）
    /// </summary>
    public void CreateCheckpoint(RoleState currentState, EnvironmentState envState, string userLastInput)
    {
        if (currentState == null)
        {
            Debug.LogError("[GameSaveMgr] currentState 为空，无法创建存档");
            return;
        }

        string id = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        var fullSave = new FullSaveData
        {
            roleState = currentState,
            environmentState = envState,
            memorySnapshot = MemoryManager.Instance.GetSnapshot()
        };
        
        JsonMgr.Instance.SaveData($"save_{id}", fullSave, JsonType.LitJson);
        
        string savePath = Application.persistentDataPath + $"/save_{id}.json";
        Debug.Log($"[GameSaveMgr] 存档数据已保存: {savePath}, 文件存在: {File.Exists(savePath)}");

        string summary = userLastInput ?? "";
        summary = summary.Length > 15 ? summary.Substring(0, 15) + "..." : summary;

        SaveSlotHeader header = new SaveSlotHeader
        {
            saveId = id,
            timeDisplay = DateTime.Now.ToString("MM/dd HH:mm"),
            summary = summary
        };

        manifest.slots.Add(header);
        
        while (manifest.slots.Count > MAX_SAVE_SLOTS)
        {
            manifest.slots.RemoveAt(0);
        }
        
        JsonMgr.Instance.SaveData(MANIFEST_NAME, manifest, JsonType.LitJson);
        
        Debug.Log($"[GameSaveMgr] 清单已更新，当前存档数: {manifest.slots.Count}");
    }

    public void CreateCheckpoint(RoleState currentState, string userLastInput)
    {
        CreateCheckpoint(currentState, null, userLastInput);
    }

    public FullSaveData LoadCheckpointFull(string saveId)
    {
        if (string.IsNullOrEmpty(saveId)) return null;
        return JsonMgr.Instance.LoadData<FullSaveData>($"save_{saveId}", JsonType.LitJson);
    }

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
        // 确保清单已加载
        if (!manifestLoaded || manifest == null)
        {
            LoadManifest();
        }
        
        if (manifest == null || manifest.slots == null)
        {
            Debug.LogWarning("[GameSaveMgr] GetAllCheckpoints: 清单为空");
            return new List<SaveSlotHeader>();
        }

        List<SaveSlotHeader> reversed = new List<SaveSlotHeader>(manifest.slots);
        reversed.Reverse();
        
        Debug.Log($"[GameSaveMgr] GetAllCheckpoints 返回 {reversed.Count} 个存档");
        return reversed;
    }

    public void DeleteCheckpoint(string saveId)
    {
        if (string.IsNullOrEmpty(saveId)) return;
        
        manifest.slots.RemoveAll(s => s.saveId == saveId);
        JsonMgr.Instance.SaveData(MANIFEST_NAME, manifest, JsonType.LitJson);
        
        Debug.Log($"[GameSaveMgr] 存档已删除: {saveId}");
    }
}