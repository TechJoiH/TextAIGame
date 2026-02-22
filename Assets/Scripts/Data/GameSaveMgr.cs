using System;
using System.Collections.Generic;
using UnityEngine;
using StateData.Role;

/// <summary>
/// 存档数据摘要（用于在列表中显示）
/// </summary>
[Serializable]
public class SaveSlotHeader
{
    public string saveId;       // 唯一ID (如 timestamp)
    public string timeDisplay;  // 显示时间 (如 "12/31 12:30")
    public string summary;      // 剧情摘要 (用户输入的最后一句)
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
/// P0 & P3: 存档与回档管理器（基于 JsonMgr）
/// </summary>
public class GameSaveMgr : MonoSingleton<GameSaveMgr>
{
    private const string MANIFEST_NAME = "save_manifest";
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
    /// 创建新的存档节点（建议在回合结算完成后调用）
    /// </summary>
    public void CreateCheckpoint(RoleState currentState, string userLastInput)
    {
        if (currentState == null) return;

        string id = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // 1) 保存实际数据
        JsonMgr.Instance.SaveData($"save_{id}", currentState, JsonType.LitJson);

        // 2) 更新清单
        string summary = userLastInput ?? "";
        summary = summary.Length > 10 ? summary.Substring(0, 10) + "..." : summary;

        SaveSlotHeader header = new SaveSlotHeader
        {
            saveId = id,
            timeDisplay = DateTime.Now.ToString("MM/dd HH:mm"),
            summary = summary
        };

        manifest.slots.Add(header);
        JsonMgr.Instance.SaveData(MANIFEST_NAME, manifest, JsonType.LitJson);

        Debug.Log($"[GameSaveMgr] 存档点已创建: {id}");
    }

    /// <summary>
    /// 加载指定存档
    /// </summary>
    public RoleState LoadCheckpoint(string saveId)
    {
        if (string.IsNullOrEmpty(saveId)) return null;
        return JsonMgr.Instance.LoadData<RoleState>($"save_{saveId}", JsonType.LitJson);
    }

    /// <summary>
    /// 获取所有存档点（最新的在最上面）
    /// </summary>
    public List<SaveSlotHeader> GetAllCheckpoints()
    {
        if (manifest == null || manifest.slots == null)
            return new List<SaveSlotHeader>();

        List<SaveSlotHeader> reversed = new List<SaveSlotHeader>(manifest.slots);
        reversed.Reverse();
        return reversed;
    }
}