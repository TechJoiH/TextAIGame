using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using LitJson;

/// <summary>
/// LLM 服务（OpenAI Compatible）
/// 支持流式输出、配置化接口与演示兜底。
/// </summary>
public class LLMService : MonoSingleton<LLMService>
{
    private const string ConfigFileName = "llm_config.json";
    private const string DefaultConfigResourcePath = "Configs/llm_config";

    [Serializable]
    public class LLMConfig
    {
        public string provider = "openai-compatible";
        public string baseUrl = "https://integrate.api.nvidia.com/v1/chat/completions";
        public string model = "meta/llama-3.1-70b-instruct";
        public string apiKey = "";
        public bool stream = true;
        public int timeoutSeconds = 45;
    }

    [Serializable]
    public class RequestPayload
    {
        public string model;
        public Message[] messages;
        public bool stream = true;
        public double temperature = 0.7;
        public int max_tokens = 1024;
    }

    [Serializable]
    public class Message
    {
        public string role;
        public string content;
    }

    private LLMConfig _config;

    public bool HasUsableRemoteConfig =>
        _config != null &&
        !string.IsNullOrWhiteSpace(_config.baseUrl) &&
        !string.IsNullOrWhiteSpace(_config.model) &&
        !string.IsNullOrWhiteSpace(_config.apiKey);

    protected override void Awake()
    {
        base.Awake();
        ReloadConfig();
    }

    public void ReloadConfig()
    {
        _config = LoadConfig();
    }

    public void PostStream(string systemPrompt, string userPrompt, Action<string> onTokenReceived, Action onComplete, Action<string> onStatus = null)
    {
        var messages = new[]
        {
            new Message { role = "system", content = systemPrompt },
            new Message { role = "user", content = userPrompt }
        };

        StartCoroutine(RequestRoutine(messages, onTokenReceived, onComplete, onStatus));
    }

    public void PostStreamWithMessages(Message[] messages, Action<string> onTokenReceived, Action onComplete, Action<string> onStatus = null)
    {
        StartCoroutine(RequestRoutine(messages, onTokenReceived, onComplete, onStatus));
    }

    public void PostNonStream(string systemPrompt, string userPrompt, Action<string> onComplete, Action<string> onStatus = null)
    {
        var messages = new[]
        {
            new Message { role = "system", content = systemPrompt },
            new Message { role = "user", content = userPrompt }
        };

        StartCoroutine(NonStreamRequestRoutine(messages, onComplete, onStatus));
    }

    private IEnumerator RequestRoutine(Message[] messages, Action<string> onToken, Action onComplete, Action<string> onStatus)
    {
        ReloadConfig();

        if (!HasUsableRemoteConfig)
        {
            onStatus?.Invoke("未检测到可用模型配置，已切换为本地演示叙事。");
            onToken?.Invoke(BuildDemoNarrative(messages));
            onComplete?.Invoke();
            yield break;
        }

        bool useStreaming = _config.stream;
        var payload = new RequestPayload
        {
            model = _config.model,
            messages = messages,
            stream = useStreaming
        };

        string json = JsonMapper.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest(_config.baseUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Clamp(_config.timeoutSeconds, 5, 120);

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", BuildAuthorizationHeader(_config.apiKey));
            if (useStreaming)
                request.SetRequestHeader("Accept", "text/event-stream");

            var operation = request.SendWebRequest();
            int lastDataIndex = 0;

            while (!operation.isDone)
            {
                yield return null;

                if (!useStreaming)
                    continue;

                string currentText = request.downloadHandler.text;
                if (string.IsNullOrEmpty(currentText) || currentText.Length <= lastDataIndex)
                    continue;

                string newData = currentText.Substring(lastDataIndex);
                lastDataIndex = currentText.Length;
                ProcessStreamData(newData, onToken);
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (useStreaming)
                {
                    string finalText = request.downloadHandler.text;
                    if (!string.IsNullOrEmpty(finalText) && finalText.Length > lastDataIndex)
                    {
                        string newData = finalText.Substring(lastDataIndex);
                        ProcessStreamData(newData, onToken);
                    }
                }
                else
                {
                    string content = ExtractNonStreamContent(request.downloadHandler.text);
                    if (!string.IsNullOrWhiteSpace(content))
                        onToken?.Invoke(content);
                }
            }
            else
            {
                Debug.LogError($"[LLM API Error]: {request.error}");
                onStatus?.Invoke($"网络请求失败（{request.error}），已切换为本地演示叙事。");
                onToken?.Invoke(BuildDemoNarrative(messages));
            }
        }

        onComplete?.Invoke();
    }

    private IEnumerator NonStreamRequestRoutine(Message[] messages, Action<string> onComplete, Action<string> onStatus)
    {
        ReloadConfig();

        if (!HasUsableRemoteConfig)
        {
            onStatus?.Invoke("未检测到可用模型配置，本次请求改走本地规则兜底。");
            onComplete?.Invoke(null);
            yield break;
        }

        var payload = new RequestPayload
        {
            model = _config.model,
            messages = messages,
            stream = false,
            max_tokens = 256
        };

        string json = JsonMapper.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest(_config.baseUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Clamp(_config.timeoutSeconds, 5, 120);

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", BuildAuthorizationHeader(_config.apiKey));

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(ExtractNonStreamContent(request.downloadHandler.text));
            }
            else
            {
                Debug.LogError($"[LLM API Error]: {request.error}");
                onStatus?.Invoke($"网络请求失败（{request.error}），本次请求已回退。");
                onComplete?.Invoke(null);
            }
        }
    }

    private void ProcessStreamData(string dataChunk, Action<string> onToken)
    {
        string[] lines = dataChunk.Split('\n');
        foreach (var line in lines)
        {
            string cleanLine = line.Trim();
            if (string.IsNullOrEmpty(cleanLine) || !cleanLine.StartsWith("data:", StringComparison.Ordinal))
                continue;

            string jsonStr = cleanLine.Substring("data:".Length).Trim();
            if (jsonStr == "[DONE]")
                return;

            try
            {
                JsonData data = JsonMapper.ToObject(jsonStr);
                if (data == null)
                    continue;

                if (data.IsObject && data.Keys.Contains("choices") && data["choices"] != null && data["choices"].Count > 0)
                {
                    JsonData choice = data["choices"][0];
                    if (choice != null && choice.IsObject && choice.Keys.Contains("delta"))
                    {
                        JsonData delta = choice["delta"];
                        if (delta != null && delta.IsObject && delta.Keys.Contains("content"))
                        {
                            string content = (string)delta["content"];
                            if (!string.IsNullOrEmpty(content))
                                onToken?.Invoke(content);
                        }
                    }
                }
            }
            catch
            {
                // 流式数据可能被切断，等待下一段补全。
            }
        }
    }

    private static string BuildAuthorizationHeader(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return string.Empty;

        return apiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? apiKey
            : $"Bearer {apiKey}";
    }

    private LLMConfig LoadConfig()
    {
        string configPath = Path.Combine(Application.streamingAssetsPath, ConfigFileName);
        if (File.Exists(configPath))
        {
            try
            {
                return JsonUtility.FromJson<LLMConfig>(File.ReadAllText(configPath));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[LLM] 读取本地配置失败: {exception.Message}");
            }
        }

        TextAsset fallbackAsset = Resources.Load<TextAsset>(DefaultConfigResourcePath);
        if (fallbackAsset != null && !string.IsNullOrWhiteSpace(fallbackAsset.text))
        {
            try
            {
                return JsonUtility.FromJson<LLMConfig>(fallbackAsset.text);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[LLM] 读取默认配置失败: {exception.Message}");
            }
        }

        return new LLMConfig();
    }

    private string ExtractNonStreamContent(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        try
        {
            JsonData data = JsonMapper.ToObject(responseText);
            if (data != null && data.Keys.Contains("choices") && data["choices"].Count > 0)
            {
                return (string)data["choices"][0]["message"]["content"];
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[LLM] 解析响应失败: {exception.Message}");
        }

        return null;
    }

    private string BuildDemoNarrative(Message[] messages)
    {
        string userPrompt = ExtractLatestUserContent(messages);
        string playerInput = ExtractSection(userPrompt, "=== 👤 玩家原始输入 ===");
        string logicResult = ExtractSection(userPrompt, "=== ⚖️ 本地逻辑裁决 ===");
        string knowledgeSection = ExtractSection(userPrompt, "=== 📚 GraphRAG-Lite 知识上下文 ===");
        string location = ExtractJsonField(userPrompt, "locationName");

        string knowledgeLine = BuildKnowledgeLine(knowledgeSection);
        string logicLine = BuildLogicLine(logicResult);
        string place = SanitizeVisibleText(string.IsNullOrWhiteSpace(location) ? "此地" : location);
        string safeInput = SanitizeVisibleText(playerInput);

        var builder = new StringBuilder();
        builder.Append($"{place}间，潮湿的山风裹着薄雾缓缓游走。");

        if (!string.IsNullOrWhiteSpace(safeInput))
            builder.Append($"你依着本能尝试{safeInput}，");
        else
            builder.Append("你屏住气息，顺着眼前的气味与风声摸索，");

        builder.Append(logicLine);

        if (!string.IsNullOrWhiteSpace(knowledgeLine))
            builder.Append(knowledgeLine);

        builder.Append("周围的一切都没有立刻给出答案，只把更深的异样感缓缓推到你面前。");
        return SanitizeVisibleText(builder.ToString());
    }

    private static string ExtractLatestUserContent(Message[] messages)
    {
        if (messages == null)
            return string.Empty;

        for (int i = messages.Length - 1; i >= 0; i--)
        {
            if (messages[i] != null && messages[i].role == "user")
                return messages[i].content ?? string.Empty;
        }

        return string.Empty;
    }

    private static string ExtractSection(string source, string marker)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(marker))
            return string.Empty;

        int startIndex = source.IndexOf(marker, StringComparison.Ordinal);
        if (startIndex < 0)
            return string.Empty;

        startIndex += marker.Length;
        string tail = source.Substring(startIndex).Trim();
        int nextSectionIndex = tail.IndexOf("\n===", StringComparison.Ordinal);
        return nextSectionIndex >= 0 ? tail.Substring(0, nextSectionIndex).Trim() : tail.Trim();
    }

    private static string ExtractJsonField(string source, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(fieldName))
            return string.Empty;

        Match match = Regex.Match(source, $"\"{fieldName}\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string BuildKnowledgeLine(string knowledgeSection)
    {
        if (string.IsNullOrWhiteSpace(knowledgeSection) || knowledgeSection.Contains("未命中直接相关条目"))
            return string.Empty;

        MatchCollection matches = Regex.Matches(knowledgeSection, @"◆\s*([^\s（]+)");
        if (matches.Count == 0)
            return string.Empty;

        var names = new StringBuilder();
        for (int i = 0; i < matches.Count && i < 2; i++)
        {
            if (i > 0)
                names.Append('、');
            names.Append(matches[i].Groups[1].Value);
        }

        return SanitizeVisibleText($"脑海里忽然浮起关于{names}的山海旧闻，像是在提醒你眼前这一步并不是毫无来历的莽撞。");
    }

    private static string BuildLogicLine(string logicResult)
    {
        if (string.IsNullOrWhiteSpace(logicResult))
            return "动作暂时没有触发额外数值变化，只有呼吸、脚步与视线在缓慢推进。";

        if (logicResult.Contains("观察"))
            return "你的注意力被迫放得更细，岩缝、水汽和树影都像在向你吐露某种迟缓的讯号。";
        if (logicResult.Contains("移动"))
            return "脚下的石土与潮气不断试探你的重心，每一步都像是在雾里重新确认方向。";
        if (logicResult.Contains("灵力消耗"))
            return "灵力被牵动时，胸腔深处像是有微热的线被生生拽走，呼吸也跟着发紧。";

        return "局势依旧沿着本地裁决的结果推进，身体先于念头感到了那一点细微的变化。";
    }

    public static string SanitizeVisibleText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        string sanitized = rawText;
        sanitized = Regex.Replace(sanitized, @"<CMD>.*?</CMD>", string.Empty, RegexOptions.Singleline);
        sanitized = Regex.Replace(sanitized, @"```[\s\S]*?```", string.Empty, RegexOptions.Singleline);
        sanitized = Regex.Replace(sanitized, @"(?m)^\s*(===|---).*$", string.Empty);
        sanitized = Regex.Replace(sanitized, @"(?i)\b(data|json|delta|message|choices|content)\b\s*[:=]\s*.*", string.Empty);
        sanitized = Regex.Replace(sanitized, @"[A-Fa-f0-9]{32,}", string.Empty);
        sanitized = Regex.Replace(sanitized, @"[A-Za-z0-9+/=_-]{48,}", string.Empty);
        sanitized = Regex.Replace(sanitized, @"\n{3,}", "\n\n");
        return sanitized.Trim();
    }
}
