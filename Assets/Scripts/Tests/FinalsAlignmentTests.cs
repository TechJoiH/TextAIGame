#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using LitJson;
using Logic.Memory;
using StateData.Role;
using StateData.Environment;
using Data.KnowledgeGraph;

public class FinalsAlignmentTests
{
    [Test]
    public void HintJsonParser_ParsesStrictThreeStrings()
    {
        const string rawJson = "[\"观察山壁草木\",\"采集祝余\",\"向雾外试探前进\"]";

        bool parsed = ActionHintPanel.TryParseHintArray(rawJson, out var hints);

        Assert.IsTrue(parsed);
        Assert.AreEqual(3, hints.Count);
        Assert.AreEqual("采集祝余", hints[1]);
    }

    [Test]
    public void CmdWhitelist_RejectsUnexpectedFields()
    {
        const string invalidJson = "{\"hp\":-5,\"gm\":\"debug\"}";
        const string validJson = "{\"get_item\":{\"name\":\"祝余\",\"count\":1}}";

        bool invalidAccepted = IARProcessor.TryValidateCommandJson(invalidJson, out var failReason);
        bool validAccepted = IARProcessor.TryValidateCommandJson(validJson, out _);

        Assert.IsFalse(invalidAccepted);
        Assert.IsTrue(failReason.Contains("未授权字段"));
        Assert.IsTrue(validAccepted);
    }

    [Test]
    public void SaveRoundTrip_RestoresKnowledgeGraphSnapshot_AndBackfillsLegacySave()
    {
        var saveData = new FullSaveData
        {
            roleState = new RoleState
            {
                identity = new IdentityState { name = "林渊" },
                attributes = new AttributeState
                {
                    currentHealth = 88,
                    maxHealth = 100,
                    currentMana = 42,
                    maxMana = 50,
                    expToNextLevel = 100
                }
            },
            environmentState = EnvironmentState.GetDefault(),
            memorySnapshot = new MemorySnapshot
            {
                shortTermMemory = new List<DialogueEntry>(),
                longTermMemories = new List<LongTermMemory>(),
                totalTurns = 1
            },
            knowledgeGraphSnapshot = new KnowledgeGraphSnapshot
            {
                entities = new List<KnowledgeEntity>(),
                relations = new List<KnowledgeRelation>(),
                discoveredEntityIds = new List<string> { "loc_zhaoyao", "herb_zhuyu" }
            }
        };

        string json = JsonMapper.ToJson(saveData);
        FullSaveData loaded = GameSaveMgr.EnsureCompatibility(JsonMapper.ToObject<FullSaveData>(json));

        Assert.NotNull(loaded.knowledgeGraphSnapshot);
        Assert.AreEqual(2, loaded.knowledgeGraphSnapshot.discoveredEntityIds.Count);

        const string legacyJson =
            "{\"roleState\":{\"identity\":{\"name\":\"林渊\"}},\"environmentState\":{\"locationName\":\"招摇山\"},\"memorySnapshot\":{\"shortTermMemory\":[],\"longTermMemories\":[],\"totalTurns\":0}}";
        FullSaveData legacyLoaded = GameSaveMgr.EnsureCompatibility(JsonMapper.ToObject<FullSaveData>(legacyJson));

        Assert.NotNull(legacyLoaded.knowledgeGraphSnapshot);
        Assert.NotNull(legacyLoaded.knowledgeGraphSnapshot.discoveredEntityIds);
        Assert.AreEqual(0, legacyLoaded.knowledgeGraphSnapshot.discoveredEntityIds.Count);
    }
}
#endif
