using LitJson;
using Logic.Inventory;
using Logic.Intent;
using StateData.Environment;
using StateData.Items;
using StateData.Role;

public static class PromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return @"你是《键入佳境》的叙事引擎，负责以《山海经》气质继续剧情。

写作要求：
1. 保持沉浸感，用环境、触觉、气味和身体反馈来表达结果，不要说“系统提示”。
2. 不要凭空创造背包里不存在的物品，也不要让角色使用不存在的装备或道具。
3. 只能从“当前场景允许物品模板”里选择掉落物品；每次掉落必须使用模板 id。
4. 如果物品、生命、灵力、经验或装备状态需要变化，只能在回复最后一行输出一个 <CMD>JSON</CMD>。
5. 正文中绝对不能解释 JSON 协议或暴露内部机制。

允许的 <CMD> 结构：
- <CMD>{""hp"": -10}</CMD>
- <CMD>{""mp"": -5}</CMD>
- <CMD>{""exp"": 20}</CMD>
- <CMD>{""get_item"": {""template_id"": ""healing_potion"", ""count"": 1, ""runtime"": {""name"": ""止血药散"", ""desc"": ""药香辛烈，触之微温。"", ""rarity"": ""普通"", ""effect_text"": ""敷于伤口时会带来灼热回暖。"", ""stat_modifiers"": [{""stat"": ""max_health"", ""value"": 5}]}}}</CMD>
- <CMD>{""lose_item"": {""instance_id"": ""item-instance-id""}}</CMD>
- <CMD>{""lose_item"": {""template_id"": ""healing_potion"", ""count"": 1}}</CMD>

规则补充：
- get_item.runtime.name / desc / rarity / effect_text / stat_modifiers 由你生成，本地接收保存。
- stat_modifiers 仅允许这些键：strength, agility, intelligence, max_health, max_mana, attack_bonus。
- 装备栏固定只有 5 个部位：Head, Body, Legs, Feet, Weapon。
- 如果当前场景已经进入新的遭遇、战斗或线索推进，不要机械重复上一轮的草药发现桥段。
- 如果你不确定是否该掉落物品，就不要输出 get_item。";
    }

    public static string BuildUserPromptWithIntent(
        string playerInput,
        RoleState state,
        EnvironmentState envState,
        string systemResult,
        IntentResult intent,
        string knowledgeContext,
        SceneItemLibraryData itemLibrary)
    {
        string roleStateJson = JsonMapper.ToJson(state);
        string envStateJson = JsonMapper.ToJson(envState ?? EnvironmentState.GetDefault());
        string intentInfo = BuildIntentDescription(intent);
        string envDirective = BuildEnvironmentDirective(envState);
        string librarySummary = itemLibrary != null
            ? itemLibrary.BuildPromptSummary()
            : "No scene item library is configured for the current scenario.";
        string inventorySummary = InventoryStateUtility.BuildInventoryPromptSummary(state, itemLibrary);
        string equipmentSummary = InventoryStateUtility.BuildEquipmentPromptSummary(state, itemLibrary);
        string derivedSummary = InventoryStateUtility.BuildDerivedAttributePromptSummary(state);
        string knowledgeBlock = string.IsNullOrWhiteSpace(knowledgeContext)
            ? "未命中直接相关的知识条目，请不要额外扩写设定。"
            : knowledgeContext.Trim();

        return $@"
=== 角色状态(JSON) ===
{roleStateJson}

=== 环境状态(JSON) ===
{envStateJson}

=== 派生属性 ===
{derivedSummary}

=== 当前装备 ===
{equipmentSummary}

=== 当前背包 ===
{inventorySummary}

=== 当前场景允许物品模板 ===
{librarySummary}

=== 动态标签与当前目标 ===
{envDirective}

=== 意图识别结果 ===
{intentInfo}

=== GraphRAG-Lite 知识上下文 ===
{knowledgeBlock}

=== 本地逻辑裁决 ===
{systemResult}
(若本地逻辑已经明确执行或拒绝了某件事，请据此续写，不要重复结算)

=== 玩家原始输入 ===
{playerInput}

=== 续写要求 ===
请基于以上状态继续剧情，保持《山海经》风格的东方奇诡质感，控制在 200 字左右。
重点：
1. 优先围绕当前目标和环境推进，不要突然跳出当前场景。
2. 如果要掉落物品，只能从“当前场景允许物品模板”中选择 template_id。
3. 如果要移除物品，只能移除“当前背包”或“当前装备”里已经存在的物品。
4. 正文只写叙事；若需要状态变化，请只在最后追加一个 <CMD>JSON</CMD>。";
    }

    public static string BuildUserPromptWithIntent(string playerInput, RoleState state, string systemResult, IntentResult intent)
    {
        return BuildUserPromptWithIntent(playerInput, state, EnvironmentState.GetDefault(), systemResult, intent, string.Empty, null);
    }

    public static string BuildUserPrompt(string playerInput, RoleState state, string systemResult)
    {
        return BuildUserPromptWithIntent(playerInput, state, systemResult, new IntentResult());
    }

    public static string BuildHintPrompt(RoleState state, EnvironmentState envState = null, string knowledgeContext = null, SceneItemLibraryData itemLibrary = null)
    {
        string stateJson = JsonMapper.ToJson(state);
        EnvironmentState runtimeEnv = envState ?? EnvironmentState.GetDefault();
        string envJson = JsonMapper.ToJson(runtimeEnv);
        string envDirective = BuildEnvironmentDirective(runtimeEnv);
        string equipmentSummary = InventoryStateUtility.BuildEquipmentPromptSummary(state, itemLibrary);
        string inventorySummary = InventoryStateUtility.BuildInventoryPromptSummary(state, itemLibrary);
        string knowledgeBlock = string.IsNullOrWhiteSpace(knowledgeContext)
            ? "无额外知识命中"
            : knowledgeContext.Trim();

        return $@"
你是《键入佳境》的行动建议助手。
当前角色状态(JSON)：
{stateJson}

当前环境状态(JSON)：
{envJson}

当前装备：
{equipmentSummary}

当前背包：
{inventorySummary}

当前动态标签与目标：
{envDirective}

相关知识：
{knowledgeBlock}

请只返回 3 个合理的下一步行动建议，必须严格输出 JSON 字符串数组，例如：[""观察周围"",""使用治疗药水"",""向东探索""].
要求：
1. 建议要考虑当前环境和背包物品。
2. 不要推荐使用不存在的物品。
3. 不要输出任何额外解释。";
    }

    private static string BuildIntentDescription(IntentResult intent)
    {
        if (intent == null || intent.actionType == ActionType.Unknown)
            return "未能识别明确意图，请自然续写。";

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
            _ => "未知",
        };
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
