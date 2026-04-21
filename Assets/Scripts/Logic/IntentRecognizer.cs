using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using LitJson;

namespace Logic.Intent
{
    /// <summary>
    /// 行动类型枚举 - 标准化行为指令
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
        public Dictionary<string, string> parameters; // 附加参数
        public string recognitionSource; // 识别来源: "rule" / "semantic" / "llm"

        public IntentResult()
        {
            actionType = ActionType.Unknown;
            confidence = 0f;
            parameters = new Dictionary<string, string>();
            recognitionSource = "rule";
        }

        public override string ToString()
        {
            return $"[意图: {actionType}, 目标: {targetEntity ?? "无"}, 置信度: {confidence:P0}, 来源: {recognitionSource}]";
        }
    }

    /// <summary>
    /// 意图识别规则类
    /// </summary>
    [Serializable]
    public class IntentRule
    {
        public ActionType actionType;
        public string[] keywords;        // 关键词列表
        public string[] semanticPatterns; // 语义模式（支持模糊匹配）
        public string regexPattern;      // 正则模式（可选，用于精确匹配）
        public int priority;             // 优先级（数值越大优先级越高）
    }

    /// <summary>
    /// 语义相似度计算器（轻量级本地方案）
    /// </summary>
    public class SemanticMatcher
    {
        // 同义词词典 - 扩展关键词覆盖范围
        private static readonly Dictionary<string, string[]> SynonymDict = new Dictionary<string, string[]>
        {
            // 攻击类同义词
            { "攻击", new[] { "打", "揍", "捶", "殴打", "出手", "动手", "招呼", "伺候", "收拾", "教训", "干掉", "解决", "消灭", "弄死", "宰了", "剁了" } },
            // 防御类同义词
            { "防御", new[] { "挡", "格", "护", "守", "躲", "闪", "避", "退", "缩", "蹲下", "趴下", "隐蔽", "找掩护" } },
            // 移动类同义词
            { "移动", new[] { "走", "跑", "爬", "游", "飞", "跳", "钻", "溜", "窜", "逃", "撤", "闪人", "跑路", "开溜", "撤退", "脚底抹油" } },
            // 观察类同义词
            { "观察", new[] { "看", "瞧", "瞅", "盯", "望", "视", "端详", "打量", "审视", "扫视", "环顾", "张望", "窥探", "侦查", "搜寻", "检查" } },
            // 休息类同义词
            { "休息", new[] { "歇", "停", "躺", "坐", "靠", "喘口气", "缓一缓", "打个盹", "眯一会", "养精蓄锐", "恢复体力" } },
            // 对话类同义词
            { "对话", new[] { "说", "讲", "聊", "谈", "问", "答", "询问", "打听", "套话", "搭讪", "交流", "沟通", "商量", "请教", "质问" } },
            // 使用物品同义词
            { "使用", new[] { "用", "吃", "喝", "服", "涂", "抹", "敷", "注射", "吞服", "嗑", "磕", "整", "搞" } },
            // 技能施放同义词
            { "施法", new[] { "放", "发", "打出", "施展", "释放", "运功", "催动", "激发", "发动", "使出", "祭出", "掏出" } },
            // 采集类同义词
            { "采集", new[] { "采", "摘", "挖", "捡", "捞", "掏", "薅", "搜刮", "收割", "拾取", "获取" } },
            // 修炼类同义词
            { "修炼", new[] { "练", "炼", "悟", "参", "冥想", "打坐", "静修", "闭关", "吐纳", "运气", "调息" } }
        };

        // 隐喻/口语化表达映射
        private static readonly Dictionary<string, ActionType> MetaphorMap = new Dictionary<string, ActionType>
        {
            // 攻击类隐喻
            { "送他上路", ActionType.Attack },
            { "让他见祖宗", ActionType.Attack },
            { "给他点颜色看看", ActionType.Attack },
            { "招呼他", ActionType.Attack },
            { "伺候他", ActionType.Attack },
            { "收拾", ActionType.Attack },
            { "教训", ActionType.Attack },
            { "干他", ActionType.Attack },
            { "怼", ActionType.Attack },
            { "莽", ActionType.Attack },
            { "冲", ActionType.Attack },
            { "上", ActionType.Attack },
            
            // 移动类隐喻
            { "开溜", ActionType.Move },
            { "脚底抹油", ActionType.Move },
            { "三十六计", ActionType.Move },
            { "跑路", ActionType.Move },
            { "闪人", ActionType.Move },
            { "撤退", ActionType.Move },
            { "溜之大吉", ActionType.Move },
            { "逃之夭夭", ActionType.Move },
            
            // 观察类隐喻
            { "瞅瞅", ActionType.Observe },
            { "瞧瞧", ActionType.Observe },
            { "看看情况", ActionType.Observe },
            { "打探", ActionType.Observe },
            { "摸清", ActionType.Observe },
            
            // 休息类隐喻
            { "喘口气", ActionType.Rest },
            { "缓一缓", ActionType.Rest },
            { "歇歇脚", ActionType.Rest },
            { "养精蓄锐", ActionType.Rest },
            
            // 对话类隐喻
            { "套套近乎", ActionType.Talk },
            { "聊两句", ActionType.Talk },
            { "打个招呼", ActionType.Talk },
            { "搭讪", ActionType.Talk },
            { "套话", ActionType.Talk },
            
            // 技能类隐喻
            { "放大招", ActionType.UseSkill },
            { "憋大的", ActionType.UseSkill },
            { "甩个技能", ActionType.UseSkill },
            { "来一发", ActionType.UseSkill }
        };

        /// <summary>
        /// 检查是否匹配隐喻表达
        /// </summary>
        public static bool TryMatchMetaphor(string input, out ActionType actionType, out float confidence)
        {
            actionType = ActionType.Unknown;
            confidence = 0f;

            foreach (var kvp in MetaphorMap)
            {
                if (input.Contains(kvp.Key))
                {
                    actionType = kvp.Value;
                    confidence = 0.85f; // 隐喻匹配给予较高置信度
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 扩展关键词匹配（包含同义词）
        /// </summary>
        public static bool MatchWithSynonyms(string input, string baseKeyword, out string matchedWord)
        {
            matchedWord = null;
            
            // 先检查原词
            if (input.Contains(baseKeyword))
            {
                matchedWord = baseKeyword;
                return true;
            }

            // 检查同义词
            if (SynonymDict.TryGetValue(baseKeyword, out var synonyms))
            {
                foreach (var synonym in synonyms)
                {
                    if (input.Contains(synonym))
                    {
                        matchedWord = synonym;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 计算语义相似度（基于字符重叠 + 同义词扩展）
        /// </summary>
        public static float CalculateSimilarity(string input, string pattern)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(pattern))
                return 0f;

            // 简化的 Jaccard 相似度
            var inputChars = new HashSet<char>(input);
            var patternChars = new HashSet<char>(pattern);
            
            int intersection = 0;
            foreach (var c in patternChars)
            {
                if (inputChars.Contains(c))
                    intersection++;
            }

            int union = inputChars.Count + patternChars.Count - intersection;
            return union > 0 ? (float)intersection / union : 0f;
        }
    }

    /// <summary>
    /// 混合意图识别器
    /// 第一层：规则引擎（高效）
    /// 第二层：语义扩展（同义词+隐喻）
    /// 第三层：LLM 后备（复杂情况）
    /// </summary>
    public class IntentRecognizer
    {
        private static IntentRecognizer _instance;
        public static IntentRecognizer Instance => _instance ??= new IntentRecognizer();

        private readonly List<IntentRule> _rules;
        private readonly Regex _targetExtractor;
        
        // LLM 后备识别的置信度阈值
        private const float LLM_FALLBACK_THRESHOLD = 0.4f;
        
        // 缓存最近的 LLM 识别结果（避免重复请求）
        private readonly Dictionary<string, IntentResult> _llmCache = new Dictionary<string, IntentResult>();
        private const int MAX_CACHE_SIZE = 50;

        private IntentRecognizer()
        {
            _rules = InitializeRules();
            _targetExtractor = new Regex(
                @"(?:对|向|往|朝|去|把|攻击|查看|观察|检查|使用|施展|释放)\s*[""「『]?(\S{1,10})[""」』]?|(\S{2,6})(?:身上|那边|那里|处|旁)",
                RegexOptions.Compiled);
        }

        /// <summary>
        /// 初始化意图识别规则（增强版）
        /// </summary>
        private List<IntentRule> InitializeRules()
        {
            return new List<IntentRule>
            {
                new IntentRule
                {
                    actionType = ActionType.Attack,
                    keywords = new[] { "攻击", "打", "杀", "斩", "砍", "刺", "劈", "揍", "击", "战斗", "进攻", "出手", "动手", "宰", "弑", "诛" },
                    semanticPatterns = new[] { "发起攻击", "主动出击", "先下手为强", "打他", "揍他", "干他" },
                    regexPattern = @"(攻击|打|杀|斩|砍|劈|揍|击|战斗|进攻|出手|动手).{0,6}",
                    priority = 10
                },
                new IntentRule
                {
                    actionType = ActionType.Defend,
                    keywords = new[] { "防御", "挡", "格挡", "闪", "躲", "挡住", "护住", "架住", "退后", "后撤" },
                    semanticPatterns = new[] { "进入防御", "做好防备", "小心戒备", "防守姿态" },
                    regexPattern = @"(防御|挡|格挡|闪开|挡住|护住|架住|退|撤)",
                    priority = 9
                },
                new IntentRule
                {
                    actionType = ActionType.Move,
                    keywords = new[] { "走", "跑", "前进", "离开", "出发", "移动", "去", "到", "向", "朝", "前往", "进入", "返回", "逃", "撤退", "后退", "转身" },
                    semanticPatterns = new[] { "动身前往", "启程", "赶往", "奔向", "逃离此地" },
                    regexPattern = @"(走|跑|前进|离开|出发|移动|去|到|向|朝|前往|进入|返回|逃|向[东南西北上下前后])",
                    priority = 7
                },
                new IntentRule
                {
                    actionType = ActionType.Explore,
                    keywords = new[] { "探索", "搜索", "寻找", "找", "调查", "探寻", "探查", "摸索", "搜寻" },
                    semanticPatterns = new[] { "四处查看", "仔细搜寻", "探索周围", "找找看" },
                    regexPattern = @"(探索|搜索|寻找|调查|探寻|探查|摸索|搜寻)",
                    priority = 6
                },
                new IntentRule
                {
                    actionType = ActionType.Observe,
                    keywords = new[] { "看", "观察", "查看", "检查", "注视", "审视", "端详", "打量", "望", "环顾", "瞧", "瞅" },
                    semanticPatterns = new[] { "仔细观察", "看看情况", "打量一番", "观望", "检查一下" },
                    regexPattern = @"(看|观察|查看|检查|注视|审视|端详|打量|环顾|瞧|瞅).{0,6}",
                    priority = 5
                },
                new IntentRule
                {
                    actionType = ActionType.UseItem,
                    keywords = new[] { "使用", "用", "吃", "服用", "拿出", "装备", "佩戴", "穿上", "戴上", "喝", "服下", "吞" },
                    semanticPatterns = new[] { "使用道具", "吃点东西", "拿出来用", "给自己用" },
                    regexPattern = @"(使用|用|吃|服用|拿出|装备|喝|服|吞).{0,8}",
                    priority = 8
                },
                new IntentRule
                {
                    actionType = ActionType.UseSkill,
                    keywords = new[] { "施展", "释放", "发动", "使用", "运功", "咒语", "施法", "催动", "祭出" },
                    semanticPatterns = new[] { "施展法术", "释放技能", "发动攻击", "使出绝招" },
                    regexPattern = @"(施展|释放|发动|使用|运功|施法|催动|祭出).{0,10}",
                    priority = 9
                },
                new IntentRule
                {
                    actionType = ActionType.Talk,
                    keywords = new[] { "说", "问", "交谈", "对话", "询问", "回答", "答", "聊", "讲", "喊", "叫", "呼唤" },
                    semanticPatterns = new[] { "与其对话", "开口询问", "搭话", "打招呼" },
                    regexPattern = @"(说|问|交谈|对话|询问|回答|答|聊|讲|喊|叫).{0,10}",
                    priority = 4
                },
                new IntentRule
                {
                    actionType = ActionType.Rest,
                    keywords = new[] { "休息", "睡觉", "打坐", "冥想", "恢复", "歇息", "歇", "躺", "坐下" },
                    semanticPatterns = new[] { "休息一下", "恢复体力", "养精蓄锐", "歇歇脚" },
                    regexPattern = @"(休息|睡觉|打坐|冥想|恢复|歇息|歇|躺|坐下)",
                    priority = 5
                },
                new IntentRule
                {
                    actionType = ActionType.Collect,
                    keywords = new[] { "采集", "收集", "捡", "拾取", "摘", "挖", "取", "拿" },
                    semanticPatterns = new[] { "采集材料", "收集物品", "捡起来", "拾取" },
                    regexPattern = @"(采集|收集|捡起|拾取|摘|挖|取).{0,6}",
                    priority = 6
                },
                new IntentRule
                {
                    actionType = ActionType.Cultivate,
                    keywords = new[] { "修炼", "修行", "练功", "突破", "参悟", "领悟", "感悟", "炼化", "吐纳" },
                    semanticPatterns = new[] { "闭关修炼", "潜心修行", "感悟天道", "突破境界" },
                    regexPattern = @"(修炼|修行|练功|突破|参悟|领悟|感悟|炼化|吐纳)",
                    priority = 5
                }
            };
        }

        /// <summary>
        /// 同步识别玩家输入的意图（主入口）
        /// </summary>
        public IntentResult Recognize(string playerInput)
        {
            if (string.IsNullOrWhiteSpace(playerInput))
                return new IntentResult();

            string input = playerInput.Trim();
            
            // ========== 第一层：隐喻/口语化表达直接匹配 ==========
            if (SemanticMatcher.TryMatchMetaphor(input, out var metaphorType, out var metaphorConf))
            {
                var metaphorResult = new IntentResult
                {
                    actionType = metaphorType,
                    confidence = metaphorConf,
                    recognitionSource = "semantic",
                    targetEntity = ExtractTarget(playerInput)
                };
                ExtractParameters(playerInput, metaphorResult);
                Debug.Log($"<color=cyan>[意图识别-隐喻] {playerInput} => {metaphorResult}</color>");
                return metaphorResult;
            }

            // ========== 第二层：增强规则引擎（含同义词扩展） ==========
            IntentResult bestResult = new IntentResult();
            int bestScore = 0;

            foreach (var rule in _rules)
            {
                int score = CalculateEnhancedMatchScore(input, rule, out string matchedVerb);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestResult.actionType = rule.actionType;
                    bestResult.actionVerb = matchedVerb;
                    bestResult.confidence = Mathf.Clamp01(score / 25f);
                    bestResult.recognitionSource = "rule";
                }
            }

            // 提取目标实体
            bestResult.targetEntity = ExtractTarget(playerInput);

            // 提取附加参数
            ExtractParameters(playerInput, bestResult);

            // ========== 第三层：置信度过低时标记需要 LLM 后备 ==========
            if (bestResult.confidence < LLM_FALLBACK_THRESHOLD)
            {
                bestResult.recognitionSource = "low_confidence";
                Debug.Log($"<color=yellow>[意图识别-低置信度] {playerInput} => {bestResult}, 建议启用LLM后备</color>");
            }
            else
            {
                Debug.Log($"<color=green>[意图识别] {playerInput} => {bestResult}</color>");
            }

            return bestResult;
        }

        /// <summary>
        /// 异步识别（启用 LLM 后备）
        /// </summary>
        public void RecognizeAsync(string playerInput, Action<IntentResult> callback)
        {
            // 先尝试本地识别
            var localResult = Recognize(playerInput);

            // 如果置信度足够高，直接返回
            if (localResult.confidence >= LLM_FALLBACK_THRESHOLD)
            {
                callback?.Invoke(localResult);
                return;
            }

            // 检查缓存
            if (_llmCache.TryGetValue(playerInput, out var cachedResult))
            {
                callback?.Invoke(cachedResult);
                return;
            }

            // 启用 LLM 后备识别
            RequestLLMIntentRecognition(playerInput, (llmResult) =>
            {
                // 合并结果：LLM 结果优先，但保留本地提取的参数
                if (llmResult.actionType != ActionType.Unknown)
                {
                    llmResult.targetEntity = llmResult.targetEntity ?? localResult.targetEntity;
                    foreach (var kvp in localResult.parameters)
                    {
                        if (!llmResult.parameters.ContainsKey(kvp.Key))
                            llmResult.parameters[kvp.Key] = kvp.Value;
                    }
                    
                    // 缓存结果
                    CacheResult(playerInput, llmResult);
                    callback?.Invoke(llmResult);
                }
                else
                {
                    // LLM 也无法识别，返回本地结果
                    callback?.Invoke(localResult);
                }
            });
        }

        /// <summary>
        /// 增强匹配得分计算（含同义词）
        /// </summary>
        private int CalculateEnhancedMatchScore(string input, IntentRule rule, out string matchedVerb)
        {
            int score = 0;
            matchedVerb = null;

            // 原始关键词匹配
            foreach (var keyword in rule.keywords)
            {
                if (input.Contains(keyword))
                {
                    score += 5 + rule.priority;
                    matchedVerb = matchedVerb ?? keyword;
                    if (input.StartsWith(keyword))
                        score += 3;
                }
            }

            // 同义词扩展匹配
            foreach (var keyword in rule.keywords)
            {
                if (SemanticMatcher.MatchWithSynonyms(input, keyword, out string matched))
                {
                    if (matched != keyword) // 避免重复计分
                    {
                        score += 4 + rule.priority / 2;
                        matchedVerb = matchedVerb ?? matched;
                    }
                }
            }

            // 语义模式匹配
            if (rule.semanticPatterns != null)
            {
                foreach (var pattern in rule.semanticPatterns)
                {
                    float similarity = SemanticMatcher.CalculateSimilarity(input, pattern);
                    if (similarity > 0.3f)
                    {
                        score += (int)(similarity * 8);
                    }
                }
            }

            // 正则匹配加分
            if (!string.IsNullOrEmpty(rule.regexPattern))
            {
                var match = Regex.Match(input, rule.regexPattern);
                if (match.Success)
                {
                    score += 5;
                    matchedVerb = matchedVerb ?? match.Groups[1].Value;
                }
            }

            return score;
        }

        /// <summary>
        /// 提取目标实体
        /// </summary>
        private string ExtractTarget(string input)
        {
            Match match = _targetExtractor.Match(input);
            if (match.Success)
            {
                string target = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                return string.IsNullOrWhiteSpace(target) ? null : target.Trim();
            }
            return null;
        }

        /// <summary>
        /// 提取附加参数
        /// </summary>
        private void ExtractParameters(string input, IntentResult result)
        {
            // 提取方向
            Match dirMatch = Regex.Match(input, @"向?(东|南|西|北|上|下|前|后|左|右)");
            if (dirMatch.Success)
                result.parameters["direction"] = dirMatch.Groups[1].Value;

            // 提取数量
            Match numMatch = Regex.Match(input, @"(\d+|一|二|三|四|五|六|七|八|九|十)[个只把瓶颗]?");
            if (numMatch.Success)
                result.parameters["quantity"] = numMatch.Groups[1].Value;

            // 提取技能名（引号内内容）
            Match skillMatch = Regex.Match(input, @"[""「『](.+?)[""」』]");
            if (skillMatch.Success)
                result.parameters["skill_name"] = skillMatch.Groups[1].Value;

            // 提取物品名（常见物品词缀）
            Match itemMatch = Regex.Match(input, @"([\u4e00-\u9fa5]{2,4}(?:丹|药|剑|刀|甲|符|石|草|果|水|酒|丸))");
            if (itemMatch.Success && !result.parameters.ContainsKey("item_name"))
                result.parameters["item_name"] = itemMatch.Groups[1].Value;
        }

        /// <summary>
        /// LLM 后备意图识别
        /// </summary>
        private void RequestLLMIntentRecognition(string playerInput, Action<IntentResult> callback)
        {
            string prompt = $@"你是一个游戏意图识别器。请分析玩家输入，返回JSON格式的意图识别结果。

玩家输入：{playerInput}

可选的动作类型（actionType）：
- Attack（攻击）
- Defend（防御）
- Move（移动）
- Explore（探索）
- UseItem（使用物品）
- UseSkill（使用技能）
- Talk（对话）
- Rest（休息）
- Observe（观察）
- Collect（采集）
- Cultivate（修炼）
- Unknown（无法识别）

请直接输出JSON，不要有其他文字：
{{""actionType"": ""类型"", ""target"": ""目标对象或null"", ""confidence"": 0.0到1.0}}";

            LLMService.Instance.PostNonStream(
                "你是精确的意图分类器，只输出JSON，不要解释。",
                prompt,
                (response) =>
                {
                    var result = ParseLLMIntentResponse(response);
                    result.recognitionSource = "llm";
                    callback?.Invoke(result);
                }
            );
        }

        /// <summary>
        /// 解析 LLM 返回的意图识别结果
        /// </summary>
        private IntentResult ParseLLMIntentResponse(string response)
        {
            var result = new IntentResult();
            
            if (string.IsNullOrEmpty(response))
                return result;

            try
            {
                // 提取 JSON 部分
                var jsonMatch = Regex.Match(response, @"\{[^{}]*\}");
                if (!jsonMatch.Success)
                    return result;

                JsonData data = JsonMapper.ToObject(jsonMatch.Value);
                
                if (data.Keys.Contains("actionType"))
                {
                    string typeStr = (string)data["actionType"];
                    if (Enum.TryParse<ActionType>(typeStr, true, out var actionType))
                    {
                        result.actionType = actionType;
                    }
                }

                if (data.Keys.Contains("target") && data["target"] != null)
                {
                    result.targetEntity = (string)data["target"];
                }

                if (data.Keys.Contains("confidence"))
                {
                    result.confidence = (float)(double)data["confidence"];
                }
                else
                {
                    result.confidence = 0.7f; // LLM 结果默认置信度
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[意图识别] LLM 响应解析失败: {e.Message}");
            }

            return result;
        }

        /// <summary>
        /// 缓存 LLM 识别结果
        /// </summary>
        private void CacheResult(string input, IntentResult result)
        {
            if (_llmCache.Count >= MAX_CACHE_SIZE)
            {
                // 简单清理：移除一半缓存
                var keys = new List<string>(_llmCache.Keys);
                for (int i = 0; i < keys.Count / 2; i++)
                {
                    _llmCache.Remove(keys[i]);
                }
            }
            _llmCache[input] = result;
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public void ClearCache()
        {
            _llmCache.Clear();
        }
    }
}
