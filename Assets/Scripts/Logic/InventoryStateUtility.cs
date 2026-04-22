using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StateData.Items;
using StateData.Role;
using UnityEngine;

namespace Logic.Inventory
{
    public sealed class DerivedAttributeState
    {
        public int strengthBase;
        public int strengthBonus;
        public int strengthTotal;

        public int agilityBase;
        public int agilityBonus;
        public int agilityTotal;

        public int intelligenceBase;
        public int intelligenceBonus;
        public int intelligenceTotal;

        public int maxHealthBase;
        public int maxHealthBonus;
        public int maxHealthTotal;

        public int maxManaBase;
        public int maxManaBonus;
        public int maxManaTotal;

        public int attackBonus;
        public int attackPower;
    }

    public static class InventoryStateUtility
    {
        public const int InventoryCapacity = 24;

        public static readonly EquipSlotType[] DefaultEquipOrder =
        {
            EquipSlotType.Head,
            EquipSlotType.Body,
            EquipSlotType.Legs,
            EquipSlotType.Feet,
            EquipSlotType.Weapon,
        };

        public static void EnsureCompatibility(RoleState state, SceneItemLibraryData itemLibrary = null)
        {
            if (state == null)
                return;

            state.identity ??= new IdentityState();
            state.attributes ??= new AttributeState();
            state.cultivation ??= new CultivationState();
            state.social ??= new SocialState();
            state.equipment ??= new EquipmentState();
            state.equipment.EnsureCollections();
            state.runtime ??= new RuntimeFlags();
            state.statusEffects ??= new StatusEffectState();
            state.statusEffects.activeEffects ??= new List<StatusEffect>();

            if (state.equipment.inventory != null && state.equipment.inventory.Count > 0)
            {
                foreach (var legacyName in state.equipment.inventory)
                {
                    if (string.IsNullOrWhiteSpace(legacyName))
                        continue;

                    var legacyEntry = CreateLegacyEntry(legacyName, itemLibrary);
                    TryAddInventoryEntry(state, legacyEntry, itemLibrary, out _);
                }

                state.equipment.inventory.Clear();
            }

            if (!string.IsNullOrWhiteSpace(state.equipment.weapon) && state.equipment.equipmentSlots.weapon == null)
            {
                var legacyWeapon = CreateLegacyEntry(state.equipment.weapon, itemLibrary);
                var template = ResolveTemplate(itemLibrary, legacyWeapon);
                if (template != null && template.equipSlot == EquipSlotType.Weapon)
                    state.equipment.equipmentSlots.weapon = CloneEntry(legacyWeapon);
                else
                    TryAddInventoryEntry(state, legacyWeapon, itemLibrary, out _);

                state.equipment.weapon = string.Empty;
            }

            NormalizeEquippedEntries(state, itemLibrary);
            NormalizeResourceCaps(state, CalculateDerivedAttributes(state));
        }

        public static ItemInventoryEntry CreateLegacyEntry(string itemName, SceneItemLibraryData itemLibrary = null)
        {
            var template = itemLibrary != null ? itemLibrary.FindTemplateByDisplayName(itemName) : null;
            string templateId = template != null && !string.IsNullOrWhiteSpace(template.templateId)
                ? template.templateId
                : $"legacy::{SanitizeId(itemName)}";

            return new ItemInventoryEntry
            {
                templateId = templateId,
                count = 1,
                runtimeData = new ItemRuntimeData
                {
                    instanceId = Guid.NewGuid().ToString("N"),
                    name = string.IsNullOrWhiteSpace(itemName) ? "未知物品" : itemName.Trim(),
                    description = template != null && !string.IsNullOrWhiteSpace(template.templateDescription)
                        ? template.templateDescription
                        : "旧存档迁移物品",
                    rarity = "普通",
                    effectText = template != null && template.IsEquipment ? "旧存档装备" : "旧存档物品",
                    statModifiers = BuildDefaultRuntimeModifiers(template)
                }
            };
        }

        public static ItemInventoryEntry CreateEntryFromTemplate(
            string templateId,
            string runtimeName,
            string description,
            string rarity,
            string effectText,
            IEnumerable<ItemStatModifier> modifiers,
            int count = 1)
        {
            return new ItemInventoryEntry
            {
                templateId = templateId,
                count = Mathf.Max(1, count),
                runtimeData = new ItemRuntimeData
                {
                    instanceId = Guid.NewGuid().ToString("N"),
                    name = string.IsNullOrWhiteSpace(runtimeName) ? templateId : runtimeName.Trim(),
                    description = string.IsNullOrWhiteSpace(description) ? "无描述" : description.Trim(),
                    rarity = string.IsNullOrWhiteSpace(rarity) ? "普通" : rarity.Trim(),
                    effectText = string.IsNullOrWhiteSpace(effectText) ? string.Empty : effectText.Trim(),
                    statModifiers = modifiers != null ? new List<ItemStatModifier>(modifiers.Select(CloneModifier)) : new List<ItemStatModifier>()
                }
            };
        }

        public static DerivedAttributeState CalculateDerivedAttributes(RoleState state)
        {
            state?.equipment?.EnsureCollections();

            var derived = new DerivedAttributeState
            {
                strengthBase = state?.attributes?.strength ?? 0,
                agilityBase = state?.attributes?.agility ?? 0,
                intelligenceBase = state?.attributes?.intelligence ?? 0,
                maxHealthBase = state?.attributes?.maxHealth ?? 0,
                maxManaBase = state?.attributes?.maxMana ?? 0,
            };

            if (state?.equipment?.equipmentSlots == null)
            {
                derived.strengthTotal = derived.strengthBase;
                derived.agilityTotal = derived.agilityBase;
                derived.intelligenceTotal = derived.intelligenceBase;
                derived.maxHealthTotal = derived.maxHealthBase;
                derived.maxManaTotal = derived.maxManaBase;
                derived.attackPower = derived.strengthBase;
                return derived;
            }

            foreach (var entry in state.equipment.equipmentSlots.EnumerateEntries())
            {
                if (entry?.runtimeData?.statModifiers == null)
                    continue;

                foreach (var modifier in entry.runtimeData.statModifiers)
                {
                    if (modifier == null || string.IsNullOrWhiteSpace(modifier.statKey))
                        continue;

                    switch (modifier.statKey.Trim().ToLowerInvariant())
                    {
                        case "strength":
                            derived.strengthBonus += modifier.value;
                            break;
                        case "agility":
                            derived.agilityBonus += modifier.value;
                            break;
                        case "intelligence":
                            derived.intelligenceBonus += modifier.value;
                            break;
                        case "max_health":
                            derived.maxHealthBonus += modifier.value;
                            break;
                        case "max_mana":
                            derived.maxManaBonus += modifier.value;
                            break;
                        case "attack_bonus":
                            derived.attackBonus += modifier.value;
                            break;
                    }
                }
            }

            derived.strengthTotal = derived.strengthBase + derived.strengthBonus;
            derived.agilityTotal = derived.agilityBase + derived.agilityBonus;
            derived.intelligenceTotal = derived.intelligenceBase + derived.intelligenceBonus;
            derived.maxHealthTotal = Mathf.Max(1, derived.maxHealthBase + derived.maxHealthBonus);
            derived.maxManaTotal = Mathf.Max(0, derived.maxManaBase + derived.maxManaBonus);
            derived.attackPower = Mathf.Max(1, derived.strengthTotal + derived.attackBonus);
            return derived;
        }

        public static void NormalizeResourceCaps(RoleState state, DerivedAttributeState derived)
        {
            if (state?.attributes == null || derived == null)
                return;

            state.attributes.currentHealth = Mathf.Clamp(state.attributes.currentHealth, 0, derived.maxHealthTotal);
            state.attributes.currentMana = Mathf.Clamp(state.attributes.currentMana, 0, derived.maxManaTotal);
            state.runtime.isCriticalState = derived.maxHealthTotal > 0 &&
                                           state.attributes.currentHealth <= Mathf.Max(1, Mathf.RoundToInt(derived.maxHealthTotal * 0.2f));
            state.runtime.isAlive = state.attributes.currentHealth > 0;
        }

        public static bool TryAddInventoryEntry(RoleState state, ItemInventoryEntry entry, SceneItemLibraryData itemLibrary, out string failReason)
        {
            failReason = null;
            if (state?.equipment == null || entry == null)
            {
                failReason = "Inventory state is not ready.";
                return false;
            }

            state.equipment.EnsureCollections();
            entry.EnsureDefaults();

            var template = ResolveTemplate(itemLibrary, entry);
            if (template != null && template.stackable)
            {
                string signature = BuildRuntimeSignature(entry);
                var existing = state.equipment.inventoryEntries.FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(candidate.templateId, entry.templateId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(BuildRuntimeSignature(candidate), signature, StringComparison.Ordinal));

                if (existing != null)
                {
                    existing.count += Mathf.Max(1, entry.count);
                    return true;
                }
            }

            if (state.equipment.inventoryEntries.Count >= InventoryCapacity)
            {
                failReason = "背包已经满了，无法放入新的物品。";
                return false;
            }

            state.equipment.inventoryEntries.Add(CloneEntry(entry));
            return true;
        }

        public static bool TryRemoveItem(RoleState state, string instanceId, string templateId, int count, out string failReason)
        {
            failReason = null;
            if (state?.equipment == null)
            {
                failReason = "Inventory state is not ready.";
                return false;
            }

            state.equipment.EnsureCollections();
            int requiredCount = Mathf.Max(1, count);

            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                if (TryRemoveByInstance(state.equipment.inventoryEntries, instanceId, requiredCount, out _) ||
                    state.equipment.equipmentSlots.TryClearByInstanceId(instanceId, out _))
                {
                    NormalizeResourceCaps(state, CalculateDerivedAttributes(state));
                    return true;
                }

                failReason = $"未找到 instance_id={instanceId} 对应的物品。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(templateId))
            {
                failReason = "lose_item 缺少可识别的物品标识。";
                return false;
            }

            for (int i = 0; i < state.equipment.inventoryEntries.Count; i++)
            {
                var entry = state.equipment.inventoryEntries[i];
                if (entry == null || !string.Equals(entry.templateId, templateId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entry.count > requiredCount)
                {
                    entry.count -= requiredCount;
                    NormalizeResourceCaps(state, CalculateDerivedAttributes(state));
                    return true;
                }

                state.equipment.inventoryEntries.RemoveAt(i);
                NormalizeResourceCaps(state, CalculateDerivedAttributes(state));
                return true;
            }

            if (state.equipment.equipmentSlots.TryGetByTemplateId(templateId, out var equippedEntry, out var slotType))
            {
                state.equipment.equipmentSlots.SetSlot(slotType, null);
                NormalizeResourceCaps(state, CalculateDerivedAttributes(state));
                return true;
            }

            failReason = $"当前状态中不存在 template_id={templateId} 的物品。";
            return false;
        }

        public static bool TryEquipInventoryItem(RoleState state, int inventoryIndex, SceneItemLibraryData itemLibrary, out string failReason)
        {
            failReason = null;
            if (state?.equipment == null)
            {
                failReason = "Inventory state is not ready.";
                return false;
            }

            state.equipment.EnsureCollections();
            if (inventoryIndex < 0 || inventoryIndex >= state.equipment.inventoryEntries.Count)
            {
                failReason = "目标物品不存在。";
                return false;
            }

            var sourceEntry = state.equipment.inventoryEntries[inventoryIndex];
            var template = ResolveTemplate(itemLibrary, sourceEntry);
            if (template == null || !template.IsEquipment)
            {
                failReason = "该物品不可装备。";
                return false;
            }

            var targetSlot = template.equipSlot;
            var equippedEntry = state.equipment.equipmentSlots.GetSlot(targetSlot);

            state.equipment.inventoryEntries.RemoveAt(inventoryIndex);
            if (equippedEntry != null)
                state.equipment.inventoryEntries.Add(CloneEntry(equippedEntry));

            state.equipment.equipmentSlots.SetSlot(targetSlot, CloneEntry(sourceEntry));
            NormalizeResourceCaps(state, CalculateDerivedAttributes(state));
            return true;
        }

        public static bool TryUnequipSlot(RoleState state, EquipSlotType slotType, SceneItemLibraryData itemLibrary, out string failReason)
        {
            failReason = null;
            if (state?.equipment == null)
            {
                failReason = "Inventory state is not ready.";
                return false;
            }

            state.equipment.EnsureCollections();
            var equippedEntry = state.equipment.equipmentSlots.GetSlot(slotType);
            if (equippedEntry == null)
            {
                failReason = "该装备栏当前为空。";
                return false;
            }

            if (state.equipment.inventoryEntries.Count >= InventoryCapacity)
            {
                failReason = "背包已经满了，无法卸下当前装备。";
                return false;
            }

            state.equipment.inventoryEntries.Add(CloneEntry(equippedEntry));
            state.equipment.equipmentSlots.SetSlot(slotType, null);
            NormalizeResourceCaps(state, CalculateDerivedAttributes(state));
            return true;
        }

        public static ItemTemplateData ResolveTemplate(SceneItemLibraryData itemLibrary, ItemInventoryEntry entry)
        {
            if (entry == null)
                return null;

            if (itemLibrary != null)
            {
                var template = itemLibrary.GetTemplate(entry.templateId);
                if (template != null)
                    return template;

                string runtimeName = entry.runtimeData?.name;
                if (!string.IsNullOrWhiteSpace(runtimeName))
                {
                    template = itemLibrary.FindTemplateByDisplayName(runtimeName);
                    if (template != null)
                    {
                        entry.templateId = template.templateId;
                        return template;
                    }
                }
            }

            return BuildSyntheticTemplate(entry);
        }

        public static ItemTemplateData ResolveTemplateByName(SceneItemLibraryData itemLibrary, string itemName)
        {
            if (itemLibrary == null || string.IsNullOrWhiteSpace(itemName))
                return null;

            return itemLibrary.FindTemplateByDisplayName(itemName);
        }

        public static ItemInventoryEntry FindInventoryEntryByName(RoleState state, string itemName, out int inventoryIndex)
        {
            inventoryIndex = -1;
            if (state?.equipment?.inventoryEntries == null || string.IsNullOrWhiteSpace(itemName))
                return null;

            for (int i = 0; i < state.equipment.inventoryEntries.Count; i++)
            {
                var entry = state.equipment.inventoryEntries[i];
                string displayName = entry?.runtimeData?.name;
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                if (displayName.IndexOf(itemName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    itemName.IndexOf(displayName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    inventoryIndex = i;
                    return entry;
                }
            }

            return null;
        }

        public static bool HasInventoryItem(RoleState state, string itemName)
        {
            return FindInventoryEntryByName(state, itemName, out _) != null;
        }

        public static string BuildInventoryPromptSummary(RoleState state, SceneItemLibraryData itemLibrary)
        {
            if (state?.equipment?.inventoryEntries == null || state.equipment.inventoryEntries.Count == 0)
                return "Backpack is empty.";

            var builder = new StringBuilder();
            for (int i = 0; i < state.equipment.inventoryEntries.Count; i++)
            {
                var entry = state.equipment.inventoryEntries[i];
                if (entry == null)
                    continue;

                var template = ResolveTemplate(itemLibrary, entry);
                builder.Append("- [")
                    .Append(i)
                    .Append("] ")
                    .Append(entry.runtimeData?.name ?? template?.displayName ?? entry.templateId)
                    .Append(" | template=")
                    .Append(entry.templateId)
                    .Append(" | count=")
                    .Append(entry.count)
                    .Append(" | kind=")
                    .Append(template?.itemKind.ToString() ?? "Unknown");

                if (template != null && template.IsEquipment)
                    builder.Append(" | slot=").Append(template.equipSlot);

                if (!string.IsNullOrWhiteSpace(entry.runtimeData?.rarity))
                    builder.Append(" | rarity=").Append(entry.runtimeData.rarity);

                if (!string.IsNullOrWhiteSpace(entry.runtimeData?.effectText))
                    builder.Append(" | effect=").Append(entry.runtimeData.effectText);

                if (entry.runtimeData?.statModifiers != null && entry.runtimeData.statModifiers.Count > 0)
                    builder.Append(" | modifiers=").Append(BuildModifierText(entry.runtimeData.statModifiers));

                builder.AppendLine();
            }

            return builder.ToString().TrimEnd();
        }

        public static string BuildEquipmentPromptSummary(RoleState state, SceneItemLibraryData itemLibrary)
        {
            if (state?.equipment?.equipmentSlots == null)
                return "No equipment slots are available.";

            var builder = new StringBuilder();
            foreach (var slotType in DefaultEquipOrder)
            {
                var entry = state.equipment.equipmentSlots.GetSlot(slotType);
                if (entry == null)
                {
                    builder.Append("- ").Append(slotType).Append(": empty").AppendLine();
                    continue;
                }

                var template = ResolveTemplate(itemLibrary, entry);
                builder.Append("- ").Append(slotType).Append(": ")
                    .Append(entry.runtimeData?.name ?? template?.displayName ?? entry.templateId)
                    .Append(" | template=").Append(entry.templateId);

                if (!string.IsNullOrWhiteSpace(entry.runtimeData?.rarity))
                    builder.Append(" | rarity=").Append(entry.runtimeData.rarity);

                if (entry.runtimeData?.statModifiers != null && entry.runtimeData.statModifiers.Count > 0)
                    builder.Append(" | modifiers=").Append(BuildModifierText(entry.runtimeData.statModifiers));

                builder.AppendLine();
            }

            return builder.ToString().TrimEnd();
        }

        public static string BuildDerivedAttributePromptSummary(RoleState state)
        {
            var derived = CalculateDerivedAttributes(state);
            return
                $"Strength {derived.strengthBase}+{derived.strengthBonus}={derived.strengthTotal}, " +
                $"Agility {derived.agilityBase}+{derived.agilityBonus}={derived.agilityTotal}, " +
                $"Intelligence {derived.intelligenceBase}+{derived.intelligenceBonus}={derived.intelligenceTotal}, " +
                $"MaxHealth {derived.maxHealthBase}+{derived.maxHealthBonus}={derived.maxHealthTotal}, " +
                $"MaxMana {derived.maxManaBase}+{derived.maxManaBonus}={derived.maxManaTotal}, " +
                $"AttackPower={derived.attackPower}";
        }

        public static string BuildModifierText(IEnumerable<ItemStatModifier> modifiers)
        {
            if (modifiers == null)
                return "none";

            var parts = new List<string>();
            foreach (var modifier in modifiers)
            {
                if (modifier == null || string.IsNullOrWhiteSpace(modifier.statKey))
                    continue;

                parts.Add($"{modifier.statKey}:{modifier.value:+#;-#;0}");
            }

            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        public static ItemInventoryEntry CloneEntry(ItemInventoryEntry source)
        {
            if (source == null)
                return null;

            return new ItemInventoryEntry
            {
                templateId = source.templateId,
                count = Mathf.Max(1, source.count),
                runtimeData = source.runtimeData != null
                    ? new ItemRuntimeData
                    {
                        instanceId = source.runtimeData.instanceId,
                        name = source.runtimeData.name,
                        description = source.runtimeData.description,
                        rarity = source.runtimeData.rarity,
                        effectText = source.runtimeData.effectText,
                        statModifiers = source.runtimeData.statModifiers != null
                            ? new List<ItemStatModifier>(source.runtimeData.statModifiers.Select(CloneModifier))
                            : new List<ItemStatModifier>()
                    }
                    : new ItemRuntimeData()
            };
        }

        private static void NormalizeEquippedEntries(RoleState state, SceneItemLibraryData itemLibrary)
        {
            if (state?.equipment?.equipmentSlots == null)
                return;

            foreach (var slotType in DefaultEquipOrder)
            {
                var entry = state.equipment.equipmentSlots.GetSlot(slotType);
                if (entry == null)
                    continue;

                entry.EnsureDefaults();
                var template = ResolveTemplate(itemLibrary, entry);
                if (template != null && template.IsEquipment && template.equipSlot == slotType)
                    continue;

                state.equipment.equipmentSlots.SetSlot(slotType, null);
                TryAddInventoryEntry(state, entry, itemLibrary, out _);
            }
        }

        private static bool TryRemoveByInstance(List<ItemInventoryEntry> inventoryEntries, string instanceId, int count, out ItemInventoryEntry removedEntry)
        {
            removedEntry = null;
            if (inventoryEntries == null || string.IsNullOrWhiteSpace(instanceId))
                return false;

            int index = inventoryEntries.FindIndex(entry =>
                entry != null &&
                entry.runtimeData != null &&
                string.Equals(entry.runtimeData.instanceId, instanceId, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                return false;

            removedEntry = inventoryEntries[index];
            if (removedEntry.count > count)
                removedEntry.count -= count;
            else
                inventoryEntries.RemoveAt(index);
            return true;
        }

        private static ItemTemplateData BuildSyntheticTemplate(ItemInventoryEntry entry)
        {
            return new ItemTemplateData
            {
                templateId = entry?.templateId,
                displayName = entry?.runtimeData?.name ?? entry?.templateId,
                itemKind = ItemKind.Misc,
                equipSlot = EquipSlotType.None,
                stackable = false,
                templateDescription = entry?.runtimeData?.description,
                allowedSceneId = "legacy"
            };
        }

        private static List<ItemStatModifier> BuildDefaultRuntimeModifiers(ItemTemplateData template)
        {
            var modifiers = new List<ItemStatModifier>();
            if (template == null || !template.IsEquipment)
                return modifiers;

            switch (template.equipSlot)
            {
                case EquipSlotType.Head:
                    modifiers.Add(new ItemStatModifier { statKey = "intelligence", value = 2 });
                    break;
                case EquipSlotType.Body:
                    modifiers.Add(new ItemStatModifier { statKey = "max_health", value = 8 });
                    break;
                case EquipSlotType.Legs:
                    modifiers.Add(new ItemStatModifier { statKey = "agility", value = 2 });
                    break;
                case EquipSlotType.Feet:
                    modifiers.Add(new ItemStatModifier { statKey = "agility", value = 1 });
                    break;
                case EquipSlotType.Weapon:
                    modifiers.Add(new ItemStatModifier { statKey = "attack_bonus", value = 6 });
                    break;
            }

            return modifiers;
        }

        private static string BuildRuntimeSignature(ItemInventoryEntry entry)
        {
            var runtime = entry?.runtimeData;
            if (runtime == null)
                return string.Empty;

            var builder = new StringBuilder();
            builder.Append(runtime.name).Append('|')
                .Append(runtime.description).Append('|')
                .Append(runtime.rarity).Append('|')
                .Append(runtime.effectText);

            if (runtime.statModifiers != null)
            {
                foreach (var modifier in runtime.statModifiers.OrderBy(item => item?.statKey))
                {
                    if (modifier == null)
                        continue;

                    builder.Append('|').Append(modifier.statKey).Append(':').Append(modifier.value);
                }
            }

            return builder.ToString();
        }

        private static string SanitizeId(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return "unknown";

            var builder = new StringBuilder();
            foreach (char c in rawValue.Trim())
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
                else if (builder.Length == 0 || builder[builder.Length - 1] != '_')
                    builder.Append('_');
            }

            return builder.ToString().Trim('_');
        }

        private static ItemStatModifier CloneModifier(ItemStatModifier source)
        {
            return source == null
                ? new ItemStatModifier()
                : new ItemStatModifier { statKey = source.statKey, value = source.value };
        }
    }

    public static class AIResponseConsistencyChecker
    {
        private const string ReplacementItemName = "\u67D0\u4EF6\u7269\u54C1";
        private const string FeedbackPrefix = "\u5DF2\u8FC7\u6EE4\u4E0E\u5F53\u524D\u6301\u6709\u9053\u5177\u4E0D\u4E00\u81F4\u7684\u63CF\u8FF0\uFF1A";
        private const string AcquisitionFeedbackPrefix = "\u5DF2\u8FC7\u6EE4\u4E0E\u5F53\u524D\u7ED3\u7B97\u4E0D\u4E00\u81F4\u7684\u83B7\u53D6\u63CF\u8FF0\uFF1A";

        private static readonly Regex ItemUsageRegex = new Regex(
            "(?:\u670D\u4E0B|\u541E\u4E0B|\u996E\u4E0B|\u559D\u4E0B|\u4F7F\u7528|\u53D6\u51FA|\u62FF\u51FA|\u62D4\u51FA|\u6325\u52A8|\u63E1\u7D27|\u88C5\u5907|\u4F69\u6234|\u7A7F\u4E0A|\u62AB\u4E0A)(?<name>[\\u4e00-\\u9fffA-Za-z0-9_]{1,12})",
            RegexOptions.Compiled);

        private static readonly Regex ItemAcquisitionRegex = new Regex(
            "(?:\u83B7\u5F97\u4E86?|\u5F97\u5230\u4E86?|\u62FE\u8D77\u4E86?|\u6361\u8D77\u4E86?|\u6361\u5230\u4E86?|\u91C7\u5F97\u4E86?|\u91C7\u4E0B\u4E86?|\u91C7\u5230\u4E86?)(?:\u4E00(?:\u4E2A|\u682A|\u679D|\u74F6|\u679A|\u628A|\u4EF6|\u5757|\u7247))?(?<name>[\\u4e00-\\u9fffA-Za-z0-9_]{1,12})",
            RegexOptions.Compiled);

        public sealed class ItemSnapshot
        {
            public HashSet<string> itemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> templateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class FilterReport
        {
            public string visibleText;
            public string feedback;
            public bool hasViolation;
        }

        public static ItemSnapshot CaptureSnapshot(RoleState state, SceneItemLibraryData itemLibrary)
        {
            InventoryStateUtility.EnsureCompatibility(state, itemLibrary);

            var snapshot = new ItemSnapshot();
            if (state?.equipment == null)
                return snapshot;

            foreach (var entry in EnumerateEntries(state))
                AddEntry(snapshot, entry, itemLibrary);

            return snapshot;
        }

        public static FilterReport FilterVisibleText(
            string visibleText,
            ItemSnapshot turnStartSnapshot,
            RoleState currentState,
            SceneItemLibraryData itemLibrary)
        {
            string sanitizedText = visibleText ?? string.Empty;
            var report = new FilterReport
            {
                visibleText = sanitizedText,
                feedback = string.Empty,
                hasViolation = false,
            };

            if (string.IsNullOrWhiteSpace(sanitizedText))
                return report;

            var currentSnapshot = CaptureSnapshot(currentState, itemLibrary);
            var allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MergeSnapshotNames(allowedNames, turnStartSnapshot);
            MergeSnapshotNames(allowedNames, currentSnapshot);

            var currentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MergeSnapshotNames(currentNames, currentSnapshot);

            var invalidMentions = new List<string>();
            foreach (Match match in ItemUsageRegex.Matches(sanitizedText))
            {
                if (!match.Success)
                    continue;

                string itemName = match.Groups["name"].Value?.Trim();
                if (string.IsNullOrWhiteSpace(itemName))
                    continue;

                if (MatchesAllowedName(itemName, allowedNames))
                    continue;

                if (!invalidMentions.Contains(itemName))
                    invalidMentions.Add(itemName);
            }

            var invalidAcquisitions = new List<string>();
            foreach (Match match in ItemAcquisitionRegex.Matches(sanitizedText))
            {
                if (!match.Success)
                    continue;

                string itemName = match.Groups["name"].Value?.Trim();
                if (string.IsNullOrWhiteSpace(itemName))
                    continue;

                if (MatchesAllowedName(itemName, currentNames))
                    continue;

                if (!invalidAcquisitions.Contains(itemName))
                    invalidAcquisitions.Add(itemName);
            }

            if (invalidMentions.Count == 0 && invalidAcquisitions.Count == 0)
                return report;

            string filteredText = sanitizedText;
            foreach (string invalidName in invalidMentions
                         .Concat(invalidAcquisitions)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(name => name.Length))
                filteredText = filteredText.Replace(invalidName, ReplacementItemName);

            report.visibleText = filteredText;
            var feedbackParts = new List<string>();
            if (invalidMentions.Count > 0)
                feedbackParts.Add(FeedbackPrefix + string.Join("\u3001", invalidMentions));
            if (invalidAcquisitions.Count > 0)
                feedbackParts.Add(AcquisitionFeedbackPrefix + string.Join("\u3001", invalidAcquisitions));

            report.feedback = string.Join("\uFF1B", feedbackParts);
            report.hasViolation = true;
            return report;
        }

        private static IEnumerable<ItemInventoryEntry> EnumerateEntries(RoleState state)
        {
            if (state?.equipment == null)
                yield break;

            if (state.equipment.inventoryEntries != null)
            {
                foreach (var entry in state.equipment.inventoryEntries)
                {
                    if (entry != null)
                        yield return entry;
                }
            }

            if (state.equipment.equipmentSlots == null)
                yield break;

            foreach (var entry in state.equipment.equipmentSlots.EnumerateEntries())
            {
                if (entry != null)
                    yield return entry;
            }
        }

        private static void AddEntry(ItemSnapshot snapshot, ItemInventoryEntry entry, SceneItemLibraryData itemLibrary)
        {
            if (snapshot == null || entry == null)
                return;

            entry.EnsureDefaults();

            if (!string.IsNullOrWhiteSpace(entry.templateId))
                snapshot.templateIds.Add(entry.templateId.Trim());

            if (!string.IsNullOrWhiteSpace(entry.runtimeData?.name))
                snapshot.itemNames.Add(entry.runtimeData.name.Trim());

            ItemTemplateData template = itemLibrary != null && !string.IsNullOrWhiteSpace(entry.templateId)
                ? itemLibrary.GetTemplate(entry.templateId)
                : null;
            if (!string.IsNullOrWhiteSpace(template?.displayName))
                snapshot.itemNames.Add(template.displayName.Trim());
        }

        private static void MergeSnapshotNames(HashSet<string> target, ItemSnapshot snapshot)
        {
            if (target == null || snapshot == null)
                return;

            foreach (string itemName in snapshot.itemNames)
            {
                if (!string.IsNullOrWhiteSpace(itemName))
                    target.Add(itemName.Trim());
            }
        }

        private static bool MatchesAllowedName(string candidate, HashSet<string> allowedNames)
        {
            if (string.IsNullOrWhiteSpace(candidate) || allowedNames == null || allowedNames.Count == 0)
                return false;

            foreach (string allowedName in allowedNames)
            {
                if (string.IsNullOrWhiteSpace(allowedName))
                    continue;

                if (string.Equals(candidate, allowedName, StringComparison.OrdinalIgnoreCase) ||
                    candidate.Contains(allowedName, StringComparison.OrdinalIgnoreCase) ||
                    allowedName.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
