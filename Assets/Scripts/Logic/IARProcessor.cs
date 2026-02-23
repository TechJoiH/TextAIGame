using UnityEngine;
using StateData.Role;
using StateData.Environment;
using Logic.Intent;
using LitJson;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public class IARProcessor : MonoSingleton<IARProcessor>
{
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
        envState = envState ?? EnvironmentState.GetDefault();

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

            case ActionType.Observe:
                if (env.isDark || intent.parameters.ContainsKey("env_vision_limited"))
                    return "观察行动：视野受限，AI应描述黑暗中的感官细节";
                if (env.isFoggy)
                    return "观察行动：迷雾遮蔽，AI应描述朦胧模糊的景象";
                return "观察行动：AI应提供周围环境细节";

            case ActionType.Move:
                string dir = intent.parameters.ContainsKey("direction") ? intent.parameters["direction"] : "前方";
                if (intent.parameters.ContainsKey("env_fog_risk"))
                    return $"移动方向: {dir} (迷雾中可能迷失)";
                return $"移动方向: {dir}";

            default:
                return null;
        }
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
                ApplyCommandToState(jsonCmd, state);
                Debug.Log($"<color=cyan>[IAR] 提取并执行指令: {jsonCmd}</color>");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[IAR] 指令解析失败: {e.Message}");
            }

            return aiFullResponse.Replace(match.Value, "").Trim();
        }

        return aiFullResponse;
    }

    /// <summary>
    /// 执行具体数值变更
    /// </summary>
    private void ApplyCommandToState(string jsonStr, RoleState state)
    {
        JsonData data = JsonMapper.ToObject(jsonStr);

        if (data.Keys.Contains("hp"))
        {
            int val = (int)data["hp"];
            state.attributes.currentHealth += val;
            state.attributes.currentHealth = Mathf.Clamp(state.attributes.currentHealth, 0, state.attributes.maxHealth);
            
            if (state.attributes.currentHealth <= 0)
                state.runtime.isAlive = false;
            else if (state.attributes.currentHealth <= state.attributes.maxHealth * 0.2f)
                state.runtime.isCriticalState = true;
        }

        if (data.Keys.Contains("mp"))
        {
            int val = (int)data["mp"];
            state.attributes.currentMana += val;
            state.attributes.currentMana = Mathf.Clamp(state.attributes.currentMana, 0, state.attributes.maxMana);
        }

        if (data.Keys.Contains("exp"))
        {
            int val = (int)data["exp"];
            state.attributes.currentExp += val;
            CheckLevelUp(state);
        }

        if (data.Keys.Contains("get_item"))
        {
            JsonData itemData = data["get_item"];
            string itemName = (string)itemData["name"];
            if (state.equipment.inventory == null)
                state.equipment.inventory = new System.Collections.Generic.List<string>();
            state.equipment.inventory.Add(itemName);
            Debug.Log($"<color=yellow>[IAR] 获得物品: {itemName}</color>");
        }

        if (data.Keys.Contains("lose_item"))
        {
            string itemName = (string)data["lose_item"];
            state.equipment.inventory?.Remove(itemName);
        }
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