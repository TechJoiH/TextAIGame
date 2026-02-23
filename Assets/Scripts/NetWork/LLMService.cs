using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using LitJson;

/// <summary>
/// LLM 服务（NVIDIA Integrate ChatCompletions）
/// 支持流式输出和多轮对话记忆
/// </summary>
public class LLMService : MonoSingleton<LLMService>
{
    private const string API_URL = "https://integrate.api.nvidia.com/v1/chat/completions";
    private const string API_KEY = "Bearer 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";
    private const string MODEL_NAME = "meta/llama-3.1-70b-instruct";

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

    private int consecutiveFailures = 0;
    private const int MAX_FAILURES = 3;

    /// <summary>
    /// 发起流式请求（简化版，兼容旧接口）
    /// </summary>
    public void PostStream(string systemPrompt, string userPrompt, Action<string> onTokenReceived, Action onComplete)
    {
        var messages = new[]
        {
            new Message { role = "system", content = systemPrompt },
            new Message { role = "user", content = userPrompt }
        };
        
        StartCoroutine(RequestRoutine(messages, onTokenReceived, onComplete));
    }

    /// <summary>
    /// 发起流式请求（支持完整消息数组，用于多轮对话）
    /// </summary>
    public void PostStreamWithMessages(Message[] messages, Action<string> onTokenReceived, Action onComplete)
    {
        StartCoroutine(RequestRoutine(messages, onTokenReceived, onComplete));
    }

    /// <summary>
    /// 发起非流式请求（用于摘要生成等场景）
    /// </summary>
    public void PostNonStream(string systemPrompt, string userPrompt, Action<string> onComplete)
    {
        var messages = new[]
        {
            new Message { role = "system", content = systemPrompt },
            new Message { role = "user", content = userPrompt }
        };
        
        StartCoroutine(NonStreamRequestRoutine(messages, onComplete));
    }

    private IEnumerator RequestRoutine(Message[] messages, Action<string> onToken, Action onComplete)
    {
        var payload = new RequestPayload
        {
            model = MODEL_NAME,
            messages = messages,
            stream = true
        };

        string json = JsonMapper.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest(API_URL, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", API_KEY);
            request.SetRequestHeader("Accept", "text/event-stream");

            var operation = request.SendWebRequest();
            int lastDataIndex = 0;

            while (!operation.isDone)
            {
                yield return null;

                string currentText = request.downloadHandler.text;
                if (string.IsNullOrEmpty(currentText) || currentText.Length <= lastDataIndex)
                    continue;

                string newData = currentText.Substring(lastDataIndex);
                lastDataIndex = currentText.Length;

                ProcessStreamData(newData, onToken);
            }

            // 处理剩余数据
            string finalText = request.downloadHandler.text;
            if (!string.IsNullOrEmpty(finalText) && finalText.Length > lastDataIndex)
            {
                string newData = finalText.Substring(lastDataIndex);
                ProcessStreamData(newData, onToken);
            }

            // 错误处理与降级
            if (request.result != UnityWebRequest.Result.Success)
            {
                consecutiveFailures++;
                Debug.LogError($"[LLM API Error]: {request.error}");
                
                if (consecutiveFailures >= MAX_FAILURES)
                {
                    onToken?.Invoke(GetFallbackResponse());
                    consecutiveFailures = 0;
                }
                else
                {
                    onToken?.Invoke($"[连接失败，正在重试... ({consecutiveFailures}/{MAX_FAILURES})]");
                }
            }
            else
            {
                consecutiveFailures = 0;
            }
        }

        onComplete?.Invoke();
    }

    private IEnumerator NonStreamRequestRoutine(Message[] messages, Action<string> onComplete)
    {
        var payload = new RequestPayload
        {
            model = MODEL_NAME,
            messages = messages,
            stream = false,
            max_tokens = 256  // 摘要用较短的Token限制
        };

        string json = JsonMapper.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest(API_URL, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", API_KEY);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    JsonData data = JsonMapper.ToObject(request.downloadHandler.text);
                    if (data != null && data.Keys.Contains("choices") && data["choices"].Count > 0)
                    {
                        string content = (string)data["choices"][0]["message"]["content"];
                        onComplete?.Invoke(content);
                        yield break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LLM] 解析响应失败: {e.Message}");
                }
            }
            else
            {
                Debug.LogError($"[LLM API Error]: {request.error}");
            }

            onComplete?.Invoke(null);
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
                if (data == null) continue;

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
                // 格式错误或截断JSON，等待下一帧继续处理
            }
        }
    }

    private string GetFallbackResponse()
    {
        string[] fallbacks = new[]
        {
            "四周雾气弥漫，你暂时无法看清前路……（网络连接不稳定）",
            "一阵眩晕袭来，意识暂时陷入混沌……（正在重新连接）",
            "山风呼啸，似乎有什么阻断了你与天地的联系……（请稍后重试）"
        };
        return fallbacks[UnityEngine.Random.Range(0, fallbacks.Length)];
    }
}