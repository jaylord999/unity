#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Keeps the magic tuning in a saved asset, so the numbers a designer dials in can never be
    /// lost.
    ///
    /// Three things used to eat them:
    ///   * tuning while the game plays - Unity throws every change away when play mode stops,
    ///   * reloading the scene (or the player prefab) before the scene was saved,
    ///   * rebuilding the five levels from the Hovl pack.
    ///
    /// Now the tuning window writes to (and reads from)
    /// <c>Assets/MCP/MysticMap/MagicTuningPreset.asset</c>, and every play session leaves a
    /// snapshot in <c>MagicTuningLastPlay.asset</c>: stopping the game puts that snapshot straight
    /// back on the component, so what was tuned in game is what the scene keeps.
    /// </summary>
    [InitializeOnLoad]
    public static class MagicTuningPresetStore
    {
        public const string AssetPath = "Assets/MCP/MysticMap/MagicTuningPreset.asset";
        public const string BackupPath = "Assets/MCP/MysticMap/MagicTuningLastPlay.asset";
        const string Folder = "Assets/MCP/MysticMap";
        const string AutoRestoreKey = "MysticMap.Magic.RestoreTuningAfterPlay";
        const string SnapshotKey = "MysticMap.Magic.TuningSnapshot";

        /// <summary>Put the play-mode tuning back on the component when the game stops.</summary>
        public static bool RestoreAfterPlay
        {
            get { return EditorPrefs.GetBool(AutoRestoreKey, true); }
            set { EditorPrefs.SetBool(AutoRestoreKey, value); }
        }

        /// <summary>True once the tuning has been saved by hand.</summary>
        public static bool HasSaved => Load() != null;

        /// <summary>True once a play session has left a snapshot behind.</summary>
        public static bool HasBackup => LoadAt(BackupPath) != null;

        static MagicTuningPresetStore()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>The preset the user saved by hand, or null.</summary>
        public static MagicTuningPreset Load() => LoadAt(AssetPath);

        /// <summary>The snapshot the last play session left behind, or null.</summary>
        public static MagicTuningPreset LoadBackup() => LoadAt(BackupPath);

        static MagicTuningPreset LoadAt(string path) => AssetDatabase.LoadAssetAtPath<MagicTuningPreset>(path);

        /// <summary>An asset at the given path, created on the spot when the tools must write to it.</summary>
        static MagicTuningPreset LoadOrCreateAt(string path, string name)
        {
            MagicTuningPreset preset = LoadAt(path);
            if (preset != null) return preset;

            if (!AssetDatabase.IsValidFolder(Folder))
            {
                Debug.LogWarning("[MysticMap] '" + Folder + "' is missing, so the magic tuning " +
                                 "cannot be saved to an asset.");
                return null;
            }

            preset = ScriptableObject.CreateInstance<MagicTuningPreset>();
            preset.name = name;
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            return preset;
        }

        /// <summary>Writes everything the SlimeMagic component holds into the saved preset.</summary>
        public static bool Save(SlimeMagic magic)
        {
            if (magic == null) return false;

            MagicTuningPreset preset = LoadOrCreateAt(AssetPath, "Magic tuning preset");
            if (preset == null) return false;

            preset.Capture(magic);
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Writes the saved preset back onto the component. Undoable, and the scene (or the prefab)
        /// is marked dirty so the change really sticks.
        /// </summary>
        public static bool Apply(SlimeMagic magic) => ApplyPreset(Load(), magic, "Load magic tuning");

        /// <summary>Writes the snapshot of the last play session back onto the component.</summary>
        public static bool ApplyBackup(SlimeMagic magic) =>
            ApplyPreset(LoadBackup(), magic, "Restore the play-mode magic tuning");

        static bool ApplyPreset(MagicTuningPreset preset, SlimeMagic magic, string undoLabel)
        {
            if (preset == null || magic == null) return false;

            Undo.RecordObject(magic, undoLabel);
            preset.Apply(magic);

            EditorUtility.SetDirty(magic);
            Dirty(magic);
            return true;
        }

        /// <summary>Remembers the tuning as it is right now, wherever the component lives.</summary>
        public static void MarkDirty(SlimeMagic magic)
        {
            if (magic == null) return;

            EditorUtility.SetDirty(magic);
            Dirty(magic);
        }

        /// <summary>Tells Unity that an edited component has to be written back to disk.</summary>
        static void Dirty(SlimeMagic magic)
        {
            if (magic == null) return;

            var scene = magic.gameObject.scene;
            if (scene.IsValid() && scene.isLoaded && !Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(scene);

            SceneView.RepaintAll();
        }

        // ---- automatic snapshot / restore around play mode -------------------
        // Unity reloads the whole domain when play mode ends, so the snapshot travels through
        // SessionState (a JSON string) and only reaches the asset once the editor is back.
        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Snapshot();
                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode) return;

            EditorApplication.delayCall += FinishPlaySession;
        }

        /// <summary>Remembers the live tuning, because stopping the game is about to throw it away.</summary>
        static void Snapshot()
        {
            SlimeMagic magic = SlimeMagicSetup.ActiveMagic();
            if (magic == null) return;

            // The debris prefab list is left out of the snapshot: object references do not survive
            // a trip through JSON, and the component keeps its own list anyway.
            var snapshot = ScriptableObject.CreateInstance<MagicTuningPreset>();
            snapshot.Capture(magic, false);

            SessionState.SetString(SnapshotKey, JsonUtility.ToJson(snapshot));
            Object.DestroyImmediate(snapshot);
        }

        /// <summary>Writes the snapshot into its own asset and, when asked, puts it back.</summary>
        static void FinishPlaySession()
        {
            string json = SessionState.GetString(SnapshotKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            SessionState.EraseString(SnapshotKey);

            MagicTuningPreset backup = LoadOrCreateAt(BackupPath, "Magic tuning (last play session)");
            if (backup != null)
            {
                JsonUtility.FromJsonOverwrite(json, backup);
                EditorUtility.SetDirty(backup);
                AssetDatabase.SaveAssets();
            }

            if (!RestoreAfterPlay) return;

            SlimeMagic magic = SlimeMagicSetup.ActiveMagic();
            if (magic == null) return;
            if (!ApplyPreset(backup, magic, "Restore the play-mode magic tuning")) return;

            Debug.Log("[MysticMap] The magic tuning you changed while playing is back on '" +
                      magic.gameObject.name + "'; it is also kept in " + BackupPath + ".");
        }
    }
}
#endif
