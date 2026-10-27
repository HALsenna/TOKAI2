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
        FieldStageAssetBuilder.ConfigureProject();
    }

    public static void PreparePreview(GameManager game)
    {
        var inert = ScriptableObject.CreateInstance<TurnCheckCard>(); inert.name = "CHECK_VisionPreviewCard";
        var policy = ScriptableObject.CreateInstance<TurnCheckPolicy>(); policy.name = "CHECK_VisionPreviewPolicy";
        game.Configure(2, Enumerable.Repeat(inert, 64), null, policy);
        game.Guard.MonitoringSource = null;
        game.InitializeGame(); game.StartFirstTurn();
        game.Prisoners[0].Deploy(new Vector2Int(game.Grid.GetRowStart(game.Grid.RowCount - 1), game.Grid.RowCount - 1));
        game.Prisoners[3].Deploy(new Vector2Int(game.Grid.GetRowEnd(0) - 1, 0));
        game.Grid.RefreshPiecePositions(game.Prisoners);
        game.SelectPlacePrisoner();
    }
}
