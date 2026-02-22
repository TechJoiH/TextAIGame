using UnityEngine;
using UnityEngine.UI;

public class HistoryPanel : BasePanel
{
    [Header("Refs")]
    public Transform contentRoot;       // ScrollView 的 Content
    public GameObject slotPrefab;       // 挂 HistorySlotItem 的预制体
    public Button closeBtn;

    private bool inited;

    public override void Init()
    {
        if (inited) return;
        inited = true;

        if (closeBtn != null)
            closeBtn.onClick.AddListener(() => HideMe());
    }

    protected override void OnShowAnimation()
    {
        base.OnShowAnimation();
        RefreshList();
    }

    private void RefreshList()
    {
        if (contentRoot == null || slotPrefab == null)
            return;

        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);

        var slots = GameSaveMgr.Instance.GetAllCheckpoints();
        for (int i = 0; i < slots.Count; i++)
        {
            var header = slots[i];

            GameObject obj = Instantiate(slotPrefab, contentRoot, false);
            var item = obj.GetComponent<HistorySlotItem>();
            if (item != null)
            {
                item.Bind(header.saveId, header.timeDisplay, header.summary, OnSlotClicked);
            }
            else
            {
                Debug.LogError("[HistoryPanel] slotPrefab 上缺少 HistorySlotItem 组件。");
            }
        }
    }

    private void OnSlotClicked(string saveId)
    {
        var state = GameSaveMgr.Instance.LoadCheckpoint(saveId);
        if (state == null)
        {
            Debug.LogError($"[HistoryPanel] 存档读取失败: {saveId}");
            return;
        }

        if (GameLoop.Instance != null)
            GameLoop.Instance.LoadGame(state);

        HideMe();

        Debug.Log($"[HistoryPanel] 已回溯至: {saveId}");
    }
}