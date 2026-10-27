using System;
using System.Collections.Generic;
using System.Linq;
using Daidatsugoku;
using UnityEngine;

/// <summary>Real-scene field adapters, rendering, occupancy and selection checks.</summary>
public static class FieldIntegrationChecks
{
    public static int Run(GameManager game)
    {
        int assertions = 0;
        Action<bool, string> require = (condition, message) =>
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("SYS-FIELD-001: " + message);
        };
        FieldStage originalStage = game.SelectedStage;
        var originalCollision = game.CollisionResolver;
        var inert = ScriptableObject.CreateInstance<TurnCheckCard>(); inert.name = "CHECK_Field_Inert";
        var policy = ScriptableObject.CreateInstance<TurnCheckPolicy>(); policy.name = "CHECK_Field_NoResponses";
        var definition = ScriptableObject.CreateInstance<FieldStageDefinition>(); definition.name = "CHECK_Field_Definition";
        try
        {
            CheckCatalogAssets(game, require);
            CheckDraftSelection(game, require);
            CheckConfirmedStageInitialization(game, inert, policy, require);
            FieldStage stage = NewStage();
            definition.Configure(stage);
            FieldStage authored = definition.ToStage();
            require(definition.IncludedInSelection && authored.StageId == "CHECK-EXTRA" && authored.Width == 8 && authored.Height == 6 && authored.CellCount == 32,
                "An additional authoring asset must supply a stage without a hardcoded stage-ID branch.");
            require(FieldStageValidator.TryValidateForPlay(authored, out _), "The explicit integration fixture must validate before it is built.");
            game.ConfigureStage(authored); game.Configure(2, Enumerable.Repeat(inert, 64), null, policy);
            game.InitializeGame();
            CheckGeometry(game, authored, require);
            CheckFencesAndGuard(game, authored, require);
            CheckVisionBridge(game, require);
            CheckOccupancy(game, require);
            CheckBuildRejection(game, authored, require);
            CheckGameplayPorts(game, inert, policy, require);
        }
        finally
        {
            game.CollisionResolver = originalCollision;
            game.ConfigureStage(originalStage); game.Configure(2, Array.Empty<SkillCardDefinition>());
            if (originalStage != null) game.InitializeGame();
            else game.ShowStageSelection();
            UnityEngine.Object.DestroyImmediate(definition);
            UnityEngine.Object.DestroyImmediate(policy);
            UnityEngine.Object.DestroyImmediate(inert);
        }
        return assertions;
    }

    private static void CheckCatalogAssets(GameManager game, Action<bool, string> require)
    {
        FieldStage[] expected = ExpectedStages();
        int[] expectedCounts = { 25, 34, 31, 44 };
        for (int i = 0; i < expected.Length; i++)
        {
            FieldStage reference = expected[i]; string id = reference.StageId;
            FieldStage stage = game.Stages.Single(value => value.StageId == id);
            require(stage.StageName == reference.StageName && stage.Width == reference.Width && stage.Height == reference.Height &&
                stage.GuardColumn == reference.GuardColumn && stage.GuardSide == GuardSide.Top,
                "The serialized stage " + id + " must retain its approved name, dimensions and TOP guard column.");
            require(stage.CellCount == expectedCounts[i] && stage.Cells.Count == expectedCounts[i],
                "The serialized stage " + id + " must have its exact approved cell count.");
            require(stage.Rows.Count == reference.Rows.Count && stage.Rows.All(row => reference.Rows.Any(expectedRow =>
                row.Y == expectedRow.Y && row.StartX == expectedRow.StartX && row.EndX == expectedRow.EndX)),
                "The serialized stage " + id + " must retain every exact approved row span.");
            require(new HashSet<FieldCell>(stage.FenceForbiddenCells).SetEquals(reference.FenceForbiddenCells),
                "The serialized stage " + id + " must retain its documented fence bans.");
            require(stage.InitialFencesDefined && stage.InitialFences.Count == reference.InitialFences.Count &&
                new HashSet<FieldCell>(stage.InitialFences).SetEquals(reference.InitialFences) && game.CanStartStage(stage, out _),
                "The serialized stage " + id + " must retain its confirmed fence coordinates and allow field initialization.");
        }
        FieldStage original = game.Stages.Single(value => value.StageId == "01");
        int[] columns = { 2, 4, 5, 5, 5, 3, 1 };
        require(Enumerable.Range(0, 7).All(x => original.Cells.Count(cell => cell.X == x) == columns[x]),
            "The user-approved Y=2 EndX=5 correction must match all seven original column counts.");
        require(original.TryGetRow(2, out FieldRow row) && row.StartX == 1 && row.EndX == 5 && game.CanStartStage(original, out _),
            "The approved original shape must be playable without a source-conflict blocker.");
        FieldStage asymmetric = game.Stages.Single(value => value.StageId == "03");
        require(asymmetric.TryGetRow(0, out FieldRow bottom) && bottom.EndX == 4 &&
            asymmetric.TryGetRow(1, out FieldRow next) && next.EndX == 4 && asymmetric.CellCount == 31,
            "The approved Stage03 row data must remain 31 cells without inventing a 32nd coordinate.");
    }

    private static void CheckDraftSelection(GameManager game, Action<bool, string> require)
    {
        var ids = new HashSet<string>(game.Stages.Select(stage => stage.StageId));
        require(new[] { "01", "02", "03", "04" }.All(ids.Contains) && ids.Count == 4 && ids.Count == game.Stages.Count,
            "Version1.1 must expose only the four confirmed stages in selection.");
        FieldStageDefinition excluded = Resources.LoadAll<FieldStageDefinition>("FieldStages").Single(value => value.ToStage().StageId == "05");
        require(!excluded.IncludedInSelection, "The existing Stage05 draft must be preserved as authoring data and explicitly excluded from selection.");
        game.ShowStageSelection();
        int history = game.Guard.PublicHistory.Count, round = game.Round, pieces = game.Prisoners.Count;
        FieldStage selected = game.SelectedStage;
        require(!game.SelectStage("05") && ReferenceEquals(game.SelectedStage, selected),
            "The unconfirmed Stage05 must not be selectable or silently substitute an approved stage.");
        require(!game.SelectStage("UNKNOWN") && ReferenceEquals(game.SelectedStage, selected), "Unknown stage IDs must not silently fall back to another stage.");
        FieldStage fixture = NewStage();
        var pending = new FieldStage("CHECK-PENDING", "Explicit unknown-fence fixture", fixture.Width, fixture.Height,
            fixture.Rows, fixture.GuardColumn, GuardSide.Top, false, Array.Empty<FieldCell>());
        game.ConfigureStage(pending);
        require(!game.CanStartStage(pending, out _) && !game.StartSelectedStage() && game.IsSelectingStage && game.State == GameState.Setup,
            "A custom stage with unknown initial fences must still refuse gameplay, even with an empty fence array.");
        require(game.Round == round && game.Guard.PublicHistory.Count == history && game.Prisoners.Count == pieces,
            "Refusing a pending stage must not initialize a match, consume monitoring or alter pieces.");
        game.RestartGame(); game.StartFirstTurn();
        require(game.Phase == TurnPhase.WaitingForSpecification && game.State == GameState.Setup &&
            game.Guard.PublicHistory.Count == history && game.Prisoners.Count == pieces,
            "Restarting an unresolved stage must not resume the previous stage or consume a draw.");
        game.ShowStageSelection(); game.StartFirstTurn();
        require(game.IsSelectingStage && game.Phase == TurnPhase.Setup && game.State == GameState.Setup,
            "The legacy start API must not bypass pre-game stage selection.");
    }

    private static void CheckConfirmedStageInitialization(GameManager game, TurnCheckCard inert, TurnCheckPolicy policy, Action<bool, string> require)
    {
        foreach (FieldStage stage in game.Stages)
        {
            game.ShowStageSelection(); game.Configure(2, Enumerable.Repeat(inert, 64), null, policy);
            require(game.SelectStage(stage.StageId) && game.StartSelectedStage() && !game.IsSelectingStage &&
                ReferenceEquals(game.Grid.Field, stage), "Every confirmed stage must initialize through the stage selection flow.");
            require(game.Prisoners.All(prisoner => prisoner.IsInReserve) && Enumerable.Range(0, game.PlayerCount).All(owner => game.OnBoardCount(owner) == 0),
                "Each confirmed stage must start with zero prisoners on the board.");
            GridCell[] tiles = game.Grid.GetComponentsInChildren<GridCell>().Where(tile => tile.gameObject.activeInHierarchy).ToArray();
            require(tiles.Length == stage.CellCount && new HashSet<FieldCell>(tiles.Select(tile => new FieldCell(tile.Cell.x, tile.Cell.y))).SetEquals(stage.Cells),
                "Every confirmed stage must render exactly its actual cells, without rectangular padding.");
            Transform[] frames = game.Grid.GetComponentsInChildren<Transform>().Where(value => value.gameObject.activeInHierarchy &&
                value.name.StartsWith("CellFrame_", StringComparison.Ordinal)).ToArray();
            require(frames.Length == tiles.Length * 4, "Each actual tile must receive four visible border strips without padding-cell borders.");
            foreach (GridCell tile in tiles)
            {
                Color color = tile.GetComponent<Renderer>().sharedMaterial.color;
                bool isStart = game.Grid.IsStartCell(tile.Cell);
                require(isStart ? color.g > color.r && color.g > color.b : Mathf.Max(color.r, color.g, color.b) - Mathf.Min(color.r, color.g, color.b) < .1f,
                    "Starts must have a green tile surface and normal cells a gray surface.");
                Transform[] tileFrames = frames.Where(value => value.name.StartsWith($"CellFrame_{tile.Cell.x}_{tile.Cell.y}_", StringComparison.Ordinal)).ToArray();
                require(tileFrames.Length == 4 && tileFrames.All(value => value.GetComponent<Renderer>().enabled && !value.GetComponent<Collider>().enabled),
                    "Tile borders must remain visible without stealing cell click input.");
            }
            foreach (FieldRow row in stage.Rows)
                require(game.Grid.IsStartCell(new Vector2Int(row.StartX, row.Y)) && game.Grid.IsExitCell(new Vector2Int(row.EndX, row.Y)),
                    "Each confirmed row must expose its own exact start and right boundary.");
            require(game.Grid.FenceCount == stage.InitialFences.Count, "Each confirmed stage must generate exactly the specified number of initial fences.");
            foreach (FieldCell fence in stage.InitialFences)
            {
                var cell = new Vector2Int(fence.X, fence.Y);
                Transform rendered = game.Grid.GetComponentsInChildren<Transform>().Single(value => value.gameObject.activeInHierarchy &&
                    value.name == "Fence_" + new FenceBoundary(fence.X, fence.Y));
                Vector3 position = game.Grid.WorldPosition(cell);
                require(game.Grid.HasFence(cell) && Mathf.Approximately(rendered.position.x, position.x) &&
                    Mathf.Approximately(rendered.position.z - position.z, .8f) && rendered.position.y > position.y + .09f,
                    "Every initial fence must be visible above the board and centered on its cell's upper edge.");
                if (game.Grid.IsStartCell(cell))
                {
                    TextMesh label = game.Grid.GetComponentsInChildren<TextMesh>().Single(value => value.gameObject.activeInHierarchy &&
                        (value.text == "START" || value.text == "S") && Mathf.Approximately(value.transform.position.x, position.x) &&
                        Mathf.Abs(value.transform.position.z - position.z) < .6f);
                    require(label.gameObject.activeInHierarchy && label.transform.position.y < rendered.position.y && game.Grid.CanPlacePrisoner(cell),
                        "An initial fence at a start must retain its visible start label and free prisoner slot.");
                }
            }
            FieldCell[] bans = stage.FenceForbiddenCells.ToArray();
            require(game.Vision.Position.Column == stage.GuardColumn && game.Vision.Position.Side == GuardSide.Top,
                "Each confirmed stage must use its own initial TOP guard column.");
            game.Vision.Flip();
            require(game.Vision.Position.Column == stage.GuardColumn && game.Vision.Position.Side == GuardSide.Bottom &&
                bans.All(cell => game.Grid.IsFenceForbidden(new Vector2Int(cell.X, cell.Y)) && !game.Grid.PlaceFence(new Vector2Int(cell.X, cell.Y))),
                "Every stage must preserve the initial fence bans and guard column after flipping.");
            require(stage.Cells.All(cell => game.Grid.IsFenceForbidden(new Vector2Int(cell.X, cell.Y)) == bans.Contains(cell)),
                "A guard flip must not introduce new bottom-side fence bans.");
            game.Vision.Flip();
            FieldCell start = stage.StartCells.First(); var startCell = new Vector2Int(start.X, start.Y);
            Prisoner escaping = game.Prisoners[0]; escaping.Deploy(startCell); escaping.Escape();
            require(game.Grid.CanPlacePrisoner(startCell), "An escape must not consume or disable an approved stage's start cell.");
        }
    }

    private static void CheckGeometry(GameManager game, FieldStage stage, Action<bool, string> require)
    {
        GridManager grid = game.Grid;
        require(grid.Width == 8 && grid.RowCount == 6 && ReferenceEquals(grid.Field, stage), "The runtime grid must use the selected stage's dynamic dimensions.");
        GridCell[] cells = grid.GetComponentsInChildren<GridCell>().Where(cell => cell.gameObject.activeInHierarchy).ToArray();
        require(cells.Length == stage.CellCount && new HashSet<Vector2Int>(cells.Select(cell => cell.Cell)).Count == cells.Length,
            "Each defined coordinate must generate one active board tile, with no rectangular padding or duplicate tiles.");
        foreach (GridCell cell in cells)
        {
            require(grid.IsInside(cell.Cell) && cell.transform.position == grid.WorldPosition(cell.Cell), "Rendered cells must keep their global bottom-left XY position.");
            VisionCell topRow = grid.ToVisionCell(cell.Cell);
            require(topRow.Row == stage.Height - 1 - cell.Cell.y && topRow.Column == cell.Cell.x &&
                    grid.TryGetVisionCell(topRow.Column, topRow.Row, out Vector2Int roundTrip) && roundTrip == cell.Cell,
                "The field/vision bridge must invert Y once and round-trip every existing cell.");
        }
        require(!grid.IsInside(new Vector2Int(0, 0)) && !grid.IsInside(new Vector2Int(0, 5)) && !grid.TryGetCell(7, 5, out _),
            "Coordinates inside nominal bounds but outside actual row intervals must remain absent.");
        require(Approximately(grid.WorldPosition(new Vector2Int(3, 1)) - grid.WorldPosition(new Vector2Int(2, 1)), new Vector3(1.6f, 0, 0)) &&
                Approximately(grid.WorldPosition(new Vector2Int(2, 2)) - grid.WorldPosition(new Vector2Int(2, 1)), new Vector3(0, 0, 1.6f)),
            "X must increase right and Y upward with a common global-column alignment.");
        foreach (FieldRow row in stage.Rows)
        {
            require(grid.IsStartCell(new Vector2Int(row.StartX, row.Y)) && grid.IsExitCell(new Vector2Int(row.EndX, row.Y)), "Every rendered row must expose its own start and exit.");
            require(grid.GetRowStart(row.Y) == row.StartX && grid.GetRowEnd(row.Y) == row.EndX && grid.GetRowLength(row.Y) == row.EndX - row.StartX + 1,
                "Runtime row queries must not assume that all rows begin at column zero.");
        }
        require(stage.ClassifyMove(new FieldCell(5, 5), new FieldCell(6, 6)) == FieldMoveKind.Escape &&
                stage.ClassifyMove(new FieldCell(4, 4), new FieldCell(4, 6)) == FieldMoveKind.Invalid,
            "Field boundary ports must prioritize source-row right-diagonal escape while rejecting other missing landings.");
    }

    private static void CheckFencesAndGuard(GameManager game, FieldStage stage, Action<bool, string> require)
    {
        GridManager grid = game.Grid;
        require(grid.FenceCount == 2 && grid.HasFence(new Vector2Int(1, 0)) && grid.HasFence(new Vector2Int(4, 2)), "Only explicitly authored initial fences may be generated.");
        foreach (FieldCell fence in stage.InitialFences)
        {
            string name = "Fence_" + new FenceBoundary(fence.X, fence.Y);
            Transform rendered = grid.GetComponentsInChildren<Transform>().Single(value => value.gameObject.activeInHierarchy && value.name == name);
            Vector3 position = grid.WorldPosition(new Vector2Int(fence.X, fence.Y));
            require(Mathf.Approximately(rendered.position.x, position.x) && Mathf.Approximately(rendered.position.z - position.z, .8f),
                "Initial fences must be centered on the corresponding cell's upper edge, not its center.");
        }
        FieldCell[] bans = stage.FenceForbiddenCells.ToArray();
        foreach (FieldCell ban in bans)
            require(grid.IsFenceForbidden(new Vector2Int(ban.X, ban.Y)) && !grid.PlaceFence(new Vector2Int(ban.X, ban.Y)), "An initial-guard-front banned upper edge must reject placement.");
        require(game.Vision.Position.Column == 4 && game.Vision.Position.Side == GuardSide.Top, "Guard initialization must come from this selected stage, not a fixed fourth column.");
        Transform[] posts = grid.GetComponentsInChildren<Transform>().Where(value => value.gameObject.activeInHierarchy && value.name == "GuardPost").ToArray();
        require(posts.Length == 1 && posts[0].position == grid.GuardWorldPosition(4, GuardSide.Top), "One guard post must display the selected initial position.");
        game.Vision.Flip();
        require(game.Vision.Position.Column == 4 && game.Vision.Position.Side == GuardSide.Bottom && posts[0].position == grid.GuardWorldPosition(4, GuardSide.Bottom), "Flipping must move the same guard without changing its column.");
        require(stage.FenceForbiddenCells.SequenceEqual(bans) && bans.All(cell => grid.IsFenceForbidden(new Vector2Int(cell.X, cell.Y))), "Initial fence bans must remain fixed after a BOTTOM flip.");
        require(!grid.IsFenceForbidden(new Vector2Int(3, 0)) && grid.PlaceFence(new Vector2Int(3, 0)), "Flipping must not invent new forbidden positions on the bottom side.");
        game.Vision.Flip();
        require(!grid.PlaceFence(new FenceBoundary(2, 2, true)) && !grid.PlaceFence(new Vector2Int(0, 5)), "Side edges and nonexistent cells are not field fence locations.");
    }

    private static void CheckVisionBridge(GameManager game, Action<bool, string> require)
    {
        GridManager grid = game.Grid;
        var visible = new HashSet<VisionCell>(game.Vision.CalculateVisibleCells(grid));
        require(visible.All(cell => grid.TryGetVisionCell(cell.Column, cell.Row, out _)), "Vision results must never include a missing field cell.");
        require(!visible.Contains(grid.ToVisionCell(new Vector2Int(4, 2))), "TOP shielding must include a live fence's own cell after the XY conversion.");
        game.Vision.Flip(); visible = new HashSet<VisionCell>(game.Vision.CalculateVisibleCells(grid));
        require(visible.Contains(grid.ToVisionCell(new Vector2Int(4, 2))) && !visible.Contains(grid.ToVisionCell(new Vector2Int(4, 3))),
            "BOTTOM shielding must preserve the fence's own cell and exclude cells above it in global Y.");
        game.Vision.Flip();
    }

    private static void CheckOccupancy(GameManager game, Action<bool, string> require)
    {
        GridManager grid = game.Grid;
        var start = new Vector2Int(1, 0);
        require(grid.CanPlacePrisoner(start) && grid.HasFence(start), "A fence at a free start cell must not consume the prisoner slot.");
        Prisoner first = game.Prisoners[0], second = game.Prisoners[1];
        first.Deploy(start); second.Deploy(start);
        require(first.IsOnBoard && second.IsInReserve && ReferenceEquals(grid.OccupantAt(start), first) && !grid.CanPlacePrisoner(start),
            "A second direct placement must not create two prisoners in the same cell.");
        require(grid.HasFence(start) && grid.OccupantAt(start, first) == null, "Fences may coexist with a prisoner and occupancy exclusion must identify only prisoners.");
        second.Deploy(new Vector2Int(0, 5));
        require(second.IsInReserve, "Direct placement must not create a prisoner on a missing field cell.");
        second.Deploy(new Vector2Int(2, 0));
        try { second.MoveTo(start); } catch (InvalidOperationException) { }
        require(second.Cell == new Vector2Int(2, 0) && ReferenceEquals(grid.OccupantAt(start), first), "Direct movement may not bypass the single-prisoner invariant.");
        grid.RefreshPiecePositions(game.Prisoners);
        require(first.transform.position == grid.WorldPosition(start) + Vector3.up * .4f, "A prisoner sharing a fence cell must render on that cell without a cohabitation offset.");
    }

    private static void CheckBuildRejection(GameManager game, FieldStage original, Action<bool, string> require)
    {
        int fences = game.Grid.FenceCount;
        var invalid = new FieldStage("CHECK-BAD", "CHECK-BAD", original.Width, original.Height, original.Rows,
            original.GuardColumn, GuardSide.Top, true, new[] { new FieldCell(4, 5) });
        try
        {
            game.Grid.SetStage(invalid); bool rejected = false;
            try { game.Grid.Build(game); } catch (InvalidOperationException) { rejected = true; }
            require(rejected && game.Grid.FenceCount == fences && invalid.InitialFences.Count == 1 && invalid.InitialFences[0].Equals(new FieldCell(4, 5)),
                "An invalid initial fence must reject the whole build without silently deleting or relocating the source data.");
        }
        finally { game.Grid.SetStage(original); }
    }

    private static void CheckGameplayPorts(GameManager game, TurnCheckCard inert, TurnCheckPolicy policy, Action<bool, string> require)
    {
        game.Configure(2, Enumerable.Repeat(inert, 64), null, policy); game.InitializeGame(); game.StartFirstTurn();
        int owner = game.CurrentPlayer;
        game.SelectPlacePrisoner(); game.OnCellClicked(new Vector2Int(2, 0));
        require(!game.ActionConsumed && game.OnBoardCount(owner) == 0, "Normal deployment must reject a free non-start cell.");
        game.OnCellClicked(new Vector2Int(1, 0));
        require(game.ActionConsumed && game.OnBoardCount(owner) == 1 && game.Grid.HasFence(new Vector2Int(1, 0)), "Normal deployment must accept a free start even when it has a fence.");

        game.InitializeGame(); game.StartFirstTurn(); owner = game.CurrentPlayer;
        Prisoner stationary = game.Prisoners.First(piece => piece.Owner != owner);
        stationary.Deploy(new Vector2Int(1, 0)); game.SelectPlacePrisoner(); game.OnCellClicked(new Vector2Int(1, 0));
        require(!game.ActionConsumed && game.OnBoardCount(owner) == 0 && ReferenceEquals(game.Grid.OccupantAt(new Vector2Int(1, 0)), stationary),
            "Occupied starts must reject normal placement without initiating a fight or consuming the action.");

        game.InitializeGame(); game.StartFirstTurn(); owner = game.CurrentPlayer;
        Prisoner incoming = game.Prisoners.First(piece => piece.Owner == owner);
        stationary = game.Prisoners.First(piece => piece.Owner != owner);
        incoming.Deploy(new Vector2Int(2, 1)); stationary.Deploy(new Vector2Int(3, 1));
        game.CollisionResolver = null; game.SelectMove(); game.OnPrisonerClicked(incoming); game.OnCellClicked(stationary.Cell);
        require(game.Phase == TurnPhase.WaitingForSpecification && incoming.Cell == new Vector2Int(2, 1) && stationary.Cell == new Vector2Int(3, 1),
            "Until the prisoner fight service is provided, occupied movement must stop at its interface without creating cohabitation or inventing fight rules.");

        game.InitializeGame(); game.StartFirstTurn(); owner = game.CurrentPlayer;
        incoming = game.Prisoners.First(piece => piece.Owner == owner);
        stationary = game.Prisoners.First(piece => piece.Owner != owner);
        incoming.Deploy(new Vector2Int(2, 1)); stationary.Deploy(new Vector2Int(3, 1));
        var rejected = new RejectingCollision(); game.CollisionResolver = rejected;
        game.SelectMove(); game.OnPrisonerClicked(incoming); game.OnCellClicked(stationary.Cell);
        require(rejected.Calls == 1 && game.Phase == TurnPhase.WaitingForSpecification && !game.CanEndTurn &&
            incoming.Cell == new Vector2Int(2, 1) && stationary.Cell == new Vector2Int(3, 1),
            "A failed collision service must retain the waiting phase and stop post-action progress.");
    }

    private sealed class RejectingCollision : IPrisonerCollisionResolver
    {
        public int Calls;
        public bool TryResolve(GameManager game, Prisoner incoming, Prisoner stationary, Vector2Int destination, out string reason)
        { Calls++; reason = "Explicit test rejection"; return false; }
    }

    private static bool Approximately(Vector3 left, Vector3 right) => (left - right).sqrMagnitude < .00001f;

    private static FieldStage[] ExpectedStages() => new[]
    {
        new FieldStage("01", "オリジナル", 7, 5,
            new[] { new FieldRow(0,0,6), new(1,0,5), new(2,1,5), new(3,1,4), new(4,2,4) }, 3, GuardSide.Top, true,
            new[] { new FieldCell(1,3), new(1,2), new(2,0), new(3,2), new(4,1) }),
        new FieldStage("02", "ロング", 8, 5,
            new[] { new FieldRow(0,0,5), new(1,0,6), new(2,0,7), new(3,0,6), new(4,0,5) }, 3, GuardSide.Top, true,
            new[] { new FieldCell(1,4), new(4,3), new(5,2), new(4,1), new(2,0) }),
        new FieldStage("03", "非対称", 7, 6,
            new[] { new FieldRow(0,0,4), new(1,0,4), new(2,1,6), new(3,1,6), new(4,0,4), new(5,0,3) }, 2, GuardSide.Top, true,
            new[] { new FieldCell(0,5), new(2,4), new(4,3), new(5,2), new(2,1) }),
        new FieldStage("04", "断崖", 9, 7,
            new[] { new FieldRow(0,0,8), new(1,0,8), new(2,0,7), new(3,1,6), new(4,1,5), new(5,2,5), new(6,2,4) }, 3, GuardSide.Top, true,
            new[] { new FieldCell(2,5), new(4,4), new(3,3), new(5,2), new(2,1), new(6,1), new(4,0) })
    };

    private static FieldStage NewStage() => new("CHECK-EXTRA", "明示的な統合検証用ステージ", 8, 6,
        new[] { new FieldRow(0, 1, 6), new(1, 0, 7), new(2, 2, 6), new(3, 1, 5), new(4, 2, 6), new(5, 3, 5) },
        4, GuardSide.Top, true, new[] { new FieldCell(1, 0), new(4, 2) });
}
