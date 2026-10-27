using System;
using Daidatsugoku;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FieldStageAssetBuilder
{
    [MenuItem("Tools/Daidatsugoku/Configure Field Stages")]
    public static void ConfigureProject()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before configuring stages.");
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/FieldStages")) AssetDatabase.CreateFolder("Assets/Resources", "FieldStages");
        // Stage01's Y=2 EndX=5 and Stage03's 31-cell total were explicitly approved by the user.
        SaveConfirmed("01", "オリジナル", 7, 5, 3, new[]
        { new FieldRow(0,0,6), new(1,0,5), new(2,1,5), new(3,1,4), new(4,2,4) },
        new[] { new FieldCell(1,3), new(1,2), new(2,0), new(3,2), new(4,1) });
        SaveConfirmed("02", "ロング", 8, 5, 3, new[]
        { new FieldRow(0,0,5), new(1,0,6), new(2,0,7), new(3,0,6), new(4,0,5) },
        new[] { new FieldCell(1,4), new(4,3), new(5,2), new(4,1), new(2,0) });
        SaveConfirmed("03", "非対称", 7, 6, 2, new[]
        { new FieldRow(0,0,4), new(1,0,4), new(2,1,6), new(3,1,6), new(4,0,4), new(5,0,3) },
        new[] { new FieldCell(0,5), new(2,4), new(4,3), new(5,2), new(2,1) });
        SaveConfirmed("04", "断崖", 9, 7, 3, new[]
        { new FieldRow(0,0,8), new(1,0,8), new(2,0,7), new(3,1,6), new(4,1,5), new(5,2,5), new(6,2,4) },
        new[] { new FieldCell(2,5), new(4,4), new(3,3), new(5,2), new(2,1), new(6,1), new(4,0) });
        // Preserve the earlier draft for authoring, but v1.1 excludes it from the catalog.
        var draft = AssetDatabase.LoadAssetAtPath<FieldStageDefinition>("Assets/Resources/FieldStages/Stage05.asset");
        if (draft != null)
        {
            draft.Configure(draft.ToStage(), draft.SourceIssue, false);
            EditorUtility.SetDirty(draft);
        }
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.OpenScene(DaidatsugokuSceneBuilder.ScenePath);
        GameManager game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        if (game.GetComponent<GuardVisionSystem>() == null) game.gameObject.AddComponent<GuardVisionSystem>();
        if (game.GetComponent<GuardVisionDebugDisplay>() == null) game.gameObject.AddComponent<GuardVisionDebugDisplay>();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("SYS-FIELD-001 v1.1: stages 01-04 configured with confirmed fences; stage 05 excluded.");
        Debug.Log($"PASS: {FieldStageChecks.Run()} field model assertions.");
    }

    private static void SaveConfirmed(string id, string title, int width, int height, int column, FieldRow[] rows, FieldCell[] fences)
    {
        string path = $"Assets/Resources/FieldStages/Stage{id}.asset";
        var definition = AssetDatabase.LoadAssetAtPath<FieldStageDefinition>(path);
        bool creating = definition == null;
        if (creating) definition = ScriptableObject.CreateInstance<FieldStageDefinition>();
        definition.Configure(new FieldStage(id, title, width, height, rows, column, GuardSide.Top, true, fences));
        if (creating) AssetDatabase.CreateAsset(definition, path);
        else EditorUtility.SetDirty(definition);
    }
}
