using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Daidatsugoku;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Exercises the real scene, pointer handlers, HUD and guard coroutine. No production card rules are invented.
public static class DaidatsugokuPrototypeChecks
{
    public static string LastReport { get; private set; } = "Not run";
    private static IEnumerator routine;
    private static GameManager game;
    private static int assertions;
    private static int originalPlayers;
    private static UnityEngine.Random.State originalRandom;
    private static float originalDelay;
    private static FlowRules originalRules;
    private static List<SkillCard> originalCards;
    private static List<PatrolCard> originalGuardDeck;
    private static readonly List<ScriptableObject> fixtures = new();
    private static FieldInfo Field(string name) => typeof(GameManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static FieldInfo GuardDeck => typeof(GuardSystem).GetField("initialDeck", BindingFlags.Instance | BindingFlags.NonPublic);

    [MenuItem("Tools/Daidatsugoku/Run Play Mode Checks")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        if (routine != null) throw new InvalidOperationException("Checks are already running.");
        game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        if (game == null) throw new InvalidOperationException("Prototype GameManager not found.");
        originalPlayers = (int)Field("playerCount").GetValue(game);
        originalRandom = UnityEngine.Random.state; UnityEngine.Random.InitState(7331);
        originalDelay = (float)Field("guardRevealSeconds").GetValue(game);
        originalRules = game.Rules; originalCards = (List<SkillCard>)Field("skillCards").GetValue(game);
        originalGuardDeck = (List<PatrolCard>)GuardDeck.GetValue(game.Guard);
        Field("guardRevealSeconds").SetValue(game, 0f);
        assertions = 0; LastReport = "Running"; routine = Checks(); EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || game == null) throw new InvalidOperationException("Play mode ended during checks.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (routine.MoveNext()) return;
            LastReport = $"PASS: {assertions} assertions for SYS-TURN-001 and board/HUD regression.";
            Debug.Log(LastReport); Cleanup();
        }
        catch (Exception exception) { LastReport = "FAIL: " + exception.Message; Debug.LogException(exception); Cleanup(); }
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick; routine = null;
        if (game != null)
        {
            Field("playerCount").SetValue(game, originalPlayers); Field("guardRevealSeconds").SetValue(game, originalDelay);
            Field("rules").SetValue(game, originalRules); Field("skillCards").SetValue(game, originalCards);
            GuardDeck.SetValue(game.Guard, originalGuardDeck); game.RestartGame();
        }
        foreach (var fixture in fixtures) UnityEngine.Object.DestroyImmediate(fixture);
        fixtures.Clear();
        UnityEngine.Random.state = originalRandom;
    }
    private static void Require(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    private static T Fixture<T>() where T : ScriptableObject
    {
        T fixture = ScriptableObject.CreateInstance<T>(); fixtures.Add(fixture); return fixture;
    }
    private static void Restart(int players = 3, FlowRules rules = null)
    {
        Field("playerCount").SetValue(game, players); Field("rules").SetValue(game, rules);
        Field("skillCards").SetValue(game, new List<SkillCard>());
        GuardDeck.SetValue(game.Guard, new List<PatrolCard> { PatrolCard.WatchAllCells });
        game.RestartGame();
    }
    private static FlowCheckCard Card(string name, Func<GameManager, int, FlowContext, bool> condition = null,
        System.Action<GameManager, int, FlowContext> effect = null, int target = -1, bool immediate = false)
    {
        var card = Fixture<FlowCheckCard>(); card.name = name; card.Condition = condition; card.Effect = effect;
        card.Target = target; card.IsImmediate = immediate; return card;
    }
    private static void Give(int owner, params SkillCard[] cards)
    {
        foreach (SkillCard card in cards) { game.Cards.Reset(new[] { card }); game.DrawSkillCard(owner); }
    }
    private static void FenceAction()
    {
        Vector2Int spare = new(4, 3);
        if (game.Grid.HasFence(spare)) game.SelectRemoveFence(); else game.SelectPlaceFence();
        game.OnCellClicked(spare);
    }
    private static void ClickButton(string objectName)
    {
        var button = game.GetComponentsInChildren<Button>(true).First(b => b.name == objectName);
        Require(button.interactable, "HUD button disabled: " + objectName); button.onClick.Invoke();
    }
    private static IEnumerator Checks()
    {
        var seen = new HashSet<int>();
        for (int players = 2; players <= 4; players++)
        {
            Restart(players); int[] order = game.TurnOrder.ToArray();
            Require(order.Length == players && order.Distinct().Count() == players, "Turn permutation invalid");
            Require(game.CurrentPlayer == order[0] && game.Round == 1, "First turn incorrect");
            Require(game.Prisoners.Count == players * 3 && game.Prisoners.All(p => p.IsInReserve && !p.IsOnBoard), "Initial prisoners incorrect");
            Require(order.All(p => game.Hand(p).Count == 0 && game.EscapedCount(p) == 0), "Initial hands/scores incorrect");
            Require(game.Grid.FenceCount == 5, "Initial fences lost");
            for (int index = 0; index < players; index++)
            {
                Require(game.CurrentPlayer == order[index], "Fixed player order violated");
                game.EndTurn(); Require(game.IsPlayerTurn && !game.ActionConsumed, "Skipped mandatory action");
                FenceAction(); Require(game.IsPlayerTurn && game.ActionConsumed, "Action auto-ended turn");
                int fences = game.Grid.FenceCount;
                game.SelectPlaceFence(); game.OnCellClicked(new Vector2Int(2, 0));
                Require(game.Grid.FenceCount == fences, "Second action accepted");
                game.EndTurn();
                if (index < players - 1)
                {
                    Require(game.State == GameState.Handoff, "Handoff missing");
                    game.OnCellClicked(Vector2Int.zero); Require(game.Grid.FenceCount == fences, "Handoff accepted input");
                    game.ConfirmHandoff();
                }
            }
            double deadline = EditorApplication.timeSinceStartup + 5;
            while (game.State == GameState.GuardTurn && EditorApplication.timeSinceStartup < deadline) yield return null;
            Require(game.State == GameState.Handoff && game.Round == 2 && game.PendingPlayer == order[0], "Round/guard sequence incorrect");
            Require(game.TurnOrder.SequenceEqual(order), "Order changed between rounds");
            game.ConfirmHandoff(); Require(game.CurrentPlayer == order[0], "Round did not start at head");
            seen.Add(order[0]);
        }
        for (int i = 0; i < 12; i++) { Restart(); seen.Add(game.TurnOrder[0]); }
        Require(seen.Count > 1, "Turn order appears deterministic");

        Restart(); int owner = game.CurrentPlayer;
        Require(game.CanPerform(PlayerAction.PlaceFence) && game.CanPerform(PlayerAction.RemoveFence), "Forced placement rule survived");
        game.SelectPlacePrisoner(); game.OnCellClicked(new Vector2Int(1, 0));
        Require(!game.ActionConsumed && game.OnBoardCount(owner) == 0, "Non-start consumed action");
        Prisoner own = game.Prisoners.First(p => p.Owner == owner);
        Prisoner other = game.Prisoners.First(p => p.Owner != owner);
        other.Deploy(Vector2Int.zero); game.OnCellClicked(Vector2Int.zero);
        Require(!game.ActionConsumed, "Occupied start accepted");
        game.OnCellClicked(new Vector2Int(0, 1));
        Require(game.ActionConsumed && game.OnBoardCount(owner) == 1, "Empty row start rejected");
        Restart(); owner = game.CurrentPlayer; own = game.Prisoners.First(p => p.Owner == owner);
        other = game.Prisoners.First(p => p.Owner != owner); own.Deploy(Vector2Int.zero); other.Deploy(new Vector2Int(1, 0));
        game.SelectMove(); ExecuteEvents.Execute(own.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
        game.OnCellClicked(new Vector2Int(1, 0)); Require(!game.ActionConsumed && own.Cell == Vector2Int.zero, "Occupied move accepted");
        game.OnCellClicked(new Vector2Int(0, 1)); Require(!game.ActionConsumed, "Diagonal move accepted");
        game.OnCellClicked(new Vector2Int(1, 1)); Require(game.ActionConsumed && own.Cell == new Vector2Int(1, 1), "Offset adjacent move rejected");
        Require(game.Grid.BoundaryOf(new Vector2Int(0, 1), CellEdge.South).Equals(game.Grid.BoundaryOf(new Vector2Int(0, 2), CellEdge.North)), "Shared boundary broken");
        Require(game.Grid.IsProtected(new Vector2Int(1, 4)) && !game.Grid.IsProtected(Vector2Int.zero), "Guard sight regression");

        // Draw at turn start, immediate resolution, overflow choice, conservation on refill.
        Restart(); var normal = Card("normal", (g, p, c) => c.Event == FlowEvent.FreePlay);
        Field("skillCards").SetValue(game, new List<SkillCard> { normal }); game.RestartGame(); owner = game.CurrentPlayer;
        Require(game.Hand(owner).Count == 1 && game.TurnOrder.Where(p => p != owner).All(p => game.Hand(p).Count == 0), "Start draw/zero initial hands incorrect");
        int resolved = 0;
        var instant = Card("instant", effect: (g, p, c) => resolved++, immediate: true);
        Give(owner, instant); Require(resolved == 1 && game.Hand(owner).Count == 1 && game.Cards.Discard.Contains(instant), "Immediate card not forced/discarded");
        for (int i = 0; i < 5; i++) Give(owner, normal);
        Require(game.Hand(owner).Count == 6 && game.OverflowPlayer == owner && !game.CanOperate && !game.CanEndTurn, "Overflow did not block input");
        var kept = game.Hand(owner)[0];
        Require(game.DiscardOverflow(owner, 3) && game.Hand(owner).Count == 5 && game.Cards.Discard.Count == 1, "Chosen discard failed");
        Require(game.Hand(owner)[0] == kept, "Discard chose wrong index");
        SkillCard recycled = game.Cards.Draw(); Require(recycled == normal && game.Hand(owner).Count == 5 && game.Cards.Draw() == null, "Refill stole hands or fabricated cards");
        Require(game.UseCard(owner, 0) && !game.ActionConsumed, "Card consumed normal action");
        FenceAction(); Require(game.UseCard(owner, 0) && game.ActionConsumed && game.IsPlayerTurn, "Post-action card unavailable");

        // Other-turn cards can initiate outside a response window without changing the active turn.
        Restart(); owner = game.CurrentPlayer; int visitor = (owner + 1) % game.PlayerCount;
        var ownOnly = Card("own-only", (g, p, c) => c.Event == FlowEvent.FreePlay && p == g.CurrentPlayer);
        var otherOnly = Card("other-only", (g, p, c) => c.Event == FlowEvent.FreePlay && p != g.CurrentPlayer);
        Give(visitor, ownOnly, otherOnly);
        Require(!game.CanUseCard(visitor, 0) && game.CanUseCard(visitor, 1), "Other-turn conditions not honored");
        // The existing score text cycles whose hand is displayed; no extra panel is created.
        var scoreButton = game.GetComponentsInChildren<Button>(true).Single(button => button.name == "Text" && button.GetComponent<RectTransform>().anchoredPosition.y == -145);
        scoreButton.onClick.Invoke();
        Require(game.GetComponentsInChildren<Button>(true).Single(button => button.name == "移動").interactable, "Other-player hand not reachable through HUD");
        ClickButton("移動");
        Require(game.Hand(visitor).Count == 1 && game.CurrentPlayer == owner && !game.ActionConsumed && game.IsPlayerTurn,
            "Other-turn card changed active turn/action or did not play");

        // Conditions, automatic and voluntary pass; card effect re-enables mandatory action.
        var rules = Fixture<FlowCheckRules>(); rules.Block = true;
        Restart(3, rules); Require(game.State == GameState.Handoff, "No actions/cards did not auto-pass");
        var rescueOnly = Card("rescue-only", (g, p, c) => c.Event == FlowEvent.GuardCapture);
        Field("skillCards").SetValue(game, new List<SkillCard> { rescueOnly }); game.RestartGame();
        Require(game.State == GameState.Handoff, "Unplayable card prevented auto-pass");
        var unlock = Card("unlock", (g, p, c) => c.Event == FlowEvent.FreePlay, (g, p, c) => rules.Block = false);
        Field("skillCards").SetValue(game, new List<SkillCard> { unlock }); game.RestartGame(); owner = game.CurrentPlayer;
        Require(game.IsPlayerTurn && game.CanEndTurn && game.HasPlayableCard(owner), "Playable card did not allow voluntary pass");
        game.UseCard(owner, 0); Require(!game.CanEndTurn && game.HasNormalAction(), "Mandatory action not restored after card");
        rules.Block = true; Field("skillCards").SetValue(game, new List<SkillCard> { unlock }); game.RestartGame();
        game.EndTurn(); Require(game.State == GameState.Handoff, "Voluntary pass rejected");

        // Three players own eligible cards, but only the selected pair can chain. Two passes, reset and LIFO.
        Restart(); int a = game.CurrentPlayer, b = game.TurnOrder[1], third = game.TurnOrder[2];
        var orderLog = new List<string>();
        var first = Card("1", (g, p, c) => c.Event == FlowEvent.FreePlay, (g, p, c) => orderLog.Add("1"), b);
        var response = Card("2", (g, p, c) => c.Event == FlowEvent.SkillCard, (g, p, c) => orderLog.Add("2"));
        var counter = Card("3", (g, p, c) => c.Event == FlowEvent.SkillCard, (g, p, c) => orderLog.Add("3"));
        Give(a, first, counter); Give(b, response); Give(third, response);
        Require(game.UseCard(a, 0) && game.Responder == b, "Target did not get first response");
        Require(!game.UseCard(third, 0), "Third party entered chain");
        game.PassChain(b); Require(game.IsChainOpen && game.ConsecutivePasses == 1 && game.Responder == a, "One pass closed chain");
        Require(game.UseCard(a, 0) && game.ConsecutivePasses == 0, "New card did not reset passes");
        game.UseCard(b, 0); game.PassChain(a); game.PassChain(b);
        Require(!game.IsChainOpen && string.Join(",", orderLog) == "2,3,1" && game.Cards.Discard.Count == 3, "LIFO/discard failed");

        // Invalidated normal action is consumed and cannot be replaced.
        Restart(); a = game.CurrentPlayer; b = game.TurnOrder[1];
        var cancel = Card("cancel", (g, p, c) => c.Event == FlowEvent.NormalAction, (g, p, c) => c.Cancelled = true);
        Give(b, cancel); int fenceCount = game.Grid.FenceCount;
        game.SelectRemoveFence(); game.OnCellClicked(new Vector2Int(0, 1));
        Require(game.IsChainOpen && game.ActionConsumed && game.Grid.FenceCount == fenceCount, "Action applied before response");
        game.UseCard(b, 0); game.PassChain(a); game.PassChain(b);
        Require(game.ActionConsumed && game.CanEndTurn && game.Grid.FenceCount == fenceCount, "Cancelled action not consumed");
        Give(a, normal); Require(game.UseCard(a, 0), "Cancelled action blocked later card");
        game.SelectPlaceFence(); game.OnCellClicked(new Vector2Int(1, 0)); Require(game.Grid.FenceCount == fenceCount, "Replaced cancelled action");

        // A response to a normal action becomes a card event, allowing a counter in the same pair.
        Restart(); a = game.CurrentPlayer; b = game.TurnOrder[1];
        var allow = Card("allow", (g, p, c) => c.Event == FlowEvent.SkillCard && c.OriginalEvent == FlowEvent.NormalAction,
            (g, p, c) => c.Cancelled = false);
        Give(a, allow); Give(b, cancel); fenceCount = game.Grid.FenceCount;
        game.SelectRemoveFence(); game.OnCellClicked(new Vector2Int(0, 1)); game.UseCard(b, 0);
        Require(game.CanUseCard(a, 0), "Counter did not see skill activation during normal-action chain");
        game.UseCard(a, 0); game.PassChain(b); game.PassChain(a);
        // LIFO: allow resolves first, cancel resolves last, so the root action is still cancelled.
        Require(game.ActionConsumed && game.Grid.FenceCount == fenceCount, "Normal-action chain resolved out of order");

        // Dedicated rule hooks reject prohibited/protected targets without consuming the action.
        var protectedRules = Fixture<FlowCheckRules>(); protectedRules.ProtectFences = true;
        Restart(3, protectedRules);
        Require(!game.CanPerform(PlayerAction.RemoveFence), "Protected fences counted as removable");
        game.SelectRemoveFence(); game.OnBoundaryClicked(game.Grid.BoundaryOf(new Vector2Int(0, 1), CellEdge.North));
        Require(!game.ActionConsumed, "Protected fence consumed action");

        // Guard result is revealed before owner rescue, with independent player windows.
        Restart(); a = game.TurnOrder[0]; b = game.TurnOrder[1];
        own = game.Prisoners.First(p => p.Owner == a); other = game.Prisoners.First(p => p.Owner == b);
        own.Deploy(Vector2Int.zero); other.Deploy(new Vector2Int(2, 0));
        var save = Card("save", (g, p, c) => c.Event == FlowEvent.GuardCapture && c.Target == p, (g, p, c) => c.Cancelled = true);
        Give(a, save); Give(b, save);
        Require(game.Guard.Targets(PatrolCard.NotWatching, game.Grid, game.Prisoners, a).Count == 0, "Not-watching targeted prisoners");
        for (int i = 0; i < 3; i++) { FenceAction(); game.EndTurn(); if (i < 2) game.ConfirmHandoff(); }
        double guardDeadline = EditorApplication.timeSinceStartup + 5;
        while (!game.IsChainOpen && EditorApplication.timeSinceStartup < guardDeadline) yield return null;
        Require(game.State == GameState.GuardTurn && game.Responder == a && own.IsOnBoard, "Guard captured before owner response");
        game.UseCard(a, 0); game.PassChain(a);
        guardDeadline = EditorApplication.timeSinceStartup + 5;
        while (!game.IsChainOpen && game.State == GameState.GuardTurn && EditorApplication.timeSinceStartup < guardDeadline) yield return null;
        Require(own.IsOnBoard && game.Responder == b, "Guard owners not separated");
        game.PassChain(b);
        guardDeadline = EditorApplication.timeSinceStartup + 5;
        while (game.State == GameState.GuardTurn && EditorApplication.timeSinceStartup < guardDeadline) yield return null;
        Require(other.Captured && own.IsOnBoard && game.Round == 2, "Guard finalization/next round incorrect");

        Restart();
        GuardDeck.SetValue(game.Guard, new List<PatrolCard> { PatrolCard.NotWatching }); game.Guard.ResetDeck();
        own = game.Prisoners.First(p => p.Owner == game.CurrentPlayer); own.Deploy(Vector2Int.zero);
        for (int i = 0; i < 3; i++) { FenceAction(); game.EndTurn(); if (i < 2) game.ConfirmHandoff(); }
        guardDeadline = EditorApplication.timeSinceStartup + 5;
        while (game.State == GameState.GuardTurn && EditorApplication.timeSinceStartup < guardDeadline) yield return null;
        Require(own.IsOnBoard && game.Round == 2 && !game.IsChainOpen, "Not-watching affected prisoner/round");

        // Victory waits for escape chain, then immediately stops remaining turns/guard.
        Restart(); a = game.CurrentPlayer; b = game.TurnOrder[1];
        var pieces = game.Prisoners.Where(p => p.Owner == a).ToArray();
        for (int i = 0; i < 3; i++) pieces[i].Deploy(new Vector2Int(i, 0));
        game.ConfirmEscape(pieces[0]); game.ConfirmEscape(pieces[1]);
        Give(b, cancel); game.SelectMove(); game.OnPrisonerClicked(pieces[2]); game.MoveRightOrEscape();
        Require(game.IsChainOpen && game.EscapedCount(a) == 2 && game.Winner == -1, "Victory confirmed before chain");
        game.PassChain(b);
        Require(game.State == GameState.Result && game.Winner == a && game.EscapedCount(a) == 3, "Third escape did not finish immediately");
        int round = game.Round; game.EndTurn(); game.ConfirmHandoff(); game.OnCellClicked(Vector2Int.zero);
        Require(game.State == GameState.Result && game.Round == round && !game.UseCard(b, 0), "Finished game accepted input");

        Restart(); a = game.CurrentPlayer; b = game.TurnOrder[1];
        pieces = game.Prisoners.Where(p => p.Owner == a).ToArray();
        for (int i = 0; i < 3; i++) pieces[i].Deploy(new Vector2Int(i, 0));
        game.ConfirmEscape(pieces[0]); game.ConfirmEscape(pieces[1]); Give(b, cancel);
        game.SelectMove(); game.OnPrisonerClicked(pieces[2]); game.MoveRightOrEscape();
        game.UseCard(b, 0); game.PassChain(a); game.PassChain(b);
        Require(game.Winner == -1 && game.IsPlayerTurn && game.EscapedCount(a) == 2 && pieces[2].IsOnBoard && game.ActionConsumed,
            "Cancelled escape produced early victory");

        Restart();
        foreach (Prisoner piece in game.Prisoners) { piece.Deploy(Vector2Int.zero); piece.Capture(); }
        FenceAction(); Require(game.IsPlayerTurn && game.Winner == -1, "Prototype no-winner rule ended the game");

        // Existing HUD geometry, contextual turn-end button and retry.
        Restart(); yield return null;
        var canvas = game.GetComponentsInChildren<Canvas>(true).Single();
        Require(canvas.GetComponent<CanvasScaler>().referenceResolution == new Vector2(1280, 800), "HUD geometry changed");
        FenceAction(); ClickButton("右へ／脱獄 →"); Require(game.State == GameState.Handoff, "HUD turn-end not wired");
        game.RestartGame(); yield return null;
        Require(game.Prisoners.All(p => p.IsInReserve) && game.Grid.FenceCount == 5 && game.Round == 1, "Restart failed");
        Require(game.GetComponentsInChildren<Prisoner>(true).Length == 9, "Restart leaked pieces");
    }
}

public sealed class FlowCheckCard : SkillCard
{
    public Func<GameManager, int, FlowContext, bool> Condition;
    public System.Action<GameManager, int, FlowContext> Effect;
    public int Target = -1;
    public bool IsImmediate;
    public override bool Immediate => IsImmediate;
    public override int TargetPlayer(GameManager game, int owner) => Target;
    public override bool CanPlay(GameManager game, int owner, FlowContext context) => Condition?.Invoke(game, owner, context) ?? false;
    public override void Resolve(GameManager game, int owner, FlowContext context) => Effect?.Invoke(game, owner, context);
}
public sealed class FlowCheckRules : FlowRules
{
    public bool Block;
    public bool ProtectFences;
    public override bool AllowsAction(GameManager game, int owner, PlayerAction action) => !Block;
    public override bool AllowsFence(GameManager game, int owner, FenceBoundary boundary, bool placing) => placing || !ProtectFences;
}
