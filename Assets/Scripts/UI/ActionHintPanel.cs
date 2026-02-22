using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LitJson;
using System.Collections;
using System.Collections.Generic;

public class ActionHintPanel : BasePanel
{
    [Header("必须在 Inspector 拖拽赋值")]
    public Button[] actionBtns;      // 3个建议按钮
    public TMP_Text[] actionTexts;   // 3个按钮上的文字
    public Button closeBtn;          // 关闭按钮
    public GameObject loadingObj;    // 加载中的转圈物体

    public override void Init()
    {
        // 绑定关闭按钮
        if (closeBtn != null) closeBtn.onClick.AddListener(() => HideMe());

        // 绑定建议按钮点击事件
        for (int i = 0; i < actionBtns.Length; i++)
        {
            int index = i; // 闭包保护
            actionBtns[i].onClick.AddListener(() => {
                ApplyHint(actionTexts[index].text);
            });
        }
    }

    protected override void OnShowAnimation()
    {
        base.OnShowAnimation();
        StartCoroutine(RequestHints());
    }

    // 请求 AI 建议的核心逻辑
    private IEnumerator RequestHints()
    {
        // 1. 安全显示 Loading (加了 null 检查防止报错)
        if (loadingObj != null) loadingObj.SetActive(true);

        // 隐藏所有按钮，准备加载
        if (actionBtns != null)
        {
            foreach (var btn in actionBtns)
                if (btn != null) btn.gameObject.SetActive(false);
        }

        // ==========================================
        // 这里是模拟 AI 思考 (正式版请解开下方的 LLM 代码)
        // ==========================================

        // 临时测试：等待 1 秒
        yield return new WaitForSeconds(1f);

        // 模拟数据 (如果你接好了 LLM，请把这里替换为 LLMService 的调用)
        List<string> hints = new List<string>() { "观察周围环境", "检查自己的身体", "呼喊名字" };

        // ==========================================

        // 2. 显示结果
        for (int i = 0; i < actionBtns.Length; i++)
        {
            if (actionBtns[i] != null && i < hints.Count)
            {
                actionBtns[i].gameObject.SetActive(true);
                if (actionTexts[i] != null) actionTexts[i].text = hints[i];
            }
        }

        // 3. 关闭 Loading
        if (loadingObj != null) loadingObj.SetActive(false);
    }

    // 应用建议并发送
    private void ApplyHint(string hint)
    {
        // 尝试从 UI 管理器获取
        var mainPanel = UIMgr.Instance.GetPanel<MainGamePanel>();

        // 【关键修复】如果管理器里没找到（比如你是直接把面板拖在场景里的），
        // 就尝试直接在场景里暴力查找
        if (mainPanel == null)
        {
            mainPanel = FindObjectOfType<MainGamePanel>();
        }

        if (mainPanel != null)
        {
            // 步骤 A: 填入文字
            if (mainPanel.inputField != null)
                mainPanel.inputField.text = hint;

            // 步骤 B: 模拟点击发送按钮
            if (mainPanel.sendButton != null)
            {
                Debug.Log($"发送建议行动: {hint}");
                mainPanel.sendButton.onClick.Invoke();
            }
            else
            {
                Debug.LogError("MainGamePanel 上的 SendButton 没绑定！无法发送。");
            }
        }
        else
        {
            // 如果两种方法都找不到，那就是真的没了
            Debug.LogError("严重错误：场景里找不到 MainGamePanel！请检查它是否被激活。");
        }

        // 关闭建议面板
        HideMe();
    }
}