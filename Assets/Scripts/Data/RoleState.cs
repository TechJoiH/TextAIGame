using System;
using System.Collections.Generic;

namespace StateData.Role
{
    /// <summary>
    /// 角色“数据状态快照”：用于存档/同步/AI Prompt 输入。
    /// 注意：它不是 FSM 的 IState。
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
        public StatusEffectState statusEffects;
    }

    [Serializable]
    public sealed class IdentityState
    {
        public string name;
        public string roleType;   // 玩家 / 队友 / NPC / 敌人
        public string race;       // 种族 / 血统
        public string faction;    // 阵营 / 派别
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
        public string cultivationSchool; // 修炼流派
        public int cultivationStage;     // 境界阶段（后续可以替换为 enum）
    }

    [Serializable]
    public sealed class SocialState
    {
        public int loyalty;    // 忠诚度
        public int affection;  // 对主角好感
    }

    [Serializable]
    public sealed class EquipmentState
    {
        public string weapon;
        public List<string> equippedSkills = new List<string>();
        public List<string> inventory = new List<string>();
    }

    [Serializable]
    public sealed class RuntimeFlags
    {
        public bool isAlive = true;
        public bool isCriticalState;
        public bool hasMajorChange;
    }
    [Serializable]
    public class StatusEffectState
    {
        public List<StatusEffect> activeEffects = new();
    }
    [Serializable]
    public class StatusEffect
    {
        public string id;                // 唯一标识（如：burn_lifespan）
        public string name;              // 显示名（寿元燃烧）
        public string type;              // Buff / Debuff / Curse / Blessing

        public string source;            // 来源（神器 / 神兽 / 世界法则）
        public string description;       // 给 AI 看的叙事说明

        public int duration;             // 剩余回合（-1 表示长期/不可逆）
        public bool isHidden;             // 是否对玩家隐藏（AI仍可见）

        public bool affectsNarrative;     // 是否影响叙事走向（关键）
    }

}