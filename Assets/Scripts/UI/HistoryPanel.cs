using System.Collections.Generic;
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
                item.Bind(header.saveId, header.timeDisplay, header.summary, OnSlotClicked);
            }
            else
            {
                Debug.LogError("[HistoryPanel] slotPrefab 缺少 HistorySlotItem 组件！");
            }
        }
        
        Debug.Log($"[HistoryPanel] RefreshList 完成，共创建 {slots.Count} 个槽位");
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
            GameLoop.Instance.LoadGame(fullData.roleState, fullData.environmentState, fullData.memorySnapshot);

        HideMe();

        Debug.Log($"[HistoryPanel] 已回溯至: {saveId}");
    }
}