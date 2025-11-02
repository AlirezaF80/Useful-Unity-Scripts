using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Tables;
using UnityEditor;
using UnityEditor.Localization;

namespace LocalizationTools
{
    /// <summary>
    /// Unity Editor script to find LocalizeStringEvent components with missing references in the current scene.
    /// This tool helps identify localization issues that can cause runtime problems.
    /// 
    /// Common causes of missing references:
    /// 1. Empty Table Reference or Entry Reference
    /// 2. Missing Table Collection (deleted or moved)
    /// 3. Missing Table Entry (deleted or renamed)
    /// 4. Invalid GUIDs (corrupted serialization)
    /// 5. Serialization issues during scene loading
    /// </summary>
    public class LocalizeStringEventReferenceChecker : EditorWindow
    {
        private Vector2 scrollPosition;
        private List<MissingReferenceInfo> missingReferences = new List<MissingReferenceInfo>();
        private bool showEmptyReferences = true;
        private bool showMissingTables = true;
        private bool showMissingEntries = true;
        private bool showInvalidGuids = true;

        private struct MissingReferenceInfo
        {
            public GameObject gameObject;
            public LocalizeStringEvent localizeStringEvent;
            public string issueType;
            public string description;
            public string tableReference;
            public string entryReference;
        }

        [MenuItem("Tools/Localization/Check LocalizeStringEvent References")]
        public static void ShowWindow()
        {
            GetWindow<LocalizeStringEventReferenceChecker>("LocalizeStringEvent Reference Checker");
        }

        private void OnEnable()
        {
            RefreshMissingReferences();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("LocalizeStringEvent Reference Checker", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Filter options
            EditorGUILayout.LabelField("Filter Options:", EditorStyles.boldLabel);
            showEmptyReferences = EditorGUILayout.Toggle("Show Empty References", showEmptyReferences);
            showMissingTables = EditorGUILayout.Toggle("Show Missing Table Collections", showMissingTables);
            showMissingEntries = EditorGUILayout.Toggle("Show Missing Table Entries", showMissingEntries);
            showInvalidGuids = EditorGUILayout.Toggle("Show Invalid GUIDs", showInvalidGuids);

            EditorGUILayout.Space();

            // Refresh button
            if (GUILayout.Button("Refresh Check"))
            {
                RefreshMissingReferences();
            }

            EditorGUILayout.Space();

            // Results
            var filteredReferences = GetFilteredReferences();
            EditorGUILayout.LabelField($"Found {filteredReferences.Count} issues:", EditorStyles.boldLabel);

            if (filteredReferences.Count == 0)
            {
                EditorGUILayout.HelpBox("No missing references found! All LocalizeStringEvent components in the scene have valid references.", MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var reference in filteredReferences)
            {
                DrawMissingReference(reference);
            }

            EditorGUILayout.EndScrollView();
        }

        private List<MissingReferenceInfo> GetFilteredReferences()
        {
            return missingReferences.Where(r => 
                (showEmptyReferences && r.issueType == "Empty Reference") ||
                (showMissingTables && r.issueType == "Missing Table Collection") ||
                (showMissingEntries && r.issueType == "Missing Table Entry") ||
                (showInvalidGuids && r.issueType == "Invalid GUID")
            ).ToList();
        }

        private void DrawMissingReference(MissingReferenceInfo reference)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header with issue type and object reference
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(reference.issueType, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                Selection.activeGameObject = reference.gameObject;
                EditorGUIUtility.PingObject(reference.gameObject);
            }
            EditorGUILayout.EndHorizontal();

            // Object path
            EditorGUILayout.LabelField("Object:", reference.gameObject.name);
            EditorGUILayout.LabelField("Path:", GetGameObjectPath(reference.gameObject));

            // Issue description
            EditorGUILayout.LabelField("Issue:", reference.description);

            // Reference details
            if (!string.IsNullOrEmpty(reference.tableReference))
            {
                EditorGUILayout.LabelField("Table Reference:", reference.tableReference);
            }
            if (!string.IsNullOrEmpty(reference.entryReference))
            {
                EditorGUILayout.LabelField("Entry Reference:", reference.entryReference);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void RefreshMissingReferences()
        {
            missingReferences.Clear();

            // Find all LocalizeStringEvent components in the current scene
            var localizeStringEvents = FindObjectsOfType<LocalizeStringEvent>();

            foreach (var localizeStringEvent in localizeStringEvents)
            {
                CheckLocalizeStringEvent(localizeStringEvent);
            }
        }

        private void CheckLocalizeStringEvent(LocalizeStringEvent localizeStringEvent)
        {
            if (localizeStringEvent == null || localizeStringEvent.StringReference == null)
                return;

            var stringReference = localizeStringEvent.StringReference;
            var gameObject = localizeStringEvent.gameObject;

            // Check for empty references
            if (stringReference.IsEmpty)
            {
                missingReferences.Add(new MissingReferenceInfo
                {
                    gameObject = gameObject,
                    localizeStringEvent = localizeStringEvent,
                    issueType = "Empty Reference",
                    description = "Both table and entry references are empty",
                    tableReference = GetTableReferenceString(stringReference),
                    entryReference = GetEntryReferenceString(stringReference)
                });
                return;
            }

            // Check table reference
            var tableRef = stringReference.TableReference;
            var entryRef = stringReference.TableEntryReference;

            if (tableRef.ReferenceType == TableReference.Type.Empty)
            {
                missingReferences.Add(new MissingReferenceInfo
                {
                    gameObject = gameObject,
                    localizeStringEvent = localizeStringEvent,
                    issueType = "Empty Reference",
                    description = "Table reference is empty",
                    tableReference = "Empty",
                    entryReference = GetEntryReferenceString(stringReference)
                });
                return;
            }

            if (entryRef.ReferenceType == TableEntryReference.Type.Empty)
            {
                missingReferences.Add(new MissingReferenceInfo
                {
                    gameObject = gameObject,
                    localizeStringEvent = localizeStringEvent,
                    issueType = "Empty Reference",
                    description = "Entry reference is empty",
                    tableReference = GetTableReferenceString(stringReference),
                    entryReference = "Empty"
                });
                return;
            }

            // Check if table collection exists
            var tableCollection = GetTableCollection(tableRef);
            if (tableCollection == null)
            {
                missingReferences.Add(new MissingReferenceInfo
                {
                    gameObject = gameObject,
                    localizeStringEvent = localizeStringEvent,
                    issueType = "Missing Table Collection",
                    description = $"Table collection '{GetTableReferenceString(stringReference)}' does not exist",
                    tableReference = GetTableReferenceString(stringReference),
                    entryReference = GetEntryReferenceString(stringReference)
                });
                return;
            }

            // Check if entry exists in the table collection
            var entry = GetTableEntry(tableCollection, entryRef);
            if (entry == null)
            {
                missingReferences.Add(new MissingReferenceInfo
                {
                    gameObject = gameObject,
                    localizeStringEvent = localizeStringEvent,
                    issueType = "Missing Table Entry",
                    description = $"Entry '{GetEntryReferenceString(stringReference)}' does not exist in table collection '{tableCollection.TableCollectionName}'",
                    tableReference = GetTableReferenceString(stringReference),
                    entryReference = GetEntryReferenceString(stringReference)
                });
            }
        }

        private StringTableCollection GetTableCollection(TableReference tableRef)
        {
            try
            {
                if (tableRef.ReferenceType == TableReference.Type.Name)
                {
                    return LocalizationEditorSettings.GetStringTableCollection(tableRef.TableCollectionName);
                }
                else if (tableRef.ReferenceType == TableReference.Type.Guid)
                {
                    var collections = LocalizationEditorSettings.GetStringTableCollections();
                    return collections.FirstOrDefault(c => c.SharedData.TableCollectionNameGuid == tableRef.TableCollectionNameGuid);
                }
            }
            catch (System.Exception)
            {
                // Invalid reference
            }
            return null;
        }

        private SharedTableData.SharedTableEntry GetTableEntry(StringTableCollection tableCollection, TableEntryReference entryRef)
        {
            if (tableCollection?.SharedData == null)
                return null;

            try
            {
                if (entryRef.ReferenceType == TableEntryReference.Type.Name)
                {
                    return tableCollection.SharedData.GetEntry(entryRef.Key);
                }
                else if (entryRef.ReferenceType == TableEntryReference.Type.Id)
                {
                    return tableCollection.SharedData.GetEntry(entryRef.KeyId);
                }
            }
            catch (System.Exception)
            {
                // Invalid reference
            }
            return null;
        }

        private string GetTableReferenceString(LocalizedString localizedString)
        {
            if (localizedString?.TableReference == null)
                return "Null";

            var tableRef = localizedString.TableReference;
            switch (tableRef.ReferenceType)
            {
                case TableReference.Type.Empty:
                    return "Empty";
                case TableReference.Type.Name:
                    return tableRef.TableCollectionName;
                case TableReference.Type.Guid:
                    return tableRef.TableCollectionNameGuid.ToString();
                default:
                    return "Unknown";
            }
        }

        private string GetEntryReferenceString(LocalizedString localizedString)
        {
            if (localizedString == null)
                return "Null";

            var entryRef = localizedString.TableEntryReference;
            switch (entryRef.ReferenceType)
            {
                case TableEntryReference.Type.Empty:
                    return "Empty";
                case TableEntryReference.Type.Name:
                    return entryRef.Key;
                case TableEntryReference.Type.Id:
                    return entryRef.KeyId.ToString();
                default:
                    return "Unknown";
            }
        }

        private string GetGameObjectPath(GameObject gameObject)
        {
            var path = gameObject.name;
            var parent = gameObject.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private void OnInspectorUpdate()
        {
            // Refresh the window when the scene changes
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Repaint();
        }

        private void OnHierarchyChange()
        {
            // Refresh when hierarchy changes
            RefreshMissingReferences();
        }

        private void OnProjectChange()
        {
            // Refresh when project assets change (table collections might be added/removed)
            RefreshMissingReferences();
        }
    }
}
