using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Data.KnowledgeGraph;

namespace Logic.GraphRAG
{
    /// <summary>
    /// GraphRAG-Lite 知识图谱管理器
    /// 基于 C# LINQ 的本地内存级知识检索
    /// </summary>
    public class GraphRAGManager : MonoSingleton<GraphRAGManager>
    {
        [Header("配置")]
        [SerializeField] private TextAsset shanHaiJingData;  // 山海经JSON数据

        // 核心数据结构
        private Dictionary<string, KnowledgeEntity> _entityDict = new Dictionary<string, KnowledgeEntity>();
        private List<KnowledgeRelation> _relations = new List<KnowledgeRelation>();
        private HashSet<string> _discoveredIds = new HashSet<string>();

        // 索引结构（加速检索）
        private Dictionary<EntityType, List<string>> _typeIndex = new Dictionary<EntityType, List<string>>();
        private Dictionary<string, List<KnowledgeRelation>> _relationIndex = new Dictionary<string, List<KnowledgeRelation>>();

        /// <summary>
        /// 已发现的实体数量
        /// </summary>
        public int DiscoveredCount => _discoveredIds.Count;

        /// <summary>
        /// 总实体数量
        /// </summary>
        public int TotalCount => _entityDict.Count;

        protected override void Awake()
        {
            base.Awake();
            InitializeShanHaiJingKnowledge();
        }

        /// <summary>
        /// 初始化《山海经》知识库
        /// </summary>
        private void InitializeShanHaiJingKnowledge()
        {
            // 预置《山海经》异兽数据
            AddEntity(new KnowledgeEntity("beast_jiuwei", "九尾狐", EntityType.Beast,
                "青丘之山有兽焉，其状如狐而九尾，其音如婴儿，能食人，食者不蛊。",
                "山海经·南山经")
            { tags = new List<string> { "神兽", "青丘", "九尾" } });

            AddEntity(new KnowledgeEntity("beast_bifang", "毕方", EntityType.Beast,
                "章莪之山有鸟焉，其状如鹤，一足，赤文青质而白喙，名曰毕方，其鸣自叫也，见则其邑有讹火。",
                "山海经·西山经")
            { tags = new List<string> { "神鸟", "火", "一足" } });

            AddEntity(new KnowledgeEntity("beast_hundun", "混沌", EntityType.Beast,
                "天山有神焉，其状如黄囊，赤如丹火，六足四翼，浑敦无面目。",
                "山海经·西山经")
            { tags = new List<string> { "凶兽", "无面", "混沌" } });

            AddEntity(new KnowledgeEntity("beast_qiongqi", "穷奇", EntityType.Beast,
                "邽山有兽焉，其状如牛而虎文，其音如吠犬，是食人。",
                "山海经·西山经")
            { tags = new List<string> { "凶兽", "食人", "四凶" } });

            AddEntity(new KnowledgeEntity("beast_taowu", "梼杌", EntityType.Beast,
                "有兽焉，其状如虎而犬毛，长二尺，人面，虎足，猪牙，尾长一丈八尺，搅乱荒中，名曰梼杌。",
                "山海经·西山经")
            { tags = new List<string> { "凶兽", "人面", "四凶" } });

            // 预置《山海经》草药数据
            AddEntity(new KnowledgeEntity("herb_zhucao", "祝余", EntityType.Herb,
                "招摇之山有草焉，其状如韭而青华，其名曰祝余，食之不饥。",
                "山海经·南山经")
            { tags = new List<string> { "辟谷", "果腹" }, properties = new Dictionary<string, string> { { "effect", "food_restore" } } });

            AddEntity(new KnowledgeEntity("herb_migu", "迷谷", EntityType.Herb,
                "招摇之山有木焉，其状如榖而黑理，其华四照，其名曰迷榖，佩之不迷。",
                "山海经·南山经")
            { tags = new List<string> { "指路", "避邪" }, properties = new Dictionary<string, string> { { "effect", "anti_confusion" } } });

            AddEntity(new KnowledgeEntity("herb_shahua", "沙棠", EntityType.Herb,
                "昆仑之丘有木焉，其状如棠，黄华赤实，其味如李而无核，名曰沙棠，可以御水，食之使人不溺。",
                "山海经·西山经")
            { tags = new List<string> { "水性", "御水" }, properties = new Dictionary<string, string> { { "effect", "water_breathing" } } });

            // 预置地点数据
            AddEntity(new KnowledgeEntity("loc_qingqiu", "青丘", EntityType.Location,
                "又东三百里，曰青丘之山，其阳多玉，其阴多青雘。",
                "山海经·南山经")
            { tags = new List<string> { "仙山", "玉石" } });

            AddEntity(new KnowledgeEntity("loc_kunlun", "昆仑", EntityType.Location,
                "昆仑之丘，是实惟帝之下都，神陆吾司之，其神状虎身而九尾，人面而虎爪。",
                "山海经·西山经")
            { tags = new List<string> { "神山", "帝都", "西王母" } });

            // 添加关系
            AddRelation(new KnowledgeRelation("beast_jiuwei", "loc_qingqiu", RelationType.FoundIn, "九尾狐出没于青丘之山"));
            AddRelation(new KnowledgeRelation("herb_shahua", "loc_kunlun", RelationType.GrowsIn, "沙棠生长于昆仑之丘"));
            AddRelation(new KnowledgeRelation("herb_migu", "beast_hundun", RelationType.CounteredBy, "迷谷可破混沌迷障"));

            Debug.Log($"[GraphRAG] 知识库初始化完成: {_entityDict.Count} 实体, {_relations.Count} 关系");
        }

        #region 实体与关系管理

        /// <summary>
        /// 添加实体
        /// </summary>
        public void AddEntity(KnowledgeEntity entity)
        {
            if (entity == null || string.IsNullOrEmpty(entity.id)) return;

            _entityDict[entity.id] = entity;

            // 更新类型索引
            if (!_typeIndex.ContainsKey(entity.entityType))
                _typeIndex[entity.entityType] = new List<string>();

            if (!_typeIndex[entity.entityType].Contains(entity.id))
                _typeIndex[entity.entityType].Add(entity.id);
        }

        /// <summary>
        /// 添加关系
        /// </summary>
        public void AddRelation(KnowledgeRelation relation)
        {
            if (relation == null) return;

            _relations.Add(relation);

            // 更新关系索引
            if (!_relationIndex.ContainsKey(relation.fromId))
                _relationIndex[relation.fromId] = new List<KnowledgeRelation>();
            _relationIndex[relation.fromId].Add(relation);

            if (!_relationIndex.ContainsKey(relation.toId))
                _relationIndex[relation.toId] = new List<KnowledgeRelation>();
            _relationIndex[relation.toId].Add(relation);
        }

        /// <summary>
        /// 标记实体为已发现
        /// </summary>
        public void DiscoverEntity(string entityId)
        {
            if (!_entityDict.ContainsKey(entityId)) return;

            var entity = _entityDict[entityId];
            if (!entity.isDiscovered)
            {
                entity.isDiscovered = true;
                entity.discoveredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _discoveredIds.Add(entityId);

                Debug.Log($"<color=green>[GraphRAG] 发现新知识: {entity.name}</color>");

                // 触发事件通知UI更新
                EventCenter.Instance.Broadcast("OnKnowledgeDiscovered", entity);
            }
        }

        #endregion

        #region LINQ 检索方法

        /// <summary>
        /// 按名称模糊搜索实体
        /// </summary>
        public List<KnowledgeEntity> SearchByName(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return new List<KnowledgeEntity>();

            return _entityDict.Values
                .Where(e => e.name.Contains(keyword) || e.description.Contains(keyword))
                .OrderByDescending(e => e.isDiscovered)
                .ThenBy(e => e.name)
                .ToList();
        }

        /// <summary>
        /// 按类型获取实体
        /// </summary>
        public List<KnowledgeEntity> GetEntitiesByType(EntityType type, bool onlyDiscovered = false)
        {
            if (!_typeIndex.ContainsKey(type)) return new List<KnowledgeEntity>();

            var query = _typeIndex[type]
                .Select(id => _entityDict[id]);

            if (onlyDiscovered)
                query = query.Where(e => e.isDiscovered);

            return query.OrderBy(e => e.name).ToList();
        }

        /// <summary>
        /// 获取实体的所有关联实体（一阶邻居）
        /// </summary>
        public List<(KnowledgeEntity entity, KnowledgeRelation relation)> GetRelatedEntities(string entityId)
        {
            var result = new List<(KnowledgeEntity, KnowledgeRelation)>();

            if (!_relationIndex.ContainsKey(entityId)) return result;

            foreach (var rel in _relationIndex[entityId])
            {
                string relatedId = rel.fromId == entityId ? rel.toId : rel.fromId;
                if (_entityDict.ContainsKey(relatedId))
                {
                    result.Add((_entityDict[relatedId], rel));
                }
            }

            return result;
        }

        /// <summary>
        /// 获取所有已发现的实体
        /// </summary>
        public List<KnowledgeEntity> GetAllDiscoveredEntities()
        {
            return _discoveredIds
                .Where(id => _entityDict.ContainsKey(id))
                .Select(id => _entityDict[id])
                .OrderByDescending(e => e.discoveredAt)
                .ToList();
        }

        /// <summary>
        /// 获取所有实体（包括未解锁的）
        /// </summary>
        public List<KnowledgeEntity> GetAllEntities()
        {
            return _entityDict.Values
                .OrderByDescending(e => e.isDiscovered)  // 已解锁的排前面
                .ThenBy(e => e.name)
                .ToList();
        }

        /// <summary>
        /// 按标签搜索实体
        /// </summary>
        public List<KnowledgeEntity> SearchByTag(string tag)
        {
            return _entityDict.Values
                .Where(e => e.tags != null && e.tags.Contains(tag))
                .ToList();
        }

        /// <summary>
        /// 语义检索：根据上下文关键词查找相关知识
        /// </summary>
        public List<KnowledgeEntity> SemanticRetrieve(string context, int topK = 5)
        {
            if (string.IsNullOrEmpty(context)) return new List<KnowledgeEntity>();

            // 简单的关键词匹配评分
            var scored = _entityDict.Values
                .Select(e => new
                {
                    Entity = e,
                    Score = CalculateRelevanceScore(e, context)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Select(x => x.Entity)
                .ToList();

            return scored;
        }

        private float CalculateRelevanceScore(KnowledgeEntity entity, string context)
        {
            float score = 0;

            // 名称匹配
            if (context.Contains(entity.name))
                score += 10;

            // 标签匹配
            foreach (var tag in entity.tags)
            {
                if (context.Contains(tag))
                    score += 3;
            }

            // 描述关键词匹配
            var descWords = entity.description.ToCharArray();
            int matchCount = context.Count(c => entity.description.Contains(c));
            score += matchCount * 0.1f;

            return score;
        }

        #endregion

        #region 知识注入与提示词构建

        /// <summary>
        /// 构建知识增强的上下文（用于注入LLM提示词）
        /// </summary>
        public string BuildKnowledgeContext(string playerInput)
        {
            var relevantEntities = SemanticRetrieve(playerInput, 3);
            if (relevantEntities.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("【山海经知识参考】");

            foreach (var entity in relevantEntities)
            {
                sb.AppendLine($"◆ {entity.name}（{GetEntityTypeName(entity.entityType)}）");
                sb.AppendLine($"  {entity.description}");
                sb.AppendLine($"  ——《{entity.source}》");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 从AI回复中抽取实体并标记发现
        /// </summary>
        public void ExtractAndDiscoverFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            foreach (var entity in _entityDict.Values)
            {
                if (text.Contains(entity.name))
                {
                    DiscoverEntity(entity.id);
                }
            }
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

        #endregion

        #region 存档与恢复

        public KnowledgeGraphSnapshot GetSnapshot()
        {
            return new KnowledgeGraphSnapshot
            {
                entities = _entityDict.Values.ToList(),
                relations = new List<KnowledgeRelation>(_relations),
                discoveredEntityIds = _discoveredIds.ToList()
            };
        }

        public void RestoreFromSnapshot(KnowledgeGraphSnapshot snapshot)
        {
            if (snapshot == null) return;

            // 恢复发现状态
            _discoveredIds.Clear();
            foreach (var id in snapshot.discoveredEntityIds)
            {
                _discoveredIds.Add(id);
                if (_entityDict.ContainsKey(id))
                    _entityDict[id].isDiscovered = true;
            }

            Debug.Log($"[GraphRAG] 已恢复知识图谱: {_discoveredIds.Count} 个已发现");
        }

        #endregion
    }
}