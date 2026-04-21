using UnityEngine;
using StateData.Role;
using StateData.Environment;
using Logic.Intent;
using LitJson;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;

public class IARProcessor : MonoSingleton<IARProcessor>
{
    private static readonly HashSet<string> AllowedCommandKeys = new HashSet<string>
    {
        "hp", "mp", "exp", "get_item", "lose_item"
    };

    private static readonly HashSet<string> AllowedItemKeys = new HashSet<string>
    {
        "name", "desc", "count"
    };

    // 行动消耗表
    private static readonly Dictionary<ActionType, ActionCost> ActionCosts = new Dictionary<ActionType, ActionCost>
    {
        { ActionType.Attack, new ActionCost { manaCost = 5, healthRisk = true } },
        { ActionType.Defend, new ActionCost { manaCost = 3, healthRisk = false } },
        { ActionType.UseSkill, new ActionCost { manaCost = 15, healthRisk = true } },
        { ActionType.Cultivate, new ActionCost { manaCost = 0, requiresSafety = true } },
        { ActionType.Rest, new ActionCost { manaCost = 0, requiresSafety = true } },
    };

    // 受环境影响的技能标签
    private static readonly HashSet<string> FireSkills = new HashSet<string> { "火", "焰", "炎", "烈", "灼", "燃" };
    private static readonly HashSet<string> WindSkills = new HashSet<string> { "风", "气", "旋", "岚" };
    private static readonly HashSet<string> LightSkills = new HashSet<string> { "光", "明", "耀", "辉", "阳" };

    private const string ZhuyuItemName = "祝余";
    private const string MiguItemName = "迷谷";
    private const string HealPotionName = "治疗药水";

    private struct ActionCost
    {
        public int manaCost;
        public bool healthRisk;
        public bool requiresSafety;
    }

    /// <summary>
    /// 1. 意图识别 + 行动合法性校验 (支持环境因素)
    /// </summary>
    public bool CheckActionValidity(string inputAction, RoleState currentState, EnvironmentState envState, out string failReason, out IntentResult intent)
    {
        failReason = "";
        intent = IntentRecognizer.Instance.Recognize(inputAction);
        return CheckActionValidity(intent, currentState, envState, out failReason);
    }

    public bool CheckActionValidity(IntentResult intent, RoleState currentState, EnvironmentState envState, out string failReason)
    {
        failReason = "";
        intent ??= new IntentResult();
        envState = envState ?? EnvironmentState.GetDefault();
        envState.EnsureCollections();

        // 生死状态检查
        if (currentState.attributes.currentHealth <= 0)
        {
            failReason = "你的身体已经无法支撑任何行动，意识渐渐沉入黑暗...";
            currentState.runtime.isAlive = false;
            return false;
        }

        // 根据动作类型进行特定校验
        if (!ValidateActionByType(intent, currentState, out failReason))
            return false;

        // ★ 环境因素校验 ★
        if (!ValidateActionByEnvironment(intent, envState, out failReason))
            return false;

        // 灵力检查
        if (ActionCosts.TryGetValue(intent.actionType, out var cost))
        {
            if (currentState.attributes.currentMana < cost.manaCost)
            {
                failReason = $"灵力不足，无法执行此行动（需要 {cost.manaCost}，当前 {currentState.attributes.currentMana}）";
                return false;
            }
        }

        // 濒死状态限制
        if (currentState.runtime.isCriticalState)
        {
            if (intent.actionType == ActionType.Attack || intent.actionType == ActionType.UseSkill)
            {
                failReason = "伤势过重，身体不允许进行如此激烈的行动...";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 兼容旧版调用（无环境参数）
    /// </summary>
    public bool CheckActionValidity(string inputAction, RoleState currentState, out string failReason, out IntentResult intent)
    {
        return CheckActionValidity(inputAction, currentState, null, out failReason, out intent);
    }

    public bool CheckActionValidity(IntentResult intent, RoleState currentState, out string failReason)
    {
        return CheckActionValidity(intent, currentState, null, out failReason);
    }

    /// <summary>
    /// ★ 环境因素校验逻辑 ★
    /// </summary>
    private bool ValidateActionByEnvironment(IntentResult intent, EnvironmentState env, out string failReason)
    {
        failReason = "";

        // 技能/攻击受环境影响
        if (intent.actionType == ActionType.UseSkill || intent.actionType == ActionType.Attack)
        {
            string skillName = intent.parameters.ContainsKey("skill_name")
                ? intent.parameters["skill_name"]
                : intent.targetEntity ?? "";

            // 雨天/潮湿环境：火系法术效果削弱或失效
            if (env.isWet && ContainsAnyTag(skillName, FireSkills))
            {
                failReason = "雨水浸透了灵气，火焰术法的威力被大幅削弱，火星在指尖挣扎着熄灭...";
                return false;
            }

            // 大风环境：风系法术增强，但精细控制困难
            if (env.isWindy && ContainsAnyTag(skillName, WindSkills))
            {
                // 不阻止，但添加标记让后续处理知道
                intent.parameters["env_boost_wind"] = "true";
            }

            // 黑暗环境：光系法术更明显，但暴露位置
            if (env.isDark && ContainsAnyTag(skillName, LightSkills))
            {
                intent.parameters["env_exposure"] = "true";
            }
        }

        // 迷雾环境：探索/移动可能迷失方向
        if (env.isFoggy && (intent.actionType == ActionType.Move || intent.actionType == ActionType.Explore))
        {
            intent.parameters["env_fog_risk"] = "true";
        }

        // 黑暗环境：观察效果受限
        if (env.isDark && intent.actionType == ActionType.Observe)
        {
            intent.parameters["env_vision_limited"] = "true";
        }

        // 潮湿环境：休息恢复效果降低
        if (env.isWet && intent.actionType == ActionType.Rest)
        {
            intent.parameters["env_damp_rest"] = "true";
        }

        return true;
    }

    /// <summary>
    /// 检查技能名是否包含特定标签
    /// </summary>
    private bool ContainsAnyTag(string skillName, HashSet<string> tags)
    {
        if (string.IsNullOrEmpty(skillName)) return false;
        foreach (var tag in tags)
        {
            if (skillName.Contains(tag)) return true;
        }
        return false;
    }

    /// <summary>
    /// 根据动作类型进行特定校验
    /// </summary>
    private bool ValidateActionByType(IntentResult intent, RoleState state, out string failReason)
    {
        failReason = "";

        switch (intent.actionType)
        {
            case ActionType.UseItem:
                if (!string.IsNullOrEmpty(intent.targetEntity))
                {
                    if (state.equipment.inventory == null || 
                        !state.equipment.inventory.Exists(i => i.Contains(intent.targetEntity)))
                    {
                        failReason = $"你翻遍行囊，却没有找到「{intent.targetEntity}」...";
                        return false;
                    }
                }
                break;

            case ActionType.UseSkill:
                string skillName = intent.parameters.ContainsKey("skill_name") 
                    ? intent.parameters["skill_name"] 
                    : intent.targetEntity;
                    
                if (!string.IsNullOrEmpty(skillName) && state.equipment.equippedSkills != null)
                {
                    if (!state.equipment.equippedSkills.Exists(s => s.Contains(skillName)))
                    {
                        failReason = $"你尚未习得「{skillName}」这门功法...";
                        return false;
                    }
                }
                break;

            case ActionType.Attack:
                if (string.IsNullOrEmpty(state.equipment.weapon))
                {
                    intent.parameters["unarmed"] = "true";
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// 2. 确定性逻辑执行 (支持环境修正)
    /// </summary>
    public string ExecuteDeterministicLogic(string inputAction, RoleState currentState, EnvironmentState envState, IntentResult intent)
    {
        var results = new List<string>();
        envState = envState ?? EnvironmentState.GetDefault();
        envState.EnsureCollections();

        // 灵力消耗
        if (ActionCosts.TryGetValue(intent.actionType, out var cost) && cost.manaCost > 0)
        {
            currentState.attributes.currentMana -= cost.manaCost;
            results.Add($"灵力消耗: -{cost.manaCost}");
        }

        // 根据动作类型生成本地裁决结果
        string verdict = GenerateLocalVerdict(intent, currentState, envState);
        if (!string.IsNullOrEmpty(verdict))
            results.Add(verdict);

        // 更新濒死状态
        float healthPercent = (float)currentState.attributes.currentHealth / currentState.attributes.maxHealth;
        currentState.runtime.isCriticalState = healthPercent <= 0.2f;

        if (results.Count == 0)
            return "[系统行动已通过IAR校验，等待叙事生成...]";

        return $"[本地裁决] {string.Join(" | ", results)}";
    }

    /// <summary>
    /// 兼容旧版调用
    /// </summary>
    public string ExecuteDeterministicLogic(string inputAction, RoleState currentState, IntentResult intent)
    {
        return ExecuteDeterministicLogic(inputAction, currentState, null, intent);
    }

    /// <summary>
    /// 生成本地逻辑裁决 (含环境修正)
    /// </summary>
    private string GenerateLocalVerdict(IntentResult intent, RoleState state, EnvironmentState env)
    {
        switch (intent.actionType)
        {
            case ActionType.Attack:
                int baseAtk = state.attributes.strength + (string.IsNullOrEmpty(state.equipment.weapon) ? 0 : 10);
                // 潮湿环境物理攻击略微受阻
                if (env.isWet)
                {
                    baseAtk = Mathf.Max(1, baseAtk - 2);
                    return $"攻击基础伤害确认，基础攻击: {baseAtk} (潮湿环境-2)";
                }
                return $"攻击基础伤害确认，基础攻击: {baseAtk}";

            case ActionType.Defend:
                return "进入防御姿态，伤害减免生效";

            case ActionType.Rest:
                int healAmount = Mathf.Min(10, state.attributes.maxHealth - state.attributes.currentHealth);
                // 潮湿环境休息效果减半
                if (env.isWet || intent.parameters.ContainsKey("env_damp_rest"))
                {
                    healAmount = Mathf.Max(1, healAmount / 2);
                    state.attributes.currentHealth += healAmount;
                    return $"休息恢复生命: +{healAmount} (潮湿环境效果减半)";
                }
                state.attributes.currentHealth += healAmount;
                return $"休息恢复生命: +{healAmount}";

            case ActionType.Cultivate:
                int manaRecover = Mathf.Min(15, state.attributes.maxMana - state.attributes.currentMana);
                state.attributes.currentMana += manaRecover;
                return $"修炼恢复灵力: +{manaRecover}";

            case ActionType.UseItem:
                return HandleUseItem(intent, state, env);

            case ActionType.Observe:
                return HandleObserve(state, env, intent);

            case ActionType.Collect:
                return HandleCollect(intent, state, env);

            case ActionType.Move:
            case ActionType.Explore:
                return HandleTraversal(intent, state, env);

            default:
                return null;
        }
    }

    private string HandleObserve(RoleState state, EnvironmentState env, IntentResult intent)
    {
        var results = new List<string>();

        if (env.isDark || intent.parameters.ContainsKey("env_vision_limited"))
            results.Add("观察行动：视野受限，应聚焦黑暗中的细微声响与触感");
        else if (env.isFoggy)
            results.Add("观察行动：迷雾遮蔽，应描写贴地游走的雾丝与若隐若现的轮廓");
        else
            results.Add("观察行动：AI应提供周围环境细节");

        if (IsInZhaoYao(env) && !env.HasClue("herbs_spotted"))
        {
            env.AddClue("herbs_spotted");
            env.AddTag("灵草踪迹");
            env.currentObjective = "采集祝余或迷谷，为继续深入招摇山迷雾做准备。";
            Logic.GraphRAG.GraphRAGManager.Instance.DiscoverEntity("herb_zhuyu");
            Logic.GraphRAG.GraphRAGManager.Instance.DiscoverEntity("herb_migu");
            results.Add("山壁潮痕之间显出祝余与迷谷的痕迹");
        }
        else if (env.HasClue("deep_path_opened") && !env.HasClue("aberration_foretold"))
        {
            env.AddClue("aberration_foretold");
            env.AddTag("青白异光");
            env.currentObjective = "盯紧异光源头，谨慎判断是否有异兽逼近。";
            results.Add("雾后有青白异光闪灭，林间似有异兽啼响");
        }

        return string.Join("；", results);
    }

    private string HandleCollect(IntentResult intent, RoleState state, EnvironmentState env)
    {
        string targetName = ResolveTargetName(intent);
        if (string.IsNullOrWhiteSpace(targetName))
        {
            if (env.HasClue("herbs_spotted") && !env.HasClue("zhuyu_collected"))
                targetName = ZhuyuItemName;
            else if (env.HasClue("herbs_spotted") && !env.HasClue("migu_collected"))
                targetName = MiguItemName;
        }

        if (ContainsText(targetName, ZhuyuItemName))
        {
            AddInventoryItem(state, ZhuyuItemName);
            Logic.GraphRAG.GraphRAGManager.Instance.DiscoverEntity("herb_zhuyu");
            env.AddClue("zhuyu_collected");
            env.AddTag("祝余已采");
            env.currentObjective = env.HasClue("migu_collected")
                ? "借迷谷辨路，继续向招摇山深处探索。"
                : "继续辨认迷谷，准备在迷雾中稳定前进。";
            AddExperience(state, 8);
            return "采得一株祝余并收入行囊，经验 +8";
        }

        if (ContainsText(targetName, MiguItemName))
        {
            AddInventoryItem(state, MiguItemName);
            Logic.GraphRAG.GraphRAGManager.Instance.DiscoverEntity("herb_migu");
            env.AddClue("migu_collected");
            env.AddTag("迷谷在手");
            env.RemoveTag("迷失方向");
            env.currentObjective = "借迷谷辨路，继续向招摇山深处探索。";
            AddExperience(state, 10);
            return "采得迷谷，可借其辨清雾中路径，经验 +10";
        }

        return "采集动作已确认，但暂未命中关键灵草";
    }

    private string HandleUseItem(IntentResult intent, RoleState state, EnvironmentState env)
    {
        string targetName = ResolveTargetName(intent);

        if (ContainsText(targetName, HealPotionName) && ConsumeInventoryItem(state, HealPotionName))
        {
            int healAmount = Mathf.Min(18, state.attributes.maxHealth - state.attributes.currentHealth);
            state.attributes.currentHealth += healAmount;
            env.AddTag("药气回暖");
            return $"服下治疗药水，生命恢复: +{healAmount}";
        }

        if (ContainsText(targetName, ZhuyuItemName) && ConsumeInventoryItem(state, ZhuyuItemName))
        {
            int healAmount = Mathf.Min(6, state.attributes.maxHealth - state.attributes.currentHealth);
            int manaAmount = Mathf.Min(6, state.attributes.maxMana - state.attributes.currentMana);
            state.attributes.currentHealth += healAmount;
            state.attributes.currentMana += manaAmount;
            env.AddTag("腹中有实");
            env.currentObjective = "体力稍定，可以继续观察或深入迷雾。";
            return $"咽下祝余后气息稍定，生命 +{healAmount}，灵力 +{manaAmount}";
        }

        if (ContainsText(targetName, MiguItemName) && HasInventoryItem(state, MiguItemName))
        {
            env.RemoveTag("迷失方向");
            env.AddTag("迷谷指路");
            env.currentObjective = "沿雾径深入，观察青白异光的来源。";
            return "佩上迷谷后，雾中的路径轮廓逐渐清晰";
        }

        return "使用物品动作已确认，AI应描写器物触感与身体反馈";
    }

    private string HandleTraversal(IntentResult intent, RoleState state, EnvironmentState env)
    {
        string direction = intent.parameters.ContainsKey("direction") ? intent.parameters["direction"] : "前方";

        if (env.isFoggy && !HasInventoryItem(state, MiguItemName) && !env.HasClue("migu_collected"))
        {
            state.attributes.currentHealth = Mathf.Max(1, state.attributes.currentHealth - 3);
            env.AddTag("迷失方向");
            env.currentObjective = "先寻找迷谷或继续观察山壁，以免在雾中折返。";
            return $"朝{direction}试探时被迷雾逼回，瘴气侵体，生命 -3";
        }

        if (IsInZhaoYao(env) && !env.HasClue("deep_path_opened"))
        {
            env.locationName = "招摇山·雾径深处";
            env.narrativeHint = "迷谷映得雾丝分层，山腹深处的青白异光时明时灭，林间偶有尖啼掠过。";
            env.isFoggy = false;
            env.AddClue("deep_path_opened");
            env.AddTag("雾径已明");
            env.AddTag("异兽踪迹");
            env.currentObjective = "沿异光继续探索，准备应对可能的异象或异兽。";
            return $"借助线索朝{direction}深入，成功穿过雾径，并捕捉到异兽活动痕迹";
        }

        if (env.HasClue("deep_path_opened") && !env.HasClue("aberration_triggered"))
        {
            env.AddClue("aberration_triggered");
            env.AddTag("异象迫近");
            env.currentObjective = "观察异光来源，谨慎准备迎接遭遇。";
            return $"朝{direction}再进一步，前方青白异光骤然逼近，遭遇已经临近";
        }

        return $"移动方向: {direction}";
    }

    private static bool IsInZhaoYao(EnvironmentState env)
    {
        return env != null &&
               (!string.IsNullOrWhiteSpace(env.locationId) && env.locationId.Contains("zhaoyao", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(env.locationName) && env.locationName.Contains("招摇山", StringComparison.Ordinal));
    }

    private static string ResolveTargetName(IntentResult intent)
    {
        if (intent == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(intent.targetEntity))
            return intent.targetEntity;

        if (intent.parameters != null)
        {
            if (intent.parameters.TryGetValue("item_name", out string itemName) && !string.IsNullOrWhiteSpace(itemName))
                return itemName;
            if (intent.parameters.TryGetValue("skill_name", out string skillName) && !string.IsNullOrWhiteSpace(skillName))
                return skillName;
        }

        return string.Empty;
    }

    private static bool ContainsText(string source, string keyword)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               !string.IsNullOrWhiteSpace(keyword) &&
               source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddInventoryItem(RoleState state, string itemName)
    {
        state.equipment.inventory ??= new List<string>();
        state.equipment.inventory.Add(itemName);
    }

    private static bool HasInventoryItem(RoleState state, string itemName)
    {
        return state.equipment.inventory != null &&
               state.equipment.inventory.Exists(item => ContainsText(item, itemName));
    }

    private static bool ConsumeInventoryItem(RoleState state, string itemName)
    {
        if (state.equipment.inventory == null)
            return false;

        int index = state.equipment.inventory.FindIndex(item => ContainsText(item, itemName));
        if (index < 0)
            return false;

        state.equipment.inventory.RemoveAt(index);
        return true;
    }

    private void AddExperience(RoleState state, int amount)
    {
        if (amount <= 0)
            return;

        state.attributes.currentExp += amount;
        CheckLevelUp(state);
    }

    // ========== 保留原有的 AI 响应解析逻辑 ==========

    /// <summary>
    /// 3. 解析并应用 AI 返回结果
    /// </summary>
    public string AnalyzeAndApplyAIResult(string aiFullResponse, RoleState state)
    {
        string pattern = @"<CMD>(.*?)</CMD>";
        Match match = Regex.Match(aiFullResponse, pattern, RegexOptions.Singleline);

        if (match.Success)
        {
            string jsonCmd = match.Groups[1].Value;
            try
            {
                if (TryValidateCommandJson(jsonCmd, out string failReason))
                {
                    ApplyCommandToState(jsonCmd, state);
                    Debug.Log($"<color=cyan>[IAR] 提取并执行指令: {jsonCmd}</color>");
                }
                else
                {
                    Debug.LogWarning($"[IAR] 已拒绝非白名单指令: {failReason} | 原始指令: {jsonCmd}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[IAR] 指令解析失败: {e.Message}");
            }

            return aiFullResponse.Replace(match.Value, "").Trim();
        }

        return aiFullResponse;
    }

    public static bool TryValidateCommandJson(string jsonStr, out string failReason)
    {
        failReason = null;
        if (string.IsNullOrWhiteSpace(jsonStr))
        {
            failReason = "空指令";
            return false;
        }

        JsonData data;
        try
        {
            data = JsonMapper.ToObject(jsonStr);
        }
        catch (Exception exception)
        {
            failReason = $"JSON 解析失败: {exception.Message}";
            return false;
        }

        if (data == null || !data.IsObject)
        {
            failReason = "顶层必须是 JSON 对象";
            return false;
        }

        foreach (string key in data.Keys)
        {
            if (!AllowedCommandKeys.Contains(key))
            {
                failReason = $"存在未授权字段: {key}";
                return false;
            }
        }

        if (data.Keys.Contains("get_item"))
        {
            JsonData itemData = data["get_item"];
            if (itemData == null || !itemData.IsObject)
            {
                failReason = "get_item 必须是对象";
                return false;
            }

            foreach (string itemKey in itemData.Keys)
            {
                if (!AllowedItemKeys.Contains(itemKey))
                {
                    failReason = $"get_item 存在未授权字段: {itemKey}";
                    return false;
                }
            }

            if (!itemData.Keys.Contains("name") || itemData["name"] == null || string.IsNullOrWhiteSpace((string)itemData["name"]))
            {
                failReason = "get_item 缺少 name";
                return false;
            }

            if (itemData.Keys.Contains("count") && TryReadInt(itemData["count"], out int count) && count <= 0)
            {
                failReason = "get_item.count 必须大于 0";
                return false;
            }
        }

        if (data.Keys.Contains("lose_item"))
        {
            if (data["lose_item"] == null || string.IsNullOrWhiteSpace((string)data["lose_item"]))
            {
                failReason = "lose_item 必须是非空字符串";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 执行具体数值变更
    /// </summary>
    private void ApplyCommandToState(string jsonStr, RoleState state)
    {
        JsonData data = JsonMapper.ToObject(jsonStr);

        if (data.Keys.Contains("hp"))
        {
            int val = ReadInt(data["hp"]);
            state.attributes.currentHealth += val;
            state.attributes.currentHealth = Mathf.Clamp(state.attributes.currentHealth, 0, state.attributes.maxHealth);
            
            if (state.attributes.currentHealth <= 0)
                state.runtime.isAlive = false;
            else if (state.attributes.currentHealth <= state.attributes.maxHealth * 0.2f)
                state.runtime.isCriticalState = true;
        }

        if (data.Keys.Contains("mp"))
        {
            int val = ReadInt(data["mp"]);
            state.attributes.currentMana += val;
            state.attributes.currentMana = Mathf.Clamp(state.attributes.currentMana, 0, state.attributes.maxMana);
        }

        if (data.Keys.Contains("exp"))
        {
            int val = ReadInt(data["exp"]);
            state.attributes.currentExp += val;
            CheckLevelUp(state);
        }

        if (data.Keys.Contains("get_item"))
        {
            JsonData itemData = data["get_item"];
            string itemName = (string)itemData["name"];
            int itemCount = itemData.Keys.Contains("count") ? Mathf.Max(1, ReadInt(itemData["count"])) : 1;
            if (state.equipment.inventory == null)
                state.equipment.inventory = new System.Collections.Generic.List<string>();
            for (int i = 0; i < itemCount; i++)
                state.equipment.inventory.Add(itemName);
            Debug.Log($"<color=yellow>[IAR] 获得物品: {itemName} x{itemCount}</color>");
        }

        if (data.Keys.Contains("lose_item"))
        {
            string itemName = (string)data["lose_item"];
            state.equipment.inventory?.Remove(itemName);
        }
    }

    private static int ReadInt(JsonData data)
    {
        return TryReadInt(data, out int value) ? value : 0;
    }

    private static bool TryReadInt(JsonData data, out int value)
    {
        value = 0;
        if (data == null)
            return false;

        try
        {
            if (data.IsInt)
            {
                value = (int)data;
                return true;
            }

            if (data.IsLong)
            {
                value = Convert.ToInt32((long)data);
                return true;
            }

            if (data.IsDouble)
            {
                value = Convert.ToInt32((double)data);
                return true;
            }

            if (data.IsString)
            {
                return int.TryParse((string)data, out value);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// 检查升级
    /// </summary>
    private void CheckLevelUp(RoleState state)
    {
        if (state.attributes.expToNextLevel <= 0)
            state.attributes.expToNextLevel = 100;

        while (state.attributes.currentExp >= state.attributes.expToNextLevel)
        {
            state.attributes.currentExp -= state.attributes.expToNextLevel;
            state.attributes.level++;
            state.attributes.expToNextLevel = (int)(state.attributes.expToNextLevel * 1.5f);
            state.attributes.maxHealth += 20;
            state.attributes.maxMana += 10;
            Debug.Log($"<color=magenta>[IAR] 升级! 当前等级: {state.attributes.level}</color>");
        }
    }
}
