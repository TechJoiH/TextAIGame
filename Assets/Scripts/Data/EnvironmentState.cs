using System;
using System.Collections.Generic;
using StateData.Role;
using UnityEngine;

namespace StateData.Environment
{
    public enum WeatherType { Clear, Foggy, Rainy, Stormy }
    public enum TimeOfDay { Dawn, Day, Dusk, Night }

    [Serializable]
    public sealed class EnvironmentState
    {
        public string locationId;
        public string locationName;
        public string biome;
        public string weather;
        public string timeOfDay;
        public string narrativeHint;
        public string currentObjective;
        public List<string> dynamicTags = new List<string>();
        public List<string> unlockedClues = new List<string>();

        public bool isWet;
        public bool isDark;
        public bool isWindy;
        public bool isFoggy;

        public void EnsureCollections()
        {
            dynamicTags ??= new List<string>();
            unlockedClues ??= new List<string>();

            AddUnique(dynamicTags, GetWeatherTag(weather));
            AddUnique(dynamicTags, GetTimeTag(timeOfDay));
            AddUnique(dynamicTags, biome);
        }

        public void AddTag(string tag)
        {
            AddUnique(dynamicTags, tag);
        }

        public bool HasTag(string tag)
        {
            return ContainsValue(dynamicTags, tag);
        }

        public bool RemoveTag(string tag)
        {
            if (dynamicTags == null || string.IsNullOrWhiteSpace(tag))
                return false;

            return dynamicTags.RemoveAll(item => string.Equals(item, tag, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        public void AddClue(string clue)
        {
            AddUnique(unlockedClues, clue);
        }

        public bool HasClue(string clue)
        {
            return ContainsValue(unlockedClues, clue);
        }

        public static EnvironmentState FromData(EnvironmentData data)
        {
            if (data == null)
                return GetDefault();

            var state = new EnvironmentState
            {
                locationId = data.locationId,
                locationName = data.locationName,
                biome = data.biome,
                weather = data.weather.ToString(),
                timeOfDay = data.timeOfDay.ToString(),
                narrativeHint = data.narrativeHint,
                currentObjective = "观察招摇山雾中的草木，找到继续深入山腹的线索。",
                isWet = data.weather == WeatherType.Rainy || data.weather == WeatherType.Stormy || data.biome == "沼泽",
                isDark = data.timeOfDay == TimeOfDay.Night || data.timeOfDay == TimeOfDay.Dusk,
                isWindy = data.weather == WeatherType.Stormy,
                isFoggy = data.weather == WeatherType.Foggy,
                dynamicTags = new List<string>(),
                unlockedClues = new List<string>()
            };

            state.EnsureCollections();
            return state;
        }

        public static EnvironmentState GetDefault()
        {
            var state = new EnvironmentState
            {
                locationId = "unknown",
                locationName = "未知之地",
                biome = "荒野",
                weather = WeatherType.Clear.ToString(),
                timeOfDay = TimeOfDay.Day.ToString(),
                narrativeHint = string.Empty,
                currentObjective = "先确认周围环境，再决定下一步行动。",
                isWet = false,
                isDark = false,
                isWindy = false,
                isFoggy = false,
                dynamicTags = new List<string>(),
                unlockedClues = new List<string>()
            };

            state.EnsureCollections();
            return state;
        }

        private static void AddUnique(List<string> values, string candidate)
        {
            if (values == null || string.IsNullOrWhiteSpace(candidate) || ContainsValue(values, candidate))
                return;

            values.Add(candidate.Trim());
        }

        private static bool ContainsValue(List<string> values, string candidate)
        {
            if (values == null || string.IsNullOrWhiteSpace(candidate))
                return false;

            foreach (var value in values)
            {
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string GetWeatherTag(string rawWeather)
        {
            return rawWeather switch
            {
                nameof(WeatherType.Foggy) => "晨雾",
                nameof(WeatherType.Rainy) => "潮湿",
                nameof(WeatherType.Stormy) => "狂风",
                nameof(WeatherType.Clear) => "清朗",
                _ => string.Empty,
            };
        }

        private static string GetTimeTag(string rawTime)
        {
            return rawTime switch
            {
                nameof(TimeOfDay.Dawn) => "破晓",
                nameof(TimeOfDay.Day) => "白昼",
                nameof(TimeOfDay.Dusk) => "薄暮",
                nameof(TimeOfDay.Night) => "夜色",
                _ => string.Empty,
            };
        }
    }

    [Serializable]
    public sealed class ScenarioConfig
    {
        public string projectTitle = "键入佳境";
        public string projectSubtitle = "山海经文字冒险";
        public string systemTagline = "智能裁决 / 知识图谱";
        public string chapterTitle = "大荒萤火";
        public string openingNarration =
            "你缓缓睁眼，晨雾正沿着山脊与松根游走。招摇山的风带着草木汁液的清苦，像某种活物在耳畔低伏呼吸。";
        public string openingNotice =
            "当前演示切片聚焦招摇山：环境观察、灵草采集与异象遭遇。";
        public string environmentResourcePath = "Configs/ZhaoYaoShanEnvironment";
        public string itemLibraryResourcePath = "Configs/ZhaoYaoShanItemLibrary";
        public ScenarioRoleData initialRole = ScenarioRoleData.CreateDefault();
        public List<string> initialDiscoveredEntityIds = new List<string> { "loc_zhaoyao" };

        public static ScenarioConfig GetDefault()
        {
            return new ScenarioConfig();
        }

        public void EnsureDefaults()
        {
            if (string.IsNullOrWhiteSpace(projectTitle))
                projectTitle = "键入佳境";
            if (string.IsNullOrWhiteSpace(projectSubtitle))
                projectSubtitle = "山海经文字冒险";
            if (string.IsNullOrWhiteSpace(systemTagline))
                systemTagline = "智能裁决 / 知识图谱";
            if (string.IsNullOrWhiteSpace(chapterTitle))
                chapterTitle = "大荒萤火";
            if (string.IsNullOrWhiteSpace(environmentResourcePath))
                environmentResourcePath = "Configs/ZhaoYaoShanEnvironment";
            if (string.IsNullOrWhiteSpace(itemLibraryResourcePath))
                itemLibraryResourcePath = "Configs/ZhaoYaoShanItemLibrary";

            initialRole ??= ScenarioRoleData.CreateDefault();
            initialDiscoveredEntityIds ??= new List<string>();
        }

        public RoleState BuildRoleState()
        {
            EnsureDefaults();
            return initialRole.ToRoleState();
        }
    }

    [Serializable]
    public sealed class ScenarioRoleData
    {
        public string name = "林渊";
        public string roleType = "玩家";
        public string race = "凡人";
        public string faction = "无门散修";
        public int level = 1;
        public int currentExp;
        public int expToNextLevel = 100;
        public int currentHealth = 80;
        public int maxHealth = 100;
        public int currentMana = 50;
        public int maxMana = 50;
        public int strength = 10;
        public int agility = 8;
        public int intelligence = 12;
        public string cultivationSchool = "散修吐纳";
        public int cultivationStage = 1;
        public int loyalty;
        public int affection;
        public string weapon = "";
        public List<string> inventory = new List<string> { "治疗药水", "青铜断剑" };
        public List<string> equippedSkills = new List<string> { "火球术", "御风诀" };

        public static ScenarioRoleData CreateDefault()
        {
            return new ScenarioRoleData();
        }

        public RoleState ToRoleState()
        {
            return new RoleState
            {
                identity = new IdentityState
                {
                    name = name,
                    roleType = roleType,
                    race = race,
                    faction = faction,
                },
                attributes = new AttributeState
                {
                    level = level,
                    currentExp = currentExp,
                    expToNextLevel = expToNextLevel,
                    currentHealth = currentHealth,
                    maxHealth = maxHealth,
                    currentMana = currentMana,
                    maxMana = maxMana,
                    strength = strength,
                    agility = agility,
                    intelligence = intelligence,
                },
                cultivation = new CultivationState
                {
                    cultivationSchool = cultivationSchool,
                    cultivationStage = cultivationStage,
                },
                social = new SocialState
                {
                    loyalty = loyalty,
                    affection = affection,
                },
                equipment = new EquipmentState
                {
                    weapon = weapon,
                    inventory = inventory != null ? new List<string>(inventory) : new List<string>(),
                    equippedSkills = equippedSkills != null ? new List<string>(equippedSkills) : new List<string>(),
                    equipmentSlots = new EquipmentSlotsState(),
                    inventoryEntries = new List<ItemInventoryEntry>(),
                },
                runtime = new RuntimeFlags
                {
                    isAlive = true,
                    isCriticalState = false,
                    hasMajorChange = false,
                },
                statusEffects = new StatusEffectState(),
            };
        }
    }
}
