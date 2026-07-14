using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ExperimentSceneCatalog", menuName = "CARE-XR/Experiment Scene Catalog")]
public sealed class ExperimentSceneCatalog : ScriptableObject
{
    [Serializable]
    public sealed class SceneEntry
    {
        public string id = "scene";
        public string displayName = "Experiment Scene";
        public string sceneName = "";
        public string dataNamespace = "Experiment_Data";
        public bool enabled = true;

        public SceneEntry()
        {
        }

        public SceneEntry(string id, string displayName, string sceneName, string dataNamespace)
        {
            this.id = id;
            this.displayName = displayName;
            this.sceneName = sceneName;
            this.dataNamespace = dataNamespace;
            enabled = true;
        }
    }

    [SerializeField] List<SceneEntry> scenes = new List<SceneEntry>();

    public IReadOnlyList<SceneEntry> Scenes => scenes;

    public List<SceneEntry> GetEnabledScenes()
    {
        var result = new List<SceneEntry>(scenes.Count);
        for (int i = 0; i < scenes.Count; i++)
        {
            SceneEntry entry = scenes[i];
            if (entry == null || !entry.enabled || string.IsNullOrWhiteSpace(entry.sceneName))
                continue;
            result.Add(entry);
        }
        return result;
    }

    public void ReplaceEntries(IEnumerable<SceneEntry> entries)
    {
        scenes.Clear();
        if (entries != null)
            scenes.AddRange(entries);
    }
}
