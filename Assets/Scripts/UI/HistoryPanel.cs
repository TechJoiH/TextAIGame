using System.Collections.Generic;
using Logic.Memory;
using UnityEngine;
using UnityEngine.UI;

public class HistoryPanel : BasePanel
{
    [Header("Refs")]
    public Transform contentRoot;       // ScrollView 的 Content
    public GameObject slotPrefab;       // 带 HistorySlotItem 的预制体
    public Button closeBtn;

    private bool inited;

    public override void Init()
    {
        if (inited) return;
        inited = true;

        if (closeBtn != null)
            closeBtn.onClick.AddListener(() =>
            {
                if (AudioMgr.Instance != null)
                    AudioMgr.Instance.PlayClickSfx();

                HideMe();
            });
    }

    protected override void OnShowAnimation()
    {
        base.OnShowAnimation();
        RefreshList();
    }

    private void RefreshList()
    {
        Debug.Log($"[HistoryPanel] RefreshList 开始, contentRoot: {contentRoot != null}, slotPrefab: {slotPrefab != null}");
        
        if (contentRoot == null)
        {
            Debug.LogError("[HistoryPanel] contentRoot 为空！请在 Inspector 中绑定");
            return;
        }
        
        if (slotPrefab == null)
        {
            Debug.LogError("[HistoryPanel] slotPrefab 为空！请在 Inspector 中绑定");
            return;
        }

        // 清除旧项
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);

        int createdCount = 0;
        createdCount += AppendMemoryEntries();

        var slots = GameSaveMgr.Instance.GetAllCheckpoints();
        Debug.Log($"[HistoryPanel] 获取到 {slots.Count} 个存档");

        for (int i = 0; i < slots.Count; i++)
        {
            var header = slots[i];
            Debug.Log($"[HistoryPanel] 创建槽位 {i}: id={header.saveId}, time={header.timeDisplay}, summary={header.summary}");

            GameObject obj = Instantiate(slotPrefab, contentRoot, false);
            var item = obj.GetComponent<HistorySlotItem>();
            if (item != null)
            {
                item.Bind(header.saveId, $"回溯点 {header.timeDisplay}", header.summary, OnSlotClicked);
                createdCount++;
            }
            else
            {
                Debug.LogError("[HistoryPanel] slotPrefab 缺少 HistorySlotItem 组件！");
            }
        }

        if (createdCount == 0)
            CreateReadonlyEntry("暂无记录", "当前还没有可回看的交互或回溯点。");

        Debug.Log($"[HistoryPanel] RefreshList 完成，共创建 {createdCount} 个条目");
    }

    private void OnSlotClicked(string saveId)
    {
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayClickSfx();

        var fullData = GameSaveMgr.Instance.LoadCheckpointFull(saveId);
        if (fullData == null || fullData.roleState == null)
        {
            Debug.LogError($"[HistoryPanel] 存档读取失败: {saveId}");
            return;
        }

        if (GameLoop.Instance != null)
            GameLoop.Instance.LoadGame(
                fullData.roleState,
                fullData.environmentState,
                fullData.memorySnapshot,
                fullData.knowledgeGraphSnapshot);

        HideMe();

        Debug.Log($"[HistoryPanel] 已回溯至: {saveId}");
    }

    private int AppendMemoryEntries()
    {
        int createdCount = 0;
        var memoryManager = MemoryManager.Instance;
        if (memoryManager == null)
            return createdCount;

        List<LongTermMemory> longTermMemories = memoryManager.GetLongTermMemoryEntries();
        int longTermStart = Mathf.Max(0, longTermMemories.Count - 2);
        for (int i = longTermStart; i < longTermMemories.Count; i++)
        {
            LongTermMemory memory = longTermMemories[i];
            if (memory == null || string.IsNullOrWhiteSpace(memory.summary))
                continue;

            CreateReadonlyEntry("记忆摘要", memory.summary);
            createdCount++;
        }

        List<DialogueEntry> dialogues = memoryManager.GetRecentDialogueEntries();
        int shortTermStart = Mathf.Max(0, dialogues.Count - 6);
        for (int i = shortTermStart; i < dialogues.Count; i++)
        {
            DialogueEntry entry = dialogues[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.content))
                continue;

            string label = entry.role == "user" ? "玩家发言" : "叙事回应";
            CreateReadonlyEntry(label, TrimSummary(entry.content, 36));
            createdCount++;
        }

        return createdCount;
    }

    private void CreateReadonlyEntry(string title, string summary)
    {
        GameObject obj = Instantiate(slotPrefab, contentRoot, false);
        var item = obj.GetComponent<HistorySlotItem>();
        if (item != null)
            item.Bind(string.Empty, title, summary, null);
    }

    private static string TrimSummary(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            return value ?? string.Empty;

        return value.Substring(0, maxLength) + "...";
    }
}
