using System;
using System.Text;
using Data.KnowledgeGraph;
using Logic.GraphRAG;
using Logic.Intent;
using Logic.Memory;
using StateData.Environment;
using StateData.Role;
using UnityEngine;

public class GameLoop : MonoBehaviour
{
    private const string ScenarioResourcePath = "Configs/ZhaoYaoShanScenario";

    public static GameLoop Instance { get; private set; }

    public MainGamePanel gamePanel;

    private RoleState playerState;
    private EnvironmentState envState;
    private ScenarioConfig activeScenario;
    private bool _gameInited;

    [Header("可选配置资源")]
    [SerializeField] private TextAsset scenarioConfigJson;
    [SerializeField] private bool enableLLMIntentFallback = true;

    private const string PrefMusicOn = "SET_MUSIC_ON";
    private const string PrefSoundOn = "SET_SOUND_ON";
    private const string PrefMusicVol = "SET_MUSIC_VOL";
    private const string PrefSoundVol = "SET_SOUND_VOL";

    public RoleState CurrentState => playerState;
    public EnvironmentState CurrentEnvironment => envState;
    public ScenarioConfig CurrentScenario => activeScenario;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        InitAudioSettings();

        if (AudioMgr.Instance != null)
            AudioMgr.Instance.PlayBGM();

        UIMgr.Instance.ShowPanel<BeginPanel>();
    }

    private void OnDestroy()
    {
        DetachGamePanelCallbacks();
        EventCenter.Instance.RemoveListener<KnowledgeEntity>("OnKnowledgeDiscovered", OnEntityDiscovered);
    }

    private void InitAudioSettings()
    {
        bool musicOn = PlayerPrefs.GetInt(PrefMusicOn, 1) == 1;
        bool soundOn = PlayerPrefs.GetInt(PrefSoundOn, 1) == 1;
        float musicVol = PlayerPrefs.GetFloat(PrefMusicVol, 1f);
        float soundVol = PlayerPrefs.GetFloat(PrefSoundVol, 1f);

        if (AudioMgr.Instance != null)
            AudioMgr.Instance.InitData(musicOn, soundOn, musicVol, soundVol);
    }

    public void StartNewGame()
    {
        if (_gameInited)
            return;

        _gameInited = true;
        MemoryManager.Instance.ClearAll();

        if (GraphRAGManager.Instance.TotalCount == 0)
            GraphRAGManager.Instance.InitializeKnowledgeLibrary();

        IntentRecognizer.Instance.ClearCache();
        InitGame();
    }

    public void ReturnToBegin()
    {
        DetachGamePanelCallbacks();
        EventCenter.Instance.RemoveListener<KnowledgeEntity>("OnKnowledgeDiscovered", OnEntityDiscovered);

        gamePanel = null;
        playerState = null;
        envState = null;
        activeScenario = null;
        _gameInited = false;
    }

    public void LoadGame(
        RoleState newState,
        EnvironmentState newEnvState = null,
        MemorySnapshot memorySnapshot = null,
        KnowledgeGraphSnapshot knowledgeGraphSnapshot = null)
    {
        if (newState == null)
            return;

        _gameInited = true;
        activeScenario ??= LoadScenarioConfig();
        playerState = newState;
        envState = newEnvState ?? LoadEnvironmentState(activeScenario);
        envState?.EnsureCollections();

        if (memorySnapshot != null)
            MemoryManager.Instance.RestoreFromSnapshot(memorySnapshot);
        else
            MemoryManager.Instance.ClearAll();

        GraphRAGManager.Instance.RestoreFromSnapshot(knowledgeGraphSnapshot);

        EnsureGamePanelReady();
        gamePanel.UpdateStateDisplay(playerState, envState);
        gamePanel.RestoreStoryFromMemory(memorySnapshot, "【系统】已恢复至该回合的角色、环境、记忆与知识图谱状态。");
    }

    private void InitGame()
    {
        activeScenario = LoadScenarioConfig();
        playerState = activeScenario.BuildRoleState();
        envState = LoadEnvironmentState(activeScenario);
        envState?.EnsureCollections();

        GraphRAGManager.Instance.ResetDiscoveredState();
        if (activeScenario.initialDiscoveredEntityIds != null)
        {
            foreach (var entityId in activeScenario.initialDiscoveredEntityIds)
                GraphRAGManager.Instance.DiscoverEntity(entityId);
        }

        EnsureGamePanelReady();
        gamePanel.ClearStory();
        gamePanel.UpdateStateDisplay(playerState, envState);
        gamePanel.AppendText(activeScenario.openingNarration, false);
        if (!string.IsNullOrWhiteSpace(activeScenario.openingNotice))
            gamePanel.AppendText($"<color=#C58F2B>【演示目标】{activeScenario.openingNotice}</color>", false);

        MemoryManager.Instance.AddAssistantMessage(activeScenario.openingNarration);
        if (!string.IsNullOrWhiteSpace(activeScenario.openingNotice))
            MemoryManager.Instance.AddAssistantMessage(activeScenario.openingNotice);
    }

    private void EnsureGamePanelReady()
    {
        gamePanel = UIMgr.Instance.ShowPanel<MainGamePanel>();
        if (gamePanel == null)
            return;

        gamePanel.Init();
        gamePanel.ApplyBranding();

        gamePanel.OnPlayerInput -= HandlePlayerInput;
        gamePanel.OnPlayerInput += HandlePlayerInput;

        EventCenter.Instance.RemoveListener<KnowledgeEntity>("OnKnowledgeDiscovered", OnEntityDiscovered);
        EventCenter.Instance.AddListener<KnowledgeEntity>("OnKnowledgeDiscovered", OnEntityDiscovered);
    }

    private void DetachGamePanelCallbacks()
    {
        if (gamePanel != null)
            gamePanel.OnPlayerInput -= HandlePlayerInput;
    }

    private ScenarioConfig LoadScenarioConfig()
    {
        ScenarioConfig scenario = null;
        TextAsset asset = scenarioConfigJson != null ? scenarioConfigJson : Resources.Load<TextAsset>(ScenarioResourcePath);

        if (asset != null && !string.IsNullOrWhiteSpace(asset.text))
        {
            try
            {
                scenario = JsonUtility.FromJson<ScenarioConfig>(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[GameLoop] 解析 ScenarioConfig 失败: {exception.Message}");
            }
        }

        scenario ??= ScenarioConfig.GetDefault();
        scenario.EnsureDefaults();
        return scenario;
    }

    private EnvironmentState LoadEnvironmentState(ScenarioConfig scenario)
    {
        string resourcePath = scenario != null ? scenario.environmentResourcePath : null;
        EnvironmentData data = !string.IsNullOrWhiteSpace(resourcePath)
            ? Resources.Load<EnvironmentData>(resourcePath)
            : null;

        if (data == null)
        {
            Debug.LogWarning($"[GameLoop] 未找到环境资源 {resourcePath}，使用默认环境状态。");
            return EnvironmentState.GetDefault();
        }

        return EnvironmentState.FromData(data);
    }

    private void OnEntityDiscovered(KnowledgeEntity entity)
    {
        if (gamePanel == null || entity == null)
            return;

        gamePanel.AppendText($"<color=#FFD700>【知识解锁】发现了「{entity.name}」！</color>", false);
    }

    private void HandlePlayerInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        string trimmedInput = input.Trim();
        gamePanel.AppendText(trimmedInput, true);
        MemoryManager.Instance.AddUserMessage(trimmedInput);

        if (enableLLMIntentFallback)
            HandlePlayerInputAsync(trimmedInput);
        else
            HandlePlayerInputSync(trimmedInput);
    }

    private void HandlePlayerInputSync(string input)
    {
        IntentResult intent = IntentRecognizer.Instance.Recognize(input);
        ProcessActionWithIntent(input, intent);
    }

    private void HandlePlayerInputAsync(string input)
    {
        gamePanel.ShowLoading(true);
        IntentRecognizer.Instance.RecognizeAsync(input, intent =>
        {
            gamePanel.ShowLoading(false);
            ProcessActionWithIntent(input, intent);
        });
    }

    private void ProcessActionWithIntent(string input, IntentResult intent)
    {
        intent ??= new IntentResult();

        if (!IARProcessor.Instance.CheckActionValidity(intent, playerState, envState, out string failReason))
        {
            gamePanel.AppendText($"<color=#FF6666>{failReason}</color>", false);
            gamePanel.UpdateStateDisplay(playerState, envState);
            return;
        }

        string logicResult = IARProcessor.Instance.ExecuteDeterministicLogic(input, playerState, envState, intent);
        string knowledgeContext = GraphRAGManager.Instance.BuildKnowledgeContext(BuildKnowledgeQuery(input));

        gamePanel.UpdateStateDisplay(playerState, envState);

        string systemPrompt = PromptBuilder.BuildSystemPrompt();
        string userPrompt = PromptBuilder.BuildUserPromptWithIntent(input, playerState, envState, logicResult, intent, knowledgeContext);
        LLMService.Message[] messages = MemoryManager.Instance.BuildMessagesWithMemory(systemPrompt, userPrompt);

        if (!string.IsNullOrWhiteSpace(knowledgeContext))
            Debug.Log($"[GameLoop] GraphRAG 命中知识上下文:\n{knowledgeContext}");

        gamePanel.ShowLoading(true);
        var fullContentBuffer = new StringBuilder();

        LLMService.Instance.PostStreamWithMessages(
            messages,
            onTokenReceived: token =>
            {
                gamePanel.AppendStreamToken(token);
                fullContentBuffer.Append(token);
            },
            onComplete: () =>
            {
                string rawText = fullContentBuffer.ToString();
                string sanitizedText = LLMService.SanitizeVisibleText(rawText);
                if (!string.Equals(rawText, sanitizedText, StringComparison.Ordinal))
                    gamePanel.ReplaceLastStreamContent(rawText, sanitizedText);

                IARProcessor.Instance.AnalyzeAndApplyAIResult(rawText, playerState);
                GraphRAGManager.Instance.ExtractAndDiscoverFromText(sanitizedText);

                gamePanel.RemoveCmdTagsFromUI();
                gamePanel.UpdateStateDisplay(playerState, envState);
                gamePanel.ShowLoading(false);
                gamePanel.FinishStream();

                AudioMgr.Instance?.PlayPageTurnSfx();

                MemoryManager.Instance.AddAssistantMessage(sanitizedText);
                GameSaveMgr.Instance.CreateCheckpoint(playerState, envState, input);

                Debug.Log($"回合结束。意图: {intent} | 来源: {intent.recognitionSource}");
            },
            onStatus: message =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                    gamePanel.AppendText($"<color=#C58F2B>【系统】{message}</color>", false);
            });
    }

    private string BuildKnowledgeQuery(string input)
    {
        if (envState == null)
            return input;

        envState.EnsureCollections();
        string tags = envState.dynamicTags != null ? string.Join(" ", envState.dynamicTags) : string.Empty;
        string clues = envState.unlockedClues != null ? string.Join(" ", envState.unlockedClues) : string.Empty;
        return $"{input} {envState.locationName} {envState.currentObjective} {tags} {clues}".Trim();
    }
}
