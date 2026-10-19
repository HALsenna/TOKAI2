using System;
using System.Collections;
using System.Linq;
using Daidatsugoku;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

// Run against the real Play-mode scene, including handoff locks and guard coroutines.
public static class DaidatsugokuPrototypeChecks
{
    public static string LastReport { get; private set; } = "Not run";
    private static IEnumerator routine;
    private static GameManager game;
    private static System.Reflection.FieldInfo delayField;
    private static float originalDelay;

    [MenuItem("Tools/Daidatsugoku/Run Play Mode Checks")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Open the prototype and enter Play mode first.");
        if (routine != null) throw new InvalidOperationException("Checks are already running.");
        game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        if (game == null) throw new InvalidOperationException("Prototype GameManager not found.");
        delayField = typeof(GameManager).GetField("guardRevealSeconds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        originalDelay = (float)delayField.GetValue(game); delayField.SetValue(game, 0f);
        LastReport = "Running"; routine = Checks(); EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || game == null) throw new InvalidOperationException("Play mode ended during checks.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (routine.MoveNext()) return;
            LastReport = "PASS: setup, pointer selection, invalid moves, handoff input lock, fences on occupied cells, duplicate rejection, removal, guard same-cell protection, capture, draw, deck refill, full 3-prisoner victory, result lock, retry.";
            Debug.Log(LastReport); Cleanup();
        }
        catch (Exception exception)
        {
            LastReport = "FAIL: " + exception.Message; Debug.LogException(exception); Cleanup();
        }
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Tick; routine = null;
        if (game != null) { delayField.SetValue(game, originalDelay); game.RestartGame(); }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static Prisoner P(int index) => game.Prisoners[index];
    private static void ToggleSpareFence()
    {
        Vector2Int spare = new(4, 3);
        if (game.Grid.HasFence(spare)) game.SelectRemoveFence(); else game.SelectPlaceFence();
        game.OnCellClicked(spare);
    }

    private static IEnumerator Checks()
    {
        game.RestartGame(); yield return null;
        Require(game.Prisoners.Count == 9 && game.State == GameState.Player1Turn, "Setup failed");
        for (int i = 0; i < 9; i++) Require(P(i).Cell == new Vector2Int(0, i / 3), "Initial cell incorrect");
        ExecuteEvents.Execute(P(0).gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
        game.OnCellClicked(new Vector2Int(2, 0));
        Require(P(0).Cell == Vector2Int.zero && game.State == GameState.Player1Turn, "Invalid move consumed turn");
        game.OnCellClicked(new Vector2Int(0, 1));
        Require(P(0).Cell == new Vector2Int(0, 1) && game.State == GameState.Handoff, "Vertical move failed");
        game.SelectPlaceFence(); game.OnCellClicked(new Vector2Int(4, 3));
        Require(!game.Grid.HasFence(new Vector2Int(4, 3)), "Handoff allowed board input");
        game.ConfirmHandoff(); Require(game.State == GameState.Player2Turn, "Handoff failed");

        game.RestartGame();
        game.SelectPlaceFence(); game.OnPrisonerClicked(P(0));
        Require(game.Grid.HasFence(Vector2Int.zero) && game.State == GameState.Handoff, "Fence on occupied cell failed");
        game.ConfirmHandoff(); game.SelectPlaceFence(); game.OnCellClicked(Vector2Int.zero);
        Require(game.State == GameState.Player2Turn && game.Grid.FenceCount == 1, "Duplicate fence consumed turn");
        game.SelectRemoveFence(); game.OnCellClicked(Vector2Int.zero);
        Require(!game.Grid.HasFence(Vector2Int.zero) && game.State == GameState.Handoff, "Fence removal failed");

        game.RestartGame();
        P(0).MoveTo(new Vector2Int(1, 0)); game.Grid.PlaceFence(Vector2Int.zero);
        int captures = game.Guard.Resolve(game.Guard.DrawCard(), game.Grid, game.Prisoners);
        Require(captures == 7 && P(0).Captured && P(1).IsOnBoard && P(2).IsOnBoard, "Same-cell protection/capture failed");
        for (int i = 0; i < 20; i++) game.Guard.DrawCard();

        game.RestartGame();
        for (int player = 0; player < 3; player++)
        {
            game.OnPrisonerClicked(P(player * 3)); game.MoveRightOrEscape();
            if (player < 2) game.ConfirmHandoff();
        }
        double deadline = EditorApplication.timeSinceStartup + 5;
        while (game.State == GameState.GuardTurn && EditorApplication.timeSinceStartup < deadline) yield return null;
        Require(game.State == GameState.Result && game.Prisoners.All(p => p.Captured), "Automatic guard/draw failed");
        game.OnCellClicked(Vector2Int.zero); game.ConfirmHandoff();
        Require(game.State == GameState.Result, "Result accepted input");

        game.RestartGame();
        // Fixture: shelter the entire winning route and the stationary P2/P3 prisoners.
        for (int x = 0; x < 5; x++) game.Grid.PlaceFence(new Vector2Int(x, 0));
        game.Grid.PlaceFence(new Vector2Int(0, 1)); game.Grid.PlaceFence(new Vector2Int(0, 2));
        for (int piece = 0; piece < 3; piece++)
        for (int step = 0; step < 5; step++)
        {
            Require(game.State == GameState.Player1Turn, "Expected P1 turn");
            game.OnPrisonerClicked(P(piece)); game.MoveRightOrEscape();
            if (piece == 2 && step == 4) break;
            game.ConfirmHandoff(); ToggleSpareFence(); game.ConfirmHandoff(); ToggleSpareFence();
            deadline = EditorApplication.timeSinceStartup + 5;
            while (game.State == GameState.GuardTurn && EditorApplication.timeSinceStartup < deadline) yield return null;
            Require(game.State == GameState.Handoff, "Guard did not hand off to P1"); game.ConfirmHandoff();
        }
        Require(game.State == GameState.Result && game.EscapedCount(0) == 3 && game.ResultMessage.Contains("P1"), "Three-prisoner victory failed");
        game.RestartGame(); yield return null;
        Require(game.State == GameState.Player1Turn && game.Grid.FenceCount == 0 && game.EscapedCount(0) == 0 && game.Prisoners.All(p => p.IsOnBoard), "Retry did not reset game");
        Require(game.GetComponentsInChildren<Prisoner>(true).Length == 9, "Retry leaked prisoners");
    }
}
