using System;
using System.Linq;
using Daidatsugoku;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Explicit prototype configuration; these values are not a final field specification.</summary>
public static class GuardVisionSceneSetup
{
    public const string FieldPath = "Assets/Settings/PrototypeGuardVisionField.asset";

    [MenuItem("Tools/Daidatsugoku/Configure Prototype Guard Vision")]
    public static void ConfigurePrototype()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before configuring the scene.");
        var scene = EditorSceneManager.OpenScene(DaidatsugokuSceneBuilder.ScenePath);
        GameManager game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        if (game == null) throw new InvalidOperationException("The prototype scene has no GameManager.");
        GuardVisionFieldDefinition field = AssetDatabase.LoadAssetAtPath<GuardVisionFieldDefinition>(FieldPath);
        if (field == null)
        {
            field = ScriptableObject.CreateInstance<GuardVisionFieldDefinition>();
            // Preserve the pre-existing stepped prototype and its displayed guard column.
            field.RowCount = GridManager.RowCount; field.ColumnCount = GridManager.Width;
            field.GuardColumn = 3; field.GuardSide = GuardSide.Top;
            AssetDatabase.CreateAsset(field, FieldPath);
        }
        GuardVisionSystem vision = game.GetComponent<GuardVisionSystem>();
        if (vision == null) vision = game.gameObject.AddComponent<GuardVisionSystem>();
        vision.Field = field;
        if (game.GetComponent<GuardVisionDebugDisplay>() == null) game.gameObject.AddComponent<GuardVisionDebugDisplay>();
        Camera camera = Camera.main;
        if (camera != null)
        {
            // Fit both external guard positions; no gameplay coordinates change.
            Vector3 position = camera.transform.position; position.z = 3.2f; camera.transform.position = position;
            camera.orthographicSize = 6f;
        }
        EditorUtility.SetDirty(vision);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("SYS-VISION-001: prototype field and scene references configured.");
    }

    public static void PreparePreview(GameManager game)
    {
        var inert = ScriptableObject.CreateInstance<TurnCheckCard>(); inert.name = "CHECK_VisionPreviewCard";
        var policy = ScriptableObject.CreateInstance<TurnCheckPolicy>(); policy.name = "CHECK_VisionPreviewPolicy";
        game.Configure(2, Enumerable.Repeat(inert, 64), null, policy);
        game.Guard.MonitoringSource = null;
        game.InitializeGame(); game.StartFirstTurn();
        game.Prisoners[0].Deploy(new Vector2Int(1, 0));
        game.Prisoners[3].Deploy(new Vector2Int(3, 4));
        game.Grid.RefreshPiecePositions(game.Prisoners);
        game.SelectPlacePrisoner();
    }
}
