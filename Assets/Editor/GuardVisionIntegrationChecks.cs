using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Daidatsugoku;
using UnityEditor;
using UnityEngine;

/// <summary>Real-scene SYS-VISION integration checks using explicit card/field fixtures.</summary>
public static class GuardVisionIntegrationChecks
{
    public static int LastAssertions { get; private set; }

    private sealed class CountingVision : IGuardVisionSnapshotService
    {
        private readonly GuardVisionSystem real;
        public int Snapshots, IndividualQueries;
        public CountingVision(GuardVisionSystem real) { this.real = real; }
        public IReadOnlyCollection<VisionCell> CalculateVisibleCells(GridManager grid) { Snapshots++; return real.CalculateVisibleCells(grid); }
        public bool IsVisible(GridManager grid, Prisoner prisoner) { IndividualQueries++; return real.IsVisible(grid, prisoner); }
    }

    private sealed class BatchFailure : IGuardPrisonerBatchService
    {
        public int Batches, IndividualCalls;
        public bool AllAliveBeforeTransition = true;
        public IReadOnlyList<Prisoner> Last;
        public void FailEscape(Prisoner prisoner) { IndividualCalls++; prisoner.Capture(); }
        public void FailEscape(IReadOnlyList<Prisoner> prisoners)
        {
            Batches++; Last = prisoners;
            AllAliveBeforeTransition &= prisoners.All(prisoner => prisoner.IsOnBoard);
            foreach (Prisoner prisoner in prisoners) prisoner.Capture();
        }
    }

    private sealed class Discovery
    {
        public int Calls;
        public bool AllAlive = true, ReadOnly = true;
        public IReadOnlyList<Prisoner> Last;
        public void Receive(IReadOnlyList<Prisoner> targets)
        {
            Calls++; Last = targets; AllAlive &= targets.All(prisoner => prisoner.IsOnBoard);
            bool rejected = false;
            try { ((IList<Prisoner>)targets)[0] = targets[0]; }
            catch (NotSupportedException) { rejected = true; }
            ReadOnly &= rejected;
        }
    }

    public static IEnumerator Run(GameManager game)
    {
        LastAssertions = 0;
        var fixtures = new List<ScriptableObject>();
        GuardVisionFieldDefinition originalField = game.Vision.Field;
        IGuardVisionService originalVision = game.Guard.VisionService;
        IGuardPrisonerService originalPrisoners = game.Guard.PrisonerService;
        GuardCardSource originalSource = game.Guard.MonitoringSource;
        GuardVisionDebugDisplay debug = game.GetComponent<GuardVisionDebugDisplay>();
        bool addedDebug = debug == null;
        if (addedDebug) debug = game.gameObject.AddComponent<GuardVisionDebugDisplay>();
        bool originalDebug = debug.ShowVision;
        var discovery = new Discovery();
        game.Guard.PrisonersDiscovered += discovery.Receive;
        try
        {
            var field = Fixture<GuardVisionFieldDefinition>(fixtures);
            var inert = Fixture<TurnCheckCard>(fixtures); inert.Condition = (manager, owner, trigger) => false;
            var policy = Fixture<TurnCheckPolicy>(fixtures);
            var watching = Fixture<TurnCheckMonitoring>(fixtures); watching.Card = PatrolCard.WatchAllCells;
            Setup(game, field, inert, policy, watching);
            CheckFieldAndPosition(game, field);
            CheckLatestFences(game);
            CheckDebug(game, debug);

            var counting = new CountingVision(game.Vision);
            game.Guard.VisionService = counting;
            Require(game.Guard.FindTargets(PatrolCard.NotWatching, game.Grid, game.Prisoners).Count == 0 && counting.Snapshots == 0 && counting.IndividualQueries == 0,
                "NOT_WATCHING must not calculate vision.");
            Require(game.Guard.FindTargets(PatrolCard.WatchAllCells, game.Grid, game.Prisoners, false).Count == 0 && counting.Snapshots == 0 && discovery.Calls == 0,
                "Disabled monitoring must not calculate vision or notify discovery.");

            yield return CheckPendingVision(game, field, inert, policy, watching, fixtures, discovery);
            game.Guard.PrisonersDiscovered -= discovery.Receive;
            discovery = new Discovery(); game.Guard.PrisonersDiscovered += discovery.Receive;
            yield return CheckCancellation(game, field, inert, policy, watching, fixtures, discovery);
        }
        finally
        {
            game.Guard.PrisonersDiscovered -= discovery.Receive;
            debug.ShowVision = originalDebug;
            game.Guard.VisionService = originalVision; game.Guard.PrisonerService = originalPrisoners;
            game.Guard.MonitoringSource = originalSource; game.Vision.Field = originalField;
            game.Configure(2, Array.Empty<SkillCardDefinition>()); game.InitializeGame();
            if (addedDebug) UnityEngine.Object.DestroyImmediate(debug);
            foreach (ScriptableObject fixture in fixtures) if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void CheckFieldAndPosition(GameManager game, GuardVisionFieldDefinition field)
    {
        GuardVisionSystem vision = game.Vision;
        vision.Field = null;
        Require(!vision.TryResetPosition(out var missing) && !string.IsNullOrEmpty(missing) && vision.Position == null,
            "Missing field configuration must fail explicitly without a random fallback.");
        vision.Field = field; field.GuardColumn = field.ColumnCount;
        Require(!vision.TryResetPosition(out var column) && !string.IsNullOrEmpty(column), "An out-of-range guard column must be rejected.");
        field.GuardColumn = 3; field.GuardSide = (GuardSide)99;
        Require(!vision.TryResetPosition(out _), "Only TOP/BOTTOM are valid guard sides.");
        field.GuardSide = GuardSide.Top; field.RowCount++;
        Require(!vision.TryResetPosition(out _), "A mismatched prototype field envelope must not be silently adapted.");
        field.RowCount = GridManager.RowCount;
        Require(vision.TryResetPosition(out _), "A valid explicit prototype field must initialize.");
        Require(vision.Position.Column == 3 && vision.Position.Side == GuardSide.Top, "Initial position must come from the field data.");
        int history = game.Guard.PublicHistory.Count;
        Prisoner[] onBoard = game.Prisoners.Where(prisoner => prisoner.IsOnBoard).ToArray();
        vision.Flip();
        Require(vision.Position.Column == 3 && vision.Position.Side == GuardSide.Bottom, "Flip must change only the side.");
        vision.CalculateVisibleCells(game.Grid);
        Require(vision.Position.Side == GuardSide.Bottom, "Calculating vision must preserve the flipped side.");
        GuardPosition copy = vision.Position; copy.Flip();
        Require(vision.Position.Side == GuardSide.Bottom, "Position snapshots must not bypass the component's state updates.");
        vision.Flip(); vision.Flip(); vision.Flip();
        Require(vision.Position.Side == GuardSide.Top && vision.Position.Column == 3, "Every repeated flip must toggle once and preserve the column.");
        Require(game.Guard.PublicHistory.Count == history && game.Prisoners.Where(prisoner => prisoner.IsOnBoard).SequenceEqual(onBoard),
            "Flipping alone must not draw, discover or remove prisoners.");
        Transform[] posts = game.GetComponentsInChildren<Transform>(true).Where(value => value.name == "GuardPost" && value.gameObject.activeInHierarchy).ToArray();
        Require(posts.Length == 1 && posts[0].position == game.Grid.GuardWorldPosition(3, GuardSide.Top),
            "Exactly one guard post must display the current field position.");
        vision.Flip();
        Require(posts[0].position == game.Grid.GuardWorldPosition(3, GuardSide.Bottom), "The same guard post must move to BOTTOM without animation.");
        vision.Flip();
    }

    private static void CheckLatestFences(GameManager game)
    {
        ClearFences(game.Grid);
        var a = new VisionCell(3, 1); var b = new VisionCell(4, 1);
        Require(game.Vision.CalculateVisibleCells(game.Grid).Contains(a) && game.Vision.CalculateVisibleCells(game.Grid).Contains(b), "The explicit stepped adapter must retain real visible cells.");
        game.Grid.PlaceFence(new FenceBoundary(1, 1));
        Require(game.Vision.CalculateVisibleCells(game.Grid).Contains(a) && game.Vision.CalculateVisibleCells(game.Grid).Contains(b), "A fence outside basic vision must not block deeper visible cells.");
        game.Grid.PlaceFence(new FenceBoundary(1, 3));
        Require(!game.Vision.CalculateVisibleCells(game.Grid).Contains(a) && !game.Vision.CalculateVisibleCells(game.Grid).Contains(b), "TOP must block a valid fence's own cell and deeper cells.");
        game.Grid.RemoveFence(new FenceBoundary(1, 3));
        Require(game.Vision.CalculateVisibleCells(game.Grid).Contains(a) && game.Vision.CalculateVisibleCells(game.Grid).Contains(b), "Destroying the blocking fence must affect the very next calculation.");
        ClearFences(game.Grid); game.Vision.Flip(); game.Grid.PlaceFence(new FenceBoundary(3, 3));
        var bottom = game.Vision.CalculateVisibleCells(game.Grid);
        Require(bottom.Contains(new VisionCell(3, 3)) && !bottom.Contains(new VisionCell(2, 3)), "BOTTOM must keep the fence's cell visible and block only above it.");
        game.Grid.RemoveFence(new FenceBoundary(3, 3));
        Require(game.Vision.CalculateVisibleCells(game.Grid).Contains(new VisionCell(2, 3)), "BOTTOM must use the latest fence destruction.");
        game.Vision.Flip(); ClearFences(game.Grid);
    }

    private static void CheckDebug(GameManager game, GuardVisionDebugDisplay debug)
    {
        Require(!debug.ShowVision, "Vision debugging must be off by default during normal play.");
        int round = game.Round, history = game.Guard.PublicHistory.Count;
        GameState state = game.State; TurnPhase phase = game.Phase;
        debug.RefreshDebugView();
        var before = new HashSet<VisionCell>(debug.VisibleCells);
        Require(before.SetEquals(game.Vision.CalculateVisibleCells(game.Grid)), "Explicit debug refresh must expose the current final vision snapshot.");
        game.Grid.PlaceFence(new FenceBoundary(3, 0));
        Require(before.SetEquals(debug.VisibleCells), "Debug drawing must keep its snapshot until explicitly refreshed.");
        debug.ShowVision = true; debug.RefreshDebugView();
        Require(!before.SetEquals(debug.VisibleCells) && debug.FenceCells.Contains(new VisionCell(0, 3)), "Explicit refresh must include current fences and changed visibility.");
        Require(game.Round == round && game.State == state && game.Phase == phase && game.Guard.PublicHistory.Count == history && game.Prisoners.All(prisoner => !prisoner.Captured),
            "Debug inspection must not advance turns, draw monitoring or discover prisoners.");
        debug.ShowVision = false; ClearFences(game.Grid);
    }

    private static IEnumerator CheckPendingVision(GameManager game, GuardVisionFieldDefinition field, TurnCheckCard inert,
        TurnCheckPolicy policy, TurnCheckMonitoring source, List<ScriptableObject> fixtures, Discovery discovery)
    {
        Setup(game, field, inert, policy, source);
        var opening = Fixture<TurnCheckCard>(fixtures); opening.AllowMonitoringInterruption = true;
        opening.Condition = (manager, owner, trigger) => Root(trigger)?.Kind == TurnEventKind.GuardMonitoring;
        Give(game, 0, opening, inert);
        policy.PlanChains = true; policy.A = 0; policy.B = 1; policy.First = 0;
        Prisoner visibleA = Piece(game, 0, 1), blocked = Piece(game, 0, 2), oldOnly = Piece(game, 1, 1), visibleB = Piece(game, 1, 2);
        Deploy(game, visibleA, 4, 3); Deploy(game, blocked, 0, 3); Deploy(game, oldOnly, 4, 5); Deploy(game, visibleB, 4, 2);
        var counting = new CountingVision(game.Vision); var failure = new BatchFailure();
        game.Guard.VisionService = counting; game.Guard.PrisonerService = failure;
        game.StartFirstTurn(); FinishPlayerTurns(game);
        yield return Until(() => game.IsChainOpen, "The permitted monitoring interruption must open before vision calculation.");
        Require(source.Draws == 1 && game.Guard.PublicHistory.Count == 1 && counting.Snapshots == 0 && discovery.Calls == 0,
            "A pending monitoring chain must reveal exactly one card without calculating vision or discovering prisoners.");
        Require(game.Context.Kind == TurnEventKind.GuardMonitoring && game.Context.MonitoringResult == PatrolCard.WatchAllCells,
            "The interruption context must expose its already drawn monitoring result.");
        game.Vision.Flip(); game.Grid.PlaceFence(new FenceBoundary(3, 4));
        Require(counting.Snapshots == 0 && discovery.Calls == 0, "Position/fence changes alone must not execute discovery.");
        game.PassResponse(); game.PassResponse();
        yield return Until(() => game.Round == 2, "Resolved monitoring must finish its round.");
        Require(counting.Snapshots == 1 && counting.IndividualQueries == 0, "Final monitoring must calculate one shared, latest vision snapshot.");
        Require(discovery.Calls == 1 && discovery.AllAlive && discovery.ReadOnly && new HashSet<Prisoner>(discovery.Last).SetEquals(new[] { visibleA, visibleB }),
            "All owners' visible prisoners must be notified together as a read-only set before transitions.");
        Require(failure.Batches == 1 && failure.IndividualCalls == 0 && failure.AllAliveBeforeTransition && ReferenceEquals(failure.Last, discovery.Last),
            "The complete discovery snapshot must be delivered once to the batch prisoner service.");
        Require(visibleA.Captured && visibleB.Captured && blocked.IsOnBoard && oldOnly.IsOnBoard, "Discovery must use the latest BOTTOM position and latest blocking fence.");
        visibleA.Deploy(Vector2Int.zero); visibleB.Deploy(Vector2Int.zero);
        Require(visibleA.Captured && visibleB.Captured && !visibleA.IsOnBoard && !visibleB.IsOnBoard && game.EscapedCount(0) == 0 && game.EscapedCount(1) == 0,
            "Discovered prisoners cannot redeploy or increase escaped totals.");
        Require(source.Draws == 1 && game.Guard.PublicHistory.Count == 1 && game.Vision.Position.Side == GuardSide.Bottom,
            "Resolution must preserve the flipped state and not redraw or duplicate history.");
    }

    private static IEnumerator CheckCancellation(GameManager game, GuardVisionFieldDefinition field, TurnCheckCard inert,
        TurnCheckPolicy policy, TurnCheckMonitoring source, List<ScriptableObject> fixtures, Discovery discovery)
    {
        Setup(game, field, inert, policy, source);
        int cancellationEffects = 0, forbiddenEffects = 0;
        var cancel = Fixture<TurnCheckCard>(fixtures); cancel.AllowMonitoringInterruption = true;
        cancel.Condition = (manager, owner, trigger) => Root(trigger)?.Kind == TurnEventKind.GuardMonitoring;
        cancel.Effect = (manager, owner, trigger) => { Root(trigger).Cancelled = true; cancellationEffects++; };
        var forbidden = Fixture<TurnCheckCard>(fixtures);
        forbidden.Condition = (manager, owner, trigger) => Root(trigger)?.Kind == TurnEventKind.GuardMonitoring;
        forbidden.Effect = (manager, owner, trigger) => forbiddenEffects++;
        Give(game, 0, cancel, inert); Give(game, 0, forbidden, inert); Give(game, 1, forbidden, inert);
        policy.PlanChains = true; policy.A = 0; policy.B = 1; policy.First = 0;
        Prisoner visible = Piece(game, 0, 1); Deploy(game, visible, 4, 3);
        var counting = new CountingVision(game.Vision); var failure = new BatchFailure();
        game.Guard.VisionService = counting; game.Guard.PrisonerService = failure;
        game.StartFirstTurn(); FinishPlayerTurns(game);
        yield return Until(() => game.IsChainOpen, "The explicit invalidation card must be allowed to respond.");
        int forbiddenFirst = Index(game, 0, forbidden), cancelIndex = Index(game, 0, cancel);
        Require(!game.CanUseCard(0, forbiddenFirst) && !game.UseCard(0, forbiddenFirst), "A card without monitoring-invalidation permission must be refused at the first response.");
        Require(game.CanUseCard(0, cancelIndex) && game.UseCard(0, cancelIndex), "An explicitly permitted invalidation card must enter the existing chain.");
        Require(game.Responder == 1 && !game.CanUseCard(1, Index(game, 1, forbidden)) && !game.UseCard(1, Index(game, 1, forbidden)),
            "The monitoring permission restriction must also apply to child response events.");
        game.PassResponse(); game.PassResponse();
        yield return Until(() => game.Round == 2, "Cancelled monitoring must finish without discovery.");
        Require(cancellationEffects == 1 && forbiddenEffects == 0 && counting.Snapshots == 0 && counting.IndividualQueries == 0,
            "Monitoring cancellation must resolve once and skip every vision calculation.");
        Require(discovery.Calls == 0 && failure.Batches == 0 && visible.IsOnBoard && game.Prisoners.All(prisoner => !prisoner.Captured),
            "Disabled monitoring must not notify or remove even a visible prisoner.");
        Require(source.Draws == 1 && game.Guard.PublicHistory.Count == 1 && game.Guard.PublicHistory[0].Result == PatrolCard.WatchAllCells,
            "Cancellation must preserve the already revealed history without extra consumption.");
    }

    private static void Setup(GameManager game, GuardVisionFieldDefinition field, TurnCheckCard inert, TurnCheckPolicy policy, TurnCheckMonitoring source)
    {
        field.RowCount = GridManager.RowCount; field.ColumnCount = GridManager.Width; field.GuardColumn = 3; field.GuardSide = GuardSide.Top;
        game.Vision.Field = field; policy.PlanChains = false;
        game.Configure(2, Enumerable.Repeat(inert, 64), null, policy); game.Guard.MonitoringSource = source;
        game.Guard.VisionService = game.Vision; game.Guard.PrisonerService = new PrototypeGuardPrisonerService();
        game.InitializeGame(); ClearFences(game.Grid);
    }
    private static void ClearFences(GridManager grid)
    {
        foreach (VisionCell cell in grid.GetVisionFrontFences()) grid.RemoveFence(new FenceBoundary(cell.Column, cell.Row));
    }
    private static void FinishPlayerTurns(GameManager game)
    {
        for (int turn = 0; turn < 2; turn++)
        {
            if (turn > 0) game.ConfirmHandoff();
            game.SelectPlacePrisoner(); game.OnCellClicked(new Vector2Int(6, 4));
            Require(game.ActionConsumed, "The explicit test action must consume one normal action."); game.EndTurn();
        }
    }
    private static void Give(GameManager game, int owner, TurnCheckCard card, TurnCheckCard inert)
    {
        game.Cards.Reset(new[] { card }, upper => upper - 1);
        Require(game.DrawSkillCard(owner), "The explicit response fixture could not be drawn.");
        game.Cards.Reset(Enumerable.Repeat(inert, 64), upper => upper - 1);
    }
    private static void Deploy(GameManager game, Prisoner prisoner, int row, int column)
    {
        Require(game.Grid.TryGetCell(column, row, out var local), "The explicit prisoner coordinate must exist in the stepped map."); prisoner.Deploy(local);
    }
    private static Prisoner Piece(GameManager game, int owner, int index) => game.Prisoners.Where(piece => piece.Owner == owner).ElementAt(index);
    private static TurnEvent Root(TurnEvent trigger) { while (trigger?.Parent != null) trigger = trigger.Parent; return trigger; }
    private static int Index(GameManager game, int owner, TurnCheckCard card)
    {
        for (int index = 0; index < game.Hand(owner).Count; index++) if (game.Hand(owner)[index] == card) return index;
        throw new InvalidOperationException("The explicit response fixture is missing from its hand.");
    }
    private static T Fixture<T>(List<ScriptableObject> fixtures) where T : ScriptableObject
    {
        var fixture = ScriptableObject.CreateInstance<T>(); fixture.name = "CHECK_Vision_" + typeof(T).Name;
        fixture.hideFlags = HideFlags.HideAndDontSave; fixtures.Add(fixture); return fixture;
    }
    private static IEnumerator Until(Func<bool> condition, string message)
    {
        double deadline = EditorApplication.timeSinceStartup + 8;
        while (!condition() && EditorApplication.timeSinceStartup < deadline) yield return null;
        Require(condition(), message);
    }
    private static void Require(bool condition, string message)
    {
        LastAssertions++; if (!condition) throw new InvalidOperationException("SYS-VISION-001: " + message);
    }
}
