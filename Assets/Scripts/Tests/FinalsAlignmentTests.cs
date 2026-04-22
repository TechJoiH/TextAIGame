#if UNITY_INCLUDE_TESTS
using System.Reflection;
using System.Collections.Generic;
using Data.KnowledgeGraph;
using LitJson;
using Logic.Inventory;
using Logic.Memory;
using NUnit.Framework;
using StateData.Environment;
using StateData.Items;
using StateData.Role;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    public void CmdWhitelist_RejectsUnexpectedFields_AndAcceptsTemplateItems()
    {
        const string invalidJson = "{\"hp\":-5,\"gm\":\"debug\"}";
        const string validJson =
            "{\"get_item\":{\"template_id\":\"zhuyu_herb\",\"count\":1,\"runtime\":{\"name\":\"祝余\",\"desc\":\"灵草\",\"rarity\":\"普通\",\"effect_text\":\"清苦回甘\",\"stat_modifiers\":[{\"stat\":\"max_health\",\"value\":5}]}}}";

        bool invalidAccepted = IARProcessor.TryValidateCommandJson(invalidJson, out var failReason);
        bool validAccepted = IARProcessor.TryValidateCommandJson(validJson, out _);

        Assert.IsFalse(invalidAccepted);
        Assert.IsTrue(failReason.Contains("未授权字段"));
        Assert.IsTrue(validAccepted);
    }

    [Test]
    public void CompatibilityMigration_UpgradesLegacyInventoryAndSupportsEquipFlow()
    {
        var library = CreateTestLibrary();
        var state = new RoleState
        {
            attributes = new AttributeState
            {
                maxHealth = 100,
                currentHealth = 100,
                maxMana = 50,
                currentMana = 50,
                strength = 10,
                agility = 8,
                intelligence = 12
            },
            equipment = new EquipmentState
            {
                inventory = new List<string> { "治疗药水", "青铜断剑" },
                weapon = string.Empty,
                equippedSkills = new List<string>()
            }
        };

        InventoryStateUtility.EnsureCompatibility(state, library);

        Assert.AreEqual(2, state.equipment.inventoryEntries.Count);
        Assert.AreEqual(0, state.equipment.inventory.Count);

        int swordIndex = state.equipment.inventoryEntries.FindIndex(item => item.templateId == "bronze_broken_sword");
        Assert.GreaterOrEqual(swordIndex, 0);

        bool equipResult = InventoryStateUtility.TryEquipInventoryItem(state, swordIndex, library, out var equipFailReason);
        Assert.IsTrue(equipResult, equipFailReason);
        Assert.NotNull(state.equipment.equipmentSlots.weapon);

        bool unequipResult = InventoryStateUtility.TryUnequipSlot(state, EquipSlotType.Weapon, library, out var unequipFailReason);
        Assert.IsTrue(unequipResult, unequipFailReason);
        Assert.IsNull(state.equipment.equipmentSlots.weapon);
        Assert.AreEqual(2, state.equipment.inventoryEntries.Count);
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
        FullSaveData loaded = GameSaveMgr.EnsureCompatibility(JsonMapper.ToObject<FullSaveData>(json), CreateTestLibrary());

        Assert.NotNull(loaded.knowledgeGraphSnapshot);
        Assert.AreEqual(2, loaded.knowledgeGraphSnapshot.discoveredEntityIds.Count);
        Assert.NotNull(loaded.roleState.equipment.inventoryEntries);

        const string legacyJson =
            "{\"roleState\":{\"identity\":{\"name\":\"林渊\"},\"equipment\":{\"inventory\":[\"治疗药水\"],\"weapon\":\"青铜断剑\"}},\"environmentState\":{\"locationName\":\"招摇山\"},\"memorySnapshot\":{\"shortTermMemory\":[],\"longTermMemories\":[],\"totalTurns\":0}}";
        FullSaveData legacyLoaded = GameSaveMgr.EnsureCompatibility(JsonMapper.ToObject<FullSaveData>(legacyJson), CreateTestLibrary());

        Assert.NotNull(legacyLoaded.knowledgeGraphSnapshot);
        Assert.NotNull(legacyLoaded.knowledgeGraphSnapshot.discoveredEntityIds);
        Assert.AreEqual(0, legacyLoaded.knowledgeGraphSnapshot.discoveredEntityIds.Count);
        Assert.AreEqual(1, legacyLoaded.roleState.equipment.inventoryEntries.Count);
        Assert.NotNull(legacyLoaded.roleState.equipment.equipmentSlots.weapon);
    }

    [Test]
    public void ResponseConsistencyChecker_AllowsNarratingItemConsumedEarlierThisTurn()
    {
        var library = CreateTestLibrary();
        library.items.Add(new ItemTemplateData
        {
            templateId = "obsidian_butcher_knife",
            displayName = "墨玉庖刀",
            itemKind = ItemKind.Equipment,
            equipSlot = EquipSlotType.Weapon,
            stackable = false,
            allowedSceneId = "loc_zhaoyao"
        });
        library.EnsureIndex();

        var turnStartState = new RoleState();
        turnStartState.equipment.inventoryEntries.Add(InventoryStateUtility.CreateEntryFromTemplate(
            "healing_potion",
            "治疗药水",
            "止血用药液",
            "普通",
            "饮下后回暖",
            null));

        var snapshot = AIResponseConsistencyChecker.CaptureSnapshot(turnStartState, library);

        var stateAfterResolution = new RoleState();
        InventoryStateUtility.EnsureCompatibility(stateAfterResolution, library);

        var report = AIResponseConsistencyChecker.FilterVisibleText(
            "你服下治疗药水，喉间顿时泛起一线辛热。",
            snapshot,
            stateAfterResolution,
            library);

        Assert.IsFalse(report.hasViolation);
        Assert.AreEqual("你服下治疗药水，喉间顿时泛起一线辛热。", report.visibleText);
    }

    [Test]
    public void ResponseConsistencyChecker_FiltersNonexistentItemUsage()
    {
        var library = CreateTestLibrary();
        var emptyState = new RoleState();
        InventoryStateUtility.EnsureCompatibility(emptyState, library);
        var snapshot = AIResponseConsistencyChecker.CaptureSnapshot(emptyState, library);

        var report = AIResponseConsistencyChecker.FilterVisibleText(
            "你服下血灵丹，四肢百骸顿时灼热起来。",
            snapshot,
            emptyState,
            library);

        Assert.IsTrue(report.hasViolation);
        Assert.IsFalse(report.visibleText.Contains("血灵丹"));
        Assert.IsTrue(report.feedback.Contains("血灵丹"));
    }

    [Test]
    public void ResponseConsistencyChecker_FiltersRejectedItemAcquisitionNarration()
    {
        var library = CreateTestLibrary();
        var emptyState = new RoleState();
        InventoryStateUtility.EnsureCompatibility(emptyState, library);
        var snapshot = AIResponseConsistencyChecker.CaptureSnapshot(emptyState, library);

        var report = AIResponseConsistencyChecker.FilterVisibleText(
            "你获得了一个祝余并顺手收入背包。",
            snapshot,
            emptyState,
            library);

        Assert.IsTrue(report.hasViolation);
        Assert.IsFalse(report.visibleText.Contains("祝余"));
        Assert.IsTrue(report.feedback.Contains("祝余"));
    }

    [Test]
    public void AnalyzeAndApplyAIResult_NormalizesTemplateAliasIntoSceneLibrary()
    {
        var library = CreateTestLibrary();
        library.items.Add(new ItemTemplateData
        {
            templateId = "dangkang_fang",
            displayName = "Dangkang Fang",
            itemKind = ItemKind.Material,
            equipSlot = EquipSlotType.None,
            stackable = true,
            allowedSceneId = "loc_zhaoyao"
        });
        library.EnsureIndex();

        var state = new RoleState();
        InventoryStateUtility.EnsureCompatibility(state, library);
        var go = new GameObject("IARProcessorAliasTest");

        try
        {
            var processor = go.AddComponent<IARProcessor>();
            string visible = processor.AnalyzeAndApplyAIResult(
                "<CMD>{\"get_item\":{\"template_id\":\"dangkang_tusk\",\"count\":1,\"runtime\":{\"name\":\"Dangkang Fang\",\"desc\":\"material\",\"rarity\":\"common\",\"effect_text\":\"\",\"stat_modifiers\":[]}}}</CMD>",
                state,
                library,
                out var feedback);

            Assert.AreEqual(string.Empty, visible);
            Assert.IsTrue(string.IsNullOrWhiteSpace(feedback), feedback);
            Assert.AreEqual(1, state.equipment.inventoryEntries.Count);
            Assert.AreEqual("dangkang_fang", state.equipment.inventoryEntries[0].templateId);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void AnalyzeAndApplyAIResult_GetItemBroadcastsCenterToast()
    {
        EventCenter.Instance.Clear();

        var library = CreateTestLibrary();
        var state = new RoleState();
        InventoryStateUtility.EnsureCompatibility(state, library);
        var go = new GameObject("IARToastTest");
        string toastMessage = null;
        System.Action<string> listener = message => toastMessage = message;

        try
        {
            EventCenter.Instance.AddListener("OnCenterToast", listener);

            var processor = go.AddComponent<IARProcessor>();
            string visible = processor.AnalyzeAndApplyAIResult(
                "<CMD>{\"get_item\":{\"template_id\":\"zhuyu_herb\",\"count\":2,\"runtime\":{\"name\":\"祝余\",\"desc\":\"灵草\",\"rarity\":\"普通\",\"effect_text\":\"回甘\",\"stat_modifiers\":[]}}}</CMD>",
                state,
                library,
                out var feedback);

            Assert.AreEqual(string.Empty, visible);
            Assert.IsTrue(string.IsNullOrWhiteSpace(feedback), feedback);
            Assert.AreEqual("获得道具：祝余 x2", toastMessage);
        }
        finally
        {
            EventCenter.Instance.RemoveListener("OnCenterToast", listener);
            EventCenter.Instance.Clear();
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TryAddLocalItem_BroadcastsCenterToast_AndSkipsToastWhenInventoryFull()
    {
        EventCenter.Instance.Clear();

        var state = new RoleState
        {
            equipment = new EquipmentState()
        };
        InventoryStateUtility.EnsureCompatibility(state, null);
        var go = new GameObject("IARLocalItemToastTest");
        string toastMessage = null;
        System.Action<string> listener = message => toastMessage = message;

        try
        {
            EventCenter.Instance.AddListener("OnCenterToast", listener);

            var processor = go.AddComponent<IARProcessor>();
            var method = typeof(IARProcessor).GetMethod("TryAddLocalItem", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            bool added = (bool)method.Invoke(processor, new object[] { state, "zhuyu_herb", "祝余", "草叶微青", "食之可暂缓饥乏。" });
            Assert.IsTrue(added);
            Assert.AreEqual("获得道具：祝余", toastMessage);

            toastMessage = null;
            while (state.equipment.inventoryEntries.Count < InventoryStateUtility.InventoryCapacity)
            {
                state.equipment.inventoryEntries.Add(InventoryStateUtility.CreateEntryFromTemplate(
                    "dummy_" + state.equipment.inventoryEntries.Count,
                    "占位物品",
                    "占位",
                    "普通",
                    string.Empty,
                    null));
            }

            bool rejectedByCapacity = (bool)method.Invoke(processor, new object[] { state, "migu_branch", "迷谷", "枝叶黑理", "可在雾中辨认路径。" });
            Assert.IsFalse(rejectedByCapacity);
            Assert.IsNull(toastMessage);
        }
        finally
        {
            EventCenter.Instance.RemoveListener("OnCenterToast", listener);
            EventCenter.Instance.Clear();
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void AnalyzeAndApplyAIResult_RejectsOutOfSceneItemWithSanitizedFeedback()
    {
        var library = CreateTestLibrary();
        var state = new RoleState();
        InventoryStateUtility.EnsureCompatibility(state, library);
        var go = new GameObject("IARRejectItemTest");

        try
        {
            var processor = go.AddComponent<IARProcessor>();
            processor.AnalyzeAndApplyAIResult(
                "<CMD>{\"get_item\":{\"template_id\":\"unique_branch\",\"count\":1}}</CMD>",
                state,
                library,
                out var feedback);

            Assert.AreEqual("已拒绝加入场景物品库之外的物品。", feedback);
            Assert.IsFalse(feedback.Contains("unique_branch"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void DirectUseInventoryItem_ConsumesHealingPotionAndRestoresHealth()
    {
        var library = CreateTestLibrary();
        var state = new RoleState
        {
            attributes = new AttributeState
            {
                currentHealth = 70,
                maxHealth = 100,
                currentMana = 50,
                maxMana = 50,
                strength = 10,
                agility = 8,
                intelligence = 12
            },
            equipment = new EquipmentState()
        };
        state.equipment.inventoryEntries.Add(InventoryStateUtility.CreateEntryFromTemplate(
            "healing_potion",
            "治疗药水",
            "止血用药液",
            "普通",
            "饮下后回暖",
            null));
        InventoryStateUtility.EnsureCompatibility(state, library);

        var env = EnvironmentState.GetDefault();
        var go = new GameObject("IARProcessorTest");

        try
        {
            var processor = go.AddComponent<IARProcessor>();
            bool used = processor.TryUseInventoryItemDirect(state, env, state.equipment.inventoryEntries[0], library, out var message);

            Assert.IsTrue(used, message);
            Assert.AreEqual(88, state.attributes.currentHealth);
            Assert.AreEqual(0, state.equipment.inventoryEntries.Count);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void MainGamePanel_ShowLoading_OnlyLocksSendControls()
    {
        var go = new GameObject("MainGamePanelLoadingTest", typeof(CanvasGroup));

        try
        {
            var panel = go.AddComponent<MainGamePanel>();
            panel.sendButton = CreateButton("SendButton", go.transform);
            panel.inputField = CreateInputField("InputField", go.transform);
            panel.historyButton = CreateButton("HistoryButton", go.transform);
            panel.hintButton = CreateButton("HintButton", go.transform);
            panel.knowledgeGraphButton = CreateButton("KnowledgeButton", go.transform);
            panel.characterButton = CreateButton("CharacterButton", go.transform);
            panel.bagButton = CreateButton("BagButton", go.transform);

            panel.ShowLoading(true);

            Assert.IsFalse(panel.sendButton.interactable);
            Assert.IsFalse(panel.inputField.interactable);
            Assert.IsTrue(panel.historyButton.interactable);
            Assert.IsTrue(panel.hintButton.interactable);
            Assert.IsTrue(panel.knowledgeGraphButton.interactable);
            Assert.IsTrue(panel.characterButton.interactable);
            Assert.IsTrue(panel.bagButton.interactable);

            panel.ShowLoading(false);

            Assert.IsTrue(panel.sendButton.interactable);
            Assert.IsTrue(panel.inputField.interactable);
            Assert.IsTrue(panel.historyButton.interactable);
            Assert.IsTrue(panel.hintButton.interactable);
            Assert.IsTrue(panel.knowledgeGraphButton.interactable);
            Assert.IsTrue(panel.characterButton.interactable);
            Assert.IsTrue(panel.bagButton.interactable);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    private static Button CreateButton(string name, Transform parent)
    {
        var button = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button)).GetComponent<Button>();
        button.transform.SetParent(parent, false);
        return button;
    }

    private static TMP_InputField CreateInputField(string name, Transform parent)
    {
        var inputField = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField)).GetComponent<TMP_InputField>();
        inputField.transform.SetParent(parent, false);
        return inputField;
    }

    private static SceneItemLibraryData CreateTestLibrary()
    {
        var library = ScriptableObject.CreateInstance<SceneItemLibraryData>();
        library.sceneId = "loc_zhaoyao";
        library.items = new List<ItemTemplateData>
        {
            new ItemTemplateData
            {
                templateId = "healing_potion",
                displayName = "治疗药水",
                itemKind = ItemKind.Consumable,
                equipSlot = EquipSlotType.None,
                stackable = true,
                allowedSceneId = "loc_zhaoyao"
            },
            new ItemTemplateData
            {
                templateId = "bronze_broken_sword",
                displayName = "青铜断剑",
                itemKind = ItemKind.Equipment,
                equipSlot = EquipSlotType.Weapon,
                stackable = false,
                allowedSceneId = "loc_zhaoyao"
            },
            new ItemTemplateData
            {
                templateId = "zhuyu_herb",
                displayName = "祝余",
                itemKind = ItemKind.Consumable,
                equipSlot = EquipSlotType.None,
                stackable = true,
                allowedSceneId = "loc_zhaoyao"
            }
        };
        library.EnsureIndex();
        return library;
    }
}
#endif
