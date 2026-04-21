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
    /// 负责本地知识加载、检索、发现与存档恢复。
    /// </summary>
    public class GraphRAGManager : MonoSingleton<GraphRAGManager>
    {
        private const string DefaultKnowledgeResourcePath = "Data/ShanHaiKnowledge";

        [Header("配置")]
        [SerializeField] private TextAsset shanHaiJingData;

        private readonly Dictionary<string, KnowledgeEntity> _entityDict = new Dictionary<string, KnowledgeEntity>();
        private readonly List<KnowledgeRelation> _relations = new List<KnowledgeRelation>();
        private readonly HashSet<string> _discoveredIds = new HashSet<string>();
        private readonly Dictionary<EntityType, List<string>> _typeIndex = new Dictionary<EntityType, List<string>>();
        private readonly Dictionary<string, List<KnowledgeRelation>> _relationIndex = new Dictionary<string, List<KnowledgeRelation>>();

        public int DiscoveredCount => _discoveredIds.Count;
        public int TotalCount => _entityDict.Count;

        protected override void Awake()
        {
            base.Awake();
            InitializeKnowledgeLibrary();
        }

        public void InitializeKnowledgeLibrary()
        {
            ClearLibrary();

            if (!TryLoadKnowledgeFromJson())
            {
                SeedFallbackKnowledge();
            }

            ResetDiscoveredState();
            Debug.Log($"[GraphRAG] 知识库初始化完成: {_entityDict.Count} 实体, {_relations.Count} 关系");
        }

        public void ResetDiscoveredState()
        {
            _discoveredIds.Clear();
            foreach (var entity in _entityDict.Values)
            {
                entity.isDiscovered = false;
                entity.discoveredAt = 0;
            }
        }

        public bool IsEntityDiscovered(string entityId)
        {
            return !string.IsNullOrWhiteSpace(entityId) && _discoveredIds.Contains(entityId);
        }

        public KnowledgeEntity GetEntityById(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return null;

            _entityDict.TryGetValue(entityId, out var entity);
            return entity;
        }

        public void AddEntity(KnowledgeEntity entity)
        {
            if (entity == null || string.IsNullOrEmpty(entity.id))
                return;

            entity.tags ??= new List<string>();
            entity.properties ??= new Dictionary<string, string>();

            _entityDict[entity.id] = entity;

            if (!_typeIndex.ContainsKey(entity.entityType))
                _typeIndex[entity.entityType] = new List<string>();

            if (!_typeIndex[entity.entityType].Contains(entity.id))
                _typeIndex[entity.entityType].Add(entity.id);
        }

        public void AddRelation(KnowledgeRelation relation)
        {
            if (relation == null || string.IsNullOrWhiteSpace(relation.fromId) || string.IsNullOrWhiteSpace(relation.toId))
                return;

            _relations.Add(relation);
            IndexRelation(relation);
        }

        public void DiscoverEntity(string entityId)
        {
            if (!_entityDict.ContainsKey(entityId))
                return;

            var entity = _entityDict[entityId];
            if (entity.isDiscovered)
                return;

            entity.isDiscovered = true;
            entity.discoveredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _discoveredIds.Add(entityId);

            Debug.Log($"<color=green>[GraphRAG] 发现新知识: {entity.name}</color>");
            EventCenter.Instance.Broadcast("OnKnowledgeDiscovered", entity);
        }

        public List<KnowledgeEntity> SearchByName(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new List<KnowledgeEntity>();

            return _entityDict.Values
                .Where(e =>
                    (!string.IsNullOrEmpty(e.name) && e.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.description) && e.description.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(e => e.isDiscovered)
                .ThenBy(e => e.name)
                .ToList();
        }

        public List<KnowledgeEntity> GetEntitiesByType(EntityType type, bool onlyDiscovered = false)
        {
            if (!_typeIndex.ContainsKey(type))
                return new List<KnowledgeEntity>();

            IEnumerable<KnowledgeEntity> query = _typeIndex[type]
                .Where(id => _entityDict.ContainsKey(id))
                .Select(id => _entityDict[id]);

            if (onlyDiscovered)
                query = query.Where(e => e.isDiscovered);

            return query.OrderByDescending(e => e.isDiscovered).ThenBy(e => e.name).ToList();
        }

        public List<(KnowledgeEntity entity, KnowledgeRelation relation)> GetRelatedEntities(string entityId)
        {
            var result = new List<(KnowledgeEntity, KnowledgeRelation)>();
            if (string.IsNullOrWhiteSpace(entityId) || !_relationIndex.ContainsKey(entityId))
                return result;

            foreach (var relation in _relationIndex[entityId])
            {
                string relatedId = relation.fromId == entityId ? relation.toId : relation.fromId;
                if (_entityDict.TryGetValue(relatedId, out var relatedEntity))
                {
                    result.Add((relatedEntity, relation));
                }
            }

            return result;
        }

        public string GetRelationDisplayText(KnowledgeEntity focusEntity, KnowledgeEntity relatedEntity, KnowledgeRelation relation)
        {
            if (focusEntity == null || relatedEntity == null || relation == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(relation.description))
                return relation.description.Trim();

            bool focusIsSource = relation.fromId == focusEntity.id;

            return relation.relationType switch
            {
                RelationType.FoundIn => focusIsSource
                    ? $"{focusEntity.name}出没于{relatedEntity.name}"
                    : $"{relatedEntity.name}常出没于此",
                RelationType.GrowsIn => focusIsSource
                    ? $"{focusEntity.name}生于{relatedEntity.name}"
                    : $"此地生有{relatedEntity.name}",
                RelationType.Drops => focusIsSource
                    ? $"{focusEntity.name}可掉落{relatedEntity.name}"
                    : $"{relatedEntity.name}可从{relatedEntity.name}之外获得",
                RelationType.CounteredBy => focusIsSource
                    ? $"{focusEntity.name}可被{relatedEntity.name}克制"
                    : $"{focusEntity.name}可克制{relatedEntity.name}",
                RelationType.Cures => focusIsSource
                    ? $"{focusEntity.name}可治疗{relatedEntity.name}"
                    : $"{relatedEntity.name}可用{focusEntity.name}缓解",
                RelationType.HostileTo => $"与{relatedEntity.name}敌对",
                RelationType.SymbioticWith => $"与{relatedEntity.name}共生",
                RelationType.RequiredFor => focusIsSource
                    ? $"{focusEntity.name}常用于{relatedEntity.name}"
                    : relatedEntity.entityType == EntityType.Herb
                        ? $"可借{relatedEntity.name}应对此地异状"
                        : $"与{relatedEntity.name}相关",
                _ => $"与{relatedEntity.name}相关"
            };
        }

        public List<KnowledgeEntity> GetAllDiscoveredEntities()
        {
            return _discoveredIds
                .Where(id => _entityDict.ContainsKey(id))
                .Select(id => _entityDict[id])
                .OrderByDescending(e => e.discoveredAt)
                .ToList();
        }

        public List<KnowledgeEntity> GetAllEntities()
        {
            return _entityDict.Values
                .OrderByDescending(e => e.isDiscovered)
                .ThenBy(e => e.name)
                .ToList();
        }

        public List<KnowledgeEntity> SearchByTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return new List<KnowledgeEntity>();

            return _entityDict.Values
                .Where(e => e.tags != null && e.tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => e.isDiscovered)
                .ToList();
        }

        public List<KnowledgeEntity> SemanticRetrieve(string context, int topK = 5)
        {
            if (string.IsNullOrWhiteSpace(context))
                return new List<KnowledgeEntity>();

            return _entityDict.Values
                .Select(entity => new
                {
                    Entity = entity,
                    Score = CalculateRelevanceScore(entity, context)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Entity.isDiscovered)
                .Take(Mathf.Max(1, topK))
                .Select(item => item.Entity)
                .ToList();
        }

        public string BuildKnowledgeContext(string playerInput)
        {
            var relevantEntities = SemanticRetrieve(playerInput, 3);
            if (relevantEntities.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            builder.AppendLine("【山海经知识参考】");

            foreach (var entity in relevantEntities)
            {
                builder.AppendLine($"◆ {entity.name}（{GetEntityTypeName(entity.entityType)}）");
                builder.AppendLine($"  {entity.description}");

                var relationTexts = GetRelatedEntities(entity.id)
                    .Take(2)
                    .Select(item => GetRelationDisplayText(entity, item.entity, item.relation))
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                if (relationTexts.Count > 0)
                    builder.AppendLine($"  关联：{string.Join("；", relationTexts)}");

                builder.AppendLine($"  ——《{entity.source}》");
            }

            return builder.ToString().TrimEnd();
        }

        public void ExtractAndDiscoverFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (var entity in _entityDict.Values)
            {
                if (!string.IsNullOrEmpty(entity.name) && text.Contains(entity.name, StringComparison.Ordinal))
                {
                    DiscoverEntity(entity.id);
                }
            }
        }

        public KnowledgeGraphSnapshot GetSnapshot()
        {
            return new KnowledgeGraphSnapshot
            {
                entities = _entityDict.Values.Select(CloneEntity).ToList(),
                relations = _relations.Select(CloneRelation).ToList(),
                discoveredEntityIds = _discoveredIds.ToList()
            };
        }

        public void RestoreFromSnapshot(KnowledgeGraphSnapshot snapshot)
        {
            ResetDiscoveredState();

            if (snapshot == null)
            {
                Debug.Log("[GraphRAG] 存档未包含知识图谱快照，已按空快照恢复。");
                return;
            }

            if (snapshot.entities != null)
            {
                foreach (var savedEntity in snapshot.entities)
                {
                    if (savedEntity == null || string.IsNullOrWhiteSpace(savedEntity.id))
                        continue;

                    if (_entityDict.TryGetValue(savedEntity.id, out var currentEntity))
                    {
                        currentEntity.description = savedEntity.description;
                        currentEntity.source = savedEntity.source;
                        currentEntity.tags = savedEntity.tags ?? new List<string>();
                        currentEntity.properties = savedEntity.properties ?? new Dictionary<string, string>();
                        currentEntity.discoveredAt = savedEntity.discoveredAt;
                    }
                    else
                    {
                        AddEntity(CloneEntity(savedEntity));
                    }
                }
            }

            if (snapshot.relations != null)
            {
                foreach (var relation in snapshot.relations)
                {
                    if (relation == null)
                        continue;

                    bool exists = _relations.Any(existing =>
                        existing.fromId == relation.fromId &&
                        existing.toId == relation.toId &&
                        existing.relationType == relation.relationType);

                    if (!exists)
                        AddRelation(CloneRelation(relation));
                }
            }

            IEnumerable<string> discoveredIds = snapshot.discoveredEntityIds ?? snapshot.entities?
                .Where(entity => entity != null && entity.isDiscovered)
                .Select(entity => entity.id)
                .ToList();

            if (discoveredIds != null)
            {
                foreach (var id in discoveredIds)
                {
                    if (!_entityDict.ContainsKey(id))
                        continue;

                    _discoveredIds.Add(id);
                    _entityDict[id].isDiscovered = true;
                    if (_entityDict[id].discoveredAt == 0)
                        _entityDict[id].discoveredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
            }

            Debug.Log($"[GraphRAG] 已恢复知识图谱: {_discoveredIds.Count} 个已发现");
        }

        private void ClearLibrary()
        {
            _entityDict.Clear();
            _relations.Clear();
            _discoveredIds.Clear();
            _typeIndex.Clear();
            _relationIndex.Clear();
        }

        private void IndexRelation(KnowledgeRelation relation)
        {
            if (!_relationIndex.ContainsKey(relation.fromId))
                _relationIndex[relation.fromId] = new List<KnowledgeRelation>();
            _relationIndex[relation.fromId].Add(relation);

            if (!_relationIndex.ContainsKey(relation.toId))
                _relationIndex[relation.toId] = new List<KnowledgeRelation>();
            _relationIndex[relation.toId].Add(relation);
        }

        private bool TryLoadKnowledgeFromJson()
        {
            var asset = shanHaiJingData != null ? shanHaiJingData : Resources.Load<TextAsset>(DefaultKnowledgeResourcePath);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                Debug.LogWarning("[GraphRAG] 未找到知识 JSON，回退到内置知识库。");
                return false;
            }

            try
            {
                var library = JsonUtility.FromJson<KnowledgeLibraryData>(asset.text);
                if (library == null || library.entities == null || library.entities.Count == 0)
                {
                    Debug.LogWarning("[GraphRAG] 知识 JSON 为空，回退到内置知识库。");
                    return false;
                }

                foreach (var entityData in library.entities)
                {
                    var entity = new KnowledgeEntity(
                        entityData.id,
                        entityData.name,
                        ParseEntityType(entityData.entityType),
                        entityData.description,
                        entityData.source)
                    {
                        tags = entityData.tags != null ? new List<string>(entityData.tags) : new List<string>()
                    };
                    AddEntity(entity);
                }

                if (library.relations != null)
                {
                    foreach (var relationData in library.relations)
                    {
                        AddRelation(new KnowledgeRelation(
                            relationData.fromId,
                            relationData.toId,
                            ParseRelationType(relationData.relationType),
                            relationData.description,
                            relationData.weight <= 0 ? 1f : relationData.weight));
                    }
                }

                Debug.Log($"[GraphRAG] 已从 JSON 载入知识库: {asset.name}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[GraphRAG] 解析知识 JSON 失败: {exception.Message}");
                return false;
            }
        }

        private void SeedFallbackKnowledge()
        {
            AddEntity(new KnowledgeEntity("loc_zhaoyao", "招摇山", EntityType.Location,
                "招摇之山多桂木与矿玉，山岚终日不散，晨雾沿着石脊与松根游走。",
                "山海经·南山经")
            { tags = new List<string> { "南山经", "雾", "灵山" } });

            AddEntity(new KnowledgeEntity("herb_zhuyu", "祝余", EntityType.Herb,
                "招摇之山有草焉，其状如韭而青华，其名曰祝余，食之不饥。",
                "山海经·南山经")
            { tags = new List<string> { "辟谷", "果腹", "草药" } });

            AddEntity(new KnowledgeEntity("herb_migu", "迷谷", EntityType.Herb,
                "招摇之山有木焉，其状如榖而黑理，其华四照，其名曰迷谷，佩之不迷。",
                "山海经·南山经")
            { tags = new List<string> { "辨路", "避雾", "灵木" } });

            AddEntity(new KnowledgeEntity("beast_jiuwei", "九尾狐", EntityType.Beast,
                "青丘之山有兽焉，其状如狐而九尾，其音如婴儿，能食人，食者不蛊。",
                "山海经·南山经")
            { tags = new List<string> { "神兽", "青丘", "九尾" } });

            AddEntity(new KnowledgeEntity("beast_bifang", "毕方", EntityType.Beast,
                "章莪之山有鸟焉，其状如鹤，一足，赤文青质而白喙，名曰毕方，见则其邑有讹火。",
                "山海经·西山经")
            { tags = new List<string> { "神鸟", "火", "一足" } });

            AddEntity(new KnowledgeEntity("loc_qingqiu", "青丘", EntityType.Location,
                "又东三百里，曰青丘之山，其阳多玉，其阴多青雘。",
                "山海经·南山经")
            { tags = new List<string> { "仙山", "玉石" } });

            AddEntity(new KnowledgeEntity("loc_zhange", "章莪之山", EntityType.Location,
                "章莪之山多怪鸟异火，山风燥烈，常有赤纹流焰出没。",
                "山海经·西山经")
            { tags = new List<string> { "火兆", "异鸟" } });

            AddRelation(new KnowledgeRelation("herb_zhuyu", "loc_zhaoyao", RelationType.GrowsIn, "祝余生于招摇山"));
            AddRelation(new KnowledgeRelation("herb_migu", "loc_zhaoyao", RelationType.GrowsIn, "迷谷生于招摇山"));
            AddRelation(new KnowledgeRelation("beast_jiuwei", "loc_qingqiu", RelationType.FoundIn, "九尾狐出没于青丘"));
            AddRelation(new KnowledgeRelation("beast_bifang", "loc_zhange", RelationType.FoundIn, "毕方出没于章莪之山"));
            AddRelation(new KnowledgeRelation("herb_migu", "loc_zhaoyao", RelationType.RequiredFor, "迷谷可帮助旅者辨清山中迷途"));
        }

        private float CalculateRelevanceScore(KnowledgeEntity entity, string context)
        {
            float score = 0f;
            if (entity == null || string.IsNullOrWhiteSpace(context))
                return score;

            if (!string.IsNullOrEmpty(entity.name) && context.IndexOf(entity.name, StringComparison.OrdinalIgnoreCase) >= 0)
                score += 10f;

            if (entity.tags != null)
            {
                foreach (var tag in entity.tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag) && context.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 3f;
                }
            }

            if (!string.IsNullOrEmpty(entity.description))
            {
                foreach (char character in context)
                {
                    if (entity.description.IndexOf(character) >= 0)
                        score += 0.08f;
                }
            }

            if (entity.isDiscovered)
                score += 0.5f;

            return score;
        }

        private static KnowledgeEntity CloneEntity(KnowledgeEntity source)
        {
            if (source == null)
                return null;

            return new KnowledgeEntity(source.id, source.name, source.entityType, source.description, source.source)
            {
                tags = source.tags != null ? new List<string>(source.tags) : new List<string>(),
                properties = source.properties != null ? new Dictionary<string, string>(source.properties) : new Dictionary<string, string>(),
                isDiscovered = source.isDiscovered,
                discoveredAt = source.discoveredAt
            };
        }

        private static KnowledgeRelation CloneRelation(KnowledgeRelation source)
        {
            if (source == null)
                return null;

            return new KnowledgeRelation(source.fromId, source.toId, source.relationType, source.description, source.weight);
        }

        private static EntityType ParseEntityType(string rawType)
        {
            return Enum.TryParse(rawType, true, out EntityType entityType)
                ? entityType
                : EntityType.Item;
        }

        private static RelationType ParseRelationType(string rawType)
        {
            return Enum.TryParse(rawType, true, out RelationType relationType)
                ? relationType
                : RelationType.RequiredFor;
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
                RelationType.Drops => "可掉落",
                RelationType.CounteredBy => "可被克制",
                RelationType.Cures => "可治疗",
                RelationType.HostileTo => "敌对",
                RelationType.SymbioticWith => "共生于",
                RelationType.RequiredFor => "常用于",
                _ => "关联"
            };
        }

        #pragma warning disable CS0649
        [Serializable]
        private sealed class KnowledgeLibraryData
        {
            public List<KnowledgeEntityData> entities = new List<KnowledgeEntityData>();
            public List<KnowledgeRelationData> relations = new List<KnowledgeRelationData>();
        }

        [Serializable]
        private sealed class KnowledgeEntityData
        {
            public string id;
            public string name;
            public string entityType;
            public string description;
            public string source;
            public List<string> tags = new List<string>();
        }

        [Serializable]
        private sealed class KnowledgeRelationData
        {
            public string fromId;
            public string toId;
            public string relationType;
            public string description;
            public float weight = 1f;
        }
        #pragma warning restore CS0649
    }
}
