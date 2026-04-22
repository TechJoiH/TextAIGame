using System;
using System.Collections.Generic;
using System.IO;
using Data.KnowledgeGraph;
using Logic.GraphRAG;
using Logic.Inventory;
using Logic.Memory;
using StateData.Environment;
using StateData.Items;
using StateData.Role;
using UnityEngine;

[Serializable]
public class SaveSlotHeader
{
    public string saveId;
    public string timeDisplay;
    public string summary;
}

[Serializable]
public class FullSaveData
{
    public RoleState roleState;
    public EnvironmentState environmentState;
    public MemorySnapshot memorySnapshot;
    public KnowledgeGraphSnapshot knowledgeGraphSnapshot;
}

[Serializable]
public class SaveManifest
{
    public List<SaveSlotHeader> slots = new List<SaveSlotHeader>();
}

public class GameSaveMgr : MonoSingleton<GameSaveMgr>
{
    private const string ManifestName = "save_manifest";
    private const int MaxSaveSlots = 20;

    private SaveManifest _manifest;
    private bool _manifestLoaded;

    protected override void Awake()
    {
        base.Awake();
        LoadManifest();
    }

    private void LoadManifest()
    {
        string path = Path.Combine(Application.persistentDataPath, ManifestName + ".json");
        if (File.Exists(path))
        {
            try
            {
                _manifest = LitJson.JsonMapper.ToObject<SaveManifest>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[GameSaveMgr] Failed to load manifest: {exception.Message}");
                _manifest = null;
            }
        }

        _manifest ??= new SaveManifest();
        _manifest.slots ??= new List<SaveSlotHeader>();
        _manifestLoaded = true;
    }

    public void CreateCheckpoint(RoleState currentState, EnvironmentState envState, string userLastInput)
    {
        if (currentState == null)
        {
            Debug.LogError("[GameSaveMgr] currentState is null, checkpoint skipped.");
            return;
        }

        string id = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fullSave = new FullSaveData
        {
            roleState = currentState,
            environmentState = envState,
            memorySnapshot = MemoryManager.Instance.GetSnapshot(),
            knowledgeGraphSnapshot = GraphRAGManager.Instance.GetSnapshot(),
        };

        JsonMgr.Instance.SaveData($"save_{id}", fullSave, JsonType.LitJson);

        string summary = userLastInput ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(envState?.locationName))
            summary = $"{envState.locationName} · {summary}".Trim().TrimEnd('·');
        else if (string.IsNullOrWhiteSpace(summary) && !string.IsNullOrWhiteSpace(envState?.currentObjective))
            summary = envState.currentObjective;

        if (summary.Length > 18)
            summary = summary.Substring(0, 18) + "...";

        _manifest.slots.Add(new SaveSlotHeader
        {
            saveId = id,
            timeDisplay = DateTime.Now.ToString("MM/dd HH:mm"),
            summary = summary,
        });

        while (_manifest.slots.Count > MaxSaveSlots)
            _manifest.slots.RemoveAt(0);

        JsonMgr.Instance.SaveData(ManifestName, _manifest, JsonType.LitJson);
    }

    public void CreateCheckpoint(RoleState currentState, string userLastInput)
    {
        CreateCheckpoint(currentState, null, userLastInput);
    }

    public FullSaveData LoadCheckpointFull(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId))
            return null;

        return EnsureCompatibility(JsonMgr.Instance.LoadData<FullSaveData>($"save_{saveId}", JsonType.LitJson));
    }

    public RoleState LoadCheckpoint(string saveId)
    {
        return LoadCheckpointFull(saveId)?.roleState;
    }

    public List<SaveSlotHeader> GetAllCheckpoints()
    {
        if (!_manifestLoaded || _manifest == null)
            LoadManifest();

        if (_manifest?.slots == null)
            return new List<SaveSlotHeader>();

        var reversed = new List<SaveSlotHeader>(_manifest.slots);
        reversed.Reverse();
        return reversed;
    }

    public void DeleteCheckpoint(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId) || _manifest?.slots == null)
            return;

        _manifest.slots.RemoveAll(slot => slot.saveId == saveId);
        JsonMgr.Instance.SaveData(ManifestName, _manifest, JsonType.LitJson);
    }

    public static FullSaveData EnsureCompatibility(FullSaveData saveData, SceneItemLibraryData itemLibrary = null)
    {
        if (saveData == null)
            return null;

        saveData.roleState ??= new RoleState();
        saveData.environmentState ??= EnvironmentState.GetDefault();
        saveData.environmentState.EnsureCollections();

        saveData.memorySnapshot ??= new MemorySnapshot
        {
            shortTermMemory = new List<DialogueEntry>(),
            longTermMemories = new List<LongTermMemory>(),
            totalTurns = 0,
        };
        saveData.memorySnapshot.shortTermMemory ??= new List<DialogueEntry>();
        saveData.memorySnapshot.longTermMemories ??= new List<LongTermMemory>();

        saveData.knowledgeGraphSnapshot ??= new KnowledgeGraphSnapshot
        {
            entities = new List<KnowledgeEntity>(),
            relations = new List<KnowledgeRelation>(),
            discoveredEntityIds = new List<string>(),
        };
        saveData.knowledgeGraphSnapshot.entities ??= new List<KnowledgeEntity>();
        saveData.knowledgeGraphSnapshot.relations ??= new List<KnowledgeRelation>();
        saveData.knowledgeGraphSnapshot.discoveredEntityIds ??= new List<string>();

        InventoryStateUtility.EnsureCompatibility(saveData.roleState, itemLibrary);
        return saveData;
    }
}
