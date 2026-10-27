using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Daidatsugoku;
using UnityEngine;

/// <summary>Exercises the actual temporary card classes through normal GameManager activation.</summary>
public static class PrototypeCardChecks
{
    public static int CheckDefaultDeck(GameManager game)
    {
        var field = typeof(GameManager).GetField("skillCards", BindingFlags.Instance | BindingFlags.NonPublic);
        var deck = (IEnumerable<SkillCardDefinition>)field.GetValue(game);
        var cards = deck.ToList();
        if (cards.Count != 30 || cards.Count(card => card is PrototypeDrawOneCard) != 25 ||
            cards.Count(card => card is PrototypeFlipGuardCard) != 5 || !(game.FlowPolicy is PrototypeSkillFlowPolicy))
            throw new InvalidOperationException("The default scene must use the authorized temporary 25/5 deck and its limited flow policy.");
        return 1;
    }

    public static int Run(GameManager game)
    {
        int assertions = 0;
        Action<bool, string> require = (condition, message) =>
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Prototype cards: " + message);
        };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        int originalCount = (int)typeof(GameManager).GetField("playerCount", flags).GetValue(game);
        var originalDeck = new List<SkillCardDefinition>((IEnumerable<SkillCardDefinition>)typeof(GameManager).GetField("skillCards", flags).GetValue(game));
        FieldStage originalStage = game.SelectedStage;
        NormalActionRules originalRules = game.Rules;
        TurnFlowPolicy originalPolicy = game.FlowPolicy;
        GuardCardSource originalMonitoring = game.Guard.MonitoringSource;
        var draw = ScriptableObject.CreateInstance<PrototypeDrawOneCard>();
        var flip = ScriptableObject.CreateInstance<PrototypeFlipGuardCard>();
        var policy = ScriptableObject.CreateInstance<PrototypeSkillFlowPolicy>();
        var unsupported = ScriptableObject.CreateInstance<TurnCheckCard>();
        int discoveries = 0, flips = 0;
        Action<IReadOnlyList<Prisoner>> discovered = targets => discoveries++;
        Action positionChanged = () => flips++;
        try
        {
            game.ConfigureStage(game.Stages.Single(stage => stage.StageId == "01"));
            game.Guard.MonitoringSource = null;
            game.Configure(2, new SkillCardDefinition[] { flip, flip, draw }, null, policy);
            game.InitializeGame();
            require(!draw.CanPlay(game, game.CurrentPlayer, null) && !flip.CanPlay(game, game.CurrentPlayer, null), "Setup must not play cards or auto-flip the guard.");
            require(game.Vision.Position.Side == GuardSide.Top, "The temporary cards must not alter initial TOP placement.");
            // No swaps: the final draw-card reference is the first card drawn.
            game.Cards.Reset(new SkillCardDefinition[] { flip, flip, draw }, size => size - 1);
            game.StartFirstTurn();
            game.Guard.PrisonersDiscovered += discovered;
            game.Vision.PositionChanged += positionChanged;
            int owner = game.CurrentPlayer, other = 1 - owner;
            int column = game.Vision.Position.Column, round = game.Round, history = game.Guard.PublicHistory.Count;
            require(game.Phase == TurnPhase.PlayerInput && game.Hand(owner).Count == 1 && ReferenceEquals(game.Hand(owner)[0], draw), "The turn-start draw must put the real temporary draw card in hand.");
            require(!draw.Immediate && !flip.Immediate && !draw.CanInvalidateGuardMonitoring && !flip.CanInvalidateGuardMonitoring, "Temporary cards must be manual, without monitoring invalidation or immediate effects.");
            require(draw.DisplayName.StartsWith("仮：") && flip.DisplayName.StartsWith("仮："), "Temporary card labels must remain explicit.");
            require(draw.CanPlay(game, owner, null) && flip.CanPlay(game, owner, null), "Both cards must allow the current owner's free-play window.");
            require(!draw.CanPlay(game, other, null) && !flip.CanPlay(game, other, null), "Other players may not use these cards during the current turn.");
            require(!draw.CanPlay(null, owner, null) && !flip.CanPlay(null, owner, null), "Missing game context must reject use.");
            var response = new TurnEvent(TurnEventKind.NormalAction, owner);
            var monitoring = new TurnEvent(TurnEventKind.GuardMonitoring, -1, monitoringResult: PatrolCard.WatchAllCells);
            require(!draw.CanPlay(game, owner, response) && !flip.CanPlay(game, owner, response) &&
                    !draw.CanPlay(game, owner, monitoring) && !flip.CanPlay(game, owner, monitoring), "These temporary cards must not introduce response or monitoring windows.");
            CheckPolicy(game, policy, draw, unsupported, owner, require);

            require(game.UseCard(owner, 0), "The real draw-card activation must succeed.");
            require(game.Hand(owner).Count == 1 && ReferenceEquals(game.Hand(owner)[0], flip) && game.Cards.DrawCount == 1,
                "Playing one draw card must consume that hand card and draw exactly one existing card.");
            require(game.Cards.Used.Count == 1 && ReferenceEquals(game.Cards.Used[0], draw), "The used draw card must go to the existing discard pile.");
            require(!game.ActionConsumed && !game.ActionDeclared && game.Phase == TurnPhase.PlayerInput && !game.IsChainOpen,
                "Drawing must preserve the normal action and return to player input without a fabricated chain.");

            Prisoner marker = game.Prisoners.First(piece => piece.Owner == owner);
            marker.Deploy(new Vector2Int(game.Grid.GetRowStart(0), 0));
            require(game.UseCard(owner, 0), "The guard-flip card must be usable before a normal action.");
            require(game.Vision.Position.Side == GuardSide.Bottom && game.Vision.Position.Column == column && flips == 1,
                "One flip must persist at BOTTOM, maintain its column and update the position once.");
            require(!game.ActionConsumed && marker.IsOnBoard && discoveries == 0 && game.Guard.PublicHistory.Count == history,
                "A guard flip must neither consume the normal action nor draw monitoring or discover prisoners.");

            require(game.DrawSkillCard(owner) && game.Hand(owner).Count == 1 && ReferenceEquals(game.Hand(owner)[0], flip), "The remaining explicit card must draw through the existing pile.");
            game.SelectPlacePrisoner(); game.OnCellClicked(new Vector2Int(game.Grid.GetRowStart(1), 1));
            require(game.ActionConsumed && game.Phase == TurnPhase.PlayerInput, "A normal action must remain available after using temporary cards.");
            require(game.UseCard(owner, 0), "The second guard-flip card must be usable after the normal action.");
            require(game.Vision.Position.Side == GuardSide.Top && game.Vision.Position.Column == column && flips == 2 && game.ActionConsumed,
                "A second flip must return to TOP while preserving the already-consumed normal action.");
            require(game.Cards.DrawCount == 0 && game.Cards.Used.Count == 3, "All three explicit fixture cards must reach the discard pile exactly once.");
            require(game.DrawSkillCard(owner) && game.Hand(owner).Count == 1 && game.Cards.DrawCount == 2 && game.Cards.Used.Count == 0,
                "The existing skill pile must recycle used temporary cards without inventing a new rebuild rule.");
            require(game.Round == round && game.CurrentPlayer == owner && game.Guard.PublicHistory.Count == history && discoveries == 0 && marker.IsOnBoard,
                "Card operations must not advance the turn, consume monitoring or remove prisoners.");
        }
        finally
        {
            game.Guard.PrisonersDiscovered -= discovered;
            game.Vision.PositionChanged -= positionChanged;
            game.Guard.MonitoringSource = originalMonitoring;
            game.ConfigureStage(originalStage); game.Configure(originalCount, originalDeck, originalRules, originalPolicy);
            if (originalStage != null) game.InitializeGame(); else game.ShowStageSelection();
            UnityEngine.Object.DestroyImmediate(unsupported); UnityEngine.Object.DestroyImmediate(policy);
            UnityEngine.Object.DestroyImmediate(flip); UnityEngine.Object.DestroyImmediate(draw);
        }
        return assertions;
    }

    private static void CheckPolicy(GameManager game, PrototypeSkillFlowPolicy policy, SkillCardDefinition draw,
        SkillCardDefinition unsupported, int owner, Action<bool, string> require)
    {
        var activation = new TurnEvent(TurnEventKind.SkillActivation, owner, card: draw);
        require(policy.TryPlanResponses(game, activation, Array.Empty<int>(), out ResponsePlan plan, out _) && plan.NoResponses,
            "The temporary policy must resolve exactly the supported no-responder card activation.");
        require(policy.TryPlanResponses(game, new TurnEvent(TurnEventKind.NormalAction, owner), Array.Empty<int>(), out plan, out _) && plan.NoResponses,
            "No-responder normal actions must remain supported.");
        require(!policy.TryPlanResponses(game, activation, new[] { owner }, out _, out string reason) && !string.IsNullOrEmpty(reason),
            "The policy must not silently discard an eligible interruption.");
        require(!policy.TryPlanResponses(game, new TurnEvent(TurnEventKind.SkillActivation, owner, card: unsupported), Array.Empty<int>(), out _, out reason) && !string.IsNullOrEmpty(reason),
            "Unspecified card types must still require their own response policy.");
        require(!policy.TryPlanResponses(game, new TurnEvent(TurnEventKind.GuardMonitoring, -1), Array.Empty<int>(), out _, out _) &&
                !policy.TryPlanResponses(game, new TurnEvent(TurnEventKind.SkillActivation, owner, card: draw, parent: activation), Array.Empty<int>(), out _, out _),
            "The temporary policy must not invent guard or nested-chain priorities.");
    }
}
