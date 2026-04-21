using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using StateData.Role;
using StateData.Environment;
using Logic.Memory;
using TMPro;
using System.Collections;
using System.Linq;
using System.Text;

public class MainGamePanel : BasePanel
{
    [Header("UI Components")]
    public TMP_Text storyText;
    public ScrollRect scrollRect;
    public TMP_InputField inputField;
    public Button sendButton;
    public TMP_Text statusText;

    [Header("Top Buttons")]
    public Button historyButton;
    public Button hintButton;
    public Button buttonSet;
    public Button knowledgeGraphButton;  // 新增：知识图谱按钮

    public event UnityAction<string> OnPlayerInput;

    private string currentStoryContent = "";
    private bool inited;

    public override void Init()
    {
        if (inited) return;
        inited = true;

        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendClick);

        if (historyButton != null)
            historyButton.onClick.AddListener(OnHistoryClick);

        if (hintButton != null)
            hintButton.onClick.AddListener(OnHintClick);
        if (buttonSet != null)
            buttonSet.onClick.AddListener(OnSetClick);

        // 新增：绑定知识图谱按钮
        if (knowledgeGraphButton != null)
            knowledgeGraphButton.onClick.AddListener(OnKnowledgeGraphClick);

        ApplyBranding();
    }

    private void OnHistoryClick()
    {
        // 播放打开面板的特殊音效
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayPanelOpenSfx();

        UIMgr.Instance.ShowPanel<HistoryPanel>();
    }

    private void OnHintClick()
    {
        // 播放打开面板的特殊音效
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayPanelOpenSfx();

        UIMgr.Instance.ShowPanel<ActionHintPanel>();
    }

    /// <summary>
    /// 新增：打开知识图谱面板
    /// </summary>
    private void OnKnowledgeGraphClick()
    {
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayPanelOpenSfx();

        UIMgr.Instance.ShowPanel<KnowledgeGraphPanel>();
    }

    private void OnSendClick()
    {
        if (inputField == null) return;
        if (string.IsNullOrWhiteSpace(inputField.text)) return;

        // 播放普通点击音效
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayClickSfx();

        string input = inputField.text;
        inputField.text = "";
        OnPlayerInput?.Invoke(input);
    }

    private void OnSetClick()
    {
        // 播放打开面板的特殊音效
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayPanelOpenSfx();

        var panel = UIMgr.Instance.ShowPanel<SetPanel>();
        if (panel != null)
            panel.SetOpenFromGame(true);
    }

    // 【优化】添加静态文本（玩家或系统提示）
    public void AppendText(string text, bool isPlayer)
    {
        string color = isPlayer ? "#4E342E" : "#000000"; // 玩家深棕色，系统黑色

        // 逻辑优化：如果当前是空的，就不加换行；否则在前面加一个换行，而不是前后都加
        string prefix = string.IsNullOrEmpty(currentStoryContent) ? "" : "\n";

        // 组合文本：加粗玩家发言，增加区分度
        if (isPlayer)
        {
            currentStoryContent += $"{prefix}<color={color}><b> {text}</b></color>\n";
        }
        else
        {
            currentStoryContent += $"{prefix}<color={color}>{text}</color>";
        }

        UpdateUIText();
    }

    // 【流式】追加 Token
    public void AppendStreamToken(string token)
    {
        currentStoryContent += token;
        UpdateUIText();
    }

    // 【流式】结束
    public void FinishStream()
    {
        // 流式结束后，额外追加一个换行，为下一轮对话留出空间
        currentStoryContent += "\n";
        UpdateUIText();
    }

    // 【清洗】移除 CMD 指令
    public void RemoveCmdTagsFromUI()
    {
        if (storyText == null) return;

        string pattern = @"<CMD>.*?</CMD>";
        currentStoryContent = System.Text.RegularExpressions.Regex.Replace(
            currentStoryContent,
            pattern,
            "",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        UpdateUIText();
    }

    public void UpdateStateDisplay(RoleState state)
    {
        UpdateStateDisplay(state, GameLoop.Instance != null ? GameLoop.Instance.CurrentEnvironment : null);
    }

    public void UpdateStateDisplay(RoleState state, EnvironmentState environmentState)
    {
        if (statusText == null || state == null) return;

        environmentState ??= EnvironmentState.GetDefault();
        environmentState.EnsureCollections();

        string tagPreview = environmentState.dynamicTags != null && environmentState.dynamicTags.Count > 0
            ? string.Join(" / ", environmentState.dynamicTags.Take(4))
            : "暂无";
        string objectiveText = string.IsNullOrWhiteSpace(environmentState.currentObjective)
            ? "自由探索"
            : environmentState.currentObjective;

        statusText.text =
            $"健康度: <color=#FF5555>{state.attributes.currentHealth}/{state.attributes.maxHealth}</color> | " +
            $"灵力: <color=#55AAFF>{state.attributes.currentMana}/{state.attributes.maxMana}</color> | " +
            $"境地: <color=#FFAA00>【{environmentState.locationName}】</color>\n" +
            $"天候: <color=#9EC8FF>{GetDisplayWeatherText(environmentState.weather)}</color> | " +
            $"时辰: <color=#B6E0FE>{GetDisplayTimeText(environmentState.timeOfDay)}</color>\n" +
            $"目标: <color=#D9B16A>{objectiveText}</color>\n" +
            $"标签: <color=#8E6B3F>{tagPreview}</color>";
    }

    public void ShowLoading(bool isLoading)
    {
        if (sendButton != null) sendButton.interactable = !isLoading;
        if (inputField != null) inputField.interactable = !isLoading;

        if (historyButton != null) historyButton.interactable = !isLoading;
        if (hintButton != null) hintButton.interactable = !isLoading;
        if (knowledgeGraphButton != null) knowledgeGraphButton.interactable = !isLoading;  // 新增
    }

    private void UpdateUIText()
    {
        if (storyText != null)
        {
            storyText.text = currentStoryContent;

            // 关键：不要直接调用 ScrollToBottom，因为 TMP 的网格更新是滞后的
            // 必须开启协程等待这一帧渲染完毕再滚，否则滚不到最底下
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(ScrollToBottomCoroutine());
            }
        }
    }

    public void ClearStory()
    {
        currentStoryContent = string.Empty;
        UpdateUIText();
    }

    public void ReplaceLastStreamContent(string original, string replacement)
    {
        if (string.IsNullOrEmpty(original) || replacement == null || string.IsNullOrEmpty(currentStoryContent))
            return;

        int index = currentStoryContent.LastIndexOf(original, System.StringComparison.Ordinal);
        if (index < 0)
            return;

        currentStoryContent =
            currentStoryContent.Substring(0, index) +
            replacement +
            currentStoryContent.Substring(index + original.Length);

        UpdateUIText();
    }

    public void RestoreStoryFromMemory(MemorySnapshot snapshot, string systemNotice = null)
    {
        var builder = new StringBuilder();

        if (snapshot?.longTermMemories != null && snapshot.longTermMemories.Count > 0)
        {
            builder.AppendLine("<color=#B79253>【已恢复记忆摘要】</color>");
            foreach (var memory in snapshot.longTermMemories)
            {
                builder.AppendLine($"<color=#7A5F2A>{memory.summary}</color>");
            }
            builder.AppendLine();
        }

        if (snapshot?.shortTermMemory != null)
        {
            foreach (var entry in snapshot.shortTermMemory)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.content))
                    continue;

                string prefix = builder.Length == 0 ? "" : "\n";
                if (entry.role == "user")
                    builder.Append($"{prefix}<color=#4E342E><b> {entry.content}</b></color>\n");
                else
                    builder.Append($"{prefix}<color=#000000>{entry.content}</color>");
            }
        }

        if (!string.IsNullOrWhiteSpace(systemNotice))
        {
            string prefix = builder.Length == 0 ? "" : "\n";
            builder.Append($"{prefix}<color=#C58F2B>{systemNotice}</color>");
        }

        currentStoryContent = builder.ToString();
        UpdateUIText();
    }

    public void ApplyBranding()
    {
        SetButtonLabel(historyButton, "记录");
        SetButtonLabel(hintButton, "推荐");
        SetButtonLabel(knowledgeGraphButton, "知识图谱");
        SetButtonLabel(buttonSet, "设置");
        SetButtonLabel(sendButton, "送出行动");

        if (inputField != null && inputField.placeholder is TMP_Text placeholder)
        {
            placeholder.text = "输入行动，例如：观察招摇山雾中的草木";
        }
    }

    private static void SetButtonLabel(Button button, string text)
    {
        if (button == null || string.IsNullOrWhiteSpace(text))
            return;

        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = text;
    }

    private static string GetDisplayTimeText(string rawTime)
    {
        return rawTime switch
        {
            "Dawn" => "清晨",
            "Day" => "白昼",
            "Dusk" => "傍晚",
            "Night" => "夜晚",
            _ => string.IsNullOrWhiteSpace(rawTime) ? "未知" : rawTime
        };
    }

    private static string GetDisplayWeatherText(string rawWeather)
    {
        return rawWeather switch
        {
            "Clear" => "晴朗",
            "Foggy" => "迷雾",
            "Rainy" => "雨幕",
            "Stormy" => "狂风",
            _ => string.IsNullOrWhiteSpace(rawWeather) ? "未知" : rawWeather
        };
    }

    private IEnumerator ScrollToBottomCoroutine()
    {
        // 等待当前帧的 UI 布局重建完成
        yield return new WaitForEndOfFrame();

        if (scrollRect != null)
        {
            // 0 代表底部，1 代表顶部
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
