using System.Text;
using UnityEngine;
using StateData.Role;

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
        // 启动时只显示开始面板，不初始化/不加载游戏面板
        UIMgr.Instance.ShowPanel<BeginPanel>();
    }

    /// <summary>
    /// 由 BeginPanel 点击“开始”按钮触发：初始化状态并加载主游戏面板
    /// </summary>
    public void StartNewGame()
    {
        if (_gameInited) return;
        _gameInited = true;

        InitGame();
    }

    // 新增：从游戏返回开始界面时调用
    public void ReturnToBegin()
    {
        // 解绑输入事件，避免下次复用面板时重复订阅
        if (gamePanel != null)
            gamePanel.OnPlayerInput -= HandlePlayerInput;

        gamePanel = null;
        playerState = null;
        _gameInited = false;
    }

    private void InitGame()
    {
        playerState = new RoleState();
        playerState.identity.name = "明渊";
        playerState.attributes.currentHealth = 80;
        playerState.attributes.maxHealth = 100;
        playerState.attributes.currentMana = 50;
        playerState.cultivation.cultivationStage = 1;

        gamePanel = UIMgr.Instance.ShowPanel<MainGamePanel>();
        if (gamePanel == null) return;

        gamePanel.Init();
        gamePanel.UpdateStateDisplay(playerState);
        gamePanel.OnPlayerInput -= HandlePlayerInput;
        gamePanel.OnPlayerInput += HandlePlayerInput;
        gamePanel.AppendText("你醒来了。这里是招摇山，空气中弥漫着腥甜的铁锈味...", false);

    }

    public void LoadGame(RoleState newState)
    {
        if (newState == null) return;

        playerState = newState;

        if (gamePanel != null)
        {
            gamePanel.UpdateStateDisplay(playerState);
            gamePanel.AppendText($"\n<color=yellow>【系统】已回溯至存档点。</color>", false);
        }
    }

    private void HandlePlayerInput(string input)
    {
        gamePanel.AppendText($"{input}", true);

        if (!IARProcessor.Instance.CheckActionValidity(input, playerState, out string failReason))
        {
            gamePanel.AppendText($"[系统阻断] {failReason}", false);
            return;
        }

        string logicResult = IARProcessor.Instance.ExecuteDeterministicLogic(input, playerState);

        gamePanel.UpdateStateDisplay(playerState);

        string systemPrompt = PromptBuilder.BuildSystemPrompt();
        string userPrompt = PromptBuilder.BuildUserPrompt(input, playerState, logicResult);

        gamePanel.ShowLoading(true);
        StringBuilder fullContentBuffer = new StringBuilder(); // 用于存储完整内容

        LLMService.Instance.PostStream(
            systemPrompt,
            userPrompt,
            onTokenReceived: (token) =>
            {
                gamePanel.AppendStreamToken(token);
                fullContentBuffer.Append(token);
            },
            onComplete: () =>
            {
                string rawText = fullContentBuffer.ToString();
                IARProcessor.Instance.AnalyzeAndApplyAIResult(rawText, playerState);

                gamePanel.RemoveCmdTagsFromUI();

                gamePanel.UpdateStateDisplay(playerState);
                gamePanel.ShowLoading(false);
                gamePanel.FinishStream();

                // 【新增】自动存档（Checkpoint）
                GameSaveMgr.Instance.CreateCheckpoint(playerState, input);

                Debug.Log("回合结算完毕。");
            }
        );
    }
}