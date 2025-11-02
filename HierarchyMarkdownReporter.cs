using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization.Components;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// An editor utility to generate a detailed Markdown report of a GameObject's hierarchy,
/// its components, and their properties. This version includes deep serialization for
/// UnityEvents, provides asset paths, and correctly reports on non-public fields
/// that have the [SerializeField] attribute.
/// The report is copied to the clipboard.
/// </summary>
public static class HierarchyMarkdownReporter
{
    #region Property Filtering Configuration

    // A global blacklist of common, noisy, or unhelpful properties to exclude from all components.
    private static readonly HashSet<string> GlobalPropertyBlacklist = new HashSet<string>
    {
        // Common Unity/Editor properties
        "hideFlags", "useGUILayout", "runInEditMode", "destroyCancellationToken", "enabled",
        "isActiveAndEnabled", "gameObject", "tag", "name", "transform",
        // Matrix and transform properties that are often redundant or verbose
        "worldToLocalMatrix", "localToWorldMatrix", "lossyScale", "rotation", "localRotation",
        "eulerAngles", "localEulerAngles", "right", "up", "forward",
        // Hierarchy-related properties
        "parent", "root", "childCount", "hierarchyCapacity", "hierarchyCount",
        // Rendering/UI properties that are often internal or overly detailed
        "hasChanged", "material", "materials", "sharedMaterial", "sharedMaterials",
        "bounds", "mesh", "canvas", "canvasRenderer", "mainTexture"
    };

    // A whitelist of specific properties for common components. If a component type is in this dictionary,
    // ONLY the properties listed here will be shown. This provides a focused view for known types.
    private static readonly Dictionary<Type, HashSet<string>> ComponentPropertyWhitelist = new Dictionary<Type, HashSet<string>>
    {
        { typeof(RectTransform), new HashSet<string> { "rect", "anchorMin", "anchorMax", "anchoredPosition", "sizeDelta", "pivot", "offsetMin", "offsetMax", "localPosition", "localScale" } },
        { typeof(CanvasRenderer), new HashSet<string>() }, // Show nothing for this component
        // TextMeshPro (covers all variants through its base class)
        { typeof(TMP_Text), new HashSet<string> { "text", "font", "fontSize", "fontStyle", "color", "alignment", "enableWordWrapping", "overflowMode", "richText", "raycastTarget" } },
        // UI Elements
        { typeof(Image), new HashSet<string> { "sprite", "color", "material", "type", "fillAmount", "fillMethod", "preserveAspect", "raycastTarget", "maskable" } },
        { typeof(Button), new HashSet<string> { "interactable", "transition", "targetGraphic", "onClick" } },
        { typeof(ScrollRect), new HashSet<string> { "content", "horizontal", "vertical", "movementType", "elasticity", "scrollSensitivity", "viewport" } },
        // Layout Groups (base class covers Horizontal and Vertical)
        { typeof(HorizontalOrVerticalLayoutGroup), new HashSet<string> { "padding", "spacing", "childAlignment", "childControlWidth", "childControlHeight", "childForceExpandWidth", "childForceExpandHeight" } },
        { typeof(GridLayoutGroup), new HashSet<string> { "padding", "cellSize", "spacing", "startCorner", "startAxis", "childAlignment", "constraint", "constraintCount" } },
        { typeof(ContentSizeFitter), new HashSet<string> { "horizontalFit", "verticalFit" } },
        { typeof(LayoutElement), new HashSet<string> { "ignoreLayout", "minWidth", "minHeight", "preferredWidth", "preferredHeight", "flexibleWidth", "flexibleHeight" } },
        // Localization (Example for a common package)
        { typeof(LocalizeStringEvent), new HashSet<string> { "StringReference", "OnUpdateString" } }
    };

    #endregion

    [MenuItem("GameObject/Report Hierarchy to Markdown", false, 0)]
    private static void ReportMarkdown(MenuCommand menuCommand)
    {
        if (!(menuCommand.context is GameObject selectedObject) || selectedObject == null)
        {
            EditorUtility.DisplayDialog("Selection Error", "You must select a GameObject in the hierarchy to generate a report.", "OK");
            return;
        }

        StringBuilder markdown = new StringBuilder();
        markdown.AppendLine($"# Component Report: {selectedObject.name}");
        markdown.AppendLine($"*Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
        markdown.AppendLine();

        markdown.AppendLine("## Hierarchy");
        markdown.AppendLine();
        BuildHierarchyTree(selectedObject.transform, markdown, 0);
        markdown.AppendLine("\n---\n");

        Transform[] allTransforms = selectedObject.GetComponentsInChildren<Transform>(true);
        foreach (Transform childTransform in allTransforms)
        {
            ReportGameObject(childTransform.gameObject, markdown, selectedObject.transform);
        }

        GUIUtility.systemCopyBuffer = markdown.ToString();
        Debug.Log($"Component report for '{selectedObject.name}' and its {allTransforms.Length - 1} children copied to clipboard!");
    }

    private static void BuildHierarchyTree(Transform obj, StringBuilder markdown, int depth)
    {
        string indent = new string(' ', depth * 2);
        string bullet = depth == 0 ? "" : "- ";
        string activeIcon = obj.gameObject.activeSelf ? "" : " *(inactive)*";
        int instanceId = obj.gameObject.GetInstanceID();

        markdown.AppendLine($"{indent}{bullet}[**{obj.name}**](#{instanceId}){activeIcon}");

        for (int i = 0; i < obj.childCount; i++)
        {
            BuildHierarchyTree(obj.GetChild(i), markdown, depth + 1);
        }
    }

    private static void ReportGameObject(GameObject obj, StringBuilder markdown, Transform root)
    {
        int instanceId = obj.GetInstanceID();

        markdown.AppendLine($"## <a id=\"{instanceId}\"></a>{obj.name} `[{instanceId}]`");
        markdown.AppendLine();

        markdown.AppendLine($"**Path:** `{GetBreadcrumbPath(obj.transform, root)}`");
        markdown.AppendLine($"**Active:** {obj.activeSelf} | **Layer:** {LayerMask.LayerToName(obj.layer)} | **Tag:** {obj.tag}");
        markdown.AppendLine();

        foreach (Component component in obj.GetComponents<Component>())
        {
            if (component == null)
            {
                markdown.AppendLine($"### <font color='red'>Missing Script</font>");
                markdown.AppendLine();
                continue;
            }

            string componentName = component.GetType().Name;
            markdown.AppendLine($"### {componentName}");

            var propertiesToReport = GetPropertiesToReport(component);
            if (!propertiesToReport.Any())
            {
                markdown.AppendLine("*No relevant properties to display*");
            }
            else
            {
                foreach (var prop in propertiesToReport)
                {
                    markdown.AppendLine($"- **{prop.Key}**: {prop.Value}");
                }
            }
            markdown.AppendLine();
        }
    }

    /// <summary>
    /// Gathers relevant properties and fields from a component, including public fields,
    /// public properties, and non-public fields marked with [SerializeField].
    /// </summary>
    private static Dictionary<string, string> GetPropertiesToReport(Component component)
    {
        var reportedProperties = new Dictionary<string, string>();
        Type componentType = component.GetType();

        HashSet<string> whitelist = null;
        for (var type = componentType; type != null && type != typeof(object); type = type.BaseType)
        {
            if (ComponentPropertyWhitelist.TryGetValue(type, out whitelist)) break;
        }

        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Process all fields (public or [SerializeField])
        foreach (FieldInfo field in componentType.GetFields(flags))
        {
            // Include the field if it's public OR if it has the SerializeField attribute.
            bool isSerializable = field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;

            if (isSerializable && ShouldIncludeProperty(field.Name, whitelist))
            {
                try
                {
                    reportedProperties[field.Name] = FormatValue(field.GetValue(component));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Could not read field '{field.Name}' on '{componentType.Name}': {ex.Message}");
                }
            }
        }

        // Process all public properties
        foreach (PropertyInfo prop in componentType.GetProperties(flags))
        {
            // We only check public properties, as private properties are rarely serialized or relevant for reporting.
            if (prop.GetMethod != null && prop.GetMethod.IsPublic && prop.GetIndexParameters().Length == 0 && ShouldIncludeProperty(prop.Name, whitelist))
            {
                try
                {
                    reportedProperties[prop.Name] = FormatValue(prop.GetValue(component, null));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Could not read property '{prop.Name}' on '{componentType.Name}': {ex.Message}");
                }
            }
        }
        return reportedProperties;
    }

    private static bool ShouldIncludeProperty(string propertyName, HashSet<string> whitelist)
    {
        // If a whitelist exists for this component type, the property MUST be in it.
        if (whitelist != null)
        {
            return whitelist.Contains(propertyName);
        }
        // Otherwise, include the property as long as it's NOT in the global blacklist.
        return !GlobalPropertyBlacklist.Contains(propertyName);
    }

    private static string GetBreadcrumbPath(Transform obj, Transform root)
    {
        if (obj == root) return root.name;

        List<string> path = new List<string>();
        for (var current = obj; current != null && current != root.parent; current = current.parent)
        {
            path.Add(current.name);
        }
        path.Reverse();
        return string.Join(" > ", path);
    }

    /// <summary>
    /// Formats a C# object value into a readable Markdown string, with deep serialization for key Unity types.
    /// </summary>
    private static string FormatValue(object value)
    {
        if (value == null) return "`null`";
        if (value is Object unityObj && !unityObj) return "`null (destroyed)`";

        // Deep serialization for UnityEvents
        if (value is UnityEventBase unityEvent)
        {
            return FormatUnityEvent(unityEvent);
        }

        // Detailed info for UnityEngine.Object (assets vs. scene objects)
        if (value is Object obj)
        {
            string name = string.IsNullOrEmpty(obj.name) ? "Unnamed" : obj.name;
            string typeName = obj.GetType().Name;

            // Check if it's a project asset (Sprite, Material, Prefab, etc.)
            if (AssetDatabase.IsMainAsset(obj) || AssetDatabase.IsSubAsset(obj))
            {
                string path = AssetDatabase.GetAssetPath(obj);
                return $"`{name}` ({typeName}) [Path: `{path}`]";
            }
            // Otherwise, it's a scene object (GameObject, Component)
            else
            {
                return $"`{name}` ({typeName}) `[ID: {obj.GetInstanceID()}]`";
            }
        }

        // Preview for collections
        if (value is IEnumerable enumerable && !(value is string))
        {
            var items = enumerable.Cast<object>().Take(5).Select(item => item?.ToString() ?? "null");
            int count = enumerable.Cast<object>().Count();
            string preview = string.Join(", ", items);
            string ellipsis = count > 5 ? ", ..." : "";
            return $"`[{preview}{ellipsis}]` ({count} items)";
        }

        // Default case for simple types
        return $"`{value}`";
    }

    /// <summary>
    /// Creates a detailed string representation of a UnityEvent's persistent listeners.
    /// </summary>
    private static string FormatUnityEvent(UnityEventBase unityEvent)
    {
        int count = unityEvent.GetPersistentEventCount();
        if (count == 0) return "`No persistent listeners`";

        var listeners = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var target = unityEvent.GetPersistentTarget(i);
            string methodName = unityEvent.GetPersistentMethodName(i);

            if (target != null && !string.IsNullOrEmpty(methodName))
            {
                listeners.Add($"{target.name}.{methodName}");
            }
            else
            {
                listeners.Add("Invalid/Missing Listener");
            }
        }
        return $"`{count} listeners: [{string.Join(", ", listeners)}]`";
    }
}
