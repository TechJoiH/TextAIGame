using LitJson;
using StateData.Role;
using StateData.Environment;
using Logic.Intent;

public static class PromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return @"你是《键入佳境》的叙事引擎，当前演示篇章为《大荒蚀灵》。
这是一款以《山海经》为灵感的 AI 文字冒险作品，核心展示能力是 IAR 本地裁决与 GraphRAG-Lite 知识增强。
你不是一个简单的游戏 DM，你是一位**专注于感官沉浸的东方玄幻作家**。

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
2.  **知识增强可见但不出戏**：当提示中出现“GraphRAG-Lite 知识上下文”时，只把它当作可靠参考，不要逐字复述，更不要暴露检索机制本身。
3.  **指令隐形化**：所有的数值结算，必须通过文末的 <CMD>JSON</CMD> 悄悄传递，**绝对**不能出现在正文中。
4.  **避免重复结算**：若“本地逻辑裁决”已经明确执行过物品、经验、血量或环境更新，就不要再输出重复的 CMD。

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
    public static string BuildUserPromptWithIntent(
        string playerInput,
        RoleState state,
        EnvironmentState envState,
        string systemResult,
        IntentResult intent,
        string knowledgeContext)
    {
        string roleStateJson = JsonMapper.ToJson(state);
        string envStateJson = JsonMapper.ToJson(envState ?? EnvironmentState.GetDefault());
        string intentInfo = BuildIntentDescription(intent);
        string envDirective = BuildEnvironmentDirective(envState);
        string knowledgeBlock = string.IsNullOrWhiteSpace(knowledgeContext)
            ? "未命中直接相关条目，请不要凭空扩写额外山海经设定。"
            : knowledgeContext.Trim();

        return $@"
=== 🌍 角色状态 (JSON) ===
{roleStateJson}

=== 🌦️ 环境状态 (JSON) ===
{envStateJson}

=== 🏷️ 动态标签与当前目标 ===
{envDirective}

=== 🎯 意图识别结果 ===
{intentInfo}

=== 📚 GraphRAG-Lite 知识上下文 ===
{knowledgeBlock}

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
3. 若提供了知识上下文，只在真正相关时自然融入，不要硬塞百科说明
4. 优先围绕「动态标签与当前目标」推进，不要突然跳出招摇山切片
5. 聚焦于行动过程和结果的感官描写
如果需要修改状态，请在文末附带 <CMD>...JSON...</CMD>。";
    }

    /// <summary>
    /// 兼容旧版调用（无环境参数）
    /// </summary>
    public static string BuildUserPromptWithIntent(string playerInput, RoleState state, string systemResult, IntentResult intent)
    {
        return BuildUserPromptWithIntent(playerInput, state, EnvironmentState.GetDefault(), systemResult, intent, "");
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

    public static string BuildHintPrompt(RoleState state, EnvironmentState envState = null, string knowledgeContext = null)
    {
        string stateJson = JsonMapper.ToJson(state);
        EnvironmentState runtimeEnv = envState ?? EnvironmentState.GetDefault();
        string envJson = JsonMapper.ToJson(runtimeEnv);
        string envDirective = BuildEnvironmentDirective(runtimeEnv);
        string knowledgeBlock = string.IsNullOrWhiteSpace(knowledgeContext)
            ? "无额外知识命中"
            : knowledgeContext.Trim();
        return $@"
你是《键入佳境》的行动建议辅助 AI。
当前玩家角色状态如下 (JSON):
{stateJson}

当前环境状态如下 (JSON):
{envJson}

当前动态标签与目标：
{envDirective}

相关山海经知识参考：
{knowledgeBlock}

请根据当前角色的状态、属性、所处环境与知识上下文，**仅仅**推荐 3 个玩家下一步可以采取的合理行动。
要求：
1. 简短有力（不超过10个字）。
2. 需考虑环境因素（如雨天不建议生火、夜晚注意视野等）。
3. 格式必须为 JSON 数组，例如：[""查看周围"", ""使用治疗药水"", ""向东探索""]。
4. 不要包含任何其他解释文字。";
    }

    private static string BuildEnvironmentDirective(EnvironmentState envState)
    {
        envState ??= EnvironmentState.GetDefault();
        envState.EnsureCollections();

        string target = string.IsNullOrWhiteSpace(envState.currentObjective)
            ? "自由探索"
            : envState.currentObjective;
        string tags = envState.dynamicTags != null && envState.dynamicTags.Count > 0
            ? string.Join("、", envState.dynamicTags)
            : "暂无";
        string clues = envState.unlockedClues != null && envState.unlockedClues.Count > 0
            ? string.Join("、", envState.unlockedClues)
            : "暂无";
        string hint = string.IsNullOrWhiteSpace(envState.narrativeHint)
            ? "无"
            : envState.narrativeHint;

        return $"当前目标: {target}\n动态标签: {tags}\n已解锁线索: {clues}\n环境提示: {hint}";
    }
}
