using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Logic.Memory
{
    /// <summary>
    /// 对话记录条目
    /// </summary>
    [Serializable]
    public class DialogueEntry
    {
        public string role;           // "user" 或 "assistant"
        public string content;        // 对话内容
        public long timestamp;        // 时间戳
        public int tokenEstimate;     // 估算Token数

        public DialogueEntry(string role, string content)
        {
            this.role = role;
            this.content = content;
            this.timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            this.tokenEstimate = EstimateTokens(content);
        }

        /// <summary>
        /// 简单的 Token 估算（中文约1.5字符/token，英文约4字符/token）
        /// </summary>
        private static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            
            int chineseCount = 0;
            int otherCount = 0;
            
            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                    chineseCount++;
                else
                    otherCount++;
            }
            
            return (int)(chineseCount / 1.5f + otherCount / 4f) + 1;
        }
    }

    /// <summary>
    /// 长期记忆摘要
    /// </summary>
    [Serializable]
    public class LongTermMemory
    {
        public string summary;                    // 压缩后的摘要文本
        public int originalTurnCount;             // 原始对话轮数
        public long createdAt;                    // 创建时间
        public List<string> keyEvents;            // 关键事件标签

        public LongTermMemory()
        {
            keyEvents = new List<string>();
            createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    /// <summary>
    /// 记忆状态快照（用于存档）
    /// </summary>
    [Serializable]
    public class MemorySnapshot
    {
        public List<DialogueEntry> shortTermMemory;
        public List<LongTermMemory> longTermMemories;
        public int totalTurns;
    }

    /// <summary>
    /// 双层记忆管理器
    /// 短期记忆：保存完整对话（滑动窗口）
    /// 长期记忆：超出窗口后进行摘要压缩
    /// </summary>
    public class MemoryManager : MonoSingleton<MemoryManager>
    {
        [Header("记忆配置")]
        [Tooltip("短期记忆最大Token数")]
        [SerializeField] private int maxShortTermTokens = 2000;
        
        [Tooltip("触发摘要压缩的Token阈值")]
        [SerializeField] private int compressionThreshold = 1500;
        
        [Tooltip("短期记忆最大轮数")]
        [SerializeField] private int maxShortTermTurns = 10;
        
        [Tooltip("长期记忆最大条数")]
        [SerializeField] private int maxLongTermCount = 5;

        // 短期记忆：完整对话历史（滑动窗口）
        private List<DialogueEntry> _shortTermMemory = new List<DialogueEntry>();
        
        // 长期记忆：压缩后的摘要
        private List<LongTermMemory> _longTermMemories = new List<LongTermMemory>();
        
        // 统计
        private int _totalTurns = 0;
        
        // 摘要生成中标志
        private bool _isSummarizing = false;

        /// <summary>
        /// 当前短期记忆的Token总数
        /// </summary>
        public int CurrentShortTermTokens
        {
            get
            {
                int total = 0;
                foreach (var entry in _shortTermMemory)
                    total += entry.tokenEstimate;
                return total;
            }
        }

        /// <summary>
        /// 添加用户输入到记忆
        /// </summary>
        public void AddUserMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            
            var entry = new DialogueEntry("user", content);
            _shortTermMemory.Add(entry);
            _totalTurns++;
            
            Debug.Log($"<color=yellow>[Memory] 添加用户消息，当前Token: {CurrentShortTermTokens}</color>");
            
            CheckAndCompress();
        }

        /// <summary>
        /// 添加AI回复到记忆（已清理CMD标签）
        /// </summary>
        public void AddAssistantMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            
            // 清理 CMD 标签后再存储
            string cleanContent = System.Text.RegularExpressions.Regex.Replace(
                content, @"<CMD>.*?</CMD>", "", 
                System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
            
            if (string.IsNullOrWhiteSpace(cleanContent)) return;
            
            var entry = new DialogueEntry("assistant", cleanContent);
            _shortTermMemory.Add(entry);
            
            Debug.Log($"<color=cyan>[Memory] 添加AI消息，当前Token: {CurrentShortTermTokens}</color>");
            
            CheckAndCompress();
        }

        /// <summary>
        /// 检查是否需要压缩并执行
        /// </summary>
        private void CheckAndCompress()
        {
            if (_isSummarizing) return;
            
            // 条件1: Token超过阈值
            // 条件2: 轮数超过最大值
            bool needCompress = CurrentShortTermTokens > compressionThreshold 
                             || _shortTermMemory.Count > maxShortTermTurns * 2;
            
            if (needCompress)
            {
                CompressOldestMemories();
            }
        }

        /// <summary>
        /// 压缩最旧的记忆到长期记忆
        /// </summary>
        private void CompressOldestMemories()
        {
            if (_shortTermMemory.Count < 4) return; // 至少保留2轮对话
            
            // 取出前半部分进行压缩
            int compressCount = _shortTermMemory.Count / 2;
            var toCompress = _shortTermMemory.GetRange(0, compressCount);
            
            // 生成本地摘要（轻量级方案）
            var summary = GenerateLocalSummary(toCompress);
            
            // 添加到长期记忆
            _longTermMemories.Add(summary);
            
            // 限制长期记忆数量
            while (_longTermMemories.Count > maxLongTermCount)
            {
                _longTermMemories.RemoveAt(0);
            }
            
            // 从短期记忆中移除已压缩的部分
            _shortTermMemory.RemoveRange(0, compressCount);
            
            Debug.Log($"<color=magenta>[Memory] 压缩完成: {compressCount}条 -> 摘要, 剩余Token: {CurrentShortTermTokens}</color>");
        }

        /// <summary>
        /// 本地轻量级摘要生成（基于规则提取）
        /// </summary>
        private LongTermMemory GenerateLocalSummary(List<DialogueEntry> entries)
        {
            var memory = new LongTermMemory();
            memory.originalTurnCount = entries.Count / 2;
            
            var summaryBuilder = new StringBuilder();
            summaryBuilder.Append("【历史摘要】");
            
            // 提取关键信息
            foreach (var entry in entries)
            {
                if (entry.role == "user")
                {
                    // 提取玩家行动关键词
                    string action = ExtractActionKeyword(entry.content);
                    if (!string.IsNullOrEmpty(action))
                    {
                        memory.keyEvents.Add($"玩家:{action}");
                    }
                }
                else if (entry.role == "assistant")
                {
                    // 提取叙事中的关键事件（简化版）
                    string keyEvent = ExtractKeyEvent(entry.content);
                    if (!string.IsNullOrEmpty(keyEvent))
                    {
                        memory.keyEvents.Add(keyEvent);
                    }
                }
            }
            
            // 构建摘要文本
            if (memory.keyEvents.Count > 0)
            {
                summaryBuilder.Append(string.Join("→", memory.keyEvents));
            }
            else
            {
                summaryBuilder.Append($"经历了{memory.originalTurnCount}轮探索");
            }
            
            memory.summary = summaryBuilder.ToString();
            return memory;
        }

        /// <summary>
        /// 从玩家输入提取行动关键词
        /// </summary>
        private string ExtractActionKeyword(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            
            // 关键动词模式
            string[] actionPatterns = { "攻击", "探索", "观察", "使用", "前往", "逃跑", "休息", "修炼", "对话", "采集" };
            
            foreach (var pattern in actionPatterns)
            {
                if (input.Contains(pattern))
                {
                    // 返回简短描述
                    return input.Length > 15 ? input.Substring(0, 15) + "..." : input;
                }
            }
            
            return input.Length > 10 ? input.Substring(0, 10) + "..." : input;
        }

        /// <summary>
        /// 从AI回复提取关键事件
        /// </summary>
        private string ExtractKeyEvent(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 20) return null;
            
            // 关键事件指示词
            string[] eventIndicators = { "发现", "遭遇", "获得", "受伤", "死亡", "逃脱", "击败", "学会", "进入", "离开" };
            
            foreach (var indicator in eventIndicators)
            {
                int idx = content.IndexOf(indicator);
                if (idx >= 0)
                {
                    int start = Math.Max(0, idx - 5);
                    int length = Math.Min(20, content.Length - start);
                    return content.Substring(start, length).Trim();
                }
            }
            
            return null;
        }

        /// <summary>
        /// 通过云端API生成高质量摘要（可选）
        /// </summary>
        public void RequestCloudSummary(List<DialogueEntry> entries, Action<string> onComplete)
        {
            if (_isSummarizing || entries == null || entries.Count == 0)
            {
                onComplete?.Invoke(null);
                return;
            }
            
            _isSummarizing = true;
            
            // 构建摘要请求
            var dialogueText = new StringBuilder();
            foreach (var entry in entries)
            {
                string role = entry.role == "user" ? "玩家" : "叙事";
                dialogueText.AppendLine($"{role}: {entry.content}");
            }
            
            string summaryPrompt = $@"请将以下对话历史压缩为一段简短摘要（50字以内），保留关键事件和状态变化：

{dialogueText}

要求：
1. 使用第三人称
2. 只保留关键情节转折
3. 直接输出摘要，不要任何解释";

            LLMService.Instance.PostStream(
                "你是一个专业的文本摘要助手。",
                summaryPrompt,
                onTokenReceived: null,  // 不需要流式输出
                onComplete: () => _isSummarizing = false
            );
        }

        /// <summary>
        /// 构建包含记忆上下文的消息数组
        /// </summary>
        public LLMService.Message[] BuildMessagesWithMemory(string systemPrompt, string userPrompt)
        {
            var messages = new List<LLMService.Message>();
            
            // 1. 系统提示
            messages.Add(new LLMService.Message { role = "system", content = systemPrompt });
            
            // 2. 长期记忆（作为系统上下文补充）
            if (_longTermMemories.Count > 0)
            {
                var longTermContext = BuildLongTermContext();
                messages.Add(new LLMService.Message 
                { 
                    role = "system", 
                    content = $"【长期记忆】\n{longTermContext}" 
                });
            }
            
            // 3. 短期记忆（完整对话历史）
            foreach (var entry in _shortTermMemory)
            {
                messages.Add(new LLMService.Message 
                { 
                    role = entry.role, 
                    content = entry.content 
                });
            }
            
            // 4. 当前用户输入
            messages.Add(new LLMService.Message { role = "user", content = userPrompt });
            
            return messages.ToArray();
        }

        /// <summary>
        /// 构建长期记忆上下文
        /// </summary>
        private string BuildLongTermContext()
        {
            var builder = new StringBuilder();
            
            for (int i = 0; i < _longTermMemories.Count; i++)
            {
                var mem = _longTermMemories[i];
                builder.AppendLine($"[回忆片段{i + 1}] {mem.summary}");
            }
            
            return builder.ToString();
        }

        /// <summary>
        /// 获取记忆状态快照（用于存档）
        /// </summary>
        public MemorySnapshot GetSnapshot()
        {
            return new MemorySnapshot
            {
                shortTermMemory = new List<DialogueEntry>(_shortTermMemory),
                longTermMemories = new List<LongTermMemory>(_longTermMemories),
                totalTurns = _totalTurns
            };
        }

        /// <summary>
        /// 从快照恢复记忆状态
        /// </summary>
        public void RestoreFromSnapshot(MemorySnapshot snapshot)
        {
            if (snapshot == null) return;
            
            _shortTermMemory = snapshot.shortTermMemory ?? new List<DialogueEntry>();
            _longTermMemories = snapshot.longTermMemories ?? new List<LongTermMemory>();
            _totalTurns = snapshot.totalTurns;
            
            Debug.Log($"[Memory] 已恢复记忆: 短期{_shortTermMemory.Count}条, 长期{_longTermMemories.Count}条");
        }

        /// <summary>
        /// 清空所有记忆（新游戏时调用）
        /// </summary>
        public void ClearAll()
        {
            _shortTermMemory.Clear();
            _longTermMemories.Clear();
            _totalTurns = 0;
            Debug.Log("[Memory] 记忆已清空");
        }

        /// <summary>
        /// 获取调试信息
        /// </summary>
        public string GetDebugInfo()
        {
            return $"短期: {_shortTermMemory.Count}条/{CurrentShortTermTokens}tokens | " +
                   $"长期: {_longTermMemories.Count}条 | 总轮数: {_totalTurns}";
        }
    }
}