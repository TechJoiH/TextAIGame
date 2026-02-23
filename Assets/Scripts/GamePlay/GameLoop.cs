using System.Text;
using UnityEngine;
using StateData.Role;
using Logic.Intent;
using Logic.Memory;

public class GameLoop : MonoBehaviour
{
    public static GameLoop Instance { get; private set; }

    public MainGamePanel gamePanel;

    private RoleState playerState;

    public RoleState CurrentState => playerState;

    private bool _gameInited;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // 启动时播放 BGM
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayBGM();

        // 启动时只显示开始面板，不初始化/不加载游戏面板
        UIMgr.Instance.ShowPanel<BeginPanel>();
    }

    /// <summary>
    /// 由 BeginPanel 点击"开始"按钮触发：初始化状态并加载主游戏面板
    /// </summary>
    public void StartNewGame()
    {
        if (_gameInited) return;
        _gameInited = true;

        // 清空记忆，开始新游戏
        MemoryManager.Instance.ClearAll();
        
        InitGame();
    }

    public void ReturnToBegin()
    {
        if (gamePanel != null)
            gamePanel.OnPlayerInput -= HandlePlayerInput;

        gamePanel = null;
        playerState = null;
        _gameInited = false;
    }

    private void InitGame()
    {
        playerState = new RoleState();
        playerState.identity.name = "林渊";
        playerState.attributes.level = 1;
        playerState.attributes.currentHealth = 80;
        playerState.attributes.maxHealth = 100;
        playerState.attributes.currentMana = 50;
        playerState.attributes.maxMana = 50;
        playerState.attributes.strength = 10;
        playerState.attributes.agility = 8;
        playerState.attributes.intelligence = 12;
        playerState.attributes.expToNextLevel = 100;
        playerState.cultivation.cultivationStage = 1;
        playerState.equipment.inventory = new System.Collections.Generic.List<string> { "治疗药水", "干粮" };

        gamePanel = UIMgr.Instance.ShowPanel<MainGamePanel>();
        if (gamePanel == null) return;

        gamePanel.Init();
        gamePanel.UpdateStateDisplay(playerState);
        gamePanel.OnPlayerInput -= HandlePlayerInput;
        gamePanel.OnPlayerInput += HandlePlayerInput;
        
        string openingText = "你醒来了。这里是招摇山，空气中弥漫着腥甜的铁锈味...";
        gamePanel.AppendText(openingText, false);
        
        // 将开场白加入记忆
        MemoryManager.Instance.AddAssistantMessage(openingText);
    }

    public void LoadGame(RoleState newState, MemorySnapshot memorySnapshot = null)
    {
        if (newState == null) return;

        playerState = newState;
        
        // 恢复记忆状态
        if (memorySnapshot != null)
        {
            MemoryManager.Instance.RestoreFromSnapshot(memorySnapshot);
        }

        if (gamePanel != null)
        {
            gamePanel.UpdateStateDisplay(playerState);
            gamePanel.AppendText($"\n<color=yellow>【系统】已回溯至存档点。</color>", false);
        }
    }

    private void HandlePlayerInput(string input)
    {
        gamePanel.AppendText($"{input}", true);
        
        // 将玩家输入加入记忆
        MemoryManager.Instance.AddUserMessage(input);

        // 1. 意图识别 + 合法性校验
        if (!IARProcessor.Instance.CheckActionValidity(input, playerState, out string failReason, out IntentResult intent))
        {
            gamePanel.AppendText($"<color=#FF6666>{failReason}</color>", false);
            gamePanel.UpdateStateDisplay(playerState);
            return;
        }

        // 2. 本地确定性逻辑执行
        string logicResult = IARProcessor.Instance.ExecuteDeterministicLogic(input, playerState, intent);
        gamePanel.UpdateStateDisplay(playerState);

        // 3. 构建带记忆上下文的提示词
        string systemPrompt = PromptBuilder.BuildSystemPrompt();
        string userPrompt = PromptBuilder.BuildUserPromptWithIntent(input, playerState, logicResult, intent);
        
        // 构建包含记忆的消息数组
        var messages = MemoryManager.Instance.BuildMessagesWithMemory(systemPrompt, userPrompt);

        gamePanel.ShowLoading(true);
        StringBuilder fullContentBuffer = new StringBuilder();

        // 4. 调用云端 LLM 生成叙事（使用带记忆的消息）
        LLMService.Instance.PostStreamWithMessages(
            messages,
            onTokenReceived: (token) =>
            {
                gamePanel.AppendStreamToken(token);
                fullContentBuffer.Append(token);
            },
            onComplete: () =>
            {
                string rawText = fullContentBuffer.ToString();
                
                // 5. 解析 AI 返回的指令并应用
                IARProcessor.Instance.AnalyzeAndApplyAIResult(rawText, playerState);

                gamePanel.RemoveCmdTagsFromUI();
                gamePanel.UpdateStateDisplay(playerState);
                gamePanel.ShowLoading(false);
                gamePanel.FinishStream();

                // 播放翻页音效（AI生成文字完成后）
                if (AudioMgr.Instance != null)
                    AudioMgr.Instance.PlayPageTurnSfx();

                // 6. 将AI回复加入记忆
                MemoryManager.Instance.AddAssistantMessage(rawText);
                
                // 7. 存档（包含记忆快照）
                GameSaveMgr.Instance.CreateCheckpoint(playerState, input);

                Debug.Log($"回合结束。意图: {intent} | 记忆: {MemoryManager.Instance.GetDebugInfo()}");
            }
        );
    }
}