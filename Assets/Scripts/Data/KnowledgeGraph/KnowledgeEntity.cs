using System;
using System.Collections.Generic;

namespace Data.KnowledgeGraph
{
    /// <summary>
    /// 实体类型枚举
    /// </summary>
    public enum EntityType
    {
        Beast,      // 异兽
        Herb,       // 草药
        Location,   // 地点
        Character,  // 人物
        Item,       // 物品
        Skill       // 技能
    }

    /// <summary>
    /// 知识实体节点
    /// </summary>
    [Serializable]
    public class KnowledgeEntity
    {
        public string id;               // 唯一标识
        public string name;             // 名称
        public EntityType entityType;   // 实体类型
        public string description;      // 描述
        public string source;           // 出处（如"山海经·南山经"）
        public List<string> tags;       // 标签
        public Dictionary<string, string> properties;  // 扩展属性
        public bool isDiscovered;       // 是否已被玩家发现
        public long discoveredAt;       // 发现时间戳

        public KnowledgeEntity()
        {
            tags = new List<string>();
            properties = new Dictionary<string, string>();
        }

        public KnowledgeEntity(string id, string name, EntityType type, string desc, string source)
        {
            this.id = id;
            this.name = name;
            this.entityType = type;
            this.description = desc;
            this.source = source;
            this.tags = new List<string>();
            this.properties = new Dictionary<string, string>();
            this.isDiscovered = false;
        }
    }

    /// <summary>
    /// 关系类型枚举
    /// </summary>
    public enum RelationType
    {
        FoundIn,        // 出没于（异兽-地点）
        GrowsIn,        // 生长于（草药-地点）
        Drops,          // 掉落（异兽-物品）
        CounteredBy,    // 被克制（异兽-物品/技能）
        Cures,          // 治疗（草药-状态）
        HostileTo,      // 敌对（异兽-异兽）
        SymbioticWith,  // 共生（实体-实体）
        RequiredFor     // 需要（物品-配方）
    }

    /// <summary>
    /// 实体关系边
    /// </summary>
    [Serializable]
    public class KnowledgeRelation
    {
        public string fromId;           // 起始实体ID
        public string toId;             // 目标实体ID
        public RelationType relationType;
        public string description;      // 关系描述
        public float weight;            // 关系强度 0-1

        public KnowledgeRelation() { }

        public KnowledgeRelation(string from, string to, RelationType type, string desc = "", float weight = 1f)
        {
            this.fromId = from;
            this.toId = to;
            this.relationType = type;
            this.description = desc;
            this.weight = weight;
        }
    }

    /// <summary>
    /// 知识图谱快照（用于存档）
    /// </summary>
    [Serializable]
    public class KnowledgeGraphSnapshot
    {
        public List<KnowledgeEntity> entities;
        public List<KnowledgeRelation> relations;
        public List<string> discoveredEntityIds;
    }
}