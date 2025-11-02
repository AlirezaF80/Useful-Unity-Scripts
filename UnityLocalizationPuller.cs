#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Localization.Plugins.Google;
using UnityEditor.Localization.Plugins.Google.Columns;
using UnityEditor.Localization.Reporting;
using UnityEngine;

public class UnityLocalizationPuller
{
    [MenuItem("Tools/Pull Localization Tables - Google Sheets")]
    public static void PullAllExtensions()
    {
        // Get every String Table Collection
        var stringTableCollections = LocalizationEditorSettings.GetStringTableCollections();

        foreach (var collection in stringTableCollections)
        {
            // Its possible a String Table Collection may have more than one GoogleSheetsExtension.
            // For example if each Locale we pushed/pulled from a different sheet.
            foreach (var extension in collection.Extensions)
            {
                if (extension is GoogleSheetsExtension googleExtension)
                {
                    PullExtension(googleExtension);
                }
            }
        }
    }

    static void PullExtension(GoogleSheetsExtension googleExtension)
    {
        // Setup the connection to Google
        var googleSheets = new GoogleSheets(googleExtension.SheetsServiceProvider);
        googleSheets.SpreadSheetId = googleExtension.SpreadsheetId;

        // Now update the collection. We can pass in an optional ProgressBarReporter so that we can updates in the Editor.
        googleSheets.PullIntoStringTableCollection(googleExtension.SheetId, googleExtension.TargetCollection as StringTableCollection,
            googleExtension.Columns, reporter: new ProgressBarReporter());
    }
}
#endif
