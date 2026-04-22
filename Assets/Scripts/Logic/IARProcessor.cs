using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LitJson;
using Logic.GraphRAG;
using Logic.Intent;
using Logic.Inventory;
using StateData.Environment;
using StateData.Items;
using StateData.Role;
using UnityEngine;
using Object = UnityEngine.Object;

public class IARProcessor : MonoSingleton<IARProcessor>
{
	private struct ActionCost
	{
		public int manaCost;

		public bool healthRisk;

		public bool requiresSafety;
	}

	private static readonly HashSet<string> AllowedCommandKeys = new HashSet<string> { "hp", "mp", "exp", "get_item", "lose_item" };

	private static readonly HashSet<string> AllowedGetItemKeys = new HashSet<string> { "template_id", "count", "runtime", "name", "desc", "rarity", "effect_text", "stat_modifiers" };

	private static readonly HashSet<string> AllowedRuntimeKeys = new HashSet<string> { "name", "desc", "rarity", "effect_text", "stat_modifiers" };

	private static readonly HashSet<string> AllowedLoseItemKeys = new HashSet<string> { "instance_id", "template_id", "count" };

	private static readonly HashSet<string> AllowedStatKeys = new HashSet<string> { "strength", "agility", "intelligence", "max_health", "max_mana", "attack_bonus" };

	private static readonly Dictionary<ActionType, ActionCost> ActionCosts = new Dictionary<ActionType, ActionCost>
	{
		{
			ActionType.Attack,
			new ActionCost
			{
				manaCost = 5,
				healthRisk = true
			}
		},
		{
			ActionType.Defend,
			new ActionCost
			{
				manaCost = 3,
				healthRisk = false
			}
		},
		{
			ActionType.UseSkill,
			new ActionCost
			{
				manaCost = 15,
				healthRisk = true
			}
		},
		{
			ActionType.Cultivate,
			new ActionCost
			{
				manaCost = 0,
				requiresSafety = true
			}
		},
		{
			ActionType.Rest,
			new ActionCost
			{
				manaCost = 0,
				requiresSafety = true
			}
		}
	};

	private static readonly HashSet<string> FireSkills = new HashSet<string> { "火", "炎", "焰", "灼", "燃" };

	private static readonly HashSet<string> WindSkills = new HashSet<string> { "风", "岚", "息", "飓" };

	private static readonly HashSet<string> LightSkills = new HashSet<string> { "光", "明", "曜", "照" };

	private const string ZhuyuTemplateId = "zhuyu_herb";

	private const string MiguTemplateId = "migu_branch";

	private const string HealingPotionTemplateId = "healing_potion";

	private const string ZhuyuName = "祝余";

	private const string MiguName = "迷谷";

	private const string HealingPotionName = "治疗药水";

	public bool CheckActionValidity(string inputAction, RoleState currentState, EnvironmentState envState, out string failReason, out IntentResult intent)
	{
		failReason = string.Empty;
		intent = IntentRecognizer.Instance.Recognize(inputAction);
		return CheckActionValidity(intent, currentState, envState, out failReason);
	}

	public bool CheckActionValidity(IntentResult intent, RoleState currentState, EnvironmentState envState, out string failReason)
	{
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		failReason = string.Empty;
		if (intent == null)
		{
			intent = new IntentResult();
		}
		if (envState == null)
		{
			envState = EnvironmentState.GetDefault();
		}
		envState.EnsureCollections();
		InventoryStateUtility.EnsureCompatibility(currentState, ((Object)GameLoop.Instance != (Object)null) ? GameLoop.Instance.CurrentItemLibrary : null);
		if (currentState.attributes.currentHealth <= 0)
		{
			failReason = "你的身体已经无法支撑任何行动，意识正缓缓坠入黑暗。";
			currentState.runtime.isAlive = false;
			return false;
		}
		if (!ValidateActionByType(intent, currentState, out failReason))
		{
			return false;
		}
		if (!ValidateActionByEnvironment(intent, envState, out failReason))
		{
			return false;
		}
		if (ActionCosts.TryGetValue(intent.actionType, out var value) && currentState.attributes.currentMana < value.manaCost)
		{
			failReason = $"灵力不足，无法执行此行动（需要 {value.manaCost}，当前 {currentState.attributes.currentMana}）。";
			return false;
		}
		if (currentState.runtime.isCriticalState && (intent.actionType == ActionType.Attack || intent.actionType == ActionType.UseSkill))
		{
			failReason = "伤势过重，身体不允许你进行如此激烈的行动。";
			return false;
		}
		return true;
	}

	public bool CheckActionValidity(string inputAction, RoleState currentState, out string failReason, out IntentResult intent)
	{
		return CheckActionValidity(inputAction, currentState, null, out failReason, out intent);
	}

	public bool CheckActionValidity(IntentResult intent, RoleState currentState, out string failReason)
	{
		return CheckActionValidity(intent, currentState, null, out failReason);
	}

	private bool ValidateActionByEnvironment(IntentResult intent, EnvironmentState env, out string failReason)
	{
		failReason = string.Empty;
		if (intent.actionType == ActionType.UseSkill || intent.actionType == ActionType.Attack)
		{
			string value = (intent.parameters.ContainsKey("skill_name") ? intent.parameters["skill_name"] : (intent.targetEntity ?? string.Empty));
			if (env.isWet && ContainsAnyTag(value, FireSkills))
			{
				failReason = "雨水浸透了灵气，火焰术法的威力被大幅削弱，火星在指尖挣扎着熄灭。";
				return false;
			}
			if (env.isWindy && ContainsAnyTag(value, WindSkills))
			{
				intent.parameters["env_boost_wind"] = "true";
			}
			if (env.isDark && ContainsAnyTag(value, LightSkills))
			{
				intent.parameters["env_exposure"] = "true";
			}
		}
		if (env.isFoggy && (intent.actionType == ActionType.Move || intent.actionType == ActionType.Explore))
		{
			intent.parameters["env_fog_risk"] = "true";
		}
		if (env.isDark && intent.actionType == ActionType.Observe)
		{
			intent.parameters["env_vision_limited"] = "true";
		}
		if (env.isWet && intent.actionType == ActionType.Rest)
		{
			intent.parameters["env_damp_rest"] = "true";
		}
		return true;
	}

	private bool ContainsAnyTag(string value, HashSet<string> tags)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}
		foreach (string tag in tags)
		{
			if (value.Contains(tag))
			{
				return true;
			}
		}
		return false;
	}

	private bool ValidateActionByType(IntentResult intent, RoleState state, out string failReason)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected O, but got Unknown
		failReason = string.Empty;
		InventoryStateUtility.EnsureCompatibility(state, ((Object)GameLoop.Instance != (Object)null) ? GameLoop.Instance.CurrentItemLibrary : null);
		switch (intent.actionType)
		{
		case ActionType.UseItem:
		{
			string text = ResolveTargetName(intent);
			if (!string.IsNullOrWhiteSpace(text) && !InventoryStateUtility.HasInventoryItem(state, text))
			{
				failReason = "你翻遍行囊，却没有找到《" + text + "》。";
				return false;
			}
			break;
		}
		case ActionType.UseSkill:
		{
			string skillName = (intent.parameters.ContainsKey("skill_name") ? intent.parameters["skill_name"] : intent.targetEntity);
			if (!string.IsNullOrEmpty(skillName) && state.equipment.equippedSkills != null && !state.equipment.equippedSkills.Exists((string skill) => skill.Contains(skillName)))
			{
				failReason = "你尚未习得《" + skillName + "》这门功法。";
				return false;
			}
			break;
		}
		case ActionType.Attack:
			if (state.equipment.equipmentSlots.GetSlot(EquipSlotType.Weapon) == null)
			{
				intent.parameters["unarmed"] = "true";
			}
			break;
		}
		return true;
	}

	public string ExecuteDeterministicLogic(string inputAction, RoleState currentState, EnvironmentState envState, IntentResult intent)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected O, but got Unknown
		List<string> list = new List<string>();
		if (envState == null)
		{
			envState = EnvironmentState.GetDefault();
		}
		envState.EnsureCollections();
		InventoryStateUtility.EnsureCompatibility(currentState, ((Object)GameLoop.Instance != (Object)null) ? GameLoop.Instance.CurrentItemLibrary : null);
		if (ActionCosts.TryGetValue(intent.actionType, out var value) && value.manaCost > 0)
		{
			currentState.attributes.currentMana -= value.manaCost;
			list.Add($"灵力消耗 -{value.manaCost}");
		}
		string text = GenerateLocalVerdict(intent, currentState, envState);
		if (!string.IsNullOrEmpty(text))
		{
			list.Add(text);
		}
		InventoryStateUtility.NormalizeResourceCaps(currentState, InventoryStateUtility.CalculateDerivedAttributes(currentState));
		if (list.Count == 0)
		{
			return "[系统动作已通过IAR校验，等待叙事生成。]";
		}
		return "[本地裁决] " + string.Join(" | ", list);
	}

	public string ExecuteDeterministicLogic(string inputAction, RoleState currentState, IntentResult intent)
	{
		return ExecuteDeterministicLogic(inputAction, currentState, null, intent);
	}

	private string GenerateLocalVerdict(IntentResult intent, RoleState state, EnvironmentState env)
	{
		DerivedAttributeState derivedAttributeState = InventoryStateUtility.CalculateDerivedAttributes(state);
		switch (intent.actionType)
		{
		case ActionType.Attack:
		{
			int attackPower = derivedAttributeState.attackPower;
			if (env.isWet)
			{
				attackPower = Mathf.Max(1, attackPower - 2);
				return $"攻击基础威力确认，攻击力 {attackPower}（潮湿环境 -2）";
			}
			return $"攻击基础威力确认，攻击力 {attackPower}";
		}
		case ActionType.Defend:
			return "进入防御姿态，伤害减免生效";
		case ActionType.Rest:
		{
			int num2 = Mathf.Min(10, derivedAttributeState.maxHealthTotal - state.attributes.currentHealth);
			if (env.isWet || intent.parameters.ContainsKey("env_damp_rest"))
			{
				num2 = Mathf.Max(1, num2 / 2);
				state.attributes.currentHealth += num2;
				return $"休息恢复生命 +{num2}（潮湿环境效果减半）";
			}
			state.attributes.currentHealth += num2;
			return $"休息恢复生命 +{num2}";
		}
		case ActionType.Cultivate:
		{
			int num = Mathf.Min(15, derivedAttributeState.maxManaTotal - state.attributes.currentMana);
			state.attributes.currentMana += num;
			return $"修炼恢复灵力 +{num}";
		}
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
		if (env != null)
		{
			return HandleObserveRefined(state, env, intent);
		}
		List<string> list = new List<string>();
		if (env.isDark || intent.parameters.ContainsKey("env_vision_limited"))
		{
			list.Add("观察行动：视野受限，应聚焦黑暗中的细微声响与触感");
		}
		else if (env.isFoggy)
		{
			list.Add("观察行动：迷雾遮挡，应描写贴地游走的雾丝与若隐若现的轮廓");
		}
		else
		{
			list.Add("观察行动：AI 应提供周围环境细节");
		}
		if (IsInZhaoYao(env) && !env.HasClue("herbs_spotted"))
		{
			env.AddClue("herbs_spotted");
			env.AddTag("灵草踪迹");
			env.currentObjective = "采集祝余或迷谷，为继续深入招摇山迷雾做准备。";
			MonoSingleton<GraphRAGManager>.Instance.DiscoverEntity("herb_zhuyu");
			MonoSingleton<GraphRAGManager>.Instance.DiscoverEntity("herb_migu");
			list.Add("山壁潮痕之间显出祝余与迷谷的痕迹");
		}
		else if (env.HasClue("deep_path_opened") && !env.HasClue("aberration_foretold"))
		{
			env.AddClue("aberration_foretold");
			env.AddTag("青白异光");
			env.currentObjective = "盯紧异光源头，谨慎判断是否有异兽逼近。";
			list.Add("雾后有青白异光闪灭，林间似有异兽啸响");
		}
		return string.Join("；", list);
	}

	private string HandleCollect(IntentResult intent, RoleState state, EnvironmentState env)
	{
		if (env != null)
		{
			return HandleCollectRefined(intent, state, env);
		}
		string text = ResolveTargetName(intent);
		if (string.IsNullOrWhiteSpace(text))
		{
			if (env.HasClue("herbs_spotted") && !env.HasClue("zhuyu_collected"))
			{
				text = "祝余";
			}
			else if (env.HasClue("herbs_spotted") && !env.HasClue("migu_collected"))
			{
				text = "迷谷";
			}
		}
		if (ContainsText(text, "祝余"))
		{
			if (!TryAddLocalItem(state, "zhuyu_herb", "祝余", "草叶微青，握在掌心时有淡淡草木清香。", "食之可暂缓饥乏。"))
			{
				return "你已经辨出祝余，却因背包已满而无法收纳。";
			}
			MonoSingleton<GraphRAGManager>.Instance.DiscoverEntity("herb_zhuyu");
			env.AddClue("zhuyu_collected");
			env.AddTag("祝余已采");
			env.currentObjective = (env.HasClue("migu_collected") ? "借迷谷辨路，继续向招摇山深处探索。" : "继续辨认迷谷，准备在迷雾中稳定前进。");
			AddExperience(state, 8);
			return "采得一株祝余并收入行囊，经验 +8";
		}
		if (ContainsText(text, "迷谷"))
		{
			if (!TryAddLocalItem(state, "migu_branch", "迷谷", "枝叶黑理，贴近时会泛出幽青反光。", "可在雾中辨认路径。"))
			{
				return "你找到了迷谷，却因背包已满而暂时无法带走。";
			}
			MonoSingleton<GraphRAGManager>.Instance.DiscoverEntity("herb_migu");
			env.AddClue("migu_collected");
			env.AddTag("迷谷在手");
			env.RemoveTag("迷失方向");
			env.currentObjective = "借迷谷辨路，继续向招摇山深处探索。";
			AddExperience(state, 10);
			return "采得迷谷，可借其辨清雾中路径，经验 +10";
		}
		return "采集动作已确认，但暂未命中关键灵草。";
	}

	private string HandleUseItem(IntentResult intent, RoleState state, EnvironmentState env)
	{
		string text = ResolveTargetName(intent);
		if (TryApplyDirectItemUse(text, state, env, out var resultMessage))
		{
			return resultMessage;
		}
		if (ContainsText(text, "治疗药水") && ConsumeInventoryItem(state, "治疗药水"))
		{
			int num = Mathf.Min(18, InventoryStateUtility.CalculateDerivedAttributes(state).maxHealthTotal - state.attributes.currentHealth);
			state.attributes.currentHealth += num;
			env.AddTag("药气回暖");
			return $"服下治疗药水，生命恢复 +{num}";
		}
		if (ContainsText(text, "祝余") && ConsumeInventoryItem(state, "祝余"))
		{
			DerivedAttributeState derivedAttributeState = InventoryStateUtility.CalculateDerivedAttributes(state);
			int num2 = Mathf.Min(6, derivedAttributeState.maxHealthTotal - state.attributes.currentHealth);
			int num3 = Mathf.Min(6, derivedAttributeState.maxManaTotal - state.attributes.currentMana);
			state.attributes.currentHealth += num2;
			state.attributes.currentMana += num3;
			env.AddTag("腹中有实");
			env.currentObjective = "体力稍定，可以继续观察或深入迷雾。";
			return $"咽下祝余后气息稍定，生命 +{num2}，灵力 +{num3}";
		}
		if (ContainsText(text, "迷谷") && InventoryStateUtility.HasInventoryItem(state, "迷谷"))
		{
			env.RemoveTag("迷失方向");
			env.AddTag("迷谷指路");
			env.currentObjective = "沿雾径深入，观察青白异光的来源。";
			return "佩上迷谷后，雾中的路径轮廓逐渐清晰。";
		}
		return "使用物品动作已确认，AI 应描写器物触感与身体反馈。";
	}

	public bool TryUseInventoryItemDirect(RoleState state, EnvironmentState env, ItemInventoryEntry entry, SceneItemLibraryData itemLibrary, out string resultMessage)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected O, but got Unknown
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Expected O, but got Unknown
		resultMessage = null;
		if (state == null || entry == null)
		{
			resultMessage = "鐩\ue1bd爣鐗╁搧涓嶅瓨鍦ㄣ€?";
			return false;
		}
		if (env == null)
		{
			env = (((Object)GameLoop.Instance != (Object)null) ? GameLoop.Instance.CurrentEnvironment : EnvironmentState.GetDefault());
		}
		env.EnsureCollections();
		if ((Object)(object)itemLibrary == (Object)null)
		{
			itemLibrary = (((Object)GameLoop.Instance != (Object)null) ? GameLoop.Instance.CurrentItemLibrary : null);
		}
		InventoryStateUtility.EnsureCompatibility(state, itemLibrary);
		ItemTemplateData itemTemplateData = InventoryStateUtility.ResolveTemplate(itemLibrary, entry);
		string text = entry.runtimeData?.name ?? itemTemplateData?.displayName ?? entry.templateId;
		if (string.IsNullOrWhiteSpace(text))
		{
			resultMessage = "璇ョ墿鍝佸綋鍓嶆棤娉曠洿鎺ヤ娇鐢ㄣ€?";
			return false;
		}
		if (TryApplyDirectItemUse(text, state, env, out resultMessage))
		{
			return true;
		}
		resultMessage = "璇ョ墿鍝佸綋鍓嶄笉鍙\ue21c洿鎺ヤ娇鐢ㄣ€?";
		return false;
	}

	private bool TryApplyDirectItemUse(string targetName, RoleState state, EnvironmentState env, out string resultMessage)
	{
		resultMessage = null;
		if (ContainsText(targetName, "治疗药水") && ConsumeInventoryItem(state, "治疗药水"))
		{
			int num = Mathf.Min(18, InventoryStateUtility.CalculateDerivedAttributes(state).maxHealthTotal - state.attributes.currentHealth);
			state.attributes.currentHealth += num;
			env?.AddTag("鑽\ue21b皵鍥炴殩");
			InventoryStateUtility.NormalizeResourceCaps(state, InventoryStateUtility.CalculateDerivedAttributes(state));
			resultMessage = $"鏈嶄笅娌荤枟鑽\ue21b按锛岀敓鍛芥仮澶?+{num}";
			return true;
		}
		if (ContainsText(targetName, "祝余") && ConsumeInventoryItem(state, "祝余"))
		{
			DerivedAttributeState derivedAttributeState = InventoryStateUtility.CalculateDerivedAttributes(state);
			int num2 = Mathf.Min(6, derivedAttributeState.maxHealthTotal - state.attributes.currentHealth);
			int num3 = Mathf.Min(6, derivedAttributeState.maxManaTotal - state.attributes.currentMana);
			state.attributes.currentHealth += num2;
			state.attributes.currentMana += num3;
			env?.AddTag("鑵逛腑鏈夊疄");
			if (env != null)
			{
				env.currentObjective = "浣撳姏绋嶅畾锛屽彲浠ョ户缁\ue161\ue747瀵熸垨娣卞叆杩烽浘銆?";
			}
			InventoryStateUtility.NormalizeResourceCaps(state, InventoryStateUtility.CalculateDerivedAttributes(state));
			resultMessage = $"鍜戒笅绁濅綑鍚庢皵鎭\ue21c◢瀹氾紝鐢熷懡 +{num2}锛岀伒鍔?+{num3}";
			return true;
		}
		if (ContainsText(targetName, "迷谷") && InventoryStateUtility.HasInventoryItem(state, "迷谷"))
		{
			env?.RemoveTag("杩峰け鏂瑰悜");
			env?.AddTag("杩疯胺鎸囪矾");
			if (env != null)
			{
				env.currentObjective = "娌块浘寰勬繁鍏ワ紝瑙傚療闈掔櫧寮傚厜鐨勬潵婧愩€?";
			}
			resultMessage = "浣╀笂杩疯胺鍚庯紝闆句腑鐨勮矾寰勮疆寤撻€愭笎娓呮櫚銆?";
			return true;
		}
		if (TryApplyGenericConsumableUse(targetName, state, env, out resultMessage))
		{
			return true;
		}
		return false;
	}

	private bool TryApplyGenericConsumableUse(string targetName, RoleState state, EnvironmentState env, out string resultMessage)
	{
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		resultMessage = null;
		if (string.IsNullOrWhiteSpace(targetName) || state == null)
		{
			return false;
		}
		SceneItemLibraryData itemLibrary = (((Object)GameLoop.Instance != (Object)null) ? GameLoop.Instance.CurrentItemLibrary : null);
		int inventoryIndex;
		ItemInventoryEntry itemInventoryEntry = InventoryStateUtility.FindInventoryEntryByName(state, targetName, out inventoryIndex);
		if (itemInventoryEntry == null)
		{
			return false;
		}
		ItemTemplateData itemTemplateData = InventoryStateUtility.ResolveTemplate(itemLibrary, itemInventoryEntry);
		if (itemTemplateData == null || itemTemplateData.itemKind != 0)
		{
			return false;
		}
		string text = itemInventoryEntry.runtimeData?.name ?? itemTemplateData.displayName ?? targetName;
		if (ContainsText(text, "治疗药水") || ContainsText(text, "祝余"))
		{
			return false;
		}
		if (!InventoryStateUtility.TryRemoveItem(state, itemInventoryEntry.runtimeData?.instanceId, null, 1, out var _))
		{
			return false;
		}
		DerivedAttributeState derivedAttributeState = InventoryStateUtility.CalculateDerivedAttributes(state);
		int num = 0;
		int num2 = 0;
		if (itemInventoryEntry.runtimeData?.statModifiers != null)
		{
			foreach (ItemStatModifier statModifier in itemInventoryEntry.runtimeData.statModifiers)
			{
				if (statModifier == null || string.IsNullOrWhiteSpace(statModifier.statKey) || statModifier.value <= 0)
				{
					continue;
				}
				string text2 = statModifier.statKey.Trim().ToLowerInvariant();
				string text3 = text2;
				if (!(text3 == "max_health"))
				{
					if (text3 == "max_mana")
					{
						num2 += statModifier.value;
					}
				}
				else
				{
					num += statModifier.value;
				}
			}
		}
		num = Mathf.Min(num, derivedAttributeState.maxHealthTotal - state.attributes.currentHealth);
		num2 = Mathf.Min(num2, derivedAttributeState.maxManaTotal - state.attributes.currentMana);
		state.attributes.currentHealth += num;
		state.attributes.currentMana += num2;
		InventoryStateUtility.NormalizeResourceCaps(state, InventoryStateUtility.CalculateDerivedAttributes(state));
		if (num > 0 || num2 > 0)
		{
			resultMessage = $"{text} 宸蹭娇鐢\ue7d2紝鐢熷懡 +{num}锛岀伒鍔?+{num2}";
		}
		else
		{
			resultMessage = (string.IsNullOrWhiteSpace(itemInventoryEntry.runtimeData?.effectText) ? (text + " 宸蹭娇鐢ㄣ€?") : (text + " 宸蹭娇鐢\ue7d2細" + itemInventoryEntry.runtimeData.effectText));
		}
		env?.AddTag("鐗╁搧宸蹭娇鐢?");
		return true;
	}

	private string HandleTraversal(IntentResult intent, RoleState state, EnvironmentState env)
	{
		if (env != null)
		{
			return HandleTraversalRefined(intent, state, env);
		}
		string text = (intent.parameters.ContainsKey("direction") ? intent.parameters["direction"] : "前方");
		if (env.isFoggy && !InventoryStateUtility.HasInventoryItem(state, "迷谷") && !env.HasClue("migu_collected"))
		{
			state.attributes.currentHealth = Mathf.Max(1, state.attributes.currentHealth - 3);
			env.AddTag("迷失方向");
			env.currentObjective = "先寻找迷谷或继续观察山壁，以免在雾中折返。";
			return "朝" + text + "试探时被迷雾逼回，瘴气侵体，生命 -3";
		}
		if (IsInZhaoYao(env) && !env.HasClue("deep_path_opened"))
		{
			env.locationName = "招摇山·雾径深处";
			env.narrativeHint = "迷谷映得雾丝分层，山腹深处的青白异光时明时灭，林间偶有尖啸掠过。";
			env.isFoggy = false;
			env.AddClue("deep_path_opened");
			env.AddTag("雾径已明");
			env.AddTag("异兽踪迹");
			env.currentObjective = "沿异光继续探索，准备应对可能的异象或异兽。";
			return "借助线索朝" + text + "深入，成功穿过雾径，并捕捉到异兽活动痕迹";
		}
		if (env.HasClue("deep_path_opened") && !env.HasClue("aberration_triggered"))
		{
			env.AddClue("aberration_triggered");
			env.AddTag("异象逼近");
			env.currentObjective = "观察异光来源，谨慎准备迎接遭遇。";
			return "朝" + text + "再进一步，前方青白异光忽然迫近，遭遇已经临近";
		}
		return "移动方向: " + text;
	}

	public string AnalyzeAndApplyAIResult(string aiFullResponse, RoleState state, SceneItemLibraryData itemLibrary, out string feedback)
	{
		feedback = null;
		Match match = Regex.Match(aiFullResponse ?? string.Empty, "<CMD>(.*?)</CMD>", RegexOptions.Singleline);
		if (!match.Success)
		{
			return aiFullResponse;
		}
		string value = match.Groups[1].Value;
		try
		{
			string failReason2;
			if (!TryValidateCommandJson(value, out var failReason))
			{
				feedback = "已拒绝非法状态指令：" + failReason;
			}
			else if (!ApplyCommandToState(value, state, itemLibrary, out failReason2))
			{
				feedback = failReason2;
			}
		}
		catch (Exception ex)
		{
			feedback = "状态指令解析失败：" + ex.Message;
		}
		return aiFullResponse.Replace(match.Value, string.Empty).Trim();
	}

	public static bool TryValidateCommandJson(string jsonStr, out string failReason)
	{
		failReason = null;
		if (string.IsNullOrWhiteSpace(jsonStr))
		{
			failReason = "空指令";
			return false;
		}
		JsonData jsonData;
		try
		{
			jsonData = JsonMapper.ToObject(jsonStr);
		}
		catch (Exception ex)
		{
			failReason = "JSON 解析失败: " + ex.Message;
			return false;
		}
		if (jsonData == null || !jsonData.IsObject)
		{
			failReason = "顶层必须是 JSON 对象";
			return false;
		}
		foreach (string key in jsonData.Keys)
		{
			if (!AllowedCommandKeys.Contains(key))
			{
				failReason = "存在未授权字段: " + key;
				return false;
			}
		}
		if (jsonData.Keys.Contains("get_item"))
		{
			JsonData jsonData2 = jsonData["get_item"];
			if (jsonData2 == null || !jsonData2.IsObject)
			{
				failReason = "get_item 必须是对象";
				return false;
			}
			foreach (string key2 in jsonData2.Keys)
			{
				if (!AllowedGetItemKeys.Contains(key2))
				{
					failReason = "get_item 存在未授权字段: " + key2;
					return false;
				}
			}
			if (!jsonData2.Keys.Contains("template_id") || string.IsNullOrWhiteSpace((string)jsonData2["template_id"]))
			{
				failReason = "get_item 缺少 template_id";
				return false;
			}
			if (jsonData2.Keys.Contains("count") && TryReadInt(jsonData2["count"], out var value) && value <= 0)
			{
				failReason = "get_item.count 必须大于 0";
				return false;
			}
			if (jsonData2.Keys.Contains("runtime"))
			{
				if (!ValidateRuntimeBlock(jsonData2["runtime"], out failReason))
				{
					return false;
				}
			}
			else if (jsonData2.Keys.Contains("stat_modifiers") && !ValidateStatModifiers(jsonData2["stat_modifiers"], out failReason))
			{
				return false;
			}
		}
		if (jsonData.Keys.Contains("lose_item"))
		{
			JsonData jsonData3 = jsonData["lose_item"];
			if (jsonData3 == null || !jsonData3.IsObject)
			{
				failReason = "lose_item 必须是对象";
				return false;
			}
			foreach (string key3 in jsonData3.Keys)
			{
				if (!AllowedLoseItemKeys.Contains(key3))
				{
					failReason = "lose_item 存在未授权字段: " + key3;
					return false;
				}
			}
			bool flag = jsonData3.Keys.Contains("instance_id") && !string.IsNullOrWhiteSpace((string)jsonData3["instance_id"]);
			bool flag2 = jsonData3.Keys.Contains("template_id") && !string.IsNullOrWhiteSpace((string)jsonData3["template_id"]);
			if (!flag && !flag2)
			{
				failReason = "lose_item 至少需要 instance_id 或 template_id";
				return false;
			}
			if (jsonData3.Keys.Contains("count") && TryReadInt(jsonData3["count"], out var value2) && value2 <= 0)
			{
				failReason = "lose_item.count 必须大于 0";
				return false;
			}
		}
		return true;
	}

	private static bool ValidateRuntimeBlock(JsonData runtimeData, out string failReason)
	{
		failReason = null;
		if (runtimeData == null || !runtimeData.IsObject)
		{
			failReason = "get_item.runtime 必须是对象";
			return false;
		}
		foreach (string key in runtimeData.Keys)
		{
			if (!AllowedRuntimeKeys.Contains(key))
			{
				failReason = "runtime 存在未授权字段: " + key;
				return false;
			}
		}
		if (runtimeData.Keys.Contains("stat_modifiers") && !ValidateStatModifiers(runtimeData["stat_modifiers"], out failReason))
		{
			return false;
		}
		return true;
	}

	private static bool ValidateStatModifiers(JsonData modifierData, out string failReason)
	{
		failReason = null;
		if (modifierData == null || !modifierData.IsArray)
		{
			failReason = "stat_modifiers 必须是数组";
			return false;
		}
		for (int i = 0; i < modifierData.Count; i++)
		{
			JsonData jsonData = modifierData[i];
			if (jsonData == null || !jsonData.IsObject)
			{
				failReason = "stat_modifiers 中的每项都必须是对象";
				return false;
			}
			string text = (jsonData.Keys.Contains("stat") ? ((string)jsonData["stat"]) : null);
			if (string.IsNullOrWhiteSpace(text) || !AllowedStatKeys.Contains(text))
			{
				failReason = "不支持的 stat modifier: " + text;
				return false;
			}
			if (!jsonData.Keys.Contains("value") || !TryReadInt(jsonData["value"], out var _))
			{
				failReason = "stat modifier 缺少有效的 value";
				return false;
			}
		}
		return true;
	}

	private bool ApplyCommandToState(string jsonStr, RoleState state, SceneItemLibraryData itemLibrary, out string failReason)
	{
		failReason = null;
		JsonData jsonData = JsonMapper.ToObject(jsonStr);
		InventoryStateUtility.EnsureCompatibility(state, itemLibrary);
		if (jsonData.Keys.Contains("hp"))
		{
			int num = ReadInt(jsonData["hp"]);
			state.attributes.currentHealth += num;
		}
		if (jsonData.Keys.Contains("mp"))
		{
			int num2 = ReadInt(jsonData["mp"]);
			state.attributes.currentMana += num2;
		}
		if (jsonData.Keys.Contains("exp"))
		{
			int num3 = ReadInt(jsonData["exp"]);
			state.attributes.currentExp += num3;
			CheckLevelUp(state);
		}
		if (jsonData.Keys.Contains("get_item"))
		{
			JsonData jsonData2 = jsonData["get_item"];
			string text = (string)jsonData2["template_id"];
			if (!TryResolveAllowedTemplateId(itemLibrary, text, out var resolvedTemplateId))
			{
				Debug.LogWarning($"[IAR] Rejected item outside scene library: {text}");
				failReason = "已拒绝加入场景物品库之外的物品。";
				InventoryStateUtility.NormalizeResourceCaps(state, InventoryStateUtility.CalculateDerivedAttributes(state));
				return false;
			}
			ItemRuntimeData itemRuntimeData = ParseRuntimeData(jsonData2);
			int num4 = ((!jsonData2.Keys.Contains("count")) ? 1 : Mathf.Max(1, ReadInt(jsonData2["count"])));
			ItemTemplateData template = itemLibrary.GetTemplate(resolvedTemplateId);
			if (template == null)
			{
				failReason = "无法在当前场景物品库中找到 template_id=" + resolvedTemplateId;
				return false;
			}
			if (template.stackable)
			{
				ItemInventoryEntry entry = new ItemInventoryEntry
				{
					templateId = resolvedTemplateId,
					count = num4,
					runtimeData = itemRuntimeData
				};
				if (!InventoryStateUtility.TryAddInventoryEntry(state, entry, itemLibrary, out failReason))
				{
					return false;
				}
			}
			else
			{
				for (int i = 0; i < num4; i++)
				{
					ItemInventoryEntry entry2 = new ItemInventoryEntry
					{
						templateId = resolvedTemplateId,
						count = 1,
						runtimeData = CloneRuntimeData(itemRuntimeData, (i == 0) ? itemRuntimeData.instanceId : null)
					};
					if (!InventoryStateUtility.TryAddInventoryEntry(state, entry2, itemLibrary, out failReason))
					{
						return false;
					}
				}
			}
			NotifyItemAcquired(template, itemRuntimeData, num4);
		}
		if (jsonData.Keys.Contains("lose_item"))
		{
			JsonData jsonData3 = jsonData["lose_item"];
			string instanceId = (jsonData3.Keys.Contains("instance_id") ? ((string)jsonData3["instance_id"]) : null);
			string templateId = (jsonData3.Keys.Contains("template_id") ? ((string)jsonData3["template_id"]) : null);
			int count = ((!jsonData3.Keys.Contains("count")) ? 1 : Mathf.Max(1, ReadInt(jsonData3["count"])));
			if (!InventoryStateUtility.TryRemoveItem(state, instanceId, templateId, count, out failReason))
			{
				return false;
			}
		}
		InventoryStateUtility.NormalizeResourceCaps(state, InventoryStateUtility.CalculateDerivedAttributes(state));
		return true;
	}

	private static void NotifyItemAcquired(ItemTemplateData template, ItemRuntimeData runtimeData, int itemCount)
	{
		string text = ResolveItemDisplayName(template, runtimeData);
		if (!string.IsNullOrWhiteSpace(text))
		{
			string text2 = ((itemCount > 1) ? $" x{itemCount}" : string.Empty);
			EventCenter.Instance.Broadcast("OnCenterToast", "获得道具：" + text + text2);
		}
	}

	private static string ResolveItemDisplayName(ItemTemplateData template, ItemRuntimeData runtimeData)
	{
		if (!string.IsNullOrWhiteSpace(runtimeData?.name))
		{
			return runtimeData.name.Trim();
		}
		if (!string.IsNullOrWhiteSpace(template?.displayName))
		{
			return template.displayName.Trim();
		}
		if (!string.IsNullOrWhiteSpace(template?.templateId))
		{
			return template.templateId.Trim();
		}
		return string.Empty;
	}

	private ItemRuntimeData ParseRuntimeData(JsonData itemData)
	{
		JsonData jsonData = (itemData.Keys.Contains("runtime") ? itemData["runtime"] : itemData);
		ItemRuntimeData itemRuntimeData = new ItemRuntimeData
		{
			instanceId = Guid.NewGuid().ToString("N"),
			name = (jsonData.Keys.Contains("name") ? ((string)jsonData["name"]) : string.Empty),
			description = (jsonData.Keys.Contains("desc") ? ((string)jsonData["desc"]) : string.Empty),
			rarity = (jsonData.Keys.Contains("rarity") ? ((string)jsonData["rarity"]) : "普通"),
			effectText = (jsonData.Keys.Contains("effect_text") ? ((string)jsonData["effect_text"]) : string.Empty),
			statModifiers = new List<ItemStatModifier>()
		};
		if (jsonData.Keys.Contains("stat_modifiers") && jsonData["stat_modifiers"].IsArray)
		{
			for (int i = 0; i < jsonData["stat_modifiers"].Count; i++)
			{
				JsonData jsonData2 = jsonData["stat_modifiers"][i];
				if (jsonData2 != null && jsonData2.IsObject)
				{
					itemRuntimeData.statModifiers.Add(new ItemStatModifier
					{
						statKey = (jsonData2.Keys.Contains("stat") ? ((string)jsonData2["stat"]) : string.Empty),
						value = (jsonData2.Keys.Contains("value") ? ReadInt(jsonData2["value"]) : 0)
					});
				}
			}
		}
		itemRuntimeData.EnsureDefaults();
		return itemRuntimeData;
	}

	private static ItemRuntimeData CloneRuntimeData(ItemRuntimeData source, string keepInstanceId = null)
	{
		ItemRuntimeData itemRuntimeData = new ItemRuntimeData
		{
			instanceId = (string.IsNullOrWhiteSpace(keepInstanceId) ? Guid.NewGuid().ToString("N") : keepInstanceId),
			name = source?.name,
			description = source?.description,
			rarity = source?.rarity,
			effectText = source?.effectText,
			statModifiers = new List<ItemStatModifier>()
		};
		if (source?.statModifiers != null)
		{
			foreach (ItemStatModifier statModifier in source.statModifiers)
			{
				itemRuntimeData.statModifiers.Add(new ItemStatModifier
				{
					statKey = statModifier?.statKey,
					value = (statModifier?.value ?? 0)
				});
			}
		}
		itemRuntimeData.EnsureDefaults();
		return itemRuntimeData;
	}

	private static int ReadInt(JsonData data)
	{
		int value;
		return TryReadInt(data, out value) ? value : 0;
	}

	private static bool TryReadInt(JsonData data, out int value)
	{
		value = 0;
		if (data == null)
		{
			return false;
		}
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

	private void AddExperience(RoleState state, int amount)
	{
		if (amount > 0)
		{
			state.attributes.currentExp += amount;
			CheckLevelUp(state);
		}
	}

	private void CheckLevelUp(RoleState state)
	{
		if (state.attributes.expToNextLevel <= 0)
		{
			state.attributes.expToNextLevel = 100;
		}
		while (state.attributes.currentExp >= state.attributes.expToNextLevel)
		{
			state.attributes.currentExp -= state.attributes.expToNextLevel;
			state.attributes.level++;
			state.attributes.expToNextLevel = Mathf.RoundToInt((float)state.attributes.expToNextLevel * 1.5f);
			state.attributes.maxHealth += 20;
			state.attributes.maxMana += 10;
		}
	}

	private bool TryAddLocalItem(RoleState state, string templateId, string fallbackName, string description, string effectText)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		SceneItemLibraryData sceneItemLibraryData = (((Object)GameLoop.Instance != (Object)null) ? GameLoop.Instance.CurrentItemLibrary : null);
		ItemTemplateData itemTemplateData = (((Object)sceneItemLibraryData != (Object)null) ? sceneItemLibraryData.GetTemplate(templateId) : null);
		string runtimeName = ((itemTemplateData != null && !string.IsNullOrWhiteSpace(itemTemplateData.displayName)) ? itemTemplateData.displayName : fallbackName);
		string description2 = ((itemTemplateData != null && !string.IsNullOrWhiteSpace(itemTemplateData.templateDescription)) ? itemTemplateData.templateDescription : description);
		ItemInventoryEntry entry = InventoryStateUtility.CreateEntryFromTemplate(templateId, runtimeName, description2, "普通", effectText, null);
		string failReason;
		bool added = InventoryStateUtility.TryAddInventoryEntry(state, entry, sceneItemLibraryData, out failReason);
		if (added)
		{
			NotifyItemAcquired(itemTemplateData, entry.runtimeData, entry.count);
		}
		return added;
	}

	private bool ConsumeInventoryItem(RoleState state, string itemName)
	{
		int inventoryIndex;
		ItemInventoryEntry itemInventoryEntry = InventoryStateUtility.FindInventoryEntryByName(state, itemName, out inventoryIndex);
		if (itemInventoryEntry?.runtimeData == null)
		{
			return false;
		}
		string failReason;
		return InventoryStateUtility.TryRemoveItem(state, itemInventoryEntry.runtimeData.instanceId, null, 1, out failReason);
	}

	private static string ResolveTargetName(IntentResult intent)
	{
		if (intent == null)
		{
			return string.Empty;
		}
		if (!string.IsNullOrWhiteSpace(intent.targetEntity))
		{
			return intent.targetEntity;
		}
		if (intent.parameters != null)
		{
			if (intent.parameters.TryGetValue("item_name", out var value) && !string.IsNullOrWhiteSpace(value))
			{
				return value;
			}
			if (intent.parameters.TryGetValue("skill_name", out var value2) && !string.IsNullOrWhiteSpace(value2))
			{
				return value2;
			}
		}
		return string.Empty;
	}

	private string HandleObserveRefined(RoleState state, EnvironmentState env, IntentResult intent)
	{
		List<string> list = new List<string>();
		if (env.isDark || intent.parameters.ContainsKey("env_vision_limited"))
		{
			list.Add("观察行动：视野受限，应聚焦黑暗中的细微声响与触感");
		}
		else if (env.isFoggy)
		{
			list.Add("观察行动：迷雾遮掩，应描写贴地游走的雾丝与若隐若现的轮廓");
		}
		else
		{
			list.Add("观察行动：AI 应提供周围环境细节");
		}
		if (IsInZhaoYao(env) && !env.HasClue("herbs_spotted"))
		{
			env.AddClue("herbs_spotted");
			env.AddTag("灵草踪迹");
			TryUpdateObjective(env, "留意能稳住气息或辨清雾路的草木线索。", "观察", "草木", "线索", "祝余", "迷谷");
			MonoSingleton<GraphRAGManager>.Instance.DiscoverEntity("herb_zhuyu");
			MonoSingleton<GraphRAGManager>.Instance.DiscoverEntity("herb_migu");
			list.Add("山壁潮痕与碎叶之间露出几缕可供辨认的草木痕迹");
		}
		else if (env.HasClue("deep_path_opened") && !env.HasClue("aberration_foretold"))
		{
			env.AddClue("aberration_foretold");
			env.AddTag("青白异光");
			TryUpdateObjective(env, "盯紧异光源头，谨慎判断是否有异兽逼近。", "异光", "异象", "深入", "线索");
			list.Add("雾后有青白异光闪烁，林间似有异兽啸响");
		}
		return string.Join("；", list);
	}

	private string HandleCollectRefined(IntentResult intent, RoleState state, EnvironmentState env)
	{
		string text = ResolveTargetName(intent);
		if (string.IsNullOrWhiteSpace(text))
		{
			if (env.HasClue("herbs_spotted") && !env.HasClue("zhuyu_collected"))
			{
				text = "祝余";
			}
			else if (env.HasClue("herbs_spotted") && !env.HasClue("migu_collected"))
			{
				text = "迷谷";
			}
		}
		if (ContainsText(text, "祝余"))
		{
			if (!TryAddLocalItem(state, "zhuyu_herb", "祝余", "草叶微青，握在掌心时有淡淡草木清香。", "食之可暂缓饥乏。"))
			{
				return "你已经辨出祝余，却因背包已满而无法收纳。";
			}
			MonoSingleton<GraphRAGManager>.Instance.DiscoverEntity("herb_zhuyu");
			env.AddClue("zhuyu_collected");
			env.AddTag("祝余已采");
			TryUpdateObjective(env, env.HasClue("migu_collected") ? "借迷谷辨路，继续向招摇山深处探索。" : "继续寻找能在雾中辨路的线索。", "祝余", "迷谷", "草木", "线索", "准备");
			AddExperience(state, 8);
			return "采得一株祝余并收入行囊，经验 +8";
		}
		if (ContainsText(text, "迷谷"))
		{
			if (!TryAddLocalItem(state, "migu_branch", "迷谷", "枝叶黑理，贴近时会泛出幽青反光。", "可在雾中辨认路径。"))
			{
				return "你找到了迷谷，却因背包已满而暂时无法带走。";
			}
			MonoSingleton<GraphRAGManager>.Instance.DiscoverEntity("herb_migu");
			env.AddClue("migu_collected");
			env.AddTag("迷谷在手");
			env.RemoveTag("迷失方向");
			TryUpdateObjective(env, "借迷谷辨路，继续向招摇山深处探索。", "迷谷", "草木", "线索", "准备", "深入");
			AddExperience(state, 10);
			return "采得迷谷，可借其辨清雾中路径，经验 +10";
		}
		return "采集动作已确认，但暂未命中关键灵草。";
	}

	private string HandleTraversalRefined(IntentResult intent, RoleState state, EnvironmentState env)
	{
		string text = (intent.parameters.ContainsKey("direction") ? intent.parameters["direction"] : "前方");
		if (env.isFoggy && !InventoryStateUtility.HasInventoryItem(state, "迷谷") && !InventoryStateUtility.HasInventoryItem(state, "迷谷枝") && !env.HasClue("migu_collected"))
		{
			state.attributes.currentHealth = Mathf.Max(1, state.attributes.currentHealth - 3);
			env.AddTag("迷失方向");
			TryUpdateObjective(env, "先寻找能辨路的线索，以免在雾中折返。", "观察", "草木", "线索", "祝余", "迷谷");
			return "你朝" + text + "试探时被迷雾逼回，瘴气侵体，生命 -3";
		}
		if (IsInZhaoYao(env) && !env.HasClue("deep_path_opened"))
		{
			env.locationName = "招摇山·雾径深处";
			env.narrativeHint = "迷谷映得雾丝分层，山腹深处的青白异光时明时灭，林间偶有尖啸掠过。";
			env.isFoggy = false;
			env.AddClue("deep_path_opened");
			env.AddTag("雾径已明");
			env.AddTag("异兽踪迹");
			TryUpdateObjective(env, "沿异光继续探索，准备应对可能的异象或异兽。", "迷谷", "深入", "异光", "线索");
			return "你借助线索朝" + text + "深入，成功穿过雾径，并捕捉到异兽活动痕迹";
		}
		if (env.HasClue("deep_path_opened") && !env.HasClue("aberration_triggered"))
		{
			env.AddClue("aberration_triggered");
			env.AddTag("异象逼近");
			TryUpdateObjective(env, "观察异光来源，谨慎准备迎接遭遇。", "异光", "异象", "深入");
			return "你朝" + text + "再进一步，前方青白异光忽然迫近，遭遇已经临近";
		}
		return "移动方向: " + text;
	}

	private static void TryUpdateObjective(EnvironmentState env, string objective, params string[] overridableKeywords)
	{
		if (env == null || string.IsNullOrWhiteSpace(objective))
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(env.currentObjective))
		{
			env.currentObjective = objective;
		}
		else
		{
			if (string.Equals(env.currentObjective, objective, StringComparison.Ordinal) || overridableKeywords == null || overridableKeywords.Length == 0)
			{
				return;
			}
			foreach (string keyword in overridableKeywords)
			{
				if (ContainsText(env.currentObjective, keyword))
				{
					env.currentObjective = objective;
					break;
				}
			}
		}
	}

	private static bool TryResolveAllowedTemplateId(SceneItemLibraryData itemLibrary, string requestedTemplateId, out string resolvedTemplateId)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Expected O, but got Unknown
		resolvedTemplateId = null;
		if ((Object)itemLibrary == (Object)null || string.IsNullOrWhiteSpace(requestedTemplateId))
		{
			return false;
		}
		itemLibrary.EnsureIndex();
		string text = requestedTemplateId.Trim();
		if (itemLibrary.IsTemplateAllowed(text))
		{
			resolvedTemplateId = text;
			return true;
		}
		string text2 = NormalizeTemplateLookupKey(text);
		string text3 = ResolveAliasTemplateId(text2);
		if (!string.IsNullOrWhiteSpace(text3) && itemLibrary.IsTemplateAllowed(text3))
		{
			resolvedTemplateId = text3;
			return true;
		}
		if (itemLibrary.items == null)
		{
			return false;
		}
		foreach (ItemTemplateData item in itemLibrary.items)
		{
			if (item == null || string.IsNullOrWhiteSpace(item.templateId) || !itemLibrary.IsTemplateAllowed(item.templateId) || (!MatchesTemplateLookupKey(text2, item.templateId) && !MatchesTemplateLookupKey(text2, item.displayName)))
			{
				continue;
			}
			resolvedTemplateId = item.templateId.Trim();
			return true;
		}
		return false;
	}

	private static bool MatchesTemplateLookupKey(string normalizedRequestedKey, string candidate)
	{
		return !string.IsNullOrWhiteSpace(normalizedRequestedKey) && !string.IsNullOrWhiteSpace(candidate) && string.Equals(normalizedRequestedKey, NormalizeTemplateLookupKey(candidate), StringComparison.OrdinalIgnoreCase);
	}

	private static string ResolveAliasTemplateId(string normalizedKey)
	{
		switch (normalizedKey)
		{
		case "zhuyu":
		case "祝余":
			return "zhuyu_herb";
		case "migu":
		case "迷谷":
		case "迷谷枝":
			return "migu_branch";
		case "healing_potion":
		case "healingpotion":
		case "治疗药水":
			return "healing_potion";
		case "bronze_broken_sword":
		case "bronzebrokensword":
		case "青铜断剑":
			return "bronze_broken_sword";
		case "cloth_headband":
		case "缚风头带":
			return "cloth_headband";
		case "traveler_robe":
		case "行山短褐":
			return "traveler_robe";
		case "linen_pants":
		case "麻布行裤":
			return "linen_pants";
		case "grass_shoes":
		case "草结轻履":
			return "grass_shoes";
		case "obsidian_butcher_knife":
		case "墨玉庖刀":
			return "obsidian_butcher_knife";
		case "broken_shanhai_scroll":
		case "brokenshanhaiscroll":
		case "残破山海图":
			return "broken_shanhai_scroll";
		case "fire_tinder":
		case "firetinder":
		case "火折":
			return "fire_tinder";
		case "dangkang_bone_soup":
		case "dangkangbonesoup":
		case "当康骨汤":
			return "dangkang_bone_soup";
		case "wild_fennel":
		case "wildfennel":
		case "野茴香":
			return "wild_fennel";
		case "dangkang_ribs":
		case "dangkangribs":
		case "当康肋排":
			return "dangkang_ribs";
		case "dangkang_fang":
		case "dangkangfang":
		case "dangkang_tusk":
		case "dangkangtusk":
		case "dangkang_tooth":
		case "dangkangtooth":
		case "当康獠牙":
			return "dangkang_fang";
		case "dangkang_tallow":
		case "dangkangtallow":
		case "当康脂膏":
			return "dangkang_tallow";
		default:
			return null;
		}
	}

	private static string NormalizeTemplateLookupKey(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		string input = value.Trim().ToLowerInvariant().Replace("\u3000", " ");
		input = Regex.Replace(input, "[\\s\\-–—\\\\/|:：,.，。()（）\\[\\]【】]+", "_");
		input = Regex.Replace(input, "_+", "_");
		return input.Trim('_');
	}

	private static bool ContainsText(string source, string keyword)
	{
		return !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(keyword) && source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsInZhaoYao(EnvironmentState env)
	{
		return env != null && ((!string.IsNullOrWhiteSpace(env.locationId) && env.locationId.Contains("zhaoyao", StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrWhiteSpace(env.locationName) && env.locationName.Contains("招摇山", StringComparison.Ordinal)));
	}
}
