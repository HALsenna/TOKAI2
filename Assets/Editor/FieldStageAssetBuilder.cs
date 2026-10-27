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
        const string originalPath = "Assets/Resources/FieldStages/Stage01.asset";
        if (AssetDatabase.LoadAssetAtPath<FieldStageDefinition>(originalPath) == null)
        {
            var original = ScriptableObject.CreateInstance<FieldStageDefinition>();
            original.Configure(new FieldStage("01", "オリジナル", 7, 5, new[]
            // User-approved correction: Y=2 ends at X=5, matching 25 cells and the column counts.
            { new FieldRow(0,0,6), new(1,0,5), new(2,1,5), new(3,1,4), new(4,2,4) }, 3, GuardSide.Top, true,
            new[] { new FieldCell(1,3), new(1,2), new(2,0), new(3,2), new(4,1) }));
            AssetDatabase.CreateAsset(original, originalPath);
        }
        SaveDraft("02", "ロングフィールド", 8, 5, 3, new[]
        { new FieldRow(0,0,5), new(1,0,6), new(2,0,7), new(3,0,6), new(4,0,5) });
        SaveDraft("03", "アシンメトリーフィールド", 7, 6, 2, new[]
        { new FieldRow(0,0,3), new(1,0,5), new(2,1,6), new(3,1,6), new(4,0,4), new(5,0,3) });
        SaveDraft("04", "ピラミッドフィールド", 7, 7, 2, new[]
        { new FieldRow(0,1,3), new(1,0,4), new(2,0,5), new(3,0,6), new(4,0,5), new(5,0,4), new(6,1,3) });
        SaveDraft("05", "双頭の監獄", 9, 9, 4, new[]
        { new FieldRow(0,1,6), new(1,1,7), new(2,0,8), new(3,2,5), new(4,3,5), new(5,2,5), new(6,0,8), new(7,1,7), new(8,1,6) });
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.OpenScene(DaidatsugokuSceneBuilder.ScenePath);
        GameManager game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        if (game.GetComponent<GuardVisionSystem>() == null) game.gameObject.AddComponent<GuardVisionSystem>();
        if (game.GetComponent<GuardVisionDebugDisplay>() == null) game.gameObject.AddComponent<GuardVisionDebugDisplay>();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("Field assets configured. Stage 01 has 25 cells; stages 02-05 await initial fence definitions.");
        Debug.Log($"PASS: {FieldStageChecks.Run()} field model assertions.");
    }

    private static void SaveDraft(string id, string title, int width, int height, int column, FieldRow[] rows)
    {
        string path = $"Assets/Resources/FieldStages/Stage{id}.asset";
        if (AssetDatabase.LoadAssetAtPath<FieldStageDefinition>(path) != null) return;
        var definition = ScriptableObject.CreateInstance<FieldStageDefinition>();
        definition.Configure(new FieldStage(id, title, width, height, rows, column, GuardSide.Top, false, Array.Empty<FieldCell>()));
        AssetDatabase.CreateAsset(definition, path);
    }
}
