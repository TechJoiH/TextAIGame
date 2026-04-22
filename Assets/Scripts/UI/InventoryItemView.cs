using System;
using StateData.Items;
using StateData.Role;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventoryItemView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image iconImage;
    public TMP_Text quantityText;
    public Button button;

    private Action<InventoryItemView> clickCallback;
    private Action<InventoryItemView> hoverEnterCallback;
    private Action<InventoryItemView> hoverExitCallback;

    public ItemInventoryEntry Entry { get; private set; }
    public ItemTemplateData Template { get; private set; }
    public bool IsEquipped { get; private set; }
    public EquipSlotType EquipSlotType { get; private set; }
    public int InventoryIndex { get; private set; }
    public string DisplayName => Entry?.runtimeData?.name ?? Template?.displayName ?? Entry?.templateId ?? "未命名物品";

    public static InventoryItemView CreateFallback(RectTransform parent, RectTransform anchorSource)
    {
        var root = new GameObject("InventoryItemView", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(InventoryItemView));
        root.transform.SetParent(parent, false);

        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = anchorSource != null ? anchorSource.anchorMin : new Vector2(0.5f, 0.5f);
        rect.anchorMax = anchorSource != null ? anchorSource.anchorMax : new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var image = root.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.95f);
        image.preserveAspect = true;
        root.GetComponent<Button>().transition = Selectable.Transition.ColorTint;

        var quantityObject = new GameObject("Quantity", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        quantityObject.transform.SetParent(root.transform, false);
        var quantityRect = quantityObject.GetComponent<RectTransform>();
        quantityRect.anchorMin = new Vector2(1f, 0f);
        quantityRect.anchorMax = new Vector2(1f, 0f);
        quantityRect.pivot = new Vector2(1f, 0f);
        quantityRect.anchoredPosition = new Vector2(-4f, 4f);
        quantityRect.sizeDelta = new Vector2(48f, 24f);

        var quantityLabel = quantityObject.GetComponent<TextMeshProUGUI>();
        quantityLabel.font = TMP_Settings.defaultFontAsset;
        quantityLabel.fontSize = 20f;
        quantityLabel.alignment = TextAlignmentOptions.BottomRight;
        quantityLabel.color = new Color(0.3f, 0.2f, 0.1f, 1f);

        var view = root.GetComponent<InventoryItemView>();
        view.iconImage = image;
        view.quantityText = quantityLabel;
        view.button = root.GetComponent<Button>();
        return view;
    }

    public void Bind(
        ItemInventoryEntry entry,
        ItemTemplateData template,
        bool isEquipped,
        Action<InventoryItemView> onClick,
        Action<InventoryItemView> onHoverEnter,
        Action<InventoryItemView> onHoverExit,
        EquipSlotType equipSlotType = EquipSlotType.None,
        int inventoryIndex = -1)
    {
        Entry = entry;
        Template = template;
        IsEquipped = isEquipped;
        EquipSlotType = equipSlotType;
        InventoryIndex = inventoryIndex;
        clickCallback = onClick;
        hoverEnterCallback = onHoverEnter;
        hoverExitCallback = onHoverExit;

        iconImage ??= GetComponent<Image>() ?? GetComponentInChildren<Image>(true);
        button ??= GetComponent<Button>() ?? GetComponentInChildren<Button>(true);
        quantityText ??= GetComponentInChildren<TMP_Text>(true);

        if (iconImage != null)
        {
            iconImage.sprite = template?.ResolveIcon();
            iconImage.type = Image.Type.Simple;
            iconImage.preserveAspect = true;
            iconImage.enabled = true;
            iconImage.color = iconImage.sprite != null ? Color.white : new Color(0.9f, 0.85f, 0.75f, 0.9f);

            if (iconImage.sprite == null)
            {
                Debug.LogWarning(
                    $"[InventoryItemView] Missing icon for item. " +
                    $"templateId={entry?.templateId}, " +
                    $"runtimeName={entry?.runtimeData?.name}, " +
                    $"iconPath={template?.iconPath}");
            }
        }
        else
        {
            Debug.LogWarning($"[InventoryItemView] Missing Image component on {name}.");
        }

        if (quantityText != null)
        {
            bool showCount = entry != null && entry.count > 1;
            quantityText.gameObject.SetActive(showCount);
            if (showCount)
                quantityText.text = entry.count.ToString();
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => clickCallback?.Invoke(this));
        }
        else
        {
            Debug.LogWarning($"[InventoryItemView] Missing Button component on {name}.");
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hoverEnterCallback?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hoverExitCallback?.Invoke(this);
    }
}
