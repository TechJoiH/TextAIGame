using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Logic.Intent
{
    /// <summary>
    /// 动作类型枚举 - 标准化动作指令
    /// </summary>
    public enum ActionType
    {
        Unknown,        // 未识别
        Attack,         // 攻击类
        Defend,         // 防御类
        Move,           // 移动类
        Explore,        // 探索类
        UseItem,        // 使用物品
        UseSkill,       // 使用技能
        Talk,           // 对话交互
        Rest,           // 休息恢复
        Observe,        // 观察查看
        Collect,        // 采集收集
        Craft,          // 制作合成
        Cultivate       // 修炼打坐
    }

    /// <summary>
    /// 意图识别结果
    /// </summary>
    [Serializable]
    public class IntentResult
    {
        public ActionType actionType;
        public string targetEntity;      // 目标实体（怪物名/物品名/地点等）
        public string actionVerb;        // 原始动词
        public float confidence;         // 置信度 0~1
        public Dictionary<string, string> parameters; // 额外参数

        public IntentResult()
        {
            actionType = ActionType.Unknown;
            confidence = 0f;
            parameters = new Dictionary<string, string>();
        }

        public override string ToString()
        {
            return $"[意图: {actionType}, 目标: {targetEntity ?? "无"}, 置信度: {confidence:P0}]";
        }
    }

    /// <summary>
    /// 意图识别规则定义
    /// </summary>
    [Serializable]
    public class IntentRule
    {
        public ActionType actionType;
        public string[] keywords;        // 关键词列表
        public string regexPattern;      // 正则模式（可选，用于复杂匹配）
        public int priority;             // 优先级（数值越大优先级越高）
    }

    /// <summary>
    /// 轻量级意图识别模块
    /// 基于正则规则与关键词匹配提取玩家输入的语义信息
    /// </summary>
    public class IntentRecognizer
    {
        private static IntentRecognizer _instance;
        public static IntentRecognizer Instance => _instance ??= new IntentRecognizer();

        private readonly List<IntentRule> _rules;
        
        // 实体提取正则（用于提取目标名词）
        private readonly Regex _targetExtractor;

        private IntentRecognizer()
        {
            _rules = InitializeRules();
            // 匹配"对/向/朝 XXX"或"XXX身上/那里"等目标模式
            _targetExtractor = new Regex(
                @"(?:对|向|朝|往|去|到|攻击|查看|观察|使用|拿起|捡起)\s*[""「『]?(\S{1,10})[""」』]?|(\S{2,6})(?:身上|那里|那边|处|旁)",
                RegexOptions.Compiled);
        }

        /// <summary>
        /// 初始化意图识别规则库
        /// </summary>
        private List<IntentRule> InitializeRules()
        {
            return new List<IntentRule>
            {
                // 攻击类 - 高优先级
                new IntentRule
                {
                    actionType = ActionType.Attack,
                    keywords = new[] { "攻击", "打", "杀", "斩", "劈", "刺", "击", "挥剑", "出手", "动手", "战斗", "进攻", "砍", "削", "轰", "施法攻击" },
                    regexPattern = @"(攻击|打|杀|斩|劈|刺|击|砍|战斗|进攻|出手|动手).{0,6}",
                    priority = 10
                },
                // 防御类
                new IntentRule
                {
                    actionType = ActionType.Defend,
                    keywords = new[] { "防御", "格挡", "闪避", "躲", "挡", "护住", "防守", "招架", "后退防御" },
                    regexPattern = @"(防御|格挡|闪避|躲开|挡住|防守|招架)",
                    priority = 9
                },
                // 移动类
                new IntentRule
                {
                    actionType = ActionType.Move,
                    keywords = new[] { "走", "跑", "前往", "离开", "进入", "返回", "移动", "去", "回", "逃", "撤退", "前进", "后退", "向东", "向西", "向南", "向北" },
                    regexPattern = @"(走|跑|前往|离开|进入|返回|去|回|逃|撤退|前进|后退|向[东西南北上下])",
                    priority = 7
                },
                // 探索类
                new IntentRule
                {
                    actionType = ActionType.Explore,
                    keywords = new[] { "探索", "搜索", "寻找", "找", "调查", "搜寻", "探查", "查找", "侦查" },
                    regexPattern = @"(探索|搜索|寻找|调查|搜寻|探查|查找)",
                    priority = 6
                },
                // 观察类
                new IntentRule
                {
                    actionType = ActionType.Observe,
                    keywords = new[] { "看", "观察", "查看", "注视", "打量", "审视", "端详", "望", "环顾", "检查" },
                    regexPattern = @"(看|观察|查看|注视|打量|审视|端详|环顾|检查).{0,6}",
                    priority = 5
                },
                // 使用物品
                new IntentRule
                {
                    actionType = ActionType.UseItem,
                    keywords = new[] { "使用", "吃", "喝", "服用", "拿出", "装备", "穿上", "戴上", "丢弃", "扔掉" },
                    regexPattern = @"(使用|吃|喝|服用|拿出|装备|穿|戴).{0,8}",
                    priority = 8
                },
                // 使用技能
                new IntentRule
                {
                    actionType = ActionType.UseSkill,
                    keywords = new[] { "施展", "释放", "发动", "使出", "运功", "催动", "施法", "念咒" },
                    regexPattern = @"(施展|释放|发动|使出|运功|催动|施法).{0,10}",
                    priority = 9
                },
                // 对话交互
                new IntentRule
                {
                    actionType = ActionType.Talk,
                    keywords = new[] { "说", "问", "交谈", "对话", "询问", "告诉", "回答", "喊", "叫", "呼唤" },
                    regexPattern = @"(说|问|交谈|对话|询问|告诉|回答|喊|叫).{0,10}",
                    priority = 4
                },
                // 休息恢复
                new IntentRule
                {
                    actionType = ActionType.Rest,
                    keywords = new[] { "休息", "睡觉", "打坐", "冥想", "疗伤", "恢复", "歇息", "静养" },
                    regexPattern = @"(休息|睡觉|打坐|冥想|疗伤|恢复|歇息|静养)",
                    priority = 5
                },
                // 采集收集
                new IntentRule
                {
                    actionType = ActionType.Collect,
                    keywords = new[] { "采集", "收集", "捡", "拾取", "摘", "挖", "取", "拿" },
                    regexPattern = @"(采集|收集|捡起|拾取|摘|挖|取).{0,6}",
                    priority = 6
                },
                // 修炼
                new IntentRule
                {
                    actionType = ActionType.Cultivate,
                    keywords = new[] { "修炼", "修行", "练功", "突破", "炼化", "吸收", "参悟" },
                    regexPattern = @"(修炼|修行|练功|突破|炼化|吸收|参悟)",
                    priority = 5
                }
            };
        }

        /// <summary>
        /// 识别玩家输入的意图
        /// </summary>
        public IntentResult Recognize(string playerInput)
        {
            if (string.IsNullOrWhiteSpace(playerInput))
                return new IntentResult();

            string input = playerInput.Trim().ToLower();
            IntentResult bestResult = new IntentResult();
            int bestScore = 0;

            foreach (var rule in _rules)
            {
                int score = CalculateMatchScore(input, rule);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestResult.actionType = rule.actionType;
                    bestResult.actionVerb = FindMatchedKeyword(input, rule.keywords);
                    bestResult.confidence = Mathf.Clamp01(score / 20f); // 归一化置信度
                }
            }

            // 提取目标实体
            bestResult.targetEntity = ExtractTarget(playerInput);

            // 提取额外参数
            ExtractParameters(playerInput, bestResult);

            Debug.Log($"<color=green>[意图识别] {playerInput} => {bestResult}</color>");
            return bestResult;
        }

        /// <summary>
        /// 计算规则匹配分数
        /// </summary>
        private int CalculateMatchScore(string input, IntentRule rule)
        {
            int score = 0;

            // 关键词匹配
            foreach (var keyword in rule.keywords)
            {
                if (input.Contains(keyword))
                {
                    score += 5 + rule.priority;
                    // 如果关键词出现在开头，额外加分
                    if (input.StartsWith(keyword))
                        score += 3;
                }
            }

            // 正则匹配额外加分
            if (!string.IsNullOrEmpty(rule.regexPattern))
            {
                if (Regex.IsMatch(input, rule.regexPattern))
                    score += 5;
            }

            return score;
        }

        /// <summary>
        /// 查找匹配的关键词
        /// </summary>
        private string FindMatchedKeyword(string input, string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (input.Contains(keyword))
                    return keyword;
            }
            return null;
        }

        /// <summary>
        /// 提取目标实体
        /// </summary>
        private string ExtractTarget(string input)
        {
            Match match = _targetExtractor.Match(input);
            if (match.Success)
            {
                // 优先返回第一个捕获组，否则返回第二个
                string target = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                return string.IsNullOrWhiteSpace(target) ? null : target.Trim();
            }
            return null;
        }

        /// <summary>
        /// 提取额外参数（方向、数量等）
        /// </summary>
        private void ExtractParameters(string input, IntentResult result)
        {
            // 提取方向
            Match dirMatch = Regex.Match(input, @"向?(东|西|南|北|上|下|左|右|前|后)");
            if (dirMatch.Success)
                result.parameters["direction"] = dirMatch.Groups[1].Value;

            // 提取数量
            Match numMatch = Regex.Match(input, @"(\d+|一|二|三|四|五|六|七|八|九|十)[个只张把瓶颗]?");
            if (numMatch.Success)
                result.parameters["quantity"] = numMatch.Groups[1].Value;

            // 提取技能名（引号内容）
            Match skillMatch = Regex.Match(input, @"[""「『](.+?)[""」』]");
            if (skillMatch.Success)
                result.parameters["skill_name"] = skillMatch.Groups[1].Value;
        }
    }
}