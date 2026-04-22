using System;
using System.Text;
using Data.KnowledgeGraph;
using Logic.GraphRAG;
using Logic.Intent;
using Logic.Inventory;
using Logic.Memory;
using StateData.Environment;
using StateData.Items;
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
    private SceneItemLibraryData activeItemLibrary;
    private bool gameInited;

    [Header("Optional config")]
    [SerializeField] private TextAsset scenarioConfigJson;
    [SerializeField] private bool enableLLMIntentFallback = true;

    private const string PrefMusicOn = "SET_MUSIC_ON";
    private const string PrefSoundOn = "SET_SOUND_ON";
    private const string PrefMusicVol = "SET_MUSIC_VOL";
    private const string PrefSoundVol = "SET_SOUND_VOL";

    public RoleState CurrentState => playerState;
    public EnvironmentState CurrentEnvironment => envState;
    public ScenarioConfig CurrentScenario => activeScenario;
    public SceneItemLibraryData CurrentItemLibrary => activeItemLibrary;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        InitAudioSettings();
        AudioMgr.Instance?.PlayBGM();
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

        AudioMgr.Instance?.InitData(musicOn, soundOn, musicVol, soundVol);
    }

    public void StartNewGame()
    {
        if (gameInited)
            return;

        gameInited = true;
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
        activeItemLibrary = null;
        gameInited = false;
    }

    public void LoadGame(
        RoleState newState,
        EnvironmentState newEnvState = null,
        MemorySnapshot memorySnapshot = null,
        KnowledgeGraphSnapshot knowledgeGraphSnapshot = null)
    {
        if (newState == null)
            return;

        gameInited = true;
        activeScenario ??= LoadScenarioConfig();
        activeItemLibrary = LoadItemLibrary(activeScenario);
        playerState = newState;
        envState = newEnvState ?? LoadEnvironmentState(activeScenario);
        envState?.EnsureCollections();
        InventoryStateUtility.EnsureCompatibility(playerState, activeItemLibrary);

        if (memorySnapshot != null)
            MemoryManager.Instance.RestoreFromSnapshot(memorySnapshot);
        else
            MemoryManager.Instance.ClearAll();

        GraphRAGManager.Instance.RestoreFromSnapshot(knowledgeGraphSnapshot);

        EnsureGamePanelReady();
        RefreshMainPanel();
        gamePanel.RestoreStoryFromMemory(memorySnapshot, "【系统】已恢复到该回合的角色、环境、记忆与知识图谱状态。");
    }

    private void InitGame()
    {
        activeScenario = LoadScenarioConfig();
        activeItemLibrary = LoadItemLibrary(activeScenario);
        playerState = activeScenario.BuildRoleState();
        envState = LoadEnvironmentState(activeScenario);
        envState?.EnsureCollections();
        InventoryStateUtility.EnsureCompatibility(playerState, activeItemLibrary);
        EnsureGamePanelReady();

        GraphRAGManager.Instance.ResetDiscoveredState();
        if (activeScenario.initialDiscoveredEntityIds != null)
        {
            foreach (var entityId in activeScenario.initialDiscoveredEntityIds)
                GraphRAGManager.Instance.DiscoverEntity(entityId);
        }

        gamePanel.ClearStory();
        RefreshMainPanel();
        gamePanel.AppendText(activeScenario.openingNarration, false);

        MemoryManager.Instance.AddAssistantMessage(activeScenario.openingNarration);
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
                Debug.LogError($"[GameLoop] Failed to parse scenario config: {exception.Message}");
            }
        }

        scenario ??= ScenarioConfig.GetDefault();
        scenario.EnsureDefaults();
        return scenario;
    }

    private SceneItemLibraryData LoadItemLibrary(ScenarioConfig scenario)
    {
        string resourcePath = scenario != null ? scenario.itemLibraryResourcePath : null;
        if (string.IsNullOrWhiteSpace(resourcePath))
            return null;

        var itemLibrary = Resources.Load<SceneItemLibraryData>(resourcePath);
        if (itemLibrary == null)
        {
            Debug.LogWarning($"[GameLoop] Missing item library resource: {resourcePath}");
            return null;
        }

        itemLibrary.EnsureIndex();
        return itemLibrary;
    }

    private EnvironmentState LoadEnvironmentState(ScenarioConfig scenario)
    {
        string resourcePath = scenario != null ? scenario.environmentResourcePath : null;
        EnvironmentData data = !string.IsNullOrWhiteSpace(resourcePath)
            ? Resources.Load<EnvironmentData>(resourcePath)
            : null;

        if (data == null)
        {
            Debug.LogWarning($"[GameLoop] Missing environment resource: {resourcePath}, fallback to default environment.");
            return EnvironmentState.GetDefault();
        }

        return EnvironmentState.FromData(data);
    }

    private void OnEntityDiscovered(KnowledgeEntity entity)
    {
        if (entity == null)
            return;
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
        InventoryStateUtility.EnsureCompatibility(playerState, activeItemLibrary);
        var turnStartItemSnapshot = AIResponseConsistencyChecker.CaptureSnapshot(playerState, activeItemLibrary);

        if (!IARProcessor.Instance.CheckActionValidity(intent, playerState, envState, out string failReason))
        {
            gamePanel.AppendText($"<color=#FF6666>{failReason}</color>", false);
            RefreshMainPanel();
            return;
        }

        string logicResult = IARProcessor.Instance.ExecuteDeterministicLogic(input, playerState, envState, intent);
        string knowledgeContext = GraphRAGManager.Instance.BuildKnowledgeContext(BuildKnowledgeQuery(input));

        string systemPrompt = PromptBuilder.BuildSystemPrompt();
        string userPrompt = PromptBuilder.BuildUserPromptWithIntent(
            input,
            playerState,
            envState,
            logicResult,
            intent,
            knowledgeContext,
            activeItemLibrary);
        var messages = MemoryManager.Instance.BuildMessagesWithMemory(systemPrompt, userPrompt);

        gamePanel.ShowLoading(true);
        var fullContentBuffer = new StringBuilder();
        gamePanel.BeginAssistantStream();

        LLMService.Instance.PostStreamWithMessages(
            messages,
            onTokenReceived: token =>
            {
                fullContentBuffer.Append(token);
                gamePanel.UpdateAssistantStream(LLMService.SanitizeStreamingVisibleText(fullContentBuffer.ToString()));
            },
            onComplete: () =>
            {
                string rawText = fullContentBuffer.ToString();
                string sanitizedText = LLMService.SanitizeVisibleText(rawText);
                string logicFeedback;
                IARProcessor.Instance.AnalyzeAndApplyAIResult(rawText, playerState, activeItemLibrary, out logicFeedback);
                var consistencyReport = AIResponseConsistencyChecker.FilterVisibleText(
                    sanitizedText,
                    turnStartItemSnapshot,
                    playerState,
                    activeItemLibrary);
                string finalVisibleText = consistencyReport.visibleText;
                gamePanel.CompleteAssistantStream(finalVisibleText);

                GraphRAGManager.Instance.ExtractAndDiscoverFromText(finalVisibleText);
                RefreshMainPanel();
                gamePanel.ShowLoading(false);

                if (!string.IsNullOrWhiteSpace(logicFeedback))
                    gamePanel.AppendText($"<color=#C58F2B>【系统】{logicFeedback}</color>", false);

                if (!string.IsNullOrWhiteSpace(consistencyReport.feedback))
                    gamePanel.AppendText($"<color=#C58F2B>【系统】{consistencyReport.feedback}</color>", false);

                AudioMgr.Instance?.PlayPageTurnSfx();
                MemoryManager.Instance.AddAssistantMessage(finalVisibleText);
                GameSaveMgr.Instance.CreateCheckpoint(playerState, envState, input);
            },
            onStatus: message =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                    gamePanel.AppendText($"<color=#C58F2B>【系统】{message}</color>", false);
            });
    }

    private void RefreshMainPanel()
    {
        if (gamePanel == null)
            return;

        InventoryStateUtility.EnsureCompatibility(playerState, activeItemLibrary);
        gamePanel.UpdateStateDisplay(playerState, envState, activeItemLibrary);
        gamePanel.BindInventoryState(playerState, envState, activeItemLibrary);
    }

    private string BuildKnowledgeQuery(string input)
    {
        if (envState == null)
            return input;

        envState.EnsureCollections();
        string tags = envState.dynamicTags != null ? string.Join(" ", envState.dynamicTags) : string.Empty;
        string clues = envState.unlockedClues != null ? string.Join(" ", envState.unlockedClues) : string.Empty;
        string inventory = InventoryStateUtility.BuildInventoryPromptSummary(playerState, activeItemLibrary);
        return $"{input} {envState.locationName} {envState.currentObjective} {tags} {clues} {inventory}".Trim();
    }
}
