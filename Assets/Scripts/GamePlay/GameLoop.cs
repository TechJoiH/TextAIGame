using System.Text;
using UnityEngine;
using StateData.Role;
using StateData.Environment;
using Logic.Intent;
using Logic.Memory;

public class GameLoop : MonoBehaviour
{
    public static GameLoop Instance { get; private set; }

    public MainGamePanel gamePanel;

    private RoleState playerState;
    private EnvironmentState envState;

    public RoleState CurrentState => playerState;
    public EnvironmentState CurrentEnvironment => envState;

    private bool _gameInited;
    
    // 是否启用 LLM 后备意图识别（可在设置中调整）
    [SerializeField] private bool enableLLMIntentFallback = true;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayBGM();

        UIMgr.Instance.ShowPanel<BeginPanel>();
    }

    public void StartNewGame()
    {
        if (_gameInited) return;
        _gameInited = true;

        MemoryManager.Instance.ClearAll();
        InitGame();
    }

    public void ReturnToBegin()
    {
        if (gamePanel != null)
            gamePanel.OnPlayerInput -= HandlePlayerInput;

        gamePanel = null;
        playerState = null;
        envState = null;
        _gameInited = false;
    }

    private void InitGame()
    {
        // 初始化角色状态
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
        playerState.equipment.inventory = new System.Collections.Generic.List<string> { "治疗药水", "火折" };
        playerState.equipment.equippedSkills = new System.Collections.Generic.List<string> { "火球术", "御风诀" };

        // 初始化环境状态
        envState = new EnvironmentState
        {
            locationId = "weishan_entrance",
            locationName = "危摇山·山脚",
            biome = "山脉",
            weather = "Foggy",
            timeOfDay = "Dawn",
            narrativeHint = "晨雾缭绕，古松苍翠",
            isWet = false,
            isDark = false,
            isFoggy = true,
            isWindy = false
        };

        gamePanel = UIMgr.Instance.ShowPanel<MainGamePanel>();
        if (gamePanel == null) return;

        gamePanel.Init();
        gamePanel.UpdateStateDisplay(playerState);
        gamePanel.OnPlayerInput -= HandlePlayerInput;
        gamePanel.OnPlayerInput += HandlePlayerInput;
        
        string openingText = "你缓缓睁眼。四周是危摇山的断崖残壁，雾气浓稠如乳，裹挟着腐朽草木的气息...";
        gamePanel.AppendText(openingText, false);
        
        MemoryManager.Instance.AddAssistantMessage(openingText);
    }

    public void LoadGame(RoleState newState, EnvironmentState newEnvState = null, MemorySnapshot memorySnapshot = null)
    {
        if (newState == null) return;

        playerState = newState;
        envState = newEnvState ?? EnvironmentState.GetDefault();
        
        if (memorySnapshot != null)
        {
            MemoryManager.Instance.RestoreFromSnapshot(memorySnapshot);
        }

        if (gamePanel != null)
        {
            gamePanel.UpdateStateDisplay(playerState);
            gamePanel.AppendText($"\n<color=yellow>【系统】已恢复至存档点。</color>", false);
        }
    }

    private void HandlePlayerInput(string input)
    {
        gamePanel.AppendText($"{input}", true);
        MemoryManager.Instance.AddUserMessage(input);

        // 根据设置选择同步或异步意图识别
        if (enableLLMIntentFallback)
        {
            HandlePlayerInputAsync(input);
        }
        else
        {
            HandlePlayerInputSync(input);
        }
    }

    /// <summary>
    /// 同步处理（仅本地规则引擎）
    /// </summary>
    private void HandlePlayerInputSync(string input)
    {
        var intent = IntentRecognizer.Instance.Recognize(input);
        ProcessActionWithIntent(input, intent);
    }

    /// <summary>
    /// 异步处理（支持 LLM 后备）
    /// </summary>
    private void HandlePlayerInputAsync(string input)
    {
        // 显示思考中状态
        gamePanel.ShowLoading(true);

        IntentRecognizer.Instance.RecognizeAsync(input, (intent) =>
        {
            gamePanel.ShowLoading(false);
            ProcessActionWithIntent(input, intent);
        });
    }

    /// <summary>
    /// 处理已识别意图的行动
    /// </summary>
    private void ProcessActionWithIntent(string input, IntentResult intent)
    {
        // 1. 合法性校验（含环境因素）
        if (!IARProcessor.Instance.CheckActionValidity(input, playerState, envState, out string failReason, out IntentResult _))
        {
            // 使用传入的 intent 而非校验返回的
            gamePanel.AppendText($"<color=#FF6666>{failReason}</color>", false);
            gamePanel.UpdateStateDisplay(playerState);
            return;
        }

        // 2. 执行确定性逻辑
        string logicResult = IARProcessor.Instance.ExecuteDeterministicLogic(input, playerState, envState, intent);
        gamePanel.UpdateStateDisplay(playerState);

        // 3. 构建提示词（含环境状态）
        string systemPrompt = PromptBuilder.BuildSystemPrompt();
        string userPrompt = PromptBuilder.BuildUserPromptWithIntent(input, playerState, envState, logicResult, intent);
        
        var messages = MemoryManager.Instance.BuildMessagesWithMemory(systemPrompt, userPrompt);

        gamePanel.ShowLoading(true);
        StringBuilder fullContentBuffer = new StringBuilder();

        // 4. 流式 LLM 请求
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

                if (AudioMgr.Instance != null)
                    AudioMgr.Instance.PlayPageTurnSfx();

                MemoryManager.Instance.AddAssistantMessage(rawText);
                GameSaveMgr.Instance.CreateCheckpoint(playerState, envState, input);

                Debug.Log($"回合结束。意图: {intent} | 来源: {intent.recognitionSource}");
            }
        );
    }
}