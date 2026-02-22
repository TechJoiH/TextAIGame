using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using LitJson; 

/// <summary>
/// LLM 服务（NVIDIA Integrate ChatCompletions）
/// MVP：UnityWebRequest + 轮询 downloadHandler.text 实现简易 SSE 流式解析。
/// </summary>
public class LLMService : MonoSingleton<LLMService>
{
    // 【修改点】NVIDIA API 配置
    // 注意：这是 NVIDIA 的通用入口，具体模型由 payload 里的 model 字段决定
    private const string API_URL = "https://integrate.api.nvidia.com/v1/chat/completions";

    // 【重要】请在这里填入你从 NVIDIA 官网申请的 "nvapi-" 开头的 Key
    // 例：Bearer nvapi-xxxxxxxx...
    private const string API_KEY = "Bearer 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";

    // 【修改点】模型名称，请确保和 NVIDIA 页面上显示的完全一致
    private const string MODEL_NAME = "meta/llama-3.1-70b-instruct";

    public class RequestPayload
    {
        public string model;
        public Message[] messages;
        public bool stream = true;
        public double temperature = 0.7;
        public int max_tokens = 1024;
    }

    public class Message
    {
        public string role;
        public string content;
    }

    /// <summary>
    /// 发送流式请求 (NVIDIA API)
    /// </summary>
    public void PostStream(string systemPrompt, string userPrompt, Action<string> onTokenReceived, Action onComplete)
    {
        StartCoroutine(IRequestRoutine(systemPrompt, userPrompt, onTokenReceived, onComplete));
    }

    private IEnumerator IRequestRoutine(string system, string user, Action<string> onToken, Action onComplete)
    {
        var messages = new[]
        {
            new Message { role = "system", content = system },
            new Message { role = "user", content = user }
        };

        var payload = new RequestPayload
        {
            model = MODEL_NAME,
            messages = messages,
            stream = true
        };

        string json = JsonMapper.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        var request = new UnityWebRequest(API_URL, UnityWebRequest.kHttpVerbPOST);
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

        // 结束后再扫一次尾巴（防止最后一包没被 while 捕获）
        string finalText = request.downloadHandler.text;
        if (!string.IsNullOrEmpty(finalText) && finalText.Length > lastDataIndex)
        {
            string newData = finalText.Substring(lastDataIndex);
            ProcessStreamData(newData, onToken);
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[NVIDIA API Error]: {request.error}\nResponse: {request.downloadHandler.text}");
            onToken?.Invoke($"[连接失败: {request.error}]");
        }

        onComplete?.Invoke();
        request.Dispose();
    }

    // 解析 SSE 格式数据 (data: {...})
    private void ProcessStreamData(string dataChunk, Action<string> onToken)
    {
        string[] lines = dataChunk.Split('\n');
        foreach (var line in lines)
        {
            string cleanLine = line.Trim();
            if (string.IsNullOrEmpty(cleanLine))
                continue;

            if (!cleanLine.StartsWith("data:", StringComparison.Ordinal))
                continue;

            string jsonStr = cleanLine.Substring("data:".Length).Trim();

            if (jsonStr == "[DONE]")
                return;

            try
            {
                JsonData data = JsonMapper.ToObject(jsonStr);
                if (data == null) continue;

                // 兼容 OpenAI/NVIDIA SSE：choices[0].delta.content
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
                // 流式传输可能截断 JSON：忽略该帧解析错误
            }
        }
    }
}