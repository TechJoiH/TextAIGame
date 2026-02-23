using UnityEngine;
using StateData.Role;
using Logic.Intent;
using LitJson;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public class IARProcessor : MonoSingleton<IARProcessor>
{
    // 动作消耗配置
    private static readonly Dictionary<ActionType, ActionCost> ActionCosts = new Dictionary<ActionType, ActionCost>
    {
        { ActionType.Attack, new ActionCost { manaCost = 5, healthRisk = true } },
        { ActionType.Defend, new ActionCost { manaCost = 3, healthRisk = false } },
        { ActionType.UseSkill, new ActionCost { manaCost = 15, healthRisk = true } },
        { ActionType.Cultivate, new ActionCost { manaCost = 0, requiresSafety = true } },
        { ActionType.Rest, new ActionCost { manaCost = 0, requiresSafety = true } },
    };

    private struct ActionCost
    {
        public int manaCost;
        public bool healthRisk;
        public bool requiresSafety;
    }

    /// <summary>
    /// 1. 意图识别 + 行动合法性校验 (Intent Recognition + Action Check)
    /// </summary>
    public bool CheckActionValidity(string inputAction, RoleState currentState, out string failReason, out IntentResult intent)
    {
        failReason = "";
        intent = IntentRecognizer.Instance.Recognize(inputAction);

        // 基础状态检查：角色是否存活
        if (currentState.attributes.currentHealth <= 0)
        {
            failReason = "你的身体已经无法支撑任何行动，意识逐渐陷入黑暗...";
            currentState.runtime.isAlive = false;
            return false;
        }

        // 根据动作类型进行特定校验
        if (!ValidateActionByType(intent, currentState, out failReason))
            return false;

        // 灵力检查
        if (ActionCosts.TryGetValue(intent.actionType, out var cost))
        {
            if (currentState.attributes.currentMana < cost.manaCost)
            {
                failReason = $"灵力不足，无法执行此行动。（需要 {cost.manaCost} 灵力，当前 {currentState.attributes.currentMana}）";
                return false;
            }
        }

        // 濒死状态限制
        if (currentState.runtime.isCriticalState)
        {
            if (intent.actionType == ActionType.Attack || intent.actionType == ActionType.UseSkill)
            {
                failReason = "你伤势过重，身体不允许进行如此剧烈的行动...";
                return false;
            }
        }

        return true;
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
                // 检查背包是否有该物品
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
                // 检查是否拥有该技能
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
                // 检查是否有武器（可选）
                if (string.IsNullOrEmpty(state.equipment.weapon))
                {
                    // 允许徒手攻击，但添加提示参数
                    intent.parameters["unarmed"] = "true";
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// 2. 确定性逻辑执行 (Deterministic Logic) - 本地裁决层
    /// </summary>
    public string ExecuteDeterministicLogic(string inputAction, RoleState currentState, IntentResult intent)
    {
        var results = new List<string>();

        // 消耗灵力
        if (ActionCosts.TryGetValue(intent.actionType, out var cost) && cost.manaCost > 0)
        {
            currentState.attributes.currentMana -= cost.manaCost;
            results.Add($"灵力消耗: -{cost.manaCost}");
        }

        // 根据动作类型生成本地裁决结果
        string verdict = GenerateLocalVerdict(intent, currentState);
        if (!string.IsNullOrEmpty(verdict))
            results.Add(verdict);

        // 更新濒死状态
        float healthPercent = (float)currentState.attributes.currentHealth / currentState.attributes.maxHealth;
        currentState.runtime.isCriticalState = healthPercent <= 0.2f;

        if (results.Count == 0)
            return "[系统：行动已通过IAR校验，正在推演因果...]";

        return $"[本地裁决] {string.Join(" | ", results)}";
    }

    /// <summary>
    /// 生成本地逻辑裁决
    /// </summary>
    private string GenerateLocalVerdict(IntentResult intent, RoleState state)
    {
        switch (intent.actionType)
        {
            case ActionType.Attack:
                int baseAtk = state.attributes.strength + (string.IsNullOrEmpty(state.equipment.weapon) ? 0 : 10);
                return $"攻击动作已确认，基础威力: {baseAtk}";

            case ActionType.Defend:
                return "进入防御姿态，伤害减免生效";

            case ActionType.Rest:
                int healAmount = Mathf.Min(10, state.attributes.maxHealth - state.attributes.currentHealth);
                state.attributes.currentHealth += healAmount;
                return $"休息恢复生命: +{healAmount}";

            case ActionType.Cultivate:
                int manaRecover = Mathf.Min(15, state.attributes.maxMana - state.attributes.currentMana);
                state.attributes.currentMana += manaRecover;
                return $"修炼恢复灵力: +{manaRecover}";

            case ActionType.Observe:
                return "观察行动，AI应描述周围环境细节";

            case ActionType.Move:
                string dir = intent.parameters.ContainsKey("direction") ? intent.parameters["direction"] : "前方";
                return $"移动方向: {dir}";

            default:
                return null;
        }
    }

    // ========== 保持原有的 AI 结果解析逻辑 ==========

    /// <summary>
    /// 3. 解析并应用 AI 返回结果 (Result Analyzer)
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

        // 处理 HP 变化
        if (data.Keys.Contains("hp"))
        {
            int val = (int)data["hp"];
            state.attributes.currentHealth += val;
            state.attributes.currentHealth = Mathf.Clamp(state.attributes.currentHealth, 0, state.attributes.maxHealth);
            
            // 触发濒死/死亡判定
            if (state.attributes.currentHealth <= 0)
                state.runtime.isAlive = false;
            else if (state.attributes.currentHealth <= state.attributes.maxHealth * 0.2f)
                state.runtime.isCriticalState = true;
        }

        // 处理 MP 变化
        if (data.Keys.Contains("mp"))
        {
            int val = (int)data["mp"];
            state.attributes.currentMana += val;
            state.attributes.currentMana = Mathf.Clamp(state.attributes.currentMana, 0, state.attributes.maxMana);
        }

        // 处理 EXP 变化
        if (data.Keys.Contains("exp"))
        {
            int val = (int)data["exp"];
            state.attributes.currentExp += val;
            CheckLevelUp(state);
        }

        // 处理获得物品
        if (data.Keys.Contains("get_item"))
        {
            JsonData itemData = data["get_item"];
            string itemName = (string)itemData["name"];
            if (state.equipment.inventory == null)
                state.equipment.inventory = new System.Collections.Generic.List<string>();
            state.equipment.inventory.Add(itemName);
            Debug.Log($"<color=yellow>[IAR] 获得物品: {itemName}</color>");
        }

        // 处理失去物品
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