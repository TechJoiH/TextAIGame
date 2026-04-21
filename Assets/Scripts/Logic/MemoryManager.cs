using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Logic.Memory
{
    [Serializable]
    public class DialogueEntry
    {
        public string role;
        public string content;
        public long timestamp;
        public int tokenEstimate;

        public DialogueEntry()
        {
        }

        public DialogueEntry(string role, string content)
        {
            this.role = role;
            this.content = content;
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            tokenEstimate = EstimateTokens(content);
        }

        private static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

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

    [Serializable]
    public class LongTermMemory
    {
        public string summary;
        public int originalTurnCount;
        public long createdAt;
        public List<string> keyEvents;

        public LongTermMemory()
        {
            keyEvents = new List<string>();
            createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    [Serializable]
    public class MemorySnapshot
    {
        public List<DialogueEntry> shortTermMemory;
        public List<LongTermMemory> longTermMemories;
        public int totalTurns;
    }

    public class MemoryManager : MonoSingleton<MemoryManager>
    {
        [Header("记忆配置")]
        [Tooltip("短期记忆最大 Token 数")]
        [SerializeField] private int maxShortTermTokens = 2000;

        [Tooltip("触发摘要压缩的 Token 阈值")]
        [SerializeField] private int compressionThreshold = 1500;

        [Tooltip("短期记忆最大轮数")]
        [SerializeField] private int maxShortTermTurns = 10;

        [Tooltip("长期记忆最大条数")]
        [SerializeField] private int maxLongTermCount = 5;

        private readonly List<DialogueEntry> _shortTermMemory = new List<DialogueEntry>();
        private readonly List<LongTermMemory> _longTermMemories = new List<LongTermMemory>();
        private int _totalTurns;
        private bool _isSummarizing;

        public int CurrentShortTermTokens
        {
            get
            {
                int total = 0;
                foreach (var entry in _shortTermMemory)
                {
                    if (entry != null)
                        total += entry.tokenEstimate;
                }

                return total;
            }
        }

        public void AddUserMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            _shortTermMemory.Add(new DialogueEntry("user", content));
            _totalTurns++;

            Debug.Log($"<color=yellow>[Memory] 添加用户消息，当前Token: {CurrentShortTermTokens}</color>");
            CheckAndCompress();
        }

        public void AddAssistantMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            string cleanContent = Regex.Replace(content, @"<CMD>.*?</CMD>", string.Empty, RegexOptions.Singleline).Trim();
            if (string.IsNullOrWhiteSpace(cleanContent))
                return;

            _shortTermMemory.Add(new DialogueEntry("assistant", cleanContent));
            Debug.Log($"<color=cyan>[Memory] 添加AI消息，当前Token: {CurrentShortTermTokens}</color>");
            CheckAndCompress();
        }

        private void CheckAndCompress()
        {
            if (_isSummarizing)
                return;

            bool needCompress =
                CurrentShortTermTokens > compressionThreshold ||
                _shortTermMemory.Count > maxShortTermTurns * 2;

            if (needCompress)
                CompressOldestMemories();
        }

        private void CompressOldestMemories()
        {
            if (_shortTermMemory.Count < 4)
                return;

            int compressCount = _shortTermMemory.Count / 2;
            var toCompress = _shortTermMemory.GetRange(0, compressCount);
            LongTermMemory summary = GenerateLocalSummary(toCompress);

            _longTermMemories.Add(summary);
            while (_longTermMemories.Count > maxLongTermCount)
                _longTermMemories.RemoveAt(0);

            _shortTermMemory.RemoveRange(0, compressCount);
            Debug.Log($"<color=magenta>[Memory] 压缩完成: {compressCount}条 -> 摘要, 剩余Token: {CurrentShortTermTokens}</color>");
        }

        private LongTermMemory GenerateLocalSummary(List<DialogueEntry> entries)
        {
            var memory = new LongTermMemory
            {
                originalTurnCount = Mathf.Max(1, entries.Count / 2)
            };

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.content))
                    continue;

                if (entry.role == "user")
                {
                    string action = ExtractActionKeyword(entry.content);
                    if (!string.IsNullOrWhiteSpace(action))
                        memory.keyEvents.Add($"玩家:{action}");
                }
                else if (entry.role == "assistant")
                {
                    string keyEvent = ExtractKeyEvent(entry.content);
                    if (!string.IsNullOrWhiteSpace(keyEvent))
                        memory.keyEvents.Add(keyEvent);
                }
            }

            if (memory.keyEvents.Count > 0)
                memory.summary = $"【历史摘要】{string.Join(" -> ", memory.keyEvents)}";
            else
                memory.summary = $"【历史摘要】经历了 {memory.originalTurnCount} 轮探索。";

            return memory;
        }

        private string ExtractActionKeyword(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            string[] actionPatterns =
            {
                "攻击", "探索", "观察", "查看", "使用", "前往", "逃跑", "休息", "修炼", "对话", "采集"
            };

            foreach (var pattern in actionPatterns)
            {
                if (input.Contains(pattern))
                    return input.Length > 15 ? input.Substring(0, 15) + "..." : input;
            }

            return input.Length > 10 ? input.Substring(0, 10) + "..." : input;
        }

        private string ExtractKeyEvent(string content)
        {
            if (string.IsNullOrWhiteSpace(content) || content.Length < 8)
                return null;

            string[] eventIndicators =
            {
                "发现", "遭遇", "获得", "受伤", "逃脱", "击败", "学会", "进入", "离开", "异象"
            };

            foreach (var indicator in eventIndicators)
            {
                int index = content.IndexOf(indicator, StringComparison.Ordinal);
                if (index < 0)
                    continue;

                int start = Math.Max(0, index - 5);
                int length = Math.Min(20, content.Length - start);
                return content.Substring(start, length).Trim();
            }

            return null;
        }

        public void RequestCloudSummary(List<DialogueEntry> entries, Action<string> onComplete)
        {
            if (_isSummarizing || entries == null || entries.Count == 0)
            {
                onComplete?.Invoke(null);
                return;
            }

            _isSummarizing = true;
            var dialogueText = new StringBuilder();
            foreach (var entry in entries)
            {
                if (entry == null)
                    continue;

                string roleName = entry.role == "user" ? "玩家" : "叙事";
                dialogueText.AppendLine($"{roleName}: {entry.content}");
            }

            string prompt =
                "请将以下对话历史压缩成一段 50 字以内的摘要，保留关键事件与状态变化，使用第三人称直接输出摘要：\n\n" +
                dialogueText;

            LLMService.Instance.PostStream(
                "你是专业的文本摘要助手。",
                prompt,
                onTokenReceived: null,
                onComplete: () =>
                {
                    _isSummarizing = false;
                    onComplete?.Invoke(null);
                });
        }

        public LLMService.Message[] BuildMessagesWithMemory(string systemPrompt, string userPrompt)
        {
            var messages = new List<LLMService.Message>
            {
                new LLMService.Message { role = "system", content = systemPrompt }
            };

            if (_longTermMemories.Count > 0)
            {
                messages.Add(new LLMService.Message
                {
                    role = "system",
                    content = $"【长期记忆】\n{BuildLongTermContext()}"
                });
            }

            var shortTermEntries = new List<DialogueEntry>();
            int runningTokens = 0;
            for (int i = _shortTermMemory.Count - 1; i >= 0; i--)
            {
                DialogueEntry entry = _shortTermMemory[i];
                if (entry == null)
                    continue;

                if (runningTokens + entry.tokenEstimate > maxShortTermTokens && shortTermEntries.Count > 0)
                    break;

                runningTokens += entry.tokenEstimate;
                shortTermEntries.Insert(0, entry);
            }

            foreach (var entry in shortTermEntries)
            {
                messages.Add(new LLMService.Message
                {
                    role = entry.role,
                    content = entry.content
                });
            }

            messages.Add(new LLMService.Message { role = "user", content = userPrompt });
            return messages.ToArray();
        }

        private string BuildLongTermContext()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < _longTermMemories.Count; i++)
            {
                builder.AppendLine($"[回忆片段{i + 1}] {_longTermMemories[i].summary}");
            }

            return builder.ToString().TrimEnd();
        }

        public MemorySnapshot GetSnapshot()
        {
            return new MemorySnapshot
            {
                shortTermMemory = new List<DialogueEntry>(_shortTermMemory),
                longTermMemories = new List<LongTermMemory>(_longTermMemories),
                totalTurns = _totalTurns
            };
        }

        public List<DialogueEntry> GetRecentDialogueEntries()
        {
            return new List<DialogueEntry>(_shortTermMemory);
        }

        public List<LongTermMemory> GetLongTermMemoryEntries()
        {
            return new List<LongTermMemory>(_longTermMemories);
        }

        public void RestoreFromSnapshot(MemorySnapshot snapshot)
        {
            _shortTermMemory.Clear();
            _longTermMemories.Clear();
            _totalTurns = 0;

            if (snapshot == null)
                return;

            if (snapshot.shortTermMemory != null)
                _shortTermMemory.AddRange(snapshot.shortTermMemory);
            if (snapshot.longTermMemories != null)
                _longTermMemories.AddRange(snapshot.longTermMemories);
            _totalTurns = snapshot.totalTurns;

            Debug.Log($"[Memory] 已恢复记忆: 短期{_shortTermMemory.Count}条, 长期{_longTermMemories.Count}条");
        }

        public void ClearAll()
        {
            _shortTermMemory.Clear();
            _longTermMemories.Clear();
            _totalTurns = 0;
            Debug.Log("[Memory] 记忆已清空");
        }

        public string GetDebugInfo()
        {
            return $"短期: {_shortTermMemory.Count}条/{CurrentShortTermTokens} tokens | 长期: {_longTermMemories.Count}条 | 总轮数: {_totalTurns}";
        }
    }
}
