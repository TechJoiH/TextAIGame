using System;
using System.Collections.Generic;
using StateData.Items;

namespace StateData.Role
{
    /// <summary>
    /// Serializable role snapshot used by save/load and prompt injection.
    /// </summary>
    [Serializable]
    public sealed class RoleState
    {
        public IdentityState identity = new IdentityState();
        public AttributeState attributes = new AttributeState();
        public CultivationState cultivation = new CultivationState();
        public SocialState social = new SocialState();
        public EquipmentState equipment = new EquipmentState();
        public RuntimeFlags runtime = new RuntimeFlags();
        public StatusEffectState statusEffects = new StatusEffectState();
    }

    [Serializable]
    public sealed class IdentityState
    {
        public string name;
        public string roleType;
        public string race;
        public string faction;
    }

    [Serializable]
    public sealed class AttributeState
    {
        public int level;
        public int currentExp;
        public int expToNextLevel;

        public int currentHealth;
        public int maxHealth;

        public int currentMana;
        public int maxMana;

        public int strength;
        public int agility;
        public int intelligence;
    }

    [Serializable]
    public sealed class CultivationState
    {
        public string cultivationSchool;
        public int cultivationStage;
    }

    [Serializable]
    public sealed class SocialState
    {
        public int loyalty;
        public int affection;
    }

    [Serializable]
    public sealed class EquipmentState
    {
        // Legacy fields retained for config/save migration.
        public string weapon;
        public List<string> equippedSkills = new List<string>();
        public List<string> inventory = new List<string>();

        public EquipmentSlotsState equipmentSlots = new EquipmentSlotsState();
        public List<ItemInventoryEntry> inventoryEntries = new List<ItemInventoryEntry>();

        public void EnsureCollections()
        {
            equippedSkills ??= new List<string>();
            inventory ??= new List<string>();
            equipmentSlots ??= new EquipmentSlotsState();
            inventoryEntries ??= new List<ItemInventoryEntry>();

            equipmentSlots.EnsureDefaults();
            foreach (var entry in inventoryEntries)
                entry?.EnsureDefaults();
        }
    }

    [Serializable]
    public sealed class RuntimeFlags
    {
        public bool isAlive = true;
        public bool isCriticalState;
        public bool hasMajorChange;
    }

    [Serializable]
    public sealed class StatusEffectState
    {
        public List<StatusEffect> activeEffects = new List<StatusEffect>();
    }

    [Serializable]
    public sealed class StatusEffect
    {
        public string id;
        public string name;
        public string type;
        public string source;
        public string description;
        public int duration;
        public bool isHidden;
        public bool affectsNarrative;
    }

    [Serializable]
    public sealed class EquipmentSlotsState
    {
        public ItemInventoryEntry head;
        public ItemInventoryEntry body;
        public ItemInventoryEntry legs;
        public ItemInventoryEntry feet;
        public ItemInventoryEntry weapon;

        public void EnsureDefaults()
        {
            head?.EnsureDefaults();
            body?.EnsureDefaults();
            legs?.EnsureDefaults();
            feet?.EnsureDefaults();
            weapon?.EnsureDefaults();
        }

        public ItemInventoryEntry GetSlot(EquipSlotType slotType)
        {
            return slotType switch
            {
                EquipSlotType.Head => head,
                EquipSlotType.Body => body,
                EquipSlotType.Legs => legs,
                EquipSlotType.Feet => feet,
                EquipSlotType.Weapon => weapon,
                _ => null,
            };
        }

        public void SetSlot(EquipSlotType slotType, ItemInventoryEntry entry)
        {
            switch (slotType)
            {
                case EquipSlotType.Head:
                    head = entry;
                    break;
                case EquipSlotType.Body:
                    body = entry;
                    break;
                case EquipSlotType.Legs:
                    legs = entry;
                    break;
                case EquipSlotType.Feet:
                    feet = entry;
                    break;
                case EquipSlotType.Weapon:
                    weapon = entry;
                    break;
            }

            entry?.EnsureDefaults();
        }

        public IEnumerable<ItemInventoryEntry> EnumerateEntries()
        {
            if (head != null) yield return head;
            if (body != null) yield return body;
            if (legs != null) yield return legs;
            if (feet != null) yield return feet;
            if (weapon != null) yield return weapon;
        }

        public bool TryGetByTemplateId(string templateId, out ItemInventoryEntry entry, out EquipSlotType slotType)
        {
            foreach (var candidateSlot in new[]
                     {
                         EquipSlotType.Head,
                         EquipSlotType.Body,
                         EquipSlotType.Legs,
                         EquipSlotType.Feet,
                         EquipSlotType.Weapon,
                     })
            {
                var candidate = GetSlot(candidateSlot);
                if (candidate == null)
                    continue;

                if (string.Equals(candidate.templateId, templateId, StringComparison.OrdinalIgnoreCase))
                {
                    entry = candidate;
                    slotType = candidateSlot;
                    return true;
                }
            }

            entry = null;
            slotType = EquipSlotType.None;
            return false;
        }

        public bool TryClearByInstanceId(string instanceId, out ItemInventoryEntry removedEntry)
        {
            foreach (var candidateSlot in new[]
                     {
                         EquipSlotType.Head,
                         EquipSlotType.Body,
                         EquipSlotType.Legs,
                         EquipSlotType.Feet,
                         EquipSlotType.Weapon,
                     })
            {
                var candidate = GetSlot(candidateSlot);
                if (candidate?.runtimeData == null)
                    continue;

                if (!string.Equals(candidate.runtimeData.instanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                removedEntry = candidate;
                SetSlot(candidateSlot, null);
                return true;
            }

            removedEntry = null;
            return false;
        }
    }

    [Serializable]
    public sealed class ItemInventoryEntry
    {
        public string templateId;
        public int count = 1;
        public ItemRuntimeData runtimeData = new ItemRuntimeData();

        public void EnsureDefaults()
        {
            count = Math.Max(1, count);
            runtimeData ??= new ItemRuntimeData();
            runtimeData.EnsureDefaults();
        }
    }

    [Serializable]
    public sealed class ItemRuntimeData
    {
        public string instanceId;
        public string name;
        public string description;
        public string rarity;
        public string effectText;
        public List<ItemStatModifier> statModifiers = new List<ItemStatModifier>();

        public void EnsureDefaults()
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                instanceId = Guid.NewGuid().ToString("N");

            name ??= string.Empty;
            description ??= string.Empty;
            rarity ??= "普通";
            effectText ??= string.Empty;
            statModifiers ??= new List<ItemStatModifier>();
        }
    }

    [Serializable]
    public sealed class ItemStatModifier
    {
        public string statKey;
        public int value;
    }
}
