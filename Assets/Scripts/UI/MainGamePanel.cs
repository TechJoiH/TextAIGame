using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Logic.Inventory;
using Logic.Memory;
using StateData.Environment;
using StateData.Items;
using StateData.Role;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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
    public Button knowledgeGraphButton;
    public Button characterButton;
    public Button bagButton;

    [Header("Panel Roots")]
    public GameObject characterPanelRoot;
    public GameObject bagPanelRoot;

    [Header("Inventory Anchors")]
    public RectTransform bagStartPosition;
    public RectTransform equipmentStartPosition;
    public GameObject itemIconPrefab;

    [Header("Tooltip")]
    public GameObject itemTooltipRoot;
    public TMP_Text itemTooltipNameText;
    public TMP_Text itemTooltipDetailText;
    public TMP_Text itemTooltipText;

    [Header("Bag Layout")]
    public int bagRows = 3;
    public int bagColumns = 8;
    public float bagStepX = 124f;
    public float bagStepY = 160f;
    public Vector2 bagCellSize = new Vector2(92f, 112f);

    [Header("Equipment Layout")]
    public int equipmentSlotCount = 5;
    public float equipmentStepX = 154f;
    public Vector2 equipmentCellSize = new Vector2(96f, 110f);

    public event UnityAction<string> OnPlayerInput;

    private readonly List<InventoryItemView> activeItemViews = new List<InventoryItemView>();
    private readonly Queue<string> centerToastQueue = new Queue<string>();
    private string currentStoryContent = string.Empty;
    private bool inited;
    private RoleState boundState;
    private EnvironmentState boundEnvironment;
    private SceneItemLibraryData boundItemLibrary;
    private RectTransform centerToastRoot;
    private TMP_Text centerToastText;
    private CanvasGroup centerToastCanvasGroup;
    private Coroutine centerToastCoroutine;
    private ScreenFlashFeedback screenFlashFeedback;
    private RoleState healthFlashState;
    private int lastHealthForFlash;
    private bool hasHealthFlashBaseline;

    private const float TooltipMinWidth = 260f;
    private const float TooltipMaxWidth = 420f;
    private const float TooltipMaxHeight = 520f;
    private const float TooltipPadding = 18f;
    private const float TooltipSpacing = 10f;
    private const float TooltipScreenMargin = 16f;
    private const string CenterToastEventName = "OnCenterToast";
    private const float CenterToastFadeDuration = 0.18f;
    private const float CenterToastHoldDuration = 1.75f;

    public override void Init()
    {
        if (inited) return;
        inited = true;

        AutoResolveReferences();
        ApplyStatusTextPresentation();

        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendClick);
        if (historyButton != null)
            historyButton.onClick.AddListener(OnHistoryClick);
        if (hintButton != null)
            hintButton.onClick.AddListener(OnHintClick);
        if (buttonSet != null)
            buttonSet.onClick.AddListener(OnSetClick);
        if (knowledgeGraphButton != null)
            knowledgeGraphButton.onClick.AddListener(OnKnowledgeGraphClick);
        if (characterButton != null)
            characterButton.onClick.AddListener(OnCharacterClick);
        if (bagButton != null)
            bagButton.onClick.AddListener(OnBagClick);

        ApplyBranding();
        ApplyInitialPanelVisibility();
        HideItemTooltip();
        EnsureCenterToastPresentation();
        EnsureScreenFlashFeedback();
        RegisterCenterToastListener();
    }

    public void BindInventoryState(RoleState state, EnvironmentState environmentState, SceneItemLibraryData itemLibrary)
    {
        boundState = state;
        boundEnvironment = environmentState;
        boundItemLibrary = itemLibrary;
        ResetHealthFlashBaseline(state);
        RefreshInventoryViews();
    }

    public void AppendText(string text, bool isPlayer)
    {
        text = NormalizeUIText(text);
        string color = isPlayer ? "#4E342E" : "#000000";
        string prefix = string.IsNullOrEmpty(currentStoryContent) ? string.Empty : "\n";

        if (isPlayer)
            currentStoryContent += $"{prefix}<color={color}><b> {text}</b></color>\n";
        else
            currentStoryContent += $"{prefix}<color={color}>{text}</color>";

        UpdateUIText();
    }

    private static string NormalizeUIText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        return text
            .Replace("銆愮郴缁熴€?", "【系统】")
            .Replace("銆愮郴缁熴€", "【系统】")
            .Replace("宸茶澶囥€?", "已装备。")
            .Replace("宸插嵏涓嬨€?", "已卸下。");
    }

    public void AppendStreamToken(string token)
    {
        currentStoryContent += token;
        UpdateUIText();
    }

    public void FinishStream()
    {
        currentStoryContent += "\n";
        UpdateUIText();
    }

    public void RemoveCmdTagsFromUI()
    {
        if (storyText == null) return;

        currentStoryContent = System.Text.RegularExpressions.Regex.Replace(
            currentStoryContent,
            @"<CMD>.*?</CMD>",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        UpdateUIText();
    }

    public void UpdateStateDisplay(RoleState state)
    {
        UpdateStateDisplay(state, GameLoop.Instance != null ? GameLoop.Instance.CurrentEnvironment : null, GameLoop.Instance != null ? GameLoop.Instance.CurrentItemLibrary : null);
    }

    public void UpdateStateDisplay(RoleState state, EnvironmentState environmentState)
    {
        UpdateStateDisplay(state, environmentState, GameLoop.Instance != null ? GameLoop.Instance.CurrentItemLibrary : null);
    }

    public void UpdateStateDisplay(RoleState state, EnvironmentState environmentState, SceneItemLibraryData itemLibrary)
    {
        if (statusText == null || state == null) return;

        ApplyStatusTextPresentation();
        environmentState ??= EnvironmentState.GetDefault();
        environmentState.EnsureCollections();
        InventoryStateUtility.EnsureCompatibility(state, itemLibrary);
        HandleHealthFlash(state);
        var derived = InventoryStateUtility.CalculateDerivedAttributes(state);
        var equippedWeapon = state.equipment.equipmentSlots.GetSlot(EquipSlotType.Weapon);

        if (Application.isPlaying)
        {
            string compactRoleName = TrimStatusText(state.identity?.name ?? "无名旅人", 10);
            string compactWeaponName = TrimStatusText(equippedWeapon?.runtimeData?.name ?? "空手", 10);
            string compactLocation = TrimStatusText(environmentState.locationName, 12);
            string compactTagPreview = environmentState.dynamicTags != null && environmentState.dynamicTags.Count > 0
                ? TrimStatusText(string.Join(" / ", environmentState.dynamicTags.Take(3)), 30)
                : "暂无";
            string compactObjectiveText = string.IsNullOrWhiteSpace(environmentState.currentObjective)
                ? "自由探索"
                : TrimStatusText(environmentState.currentObjective, 24);

            statusText.text =
                $"角色 <color=#8E6B3F>{compactRoleName}</color>  <color=#000000>Lv.{state.attributes.level}</color>  生命 <color=#FF5555>{state.attributes.currentHealth}/{derived.maxHealthTotal}</color>  灵力 <color=#55AAFF>{state.attributes.currentMana}/{derived.maxManaTotal}</color>\n" +
                $"力 <color=#B5651D>{derived.strengthTotal}</color>  敏 <color=#6C8B3C>{derived.agilityTotal}</color>  智 <color=#5D7FB8>{derived.intelligenceTotal}</color>  攻 <color=#6F3D1F>{derived.attackPower}</color>  武 <color=#7A5F2A>{compactWeaponName}</color>\n" +
                $"地 <color=#FFAA00>{compactLocation}</color>  天 <color=#9EC8FF>{GetDisplayWeatherText(environmentState.weather)}</color>  时 <color=#B6E0FE>{GetDisplayTimeText(environmentState.timeOfDay)}</color>  目标 <color=#D9B16A>{compactObjectiveText}</color>\n" +
                $"标签 <color=#8E6B3F>{compactTagPreview}</color>";
            return;
        }

        if (Application.isPlaying)
        {
            string compactWeaponName = equippedWeapon?.runtimeData?.name ?? "空手";
            string compactTagPreview = environmentState.dynamicTags != null && environmentState.dynamicTags.Count > 0
                ? string.Join(" / ", environmentState.dynamicTags.Take(4))
                : "暂无";
            string compactObjectiveText = string.IsNullOrWhiteSpace(environmentState.currentObjective)
                ? "自由探索"
                : environmentState.currentObjective;

            statusText.text =
                $"角色: <color=#8E6B3F>{state.identity?.name ?? "无名旅人"}</color>  Lv.{state.attributes.level}\n" +
                $"生命: <color=#FF5555>{state.attributes.currentHealth}/{derived.maxHealthTotal}</color>\n" +
                $"灵力: <color=#55AAFF>{state.attributes.currentMana}/{derived.maxManaTotal}</color>\n" +
                $"力量: <color=#B5651D>{derived.strengthTotal}</color>  " +
                $"敏捷: <color=#6C8B3C>{derived.agilityTotal}</color>  " +
                $"智力: <color=#5D7FB8>{derived.intelligenceTotal}</color>\n" +
                $"攻击: <color=#6F3D1F>{derived.attackPower}</color>  武器: <color=#7A5F2A>{compactWeaponName}</color>\n" +
                $"地点: <color=#FFAA00>{environmentState.locationName}</color>  天气: <color=#9EC8FF>{GetDisplayWeatherText(environmentState.weather)}</color>  " +
                $"时景: <color=#B6E0FE>{GetDisplayTimeText(environmentState.timeOfDay)}</color>\n" +
                $"目标: <color=#D9B16A>{compactObjectiveText}</color>\n" +
                $"标签: <color=#8E6B3F>{compactTagPreview}</color>";
            return;
        }
        string weaponName = equippedWeapon?.runtimeData?.name ?? "空手";

        string tagPreview = environmentState.dynamicTags != null && environmentState.dynamicTags.Count > 0
            ? string.Join(" / ", environmentState.dynamicTags.Take(4))
            : "暂无";
        string objectiveText = string.IsNullOrWhiteSpace(environmentState.currentObjective)
            ? "自由探索"
            : environmentState.currentObjective;

        statusText.text =
            $"角色: <color=#8E6B3F>{state.identity?.name ?? "无名旅人"}</color>  Lv.{state.attributes.level}\n" +
            $"生命: <color=#FF5555>{state.attributes.currentHealth}/{derived.maxHealthTotal}</color>  (基础 {derived.maxHealthBase} + 装备 {derived.maxHealthBonus:+#;-#;0})\n" +
            $"灵力: <color=#55AAFF>{state.attributes.currentMana}/{derived.maxManaTotal}</color>  (基础 {derived.maxManaBase} + 装备 {derived.maxManaBonus:+#;-#;0})\n" +
            $"力量: <color=#B5651D>{derived.strengthBase}+{derived.strengthBonus}={derived.strengthTotal}</color>  " +
            $"敏捷: <color=#6C8B3C>{derived.agilityBase}+{derived.agilityBonus}={derived.agilityTotal}</color>  " +
            $"智力: <color=#5D7FB8>{derived.intelligenceBase}+{derived.intelligenceBonus}={derived.intelligenceTotal}</color>\n" +
            $"攻击: <color=#6F3D1F>{derived.attackPower}</color>  武器: <color=#7A5F2A>{weaponName}</color>\n" +
            $"地点: <color=#FFAA00>{environmentState.locationName}</color>  天气: <color=#9EC8FF>{GetDisplayWeatherText(environmentState.weather)}</color>  " +
            $"时景: <color=#B6E0FE>{GetDisplayTimeText(environmentState.timeOfDay)}</color>\n" +
            $"目标: <color=#D9B16A>{objectiveText}</color>\n" +
            $"标签: <color=#8E6B3F>{tagPreview}</color>";
    }

    public void ShowLoading(bool isLoading)
    {
        if (sendButton != null) sendButton.interactable = !isLoading;
        if (inputField != null) inputField.interactable = !isLoading;
        if (historyButton != null) historyButton.interactable = true;
        if (hintButton != null) hintButton.interactable = true;
        if (knowledgeGraphButton != null) knowledgeGraphButton.interactable = true;
        if (characterButton != null) characterButton.interactable = true;
        if (bagButton != null) bagButton.interactable = true;
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

        int index = currentStoryContent.LastIndexOf(original, StringComparison.Ordinal);
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
                builder.AppendLine($"<color=#7A5F2A>{memory.summary}</color>");
            builder.AppendLine();
        }

        if (snapshot?.shortTermMemory != null)
        {
            foreach (var entry in snapshot.shortTermMemory)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.content))
                    continue;

                string prefix = builder.Length == 0 ? string.Empty : "\n";
                if (entry.role == "user")
                    builder.Append($"{prefix}<color=#4E342E><b> {entry.content}</b></color>\n");
                else
                    builder.Append($"{prefix}<color=#000000>{entry.content}</color>");
            }
        }

        if (!string.IsNullOrWhiteSpace(systemNotice))
        {
            string prefix = builder.Length == 0 ? string.Empty : "\n";
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
        SetButtonLabel(characterButton, "人物");
        SetButtonLabel(bagButton, "背包");

        if (inputField != null && inputField.placeholder is TMP_Text placeholder)
            placeholder.text = "输入行动，例如：观察招摇山雾中的草木";
    }

    private void OnHistoryClick()
    {
        AudioMgr.Instance?.PlayPanelOpenSfx();
        UIMgr.Instance.ShowPanel<HistoryPanel>();
    }

    private void OnHintClick()
    {
        AudioMgr.Instance?.PlayPanelOpenSfx();
        UIMgr.Instance.ShowPanel<ActionHintPanel>();
    }

    private void OnKnowledgeGraphClick()
    {
        AudioMgr.Instance?.PlayPanelOpenSfx();
        UIMgr.Instance.ShowPanel<KnowledgeGraphPanel>();
    }

    private void OnSendClick()
    {
        if (inputField == null || string.IsNullOrWhiteSpace(inputField.text))
            return;

        AudioMgr.Instance?.PlayClickSfx();
        string input = inputField.text;
        inputField.text = string.Empty;
        OnPlayerInput?.Invoke(input);
    }

    private void OnSetClick()
    {
        AudioMgr.Instance?.PlayPanelOpenSfx();
        var panel = UIMgr.Instance.ShowPanel<SetPanel>();
        if (panel != null)
            panel.SetOpenFromGame(true);
    }

    private void OnCharacterClick()
    {
        AudioMgr.Instance?.PlayClickSfx();
        TogglePanelActive(characterPanelRoot);
    }

    private void OnBagClick()
    {
        AudioMgr.Instance?.PlayClickSfx();
        TogglePanelActive(bagPanelRoot);
    }

    private void TogglePanelActive(GameObject panelRoot)
    {
        if (panelRoot == null)
            return;

        panelRoot.SetActive(!panelRoot.activeSelf);
        if (!panelRoot.activeSelf)
            HideItemTooltip();
    }

    private void AutoResolveReferences()
    {
        characterPanelRoot ??= FindChildGameObject("PanelCharacter");
        bagPanelRoot ??= FindChildGameObject("ImageBagBk");
        bagStartPosition ??= FindChildRectTransform("BagOrigin");
        equipmentStartPosition ??= FindChildRectTransform("CharacterOrigin");
        itemTooltipRoot = ResolveTooltipRootInstance(itemTooltipRoot);

        if (characterPanelRoot == null && statusText != null)
            characterPanelRoot = statusText.transform.parent != null ? statusText.transform.parent.gameObject : null;
        if (bagPanelRoot == null && bagStartPosition != null)
            bagPanelRoot = bagStartPosition.parent != null ? bagStartPosition.parent.gameObject : null;

        if (itemTooltipRoot != null)
        {
            var tooltipTexts = itemTooltipRoot.GetComponentsInChildren<TMP_Text>(true);

            if (itemTooltipNameText == null)
                itemTooltipNameText = FindTooltipText(tooltipTexts, "name");

            if (itemTooltipDetailText == null)
                itemTooltipDetailText = FindTooltipText(tooltipTexts, "detail", "desc", "info");

            if (itemTooltipText == null)
                itemTooltipText = tooltipTexts.FirstOrDefault();

            if (itemTooltipNameText == null && itemTooltipDetailText == null)
                itemTooltipDetailText = itemTooltipText;
        }

        ApplyTooltipPresentation();
    }

    private GameObject ResolveTooltipRootInstance(GameObject tooltipReference)
    {
        var existingTooltip = FindChildGameObject("ItemTooltip");
        if (existingTooltip != null && existingTooltip.scene.IsValid() && existingTooltip.scene.isLoaded)
            return existingTooltip;

        if (tooltipReference == null)
            tooltipReference = existingTooltip;

        if (tooltipReference == null)
            return null;

        if (tooltipReference.scene.IsValid() && tooltipReference.scene.isLoaded)
            return tooltipReference;

        var parent = transform as RectTransform;
        var instance = Instantiate(tooltipReference, parent, false);
        instance.name = tooltipReference.name;
        instance.SetActive(false);
        return instance;
    }

    private void ApplyTooltipPresentation()
    {
        if (itemTooltipRoot == null)
            return;

        var canvasGroup = itemTooltipRoot.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = itemTooltipRoot.AddComponent<CanvasGroup>();

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        var graphics = itemTooltipRoot.GetComponentsInChildren<Graphic>(true);
        foreach (var graphic in graphics)
        {
            if (graphic != null)
                graphic.raycastTarget = false;
        }

        TMP_FontAsset fallbackFont = storyText != null ? storyText.font : statusText != null ? statusText.font : null;

        // 不再在运行时强制设置字号/自动缩放；仅保留黑色与基础交互属性。
        ApplyTooltipTextNonSizingPresentation(itemTooltipNameText, fallbackFont);
        ApplyTooltipTextNonSizingPresentation(itemTooltipDetailText, fallbackFont);
        ApplyTooltipTextNonSizingPresentation(itemTooltipText, fallbackFont);
    }

    private static void ApplyTooltipTextNonSizingPresentation(TMP_Text text, TMP_FontAsset fallbackFont)
    {
        if (text == null)
            return;

        text.color = Color.black; // 保留：文字为黑色
        text.raycastTarget = false;
        text.richText = false;

        if (fallbackFont != null)
            text.font = fallbackFont;
    }

    private void ApplyStatusTextPresentation()
    {
        if (statusText == null)
            return;

        statusText.enableAutoSizing = true;
        statusText.fontSizeMax = 24f;
        statusText.fontSizeMin = 14f;
        statusText.enableWordWrapping = false;
        statusText.overflowMode = TextOverflowModes.Overflow;
        statusText.alignment = TextAlignmentOptions.TopLeft;
        statusText.lineSpacing = -10f;
        statusText.characterSpacing = 0f;
        statusText.margin = new Vector4(8f, 6f, 8f, 6f);
        statusText.color = Color.black;
        statusText.raycastTarget = false;
    }

    private void ConfigureTooltipTextAppearance(TMP_Text text, TMP_FontAsset fallbackFont, bool isTitle)
    {
        if (text == null)
            return;

        text.color = Color.black;
        text.enableAutoSizing = false;
        text.fontSize = isTitle ? 30f : 18f;
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = text.fontSize;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = false;
        text.richText = false;

        if (fallbackFont != null)
            text.font = fallbackFont;
    }

    private void ApplyInitialPanelVisibility()
    {
        if (characterPanelRoot != null)
            characterPanelRoot.SetActive(false);

        if (bagPanelRoot != null)
            bagPanelRoot.SetActive(false);
    }

    private void EnsureCenterToastPresentation()
    {
        if (centerToastRoot == null)
            centerToastRoot = FindChildRectTransform("CenterToastRoot");

        if (centerToastRoot == null)
            centerToastRoot = CreateCenterToastRoot();

        if (centerToastRoot == null)
            return;

        centerToastCanvasGroup = centerToastRoot.GetComponent<CanvasGroup>();
        if (centerToastCanvasGroup == null)
            centerToastCanvasGroup = centerToastRoot.gameObject.AddComponent<CanvasGroup>();

        centerToastCanvasGroup.alpha = 0f;
        centerToastCanvasGroup.interactable = false;
        centerToastCanvasGroup.blocksRaycasts = false;

        if (centerToastText == null)
            centerToastText = centerToastRoot.GetComponentInChildren<TMP_Text>(true);

        if (centerToastText != null)
        {
            centerToastText.color = Color.white;
            centerToastText.enableAutoSizing = true;
            centerToastText.fontSizeMax = 30f;
            centerToastText.fontSizeMin = 18f;
            centerToastText.enableWordWrapping = true;
            centerToastText.overflowMode = TextOverflowModes.Overflow;
            centerToastText.alignment = TextAlignmentOptions.Center;
            centerToastText.raycastTarget = false;
            centerToastText.richText = false;

            TMP_FontAsset fallbackFont = storyText != null ? storyText.font : statusText != null ? statusText.font : null;
            if (fallbackFont != null)
                centerToastText.font = fallbackFont;
        }

        UpdateCenterToastLayout();
        centerToastRoot.gameObject.SetActive(false);
    }

    private RectTransform CreateCenterToastRoot()
    {
        var toastObject = new GameObject("CenterToastRoot", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        toastObject.transform.SetParent(transform, false);

        var toastRect = toastObject.GetComponent<RectTransform>();
        toastRect.anchorMin = new Vector2(0.5f, 0.5f);
        toastRect.anchorMax = new Vector2(0.5f, 0.5f);
        toastRect.pivot = new Vector2(0.5f, 0.5f);
        toastRect.anchoredPosition = new Vector2(0f, 36f);
        toastRect.sizeDelta = new Vector2(760f, 132f);

        var background = toastObject.GetComponent<Image>();
        background.sprite = null;
        background.type = Image.Type.Simple;
        background.color = new Color(0.09f, 0.07f, 0.04f, 0.86f);
        background.raycastTarget = false;

        var textObject = new GameObject("CenterToastText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(toastObject.transform, false);

        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(32f, 22f);
        textRect.offsetMax = new Vector2(-32f, -22f);

        centerToastText = textObject.GetComponent<TextMeshProUGUI>();
        centerToastText.text = string.Empty;

        return toastRect;
    }

    private void UpdateCenterToastLayout()
    {
        if (centerToastRoot == null)
            return;

        RectTransform panelRect = transform as RectTransform;
        float availableWidth = panelRect != null && panelRect.rect.width > 0f ? panelRect.rect.width : Screen.width;
        float toastWidth = Mathf.Clamp(availableWidth - 96f, 320f, 760f);
        float toastHeight = toastWidth <= 420f ? 116f : 132f;
        centerToastRoot.sizeDelta = new Vector2(toastWidth, toastHeight);
    }

    private void RegisterCenterToastListener()
    {
        EventCenter.Instance.RemoveListener<string>(CenterToastEventName, HandleCenterToastRequested);
        EventCenter.Instance.AddListener<string>(CenterToastEventName, HandleCenterToastRequested);
    }

    private void UnregisterCenterToastListener()
    {
        EventCenter.Instance.RemoveListener<string>(CenterToastEventName, HandleCenterToastRequested);
    }

    private void EnsureScreenFlashFeedback()
    {
        if (screenFlashFeedback != null)
            return;

        screenFlashFeedback = GetComponent<ScreenFlashFeedback>();
        if (screenFlashFeedback == null)
            screenFlashFeedback = gameObject.AddComponent<ScreenFlashFeedback>();
    }

    private void HandleHealthFlash(RoleState state)
    {
        if (state == null)
        {
            ResetHealthFlashBaseline(null);
            return;
        }

        int currentHealth = state.attributes.currentHealth;
        if (healthFlashState != state || !hasHealthFlashBaseline)
        {
            ResetHealthFlashBaseline(state);
            return;
        }

        if (currentHealth < lastHealthForFlash)
        {
            EnsureScreenFlashFeedback();
            screenFlashFeedback?.PlayDamageFlash();
        }
        else if (currentHealth > lastHealthForFlash)
        {
            EnsureScreenFlashFeedback();
            screenFlashFeedback?.PlayHealFlash();
        }

        lastHealthForFlash = currentHealth;
    }

    private void ResetHealthFlashBaseline(RoleState state)
    {
        healthFlashState = state;
        hasHealthFlashBaseline = state != null;
        lastHealthForFlash = state != null ? state.attributes.currentHealth : 0;
    }

    private void HandleCenterToastRequested(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        centerToastQueue.Enqueue(message.Trim());
        if (centerToastCoroutine == null && gameObject.activeInHierarchy)
            centerToastCoroutine = StartCoroutine(PlayCenterToastQueue());
    }

    private IEnumerator PlayCenterToastQueue()
    {
        while (centerToastQueue.Count > 0)
            yield return PlayCenterToast(centerToastQueue.Dequeue());

        centerToastCoroutine = null;
    }

    private IEnumerator PlayCenterToast(string message)
    {
        EnsureCenterToastPresentation();
        if (centerToastRoot == null || centerToastText == null || centerToastCanvasGroup == null)
            yield break;

        centerToastText.text = message;
        centerToastRoot.gameObject.SetActive(true);
        centerToastRoot.SetAsLastSibling();

        Vector3 hiddenScale = new Vector3(0.94f, 0.94f, 1f);
        Vector3 shownScale = Vector3.one;

        yield return AnimateCenterToast(0f, 1f, hiddenScale, shownScale);
        yield return new WaitForSecondsRealtime(CenterToastHoldDuration);
        yield return AnimateCenterToast(1f, 0f, shownScale, hiddenScale);

        centerToastRoot.gameObject.SetActive(false);
    }

    private IEnumerator AnimateCenterToast(float fromAlpha, float toAlpha, Vector3 fromScale, Vector3 toScale)
    {
        float elapsed = 0f;
        centerToastCanvasGroup.alpha = fromAlpha;
        centerToastRoot.localScale = fromScale;

        while (elapsed < CenterToastFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / CenterToastFadeDuration);
            centerToastCanvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, progress);
            centerToastRoot.localScale = Vector3.LerpUnclamped(fromScale, toScale, progress);
            yield return null;
        }

        centerToastCanvasGroup.alpha = toAlpha;
        centerToastRoot.localScale = toScale;
    }

    private void UpdateUIText()
    {
        if (storyText == null)
            return;

        storyText.text = currentStoryContent;
        if (gameObject.activeInHierarchy)
            StartCoroutine(ScrollToBottomCoroutine());
    }

    private IEnumerator ScrollToBottomCoroutine()
    {
        yield return new WaitForEndOfFrame();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 0f;
    }

    private void RefreshInventoryViews()
    {
        ClearSpawnedItemViews();
        HideItemTooltip();

        if (boundState == null)
            return;

        InventoryStateUtility.EnsureCompatibility(boundState, boundItemLibrary);

        for (int i = 0; i < boundState.equipment.inventoryEntries.Count && i < bagRows * bagColumns; i++)
        {
            int row = i / bagColumns;
            int col = i % bagColumns;
            Vector2 position = GetBagPosition(row, col);
            var entry = boundState.equipment.inventoryEntries[i];
            var template = InventoryStateUtility.ResolveTemplate(boundItemLibrary, entry);
            SpawnItemView(position, bagCellSize, entry, template, false, EquipSlotType.None, i);
        }

        for (int i = 0; i < equipmentSlotCount && i < InventoryStateUtility.DefaultEquipOrder.Length; i++)
        {
            EquipSlotType slotType = InventoryStateUtility.DefaultEquipOrder[i];
            var entry = boundState.equipment.equipmentSlots.GetSlot(slotType);
            if (entry == null)
                continue;

            var template = InventoryStateUtility.ResolveTemplate(boundItemLibrary, entry);
            SpawnItemView(GetEquipmentPosition(i), equipmentCellSize, entry, template, true, slotType, -1);
        }
    }

    private void SpawnItemView(
        Vector2 anchoredPosition,
        Vector2 cellSize,
        ItemInventoryEntry entry,
        ItemTemplateData template,
        bool isEquipped,
        EquipSlotType slotType,
        int inventoryIndex)
    {
        RectTransform parent = ResolveParentRoot(isEquipped);
        RectTransform anchorSource = isEquipped ? equipmentStartPosition : bagStartPosition;
        if (parent == null || anchorSource == null || entry == null)
            return;

        GameObject viewObject;
        InventoryItemView view;
        if (itemIconPrefab != null)
        {
            viewObject = Instantiate(itemIconPrefab, parent, false);
            view = viewObject.GetComponent<InventoryItemView>() ?? viewObject.AddComponent<InventoryItemView>();
        }
        else
        {
            view = InventoryItemView.CreateFallback(parent, anchorSource);
            viewObject = view.gameObject;
        }

        RectTransform rectTransform = viewObject.GetComponent<RectTransform>();
        if (rectTransform == null)
            rectTransform = viewObject.AddComponent<RectTransform>();

        rectTransform.anchorMin = anchorSource.anchorMin;
        rectTransform.anchorMax = anchorSource.anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = cellSize;

        view.Bind(
            entry,
            template,
            isEquipped,
            OnItemViewClicked,
            OnItemViewHoverEnter,
            OnItemViewHoverExit,
            slotType,
            inventoryIndex);

        activeItemViews.Add(view);
    }

    private void OnItemViewClicked(InventoryItemView itemView)
    {
        if (boundState == null || itemView == null)
            return;

        AudioMgr.Instance?.PlayClickSfx();
        OnItemViewHoverEnter(itemView);

        string message = null;
        bool changed = false;

        if (itemView.IsEquipped)
        {
            changed = InventoryStateUtility.TryUnequipSlot(boundState, itemView.EquipSlotType, boundItemLibrary, out message);
            if (changed)
                message = $"{itemView.DisplayName} 已卸下。";
        }
        else if (itemView.Template != null && itemView.Template.IsEquipment)
        {
            changed = InventoryStateUtility.TryEquipInventoryItem(boundState, itemView.InventoryIndex, boundItemLibrary, out message);
            if (changed)
                message = $"{itemView.DisplayName} 已装备。";
        }

        else
        {
            changed = TryUseInventoryItemFromUI(itemView, out message);
        }

        if (!string.IsNullOrWhiteSpace(message))
            AppendText($"<color=#C58F2B>【系统】{message}</color>", false);

        if (!changed)
            return;

        UpdateStateDisplay(boundState, boundEnvironment, boundItemLibrary);
        RefreshInventoryViews();
    }

    private bool TryUseInventoryItemFromUI(InventoryItemView itemView, out string message)
    {
        message = null;
        if (boundState == null || itemView?.Entry == null)
        {
            message = "目标物品不存在。";
            return false;
        }

        boundEnvironment ??= EnvironmentState.GetDefault();
        boundEnvironment.EnsureCollections();
        InventoryStateUtility.EnsureCompatibility(boundState, boundItemLibrary);

        ItemInventoryEntry entry = itemView.Entry;
        ItemTemplateData template = itemView.Template ?? InventoryStateUtility.ResolveTemplate(boundItemLibrary, entry);
        string displayName = entry.runtimeData?.name ?? template?.displayName ?? entry.templateId;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            message = "该物品信息不完整，暂时无法使用。";
            return false;
        }

        if (ContainsItemKeyword(displayName, "治疗药水"))
        {
            if (!TryConsumeExactInventoryEntry(entry))
            {
                message = "治疗药水不存在或已被使用。";
                return false;
            }

            int restoredHealth = Mathf.Min(18, InventoryStateUtility.CalculateDerivedAttributes(boundState).maxHealthTotal - boundState.attributes.currentHealth);
            boundState.attributes.currentHealth += restoredHealth;
            boundEnvironment.AddTag("药气回暖");
            InventoryStateUtility.NormalizeResourceCaps(boundState, InventoryStateUtility.CalculateDerivedAttributes(boundState));
            message = $"服下治疗药水，生命恢复 +{restoredHealth}";
            return true;
        }

        if (ContainsItemKeyword(displayName, "祝余"))
        {
            if (!TryConsumeExactInventoryEntry(entry))
            {
                message = "祝余不存在或已被使用。";
                return false;
            }

            DerivedAttributeState derived = InventoryStateUtility.CalculateDerivedAttributes(boundState);
            int restoredHealth = Mathf.Min(6, derived.maxHealthTotal - boundState.attributes.currentHealth);
            int restoredMana = Mathf.Min(6, derived.maxManaTotal - boundState.attributes.currentMana);
            boundState.attributes.currentHealth += restoredHealth;
            boundState.attributes.currentMana += restoredMana;
            boundEnvironment.AddTag("食草回甘");
            boundEnvironment.currentObjective = "体力稍定，可以继续观察或深入迷雾。";
            InventoryStateUtility.NormalizeResourceCaps(boundState, InventoryStateUtility.CalculateDerivedAttributes(boundState));
            message = $"咽下祝余后气息稍定，生命 +{restoredHealth}，灵力 +{restoredMana}";
            return true;
        }

        if (ContainsItemKeyword(displayName, "迷谷"))
        {
            boundEnvironment.RemoveTag("迷失方向");
            boundEnvironment.AddTag("迷谷指路");
            boundEnvironment.currentObjective = "沿雾径深入，观察青白异光的来源。";
            message = "佩上迷谷后，雾中的路径轮廓逐渐清晰。";
            return true;
        }

        if (template == null || template.itemKind != ItemKind.Consumable)
        {
            message = "该物品当前无法直接使用。";
            return false;
        }

        if (!TryConsumeExactInventoryEntry(entry))
        {
            message = "该物品不存在或已被使用。";
            return false;
        }

        DerivedAttributeState genericDerived = InventoryStateUtility.CalculateDerivedAttributes(boundState);
        int genericHealth = 0;
        int genericMana = 0;
        if (entry.runtimeData?.statModifiers != null)
        {
            foreach (var modifier in entry.runtimeData.statModifiers)
            {
                if (modifier == null || string.IsNullOrWhiteSpace(modifier.statKey) || modifier.value <= 0)
                    continue;

                string statKey = modifier.statKey.Trim().ToLowerInvariant();
                if (statKey == "max_health")
                    genericHealth += modifier.value;
                else if (statKey == "max_mana")
                    genericMana += modifier.value;
            }
        }

        genericHealth = Mathf.Min(genericHealth, genericDerived.maxHealthTotal - boundState.attributes.currentHealth);
        genericMana = Mathf.Min(genericMana, genericDerived.maxManaTotal - boundState.attributes.currentMana);
        boundState.attributes.currentHealth += genericHealth;
        boundState.attributes.currentMana += genericMana;
        InventoryStateUtility.NormalizeResourceCaps(boundState, InventoryStateUtility.CalculateDerivedAttributes(boundState));
        boundEnvironment.AddTag("物品已使用");

        if (genericHealth > 0 || genericMana > 0)
        {
            message = $"{displayName} 已使用，生命 +{genericHealth}，灵力 +{genericMana}";
        }
        else
        {
            message = string.IsNullOrWhiteSpace(entry.runtimeData?.effectText)
                ? displayName + " 已使用。"
                : displayName + " 已使用：" + entry.runtimeData.effectText;
        }

        return true;
    }

    private bool TryConsumeExactInventoryEntry(ItemInventoryEntry entry)
    {
        if (boundState == null || entry == null)
            return false;

        return InventoryStateUtility.TryRemoveItem(
            boundState,
            entry.runtimeData?.instanceId,
            null,
            1,
            out _);
    }

    private static bool ContainsItemKeyword(string source, string keyword)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               !string.IsNullOrWhiteSpace(keyword) &&
               source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnItemViewHoverEnter(InventoryItemView itemView)
    {
        if (itemTooltipRoot == null || itemView == null)
            return;

        string title = BuildTooltipTitle(itemView.Entry, itemView.Template);
        string detail = BuildCompactTooltipDetail(itemView.Entry, itemView.Template);
        bool hasBoundTooltipText = itemTooltipNameText != null || itemTooltipDetailText != null || itemTooltipText != null;
        if (!hasBoundTooltipText)
            return;

        itemTooltipRoot.SetActive(true);

        if (itemTooltipNameText != null)
            itemTooltipNameText.text = title;

        if (itemTooltipDetailText != null)
            itemTooltipDetailText.text = detail;

        if (itemTooltipText != null &&
            itemTooltipText != itemTooltipNameText &&
            itemTooltipText != itemTooltipDetailText)
        {
            itemTooltipText.text = $"{title}\n{detail}";
        }
        else if (itemTooltipNameText == null && itemTooltipDetailText == itemTooltipText && itemTooltipText != null)
        {
            itemTooltipText.text = $"{title}\n{detail}";
        }

        var tooltipRect = itemTooltipRoot.transform as RectTransform;
        if (tooltipRect != null && itemView.transform is RectTransform sourceRect)
        {
            tooltipRect.SetAsLastSibling();
            PositionTooltip(tooltipRect, sourceRect);
        }
    }

    private void OnItemViewHoverExit(InventoryItemView itemView)
    {
        HideItemTooltip();
    }

    private void HideItemTooltip()
    {
        if (itemTooltipRoot != null)
            itemTooltipRoot.SetActive(false);
    }

    private void ApplyTooltipLayout(RectTransform tooltipRect, string title, string detail)
    {
        if (tooltipRect == null)
            return;

        if (itemTooltipNameText != null && itemTooltipDetailText != null)
        {
            LayoutSplitTooltip(tooltipRect, title, detail);
            return;
        }

        if (itemTooltipText != null)
            LayoutSingleTooltip(tooltipRect, $"{title}\n{detail}");
    }

    private void LayoutSplitTooltip(RectTransform tooltipRect, string title, string detail)
    {
        RectTransform titleRect = itemTooltipNameText.transform as RectTransform;
        RectTransform detailRect = itemTooltipDetailText.transform as RectTransform;
        if (titleRect == null || detailRect == null)
            return;

        float contentMaxWidth = TooltipMaxWidth - TooltipPadding * 2f;
        float contentMinWidth = TooltipMinWidth - TooltipPadding * 2f;
        float titleWidth = itemTooltipNameText.GetPreferredValues(title, contentMaxWidth, 0f).x;
        float detailWidth = itemTooltipDetailText.GetPreferredValues(detail, contentMaxWidth, 0f).x;
        float contentWidth = Mathf.Clamp(Mathf.Max(titleWidth, detailWidth), contentMinWidth, contentMaxWidth);

        float titleHeight = itemTooltipNameText.GetPreferredValues(title, contentWidth, 0f).y;
        float detailHeight = itemTooltipDetailText.GetPreferredValues(detail, contentWidth, 0f).y;
        float height = TooltipPadding + titleHeight + TooltipSpacing + detailHeight + TooltipPadding;

        if (height > TooltipMaxHeight)
        {
            detailHeight = Mathf.Max(120f, TooltipMaxHeight - TooltipPadding * 2f - titleHeight - TooltipSpacing);
            height = TooltipPadding + titleHeight + TooltipSpacing + detailHeight + TooltipPadding;
        }

        tooltipRect.pivot = new Vector2(0f, 1f);
        tooltipRect.sizeDelta = new Vector2(contentWidth + TooltipPadding * 2f, height);

        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(TooltipPadding, -TooltipPadding);
        titleRect.sizeDelta = new Vector2(contentWidth, titleHeight);

        detailRect.anchorMin = new Vector2(0f, 1f);
        detailRect.anchorMax = new Vector2(0f, 1f);
        detailRect.pivot = new Vector2(0f, 1f);
        detailRect.anchoredPosition = new Vector2(TooltipPadding, -(TooltipPadding + titleHeight + TooltipSpacing));
        detailRect.sizeDelta = new Vector2(contentWidth, detailHeight);
    }

    private void LayoutSingleTooltip(RectTransform tooltipRect, string content)
    {
        RectTransform textRect = itemTooltipText.transform as RectTransform;
        if (textRect == null)
            return;

        float contentMaxWidth = TooltipMaxWidth - TooltipPadding * 2f;
        float contentMinWidth = TooltipMinWidth - TooltipPadding * 2f;
        float preferredWidth = itemTooltipText.GetPreferredValues(content, contentMaxWidth, 0f).x;
        float contentWidth = Mathf.Clamp(preferredWidth, contentMinWidth, contentMaxWidth);
        float contentHeight = itemTooltipText.GetPreferredValues(content, contentWidth, 0f).y;
        float height = Mathf.Min(TooltipMaxHeight, contentHeight + TooltipPadding * 2f);

        tooltipRect.pivot = new Vector2(0f, 1f);
        tooltipRect.sizeDelta = new Vector2(contentWidth + TooltipPadding * 2f, height);

        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(0f, 1f);
        textRect.pivot = new Vector2(0f, 1f);
        textRect.anchoredPosition = new Vector2(TooltipPadding, -TooltipPadding);
        textRect.sizeDelta = new Vector2(contentWidth, height - TooltipPadding * 2f);
    }

    private void PositionTooltip(RectTransform tooltipRect, RectTransform sourceRect)
    {
        RectTransform parentRect = tooltipRect.parent as RectTransform;
        if (parentRect == null)
            return;

        var corners = new Vector3[4];
        sourceRect.GetWorldCorners(corners);
        Vector2 topLeftScreen = RectTransformUtility.WorldToScreenPoint(null, corners[1]);
        Vector2 topRightScreen = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        Vector2 bottomLeftScreen = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        Vector2 bottomRightScreen = RectTransformUtility.WorldToScreenPoint(null, corners[3]);
        Vector2 rightCenterScreen = (topRightScreen + bottomRightScreen) * 0.5f;
        Vector2 leftCenterScreen = (topLeftScreen + bottomLeftScreen) * 0.5f;

        float width = tooltipRect.rect.width;
        float height = tooltipRect.rect.height;
        bool showOnRight = rightCenterScreen.x + TooltipScreenMargin + width <= Screen.width - TooltipScreenMargin;

        tooltipRect.pivot = showOnRight ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);

        Vector2 targetScreen = showOnRight
            ? rightCenterScreen + new Vector2(TooltipScreenMargin, 0f)
            : leftCenterScreen + new Vector2(-TooltipScreenMargin, 0f);

        if (targetScreen.x < TooltipScreenMargin)
            targetScreen.x = TooltipScreenMargin;

        if (targetScreen.x > Screen.width - TooltipScreenMargin)
            targetScreen.x = Screen.width - TooltipScreenMargin;

        float halfHeight = height * 0.5f;
        if (targetScreen.y - halfHeight < TooltipScreenMargin)
            targetScreen.y = TooltipScreenMargin + halfHeight;

        if (targetScreen.y + halfHeight > Screen.height - TooltipScreenMargin)
            targetScreen.y = Screen.height - TooltipScreenMargin - halfHeight;

        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parentRect, targetScreen, null, out var worldPoint))
            tooltipRect.position = worldPoint;
    }

    private string BuildTooltipTitle(ItemInventoryEntry entry, ItemTemplateData template)
    {
        if (entry == null)
            return string.Empty;

        return entry.runtimeData?.name ?? template?.displayName ?? entry.templateId;
    }

    private string BuildCompactTooltipDetail(ItemInventoryEntry entry, ItemTemplateData template)
    {
        if (entry == null)
            return string.Empty;

        string rarity = string.IsNullOrWhiteSpace(entry.runtimeData?.rarity) ? "普通" : entry.runtimeData.rarity;
        string description = string.IsNullOrWhiteSpace(entry.runtimeData?.description)
            ? template?.templateDescription ?? "无描述"
            : entry.runtimeData.description;

        return $"稀有度: {rarity}\n说明: {description}";
    }

    private static string TrimStatusText(string rawText, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return "暂无";

        string trimmed = rawText.Trim();
        if (maxLength <= 3 || trimmed.Length <= maxLength)
            return trimmed;

        return trimmed.Substring(0, maxLength - 3) + "...";
    }

    private string BuildTooltipDetail(ItemInventoryEntry entry, ItemTemplateData template, bool isEquipped, EquipSlotType slotType)
    {
        if (entry == null)
            return string.Empty;

        string rarity = string.IsNullOrWhiteSpace(entry.runtimeData?.rarity) ? "普通" : entry.runtimeData.rarity;
        string description = string.IsNullOrWhiteSpace(entry.runtimeData?.description)
            ? template?.templateDescription ?? "无描述"
            : entry.runtimeData.description;

        return $"稀有度: {rarity}\n说明: {description}";
    }

    private string BuildTooltipText(ItemInventoryEntry entry, ItemTemplateData template, bool isEquipped, EquipSlotType slotType)
    {
        if (entry == null)
            return string.Empty;

        string slotText = template != null && template.IsEquipment
            ? template.equipSlot.ToString()
            : (isEquipped ? slotType.ToString() : "背包物品");
        string countText = entry.count > 1 ? $"数量: {entry.count}\n" : string.Empty;
        string rarity = string.IsNullOrWhiteSpace(entry.runtimeData?.rarity) ? "普通" : entry.runtimeData.rarity;
        string description = string.IsNullOrWhiteSpace(entry.runtimeData?.description)
            ? template?.templateDescription ?? "无描述"
            : entry.runtimeData.description;
        string effect = string.IsNullOrWhiteSpace(entry.runtimeData?.effectText) ? "无" : entry.runtimeData.effectText;
        string modifiers = entry.runtimeData?.statModifiers != null && entry.runtimeData.statModifiers.Count > 0
            ? InventoryStateUtility.BuildModifierText(entry.runtimeData.statModifiers)
            : "none";

        return
            $"{entry.runtimeData?.name ?? template?.displayName ?? entry.templateId}\n" +
            $"模板: {entry.templateId}\n" +
            $"分类: {template?.itemKind.ToString() ?? "Unknown"}\n" +
            $"槽位: {slotText}\n" +
            $"稀有度: {rarity}\n" +
            countText +
            $"说明: {description}\n" +
            $"效果: {effect}\n" +
            $"词条: {modifiers}";
    }

    private static TMP_Text FindTooltipText(IEnumerable<TMP_Text> texts, params string[] keywords)
    {
        if (texts == null || keywords == null || keywords.Length == 0)
            return null;

        foreach (var text in texts)
        {
            if (text == null || string.IsNullOrWhiteSpace(text.name))
                continue;

            foreach (string keyword in keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    text.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return text;
                }
            }
        }

        return null;
    }

    private void ClearSpawnedItemViews()
    {
        foreach (var view in activeItemViews)
        {
            if (view != null)
                Destroy(view.gameObject);
        }

        activeItemViews.Clear();
    }

    private RectTransform ResolveParentRoot(bool isEquipped)
    {
        if (isEquipped)
            return equipmentStartPosition != null ? equipmentStartPosition.parent as RectTransform : characterPanelRoot != null ? characterPanelRoot.transform as RectTransform : null;

        return bagStartPosition != null ? bagStartPosition.parent as RectTransform : bagPanelRoot != null ? bagPanelRoot.transform as RectTransform : null;
    }

    private Vector2 GetBagPosition(int row, int column)
    {
        if (bagStartPosition == null)
            return Vector2.zero;

        return bagStartPosition.anchoredPosition + new Vector2(column * bagStepX, -row * bagStepY);
    }

    private Vector2 GetEquipmentPosition(int index)
    {
        if (equipmentStartPosition == null)
            return Vector2.zero;

        return equipmentStartPosition.anchoredPosition + new Vector2(index * equipmentStepX, 0f);
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

    private GameObject FindChildGameObject(string childName)
    {
        var child = FindChildTransform(childName);
        return child != null ? child.gameObject : null;
    }

    private RectTransform FindChildRectTransform(string childName)
    {
        return FindChildTransform(childName) as RectTransform;
    }

    private Transform FindChildTransform(string childName)
    {
        if (string.IsNullOrWhiteSpace(childName))
            return null;

        var transforms = GetComponentsInChildren<Transform>(true);
        foreach (var candidate in transforms)
        {
            if (candidate != null && string.Equals(candidate.name, childName, StringComparison.Ordinal))
                return candidate;
        }

        return null;
    }

    private void OnDestroy()
    {
        UnregisterCenterToastListener();
    }

    private void OnEnable()
    {
        if (centerToastCoroutine == null && centerToastQueue.Count > 0 && gameObject.activeInHierarchy)
            centerToastCoroutine = StartCoroutine(PlayCenterToastQueue());
    }
}
