using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Daidatsugoku;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Explicit test fixtures do not supply production card effects, monitoring rates,
// or otherwise undefined response priorities.
public sealed class TurnCheckCard : SkillCardDefinition
{
    public bool ForceImmediate;
    public bool AllowMonitoringInterruption;
    public int Target = -1;
    public Func<GameManager, int, TurnEvent, bool> Condition;
    public Action<GameManager, int, TurnEvent> Effect;
    public override bool Immediate => ForceImmediate;
    public override bool CanInvalidateGuardMonitoring => AllowMonitoringInterruption;
    public override int TargetPlayer(GameManager game, int owner) => Target;
    public override bool CanPlay(GameManager game, int owner, TurnEvent trigger) => Condition != null && Condition(game, owner, trigger);
    public override void Resolve(GameManager game, int owner, TurnEvent trigger) => Effect?.Invoke(game, owner, trigger);
}
public sealed class TurnCheckRules : NormalActionRules
{
    public bool Deploy, Move, Fences;
    public override bool CanDeploy(GameManager game, int owner, Vector2Int cell) => Deploy && game.Grid.IsInside(cell) && game.ReserveCount(owner) > 0;
    public override bool CanMove(GameManager game, Prisoner piece, Vector2Int destination) => Move && piece != null && piece.IsOnBoard && game.Grid.CanMove(piece.Cell, destination);
    public override bool CanEscape(GameManager game, Prisoner piece) => Move && piece != null && piece.IsOnBoard && game.Grid.IsExitCell(piece.Cell);
    public override bool CanChangeFence(GameManager game, int owner, FenceBoundary boundary, bool placing) => Fences && game.Grid.IsValidBoundary(boundary) && game.Grid.HasFence(boundary) != placing;
}
public sealed class TurnCheckPolicy : TurnFlowPolicy
{
    public bool PlanChains;
    public bool AllowGuardOrder = true;
    public int A, B, First;
    public override bool TryPlanResponses(GameManager game, TurnEvent trigger, IReadOnlyList<int> eligibleOwners, out ResponsePlan plan, out string reason)
    {
        reason = null;
        plan = PlanChains && eligibleOwners.Count > 0 ? new ResponsePlan(A, B, First) : ResponsePlan.WithoutResponses;
        return true;
    }
    public override bool TryOrderGuardResponses(IReadOnlyList<int> owners, out int[] order)
    {
        order = AllowGuardOrder ? owners.OrderBy(owner => owner).ToArray() : null;
        return order != null;
    }
}
public sealed class TurnCheckMonitoring : GuardCardSource
{
    public PatrolCard Card;
    public bool Unavailable;
    public int Draws;
    public readonly List<PatrolCard> History = new();
    public override void ResetSession() { Draws = 0; History.Clear(); }
    public override bool TryDraw(int round, out PatrolCard card, out string reason)
    {
        card = Card; reason = null;
        if (Unavailable) { reason = "CHECK: monitoring source unavailable"; return false; }
        Draws++; History.Add(card); return true;
    }
}

public static class DaidatsugokuPrototypeChecks
{
    public static string LastReport { get; private set; } = "Not run";
    private static readonly Stack<IEnumerator> routines = new();
    private static readonly List<ScriptableObject> fixtures = new();
    private static readonly Dictionary<FieldInfo, object> originalSettings = new();
    private static GameManager game;
    private static GuardCardSource originalMonitoring;
    private static UnityEngine.Random.State originalRandom;
    private static int assertions;

    [MenuItem("Tools/Daidatsugoku/Run Play Mode Checks")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode in the prototype scene first.");
        if (routines.Count > 0) throw new InvalidOperationException("Play mode checks are already running.");
        game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        if (game == null) throw new InvalidOperationException("The active scene has no GameManager.");
        originalSettings.Clear();
        foreach (FieldInfo field in typeof(GameManager).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
            if (field.IsDefined(typeof(SerializeField), true)) originalSettings.Add(field, field.GetValue(game));
        originalMonitoring = game.Guard.MonitoringSource; originalRandom = UnityEngine.Random.state;
        assertions = 0; LastReport = "Running";
        routines.Push(Checks()); EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || game == null) throw new InvalidOperationException("Play mode ended while checks were running.");
            EditorApplication.QueuePlayerLoopUpdate();
            while (routines.Count > 0)
            {
                IEnumerator current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); (current as IDisposable)?.Dispose(); continue; }
                if (current.Current is IEnumerator nested) { routines.Push(nested); continue; }
                return;
            }
            LastReport = $"PASS: {assertions} assertions; DEV-TURN-001, SYS-GUARD-001 v1.2, SYS-VISION-001 v1.0 and SYS-FIELD-001 v1.0 scene, deck, history, vision, field, integration and HUD checks.";
            Debug.Log(LastReport); Cleanup();
        }
        catch (Exception exception)
        {
            LastReport = $"FAIL after {assertions} assertions: {exception.Message}";
            Debug.LogException(exception); Cleanup();
        }
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        while (routines.Count > 0) (routines.Pop() as IDisposable)?.Dispose();
    }
    private static void Restore()
    {
        try
        {
            if (game != null)
            {
                foreach (var setting in originalSettings) setting.Key.SetValue(game, setting.Value);
                game.Guard.MonitoringSource = originalMonitoring; game.RestartGame();
            }
        }
        finally
        {
            foreach (ScriptableObject fixture in fixtures) if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            fixtures.Clear(); originalSettings.Clear(); UnityEngine.Random.state = originalRandom;
        }
    }
    private static T Fixture<T>(string label) where T : ScriptableObject
    {
        T fixture = ScriptableObject.CreateInstance<T>(); fixture.name = "CHECK_" + label;
        fixture.hideFlags = HideFlags.HideAndDontSave; fixtures.Add(fixture); return fixture;
    }
    private static void Require(bool condition, string message)
    {
        assertions++; if (!condition) throw new InvalidOperationException(message);
    }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        bool threw = false; try { action(); } catch (T) { threw = true; } Require(threw, message);
    }
    private static TurnCheckCard Card(string label, Func<GameManager, int, TurnEvent, bool> condition = null, Action<GameManager, int, TurnEvent> effect = null)
    {
        TurnCheckCard card = Fixture<TurnCheckCard>(label); card.Condition = condition; card.Effect = effect; return card;
    }
    private static IEnumerable<SkillCardDefinition> Deck(SkillCardDefinition card, int count = 32) => Enumerable.Repeat(card, count);
    private static Prisoner Piece(int owner, int index = 0) => game.Prisoners.Where(piece => piece.Owner == owner).ElementAt(index);
    private static TurnEvent Root(TurnEvent trigger) { while (trigger?.Parent != null) trigger = trigger.Parent; return trigger; }
    private static void Begin(int players, SkillCardDefinition inert, NormalActionRules rules, TurnFlowPolicy policy, GuardCardSource monitoring = null)
    {
        game.Configure(players, Deck(inert), rules, policy); game.Guard.MonitoringSource = monitoring;
        game.InitializeGame(); game.StartFirstTurn();
    }
    private static void Give(int owner, SkillCardDefinition card)
    {
        game.Cards.Reset(new[] { card }, upper => upper - 1); Require(game.DrawSkillCard(owner), "Fixture card could not be drawn");
    }
    private static int CardIndex(int owner, SkillCardDefinition card)
    {
        for (int index = 0; index < game.Hand(owner).Count; index++) if (game.Hand(owner)[index] == card) return index;
        throw new InvalidOperationException("Fixture card is missing from hand");
    }
    private static void Play(int owner, SkillCardDefinition card) => game.UseCard(owner, CardIndex(owner, card));
    private static T Hud<T>(string fieldName) where T : class
    {
        FieldInfo field = typeof(GameHud).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        T value = field?.GetValue(game.GetComponent<GameHud>()) as T;
        Require(value != null, "HUD field is not connected: " + fieldName); return value;
    }
    private static IEnumerator Until(Func<bool> condition, string message)
    {
        double deadline = EditorApplication.timeSinceStartup + 8;
        while (!condition() && EditorApplication.timeSinceStartup < deadline) yield return null;
        Require(condition(), message + " (phase " + game.Phase + ")");
    }
    private static IEnumerator Checks()
    {
        try
        {
            FieldInfo delay = typeof(GameManager).GetField("guardRevealSeconds", BindingFlags.NonPublic | BindingFlags.Instance);
            Require(delay != null, "Guard reveal delay field missing"); delay.SetValue(game, 0f);
            TurnCheckCard inert = Card("EventOnly", (manager, owner, trigger) => false);
            TurnCheckPolicy policy = Fixture<TurnCheckPolicy>("ExplicitNoResponses");
            TurnCheckMonitoring quiet = Fixture<TurnCheckMonitoring>("NotWatching"); quiet.Card = PatrolCard.NotWatching;
            CheckChain(); CheckPile(inert); CheckInitialization(inert, policy); CheckBoard();
            yield return CheckTurns(inert, policy, quiet);
            yield return CheckPasses(inert, policy);
            CheckCards(inert, policy); CheckPolicyWait(inert, policy); CheckInterruptedAction(inert);
            CheckActivationCancellation(inert); CheckUnresolvedEffects(inert); CheckActionOverflow(inert);
            // The original owner-by-owner CheckMonitoring remains below for historical
            // comparison; current SYS-VISION uses interruptions before discovery.
            assertions += GuardVisionCalculatorChecks.Run();
            yield return GuardVisionIntegrationChecks.Run(game);
            assertions += GuardVisionIntegrationChecks.LastAssertions;
            assertions += GuardDeckChecks.Run();
            assertions += GuardSystemChecks.Run(game);
            assertions += FieldStageChecks.Run();
            assertions += FieldIntegrationChecks.Run(game);
            CheckVictory(inert); yield return null;
        }
        finally { Restore(); }
    }
    private static void CheckChain()
    {
        Throws<ArgumentException>(() => new ResponseChain<string>(1, 1, 1), "Chain accepted identical participants");
        Throws<ArgumentException>(() => new ResponseChain<string>(1, 2, 3), "Chain accepted an external first responder");
        var chain = new ResponseChain<string>(1, 2, 2);
        Require(chain.Responder == 2 && chain.Count == 0 && !chain.Closed, "Externally supplied first responder ignored");
        Require(chain.Contains(1) && chain.Contains(2) && !chain.Contains(3), "Participant membership failed");
        Require(!chain.TryAdd(3, "third") && !chain.TryPass(1), "Chain accepted outsider or out-of-turn pass");
        Throws<InvalidOperationException>(() => chain.ResolutionOrder(), "Chain resolved before closing");
        Require(chain.TryAdd(2, "earlier") && chain.TryPass(1) && chain.ConsecutivePasses == 1, "Response or first pass failed");
        Require(chain.TryAdd(2, "later") && chain.ConsecutivePasses == 0 && chain.Responder == 1, "New response did not reset passes");
        Require(chain.TryPass(1) && chain.TryPass(2) && chain.Closed, "Two passes did not close chain");
        Require(!chain.TryAdd(1, "late") && !chain.TryPass(1), "Closed chain accepted input");
        IReadOnlyList<string> resolution = chain.ResolutionOrder();
        Require(resolution.SequenceEqual(new[] { "later", "earlier" }), "Chain resolution is not LIFO");
        Throws<NotSupportedException>(() => ((IList<string>)resolution)[0] = "changed", "Chain snapshot is mutable");
        Require(chain.ResolutionOrder()[0] == "later", "Resolution snapshot exposed storage");
    }
    private static void CheckPile(SkillCardDefinition inert)
    {
        TurnCheckCard other = Card("OtherUnplayable"); var pile = new SkillCardPile();
        pile.Reset(new[] { inert, other }, upper => upper - 1);
        Require(pile.TryDraw(out SkillCardDefinition held) && held == other && pile.DrawCount == 1, "Pile initial draw failed");
        Require(pile.TryDraw(out SkillCardDefinition used) && used == inert, "Pile second draw failed"); pile.Discard(used);
        Require(pile.DrawCount == 0 && pile.Used.Count == 1, "Discard did not enter used pile");
        Require(pile.TryDraw(out SkillCardDefinition recycled) && recycled == inert && pile.Used.Count == 0, "Used cards did not refill pile");
        Require(!pile.TryDraw(out _) && held == other, "Refill recovered a held card");
    }
    private static void CheckInitialization(SkillCardDefinition inert, TurnCheckPolicy policy)
    {
        Throws<ArgumentException>(() => game.Configure(1, Deck(inert)), "One-player setup accepted");
        Throws<ArgumentException>(() => game.Configure(5, Deck(inert)), "Five-player setup accepted");
        for (int players = 2; players <= 4; players++)
        {
            int previousPieces = game.Prisoners.Count; game.Configure(players, Deck(inert), null, policy);
            Require(game.Prisoners.Count == previousPieces, "Configure changed scene before initialization"); game.InitializeGame();
            Require(game.PlayerCount == players && game.Phase == TurnPhase.Setup && game.Round == 1, "Initialization phase/count/round failed");
            Require(game.TurnOrder.OrderBy(owner => owner).SequenceEqual(Enumerable.Range(0, players)), "Turn order is not a permutation");
            Require(game.Prisoners.Count == players * 3 && game.Prisoners.All(piece => piece.IsInReserve && !piece.IsOnBoard), "Prisoners did not start in reserve");
            for (int owner = 0; owner < players; owner++) Require(game.Hand(owner).Count == 0 && game.EscapedCount(owner) == 0 && game.OnBoardCount(owner) == 0, "Initial hand/escape/on-board count is not zero");
            Require(game.Winner == -1 && game.Grid.FenceCount == 5, "Initial winner/fences failed");
        }
        var orders = new HashSet<string>();
        for (int seed = 0; seed < 12; seed++) { UnityEngine.Random.InitState(seed); game.InitializeGame(); orders.Add(string.Join(",", game.TurnOrder)); }
        Require(orders.Count > 1, "Turn order stayed constant across random seeds");
    }
    private static void CheckBoard()
    {
        int cells = 0;
        for (int row = 0; row < game.Grid.RowCount; row++)
        {
            game.Grid.Field.TryGetRow(row, out FieldRow span);
            Require(game.Grid.GetRowLength(row) == span.EndX - span.StartX + 1, "Stage row span changed"); cells += game.Grid.GetRowLength(row);
            Require(game.Grid.IsExitCell(new Vector2Int(span.EndX, row)) && game.Grid.IsStartCell(new Vector2Int(span.StartX, row)), "Per-row start/exit changed");
        }
        Require(cells == game.Grid.Field.CellCount, "Stage cell count changed");
        foreach (FieldCell cell in game.Grid.Field.InitialFences) Require(game.Grid.HasFence(new Vector2Int(cell.X, cell.Y)), "Stage initial fence missing");
        foreach (FieldCell cell in game.Grid.Field.FenceForbiddenCells) Require(!game.Grid.PlaceFence(new Vector2Int(cell.X, cell.Y)), "Forbidden fence was accepted");
        Require(game.Grid.CanMove(Vector2Int.zero, Vector2Int.right) && game.Grid.CanMove(Vector2Int.zero, Vector2Int.up), "Existing prototype movement directions changed");
    }
    private static IEnumerator CheckTurns(SkillCardDefinition inert, TurnCheckPolicy policy, TurnCheckMonitoring quiet)
    {
        Begin(4, inert, null, policy, quiet); int[] order = game.TurnOrder.ToArray();
        Require(game.CurrentPlayer == order[0] && game.Hand(order[0]).Count == 1 && game.Phase == TurnPhase.PlayerInput, "First turn/draw failed");
        Require(!game.ActionConsumed && !game.ActionDeclared && !game.CanEndTurn, "Action obligation missing");
        Require(!Hud<Button>("endTurn").interactable, "HUD enabled premature end turn"); game.EndTurn();
        Require(game.CurrentPlayer == order[0] && game.Phase == TurnPhase.PlayerInput, "Premature turn end accepted");
        game.SelectPlaceFence(); Require(game.Action == PlayerAction.PlaceFence, "Undefined mandatory deployment added");
        game.SelectPlacePrisoner(); game.OnCellClicked(new Vector2Int(-1, 0));
        Require(!game.ActionConsumed && !game.ActionDeclared, "Invalid action consumed/declaration accepted");
        Piece(order[0]).Deploy(Vector2Int.zero); game.OnCellClicked(Vector2Int.zero);
        Require(!game.ActionConsumed, "Occupied start accepted a second prisoner");
        game.OnCellClicked(new Vector2Int(game.Grid.GetRowStart(1), 1));
        Require(game.OnBoardCount(order[0]) == 2 && game.Prisoners.Count(piece => piece.IsOnBoard && piece.Cell == Vector2Int.zero) == 1, "Start placement/occupancy failed");
        Require(game.ActionConsumed && game.Phase == TurnPhase.PlayerInput && game.CurrentPlayer == order[0], "Action automatically ended turn");
        int onBoard = game.OnBoardCount(order[0]); game.OnCellClicked(Vector2Int.zero);
        Require(game.OnBoardCount(order[0]) == onBoard, "Second action accepted");
        Require(game.CanEndTurn && Hud<Button>("endTurn").interactable, "End turn did not unlock");
        Hud<Button>("endTurn").onClick.Invoke(); Require(game.Phase == TurnPhase.Handoff, "HUD end turn not wired"); game.ConfirmHandoff();
        for (int index = 1; index < order.Length; index++)
        {
            Require(game.CurrentPlayer == order[index] && game.Hand(order[index]).Count == 1, "Fixed order/turn draw failed");
            game.SelectPlacePrisoner(); game.OnCellClicked(new Vector2Int(game.Grid.GetRowStart(index + 1), index + 1));
            Require(game.ActionConsumed && game.Phase == TurnPhase.PlayerInput, "Action consumption failed"); game.EndTurn();
            if (index < order.Length - 1) { Require(game.Phase == TurnPhase.Handoff, "Handoff missing"); game.ConfirmHandoff(); }
        }
        yield return Until(() => game.Phase == TurnPhase.Handoff && game.Round == 2, "Guard did not finish round");
        Require(quiet.Draws == 1 && quiet.History.SequenceEqual(new[] { PatrolCard.NotWatching }), "Monitoring did not draw exactly once after all turns");
        Require(game.Prisoners.Count(piece => piece.IsOnBoard) == 5 && game.Prisoners.All(piece => !piece.Captured), "Not-watching affected prisoners"); game.ConfirmHandoff();
        Require(game.CurrentPlayer == order[0] && game.Hand(order[0]).Count == 2 && game.TurnOrder.SequenceEqual(order), "Next round rerolled order/omitted draw");
        Require(game.GetComponentsInChildren<Prisoner>(true).Length == 12, "Initialization leaked prisoners");
    }
    private static IEnumerator CheckPasses(SkillCardDefinition inert, TurnCheckPolicy policy)
    {
        TurnCheckRules blocked = Fixture<TurnCheckRules>("NoActions");
        TurnCheckCard playable = Card("PlayOrPass", (manager, owner, trigger) => trigger == null);
        game.Configure(2, Deck(playable), blocked, policy); game.InitializeGame(); game.StartFirstTurn();
        Require(game.Phase == TurnPhase.PlayerInput && game.CanEndTurn && !game.ActionConsumed, "Blocked player lost card/pass choice");
        game.EndTurn(); Require(game.Phase == TurnPhase.Handoff, "Voluntary pass failed");
        int effects = 0;
        TurnCheckCard unlock = Card("EnableDeploy", (manager, owner, trigger) => trigger == null, (manager, owner, trigger) => { effects++; blocked.Deploy = true; });
        game.Configure(2, Deck(unlock), blocked, policy); game.InitializeGame(); game.StartFirstTurn(); game.UseCard(game.CurrentPlayer, 0);
        Require(effects == 1 && !game.ActionConsumed && game.Phase == TurnPhase.PlayerInput && !game.CanEndTurn, "Card did not restore action obligation");
        TurnCheckMonitoring unavailable = Fixture<TurnCheckMonitoring>("UnavailableMonitoring"); unavailable.Unavailable = true;
        blocked.Deploy = false; game.Configure(2, Deck(inert), blocked, policy); game.Guard.MonitoringSource = unavailable; game.InitializeGame(); game.StartFirstTurn();
        yield return Until(() => game.Phase == TurnPhase.WaitingForSpecification, "Unplayable cards blocked automatic pass");
        Require(game.Round == 1 && game.Hand(0).Count == 1 && game.Hand(1).Count == 1 && game.Prisoners.All(piece => piece.IsInReserve), "Auto pass skipped draw/performed action");
        Require(!game.CanEndTurn && !Hud<Button>("endTurn").interactable, "Missing monitoring source did not lock flow");
        TurnCheckMonitoring quiet = Fixture<TurnCheckMonitoring>("RetryMonitoring"); quiet.Card = PatrolCard.NotWatching;
        game.Guard.MonitoringSource = quiet; game.RetryPendingSpecification();
        yield return Until(() => game.Round == 2, "Monitoring retry did not resume"); Require(quiet.Draws == 1, "Retry duplicated monitoring draw");
    }
    private static void CheckCards(SkillCardDefinition inert, TurnCheckPolicy policy)
    {
        int immediateEffects = 0;
        TurnCheckCard immediate = Card("Immediate", (manager, owner, trigger) => true, (manager, owner, trigger) => immediateEffects++); immediate.ForceImmediate = true;
        game.Configure(2, new[] { immediate }, null, policy); game.InitializeGame();
        Require(game.Hand(0).Count == 0 && game.Hand(1).Count == 0, "Setup drew initial hand"); game.StartFirstTurn();
        Require(immediateEffects == 1 && game.Hand(game.CurrentPlayer).Count == 0 && game.Cards.Used.Contains(immediate), "Immediate card not forced/discarded");
        Require(game.Phase == TurnPhase.PlayerInput && !game.ActionConsumed, "Immediate card consumed action");
        Begin(2, inert, null, policy); int owner = game.CurrentPlayer;
        for (int index = 0; index < 5; index++) Give(owner, Card("Overflow_" + index));
        Require(game.Hand(owner).Count == 6 && game.Phase == TurnPhase.ChooseDiscard && game.OverflowPlayer == owner, "Hand overflow did not suspend flow");
        Require(!game.CanEndTurn && Hud<GameObject>("handPanel").activeSelf, "Overflow input lock/UI failed");
        game.SelectPlacePrisoner(); game.OnCellClicked(Vector2Int.zero); Require(game.OnBoardCount(owner) == 0, "Overflow allowed action");
        SkillCardDefinition newest = game.Hand(owner)[5];
        Require(Hud<Button>("nextPage").interactable, "Overflow sixth card has no UI page");
        Hud<Button>("nextPage").onClick.Invoke(); Hud<Button[]>("cardButtons")[0].onClick.Invoke();
        Require(game.Hand(owner).Count == 5 && !game.Hand(owner).Contains(newest) && game.Cards.Used.Contains(newest), "Newly drawn card could not be discarded");
        Require(game.Phase == TurnPhase.PlayerInput, "Discard did not resume player turn");
        TurnCheckCard newestAgain = Card("KeepNew"); Give(owner, newestAgain); SkillCardDefinition oldest = game.Hand(owner)[0]; game.DiscardOverflow(owner, 0);
        Require(game.Hand(owner).Count == 5 && game.Hand(owner).Contains(newestAgain) && !game.Hand(owner).Contains(oldest), "Older card could not be discarded");
        Begin(2, inert, null, policy); int freeEffects = 0;
        TurnCheckCard free = Card("RepeatFree", (manager, player, trigger) => trigger == null, (manager, player, trigger) => freeEffects++);
        for (int index = 0; index < 7; index++) { Give(game.CurrentPlayer, free); Play(game.CurrentPlayer, free); }
        Require(freeEffects == 7 && !game.ActionConsumed && game.Phase == TurnPhase.PlayerInput, "Common card cap/action cost introduced");
        game.Configure(2, Array.Empty<SkillCardDefinition>(), null, policy); game.InitializeGame(); game.StartFirstTurn();
        Require(game.Phase == TurnPhase.WaitingForSpecification && game.Hand(game.CurrentPlayer).Count == 0 && game.Round == 1, "Both piles empty silently skipped draw");
        int waitingOwner = game.CurrentPlayer; game.Cards.Discard(inert); game.RetryPendingSpecification();
        Require(game.Phase == TurnPhase.PlayerInput && game.CurrentPlayer == waitingOwner && game.Hand(waitingOwner).Count == 1, "Pending empty-deck draw retry failed");
    }
    private static void CheckPolicyWait(SkillCardDefinition inert, TurnCheckPolicy policy)
    {
        int effects = 0; Begin(2, inert, null, null);
        TurnCheckCard free = Card("NeedsPolicy", (manager, owner, trigger) => trigger == null, (manager, owner, trigger) => effects++);
        int actor = game.CurrentPlayer; Give(actor, free); Play(actor, free);
        Require(game.Phase == TurnPhase.WaitingForSpecification && effects == 0 && !game.CanEndTurn, "Undefined response policy was guessed");
        game.FlowPolicy = policy; game.RetryPendingSpecification();
        Require(effects == 1 && game.Phase == TurnPhase.PlayerInput && !game.ActionConsumed, "Policy did not resume card once");
        game.RetryPendingSpecification(); Require(effects == 1, "Retry replayed completed card");
    }
    private static void CheckInterruptedAction(SkillCardDefinition inert)
    {
        TurnCheckPolicy policy = Fixture<TurnCheckPolicy>("TwoParticipants"); Begin(3, inert, null, policy);
        int actor = game.CurrentPlayer, opponent = (actor + 1) % 3, outsider = (actor + 2) % 3;
        policy.PlanChains = true; policy.A = actor; policy.B = opponent; policy.First = opponent; var log = new List<string>();
        TurnCheckCard cancel = Card("CancelAction", (manager, owner, trigger) => Root(trigger)?.Kind == TurnEventKind.NormalAction, (manager, owner, trigger) => { log.Add("cancel"); Root(trigger).Cancelled = true; }); cancel.Target = actor;
        TurnCheckCard reply = Card("Reply", (manager, owner, trigger) => Root(trigger)?.Kind == TurnEventKind.NormalAction, (manager, owner, trigger) => log.Add("reply")); reply.Target = opponent;
        Give(opponent, cancel); Give(actor, reply); game.SelectPlacePrisoner(); game.OnCellClicked(Vector2Int.zero);
        Require(game.Phase == TurnPhase.Response && game.Responder == opponent && game.ActionDeclared && !game.ActionConsumed, "Action did not open externally ordered responses");
        Require(game.OnBoardCount(actor) == 0 && game.InputPlayer != outsider && !game.CanEndTurn, "Unresolved action changed board/admitted third player");
        Require(Hud<Button>("responsePass").interactable && Hud<GameObject>("handPanel").activeSelf, "Chain HUD unavailable");
        Play(opponent, cancel); Require(game.Responder == actor, "Response did not alternate"); Play(actor, reply); Require(game.Responder == opponent, "Reply did not alternate");
        game.PassResponse(); Require(game.Phase == TurnPhase.Response, "One pass closed chain"); game.PassResponse();
        Require(log.SequenceEqual(new[] { "reply", "cancel" }), "Integrated responses not LIFO");
        Require(game.ActionConsumed && game.Phase == TurnPhase.PlayerInput && game.OnBoardCount(actor) == 0, "Cancelled action was not consumed");
        game.SelectPlacePrisoner(); game.OnCellClicked(Vector2Int.zero); Require(game.OnBoardCount(actor) == 0 && game.CanEndTurn, "Cancelled action allowed retry");
        int freeEffects = 0; TurnCheckCard free = Card("AfterAction", (manager, owner, trigger) => trigger == null, (manager, owner, trigger) => freeEffects++);
        policy.PlanChains = false; Give(actor, free); Play(actor, free);
        Require(freeEffects == 1 && game.ActionConsumed && game.Phase == TurnPhase.PlayerInput, "Card after normal action failed");
    }
    private static void CheckActivationCancellation(SkillCardDefinition inert)
    {
        TurnCheckPolicy policy = Fixture<TurnCheckPolicy>("ActivationCancellation"); Begin(2, inert, null, policy);
        int actor = game.CurrentPlayer, opponent = 1 - actor, initialEffects = 0;
        TurnCheckCard initial = Card("InitialSkill", (manager, owner, trigger) => trigger == null, (manager, owner, trigger) => initialEffects++); initial.Target = opponent;
        TurnCheckCard cancel = Card("CancelInitialSkill", (manager, owner, trigger) => trigger?.Kind == TurnEventKind.SkillActivation,
            (manager, owner, trigger) => trigger.Parent.Cancelled = true); cancel.Target = actor;
        Give(actor, initial); Give(opponent, cancel);
        policy.PlanChains = true; policy.A = actor; policy.B = opponent; policy.First = opponent;
        Play(actor, initial); Play(opponent, cancel); game.PassResponse(); game.PassResponse();
        Require(initialEffects == 0 && game.Cards.Used.Contains(initial) && game.Cards.Used.Contains(cancel), "Cancelled initial skill still resolved or was not discarded");
        Require(game.Phase == TurnPhase.PlayerInput && !game.ActionConsumed, "Cancelled skill changed normal action state");

        Begin(2, inert, null, policy); actor = game.CurrentPlayer; opponent = 1 - actor;
        int cancelEffects = 0, counterEffects = 0;
        cancel = Card("ActionInterference", (manager, owner, trigger) => Root(trigger)?.Kind == TurnEventKind.NormalAction,
            (manager, owner, trigger) => { cancelEffects++; trigger.Parent.Cancelled = true; }); cancel.Target = actor;
        TurnCheckCard counter = Card("CancelInterference", (manager, owner, trigger) => Root(trigger)?.Kind == TurnEventKind.NormalAction,
            (manager, owner, trigger) => { counterEffects++; trigger.Parent.Cancelled = true; }); counter.Target = opponent;
        Give(opponent, cancel); Give(actor, counter); policy.A = actor; policy.B = opponent; policy.First = opponent;
        game.SelectPlacePrisoner(); game.OnCellClicked(Vector2Int.zero); Play(opponent, cancel); Play(actor, counter); game.PassResponse(); game.PassResponse();
        Require(counterEffects == 1 && cancelEffects == 0, "Counter did not cancel the interference activation");
        Require(game.OnBoardCount(actor) == 1 && game.ActionConsumed && game.Phase == TurnPhase.PlayerInput, "Cancelled interference still prevented original action");
    }
    private static void CheckUnresolvedEffects(SkillCardDefinition inert)
    {
        for (int mode = 0; mode < 2; mode++)
        {
            TurnCheckPolicy policy = Fixture<TurnCheckPolicy>("UnresolvedEffect_" + mode); Begin(2, inert, null, policy);
            int actor = game.CurrentPlayer, opponent = 1 - actor;
            int initialEffects = 0, olderEffects = 0, drawEffects = 0, nestedEffects = 0;
            TurnCheckCard initial = Card("PendingInitial", (manager, owner, trigger) => trigger == null, (manager, owner, trigger) => initialEffects++); initial.Target = opponent;
            TurnCheckCard older = Card("PendingEarlierResponse", (manager, owner, trigger) => trigger != null, (manager, owner, trigger) => olderEffects++); older.Target = actor;
            TurnCheckCard drawing = Card("DrawDuringResolution", (manager, owner, trigger) => trigger != null,
                (manager, owner, trigger) => { drawEffects++; manager.DrawSkillCard(owner); }); drawing.Target = opponent;
            Give(actor, initial); Give(opponent, older); Give(actor, drawing);
            TurnCheckCard nested = Card("NestedImmediate", (manager, owner, trigger) => true, (manager, owner, trigger) => nestedEffects++); nested.ForceImmediate = true;
            game.Cards.Reset(mode == 0 ? Array.Empty<SkillCardDefinition>() : new SkillCardDefinition[] { nested }, upper => upper - 1);
            policy.PlanChains = true; policy.A = actor; policy.B = opponent; policy.First = opponent;
            Play(actor, initial); Play(opponent, older); Play(actor, drawing); game.PassResponse(); game.PassResponse();
            Require(drawEffects == 1 && game.Phase == TurnPhase.WaitingForSpecification && initialEffects == 0 && olderEffects == 0 && nestedEffects == 0,
                mode == 0 ? "Empty draw inside an effect allowed remaining responses to resolve" : "Nested immediate priority was guessed and remaining responses resolved");
            Require(!game.CanEndTurn && !game.ActionConsumed && game.Cards.Used.Contains(drawing) && !game.Cards.Used.Contains(older), "Unresolved effect did not preserve the unfinished chain");
            game.RetryPendingSpecification();
            Require(game.Phase == TurnPhase.WaitingForSpecification && drawEffects == 1 && initialEffects == 0 && olderEffects == 0, "Retry replayed an unresolved effect without a defined continuation");
            game.RestartGame();
            Require(game.Phase == TurnPhase.PlayerInput && game.Prisoners.All(piece => piece.IsInReserve), "Restart did not remove unresolved effect state");
        }
    }
    private static void CheckActionOverflow(SkillCardDefinition inert)
    {
        for (int restart = 0; restart < 2; restart++)
        {
            TurnCheckPolicy policy = Fixture<TurnCheckPolicy>("ActionOverflow_" + restart); Begin(2, inert, null, policy);
            int actor = game.CurrentPlayer, opponent = 1 - actor, effects = 0;
            for (int index = 0; index < 4; index++) Give(actor, inert);
            TurnCheckCard drawing = Card("OverflowBeforeAction", (manager, owner, trigger) => Root(trigger)?.Kind == TurnEventKind.NormalAction,
                (manager, owner, trigger) => { effects++; manager.DrawSkillCard(actor); manager.DrawSkillCard(actor); }); drawing.Target = actor;
            Give(opponent, drawing); game.Cards.Reset(Deck(inert, 2), upper => upper - 1);
            policy.PlanChains = true; policy.A = actor; policy.B = opponent; policy.First = opponent;
            game.SelectPlacePrisoner(); game.OnCellClicked(Vector2Int.zero); Play(opponent, drawing); game.PassResponse(); game.PassResponse();
            Require(effects == 1 && game.Hand(actor).Count == 7 && game.Phase == TurnPhase.ChooseDiscard && game.OnBoardCount(actor) == 0 && !game.ActionConsumed, "Overflow did not suspend original action completion");
            if (restart == 0)
            {
                game.DiscardOverflow(actor, 0); Require(game.Phase == TurnPhase.ChooseDiscard && game.Hand(actor).Count == 6 && game.OnBoardCount(actor) == 0, "Original action resumed before all excess cards were discarded");
                game.DiscardOverflow(actor, 0);
                Require(game.Phase == TurnPhase.PlayerInput && game.Hand(actor).Count == 5 && game.ActionConsumed && game.OnBoardCount(actor) == 1 && effects == 1, "Original action did not resume exactly once after overflow");
                game.DiscardOverflow(actor, 0); game.OnCellClicked(Vector2Int.zero);
                Require(game.OnBoardCount(actor) == 1 && effects == 1, "Completed overflow action could be replayed");
            }
            else
            {
                game.RestartGame(); game.DiscardOverflow(actor, 0); game.RetryPendingSpecification();
                Require(game.Phase == TurnPhase.PlayerInput && !game.ActionConsumed && game.Prisoners.All(piece => piece.IsInReserve) && effects == 1, "Restart retained the pending action completion");
            }
        }
    }
    private static IEnumerator CheckMonitoring(SkillCardDefinition inert)
    {
        TurnCheckPolicy policy = Fixture<TurnCheckPolicy>("GuardOrder"); policy.AllowGuardOrder = false;
        TurnCheckMonitoring watch = Fixture<TurnCheckMonitoring>("Watching"); watch.Card = PatrolCard.WatchAllCells;
        game.Configure(2, Deck(inert), null, policy); game.Guard.MonitoringSource = watch; game.InitializeGame(); int rescues = 0;
        TurnCheckCard rescue = Card("Rescue", (manager, owner, trigger) => Root(trigger)?.Kind == TurnEventKind.GuardCapture, (manager, owner, trigger) =>
        {
            TurnEvent capture = Root(trigger); Require(capture.Kind == TurnEventKind.GuardCapture && capture.CaptureTargets.All(piece => piece.Owner == owner), "Guard response mixed target owners"); capture.Cancelled = true; rescues++;
        });
        Give(0, rescue); game.Cards.Reset(Deck(inert), upper => upper - 1); game.StartFirstTurn();
        for (int turn = 0; turn < 2; turn++) { game.SelectPlacePrisoner(); game.OnCellClicked(Vector2Int.zero); game.EndTurn(); if (turn == 0) game.ConfirmHandoff(); }
        yield return Until(() => game.Phase == TurnPhase.WaitingForSpecification, "Multiple guard owners did not wait for response order");
        Require(watch.Draws == 1 && watch.History.Single() == PatrolCard.WatchAllCells && Piece(0).IsOnBoard && Piece(1).IsOnBoard, "Monitoring redrawn/captured before ordering");
        typeof(GameManager).GetMethod("BeginGuardTurn", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, null);
        Require(watch.Draws == 1 && game.Guard.PublicHistory.Count == 1, "Reentering a guard turn repeated draw/history publication");
        game.Grid.PlaceFence(Vector2Int.zero); policy.AllowGuardOrder = true; game.RetryPendingSpecification();
        yield return Until(() => game.Phase == TurnPhase.GuardResponse || game.Phase == TurnPhase.Response, "Explicit guard order did not open rescue window");
        Require(game.InputPlayer == 0 && rescues == 0 && Piece(0).IsOnBoard, "Rescue window offered at wrong time"); Play(0, rescue);
        double deadline = EditorApplication.timeSinceStartup + 8;
        while (game.Round == 1 && EditorApplication.timeSinceStartup < deadline) { if (game.Phase == TurnPhase.GuardResponse || game.Phase == TurnPhase.Response) game.PassResponse(); yield return null; }
        Require(game.Round == 2 && rescues == 1 && !Piece(0).Captured && Piece(1).Captured, "Per-owner rescue/capture outcomes not separated");
        Require(watch.Draws == 1, "Guard retry/rescue redrew monitoring card");
    }
    private static void CheckVictory(SkillCardDefinition inert)
    {
        TurnCheckPolicy policy = Fixture<TurnCheckPolicy>("EscapeResponse"); Begin(2, inert, null, policy);
        int owner = game.CurrentPlayer, opponent = 1 - owner;
        for (int index = 0; index < 2; index++) { Prisoner piece = Piece(owner, index); piece.Deploy(new Vector2Int(game.Grid.GetRowEnd(0), 0)); game.ConfirmEscape(piece); }
        Require(game.EscapedCount(owner) == 2 && game.Winner == -1, "Confirmed escape count before victory failed"); game.ConfirmEscape(Piece(owner)); Require(game.EscapedCount(owner) == 2, "Duplicate escape counted twice");
        Prisoner last = Piece(owner, 2); last.Deploy(new Vector2Int(game.Grid.GetRowEnd(0), 0));
        TurnCheckCard response = Card("BeforeEscape", (manager, actor, trigger) => Root(trigger)?.Kind == TurnEventKind.NormalAction); response.Target = owner; Give(opponent, response);
        policy.PlanChains = true; policy.A = owner; policy.B = opponent; policy.First = opponent;
        game.SelectMove(); game.OnPrisonerClicked(last); game.MoveRightOrEscape();
        Require(game.Phase == TurnPhase.Response && game.Winner == -1 && game.EscapedCount(owner) == 2 && last.IsOnBoard, "Victory committed before chain close"); game.PassResponse(); game.PassResponse();
        Require(game.Phase == TurnPhase.Finished && game.Winner == owner && game.EscapedCount(owner) == 3 && last.Escaped, "Third escape did not immediately finish game");
        Require(Hud<GameObject>("result").activeSelf && game.ResultMessage.Contains("P" + (owner + 1)), "Winner HUD not connected");
        int round = game.Round, actorBefore = game.CurrentPlayer, beforeBoard = game.OnBoardCount(opponent);
        game.EndTurn(); game.ConfirmHandoff(); game.PassResponse(); game.SelectPlacePrisoner(); game.OnCellClicked(Vector2Int.zero);
        Require(game.Phase == TurnPhase.Finished && game.Round == round && game.CurrentPlayer == actorBefore && game.OnBoardCount(opponent) == beforeBoard, "Finished game accepted progression/input");
        game.RestartGame(); Require(game.Winner == -1 && game.Round == 1 && game.EscapedCount(owner) == 0 && game.Prisoners.All(piece => piece.IsInReserve), "Restart did not clear result");
    }
}
