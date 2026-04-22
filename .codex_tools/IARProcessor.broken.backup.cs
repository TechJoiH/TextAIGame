using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LitJson;
using Logic.Intent;
using Logic.Inventory;
using StateData.Environment;
using StateData.Items;
using StateData.Role;
using UnityEngine;

public class IARProcessor : MonoSingleton<IARProcessor>
{
    private static readonly HashSet<string> AllowedCommandKeys = new HashSet<string>
    {
        "hp", "mp", "exp", "get_item", "lose_item"
    };

    private static readonly HashSet<string> AllowedGetItemKeys = new HashSet<string>
    {
        "template_id", "count", "runtime", "name", "desc", "rarity", "effect_text", "stat_modifiers"
    };

    private static readonly HashSet<string> AllowedRuntimeKeys = new HashSet<string>
    {
        "name", "desc", "rarity", "effect_text", "stat_modifiers"
    };

    private static readonly HashSet<string> AllowedLoseItemKeys = new HashSet<string>
    {
        "instance_id", "template_id", "count"
    };

    private static readonly HashSet<string> AllowedStatKeys = new HashSet<string>
    {
        "strength", "agility", "intelligence", "max_health", "max_mana", "attack_bonus"
    };

    private static readonly Dictionary<ActionType, ActionCost> ActionCosts = new Dictionary<ActionType, ActionCost>
    {
        { ActionType.Attack, new ActionCost { manaCost = 5, healthRisk = true } },
        { ActionType.Defend, new ActionCost { manaCost = 3, healthRisk = false } },
        { ActionType.UseSkill, new ActionCost { manaCost = 15, healthRisk = true } },
        { ActionType.Cultivate, new ActionCost { manaCost = 0, requiresSafety = true } },
        { ActionType.Rest, new ActionCost { manaCost = 0, requiresSafety = true } },
    };

    private static readonly HashSet<string> FireSkills = new HashSet<string> { "闂?, "闂?, "闂?, "闂?, "闂? };
    private static readonly HashSet<string> WindSkills = new HashSet<string> { "婵?, "闂佽楠搁崢婊堝磻?, "闂?, "婵? };
    private static readonly HashSet<string> LightSkills = new HashSet<string> { "闂?, "闂?, "闂?, "闂? };

    private const string ZhuyuTemplateId = "zhuyu_herb";
    private const string MiguTemplateId = "migu_branch";
    private const string HealingPotionTemplateId = "healing_potion";
    private const string ZhuyuName = "缂傚倸鍊峰鎺楁倿閿旂偓宕查柍褜鍓涚槐?;
    private const string MiguName = "闂備礁鎼ˇ顐﹀疾濠靛钃熷鑸靛姈閸?;
    private const string HealingPotionName = "濠电姷鏁搁崑娑㈡儗娴ｅ摜鏆﹂柣銏㈩焾閸戠娀鏌ｉ弬鍨倯闁搞倖甯楅妵鍕冀閵娧€濮囬柣?;

    private struct ActionCost
    {
        public int manaCost;
        public bool healthRisk;
        public bool requiresSafety;
    }

    public bool CheckActionValidity(string inputAction, RoleState currentState, EnvironmentState envState, out string failReason, out IntentResult intent)
    {
        failReason = string.Empty;
        intent = IntentRecognizer.Instance.Recognize(inputAction);
        return CheckActionValidity(intent, currentState, envState, out failReason);
    }

    public bool CheckActionValidity(IntentResult intent, RoleState currentState, EnvironmentState envState, out string failReason)
    {
        failReason = string.Empty;
        intent ??= new IntentResult();
        envState ??= EnvironmentState.GetDefault();
        envState.EnsureCollections();
        InventoryStateUtility.EnsureCompatibility(currentState, GameLoop.Instance != null ? GameLoop.Instance.CurrentItemLibrary : null);

        if (currentState.attributes.currentHealth <= 0)
        {
            failReason = "婵犵數鍋犻幓顏嗗緤閹稿孩鍙忕€瑰嫭澹嬮弸鏃堟煙鏉堟儳顩柛娆愭礃閵囧嫰骞囬崜浣猴紵缂備礁顦抽崑鎰板焵椤掑喚娼愰柣鈩冩礈娴滅鈻庨幋婵嗙亰闂佽法鍠撴慨瀵哥尵瀹ュ鐓熸俊顖濇閿涘秹鏌曢崱妤嬭含闁哄矉绻濋崺鈧い鎺戝閺佸秵绻涢幋鐐垫噧妞ゅ繗顫夌换娑氣偓鐢殿焾闉嬪銈嗘煥閿曪妇妲愰悙宸悑闁搞儯鍔屾禒鎺楁煟韫囨洖浠╂俊顐㈠椤ゅ嫰姊绘担鐟邦嚋缂佸鍨垮鐢割敆閸曨偆顦梺瑙勵問閸犳鈧碍宀搁弻娑㈠焺閸愬じ绶甸梺鍛婃煛閸嬫挾绱撻崒姘偓鎼佸磹瑜版帒绠伴柛鎾楀嫷娼熼梺鎸庢礀閸婃悂鏌ㄩ妶澶嬬厽婵☆垵顕х徊鑽も偓娈垮枛閸熷潡锝炲Δ鈧埥澶娾枎韫囨洑妗撴繝鐢靛仜閻楁粓宕戞繝鍐х箚?;
            currentState.runtime.isAlive = false;
            return false;
        }

        if (!ValidateActionByType(intent, currentState, out failReason))
            return false;

        if (!ValidateActionByEnvironment(intent, envState, out failReason))
            return false;

        if (ActionCosts.TryGetValue(intent.actionType, out var cost) && currentState.attributes.currentMana < cost.manaCost)
        {
            failReason = $"闂傚倷娴囬褏鎹㈠Ο渚劷鐟滃酣骞堥妸銊х杸闁哄啫鍊甸弸鏍倵楠炲灝鍔氶柣妤€妫濆畷浼村即閵忥紕鍙嗗┑鐐村灦钃辨繝鈧悧鍫滅箚妞ゆ劗濮撮埀顒佹礋閸┿垽寮崼婵嗗祮闂佺粯鏌ㄩ惃婵嬪箰閸愵喗鐓熼柣鎰嚟閳藉鏌￠崼顐㈠⒋妞ゃ垺顨呴埞鎴﹀醇閻旇渹绮ч梺璇茬箳閸嬬姴螞閸曨噮鏉介梻鍌欑劍鐎笛呯矙閹达附鍋嬮柟鍓у仺閳ь剚甯″畷婊勬媴閻熺増姣?{cost.manaCost}闂傚倷鐒︾€笛呯矙閹达附鍤愭い鏍ㄧ矌缁€濠囨倵閿濆骸鏋涚紒鈧?{currentState.attributes.currentMana}闂傚倷鐒︾€笛呯矙閹次诲洭顢橀姀鐘靛姦?;
            return false;
        }

        if (currentState.runtime.isCriticalState &&
            (intent.actionType == ActionType.Attack || intent.actionType == ActionType.UseSkill))
        {
            failReason = "婵犵數鍋涢顓㈩敄閺囥垹纾归柡鍥╁枔缁犲墽鈧箍鍎卞Λ娆愮閵堝鐓曢柕澶樺枛婢ь喗绻涢崨顐㈢伈闁哄被鍔岄埥澶娢熼懖鈺佸О闂傚倸鍊歌墝闁哄懏绮撻獮蹇涘川閺夋垹顔掗梺鑲┾拡閸撴盯骞夎ぐ鎺撯拺缂佸娉曠粻锝嗙箾閺夋垶鍠樻い銏＄懆缁犳盯寮埀顒佸垔閹绢喗鐓熸俊顖涘椤忕姷绱掗悩宸吋闁诡喛娉涢埥澶娢熼崹顕呬純婵＄偑鍊愰弫顏堝礃閻愵剚銆冮梻渚€娼ч悧鍡椢涘☉姘闁绘柨鍚嬮悡鐔兼煙鐎涙绠樺褋鍨虹换娑㈠醇閻旈浠奸梺璇″枟婢瑰棙绂掗敃鍌涘癄濠㈣鍘介〃濠囧蓟?;
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
            string skillName = intent.parameters.ContainsKey("skill_name")
                ? intent.parameters["skill_name"]
                : intent.targetEntity ?? string.Empty;

            if (env.isWet && ContainsAnyTag(skillName, FireSkills))
            {
                failReason = "闂傚倸鍊搁崐绋课涙惔銊ョ鐎广儱顦粻鏉款熆鐠鸿　濮囩憸鐗堝笒缁犵懓霉閿濆棗绲诲ù婊堢畺閻擃偊宕惰閹癸綁鏌涢悢鍑よ含闁哄矉缍€缁犳盯骞橀張鐢甸┏闂備焦鐪归崝搴ㄥ极婵犳艾鏋佺€广儱鎳愰弳鍡涙煃瑜滈崜姘扁偓闈涖偢閸┾偓妞ゆ帒瀚悡鐔兼煙閻戞ɑ鐓ユい锝呭悑缁绘繈鍩€椤掑嫬绀冩繛鏉戭儐閻忓啴姊洪崫鍕窛濠殿喚鏁婚幃楣冨础閻愨晜鏂€闂佺粯鍔栨竟鍡浰囬敃鈧埞鎴︻敊閼测晝顔婇梺璇″櫘閸ｏ綁銆侀弴顫稏妞ゆ垶瀵ч幑锝嗕繆閵堝洤啸闁稿鐩獮濠冩償閵忊剝鐝峰┑鐐村灦閿曗晠宕甸弴銏＄厵闁圭粯甯╅崕鎴犵磼閳ь剚寰勯幇顓犲幗闂婎偄娴勭徊鑺ョ濠靛洣绻嗛悹鍥囧懐鏆ら悗瑙勬礈閸忔ɑ淇婇幖浣规櫇闁逞屽墴閹浇銇愰幒鎾斥偓鍫曠叓閸ャ劍鈷掑褜鍓熼幃妯跨疀閹捐泛鈪靛Δ鐘靛仜缁夌敻骞嗛弮鍫熸櫜闁糕剝鐟ч妶鐑芥⒒娴ｈ櫣甯涢柡灞诲妿閳ь剚鍑归崰妤冣偓闈涖偢瀹曟﹢顢欓崲?;
                return false;
            }

            if (env.isWindy && ContainsAnyTag(skillName, WindSkills))
                intent.parameters["env_boost_wind"] = "true";

            if (env.isDark && ContainsAnyTag(skillName, LightSkills))
                intent.parameters["env_exposure"] = "true";
        }

        if (env.isFoggy && (intent.actionType == ActionType.Move || intent.actionType == ActionType.Explore))
            intent.parameters["env_fog_risk"] = "true";

        if (env.isDark && intent.actionType == ActionType.Observe)
            intent.parameters["env_vision_limited"] = "true";

        if (env.isWet && intent.actionType == ActionType.Rest)
            intent.parameters["env_damp_rest"] = "true";

        return true;
    }

    private bool ContainsAnyTag(string value, HashSet<string> tags)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var tag in tags)
        {
            if (value.Contains(tag))
                return true;
        }

        return false;
    }

    private bool ValidateActionByType(IntentResult intent, RoleState state, out string failReason)
    {
        failReason = string.Empty;
        InventoryStateUtility.EnsureCompatibility(state, GameLoop.Instance != null ? GameLoop.Instance.CurrentItemLibrary : null);

        switch (intent.actionType)
        {
            case ActionType.UseItem:
            {
                string targetName = ResolveTargetName(intent);
                if (!string.IsNullOrWhiteSpace(targetName) && !InventoryStateUtility.HasInventoryItem(state, targetName))
                {
                    failReason = $"婵犵數鍋犻幓顏嗗緤閹稿孩鍙忛柣鎴ｅГ閸婄兘鏌￠崶銉ョ仾闁绘帟鍋愰埀顒€绠嶉崕閬嶆偋濠婂牆缁╁ù鐘差儐閻撴洟鏌曟径鍫濆姶闁绘搩鍘剧槐鎺撴綇閵娧呯杽閻庤娲╃槐鏇㈠焵椤掍胶鈯曟い顓炴搐椤繑銈ｉ崘鈺冨幐闂佸壊鍋呯换宥呂ｉ悷閭︾唵閻熸瑥瀚搁懓璺ㄢ偓瑙勬礃瀹€鎼佸箠濠婂牜鏁勯柦妯侯槷婢规洘绻濈喊澶岀？闁搞劍鐡玶getName}闂傚倷绶氬褍螞濡ゅ拋鏁勯柛鎰靛枛閻?;
                    return false;
                }

                break;
            }

            case ActionType.UseSkill:
            {
                string skillName = intent.parameters.ContainsKey("skill_name")
                    ? intent.parameters["skill_name"]
                    : intent.targetEntity;

                if (!string.IsNullOrEmpty(skillName) && state.equipment.equippedSkills != null)
                {
                    if (!state.equipment.equippedSkills.Exists(skill => skill.Contains(skillName)))
                    {
                        failReason = $"婵犵數鍋犻幓顏嗗緤閹稿孩鍙忛柟缁㈠枟閸庡秴鈹戦悩瀹犲閻庢艾顦伴妵鍕疀閹捐櫕娈堕梺浼欑畱濞寸兘鍩€椤掑倹鏆╁褎顨婃俊闈涱煥閸繄鍔﹀銈嗗坊閸嬫挻銇勯鍕ゾkillName}闂傚倷绶氬褍螞濡ゅ拋鏁勯柛顐犲灪瀹曟煡鏌″搴″箺闁抽攱甯￠弻鏇熺箾閸喖濮庡銈庡亞婵數鎹㈠☉娆忕窞閻庯綆鍋嗛敍姗€姊?;
                        return false;
                    }
                }

                break;
            }

            case ActionType.Attack:
                if (state.equipment.equipmentSlots.GetSlot(EquipSlotType.Weapon) == null)
                    intent.parameters["unarmed"] = "true";
                break;
        }

        return true;
    }

    public string ExecuteDeterministicLogic(string inputAction, RoleState currentState, EnvironmentState envState, IntentResult intent)
    {
        var results = new List<string>();
        envState ??= EnvironmentState.GetDefault();
        envState.EnsureCollections();
        InventoryStateUtility.EnsureCompatibility(currentState, GameLoop.Instance != null ? GameLoop.Instance.CurrentItemLibrary : null);

        if (ActionCosts.TryGetValue(intent.actionType, out var cost) && cost.manaCost > 0)
        {
            currentState.attributes.currentMana -= cost.manaCost;
            results.Add($"闂傚倷娴囬褏鎹㈠Ο渚劷鐟滃酣骞堥妸銊х杸闁圭虎鍨遍～宥夋⒑閸撴彃浜剧紒鍙夋そ瀹?-{cost.manaCost}");
        }

        string verdict = GenerateLocalVerdict(intent, currentState, envState);
        if (!string.IsNullOrEmpty(verdict))
            results.Add(verdict);

        InventoryStateUtility.NormalizeResourceCaps(currentState, InventoryStateUtility.CalculateDerivedAttributes(currentState));
        if (results.Count == 0)
            return "[缂傚倸鍊风欢锟犲垂闂堟稓鏆﹂柣銏ゆ涧閸ㄦ繃绻涘顔荤盎缂佲偓婢舵劖鐓忓┑鐐茬仢閳ь剚顨堢划娆愮節閸曘劌浜鹃柣鐔哄閸熺偤鏌ｉ幙鍕瘈闁糕斁鍋撳銈嗗笂濡炴帗绂嶉姀銏㈢＜閻犲洦褰冮崜鐩嘡闂傚倷绀侀幖顐ょ矙閸曨厽宕叉繝闈涱儐閸嬫ɑ绻涢崱妯诲鞍闁哄拋鍓涢埀顒€鍘滈崑鎾绘煃瑜滈崜鐔煎箖濞差亜绠ｆ繝鍨姇濞堫偊姊洪崨濠佺繁闁告鍋愮槐鐐哄磼濮ｎ厾鎳撻…銊╁礃閹冨闂備浇妗ㄩ悞锕傚箲閸ヮ剛宓侀悗锝庡枛缁犳盯鏌嶇憴鍕姢濞存粍鍎宠彁?;

        return $"[闂傚倷绀侀幖顐︽偋濠婂牆绀堟繛鍡楅獜閼板潡鎮楅棃娑欏暈闁哥喐鍨甸湁闁挎繂娴傞悞楣冩煛鐎Ｑ冧壕] {string.Join(" | ", results)}";
    }

    public string ExecuteDeterministicLogic(string inputAction, RoleState currentState, IntentResult intent)
    {
        return ExecuteDeterministicLogic(inputAction, currentState, null, intent);
    }

    private string GenerateLocalVerdict(IntentResult intent, RoleState state, EnvironmentState env)
    {
        var derived = InventoryStateUtility.CalculateDerivedAttributes(state);

        switch (intent.actionType)
        {
            case ActionType.Attack:
                int attackPower = derived.attackPower;
                if (env.isWet)
                {
                    attackPower = Mathf.Max(1, attackPower - 2);
                    return $"闂傚倷娴囬妴鈧柛瀣崌閹綊宕堕妸銉хシ濠殿噯绲介敃顏堝蓟濞戙垹鐓涢柛鏇ㄥ墻娴犫晛顪冮妶鍐ㄧ仼闁瑰啿绻掗幑銏犫槈濞嗘劕顎撶紓浣割儐椤戞瑥顭囧┑鍫㈢＜缂備降鍨瑰楣冩煕閻樺磭娲存い銏∩戠换婵嗩潩椤撶喐鐝梺璇茬箳閸嬬喖宕戦幘璇叉辈濞寸厧鐡ㄩ悡鏇㈡煙鏉堝墽绉甸柛銈嗙懃閳?{attackPower}闂傚倷鐒︾€笛呯矙閹达附鍋嬪┑鐘叉处閸嬵亪鏌涢埄鍐炬闁兼澘娼￠幃褰掑箒閹烘垵顬堢紓浣哄У閼归箖鈥?-2闂?;
                }
                return $"闂傚倷娴囬妴鈧柛瀣崌閹綊宕堕妸銉хシ濠殿噯绲介敃顏堝蓟濞戙垹鐓涢柛鏇ㄥ墻娴犫晛顪冮妶鍐ㄧ仼闁瑰啿绻掗幑銏犫槈濞嗘劕顎撶紓浣割儐椤戞瑥顭囧┑鍫㈢＜缂備降鍨瑰楣冩煕閻樺磭娲存い銏∩戠换婵嗩潩椤撶喐鐝梺璇茬箳閸嬬喖宕戦幘璇叉辈濞寸厧鐡ㄩ悡鏇㈡煙鏉堝墽绉甸柛銈嗙懃閳?{attackPower}";

            case ActionType.Defend:
                return "闂備礁鎼ˇ顐﹀疾濠婂懐鐭欓柟杈剧畱閻鏌涢埄鍐姇闁抽攱鎹囬弻鐔虹磼濡櫣鐟ㄩ梺杞扮劍閻楁洟鍩為幋锔绘晩闁芥ê顦介崵瀣⒑闁偛鑻崢鍝ョ磼闊彃鈧危閹邦兘鏀介柛銉戝苯娈奸梻渚€娼ч悧鍡涘箯鐎ｎ喖鑸规繛宸簼閻撴洟鏌熺€涙绠樼紒鐘卞嵆閺屾稑螣缁嬪簱鍋撳┑瀣畺濞寸姴顑嗛崑鍕煕濞戞﹫鏀婚柣?;

            case ActionType.Rest:
                int healAmount = Mathf.Min(10, derived.maxHealthTotal - state.attributes.currentHealth);
                if (env.isWet || intent.parameters.ContainsKey("env_damp_rest"))
                {
                    healAmount = Mathf.Max(1, healAmount / 2);
                    state.attributes.currentHealth += healAmount;
                    return $"婵犵數鍋炲娆撳触鐎ｎ喖鍨傞柤鎼佹涧椤曢亶鏌涘☉娆愮稇缂佺姵甯￠弻鏇＄疀閺囩儐鈧本淇婇幓鎺濈吋闁哄矉缍佹慨鈧柕蹇曞У閻庡姊?+{healAmount}闂傚倷鐒︾€笛呯矙閹达附鍋嬪┑鐘叉处閸嬵亪鏌涢埄鍐炬闁兼澘娼￠幃褰掑箒閹烘垵顬堢紓浣哄У閼归箖鈥﹂懗顖ｆЩ闂佸摜鍣ラ崹璺侯嚕閺屻儱唯闁宠桨绀侀崵鎴濃攽閻樿宸ラ柛鐕佸亜閳诲秹濡舵径瀣幈濠德板€愰崑鎾淬亜椤撗冨箻婵?;
                }
                state.attributes.currentHealth += healAmount;
                return $"婵犵數鍋炲娆撳触鐎ｎ喖鍨傞柤鎼佹涧椤曢亶鏌涘☉娆愮稇缂佺姵甯￠弻鏇＄疀閺囩儐鈧本淇婇幓鎺濈吋闁哄矉缍佹慨鈧柕蹇曞У閻庡姊?+{healAmount}";

            case ActionType.Cultivate:
                int manaRecover = Mathf.Min(15, derived.maxManaTotal - state.attributes.currentMana);
                state.attributes.currentMana += manaRecover;
                return $"婵犵數鍎戠徊钘壝归崒鐐茬獥婵娉涚壕鑽ゆ喐閺冨牏宓佸〒姘ｅ亾濠殿喒鍋撻梺缁樼憿閸嬫挻淇婇幓鎺濈吋闁哄矉缍€缁犳稑鈽夊▎鎰版暘濠?+{manaRecover}";

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
            results.Add("闂備浇宕甸崰鎰版偡閵夆晛纾归柟闂寸劍閸庡矂鏌涚仦鍓х煁闁稿锕㈤幃姗€鎮欓懜娈挎閻炴氨鍠栧娲箰鎼达絺濮囨繛锝呮处濡炰粙鏁愰悙纰樺亾閿濆骸鏋熼柣鎾冲€块獮鏍垝閸忓浜剧€规洖娲ㄨⅲ闂傚倸鍊搁崐鍝モ偓姘煎弮瀹曟繆顦存俊鍙夊姇閳规垿宕堕妸銉ュΤ闂備礁鎼崯顐⒚洪敐鍥у灊闁汇垹鎲￠悡鐔兼煙閾忕懓浠ч柣锝囨暩缁辨帡顢氶崱娆戞殼閻庢鍠栭崯鍧椻€旈崘顔肩闁归偊鍓欑粻鎴︽⒒娴ｅ湱婀介柛鏂跨灱閳ь剚鍑归崰姘跺礆閹烘洜鐤€闁瑰灝鍟▓顐㈩渻閵堝棙鈷掗柕鍡楊儑濡叉劙鏁愭径瀣幈濠电娀娼уΛ娑樼毈缂傚倷鐒﹂〃鍛存儔婵傜绠熼柟缁㈠枟閺呮粓鏌﹀Ο渚Ц闂?);
        else if (env.isFoggy)
            results.Add("闂備浇宕甸崰鎰版偡閵夆晛纾归柟闂寸劍閸庡矂鏌涚仦鍓х煁闁稿锕㈤幃姗€鎮欓懜娈挎閻炴氨鍠栧娲箰鎼达絺濮囨繛锝呮穿濞咃綁宕犻弽顓熷亹缂備焦菤閹稿懏淇婇妶蹇曞埌闁哥啿鏅濆Σ鎰板即閵忥紕鍘电紓渚囧灡濞叉﹢锝炴径宀€纾奸弶鍫涘妿缁犳牠鏌熼娑欘棃鐎殿噮鍣ｉ崺鈧い鎺嗗亾妞ゆ洩缍佸畷绋课旈埀顒傜矆閸愨晝绠鹃柛鈩兠粭鎺楁煕閵娾晙鎲鹃柡灞剧洴閺佸倿骞嗚閺嗩參姊虹紒妯煎ⅹ濠⒀冮叄楠炴劘顦规鐐茬Ч椤㈡瑩鎮剧仦鐐啅闂傚倸鍊搁崐绋课涘Δ鍕╀汗闁绘劗鏁哥粻鏃堟煥濠靛棙顥犻柍缁樻楠炴牕菐椤掆偓閳ь剚娲熼、鏃堟偄閸忓皷鎷婚梺鍛婁緱閸犳牗鎱ㄥ畝鍕€垫慨妯稿劚閻忔煡鏌熼钘夆枙妤犵偛绉归、娆撴偩鐏炵偓顔忛梻浣告惈椤﹂亶宕曢幇鏉跨獥闁哄稁鍋勯崹?);
        else
            results.Add("闂備浇宕甸崰鎰版偡閵夆晛纾归柟闂寸劍閸庡矂鏌涚仦鍓х煁闁稿锕㈤幃姗€鎮欓懜娈挎閻炴氨鍠栧娲箰鎼达絺濮囧銈嗗灥閳?闂備礁婀遍崢褔鎮洪妸銉綎缁绢厼鎳屾禍鐟懊归敐鍛础闁荤喕顫夌换娑㈠箣閻愬棙鍨垮畷鎶筋敍閻愬鍘遍梺闈涱槶閸庢椽宕甸悢鍓叉闁绘劕寮堕ˉ鐐烘煛閸涱厾鍩ｇ€规洘锕㈤、鏃堝幢濞嗘垵甯為梻?);

        if (IsInZhaoYao(env) && !env.HasClue("herbs_spotted"))
        {
            env.AddClue("herbs_spotted");
            env.AddTag("闂傚倷娴囬褏鎹㈢€ｎ剙绶ら悹鍥皺閺嗭箓鐓崶銊с€掗柛娆愬笚閵囧嫯绠涢幘鏉戭槱婵?);
            env.currentObjective = "闂傚倸鍊烽悞锕併亹閸愵煁娲敇閻愨晜鐏冮梺鎸庢煥婢т粙鎯岄崼鐔虹瘈闂傚牊绋掗崳鐣岀磼鐎ｎ亝鍠橀柡宀€鍠撻埀顒佺⊕椤洦鏅跺畷鍥╃＜妞ゆ牗绮屽暩闂佺懓顨庨崑濠傜暦濞嗘挸围闁搞儮鏅╁Σ宄扳攽閻愭潙鐏﹂柟閫涚閳诲秹鏁愭径濠勵吅婵＄偛顑呭ù椋庡閸忕浜滈柟鎹愭硾閺嬫梻绱掗悩鍐叉诞闁哄本绋栫粻娑氣偓锝庝簻婵洟鏌ｆ惔銏㈩暡闁搞劌娼￠悰顔嘉旈崨顓犲€為悷婊冪Ф閳ь剙鐏氭繛濠傤嚕閸洖鐓涢柛鎰╁妼楠炲鈹戦悙鑼憼闁挎洏鍨归悾鐑藉箣濠垫劖鍍靛銈嗗坊閸嬫挻绻涢崨顔炬噧妞ゎ叀娉曢幑鍕倻濡搫浜堕梻?;
            Logic.GraphRAG.GraphRAGManager.Instance.DiscoverEntity("herb_zhuyu");
            Logic.GraphRAG.GraphRAGManager.Instance.DiscoverEntity("herb_migu");
            results.Add("闂備浇顕х换鎺楀磻濞戞鏆︽い鎺戝婢舵劕骞㈡俊銈咃功閸斿嘲顪冮妶鍡樷拻闁稿鎹囪棢婵炲棗绻嗗Σ鍫ユ煛閸屾侗鍎ラ柛搴㈡⒐娣囧﹪顢曢姀鈥充淮閻庢鍠曢崡鎶界嵁閸℃凹妲诲┑顕嗙到椤﹁京妲愰幒鎾村闁革富鍘鹃悾杈╃磽閸屾氨校闁搞劌澧庣划娆愬緞鐎ｎ偄鍔呴梺闈涚墕濞层劑銆傞妸鈺傜厽闁绘ê妯婇崕鏃€銇勯妷锔藉碍闁崇粯鎹囬獮瀣晜閽樺澧婚梻浣告惈濞茬娀宕滃┑鍥ㄥ弿?);
        }
        else if (env.HasClue("deep_path_opened") && !env.HasClue("aberration_foretold"))
        {
            env.AddClue("aberration_foretold");
            env.AddTag("闂傚倸鍊搁崐鎼佹偋婵犲嫭鏆滈柣鎰ゴ閺嬪秶鎲稿澶樻晪闁挎繂顦壕鍏兼叏濮楀棗澧弶?);
            env.currentObjective = "闂傚倷鑳堕崕鐢稿疾閳哄懎鍨傞柟鎯版缁犵敻鏌ㄥ┑鍡樺闁搞倖娲熼弻娑氫沪閹规劕顥濋柣銏╁灛閸斿秶鎹㈠┑濠庢Ъ闂佸憡姊瑰ú鐔镐繆閻㈢绫嶉柛顐ゅ枑濞呮牠鎮楅崗澶婁壕闂佸憡鍔︽禍婵嬵敇婵犳碍鈷戦柣鐔告緲閼哥懓螖閻樿櫕鍊愮€规洝顫夌粋鎺斺偓锝庝簼椤ユ繂顪冮妶鍡樺暗闁哥姵鍔楅悷褔姊绘担鍛婂暈闁绘棏鍓熷浠嬪礋椤愵剝鈧灝霉閿濆懎顥忛柛銈嗘礋閺屾稓浠﹂幑鎰棟闂佽鍠撻崹钘夘潖濞差亶鏁冮柕澶堝労濡牏绱撴担绛嬪殭闂佸府绲炬穱?;
            results.Add("闂傚倸鍊搁崐绋课涘▎鎾崇？闁规儼妫勭粻鏍ㄤ繆閵堝懏鍣归悗姘槸椤潡鎳滈棃娑橆潔缂傚倸绉电敮锟犲蓟閿濆牜妯佸┑鈽嗗亜鐎氼喖危閹版澘绠婚悹鍥皺椤ρ勭節閵忥絽鐓愰柛鏃€鐗犻弫宥堢疀濞戞瑧鍘介棅顐㈡祫缁茶棄顕ｇ捄銊х＜閺夊牄鍔庣粻鐐碘偓娈垮枤椤牐鐏冮梺鍛婁緱閸ｎ垳妲愰崹顐ょ閻庣數顭堢敮鍫曟煕濮橆剦鍎忛柨鏇樺灪閹峰懘宕滈幓鎺擃吙闂備胶顭堥張顒勬嚌妤ｅ啫绠栭柕蹇嬪€栭悡鏇㈡煟閺傛崘顒熼柣鎺楃畺閺?);
        }

        return string.Join("闂?, results);
    }

    private string HandleCollect(IntentResult intent, RoleState state, EnvironmentState env)
    {
        string targetName = ResolveTargetName(intent);
        if (string.IsNullOrWhiteSpace(targetName))
        {
            if (env.HasClue("herbs_spotted") && !env.HasClue("zhuyu_collected"))
                targetName = ZhuyuName;
            else if (env.HasClue("herbs_spotted") && !env.HasClue("migu_collected"))
                targetName = MiguName;
        }

        if (ContainsText(targetName, ZhuyuName))
        {
            if (!TryAddLocalItem(state, ZhuyuTemplateId, ZhuyuName, DecodeEscaped("\\u8349\\u53f6\\u5fae\\u9752\\uff0c\\u63e1\\u5728\\u638c\\u5fc3\\u65f6\\u6709\\u6de1\\u6de1\\u8349\\u6728\\u6e05\\u9999\\u3002"), DecodeEscaped("\\u98df\\u4e4b\\u53ef\\u6682\\u7f13\\u9965\\u4e4f\\u3002")))
                return DecodeEscaped("\\u4f60\\u5df2\\u8fa8\\u51fa\\u795d\\u4f59\\uff0c\\u4f46\\u56e0\\u80cc\\u5305\\u5df2\\u6ee1\\u800c\\u65e0\\u6cd5\\u6536\\u7eb3\\u3002");

            Logic.GraphRAG.GraphRAGManager.Instance.DiscoverEntity("herb_zhuyu");
            env.AddClue("zhuyu_collected");
            env.AddTag(DecodeEscaped("\\u795d\\u4f59\\u5df2\\u91c7"));
            env.currentObjective = env.HasClue("migu_collected")
                ? DecodeEscaped("\\u501f\\u8ff7\\u8c37\\u8fa8\\u8def\\uff0c\\u7ee7\\u7eed\\u5411\\u62db\\u6447\\u5c71\\u6df1\\u5904\\u63a2\\u7d22\\u3002")
                : DecodeEscaped("\\u7ee7\\u7eed\\u8fa8\\u8ba4\\u8ff7\\u8c37\\uff0c\\u51c6\\u5907\\u5728\\u8ff7\\u96fe\\u4e2d\\u7a33\\u5b9a\\u524d\\u8fdb\\u3002");
            AddExperience(state, 8);
            return DecodeEscaped("\\u53d6\\u5f97\\u4e00\\u682a\\u795d\\u4f59\\u5e76\\u6536\\u5165\\u884c\\u56ca\\uff0c\\u7ecf\\u9a8c") + " +8";
        }

        if (ContainsText(targetName, MiguName))
        {
            if (!TryAddLocalItem(state, MiguTemplateId, MiguName, DecodeEscaped("\\u679d\\u53f6\\u9ed1\\u7406\\uff0c\\u8d34\\u8fd1\\u65f6\\u4f1a\\u6cdb\\u51fa\\u5e7d\\u9752\\u53cd\\u5149\\u3002"), DecodeEscaped("\\u53ef\\u5728\\u96fe\\u4e2d\\u8fa8\\u8ba4\\u8def\\u5f84\\u3002")))
                return DecodeEscaped("\\u4f60\\u627e\\u5230\\u4e86\\u8ff7\\u8c37\\uff0c\\u5374\\u56e0\\u80cc\\u5305\\u5df2\\u6ee1\\u800c\\u6682\\u65f6\\u65e0\\u6cd5\\u5e26\\u8d70\\u3002");

            Logic.GraphRAG.GraphRAGManager.Instance.DiscoverEntity("herb_migu");
            env.AddClue("migu_collected");
            env.AddTag(DecodeEscaped("\\u8ff7\\u8c37\\u5728\\u624b"));
            env.RemoveTag(DecodeEscaped("\\u8ff7\\u5931\\u65b9\\u5411"));
            env.currentObjective = DecodeEscaped("\\u501f\\u8ff7\\u8c37\\u8fa8\\u8def\\uff0c\\u7ee7\\u7eed\\u5411\\u62db\\u6447\\u5c71\\u6df1\\u5904\\u63a2\\u7d22\\u3002");
            AddExperience(state, 10);
            return DecodeEscaped("\\u53d6\\u5f97\\u8ff7\\u8c37\\uff0c\\u53ef\\u501f\\u5176\\u8fa8\\u6e05\\u96fe\\u4e2d\\u8def\\u5f84\\uff0c\\u7ecf\\u9a8c") + " +10";
        }

        return DecodeEscaped("\\u91c7\\u96c6\\u52a8\\u4f5c\\u5df2\\u786e\\u8ba4\\uff0c\\u4f46\\u6682\\u672a\\u547d\\u4e2d\\u5173\\u952e\\u7075\\u8349\\u3002");
    }

    private string HandleUseItem(IntentResult intent, RoleState state, EnvironmentState env)
    {
        string targetName = ResolveTargetName(intent);
        if (TryApplyDirectItemUse(targetName, state, env, out string directUseMessage))
            return directUseMessage;

        if (ContainsText(targetName, HealingPotionName) && ConsumeInventoryItem(state, HealingPotionName))
        {
            int healAmount = Mathf.Min(18, InventoryStateUtility.CalculateDerivedAttributes(state).maxHealthTotal - state.attributes.currentHealth);
            state.attributes.currentHealth += healAmount;
            env.AddTag(DecodeEscaped("\\u836f\\u6c14\\u56de\\u6696"));
            return DecodeEscaped("\\u670d\\u4e0b\\u6cbb\\u7597\\u836f\\u6c34\\uff0c\\u751f\\u547d\\u6062\\u590d") + $" +{healAmount}";
        }

        if (ContainsText(targetName, ZhuyuName) && ConsumeInventoryItem(state, ZhuyuName))
        {
            var derived = InventoryStateUtility.CalculateDerivedAttributes(state);
            int healAmount = Mathf.Min(6, derived.maxHealthTotal - state.attributes.currentHealth);
            int manaAmount = Mathf.Min(6, derived.maxManaTotal - state.attributes.currentMana);
            state.attributes.currentHealth += healAmount;
            state.attributes.currentMana += manaAmount;
            env.AddTag(DecodeEscaped("\\u8179\\u4e2d\\u6709\\u5b9e"));
            env.currentObjective = DecodeEscaped("\\u4f53\\u529b\\u7a0d\\u5b9a\\uff0c\\u53ef\\u4ee5\\u7ee7\\u7eed\\u89c2\\u5bdf\\u6216\\u6df1\\u5165\\u8ff7\\u96fe\\u3002");
            return DecodeEscaped("\\u54bd\\u4e0b\\u795d\\u4f59\\u540e\\u6c14\\u606f\\u7a0d\\u5b9a\\uff0c\\u751f\\u547d") + $" +{healAmount}" + DecodeEscaped("\\uff0c\\u7075\\u529b") + $" +{manaAmount}";
        }

        if (ContainsText(targetName, MiguName) && InventoryStateUtility.HasInventoryItem(state, MiguName))
        {
            env.RemoveTag(DecodeEscaped("\\u8ff7\\u5931\\u65b9\\u5411"));
            env.AddTag(DecodeEscaped("\\u8ff7\\u8c37\\u6307\\u8def"));
            env.currentObjective = DecodeEscaped("\\u6cbf\\u96fe\\u5f84\\u6df1\\u5165\\uff0c\\u89c2\\u5bdf\\u9752\\u767d\\u5f02\\u5149\\u7684\\u6765\\u6e90\\u3002");
            return DecodeEscaped("\\u4f69\\u4e0a\\u8ff7\\u8c37\\u540e\\uff0c\\u96fe\\u4e2d\\u7684\\u8def\\u5f84\\u8f6e\\u5ed3\\u9010\\u6e10\\u6e05\\u6670\\u3002");
        }

        return DecodeEscaped("\\u4f7f\\u7528\\u7269\\u54c1\\u52a8\\u4f5c\\u5df2\\u786e\\u8ba4\\uff0cAI \\u5e94\\u63cf\\u5199\\u5668\\u7269\\u89e6\\u611f\\u4e0e\\u8eab\\u4f53\\u53cd\\u9988\\u3002");
    }

    public bool TryUseInventoryItemDirect(RoleState state, EnvironmentState env, ItemInventoryEntry entry, SceneItemLibraryData itemLibrary, out string resultMessage)
    {
        resultMessage = null;
        if (state == null || entry == null)
        {
            resultMessage = DecodeEscaped("\\u9053\\u5177\\u72b6\\u6001\\u5f02\\u5e38\\uff0c\\u65e0\\u6cd5\\u76f4\\u63a5\\u4f7f\\u7528\\u3002");
            return false;
        }

        env ??= GameLoop.Instance != null ? GameLoop.Instance.CurrentEnvironment : EnvironmentState.GetDefault();
        env.EnsureCollections();
        itemLibrary ??= GameLoop.Instance != null ? GameLoop.Instance.CurrentItemLibrary : null;
        InventoryStateUtility.EnsureCompatibility(state, itemLibrary);

        var template = InventoryStateUtility.ResolveTemplate(itemLibrary, entry);
        string targetName = entry.runtimeData?.name ?? template?.displayName ?? entry.templateId;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            resultMessage = DecodeEscaped("\\u672a\\u80fd\\u8bc6\\u522b\\u8be5\\u9053\\u5177\\u540d\\u79f0\\uff0c\\u65e0\\u6cd5\\u76f4\\u63a5\\u4f7f\\u7528\\u3002");
            return false;
        }

        if (TryApplyDirectItemUse(targetName, state, env, out resultMessage))
            return true;

        resultMessage = DecodeEscaped("\\u8be5\\u7269\\u54c1\\u5f53\\u524d\\u4e0d\\u53ef\\u76f4\\u63a5\\u4f7f\\u7528\\u3002");
        return false;
    }

    private bool TryApplyDirectItemUse(string targetName, RoleState state, EnvironmentState env, out string resultMessage)
    {
        resultMessage = null;
        if (ContainsText(targetName, HealingPotionName) && ConsumeInventoryItem(state, HealingPotionName))
        {
            int healAmount = Mathf.Min(18, InventoryStateUtility.CalculateDerivedAttributes(state).maxHealthTotal - state.attributes.currentHealth);
            state.attributes.currentHealth += healAmount;
            env?.AddTag(DecodeEscaped("\\u836f\\u6c14\\u56de\\u6696"));
            InventoryStateUtility.NormalizeResourceCaps(state, InventoryStateUtility.CalculateDerivedAttributes(state));
            resultMessage = DecodeEscaped("\\u670d\\u4e0b\\u6cbb\\u7597\\u836f\\u6c34\\uff0c\\u751f\\u547d\\u6062\\u590d") + $" +{healAmount}";
            return true;
        }

        if (ContainsText(targetName, ZhuyuName) && ConsumeInventoryItem(state, ZhuyuName))
        {
            var derived = InventoryStateUtility.CalculateDerivedAttributes(state);
            int healAmount = Mathf.Min(6, derived.maxHealthTotal - state.attributes.currentHealth);
            int manaAmount = Mathf.Min(6, derived.maxManaTotal - state.attributes.currentMana);
            state.attributes.currentHealth += healAmount;
            state.attributes.currentMana += manaAmount;
            env?.AddTag(DecodeEscaped("\\u8179\\u4e2d\\u6709\\u5b9e"));
            if (env != null)
                env.currentObjective = DecodeEscaped("\\u4f53\\u529b\\u7a0d\\u5b9a\\uff0c\\u53ef\\u4ee5\\u7ee7\\u7eed\\u89c2\\u5bdf\\u6216\\u6df1\\u5165\\u8ff7\\u96fe\\u3002");
            InventoryStateUtility.NormalizeResourceCaps(state, InventoryStateUtility.CalculateDerivedAttributes(state));
            resultMessage = DecodeEscaped("\\u54bd\\u4e0b\\u795d\\u4f59\\u540e\\u6c14\\u606f\\u7a0d\\u5b9a\\uff0c\\u751f\\u547d") + $" +{healAmount}" + DecodeEscaped("\\uff0c\\u7075\\u529b") + $" +{manaAmount}";
            return true;
        }

        if (ContainsText(targetName, MiguName) && InventoryStateUtility.HasInventoryItem(state, MiguName))
        {
            env?.RemoveTag(DecodeEscaped("\\u8ff7\\u5931\\u65b9\\u5411"));
            env?.AddTag(DecodeEscaped("\\u8ff7\\u8c37\\u6307\\u8def"));
            if (env != null)
                env.currentObjective = DecodeEscaped("\\u6cbf\\u96fe\\u5f84\\u6df1\\u5165\\uff0c\\u89c2\\u5bdf\\u9752\\u767d\\u5f02\\u5149\\u7684\\u6765\\u6e90\\u3002");
            resultMessage = DecodeEscaped("\\u4f69\\u4e0a\\u8ff7\\u8c37\\u540e\\uff0c\\u96fe\\u4e2d\\u7684\\u8def\\u5f84\\u8f6e\\u5ed3\\u9010\\u6e10\\u6e05\\u6670\\u3002");
            return true;
        }

        if (TryApplyGenericConsumableUse(targetName, state, env, out resultMessage))
            return true;

        return false;
    }
    private static string DecodeEscaped(string escaped)
    {
        if (string.IsNullOrEmpty(escaped))
            return string.Empty;

        return (string)JsonMapper.ToObject($"\"{escaped}\"");
    }



    private bool TryApplyGenericConsumableUse(string targetName, RoleState state, EnvironmentState env, out string resultMessage)
    {
        resultMessage = null;
        if (string.IsNullOrWhiteSpace(targetName) || state == null)
            return false;

        var library = GameLoop.Instance != null ? GameLoop.Instance.CurrentItemLibrary : null;
        var entry = InventoryStateUtility.FindInventoryEntryByName(state, targetName, out _);
        if (entry == null)
            return false;

        var template = InventoryStateUtility.ResolveTemplate(library, entry);
        if (template == null || template.itemKind != ItemKind.Consumable)
            return false;

        string displayName = entry.runtimeData?.name ?? template.displayName ?? targetName;

        if (ContainsText(displayName, HealingPotionName) || ContainsText(displayName, ZhuyuName))
            return false;

        if (!InventoryStateUtility.TryRemoveItem(state, entry.runtimeData?.instanceId, null, 1, out _))
            return false;

        var derived = InventoryStateUtility.CalculateDerivedAttributes(state);
        int healAmount = 0;
        int manaAmount = 0;

        if (entry.runtimeData?.statModifiers != null)
        {
            foreach (var modifier in entry.runtimeData.statModifiers)
            {
                if (modifier == null || string.IsNullOrWhiteSpace(modifier.statKey) || modifier.value <= 0)
                    continue;

                switch (modifier.statKey.Trim().ToLowerInvariant())
                {
                    case "max_health":
                        healAmount += modifier.value;
                        break;
                    case "max_mana":
                        manaAmount += modifier.value;
                        break;
                }
            }
        }

        healAmount = Mathf.Min(healAmount, derived.maxHealthTotal - state.attributes.currentHealth);
        manaAmount = Mathf.Min(manaAmount, derived.maxManaTotal - state.attributes.currentMana);
        state.attributes.currentHealth += healAmount;
        state.attributes.currentMana += manaAmount;
        InventoryStateUtility.NormalizeResourceCaps(state, InventoryStateUtility.CalculateDerivedAttributes(state));

        if (healAmount > 0 || manaAmount > 0)
        {
            resultMessage = $"{displayName} 闂備浇顕у锕傦綖婢舵劖鍋ら柡鍥╁剱閸ゆ洟鏌熼幆褜鍤熺紒鐘荤畺閺屾盯濡烽鐓庮潽闂佺顑冮崝宥夊Φ閸曨垼鏁囬柣鏃堫棑椤戝倻绱撴担鎻掍壕婵犮垼鍩栭崝鏍偂閻斿吋鐓忛煫鍥ь儏閻忊晠鏌＄€ｎ亪鍙勯柡?+{healAmount}闂傚倸鍊烽悞锔锯偓绗涘懐鐭欓柟杈鹃檮閸庢棃鏌涜箛鏇炲付妞ゆ洟浜堕弻鐔兼倻濮楀棙鐣剁紓浣插亾?+{manaAmount}";
        }
        else
        {
            resultMessage = string.IsNullOrWhiteSpace(entry.runtimeData?.effectText)
                ? $"{displayName} 闂備浇顕у锕傦綖婢舵劖鍋ら柡鍥╁剱閸ゆ洟鏌熼幆褜鍤熺紒鐘荤畺閺屾盯濡烽鐓庮潽闂佺顑冮崝鎴﹀蓟閺囩喓绡€闊洦娲栭惌婵嬫⒑?"
                : $"{displayName} 闂備浇顕у锕傦綖婢舵劖鍋ら柡鍥╁剱閸ゆ洟鏌熼幆褜鍤熺紒鐘荤畺閺屾盯濡烽鐓庮潽闂佺顑冮崝宥夊Φ閸曨垼鏁囬柣鏃堫棑椤戝倻绱撴担鐟板闁冲嘲鐛渘try.runtimeData.effectText}";
        }

        env?.AddTag("闂傚倸鍊烽懗鍓佸垝椤栫偛桅婵炴垯鍨归悿鐐箾閹存瑥鐏╃紒鐘叉贡閹茬顓兼径妯绘櫓闂婎偄娲︾粙鎾诲矗閹炬番浜滈柟鎹愭硾娴滃綊鏌＄€ｎ亞绠绘慨?");
        return true;
    }

    private string HandleTraversal(IntentResult intent, RoleState state, EnvironmentState env)
    {
        string direction = intent.parameters.ContainsKey("direction") ? intent.parameters["direction"] : "闂傚倷绀侀幉锟犲箰閸濄儳鐭撻柣銏㈩焾閽?;

        if (env.isFoggy && !InventoryStateUtility.HasInventoryItem(state, MiguName) && !env.HasClue("migu_collected"))
        {
            state.attributes.currentHealth = Mathf.Max(1, state.attributes.currentHealth - 3);
            env.AddTag("闂備礁鎼ˇ顐﹀疾濞戙垹搴婇柡灞诲劚妗呴梺纭呮彧缁犳垵螞濮椻偓閺岋綁鎮㈤崫鍕垫毉闂?);
            env.currentObjective = "闂傚倷鑳堕…鍫㈡崲閹扮増鍋嬮柛鈩冧緱閺佸霉閸忓吋缍戠紒鐘劦閺岀喓鎲撮崟顐㈩潕缂佺偓婢橀崯鎾箖閸︻厸鍋撻敐搴℃灍闁轰線浜堕弻娑㈠Ω閿濆懎顬嬬紓浣介哺鐢喖藝閹绢喗鐓熼柕蹇ョ到閸氬綊鏌熼崣澶嬪唉鐎规洘甯掗～婵囨綇椤垟鍋撴径鎰厽闊洦娲栨禒婊呪偓瑙勬处閸撶喕妫㈤梺绯曞墲缁嬫帡寮查鍕€堕柣鎰劋閿涚喓绱掗妸銈呭祮闁哄本绋栫粻娑㈠箻缂傚簺鍨虹换婵嬪焵椤掑嫷鏁傞柛顐ゅ暱閹稿懘姊洪崨濠勬噣闁稿孩鎸冲畷銏ゎ敆閸曨剛鍘甸梺鐓庢啞椤旀牠鎮為崜褏纾肩紓浣姑悘鏉戔攽?;
            return $"闂傚倷绀侀幖顐︽偋婵犲啫顕辩€圭妾畆ection}闂備浇宕垫慨鏉懨洪敃鈧叅婵☆垰鍚嬮弳婊兠归崗鍏肩稇缂侇偄绉归弻鐔碱敍閻愯弓鍠婂┑鐐靛帶閻栫厧顕ｉ崼鏇炵厸闁告劑鍔岄獮瀣攽閻愯尙鎽犻柨鏇樺灲瀵宕ㄧ€涙ê浜楅柟鍏肩暘閸ㄥ藝閵夆晜鈷戦柟绋挎捣閳藉鏌ｉ鍌氬付閸楅亶鏌熼鍡楁湰閸嶇敻姊洪崫鍕偓璺ㄧ不閺嶎厼姹查柛娑卞姸瑜版帗鏅查柛鈩兩戦崕鎾剁磽娴ｄ粙鍝虹紒璇茬墦楠炲啯绂掔€ｎ偄浜滈梻浣哥仢椤戝懎鈻?-3";
        }

        if (IsInZhaoYao(env) && !env.HasClue("deep_path_opened"))
        {
            env.locationName = "闂傚倷绀佺紞濠囧绩鏉堚晝鐭欓柟鐑橆殔缁犵喖鏌ｉ幇顒€鎮佹繛宀婁邯閺岋綁濮€閳轰礁绠婚梺绋跨箲閿氶棁澶嬩繆椤栨瑨顒熼柛銈嗩殜閺屾盯寮撮妸銉ヮ潾缂備胶濮电敮鎺椻€?;
            env.narrativeHint = "闂備礁鎼ˇ顐﹀疾濠靛钃熷鑸靛姈閸ゅ嫭绻涘顔荤盎闁告瑥锕弻锝呂熼崹顔炬缂備浇顔婄欢姘潖婵犳艾骞㈡俊銈傚亾缂傚秵鐗滅槐鎺懳旂€ｎ剛袦閻庤娲樺畝绋跨暦閸楃倣鏃堝礃閳轰礁鐟庨梻鍌欑劍鐎笛呯矙閹达附鍤愭い鏍仜缁€灞句繆閵堝懏鍣洪柛銈呯墦閹嘲鈻庤箛鎿冧紑缂備胶濮电敮鎺椻€︾捄銊﹀磯闁告繂瀚锋导鈧繝鐢靛仜閻楀﹪宕归崹顔ユ盯宕橀鑲╊唺闂佸搫鍟犻崑鎾淬亜韫囧鈧鍩€椤掍緡鍟忛柛鐘崇墵瀹曟劙骞栨担绋垮亶濠电姴锕ら悧鍡欑尵瀹ュ鐓犵紒瀣硶娴犳稒銇勯锝囧⒌闁哄矉绻濆畷姗€濡搁妷顔芥濠电偛顕崢褔宕幘顔兼瀬鐎广儱娲ｅ▽顏堟煕閹炬鍊婚惄搴ㄦ⒒閸屾艾鈧悂宕愰悷閭︾劷婵炲棙鍔曢閬嶆煙閹规劦鍤欓悗姘槸椤法鎹勯悮鏉戝濠殿噯绲块崑銈夊蓟濞戙垺鍋勯梺鍨儏閺嗗牓鏌ｉ姀鈺佺伇闁告挻鑹鹃…鍥ㄧ節濮橆剛鍊為梺闈涱槶閸ㄨ绂?;
            env.isFoggy = false;
            env.AddClue("deep_path_opened");
            env.AddTag("闂傚倸鍊搁崐绋课涘▎鎾崇？鐎规洖娲﹂浠嬫煟閹邦剚鈻曢柛鐔锋嚇閺屾稑鈻庤箛锝喰у?);
            env.AddTag("闂佽瀛╅鏍窗閺嶎厼纾归柟闂寸鐟欙箓鏌￠崘銊уⅱ闁告瑦甯楅妵鍕疀閹炬潙顦╂繝?);
            env.currentObjective = "濠电姷鏁搁崑娑㈡嚐椤栫偞鍊舵繝闈涚墛椤洟鏌熼幑鎰靛殭缂佲偓閸屾凹鐔嗛悹铏瑰劋濠€浼存煕閳哄倻銆掔紒杈ㄦ尰缁楃喖宕惰缁犳椽鏌ｉ姀鈺佺仭濠㈢懓妫楀嵄闁规澘搴滅紞鏍ㄣ亜閺傚灝鈷旀い锔诲櫍濮婃椽宕ㄦ繝鍌滀淮濠碘槅鍋呴〃濠呮濡炪倖甯婇悞锕傚磿閻斿吋鐓涢柛灞句緱閸庛儵鏌涢妶鍥р枅闁哄本绋戦埢搴∥熼搹閫涙樊闂備胶顭堢换鎴︽晝閵忋倕绠氶柛鎰靛枛缁€瀣亜閹扳晛鈧鏁嶅┑瀣厽闁绘﹢娼ф禍褰掓煕閹惧绠炵€规洖缍婃俊鎼佸Ψ椤斿吋顓块梻浣侯焾閺堫剟鎳濇ィ鍐ㄧ畺闁靛繈鍊栭悡?;
            return $"闂傚倷鑳堕…鍫ユ晝閿曞倸绐楅幖娣灪閸欏繑銇勮箛鎾搭棤婵☆偒鍨堕幃褰掑箒閹烘垵顬堥梺绋款儏缁夊綊寮婚敓鐘查唶闁绘柨澧庣粈濯巌rection}濠电姷鏁搁崕鎴犲緤閽樺鏆︽い鎺戝閻鏌涢埄鍐姇闁哄拋鍓熼幃姗€鎮欓弶鎴濆Б闂佽绻愬ú顓㈠蓟濞戙垹绫嶉柛灞剧閻忎線姊虹€圭媭娼愮紒瀣笒椤洦绻濆顒傚€炲銈嗗笂閼冲爼銆呴銏″€甸柛顭戝亝缁舵煡鎮楀顒佹喐婵″弶鍔曢埞鎴犫偓锝庡墮缁侊箓姊洪崗鍏煎€愭繛浣冲懐鐭嗛悗锝庡枟閻撴瑩鏌ｉ悢鐓庝喊婵″弶鎮傞弻娑㈠煛娴ｈ棄顏銈嗘穿缂嶄礁鐣烽幒鎴僵妞ゆ帊绀佹禍婊勭節閻㈤潧浠╅悘蹇ｄ邯椤㈡俺顦抽柟渚垮姂閸╋繝宕ㄩ鐓庡⒒闂備礁鎼ú鐘诲礈濠靛洦鍙?;
        }

        if (env.HasClue("deep_path_opened") && !env.HasClue("aberration_triggered"))
        {
            env.AddClue("aberration_triggered");
            env.AddTag("闂佽瀛╅鏍窗閺嶎厼纾瑰ù鐘差儐鐎垫﹢鏌涢弴銊ョ仭闁绘帒顭烽弻銈囨喆閸曨偒妫嗙紓?);
            env.currentObjective = "闂備浇宕甸崰鎰版偡閵夆晛纾归柟闂寸劍閸庡矂鏌涚仦鎯у毈闁搞倖娲熼弻娑氫沪閹规劕顥濋柣銏╁灛閸旀垿寮婚埄鍐ㄧ窞闁割偅绻傛慨鏇犵磽娓氬洨鍘涢柛锝忕秮瀵偄顓奸崨顖涙畷闂佸憡鍔︽禍婵嬵敇婵犳碍鈷戦柣鐔告緲閼哥懓螖閻樿櫕鍊愮€规洘濞婇、娑樷槈濡偐鐛梻浣规偠閸庣儤绂嶉悙鐢典笉闁挎繂顦伴悡娆撴煙椤撶喎绗掗柛鏃撶畱闇夐柣鎾抽閺嗭綁鏌＄仦鑺ヮ棦鐎规洖鐖兼俊鐑藉閻樺崬顥?;
            return $"闂傚倷绀侀幖顐︽偋婵犲啫顕辩€圭妾畆ection}闂傚倷绀侀幉锟犲礉閺囩姷鐭撳瀣瀹曞弶淇婇婵嗗惞闁崇粯妫冮弻宥堫檨闁告挻鐟╅敐鐐哄閵堝憘銊︺亜閺嶃劎鐭屾い锔诲櫍濮婃椽宕ㄦ繝鍐ㄧ缂備礁顦遍弫璇差嚕婵犳碍鍋勯柛蹇曗拡濡啴姊洪崷顓炲妺婵﹤缍婇、鏃堝Χ閸ワ絽浜炬繛鍫濈仢閺嬫盯鏌涙繝鍐╁€愮€规洏鍨归…銊╁幢濞嗘垶婢戦梻浣风串缁蹭粙鎳熼娑欏弿鐟滄柨顕ｉ崼鏇炵厸濠电姴鍊烽崙鐣岀磽娴ｇ瓔鍤欓梺甯秮瀵偄顓奸崨顖涙畷闂佸憡娲︽禍鐐靛妤ｅ啯鈷掑〒姘搐婢ь喗绻涚仦鍌氣偓婵嗙暦濞嗘挻鏅柛鏇ㄥ幘閿涙粓妫呴銏℃悙濡ょ姵鎮傝棟闁哄被鍎辩痪?;
        }

        return $"缂傚倸鍊风粈渚€藝閹剁瓔鏁嬬憸搴ㄥ箞閵娾晛鐓涢柛娑卞幗椤ユ繈姊洪懡銈呮灁濠⒀勵殜瀹? {direction}";
    }

    public string AnalyzeAndApplyAIResult(string aiFullResponse, RoleState state, SceneItemLibraryData itemLibrary, out string feedback)
    {
        feedback = null;
        const string pattern = @"<CMD>(.*?)</CMD>";
        Match match = Regex.Match(aiFullResponse ?? string.Empty, pattern, RegexOptions.Singleline);

        if (!match.Success)
            return aiFullResponse;

        string jsonCmd = match.Groups[1].Value;
        try
        {
            if (!TryValidateCommandJson(jsonCmd, out string failReason))
            {
                feedback = $"闂佽楠稿﹢閬嶁€﹂崼婵愬殨闁告挷璁查崑鎾愁潩椤掍緡妫ょ紓浣介哺鐢帡鍩為幋鐘亾閿濆骸浜滄い顐熲偓鏂ユ斀闁绘劕妯婂Σ瑙勭箾閸欏鐭嬮柟渚垮姂楠炴牗鎷呴崫銉ф殸濠电偠鎻徊鍧楀箠閹剧粯鍎婇柣鐔稿櫞瑜版帗鍋愭い鏂跨毞閸嬫捁顦存俊鍙夊姍閹啫顫忛張绯筶Reason}";
            }
            else if (!ApplyCommandToState(jsonCmd, state, itemLibrary, out string applyFailReason))
            {
                feedback = applyFailReason;
            }
        }
        catch (Exception exception)
        {
            feedback = $"闂傚倷鑳剁划顖炩€﹂崼銉ユ槬闁哄稁鍘奸悞鍨亜閹达絾纭堕柛鏂跨Ч閹宕归銈庢殹缂備礁鍊圭敮鈩冧繆閸洖鏄ラ柟绋块濞搭喚鈧鍠楅幃鍌炵嵁閸ヮ剦鏁勯柛娆嶅劜濠⑩偓闂備浇宕垫慨鐢稿礉閹达箑纾块柤濮愬€栭～鏇㈡煟閵婏富鏆玿ception.Message}";
        }

        return aiFullResponse.Replace(match.Value, string.Empty).Trim();
    }

    public static bool TryValidateCommandJson(string jsonStr, out string failReason)
    {
        failReason = null;
        if (string.IsNullOrWhiteSpace(jsonStr))
        {
            failReason = "缂傚倸鍊风粈渚€鎯夋總绋跨？闁靛牆娲犻崑鎾诲垂椤愩値鏆＄紓?;
            return false;
        }

        JsonData data;
        try
        {
            data = JsonMapper.ToObject(jsonStr);
        }
        catch (Exception exception)
        {
            failReason = $"JSON 闂備浇宕甸崰鎰版偡鏉堚晛绶ゅΔ锝呭暞閸婄敻鏌ら幁鎺戝姎缂佸墎鍋ゅ鍫曞醇椤愵澀鍑介悶? {exception.Message}";
            return false;
        }

        if (data == null || !data.IsObject)
        {
            failReason = "婵犵绱曢崑鎴﹀磹濞戞ǚ鏋栨繛鎴欏灪閸庢垿鏌熷畡鎵伇闁汇倐鍋撻梻浣告啞缁嬫帡鎮鹃鍫濈劦妞ゆ巻鍋撶紒缁樏?JSON 闂備浇顕уù鐑藉极閹间降鈧焦绻濋崶銊ョ樁?;
            return false;
        }

        foreach (string key in data.Keys)
        {
            if (!AllowedCommandKeys.Contains(key))
            {
                failReason = $"闂備浇顕х€涒晝绮欓幒妤佹櫔婵犵數濮崑鎾淬亜韫囨挾澧曢悗姘槹閵囧嫯绠涢幘鏉戞闂佽娼欏锟犲蓟閳╁啫绶炲┑鐘插€瑰▓顓㈡⒑閻戔晛顫掗柛鎰剁稻閻? {key}";
                return false;
            }
        }

        if (data.Keys.Contains("get_item"))
        {
            JsonData itemData = data["get_item"];
            if (itemData == null || !itemData.IsObject)
            {
                failReason = "get_item 闂傚倸顭崑鍕洪妶澶婄疇婵せ鍋撳┑锛勵棎缁犳稑鈽夊Ο鍝勬尋婵＄偑鍊栭悧妤呭礄瑜版帒鍚归柣妯肩帛閸?;
                return false;
            }

            foreach (string itemKey in itemData.Keys)
            {
                if (!AllowedGetItemKeys.Contains(itemKey))
                {
                    failReason = $"get_item 闂備浇顕х€涒晝绮欓幒妤佹櫔婵犵數濮崑鎾淬亜韫囨挾澧曢悗姘槹閵囧嫯绠涢幘鏉戞闂佽娼欏锟犲蓟閳╁啫绶炲┑鐘插€瑰▓顓㈡⒑閻戔晛顫掗柛鎰剁稻閻? {itemKey}";
                    return false;
                }
            }

            if (!itemData.Keys.Contains("template_id") || string.IsNullOrWhiteSpace((string)itemData["template_id"]))
            {
                failReason = "get_item 缂傚倸鍊搁崐鎼佸磹閹间礁绠规い鎰堕檮閸?template_id";
                return false;
            }

            if (itemData.Keys.Contains("count") && TryReadInt(itemData["count"], out int count) && count <= 0)
            {
                failReason = "get_item.count 闂傚倸顭崑鍕洪妶澶婄疇婵せ鍋撳┑锛勵棎缁犳盯寮捄銊х嵁闁荤喐绮岄惉濂告嚍?0";
                return false;
            }

            if (itemData.Keys.Contains("runtime"))
            {
                if (!ValidateRuntimeBlock(itemData["runtime"], out failReason))
                    return false;
            }
            else if (itemData.Keys.Contains("stat_modifiers"))
            {
                if (!ValidateStatModifiers(itemData["stat_modifiers"], out failReason))
                    return false;
            }
        }

        if (data.Keys.Contains("lose_item"))
        {
            JsonData loseData = data["lose_item"];
            if (loseData == null || !loseData.IsObject)
            {
                failReason = "lose_item 闂傚倸顭崑鍕洪妶澶婄疇婵せ鍋撳┑锛勵棎缁犳稑鈽夊Ο鍝勬尋婵＄偑鍊栭悧妤呭礄瑜版帒鍚归柣妯肩帛閸?;
                return false;
            }

            foreach (string key in loseData.Keys)
            {
                if (!AllowedLoseItemKeys.Contains(key))
                {
                    failReason = $"lose_item 闂備浇顕х€涒晝绮欓幒妤佹櫔婵犵數濮崑鎾淬亜韫囨挾澧曢悗姘槹閵囧嫯绠涢幘鏉戞闂佽娼欏锟犲蓟閳╁啫绶炲┑鐘插€瑰▓顓㈡⒑閻戔晛顫掗柛鎰剁稻閻? {key}";
                    return false;
                }
            }

            bool hasInstance = loseData.Keys.Contains("instance_id") && !string.IsNullOrWhiteSpace((string)loseData["instance_id"]);
            bool hasTemplate = loseData.Keys.Contains("template_id") && !string.IsNullOrWhiteSpace((string)loseData["template_id"]);
            if (!hasInstance && !hasTemplate)
            {
                failReason = "lose_item 闂傚倷鑳堕崢褔宕查弻銉ョ柈闁秆勵殕閸庡秵銇勯弽顐沪妞ゃ儱妫濋弻宥堫檨闁告挻鐩獮?instance_id 闂?template_id";
                return false;
            }

            if (loseData.Keys.Contains("count") && TryReadInt(loseData["count"], out int count) && count <= 0)
            {
                failReason = "lose_item.count 闂傚倸顭崑鍕洪妶澶婄疇婵せ鍋撳┑锛勵棎缁犳盯寮捄銊х嵁闁荤喐绮岄惉濂告嚍?0";
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
            failReason = "get_item.runtime 闂傚倸顭崑鍕洪妶澶婄疇婵せ鍋撳┑锛勵棎缁犳稑鈽夊Ο鍝勬尋婵＄偑鍊栭悧妤呭礄瑜版帒鍚归柣妯肩帛閸?;
            return false;
        }

        foreach (string runtimeKey in runtimeData.Keys)
        {
            if (!AllowedRuntimeKeys.Contains(runtimeKey))
            {
                failReason = $"runtime 闂備浇顕х€涒晝绮欓幒妤佹櫔婵犵數濮崑鎾淬亜韫囨挾澧曢悗姘槹閵囧嫯绠涢幘鏉戞闂佽娼欏锟犲蓟閳╁啫绶炲┑鐘插€瑰▓顓㈡⒑閻戔晛顫掗柛鎰剁稻閻? {runtimeKey}";
                return false;
            }
        }

        if (runtimeData.Keys.Contains("stat_modifiers") && !ValidateStatModifiers(runtimeData["stat_modifiers"], out failReason))
            return false;

        return true;
    }

    private static bool ValidateStatModifiers(JsonData modifierData, out string failReason)
    {
        failReason = null;
        if (modifierData == null || !modifierData.IsArray)
        {
            failReason = "stat_modifiers 闂傚倸顭崑鍕洪妶澶婄疇婵せ鍋撳┑锛勵棎缁犳稑鈽夊Ο鍝勬尋婵＄偑鍊栭悧妤冪矙閹烘垟鏋旈柛鎾茶兌绾?;
            return false;
        }

        for (int i = 0; i < modifierData.Count; i++)
        {
            var modifier = modifierData[i];
            if (modifier == null || !modifier.IsObject)
            {
                failReason = "stat_modifiers 婵犵數鍋為崹鍫曞箹閳哄懎鍌ㄩ柟顖嗏偓閺嬫棃鏌熺€涙娓ら柟鐑樻礈閻も偓濠电偞鍨堕悧鎴﹀磻閹炬剚鍚嬪璺侯儑閸旀悂姊洪懞銉冾亪藝鏉堚晝涓嶉柟瀛樼箥濞撳鏌曢崼婵囧窛閻㈩垱绋掓穱濠囧箵閹烘挻鍠愰梺閫炲苯澧伴柡浣规倐閵嗕焦绻濋崶銊ョ樁?;
                return false;
            }

            string statKey = modifier.Keys.Contains("stat") ? (string)modifier["stat"] : null;
            if (string.IsNullOrWhiteSpace(statKey) || !AllowedStatKeys.Contains(statKey))
            {
                failReason = $"婵犵數鍋為崹鍫曞箰閸濄儳鐭撻柣銏㈩焾閺嬩焦銇勯弴妤€浜惧Δ鐘靛仦閸旀牠骞忛崨瀛樺€绘俊顖氬悑濞?stat modifier: {statKey}";
                return false;
            }

            if (!modifier.Keys.Contains("value") || !TryReadInt(modifier["value"], out _))
            {
                failReason = "stat modifier 缂傚倸鍊搁崐鎼佸磹閹间礁绠规い鎰堕檮閸庡秵銇勯弽顐粶閻庢艾顦…璺ㄦ崉娓氼垰鍓卞┑鐐叉噹缁绘﹢寮?value";
                return false;
            }
        }

        return true;
    }

    private bool ApplyCommandToState(string jsonStr, RoleState state, SceneItemLibraryData itemLibrary, out string failReason)
    {
        failReason = null;
        JsonData data = JsonMapper.ToObject(jsonStr);
        InventoryStateUtility.EnsureCompatibility(state, itemLibrary);

        if (data.Keys.Contains("hp"))
        {
            int val = ReadInt(data["hp"]);
            state.attributes.currentHealth += val;
        }

        if (data.Keys.Contains("mp"))
        {
            int val = ReadInt(data["mp"]);
            state.attributes.currentMana += val;
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
            string templateId = (string)itemData["template_id"];
            if (itemLibrary == null || !itemLibrary.IsTemplateAllowed(templateId))
            {
                failReason = $"闂佽楠稿﹢閬嶁€﹂崼婵愬殨闁告挷璁查崑鎾愁潩椤掍緡妫ょ紓浣介哺鐢帡鍩為幋锕€骞㈡俊顖濆亹濮ｏ綁姊绘担鍛婃儓闁瑰嘲顑夊畷褰掓惞椤愶絾鐝烽梺绉嗗嫷娈斿ù鑲╁Т闇夐柨婵嗘噺閹插憡鎱ㄥΟ绋垮闁宠棄顦靛顒€鈻庨幆褍澹堟俊鐐€х粻鎴︹€﹂悜钘夌畾闁告劦鍠栫粈瀣亜閹捐泛浜归柟绋挎嚇濮? {templateId}";
                InventoryStateUtility.NormalizeResourceCaps(state, InventoryStateUtility.CalculateDerivedAttributes(state));
                return false;
            }

            var runtimeData = ParseRuntimeData(itemData);
            int itemCount = itemData.Keys.Contains("count") ? Mathf.Max(1, ReadInt(itemData["count"])) : 1;
            var template = itemLibrary.GetTemplate(templateId);
            if (template == null)
            {
                failReason = $"闂傚倷绀侀幖顐﹀疮閻楀牊鍙忛柣銏犵仛閸忔粓鏌涢锝嗙闁圭懓鐖奸弻鏇熺箾閸喖濮㈢紓浣割槸閵堟悂寮诲☉銏犵鐎规洖娉﹂姀锛勭瘈鐟滃宕戦幘缁樷拺闁告繂瀚刊濂告煕閵娿劍鐝柡鍛埣瀹曠螖閳ь剚瀵奸悩瑁佸綊鏁愰崨顔藉枑濠殿喗菧閸斿矂鍩ユ径鎰闁告劑鍔岀粻鐑樼節閵忥綆娼愰柨鏇樺灩閻?template_id={templateId}";
                return false;
            }

            if (template.stackable)
            {
                var entry = new ItemInventoryEntry { templateId = templateId, count = itemCount, runtimeData = runtimeData };
                if (!InventoryStateUtility.TryAddInventoryEntry(state, entry, itemLibrary, out failReason))
                    return false;
            }
            else
            {
                for (int i = 0; i < itemCount; i++)
                {
                    var entry = new ItemInventoryEntry
                    {
                        templateId = templateId,
                        count = 1,
                        runtimeData = CloneRuntimeData(runtimeData, i == 0 ? runtimeData.instanceId : null)
                    };

                    if (!InventoryStateUtility.TryAddInventoryEntry(state, entry, itemLibrary, out failReason))
                        return false;
                }
            }

            NotifyItemAcquired(template, runtimeData, itemCount);
        }

        if (data.Keys.Contains("lose_item"))
        {
            JsonData loseData = data["lose_item"];
            string instanceId = loseData.Keys.Contains("instance_id") ? (string)loseData["instance_id"] : null;
            string templateId = loseData.Keys.Contains("template_id") ? (string)loseData["template_id"] : null;
            int count = loseData.Keys.Contains("count") ? Mathf.Max(1, ReadInt(loseData["count"])) : 1;
            if (!InventoryStateUtility.TryRemoveItem(state, instanceId, templateId, count, out failReason))
                return false;
        }

        InventoryStateUtility.NormalizeResourceCaps(state, InventoryStateUtility.CalculateDerivedAttributes(state));
        return true;
    }

    private static void NotifyItemAcquired(ItemTemplateData template, ItemRuntimeData runtimeData, int itemCount)
    {
        string itemName = ResolveItemDisplayName(template, runtimeData);
        if (string.IsNullOrWhiteSpace(itemName))
            return;

        string countSuffix = itemCount > 1 ? $" x{itemCount}" : string.Empty;
        EventCenter.Instance.Broadcast("OnCenterToast", $"闂傚倷绀侀崥瀣磿閹惰棄搴婇柤纰卞墯椤愪粙姊洪鈧粔鐢告倿婵傚憡鐓欓柟顖嗗啯姣愰悗鐢靛濡啴寮婚妸銉㈡婵炲棙锚婵湈temName}{countSuffix}");
    }

    private static string ResolveItemDisplayName(ItemTemplateData template, ItemRuntimeData runtimeData)
    {
        if (!string.IsNullOrWhiteSpace(runtimeData?.name))
            return runtimeData.name.Trim();

        if (!string.IsNullOrWhiteSpace(template?.displayName))
            return template.displayName.Trim();

        if (!string.IsNullOrWhiteSpace(template?.templateId))
            return template.templateId.Trim();

        return string.Empty;
    }

    private ItemRuntimeData ParseRuntimeData(JsonData itemData)
    {
        JsonData runtimeData = itemData.Keys.Contains("runtime") ? itemData["runtime"] : itemData;
        var parsed = new ItemRuntimeData
        {
            instanceId = Guid.NewGuid().ToString("N"),
            name = runtimeData.Keys.Contains("name") ? (string)runtimeData["name"] : string.Empty,
            description = runtimeData.Keys.Contains("desc") ? (string)runtimeData["desc"] : string.Empty,
            rarity = runtimeData.Keys.Contains("rarity") ? (string)runtimeData["rarity"] : "闂傚倷绀侀幖顐﹀箯鐎ｎ喖闂柨婵嗩槸閻?,
            effectText = runtimeData.Keys.Contains("effect_text") ? (string)runtimeData["effect_text"] : string.Empty,
            statModifiers = new List<ItemStatModifier>()
        };

        if (runtimeData.Keys.Contains("stat_modifiers") && runtimeData["stat_modifiers"].IsArray)
        {
            for (int i = 0; i < runtimeData["stat_modifiers"].Count; i++)
            {
                var modifier = runtimeData["stat_modifiers"][i];
                if (modifier == null || !modifier.IsObject)
                    continue;

                parsed.statModifiers.Add(new ItemStatModifier
                {
                    statKey = modifier.Keys.Contains("stat") ? (string)modifier["stat"] : string.Empty,
                    value = modifier.Keys.Contains("value") ? ReadInt(modifier["value"]) : 0,
                });
            }
        }

        parsed.EnsureDefaults();
        return parsed;
    }

    private static ItemRuntimeData CloneRuntimeData(ItemRuntimeData source, string keepInstanceId = null)
    {
        var clone = new ItemRuntimeData
        {
            instanceId = string.IsNullOrWhiteSpace(keepInstanceId) ? Guid.NewGuid().ToString("N") : keepInstanceId,
            name = source?.name,
            description = source?.description,
            rarity = source?.rarity,
            effectText = source?.effectText,
            statModifiers = new List<ItemStatModifier>()
        };

        if (source?.statModifiers != null)
        {
            foreach (var modifier in source.statModifiers)
            {
                clone.statModifiers.Add(new ItemStatModifier
                {
                    statKey = modifier?.statKey,
                    value = modifier?.value ?? 0,
                });
            }
        }

        clone.EnsureDefaults();
        return clone;
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
                return int.TryParse((string)data, out value);
        }
        catch
        {
            return false;
        }

        return false;
    }

    private void AddExperience(RoleState state, int amount)
    {
        if (amount <= 0)
            return;

        state.attributes.currentExp += amount;
        CheckLevelUp(state);
    }

    private void CheckLevelUp(RoleState state)
    {
        if (state.attributes.expToNextLevel <= 0)
            state.attributes.expToNextLevel = 100;

        while (state.attributes.currentExp >= state.attributes.expToNextLevel)
        {
            state.attributes.currentExp -= state.attributes.expToNextLevel;
            state.attributes.level++;
            state.attributes.expToNextLevel = Mathf.RoundToInt(state.attributes.expToNextLevel * 1.5f);
            state.attributes.maxHealth += 20;
            state.attributes.maxMana += 10;
        }
    }

    private bool TryAddLocalItem(RoleState state, string templateId, string fallbackName, string description, string effectText)
    {
        var library = GameLoop.Instance != null ? GameLoop.Instance.CurrentItemLibrary : null;
        var template = library != null ? library.GetTemplate(templateId) : null;
        string runtimeName = template != null && !string.IsNullOrWhiteSpace(template.displayName) ? template.displayName : fallbackName;
        string runtimeDesc = template != null && !string.IsNullOrWhiteSpace(template.templateDescription) ? template.templateDescription : description;
        var entry = InventoryStateUtility.CreateEntryFromTemplate(templateId, runtimeName, runtimeDesc, "闂傚倷绀侀幖顐﹀箯鐎ｎ喖闂柨婵嗩槸閻?, effectText, null);
        return InventoryStateUtility.TryAddInventoryEntry(state, entry, library, out _);
    }

    private bool ConsumeInventoryItem(RoleState state, string itemName)
    {
        var entry = InventoryStateUtility.FindInventoryEntryByName(state, itemName, out _);
        if (entry?.runtimeData == null)
            return false;

        return InventoryStateUtility.TryRemoveItem(state, entry.runtimeData.instanceId, null, 1, out _);
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

    private static bool IsInZhaoYao(EnvironmentState env)
    {
        return env != null &&
               ((!string.IsNullOrWhiteSpace(env.locationId) && env.locationId.Contains("zhaoyao", StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(env.locationName) && env.locationName.Contains("闂傚倷绀佺紞濠囧绩鏉堚晝鐭欓柟鐑橆殔缁犵喖鏌ｉ幇顒€鎮佹繛?, StringComparison.Ordinal)));
    }
}

public static class AIResponseConsistencyChecker
{
    private static readonly string[] ItemActionVerbs =
    {
        "婵犵數鍋犻幓顏嗙礊閳ь剚绻涙径瀣鐎?, "闂傚倷绀侀幖顐︽偋閸℃瑧鐭撶€规洖娲ㄧ粻?, "闂傚倷绀侀幗婊勬叏閻㈠灚宕查柍褜鍓涚槐?, "婵犲痉鏉库偓鏍€冭箛娑樼獥闁哄洨濮风粻?, "闂傚倷绀侀幉鈥愁潩閵娾晛纾绘俊顖濆亹缁?, "闂傚倷绀侀幉锛勫垝瀹ュ鍨傛繝闈涚墢缁?, "闂傚倷绀侀幉锟犳嚌妤ｅ啫鐤柍鍝勫暟缁?, "闂傚倷绀侀幉鈥愁潩閵娾晛纾婚柣鎰嚋閼?,
        "闂傚倷鑳剁划顖炲礉閺囩儑鑰块梺顒€绉撮梻?, "闂傚倷鑳堕幊鎾朵焊閸涱垳纾芥慨妯诲閸?, "婵犵數鍋犻幓顏嗗緤濞差亜鐤炬繛鍡樺灩缁?, "闂傚倷鑳堕幊鎾活敋椤撶姵宕叉繝闈涚墢缁?, "缂傚倸鍊风粈渚€鎳熼鐐参︽俊顖濆亹缁?, "缂傚倸鍊风欢锟犲垂閻㈠壊鏁婂┑鐘插缁?, "闂備浇宕甸崰宥囩矆娓氣偓楠炲﹥鎯旈妸?,
        "闂傚倷绀佺紞濠囧绩闁秴钃熼柕濞炬櫅閸?, "闂傚倷娴囬～澶愬磿閻撳宫娑㈠礋椤栨艾鎯?, "闂傚倷娴囬～澶愬箖閸洖纾块弶鍫涘妽閸?, "闂傚倷娴囬～澶愬箖閸洖纾块柟缁㈠枛缁?, "闂傚倷绶氶埀顒佺〒娑撹尙绱掓潏銊︾妤?, "闂傚倷绀佺紞濠囧疾閹绘崡褰掑炊椤掆偓缁?, "缂傚倸鍊峰鎺旀閿熺姴鍌ㄥ┑鍌滎焾閸?, "闂傚倷绀侀幉锟犳偡閿曞倹鍋嬮柡鍥ュ灩閸?,
        "闂備浇顕х换鎺楀磻閻愯娲冀椤愶綆娼?, "闂傚倷鑳堕、濠偽涢崟顖涙櫇闁靛牆娲﹂～?
    };

    private static readonly Regex UnknownItemUsageRegex = new Regex(
        @"(?:婵犵數鍋犻幓顏嗙礊閳ь剚绻涙径瀣鐎殿噮鍋婅棟闁靛繈鍊栭悡娑㈡煕椤愶絿绠樼亸蹇曠磽娴ｈ娈旈悽顖氬濮婃椽宕妷銉愶絿绱掗悜鈺佷壕缂傚倷鐒﹂〃鍛暜閸曨兛绻嗛柕鍫濇－濞堛垽鏌涢悩铏缂侇噮鍙冨畷銏＄節閸ャ劎鍘卞┑顔矫畷顒佺閹烘梻纾兼俊銈傚亾閻㈩垰瀚板娲川婵犲嫮鐓侀梺鎼炲妽缁诲嫮鍒掗銏犲偍濠电姴娲﹂悡鏇熸叏濮楀棗澧柡鍡樻礈缁辨帒螖閳ь剛鏁崟顖涒拺闁告稑锕ラ惃鎴︽煕鐎ｎ剙浠滈柨鏇樺灲瀹曟娊鎮㈤崗鑲╁幗闂佸綊鍋婇崜姘辨嫻閿熺姵鐓曢柨婵嗗缁蹭粙姊绘担鐑樺殌閻忓繐鎳愮槐鎾愁潩閹典礁浜鹃柛顭戝亜閳荤厧鈹戦悙鑸靛涧缂佸弶妞藉鎻掆枎閹寸姷鐣堕梺绋匡工閻楁捇寮婚悢琛″亾闂堟稒鎲哥紒鈾€鍋撶紓鍌欑劍椤ㄥ懘鍩€椤掆偓閻楀繒妲愰幒鏂炬勃濠殿喗鍔掔划褏绱撴担璇℃當闁逞屽墮閻楀繒妲愰幒妤婃晬婵犲灚鍔曢弫鐣岀磽娴ｈ娈旈柍褜鍓欓悧鎾诲箖闁垮濯撮柛娑橈工濞咃絽顪冮妶蹇擃洭闁瑰眰鍨藉楦裤亹閹烘挻鏆犻梺鍝勮閸旀垵鐣烽悽鍛婂仱闁告稑鐡ㄩ悡娆撴煙椤栨粌顣兼い銉ヮ樀閺屾盯濡堕崶銊﹀剺闂傚倷娴囬～澶愬箖閸洖纾块弶鍫涘妽閸欏繘鏌ㄩ弴鐘叉灈闁哄瞼鍠栭獮鏍倷閹绘帒袚闂備胶顢婇鏍ь潖婵犳碍鈷戦梺顐ｇ〒娑撹尙绱掓潏銊︾妤犵偞鎹囪棢闁稿瞼鍋為悡娆撴⒒閳ь剟骞囬鐘仒闂備胶顢婇鏍ь潖閼姐倗纾藉ù锝夘棑鐠愪即鏌涘Ο鎭掑仮鐎规洜鏁婚幃娲川鐎涙鍘卞┑鐐叉閸旀鏅堕鍫熺厱闁靛牆娲﹂幆宥夋⒑鐠囪尙绠抽柛瀣仜鐓ら柡宥庡亝椤洟鏌熼崣澶婃诞闁哄瞼鍠栭幐濠冨緞濞戞ü绱ｇ紓?(?:婵犵數鍋涢悺銊у垝瀹€鍕瀬婵°倕鎳忛崑锝囨喐韫囨稑鐓橀柛宀€鍋為悡鏇㈡煙鐎电啸婵絿鍠栧缁樼瑹閸パ冨濠殿喖锕ら…宄邦嚕閸洖鐓涢柛灞诲€愰崑鎾诲礃椤旂晫鍘甸梻鍌氱墛娓氭宕曟惔銊︾厾闁割煈鍋勯悘鎾煙椤栨艾鏆ｇ€规洘绮撳鎾閳ヨ櫕鐣煎┑鐐差嚟閸樠囨偤閵娾晜鏅柣鏂垮悑閻撶喖鏌曟繛鍨姎闁愁亜缍婂铏规嫚閳ュ啿骞嬮梺绋款儐閹告悂鍩ユ径鎰闁告劑鍔岀粻璇测攽閻愯尙澧涢柛鐔奉儔濮婃椽鎮℃惔鈩冩瘣闂佸吋妞块崹鎶藉焵椤掆偓閻忔岸鏁冮姀銈呰摕閻忕偟鏅弳鍡涙煃瑜滈崜娆撳煝閹捐绠熼柟鐑橆殕閸嬬姵绻涢幋鐑嗙劸閻庢氨澧楃换娑欏緞濡搫绠虹紓浣割儏椤︾敻銆佸鈧幃鈺佺暦閸ャ劍顔弢婵犵數鍋為崹鍫曞箰閹绢喖纾婚柟鍓х帛閻撶喖鏌ㄥ☉妯虹彅闁哄倸绨卞Σ鍫ユ煙缂併垹鐏犲ù婊堢畺濮婅櫣娑甸崨顔俱€愰柧缁樺笚缁绘盯骞嬮悙鏉戠殤闂佺顑嗛幑鍥蓟濞戞鏃堝礃閿濆棙鏆呮繝鐢靛仦閸ㄥ爼骞愰幘顔肩；闁瑰墽绮悡娑氣偓骞垮劚濞层劑寮冲顓犵闁瑰鍋炵亸銊╂煕鐎ｎ偅灏甸柍褜鍓氶鏍窗濮樿泛鍨傛い鎺嗗亾闁宠棄顦甸獮姗€顢涘顐㈩棜闂傚倷绀侀幖顐︻敄閸℃稒鍋╂繝闈涙濡插牓鏌熺紒銏犵仩濞存粓绠栧娲川婵犲啫鏆楀┑鈩冨絻閸燁偊鍩ユ径鎰妞ゆ牗鐭竟鏇炩攽閻愬樊鍤熷┑顔炬焿椤も偓婵犵數鍋為崹鍫曞箰閹绢喖纾婚柟鎹愮М瑜版帗鍋愬ù锝呭暙楠炪垹鈹戦悙鏉戠仸闁瑰憡鎸冲畷鎴﹀箻缂佹鍘遍悷婊冮叄閸╂稑顓奸崨顏咁潔闂佸湱铏庨崹鐗堢閸撗呯＜闁告挆鍐炬毉闂佸啿鍢茬粔鎾煡婢舵劕绠绘い鏍ㄧ煯婢规洟姊绘担鍛婃儓闁兼椿鍨遍弲鍫曨敂閸啿鎷哄銈嗘尪閸斿海绮欐繝姘厽闁圭儤姊诲В鐔兼⒒閸屾瑧璐伴柛娆忕箳瀵板﹪鎮欓璺ㄧ畾缂備焦妫冩禍璺侯嚕閸洖鐓涢柛灞剧矋閸ｎ厽绻濈喊澶岀？妞ゅ浚鍣ｉ弻鈩冨緞閸℃ɑ鐝曢梺鑽ゅ暀閸パ咁啈闂佹椿鈧澘娲ょ痪褔鏌嶉崫鍕偓濠氬箠閸愵亖鍋撶憴鍕碍闁芥ǚ鏅犻弻鈩冨緞閸℃ɑ鐝曢梺鑽ゅ櫐缁犳捇骞冭婢规洟鏁撻悩鑼獓闂佸啿鎼崐濠氬箠閸愵亞纾奸弶鍫氭暕閺冨牊鈷掗柛灞剧閸ｅ湱绱掔紒姗嗘疁婵☆偄鎳橀崺锟犲川椤旂厧澹勯梻浣告啞閹歌煤閻旈鏆︽慨姗嗗墻濞尖晜銇勯幒鎴濃偓鑽ょ不閹烘鈷戠憸鐗堝笚濞懷勩亜閹存繃鎼愰柍缁樻崌楠炲骞掑Δ浣哄幈闂佸湱鍎ら幐楣冦€傞弻銉﹀€甸柨婵嗗€告禍楣冩⒑閼姐倕顫掗柛銉ㄥ煐椤ユ牕鈹戦悙鑼闁哥喎顑夊娲偂鎼达絼绮靛┑鐘灪閿曘垽鐛崘銊庢棃宕ㄩ鐓庡闂備礁鎲￠幐璇裁洪悢鑲╁祦闁逞屽墴閺屽秷顧侀柛鎾跺枛瀵崵浠﹂悾灞炬畷闂侀€炲苯澧柍缁樻崌楠炲骞掑Δ浣哄幍闂佸憡绻傜€氼剟銆冨▎鎾寸厾闁割煈鍋勯悘鎾煙椤栨艾鏆ｇ€规洘绮撳鎾閻樻鍚嬮梺璇插嚱缂嶅棝宕伴弽褉鏋嶉柨鐔哄У閻??(?<item>[\u4e00-\u9fa5A-Za-z0-9]{1,12}(?:闂傚倷娴囬崑鎰版偤閺冨牆鍨傞梺顒€绉寸粻鏉款熆瑜嶉敃顏堝蓟閵堝洨椹抽悗锝庝簽娴狀參姊洪崫鍕棞闁芥ǚ鏅犲娲嚃閳哄啯鎲橀梺鎼炲姂娴滃爼鐛笟鈧幃鐢稿川椤撴稒顫嶉梺瑙勫劤婢у酣寮冲鑸碘拺闁告稑锕ョ亸浼存煕閻斿鍎旈柡灞剧洴楠炴帡宕卞鎯ь棜|闂傚倷鐒﹂幃鍫曞磿瀹曞洨鐜婚煫鍥ㄧ⊕閻撴盯鎮楅敐搴′簽闂婎偄鐗嗛埞鎴︻敊濞嗘儳娈愬┑鈩冪ゴ閺呯娀寮婚妶澶婄闁圭儤绻傞崜閬嶆煟閵忊晛鐏￠柟顔荤矙濮婅櫣绱掑Ο绗衡偓鎺戔攽椤曗偓閺€閬嶅疾閸洘鍋╅柡宥庡幗閻撶娀鏌℃径瀣仴闁逞屽厸缁瑥鐣烽鐐插偍濠电姴鍋嗛悢鍡涙煠缁嬭法浠涚紓宥嗗灴閺岋綁濮€閵忕姷鏌ч梻鍌欑劍濡炲潡宕ｆ惔銊ョ煑闁逞屽墯閵囧嫰鏁傜紒銏☆唹闂備浇宕甸崑鐐电矙閹达附鍎楀〒姘ｅ亾闁轰焦鎹囬妴鍐磼閻愯尙楠囬梺缁樺姍椤ゅ倿宕靛▎寰柨螖閸愨晙绶甸梻鍌欐祰椤曆兠瑰璺虹；闁告侗鍠楅崣蹇涙倵闂堟稓肖缂佽鲸甯掕灃濠电姴鍟伴ˇ浼存⒒娴ｅ憡鍟為柣妤婂墮椤繑绻濆顓犲幈濠德板€曢崯鐘诲绩閵堝鈷戝ù鍏肩懅绾捐法鎮☉銏♀拺闁煎湱澧楅ˉ澶嬨亜閿濆骸鐏﹂柡灞诲€濋獮鎾诲箳濠靛洦顔夐梺璇插椤旀牠宕板Δ鍛畺濞寸姴顑嗛悡鐔搞亜閹捐泛鏋旈柕鍡樻濮?)",
        RegexOptions.Compiled);

    public sealed class TurnItemSnapshot
    {
        public HashSet<string> itemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ConsistencyReport
    {
        public string visibleText;
        public string feedback;
        public bool hasViolation;
    }

    public static TurnItemSnapshot CaptureSnapshot(RoleState state, SceneItemLibraryData itemLibrary)
    {
        var snapshot = new TurnItemSnapshot();
        AddStateItemNames(snapshot.itemNames, state, itemLibrary);
        return snapshot;
    }

    public static ConsistencyReport FilterVisibleText(
        string visibleText,
        TurnItemSnapshot turnStartSnapshot,
        RoleState stateAfterResolution,
        SceneItemLibraryData itemLibrary)
    {
        string sourceText = visibleText ?? string.Empty;
        var report = new ConsistencyReport
        {
            visibleText = sourceText,
            feedback = null,
            hasViolation = false
        };

        if (string.IsNullOrWhiteSpace(sourceText))
            return report;

        var allowedOwnedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (turnStartSnapshot?.itemNames != null)
        {
            foreach (string name in turnStartSnapshot.itemNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    allowedOwnedNames.Add(name.Trim());
            }
        }

        AddStateItemNames(allowedOwnedNames, stateAfterResolution, itemLibrary);

        var knownLibraryNames = BuildKnownLibraryNames(itemLibrary);
        foreach (string ownedName in allowedOwnedNames)
            knownLibraryNames.Add(ownedName);

        var sentences = SplitSentences(sourceText);
        var keptSentences = new List<string>(sentences.Count);
        var offendingItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string sentence in sentences)
        {
            if (ShouldFilterSentence(sentence, allowedOwnedNames, knownLibraryNames, out string offendingItem))
            {
                report.hasViolation = true;
                if (!string.IsNullOrWhiteSpace(offendingItem))
                    offendingItems.Add(offendingItem.Trim());
                continue;
            }

            keptSentences.Add(sentence);
        }

        if (!report.hasViolation)
            return report;

        string filteredText = string.Concat(keptSentences).Trim();
        if (string.IsNullOrWhiteSpace(filteredText))
            filteredText = "婵犵數鍋犻幓顏嗗緤閹稿孩鍙忛悗闈涙啞閺嗘粓鏌涢妷锝呭闁崇粯妫冮幃妤呮晲鎼粹€愁潽闂佺顭徊浠嬪煡婢舵劕绫嶉柛銉畱閹牆鈹戦悙鑼闁搞劏鍋愮划璇测槈閵忊槅妫冨┑鐐村灦閻楁梻鍒掗幘缁樷拺闁圭娴烽埥澶愭煟椤撶偛鈧灝鐣烽幎钘夌濞达絽鎲￠ˉ婵嬫⒑鐟欏嫷鍟忛柛鐘冲浮閸┾偓妞ゆ帊绶￠悞浠嬫煙瀹勯偊鍎斿┑鈥崇埣瀹曞崬螖婵犲啯绶梻浣告惈椤︻垶鎮ч崱妤嬭€块柛妤冨剳閼板潡鏌嶉妷锕€澧繛闂村嵆閺屻劌鈹戦崱娆忊叡缂備胶濮烽崗姗€寮婚妶鍥ｅ亾閸︻厼校妞ゃ儱顦埞鎴︻敊鐟欏嫭鐝氶梺璇″枙缁瑦淇婂宀婃Ъ闂佷紮缍佹禍鍫曞蓟濞戙垺鏅查柛鎰╁妼椤绻濋埛鈧崘鈺佷淮婵?;

        report.visibleText = filteredText;
        report.feedback = offendingItems.Count > 0
            ? $"婵犵數鍋為崹鍫曞箰閹绢喖纾婚柟鍓х帛閻撶姷鈧懓瀚竟鍡涘箺閻樼粯鐓冪憸婊堝磿閹间礁绀嬫い鎾跺亹閸欑偛鈹戞幊閸婃鎱ㄩ幘顔藉剹濠㈣泛鏈～鏇㈡煛婢跺鍎ラ柛鐔锋嚇閺屻倝鎮℃惔鈽嗘殺缂備胶濮甸悧妤冩崲濠靛洨绠鹃柛顭戝枓閸嬫捇寮撮悩鎰佹綗闂佸啿鎼幊蹇涘磻閵娾晜鐓忓┑鐐茬仢閳ь剚顨婇幃宄扳攽鐎ｎ偄鈧灚绻涢幋鐑嗕痪妞ゅ繐瀚搁懓鎸庛亜韫囨挾澧涢柛瀣儔閺屽秵娼幍顔煎闂佽鍨伴崐鍧楀蓟閿濆惟闁靛鍎烘禒濂告偡濠婂嫭绶查柨鏇樺劤缁顓奸崶锝呬壕闁挎繂鎳庨。宕囩磼閳ь剚寰勭€ｎ剛顔曢梺鐟扮摠缁诲秴危閸濄儮鍋撳▓鍨灓闁稿繑锕㈠顐㈩吋閸滀焦妗ㄧ紓浣芥閺咁湼ing.Join("闂?, offendingItems.Take(3))}"
            : "婵犵數鍋為崹鍫曞箰閹绢喖纾婚柟鍓х帛閻撶姷鈧懓瀚竟鍡涘箺閻樼粯鐓冪憸婊堝磿閹间礁绀嬫い鎾跺亹閸欑偛鈹戞幊閸婃鎱ㄩ幘顔藉剹濠㈣泛鏈～鏇㈡煛婢跺鍎ラ柛鐔锋嚇閺屻倝鎮℃惔鈽嗘殺缂備胶濮甸悧妤冩崲濠靛洨绠鹃柛顭戝枓閸嬫捇寮撮悩鎰佹綗闂佸啿鎼幊蹇涘磻閵娾晜鐓忓┑鐐茬仢閳ь剚顨婇幃宄扳攽鐎ｎ偄鈧灚绻涢幋鐑嗕痪妞ゅ繐瀚搁懓鎸庛亜韫囨挾澧涢柛瀣儔閺屽秵娼幍顔煎闂佽鍨伴崐鍧楀蓟閿濆惟闁靛鍎烘禒濂告偡濠婂嫭绶查柨鏇樺劤缁顓奸崶锝呬壕闁挎繂鍟俊鐑芥煕?;
        return report;
    }

    private static bool ShouldFilterSentence(
        string sentence,
        HashSet<string> allowedOwnedNames,
        HashSet<string> knownLibraryNames,
        out string offendingItem)
    {
        offendingItem = null;
        if (string.IsNullOrWhiteSpace(sentence))
            return false;

        string trimmed = sentence.Trim();
        if (!ContainsAny(trimmed, ItemActionVerbs))
            return false;

        foreach (string candidate in knownLibraryNames.OrderByDescending(item => item.Length))
        {
            if (string.IsNullOrWhiteSpace(candidate) || !trimmed.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                continue;

            if (allowedOwnedNames.Contains(candidate))
                return false;

            offendingItem = candidate;
            return true;
        }

        Match unknownItemMatch = UnknownItemUsageRegex.Match(trimmed);
        if (!unknownItemMatch.Success)
            return false;

        string unknownItem = unknownItemMatch.Groups["item"].Value;
        if (string.IsNullOrWhiteSpace(unknownItem) || allowedOwnedNames.Contains(unknownItem))
            return false;

        offendingItem = unknownItem.Trim();
        return true;
    }

    private static void AddStateItemNames(HashSet<string> sink, RoleState state, SceneItemLibraryData itemLibrary)
    {
        if (sink == null || state?.equipment == null)
            return;

        state.equipment.EnsureCollections();

        foreach (var entry in state.equipment.inventoryEntries)
            AddEntryNames(sink, entry, itemLibrary);

        foreach (var entry in state.equipment.equipmentSlots.EnumerateEntries())
            AddEntryNames(sink, entry, itemLibrary);
    }

    private static void AddEntryNames(HashSet<string> sink, ItemInventoryEntry entry, SceneItemLibraryData itemLibrary)
    {
        if (sink == null || entry == null)
            return;

        entry.EnsureDefaults();

        if (!string.IsNullOrWhiteSpace(entry.runtimeData?.name))
            sink.Add(entry.runtimeData.name.Trim());

        var template = InventoryStateUtility.ResolveTemplate(itemLibrary, entry);
        if (!string.IsNullOrWhiteSpace(template?.displayName))
            sink.Add(template.displayName.Trim());
    }

    private static HashSet<string> BuildKnownLibraryNames(SceneItemLibraryData itemLibrary)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (itemLibrary?.items == null)
            return names;

        itemLibrary.EnsureIndex();
        foreach (var template in itemLibrary.items)
        {
            if (!string.IsNullOrWhiteSpace(template?.displayName))
                names.Add(template.displayName.Trim());
        }

        return names;
    }

    private static List<string> SplitSentences(string text)
    {
        var sentences = new List<string>();
        if (string.IsNullOrEmpty(text))
            return sentences;

        foreach (Match match in Regex.Matches(text, @"[^\r\n闂傚倷绶氬褍螞濡ゅ懎纾诲┑鐘插椤洟鏌熼悜姗嗘畷闁哄拋鍓熼弻锝夊即閻愭祴鍋撴繝姘卞祦??]+[\r\n闂傚倷绶氬褍螞濡ゅ懎纾诲┑鐘插椤洟鏌熼悜姗嗘畷闁哄拋鍓熼弻锝夊即閻愭祴鍋撴繝姘卞祦??]*"))
        {
            if (!string.IsNullOrWhiteSpace(match.Value))
                sentences.Add(match.Value);
        }

        return sentences;
    }

    private static bool ContainsAny(string source, IEnumerable<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(source) || candidates == null)
            return false;

        foreach (string candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                source.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
