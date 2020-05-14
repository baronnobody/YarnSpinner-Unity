using UnityEngine;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using System.Linq;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

[CustomEditor(typeof(YarnImporter))]
public class YarnImporterEditor : ScriptedImporterEditor {

    int selectedLanguageIndex;

    int selectedNewTranslationLanguageIndex;

    /// <summary>
    /// Index of the currently selected voice over language.
    /// Only show voiceovers for one language.
    /// </summary>
    int selectedVoiceoverLanguageIndex;

    /// <summary>
    /// Foldout bool for voiceover list.
    /// </summary>
    bool showVoiceovers = false;

    SerializedProperty baseLanguageIdProperty;

    private Culture[] _culturesAvailable;

    /// <summary>
    /// Contains all yarn lines in all available languages on this asset. Used for line hinting on the voice overs list.
    /// </summary>
    Dictionary<string, Dictionary<string, string>> _allLanguagesStringTable = new Dictionary<string, Dictionary<string, string>>();

    private const string _audioVoiceOverInitializeHelpBox = "Hit 'Apply' to initialize the currently selected voice over language!";
    private const string _audioVoiceOverNoYarnLinesOnAsset = "No yarn lines found on this asset so no voice overs can be linked to lines.";

    public override void OnEnable() {
        base.OnEnable();
        baseLanguageIdProperty = serializedObject.FindProperty("baseLanguageID");
        _culturesAvailable = Cultures.AvailableCultures;

        // Check for situations where we don't want to apply the project settings (for instance, if no settings have been made at all ...)
        if (ProjectSettings.TextProjectLanguages.Count > 0 && (string.IsNullOrEmpty(baseLanguageIdProperty.stringValue) || ProjectSettings.TextProjectLanguages.Contains(baseLanguageIdProperty.stringValue))) {
            // Reduce the available languages to the list defined on the project settings
            _culturesAvailable = Cultures.LanguageNamesToCultures(ProjectSettings.TextProjectLanguages.ToArray());
        }
        if (string.IsNullOrEmpty(baseLanguageIdProperty.stringValue)) {
            if (ProjectSettings.TextProjectLanguages.Count > 0) {
                // Use first language from project settings as base language
                selectedLanguageIndex = 0;
            } else {
                // Use system's language as base language if no project settings are defined
                selectedLanguageIndex = _culturesAvailable.
                    Select((culture, index) => new { culture, index })
                    .FirstOrDefault(element => element.culture.Name == CultureInfo.CurrentCulture.Name)
                    .index;
            }
        } else {
            // Get index from previously stored base language setting
            selectedLanguageIndex = _culturesAvailable.Select((culture, index) => new { culture, index })
                .FirstOrDefault(pair => pair.culture.Name == baseLanguageIdProperty.stringValue)
                .index;
        }

        // Assets imported with older code should be reimported so we have a reference to the YarnProgram
        if (serializedObject.FindProperty("programContainer").objectReferenceValue == null) {
            (target as YarnImporter).SaveAndReimport();
            serializedObject.Update();
        }
        // Get all yarn lines of all languages so we can line hint them on the voice overs list
        var _yarnProgram = serializedObject.FindProperty("programContainer").objectReferenceValue as YarnProgram;
        if (_yarnProgram) {
            _allLanguagesStringTable.Add(baseLanguageIdProperty.stringValue, _yarnProgram.GetStringTable(baseLanguageIdProperty.stringValue));
            foreach (var language in _yarnProgram.localizations) {
                _allLanguagesStringTable.Add(language.languageName, _yarnProgram.GetStringTable(language.languageName));
            }
        }
    }

    public override void OnDisable() {
        base.OnDisable();
    }


    public override void OnInspectorGUI() {
        serializedObject.Update();
        EditorGUILayout.Space();
        YarnImporter yarnImporter = (target as YarnImporter);

        // All text languages on this asset (translations and  base language)
        var textLanguageNamesOnAsset = yarnImporter.localizations.
            Select(element => element.languageName).
            Append(_culturesAvailable[selectedLanguageIndex].Name).
            OrderBy(element => element).
            ToArray();
        var audioLanguageNamesOnAsset = ProjectSettings.AudioProjectLanguages.Count > 0 ?
            ProjectSettings.AudioProjectLanguages.ToArray() :
            textLanguageNamesOnAsset;

        selectedLanguageIndex = EditorGUILayout.Popup("Base Language", selectedLanguageIndex, Cultures.CulturesToDisplayNames(_culturesAvailable));
        baseLanguageIdProperty.stringValue = _culturesAvailable[selectedLanguageIndex].Name;

        if (yarnImporter.isSuccesfullyCompiled == false) {
            EditorGUILayout.HelpBox(yarnImporter.compilationErrorMessage, MessageType.Error);
            return;
        }

        EditorGUILayout.Space();

        var canCreateLocalisation = yarnImporter.StringsAvailable == true && yarnImporter.AnyImplicitStringIDs == false;

        using (new EditorGUI.DisabledScope(!canCreateLocalisation))
        using (new EditorGUILayout.HorizontalScope()) {

            var culturesAvailableNotOnAsset = _culturesAvailable.Except(Cultures.LanguageNamesToCultures(textLanguageNamesOnAsset)).ToArray();
            audioLanguageNamesOnAsset = audioLanguageNamesOnAsset.Except(Cultures.CulturesToNames(culturesAvailableNotOnAsset)).ToArray();

            if (culturesAvailableNotOnAsset.Length > 0) {
                selectedNewTranslationLanguageIndex = EditorGUILayout.Popup(selectedNewTranslationLanguageIndex, Cultures.CulturesToDisplayNames(culturesAvailableNotOnAsset));

                if (GUILayout.Button("Create New Localisation", EditorStyles.miniButton)) {
                    var stringsTableText = AssetDatabase
                        .LoadAllAssetsAtPath(yarnImporter.assetPath)
                        .OfType<TextAsset>()
                        .FirstOrDefault()?
                        .text ?? "";

                    var selectedCulture = culturesAvailableNotOnAsset[selectedNewTranslationLanguageIndex];

                    var assetDirectory = Path.GetDirectoryName(yarnImporter.assetPath);

                    var newStringsTablePath = $"{assetDirectory}/{Path.GetFileNameWithoutExtension(yarnImporter.assetPath)} ({selectedCulture.Name}).csv";
                    newStringsTablePath = AssetDatabase.GenerateUniqueAssetPath(newStringsTablePath);

                    var writer = File.CreateText(newStringsTablePath);
                    writer.Write(stringsTableText);
                    writer.Close();

                    AssetDatabase.ImportAsset(newStringsTablePath);

                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(newStringsTablePath);

                    EditorGUIUtility.PingObject(asset);

                    // Automatically add newly created translation csv file to yarn program
                    var localizationsIndex = System.Array.FindIndex(yarnImporter.localizations, element => element.languageName == selectedCulture.Name);
                    var localizationSerializedProperty = serializedObject.FindProperty("localizations");
                    if (localizationsIndex != -1) {
                        localizationSerializedProperty.GetArrayElementAtIndex(localizationsIndex).FindPropertyRelative("text").objectReferenceValue = asset;
                    } else {
                        localizationSerializedProperty.InsertArrayElementAtIndex(localizationSerializedProperty.arraySize);
                        localizationSerializedProperty.GetArrayElementAtIndex(localizationSerializedProperty.arraySize - 1).FindPropertyRelative("text").objectReferenceValue = asset;
                        localizationSerializedProperty.GetArrayElementAtIndex(localizationSerializedProperty.arraySize - 1).FindPropertyRelative("languageName").stringValue = selectedCulture.Name;
                    }
                }
            } else {
                EditorGUILayout.HelpBox("Go to Project Settings if you want to add more translations.", MessageType.Info);
            }

        }

        if (yarnImporter.StringsAvailable == false) {
            EditorGUILayout.HelpBox("This file doesn't contain any localisable lines or options.", MessageType.Info);
        }

        if (yarnImporter.AnyImplicitStringIDs) {
            EditorGUILayout.HelpBox("Add #line: tags to all lines and options to enable creating new localisations. Either add them manually, or click Add Line Tags to automatically add tags. Note that this will modify your files on disk, and cannot be undone.", MessageType.Info);
            if (GUILayout.Button("Add Line Tags")) {
                AddLineTagsToFile(yarnImporter.assetPath);
            }
        }
        // Only update localizations if line tags exist and localizations exist
        else if (yarnImporter .localizations.Length > 0)
        {
            if (GUILayout.Button("Update Localizations"))
            {
                UpdateLocalizations(AssetDatabase
                        .LoadAllAssetsAtPath(yarnImporter.assetPath)
                        .OfType<TextAsset>()
                        .FirstOrDefault()?
                        .text ?? "");
            }
        }

        // Localization list
        EditorGUILayout.PropertyField(serializedObject.FindProperty("localizations"), true);

        EditorGUILayout.Space();

        // Automatically find voice over assets based on the linetag and the language id
        // Note: this could be expanded to multiple search patterns via actions or 
        // delegates returning their results, comparing these results and selecting the 
        // result that returns exactly one matching asset.
        // Possible alternative search patterns: 
        // * search for $linetag but return asset with parent directory matching $language
        // * search for "$linetag-$language"
        // * search for "$language-$linetag"
        if (GUILayout.Button("Import Voice Over Audio Files")) {
            // For every linetag of this yarn asset
            for (int i = 0; i < yarnImporter.voiceOvers.Length; i++) {
                LinetagToLanguage linetagToLanguage = yarnImporter.voiceOvers[i];
                var linetag = linetagToLanguage.linetag.Remove(0, 5);
                // For every language of this yarn asset
                for (int j = 0; j < linetagToLanguage.languageToAudioclip.Length; j++) {
                    LanguageToAudioclip languageToAudioclip = linetagToLanguage.languageToAudioclip[j];

                    // Do not overwrite existing content
                    if (languageToAudioclip.audioClip != null) {
                        continue;
                    }

                    var language = languageToAudioclip.language;
                    var results = AssetDatabase.FindAssets("t:AudioClip " + linetag + " " + language);

                    // Write found AudioClip into voice overs array
                    if (results.Length != 0) {
                        var voiceOversProp = serializedObject.FindProperty("voiceOvers");
                        var linetagProp = voiceOversProp.GetArrayElementAtIndex(i).FindPropertyRelative("linetag");
                        var languagetoAudioClipProp = voiceOversProp.GetArrayElementAtIndex(i).FindPropertyRelative("languageToAudioclip");
                        var audioclipProp = languagetoAudioClipProp.GetArrayElementAtIndex(j).FindPropertyRelative("audioClip");
                        audioclipProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(results[0]));
                    }

                    // Return info if the search results were ambiguous or there was not result
                    if (results.Length > 1) {
                        Debug.LogWarning("More than one asset found matching the linetag " + linetag + "  and the language " + language + ".");
                    } else if (results.Length == 0) {
                        Debug.LogWarning("No asset found matching the linetag '" + linetag + "' and the language '" + language + "'.");
                    }
                }
            }

        }
        // Voiceover list. Reduced to one language.
        showVoiceovers = EditorGUILayout.Foldout(showVoiceovers, "Voice Overs"); // FIXME: Clicking on the foldout triangle doesn't open/close the foldout
        if (showVoiceovers) {
            EditorGUI.indentLevel++;
            // Language selected here will reduce the visual representation of the voiceover data structure
            selectedVoiceoverLanguageIndex = EditorGUILayout.Popup(selectedVoiceoverLanguageIndex, Cultures.LanguageNamesToDisplayNames(audioLanguageNamesOnAsset), GUILayout.MaxWidth(96));
            // Bound-check (f.g. currently selected voice over language has been removed from the available translations on this asset)
            selectedVoiceoverLanguageIndex = Mathf.Min(audioLanguageNamesOnAsset.Length - 1, selectedVoiceoverLanguageIndex);
            var selectedVoiceOverLanguageExists = false;
            // Only draw AudioClips from selected language
            for (int i = 0; i < yarnImporter.voiceOvers.Length; i++) {
                LinetagToLanguage linetagToLanguage = yarnImporter.voiceOvers[i];
                for (int j = 0; j < linetagToLanguage.languageToAudioclip.Length; j++) {
                    LanguageToAudioclip languageToAudioclip = linetagToLanguage.languageToAudioclip[j];
                    if (languageToAudioclip.language == audioLanguageNamesOnAsset[selectedVoiceoverLanguageIndex]) {
                        selectedVoiceOverLanguageExists = true;
                        var voiceOversProp = serializedObject.FindProperty("voiceOvers");
                        var linetagProp = voiceOversProp.GetArrayElementAtIndex(i).FindPropertyRelative("linetag");
                        var languagetoAudioClipProp = voiceOversProp.GetArrayElementAtIndex(i).FindPropertyRelative("languageToAudioclip");
                        var languageProp = languagetoAudioClipProp.GetArrayElementAtIndex(j).FindPropertyRelative("language");
                        var audioclipProp = languagetoAudioClipProp.GetArrayElementAtIndex(j).FindPropertyRelative("audioClip");
                        var label = linetagProp.stringValue;
                        if (_allLanguagesStringTable.ContainsKey(languageProp.stringValue) 
                            && _allLanguagesStringTable[languageProp.stringValue].ContainsKey(linetagProp.stringValue)) {
                            label = linetagProp.stringValue + " ('" + _allLanguagesStringTable[languageProp.stringValue][linetagProp.stringValue] + "')";
                        }
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(label);
                        EditorGUILayout.PropertyField(audioclipProp, new GUIContent(""));
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            if (!selectedVoiceOverLanguageExists) {
                if (YarnProgram.GetStringTable(yarnImporter.baseLanguage).Count > 0) {
                    EditorGUILayout.HelpBox(_audioVoiceOverInitializeHelpBox, MessageType.Info);
                } else {
                    EditorGUILayout.HelpBox(_audioVoiceOverNoYarnLinesOnAsset, MessageType.Info);
                }
            }
            EditorGUI.indentLevel--;
        }

        var success = serializedObject.ApplyModifiedProperties();
#if UNITY_2018
        if (success) {
            EditorUtility.SetDirty(target);
            AssetDatabase.WriteImportSettingsIfDirty(AssetDatabase.GetAssetPath(target));
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(target));
        }
#endif
#if UNITY_2019_1_OR_NEWER
        ApplyRevertGUI();
#endif
    }

    private void AddLineTagsToFile(string assetPath) {
        // First, gather all existing line tags, so that we don't
        // accidentally overwrite an existing one. Do this by finding _all_
        // YarnPrograms, and by extension their importers, and get the
        // string tags that they found.

        var allLineTags = Resources.FindObjectsOfTypeAll<YarnProgram>() // get all yarn programs that have been imported
            .Select(asset => AssetDatabase.GetAssetOrScenePath(asset)) // get the path on disk
            .Select(path => AssetImporter.GetAtPath(path)) // get the asset importer for that path
            .OfType<YarnImporter>() // ensure that they're all YarnImporters
            .SelectMany(importer => importer.stringIDs)
            .ToList(); // get all string IDs, flattened into one list            

        var contents = File.ReadAllText(assetPath);
        var taggedVersion = Yarn.Compiler.Utility.AddTagsToLines(contents, allLineTags);

        File.WriteAllText(assetPath, taggedVersion);

        AssetDatabase.ImportAsset(assetPath);
    }

    void UpdateLocalizations(string newBaseCsv)
    {
        // Goes through each localization string table csv file and merges it with the new base string table
        YarnImporter yarnImporter = (target as YarnImporter);

        for (int i = 0; i < yarnImporter.localizations.Length; i++)
        {
            // Feeds the merge function with the old localized string table, the new base string table and the current file name in case it has changed.
            string merged = MergeStringTables(yarnImporter.localizations[i].text.text, newBaseCsv, Path.GetFileNameWithoutExtension(yarnImporter.assetPath));

            // Save the merged csv to file (copied from the "Create New Localisation" button).
            var assetDirectory = Path.GetDirectoryName(yarnImporter.assetPath);

            string language = yarnImporter.localizations[i].languageName;
            var newStringsTablePath = $"{assetDirectory}/{Path.GetFileNameWithoutExtension(yarnImporter.assetPath)} ({language}).csv";
            newStringsTablePath = AssetDatabase.GenerateUniqueAssetPath(newStringsTablePath);

            var writer = File.CreateText(newStringsTablePath);
            writer.Write(merged);
            writer.Close();

            AssetDatabase.ImportAsset(newStringsTablePath);

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(newStringsTablePath);

            EditorGUIUtility.PingObject(asset);
        }
    }

    string MergeStringTables(string oldLocalizedCsv, string newBaseCsv, string outputFileName)
    {
        // Use the CsvHelper to convert the two csvs to lists so they are easy to work with
        List<CsvEntry> oldEntries = new List<CsvEntry>();
        List<CsvEntry> newEntries = new List<CsvEntry>();

        using (var oldReader = new StringReader(oldLocalizedCsv))
        using (var newReader = new StringReader(newBaseCsv))
        {
            CsvHelper.CsvReader oldParser = new CsvHelper.CsvReader(oldReader, new CsvHelper.Configuration.Configuration(CultureInfo.InvariantCulture));
            CsvHelper.CsvReader newParser = new CsvHelper.CsvReader(newReader, new CsvHelper.Configuration.Configuration(CultureInfo.InvariantCulture));

            oldParser.Read();
            newParser.Read();

            oldParser.ReadHeader();
            newParser.ReadHeader();

            while (oldParser.Read())
            {
                oldEntries.Add(
                    new CsvEntry
                    {
                        id = oldParser.GetField("id"),
                        text = oldParser.GetField("text"),
                        file = oldParser.GetField("file"),
                        node = oldParser.GetField("node"),
                        lineNumber = oldParser.GetField<int>("lineNumber"),
                    }
                );
            }

            while (newParser.Read())
            {
                newEntries.Add(
                    new CsvEntry
                    {
                        id = newParser.GetField("id"),
                        text = newParser.GetField("text"),
                        file = newParser.GetField("file"),
                        node = newParser.GetField("node"),
                        lineNumber = newParser.GetField<int>("lineNumber"),
                    }
                );
            }

        }


        // This is where we merge the two string tables. Here's what's happening:
        // Use CsvParser to parse new base string table and old localized string table
        // The strategy is to use the fact that the two string tables look alike to optimize.
        // The algorithm goes through the string tables side by side. Imagine two fingers running
        // through the entries. At each line there are four different scenarios that we test for:
        //   scenario 1: The lines match (matching tags)
        //   scenario 2: The line in the new string table exists in the old string table but has been moved from somewhere else
        //   scenario 3: The line in the new string table is completely new (no line tags in old string table match it)
        //   scenario 4: The line in the old string table has been deleted (no line tags in new string table match it)

        //Go line by line:
        //1.If line tags are the same: add old localized with new line number and node. Increase index of both. (s1: matching lines)
        //2.Else if line tags are different:
        //  a. Search forward in the old string table for that line tag
        //    i. if we find it: add old localized with new line number and node. Remove from old. Increase new index. (s2: old line moved)
        //    ii. If we don't find it: Search forward in new string table for that line tag
        //      I. if we find it: add the one new line we are on. Increase new index. (s3: line is new)
        //      II. if we don't find it: ignore line. Increase old index. (s4: line has been deleted)
        int oldIndex = 0;
        int newIndex = 0;

        List<CsvEntry> mergedEntries = new List<CsvEntry>();

        // Mark new lines as new so they are easy to spot
        string newlineMarker = " (((NEW LINE)))";

        while (true)
        {
            // If no more entries in old: add the rest of the new entries and break
            if (oldEntries.Count <= oldIndex)
            {
                for (int i = newIndex; i < newEntries.Count; i++)
                {
                    CsvEntry entry = newEntries[i];
                    entry.text += newlineMarker;
                    mergedEntries.Add(entry);
                }
                break;
            }

            // If no more entries in new: all additional old entries must have been deleted so break
            if (newEntries.Count <= newIndex)
            {
                break;
            }

            //1. If line tags are the same: add old localized with new line number. Increase index of both.
            if (oldEntries[oldIndex].id == newEntries[newIndex].id)
            {
                CsvEntry entry = oldEntries[oldIndex];
                entry.lineNumber = newEntries[newIndex].lineNumber;
                entry.node = newEntries[newIndex].node;
                mergedEntries.Add(entry);
                oldIndex++;
                newIndex++;
                continue;
            }
            //2. Else if line tags are different:
            else
            {
                // a. Search forward in the old string table for that line tag
                bool didFindInOld = false;
                for (int i = oldIndex + 1; i < oldEntries.Count; i++)
                {
                    // i. if we find it: add old localized with new line number. Remove from old. Increase index of new. (old line moved)
                    if (oldEntries[i].id == newEntries[newIndex].id)
                    {
                        CsvEntry entry = oldEntries[i];
                        entry.lineNumber = newEntries[newIndex].lineNumber;
                        entry.node = newEntries[newIndex].node;
                        mergedEntries.Add(entry);
                        oldEntries.RemoveAt(i);
                        didFindInOld = true;
                        newIndex++;
                        break;
                    }
                }
                if (didFindInOld)
                {
                    continue;
                }

                // ii.If we don't find it: Search forward in new string table for that line tag
                bool didFindInNew = false;
                for (int i = newIndex + 1; i < newEntries.Count; i++)
                {
                    // I. if we find it: add the one new line we are on. Increase index of new. (line is new)
                    if (oldEntries[oldIndex].id == newEntries[i].id)
                    {
                        CsvEntry entry = newEntries[newIndex];
                        entry.text += newlineMarker;
                        mergedEntries.Add(entry);
                        newIndex++;
                        didFindInNew = true;
                        break;
                    }
                }
                // II. if we don't find it: ignore line. Increase index of old. (line has been deleted)
                if (!didFindInNew)
                {
                    oldIndex++;
                }
            }
        }

        // Entries are not necessarily added in the correct order and have to be sorted
        mergedEntries.Sort((a, b) => a.lineNumber.CompareTo(b.lineNumber));

        // Create new Csv file
        using (var memoryStream = new MemoryStream())
        using (var textWriter = new StreamWriter(memoryStream))
        {
            // Generate the localised .csv file
            var csv = new CsvHelper.CsvWriter(textWriter, new CsvHelper.Configuration.Configuration(CultureInfo.InvariantCulture));

            var lines = mergedEntries.Select(x => new
            {
                id = x.id,
                text = x.text,
                file = outputFileName,
                node = x.node,
                lineNumber = x.lineNumber
            });

            csv.WriteRecords(lines);

            textWriter.Flush();

            memoryStream.Position = 0;

            using (var reader = new StreamReader(memoryStream))
            {
                return reader.ReadToEnd();
            }
        }
    }
    
    struct CsvEntry
    {
        public string id;
        public string text;
        public string file;
        public string node;
        public int lineNumber;
    }
}