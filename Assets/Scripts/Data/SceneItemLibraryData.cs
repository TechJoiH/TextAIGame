using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StateData.Items
{
    public enum ItemKind
    {
        Consumable = 0,
        Equipment = 1,
        Material = 2,
        Quest = 3,
        Misc = 4,
    }

    public enum EquipSlotType
    {
        None = 0,
        Head = 1,
        Body = 2,
        Legs = 3,
        Feet = 4,
        Weapon = 5,
    }

    [Serializable]
    public sealed class ItemTemplateData
    {
        public string templateId;
        public string displayName;
        public ItemKind itemKind;
        public EquipSlotType equipSlot;
        public bool stackable;
        public string iconPath;
        public Sprite iconSprite;
        public string allowedSceneId;

        [TextArea(2, 5)]
        public string templateDescription;

        public bool IsEquipment => itemKind == ItemKind.Equipment && equipSlot != EquipSlotType.None;

        public Sprite ResolveIcon()
        {
            if (iconSprite != null)
                return iconSprite;

            string resourcePath = NormalizeResourcePath(iconPath);
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;

            iconSprite = Resources.Load<Sprite>(resourcePath);
            return iconSprite;
        }

        private static string NormalizeResourcePath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return null;

            string normalized = rawPath.Replace("\\", "/").Trim();
            const string resourcesMarker = "Resources/";
            int resourcesIndex = normalized.IndexOf(resourcesMarker, StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex >= 0)
                normalized = normalized.Substring(resourcesIndex + resourcesMarker.Length);

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("Assets/".Length);

            if (normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.LastIndexOf('.'));
            }

            return normalized.Trim('/');
        }
    }

    [CreateAssetMenu(fileName = "SceneItemLibrary", menuName = "GameData/Scene Item Library")]
    public sealed class SceneItemLibraryData : ScriptableObject
    {
        public string sceneId;
        public List<ItemTemplateData> items = new List<ItemTemplateData>();

        private readonly Dictionary<string, ItemTemplateData> _templateLookup = new Dictionary<string, ItemTemplateData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ItemTemplateData> _nameLookup = new Dictionary<string, ItemTemplateData>(StringComparer.OrdinalIgnoreCase);
        private bool _indexed;

        public void EnsureIndex()
        {
            if (_indexed)
                return;

            _indexed = true;
            _templateLookup.Clear();
            _nameLookup.Clear();

            if (items == null)
                items = new List<ItemTemplateData>();

            foreach (var template in items)
            {
                if (template == null || string.IsNullOrWhiteSpace(template.templateId))
                    continue;

                _templateLookup[template.templateId.Trim()] = template;

                if (!string.IsNullOrWhiteSpace(template.displayName))
                    _nameLookup[template.displayName.Trim()] = template;
            }
        }

        public ItemTemplateData GetTemplate(string templateId)
        {
            EnsureIndex();
            if (string.IsNullOrWhiteSpace(templateId))
                return null;

            _templateLookup.TryGetValue(templateId.Trim(), out var template);
            return template;
        }

        public ItemTemplateData FindTemplateByDisplayName(string displayName)
        {
            EnsureIndex();
            if (string.IsNullOrWhiteSpace(displayName))
                return null;

            if (_nameLookup.TryGetValue(displayName.Trim(), out var directMatch))
                return directMatch;

            foreach (var template in items)
            {
                if (template == null || string.IsNullOrWhiteSpace(template.displayName))
                    continue;

                if (displayName.IndexOf(template.displayName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    template.displayName.IndexOf(displayName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return template;
                }
            }

            return null;
        }

        public bool IsTemplateAllowed(string templateId)
        {
            var template = GetTemplate(templateId);
            if (template == null)
                return false;

            return string.IsNullOrWhiteSpace(template.allowedSceneId) ||
                   string.IsNullOrWhiteSpace(sceneId) ||
                   string.Equals(template.allowedSceneId, sceneId, StringComparison.OrdinalIgnoreCase);
        }

        public string BuildPromptSummary(int maxCount = 24)
        {
            EnsureIndex();

            if (items == null || items.Count == 0)
                return "No scene item templates are available.";

            var builder = new StringBuilder();
            int count = 0;
            foreach (var template in items)
            {
                if (template == null || string.IsNullOrWhiteSpace(template.templateId))
                    continue;

                if (count > 0)
                    builder.AppendLine();

                builder.Append("- ")
                    .Append(template.templateId)
                    .Append(" | ")
                    .Append(string.IsNullOrWhiteSpace(template.displayName) ? "Unnamed" : template.displayName)
                    .Append(" | kind=")
                    .Append(template.itemKind)
                    .Append(" | slot=")
                    .Append(template.equipSlot)
                    .Append(" | stackable=")
                    .Append(template.stackable ? "true" : "false");

                if (!string.IsNullOrWhiteSpace(template.templateDescription))
                    builder.Append(" | note=").Append(template.templateDescription.Trim());

                count++;
                if (count >= maxCount)
                    break;
            }

            return builder.ToString();
        }
    }
}
