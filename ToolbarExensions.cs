#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

[InitializeOnLoad]
public static class ToolbarExtensions
{
    private static ScriptableObject m_currentToolbar;
    private static string[] m_scenePaths;
    private static string[] m_sceneNames;
    private static int m_selectedSceneIndex = -1;

    static ToolbarExtensions()
    {
        EditorApplication.update -= OnUpdate;
        EditorApplication.update += OnUpdate;
        EditorBuildSettings.sceneListChanged += RefreshSceneList;
        RefreshSceneList();
    }

    private static void OnUpdate()
    {
        if (m_currentToolbar == null)
        {
            var toolbars = Resources.FindObjectsOfTypeAll<ScriptableObject>().Where(o => o.GetType().Name == "Toolbar").ToArray();
            m_currentToolbar = toolbars.Length > 0 ? toolbars[0] : null;
            if (m_currentToolbar != null)
            {
                var root = m_currentToolbar.GetType().GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(m_currentToolbar) as VisualElement;
                var toolbarZone = root.Q("ToolbarZoneRightAlign");
                if (toolbarZone != null)
                {
                    var container = new IMGUIContainer();
                    container.onGUIHandler = OnToolbarGUI;
                    toolbarZone.Add(container);
                }
            }
        }
    }

    private static void OnToolbarGUI()
    {
        GUILayout.BeginHorizontal();

        if (GUILayout.Button(new GUIContent("Start at First", "Starts the game at the first scene (index 0) in build settings."), GUILayout.Width(80)))
        {
            StartAtFirstScene();
        }

        if (m_scenePaths != null && m_scenePaths.Length > 0)
        {
            int newSelectedSceneIndex = EditorGUILayout.Popup(m_selectedSceneIndex, m_sceneNames, GUILayout.Width(100));
            if (newSelectedSceneIndex != m_selectedSceneIndex)
            {
                m_selectedSceneIndex = newSelectedSceneIndex;
                SwitchToSelectedScene();
            }
        }

        GUILayout.EndHorizontal();
    }

    private static void RefreshSceneList()
    {
        var scenes = EditorBuildSettings.scenes;
        var sceneList = new List<(string name, string path)>();

        foreach (var scene in scenes)
        {
            if (scene.enabled)
            {
                var sceneName = Path.GetFileNameWithoutExtension(scene.path);
                sceneList.Add((sceneName, scene.path));
            }
        }

        m_sceneNames = sceneList.Select(s => s.name).ToArray();
        m_scenePaths = sceneList.Select(s => s.path).ToArray();

        var activeScenePath = EditorSceneManager.GetActiveScene().path;
        m_selectedSceneIndex = Array.IndexOf(m_scenePaths, activeScenePath);
    }

    private static void StartAtFirstScene()
    {
        if (m_scenePaths == null || m_scenePaths.Length == 0)
        {
            Debug.LogWarning("No enabled scenes found in build settings.");
            return;
        }

        string firstScenePath = m_scenePaths[0];

        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
        }

        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene(firstScenePath);
            EditorApplication.isPlaying = true;
        }
    }

    private static void SwitchToSelectedScene()
    {
        if (m_selectedSceneIndex >= 0 && m_selectedSceneIndex < m_scenePaths.Length)
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(m_scenePaths[m_selectedSceneIndex]);
            }
        }
    }
}
#endif
