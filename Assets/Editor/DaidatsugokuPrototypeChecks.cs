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
            LastReport = "PASS: stepped 25-cell board, 5 initial boundary fences, shared boundaries, downstream ray protection, offset adjacency, row-specific exits, reserve deployment, pointer selection, input locks, duplicate rejection, removal, capture, draw, deck refill, full victory, retry.";
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

    // Legacy movement/guard scenarios start with all pieces deployed.
    private static void RestartWithDeployedPieces()
    {
        game.RestartGame();
        for (int row = 0; row < GridManager.RowCount; row++)
        for (int x = 0; x < game.Grid.GetRowLength(row); x++) game.Grid.RemoveFence(new Vector2Int(x, row));
        foreach (Prisoner prisoner in game.Prisoners) prisoner.Deploy(new Vector2Int(0, prisoner.Owner));
        game.Grid.RefreshPiecePositions(game.Prisoners);
        game.SelectMove();
    }
    private static IEnumerator Checks()
    {
        game.RestartGame(); yield return null;
        int cells = 0;
        for (int row = 0; row < GridManager.RowCount; row++)
        {
            Require(game.Grid.GetRowLength(row) == row + 3, "Row length incorrect");
            for (int x = 0; x < game.Grid.GetRowLength(row); x++) cells++;
            Require(game.Grid.IsExitCell(new Vector2Int(row + 2, row)), "Row exit incorrect");
        }
        Require(cells == 25 && game.Grid.FenceCount == 5, "Board/fence count incorrect");
        Vector2Int[] initial = { new(0, 1), new(0, 2), new(2, 2), new(4, 3), new(2, 4) };
        foreach (Vector2Int cell in initial) Require(game.Grid.HasFence(cell), "Initial fence missing");
        Require(game.Grid.BoundaryOf(new Vector2Int(0, 1), CellEdge.South).Equals(game.Grid.BoundaryOf(new Vector2Int(0, 2), CellEdge.North)), "Shared boundary key incorrect");
        Require(!game.Grid.PlaceFence(game.Grid.BoundaryOf(new Vector2Int(0, 1), CellEdge.South)), "Opposite-side duplicate fence accepted");
        Require(game.Grid.IsProtected(new Vector2Int(1, 4)) && game.Grid.IsProtected(new Vector2Int(3, 4)), "Downstream protection failed");
        Require(!game.Grid.IsProtected(Vector2Int.zero) && !game.Grid.IsProtected(new Vector2Int(2, 0)), "Fence incorrectly protected upstream cells");
        FenceBoundary vertical = game.Grid.BoundaryOf(new Vector2Int(1, 0), CellEdge.West);
        Require(game.Grid.PlaceFence(vertical) && !game.Grid.IsProtected(new Vector2Int(1, 0)), "Vertical boundary incorrectly blocked downward sight");
        game.Grid.RemoveFence(vertical);
        Require(!game.Grid.CanMove(Vector2Int.zero, new Vector2Int(0, 1)) && game.Grid.CanMove(Vector2Int.zero, new Vector2Int(1, 1)), "Offset adjacency incorrect");
        game.RestartGame();
        Require(game.OnBoardCount(0) == 0 && game.ReserveCount(0) == 3 && game.MustPlacePrisoner, "Reserve setup failed");
        game.SelectPlaceFence(); game.SelectMove(); game.SelectRemoveFence();
        Require(game.Action == PlayerAction.PlacePrisoner, "Mandatory deployment allowed another action");
        game.OnCellClicked(new Vector2Int(-1, 0));
        Require(game.State == GameState.Player1Turn && game.ReserveCount(0) == 3, "Invalid deployment consumed turn");
        game.OnCellClicked(new Vector2Int(4, 3));
        Require(game.State == GameState.Handoff && game.OnBoardCount(0) == 1 && game.ReserveCount(0) == 2 && P(0).Cell == new Vector2Int(4, 3), "Free deployment failed");
        game.ConfirmHandoff();
        Require(game.MustPlacePrisoner && game.State != GameState.Result, "Reserve pieces ignored in result check");
        game.RestartGame(); P(0).Deploy(Vector2Int.zero); game.SelectPlaceFence();
        Require(game.Action == PlayerAction.PlaceFence && !game.MustPlacePrisoner, "Fence choice with deployed piece failed");
        game.SelectPlacePrisoner(); game.OnPrisonerClicked(P(0));
        Require(game.OnBoardCount(0) == 2 && game.ReserveCount(0) == 1 && game.State == GameState.Handoff, "Additional deployment on occupied cell failed");
        RestartWithDeployedPieces();
        game.SelectPlaceFence();
        GridCell tile = game.GetComponentsInChildren<GridCell>().First(c => c.Cell == Vector2Int.zero);
        var edgeClick = new PointerEventData(EventSystem.current);
        edgeClick.pointerCurrentRaycast = new RaycastResult { worldPosition = game.Grid.WorldPosition(Vector2Int.zero) + Vector3.right * .65f };
        ExecuteEvents.Execute(tile.gameObject, edgeClick, ExecuteEvents.pointerClickHandler);
        FenceBoundary east = game.Grid.BoundaryOf(Vector2Int.zero, CellEdge.East);
        Require(game.Grid.HasFence(east) && game.State == GameState.Handoff, "Pointer edge placement failed");
        game.ConfirmHandoff(); game.SelectRemoveFence();
        tile = game.GetComponentsInChildren<GridCell>().First(c => c.Cell == new Vector2Int(1, 0));
        edgeClick.pointerCurrentRaycast = new RaycastResult { worldPosition = game.Grid.WorldPosition(tile.Cell) + Vector3.left * .65f };
        ExecuteEvents.Execute(tile.gameObject, edgeClick, ExecuteEvents.pointerClickHandler);
        Require(!game.Grid.HasFence(east) && game.State == GameState.Handoff, "Opposite edge pointer removal failed");
        RestartWithDeployedPieces(); yield return null;
        Require(game.Prisoners.Count == 9 && game.State == GameState.Player1Turn, "Setup failed");
        for (int i = 0; i < 9; i++) Require(P(i).Cell == new Vector2Int(0, i / 3), "Initial cell incorrect");
        ExecuteEvents.Execute(P(0).gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
        game.OnCellClicked(new Vector2Int(2, 0));
        Require(P(0).Cell == Vector2Int.zero && game.State == GameState.Player1Turn, "Invalid move consumed turn");
        game.OnCellClicked(new Vector2Int(0, 1));
        Require(game.State == GameState.Player1Turn, "Diagonal step consumed turn");
        game.OnCellClicked(new Vector2Int(1, 1));
        Require(P(0).Cell == new Vector2Int(1, 1) && game.State == GameState.Handoff, "Vertical move failed");
        game.SelectPlaceFence(); game.OnCellClicked(new Vector2Int(4, 3));
        Require(!game.Grid.HasFence(new Vector2Int(4, 3)), "Handoff allowed board input");
        game.ConfirmHandoff(); Require(game.State == GameState.Player2Turn, "Handoff failed");

        RestartWithDeployedPieces();
        game.SelectPlaceFence(); game.OnPrisonerClicked(P(0));
        Require(game.Grid.HasFence(Vector2Int.zero) && game.State == GameState.Handoff, "Fence on occupied cell failed");
        game.ConfirmHandoff(); game.SelectPlaceFence(); game.OnCellClicked(Vector2Int.zero);
        Require(game.State == GameState.Player2Turn && game.Grid.FenceCount == 1, "Duplicate fence consumed turn");
        game.SelectRemoveFence(); game.OnCellClicked(Vector2Int.zero);
        Require(!game.Grid.HasFence(Vector2Int.zero) && game.State == GameState.Handoff, "Fence removal failed");

        RestartWithDeployedPieces();
        P(0).MoveTo(new Vector2Int(1, 0)); game.Grid.PlaceFence(Vector2Int.zero);
        Require(game.Guard.Resolve(PatrolCard.LookAway, game.Grid, game.Prisoners) == 0 && game.Prisoners.All(p => p.IsOnBoard), "Look-away captured prisoners");
        int captures = game.Guard.Resolve(PatrolCard.WatchAllCells, game.Grid, game.Prisoners);
        Require(captures == 7 && P(0).Captured && P(1).IsOnBoard && P(2).IsOnBoard, "Same-cell protection/capture failed");
        game.Guard.ResetDeck();
        for (int batch = 0; batch < 3; batch++)
        {
            int watching = 0;
            for (int i = 0; i < 8; i++)
            {
                PatrolCard card = game.Guard.DrawCard();
                Require(card == PatrolCard.WatchAllCells || card == PatrolCard.LookAway, "Unknown patrol card");
                if (card == PatrolCard.WatchAllCells) watching++;
            }
            Require(watching == 4, "Patrol deck/refill must contain both states equally");
        }

        RestartWithDeployedPieces();
        for (int player = 0; player < 3; player++)
        {
            game.OnPrisonerClicked(P(player * 3)); game.MoveRightOrEscape();
            if (player < 2) game.ConfirmHandoff();
        }
        double deadline = EditorApplication.timeSinceStartup + 5;
        while (game.State == GameState.GuardTurn && EditorApplication.timeSinceStartup < deadline) yield return null;
        if (game.State == GameState.Handoff)
        {
            Require(game.Prisoners.All(p => p.IsOnBoard) && game.Round == 2 && game.Message.Contains("見ていません"), "Automatic look-away failed");
            // Verify the capture/result path independently of the shuffled first card.
            game.Guard.Resolve(PatrolCard.WatchAllCells, game.Grid, game.Prisoners);
            typeof(GameManager).GetMethod("CheckResult", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(game, null);
        }
        Require(game.State == GameState.Result && game.Prisoners.All(p => p.Captured), "Automatic guard/draw failed");
        game.OnCellClicked(Vector2Int.zero); game.ConfirmHandoff();
        Require(game.State == GameState.Result, "Result accepted input");

        RestartWithDeployedPieces();
        // Fixture: shelter the entire winning route and the stationary P2/P3 prisoners.
        for (int x = 0; x < game.Grid.GetRowLength(0); x++) game.Grid.PlaceFence(new Vector2Int(x, 0));
        game.Grid.PlaceFence(new Vector2Int(0, 1)); game.Grid.PlaceFence(new Vector2Int(0, 2));
        for (int piece = 0; piece < 3; piece++)
        for (int step = 0; step < game.Grid.GetRowLength(0); step++)
        {
            Require(game.State == GameState.Player1Turn, "Expected P1 turn");
            game.OnPrisonerClicked(P(piece)); game.MoveRightOrEscape();
            if (piece == 2 && step == game.Grid.GetRowLength(0) - 1) break;
            game.ConfirmHandoff(); ToggleSpareFence(); game.ConfirmHandoff(); ToggleSpareFence();
            deadline = EditorApplication.timeSinceStartup + 5;
            while (game.State == GameState.GuardTurn && EditorApplication.timeSinceStartup < deadline) yield return null;
            Require(game.State == GameState.Handoff, "Guard did not hand off to P1"); game.ConfirmHandoff();
        }
        Require(game.State == GameState.Result && game.EscapedCount(0) == 3 && game.ResultMessage.Contains("P1"), "Three-prisoner victory failed");
        game.RestartGame(); yield return null;
        Require(game.State == GameState.Player1Turn && game.Grid.FenceCount == 5 && game.EscapedCount(0) == 0 && game.Prisoners.All(p => p.IsInReserve && !p.IsOnBoard), "Retry did not reset game");
        Require(game.GetComponentsInChildren<Prisoner>(true).Length == 9, "Retry leaked prisoners");
    }
}
