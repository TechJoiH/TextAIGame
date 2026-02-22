using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using StateData.Role;
using TMPro;
using System.Collections; // 协程

public class MainGamePanel : BasePanel
{
    [Header("UI Components")]
    public TMP_Text storyText;
    public ScrollRect scrollRect;
    public TMP_InputField inputField;
    public Button sendButton;
    public TMP_Text statusText;

    [Header("Top Buttons")]
    public Button historyButton;     // 打开历史存档面板
    public Button hintButton;
    public Button buttonSet;// 打开行动建议面板

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
    }

    private void OnHistoryClick()
    {
        UIMgr.Instance.ShowPanel<HistoryPanel>();
    }

    private void OnHintClick()
    {
        UIMgr.Instance.ShowPanel<ActionHintPanel>();
    }

    private void OnSendClick()
    {
        if (inputField == null) return;
        if (string.IsNullOrWhiteSpace(inputField.text)) return;

        string input = inputField.text;
        inputField.text = "";
        OnPlayerInput?.Invoke(input);
    }

    private void OnSetClick()
    {
        var panel = UIMgr.Instance.ShowPanel<SetPanel>();
        if (panel != null)
            panel.SetOpenFromGame(true);
    }

    // 【优化】添加静态文本（玩家或系统提示）
    public void AppendText(string text, bool isPlayer)
    {
        string color = isPlayer ? "#FFD700" : "#FFFFFF"; // 玩家金色，系统白色

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
        if (statusText == null || state == null) return;

        // 使用富文本让数值更显眼
        statusText.text =
            $"健康度: <color=#FF5555>{state.attributes.currentHealth}/{state.attributes.maxHealth}</color> | " +
            $"灵力: <color=#55AAFF>{state.attributes.currentMana}</color> | " +
            $"环境: <color=#FFAA00>【招摇山】</color>";
    }

    public void ShowLoading(bool isLoading)
    {
        if (sendButton != null) sendButton.interactable = !isLoading;
        if (inputField != null) inputField.interactable = !isLoading;

        // 可选：加载时禁用顶部按钮，避免并发请求/回档冲突
        if (historyButton != null) historyButton.interactable = !isLoading;
        if (hintButton != null) hintButton.interactable = !isLoading;
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