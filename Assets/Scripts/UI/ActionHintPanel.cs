using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LitJson;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ActionHintPanel : BasePanel
{
    [Header("需要在 Inspector 中绑定")]
    public Button[] actionBtns;
    public TMP_Text[] actionTexts;
    public Button closeBtn;
    public GameObject loadingObj;

    public override void Init()
    {
        if (closeBtn != null)
        {
            closeBtn.onClick.RemoveAllListeners();
            closeBtn.onClick.AddListener(() =>
            {
                AudioMgr.Instance?.PlayClickSfx();
                HideMe();
            });
        }

        for (int i = 0; i < actionBtns.Length; i++)
        {
            int index = i;
            actionBtns[i].onClick.RemoveAllListeners();
            actionBtns[i].onClick.AddListener(() => ApplyHint(actionTexts[index].text));
        }
    }

    protected override void OnShowAnimation()
    {
        base.OnShowAnimation();
        StartCoroutine(RequestHints());
    }

    private IEnumerator RequestHints()
    {
        SetLoading(true);

        var state = GameLoop.Instance != null ? GameLoop.Instance.CurrentState : null;
        var envState = GameLoop.Instance != null ? GameLoop.Instance.CurrentEnvironment : null;
        string knowledgeSeed = $"{envState?.locationName} {envState?.narrativeHint} {string.Join(" ", state?.equipment?.inventory ?? new List<string>())}";
        string knowledgeContext = Logic.GraphRAG.GraphRAGManager.Instance.BuildKnowledgeContext(knowledgeSeed);
        string prompt = PromptBuilder.BuildHintPrompt(state, envState, knowledgeContext);

        string response = null;
        string statusMessage = null;
        bool finished = false;

        LLMService.Instance.PostNonStream(
            "你是严格输出 JSON 数组的行动建议器。",
            prompt,
            onComplete: result =>
            {
                response = result;
                finished = true;
            },
            onStatus: message => statusMessage = message);

        while (!finished)
            yield return null;

        bool parsed = TryParseHintArray(response, out var parsedHints);
        List<string> hints = parsed
            ? parsedHints
            : BuildLocalFallbackHints(state, envState);

        if (!string.IsNullOrWhiteSpace(statusMessage) || !parsed)
            NotifyMainPanel(statusMessage ?? "智能建议暂不可用，已切换为本地规则建议。");

        ApplyHintsToButtons(hints);
        SetLoading(false);
    }

    public static bool TryParseHintArray(string rawText, out List<string> hints)
    {
        hints = new List<string>();
        if (string.IsNullOrWhiteSpace(rawText))
            return false;

        try
        {
            JsonData data = JsonMapper.ToObject(rawText.Trim());
            if (data == null || !data.IsArray || data.Count != 3)
                return false;

            for (int i = 0; i < data.Count; i++)
            {
                if (!data[i].IsString)
                    return false;

                string hint = ((string)data[i]).Trim();
                if (string.IsNullOrWhiteSpace(hint))
                    return false;

                hints.Add(hint);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static List<string> BuildLocalFallbackHints(StateData.Role.RoleState state, StateData.Environment.EnvironmentState envState)
    {
        var hints = new List<string>();
        envState ??= StateData.Environment.EnvironmentState.GetDefault();

        if (state?.attributes != null &&
            state.attributes.currentHealth <= Mathf.Max(35, state.attributes.maxHealth / 3) &&
            state.equipment?.inventory != null &&
            state.equipment.inventory.Any(item => item.Contains("药")))
        {
            hints.Add("使用治疗药水");
        }

        if (envState.isFoggy)
        {
            if (Logic.GraphRAG.GraphRAGManager.Instance.IsEntityDiscovered("herb_migu"))
                hints.Add("借迷谷辨路");
            hints.Add("向雾外试探前进");
        }

        if (!string.IsNullOrWhiteSpace(envState.locationName) && envState.locationName.Contains("招摇山"))
        {
            hints.Add("观察山壁草木");
            hints.Add("采集祝余");
        }

        if (envState.HasClue("deep_path_opened"))
            hints.Add("观察异光来源");

        if (envState.HasClue("aberration_triggered") || envState.HasTag("异象迫近"))
            hints.Add("留意异兽踪迹");

        if (state?.attributes != null && state.attributes.currentMana <= state.attributes.maxMana / 2)
            hints.Add("调息恢复灵力");

        if (hints.Count < 3)
            hints.Add("留意异兽踪迹");
        if (hints.Count < 3)
            hints.Add("查看随身行囊");
        if (hints.Count < 3)
            hints.Add("辨认前方地势");

        return hints
            .Where(hint => !string.IsNullOrWhiteSpace(hint))
            .Distinct()
            .Take(3)
            .ToList();
    }

    private void ApplyHintsToButtons(List<string> hints)
    {
        for (int i = 0; i < actionBtns.Length; i++)
        {
            bool hasHint = hints != null && i < hints.Count;
            if (actionBtns[i] != null)
                actionBtns[i].gameObject.SetActive(hasHint);

            if (hasHint && actionTexts != null && i < actionTexts.Length && actionTexts[i] != null)
                actionTexts[i].text = hints[i];
        }
    }

    private void SetLoading(bool isLoading)
    {
        if (loadingObj != null)
            loadingObj.SetActive(isLoading);

        if (!isLoading || actionBtns == null)
            return;

        foreach (var button in actionBtns)
        {
            if (button != null)
                button.gameObject.SetActive(false);
        }
    }

    private void ApplyHint(string hint)
    {
        AudioMgr.Instance?.PlayClickSfx();

        var mainPanel = UIMgr.Instance.GetPanel<MainGamePanel>() ?? FindObjectOfType<MainGamePanel>();
        if (mainPanel == null)
        {
            Debug.LogError("场景里找不到 MainGamePanel，无法发送建议行动。");
            HideMe();
            return;
        }

        if (mainPanel.inputField != null)
            mainPanel.inputField.text = hint;

        if (mainPanel.sendButton != null)
        {
            Debug.Log($"发送建议行动: {hint}");
            mainPanel.sendButton.onClick.Invoke();
        }
        else
        {
            Debug.LogError("MainGamePanel 的 sendButton 未绑定，无法发送建议行动。");
        }

        HideMe();
    }

    private void NotifyMainPanel(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var mainPanel = UIMgr.Instance.GetPanel<MainGamePanel>() ?? FindObjectOfType<MainGamePanel>();
        if (mainPanel != null)
            mainPanel.AppendText($"<color=#C58F2B>【建议系统】{message}</color>", false);
    }
}
