using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Data.KnowledgeGraph;
using Logic.GraphRAG;

public class KnowledgeGraphPanel : BasePanel
{
    [Header("UI References")]
    public Button closeBtn;
    public Transform contentRoot;           // ScrollView Content
    public GameObject entityItemPrefab;     // 已解锁实体条目预制体
    public GameObject lockedEntityItemPrefab; // 未解锁实体条目预制体

    [Header("Filter Buttons")]
    public Button btnAll;
    public Button btnBeast;
    public Button btnHerb;
    public Button btnLocation;

    [Header("Detail Panel")]
    public GameObject detailPanel;
    public TMP_Text detailTitle;
    public TMP_Text detailType;
    public TMP_Text detailSource;
    public TMP_Text detailDescription;
    public TMP_Text detailRelations;
    public Button detailCloseBtn;

    [Header("Stats")]
    public TMP_Text statsText;

    private bool inited;
    private EntityType? currentFilter = null;

    public override void Init()
    {
        if (inited) return;
        inited = true;

        closeBtn?.onClick.AddListener(() =>
        {
            AudioMgr.Instance?.PlayClickSfx();
            HideMe();
        });

        detailCloseBtn?.onClick.AddListener(() =>
        {
            AudioMgr.Instance?.PlayClickSfx();
            detailPanel?.SetActive(false);
        });

        // 筛选按钮
        btnAll?.onClick.AddListener(() => SetFilter(null));
        btnBeast?.onClick.AddListener(() => SetFilter(EntityType.Beast));
        btnHerb?.onClick.AddListener(() => SetFilter(EntityType.Herb));
        btnLocation?.onClick.AddListener(() => SetFilter(EntityType.Location));

        detailPanel?.SetActive(false);
    }

    protected override void OnShowAnimation()
    {
        base.OnShowAnimation();
        RefreshList();
        UpdateStats();
    }

    private void SetFilter(EntityType? type)
    {
        AudioMgr.Instance?.PlayClickSfx();
        currentFilter = type;
        RefreshList();
    }

    private void RefreshList()
    {
        if (contentRoot == null || entityItemPrefab == null) return;

        // 清除旧项
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);

        // 获取所有实体（不管是否解锁）
        List<KnowledgeEntity> entities = GetAllEntities();

        // 创建UI项
        foreach (var entity in entities)
        {
            if (entity.isDiscovered)
            {
                // 已解锁：显示正常预制体
                CreateEntityItem(entity);
            }
            else
            {
                // 未解锁：显示锁定预制体
                CreateLockedEntityItem(entity);
            }
        }
    }

    /// <summary>
    /// 获取所有实体（根据筛选条件）
    /// </summary>
    private List<KnowledgeEntity> GetAllEntities()
    {
        if (currentFilter.HasValue)
        {
            // 按类型获取所有实体（包括未解锁的）
            return GraphRAGManager.Instance.GetEntitiesByType(currentFilter.Value, false);
        }
        else
        {
            // 获取全部实体
            return GraphRAGManager.Instance.GetAllEntities();
        }
    }

    private void CreateEntityItem(KnowledgeEntity entity)
    {
        GameObject obj = Instantiate(entityItemPrefab, contentRoot, false);
        
        // 获取子对象中的 TMP_Text 组件
        var nameText = obj.GetComponentInChildren<TMP_Text>();
        var button = obj.GetComponent<Button>();

        if (nameText != null)
            nameText.text = entity.name;

        // 点击显示详情
        button?.onClick.AddListener(() => ShowEntityDetail(entity));
    }

    /// <summary>
    /// 创建未解锁的实体条目
    /// </summary>
    private void CreateLockedEntityItem(KnowledgeEntity entity)
    {
        // 使用未解锁预制体，如果没有设置则用默认预制体
        GameObject prefab = lockedEntityItemPrefab != null ? lockedEntityItemPrefab : entityItemPrefab;
        GameObject obj = Instantiate(prefab, contentRoot, false);

        // 如果使用的是锁定预制体，不需要设置文字（预制体自带锁定样式）
        if (lockedEntityItemPrefab != null)
        {
            // 可选：禁用按钮点击
            var btn = obj.GetComponent<Button>();
            if (btn != null) btn.interactable = false;
        }
        else
        {
            // 如果没有锁定预制体，使用默认预制体但显示为锁定状态
            var nameText = obj.transform.Find("NameText")?.GetComponent<TMP_Text>();
            var typeText = obj.transform.Find("TypeText")?.GetComponent<TMP_Text>();
            
            if (nameText != null)
                nameText.text = "???";
            if (typeText != null)
                typeText.text = "未解锁";

            var btn = obj.GetComponent<Button>();
            if (btn != null) btn.interactable = false;
        }
    }

    private void ShowEntityDetail(KnowledgeEntity entity)
    {
        AudioMgr.Instance?.PlayClickSfx();

        if (detailPanel == null) return;

        detailPanel.SetActive(true);

        if (detailTitle != null)
            detailTitle.text = entity.name;

        if (detailType != null)
            detailType.text = $"类型：{GetEntityTypeName(entity.entityType)}";

        if (detailSource != null)
            detailSource.text = $"出处：《{entity.source}》";

        if (detailDescription != null)
            detailDescription.text = entity.description;

        // 显示关联实体
        if (detailRelations != null)
        {
            var relations = GraphRAGManager.Instance.GetRelatedEntities(entity.id);
            if (relations.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<color=#FFD700>相关联的知识：</color>");
                foreach (var (relatedEntity, relation) in relations)
                {
                    string relTypeName = GetRelationTypeName(relation.relationType);
                    sb.AppendLine($"  • {relTypeName} → {relatedEntity.name}");
                }
                detailRelations.text = sb.ToString();
            }
            else
            {
                detailRelations.text = "<color=#888888>暂无关联知识</color>";
            }
        }
    }

    private void UpdateStats()
    {
        if (statsText == null) return;

        int discovered = GraphRAGManager.Instance.DiscoveredCount;
        int total = GraphRAGManager.Instance.TotalCount;
        statsText.text = $"知识收集进度：{discovered} / {total}";
    }

    private string GetEntityTypeName(EntityType type)
    {
        return type switch
        {
            EntityType.Beast => "异兽",
            EntityType.Herb => "草药",
            EntityType.Location => "地点",
            EntityType.Character => "人物",
            EntityType.Item => "物品",
            EntityType.Skill => "技能",
            _ => "未知"
        };
    }

    private string GetRelationTypeName(RelationType type)
    {
        return type switch
        {
            RelationType.FoundIn => "出没于",
            RelationType.GrowsIn => "生长于",
            RelationType.Drops => "掉落",
            RelationType.CounteredBy => "被克制",
            RelationType.Cures => "可治疗",
            RelationType.HostileTo => "敌对",
            RelationType.SymbioticWith => "共生",
            RelationType.RequiredFor => "用于",
            _ => "关联"
        };
    }
}