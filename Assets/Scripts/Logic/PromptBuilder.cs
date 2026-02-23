using LitJson;
using StateData.Role;
using StateData.Environment;
using Logic.Intent;

public static class PromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return @"你是由【神经符号架构】驱动的《大荒蚀灵》叙事引擎。
你不是一个简单的游戏DM，你是一位**专注于'感官沉浸'的东方玄幻作家**。

### 🎨 核心美学标准 (Aesthetic Protocol) - 必须严格执行
你的文字必须具备以下特征，与《大荒蚀灵》原作风格保持一致：

1.  **显微镜式环境描写**：
    * ❌ 禁止：你走到森林里，雾很大。
    * ✅ 正确：雾气是从腐殖质深厚的土壤里蒸腾起来的，裹挟着草木根茎断裂后的清甜汁液气息，浓稠如牛乳般贴着地表匍匐。
    * **要求**：多用具象的名词（苔藓、孢子、锈迹、骨骼）来构建画面。

2.  **通感与生理不适 (Cthulhu Tone)**：
    * 强调**触觉**（黏腻、湿滑、粗糙）和**嗅觉**（腥甜、铁锈味、腐烂）。
    * 让自然环境带有一种“活物”的诡异感（例如：雾是活的，山如死兽）。

3.  **去游戏化叙事**：
    * 严禁使用“你获得了”、“你造成了”、“系统提示”等出戏的词汇。
    * 将数值变化转化为**生理反馈**。
        * HP减少 -> 痛感、眩晕、视线模糊、喉头涌上腥甜。
        * 获得物品 -> 指尖触碰到冰凉的硬物、沉甸甸的手感。

4.  **环境感知叙事**：
    * 根据天气/时间/地形调整描写基调：
        * 雨天 -> 潮湿、泥泞、火焰受阻、视野受限
        * 夜晚 -> 阴暗、月光、影子诡异、听觉敏锐
        * 迷雾 -> 方向迷失、若隐若现、回声扭曲

### 🛑 逻辑宪法：数据霸权
1.  **世界状态以 JSON 为准**：玩家若描述与 JSON 冲突的结果（如没死说死了），判定为角色幻觉，**必须驳回**。
2.  **指令隐形化**：所有的数值结算，必须通过文末的 <CMD>JSON</CMD> 悄悄传递，**绝对**不能出现在正文中。

### 🔧 JSON 指令规范 (Neuro-Symbolic Protocol)
仅在状态发生实质变化时，在回复的**最后一行**输出：

- 造成伤害/受伤: <CMD>{""hp"": -10}</CMD>
- 灵力消耗: <CMD>{""mp"": -5}</CMD>
- 获得经验: <CMD>{""exp"": 20}</CMD>
- 获得物品: <CMD>{""get_item"": {""name"": ""青铜断剑"", ""desc"": ""剑身布满绿锈"", ""count"": 1}}</CMD>
- 失去物品: <CMD>{""lose_item"": ""治疗药水""}</CMD>
";
    }

    /// <summary>
    /// 构建增强版用户提示词（包含意图识别结果 + 环境状态）
    /// </summary>
    public static string BuildUserPromptWithIntent(string playerInput, RoleState state, EnvironmentState envState, string systemResult, IntentResult intent)
    {
        string roleStateJson = JsonMapper.ToJson(state);
        string envStateJson = JsonMapper.ToJson(envState ?? EnvironmentState.GetDefault());
        string intentInfo = BuildIntentDescription(intent);

        return $@"
=== 🌍 角色状态 (JSON) ===
{roleStateJson}

=== 🌦️ 环境状态 (JSON) ===
{envStateJson}

=== 🎯 意图识别结果 ===
{intentInfo}

=== ⚖️ 本地逻辑裁决 ===
{systemResult}
(若判定失败或无效，请依据此结果描写，但不要直接暴露系统语言)

=== 👤 玩家原始输入 ===
{playerInput}

=== 🖋️ 沉浸式续写指令 ===
请基于《山海经》苍凉古朴的笔触续写（200字左右）。
**重点**：
1. 仅在环境发生变化时简要描写天气/地形，避免每次重复相同的环境描述
2. 根据「意图识别结果」把握行动类型和目标
3. 聚焦于行动过程和结果的感官描写
如果需要修改状态，请在文末附带 <CMD>...JSON...</CMD>。";
    }

    /// <summary>
    /// 兼容旧版调用（无环境参数）
    /// </summary>
    public static string BuildUserPromptWithIntent(string playerInput, RoleState state, string systemResult, IntentResult intent)
    {
        return BuildUserPromptWithIntent(playerInput, state, EnvironmentState.GetDefault(), systemResult, intent);
    }

    /// <summary>
    /// 构建意图描述文本
    /// </summary>
    private static string BuildIntentDescription(IntentResult intent)
    {
        if (intent == null || intent.actionType == ActionType.Unknown)
            return "未能识别明确意图，请自由发挥";

        string desc = $"动作类型: {GetActionTypeName(intent.actionType)}";
        
        if (!string.IsNullOrEmpty(intent.targetEntity))
            desc += $"\n目标对象: {intent.targetEntity}";

        if (!string.IsNullOrEmpty(intent.actionVerb))
            desc += $"\n动作动词: {intent.actionVerb}";

        if (intent.parameters != null && intent.parameters.Count > 0)
        {
            desc += "\n附加参数:";
            foreach (var kv in intent.parameters)
                desc += $" {kv.Key}={kv.Value}";
        }

        desc += $"\n置信度: {intent.confidence:P0}";
        return desc;
    }

    private static string GetActionTypeName(ActionType type)
    {
        return type switch
        {
            ActionType.Attack => "攻击",
            ActionType.Defend => "防御",
            ActionType.Move => "移动",
            ActionType.Explore => "探索",
            ActionType.UseItem => "使用物品",
            ActionType.UseSkill => "施展技能",
            ActionType.Talk => "对话交互",
            ActionType.Rest => "休息恢复",
            ActionType.Observe => "观察查看",
            ActionType.Collect => "采集收集",
            ActionType.Cultivate => "修炼",
            _ => "未知"
        };
    }

    public static string BuildUserPrompt(string playerInput, RoleState state, string systemResult)
    {
        return BuildUserPromptWithIntent(playerInput, state, systemResult, new IntentResult());
    }

    public static string BuildHintPrompt(RoleState state, EnvironmentState envState = null)
    {
        string stateJson = JsonMapper.ToJson(state);
        string envJson = JsonMapper.ToJson(envState ?? EnvironmentState.GetDefault());
        return $@"
你是一个文字冒险游戏的辅助AI。
当前玩家角色状态如下 (JSON):
{stateJson}

当前环境状态如下 (JSON):
{envJson}

请根据当前角色的状态、属性和所处环境，**仅仅**推荐 3 个玩家下一步可以采取的合理行动。
要求：
1. 简短有力（不超过10个字）。
2. 需考虑环境因素（如雨天不建议生火、夜晚注意视野等）。
3. 格式必须为 JSON 数组，例如：[""查看周围"", ""使用治疗药水"", ""向东探索""]。
4. 不要包含任何其他解释文字。";
    }
}