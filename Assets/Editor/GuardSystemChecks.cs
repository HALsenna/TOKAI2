using System;
using System.Collections.Generic;
using System.Linq;
using Daidatsugoku;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>SYS-GUARD-001 integration and real UGUI checks; all fixtures are explicit.</summary>
public static class GuardSystemChecks
{
    private sealed class Vision : IGuardVisionService
    {
        public Prisoner Visible;
        public int Calls;
        public bool IsVisible(GridManager grid, Prisoner prisoner) { Calls++; return prisoner == Visible; }
    }
    private sealed class Failure : IGuardPrisonerService
    {
        public readonly List<Prisoner> Targets = new();
        public void FailEscape(Prisoner prisoner) => Targets.Add(prisoner);
    }

    public static int Run(GameManager game)
    {
        int assertions = 0;
        Action<bool, string> require = (condition, message) =>
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("SYS-GUARD-001: " + message);
        };
        GameObject testObject = new("CHECK_GuardHistory", typeof(GuardSystem));
        TurnCheckCard inert = ScriptableObject.CreateInstance<TurnCheckCard>();
        TurnCheckPolicy policy = ScriptableObject.CreateInstance<TurnCheckPolicy>();
        TurnCheckMonitoring source = ScriptableObject.CreateInstance<TurnCheckMonitoring>();
        var guard = testObject.GetComponent<GuardSystem>();
        try
        {
            guard.ResetDeck();
            require(guard.PublicHistory.Count == 0, "G-011: a new match has no revealed results.");
            for (int round = 1; round <= 65; round++)
            {
                require(guard.TryDrawMonitoring(round, out var card, out _), "G-005: default monitoring must be available.");
                GuardHistoryEntry entry = guard.PublicHistory[round - 1];
                require(entry.SequenceId == round && entry.DeckId == (round - 1) / 30 + 1 && entry.CardIndex == (round - 1) % 30 + 1 && entry.Result == card,
                    "G-008/G-009/G-011: history metadata/result must survive deck regeneration.");
                require(guard.TryDrawMonitoring(round, out var repeated, out _) && repeated == card && guard.PublicHistory.Count == round,
                    "G-005: a duplicate round must reuse its published result without consuming or recording again.");
            }
            require(guard.TryDrawMonitoring(1, out var first, out _) && first == guard.PublicHistory[0].Result && guard.PublicHistory.Count == 65,
                "G-011: retrying an earlier revealed round must preserve all history.");
            require(!guard.TryDrawMonitoring(0, out _, out _) && !guard.TryDrawMonitoring(-1, out _, out _) && guard.PublicHistory.Count == 65,
                "Invalid round IDs must not consume cards.");
            bool immutable = false;
            try { ((IList<GuardHistoryEntry>)guard.PublicHistory)[0] = default; }
            catch (NotSupportedException) { immutable = true; }
            require(immutable, "G-012: published history storage must be read-only.");

            source.Unavailable = true; guard.MonitoringSource = source; guard.ResetDeck();
            require(!guard.TryDrawMonitoring(1, out _, out _) && guard.PublicHistory.Count == 0, "A failed source must not publish a draw.");
            source.Unavailable = false; source.Card = PatrolCard.NotWatching;
            require(guard.TryDrawMonitoring(1, out _, out _) && source.Draws == 1, "A failed draw can be retried exactly once.");
            guard.MonitoringSource = null;
            require(guard.TryDrawMonitoring(2, out _, out _) && source.Draws == 2, "Source changes must wait for session reset.");
            guard.ResetDeck();
            require(guard.PublicHistory.Count == 0 && guard.TryDrawMonitoring(1, out _, out _) && source.Draws == 2,
                "Reset must apply the new source configuration and clear history.");

            game.Configure(2, Enumerable.Repeat(inert, 64), null, policy);
            game.Guard.MonitoringSource = null; game.InitializeGame(); game.StartFirstTurn();
            Prisoner[] pieces = game.Prisoners.Take(2).ToArray();
            foreach (var piece in pieces) piece.Deploy(Vector2Int.zero);
            var vision = new Vision { Visible = pieces[0] }; var failure = new Failure();
            guard.VisionService = vision; guard.PrisonerService = failure;
            require(guard.Resolve(PatrolCard.NotWatching, game.Grid, game.Prisoners) == 0 && vision.Calls == 0 && failure.Targets.Count == 0,
                "G-007: NOT_WATCHING must not call either external service.");
            require(guard.Resolve(PatrolCard.WatchAllCells, game.Grid, game.Prisoners) == 1 && vision.Calls == 2 && failure.Targets.SequenceEqual(new[] { pieces[0] }),
                "G-006: only visible, on-board prisoners are passed to the failure service.");

            CheckOverlay(game, require);
        }
        finally
        {
            var overlay = game.GetComponentInChildren<GuardHistoryOverlay>(true);
            if (overlay != null) overlay.Close();
            game.Configure(2, Array.Empty<SkillCardDefinition>()); game.InitializeGame();
            UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(policy);
            UnityEngine.Object.DestroyImmediate(inert); UnityEngine.Object.DestroyImmediate(testObject);
        }
        return assertions;
    }

    private static void CheckOverlay(GameManager game, Action<bool, string> require)
    {
        game.InitializeGame(); game.StartFirstTurn();
        GuardHistoryOverlay overlay = game.GetComponentInChildren<GuardHistoryOverlay>(true);
        require(overlay != null, "G-013: history must be connected to the real game HUD.");
        Button button = overlay.transform.Find("GuardHistoryButton").GetComponent<Button>();
        require(button.gameObject.activeInHierarchy && button.IsInteractable(), "G-013: the history launcher is always available.");
        GuardHistoryTheme originalTheme = overlay.Theme;
        var state = game.State; var phase = game.Phase; var action = game.Action;
        int round = game.Round, handCount = game.Hand(game.CurrentPlayer).Count;
        button.onClick.Invoke();
        require(overlay.IsOpen && game.Guard.PublicHistory.Count == 0 && game.State == state && game.Phase == phase && game.Round == round,
            "G-016: opening empty history must not draw or advance the game.");
        require(overlay.GetComponentsInChildren<Text>().Any(text => text.name == "GuardHistoryEmpty"), "Empty history must show a useful message.");
        game.SelectMove(); game.OnCellClicked(Vector2Int.zero); game.EndTurn(); game.ConfirmHandoff(); game.RestartGame(); game.RetryPendingSpecification();
        require(game.Action == action && game.Phase == phase && game.Round == round && game.Prisoners.All(piece => piece.IsInReserve) && game.Hand(game.CurrentPlayer).Count == handCount,
            "G-016: gameplay callbacks must reject input while history is open.");
        overlay.Close();
        require(!overlay.IsOpen && button.IsInteractable() && game.State == state && game.Phase == phase, "G-013/G-016: closing must restore input without advancing.");
        game.SelectPlacePrisoner(); game.OnCellClicked(Vector2Int.zero);
        require(game.ActionConsumed && game.CanEndTurn, "The close operation must restore normal gameplay input.");
        for (int i = 1; i <= 200; i++) require(game.Guard.TryDrawMonitoring(i, out _, out _), "Create a long, public history.");
        var before = game.Guard.PublicHistory.ToArray();
        overlay.Open(); Canvas.ForceUpdateCanvases(); overlay.Refresh();
        Image background = overlay.transform.Find("GuardHistoryOverlay").GetComponent<Image>();
        require(background.color == new Color(0, 0, 0, .7f), "G-014: the full-screen background must be black with seventy percent opacity.");
        ScrollRect scroll = overlay.GetComponentInChildren<ScrollRect>();
        require(scroll != null && scroll.vertical && !scroll.horizontal && scroll.content.rect.height > scroll.viewport.rect.height,
            $"G-017: long history must overflow a vertically scrollable viewport (content={scroll?.content.rect}, viewport={scroll?.viewport.rect}, screen={Screen.width}x{Screen.height}).");
        require(scroll.content.Find("GuardHistoryDeck-1") != null && scroll.content.Find("GuardHistoryDeck-2") != null && scroll.content.Find("GuardHistoryDeck-3") != null,
            "G-017: each deck must have its own separator.");
        scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
        require(scroll.content.anchoredPosition.y > 0, "G-017: scrolling to the end must move the content to later results.");
        scroll.verticalNormalizedPosition = 1;
        for (int i = 0; i < before.Length; i++)
        {
            GuardHistoryEntry entry = before[i];
            Image card = scroll.content.Find("GuardHistoryCard-" + entry.SequenceId).GetComponent<Image>();
            Text text = card.GetComponentInChildren<Text>();
            bool watching = entry.Result == PatrolCard.WatchAllCells;
            require(card.color == (watching ? originalTheme.Watching : originalTheme.NotWatching) && text.color == Color.white && text.fontStyle == FontStyle.Bold && text.alignment == TextAnchor.MiddleCenter && text.text == (watching ? "見\nと\nる" : "見\nと\nら\nん"),
                "G-015: result cards must use the specified color and centered white bold vertical text.");
            require(((RectTransform)card.transform).rect.height > ((RectTransform)card.transform).rect.width, "G-015: cards must be portrait rectangles.");
        }
        Button end = game.GetComponentsInChildren<Button>(true).First(value => value.GetComponentInChildren<Text>()?.text == "ターン終了");
        require(!end.IsInteractable() && EventSystem.current.currentSelectedGameObject.name == "GuardHistoryCloseButton", "Only history controls may receive pointer/submit input.");
        game.EndTurn(); require(game.Round == round && game.Phase == phase && game.ActionConsumed, "Opening history must not allow end-turn input through.");
        overlay.SetTheme(new GuardHistoryTheme { Watching = Color.blue, NotWatching = Color.yellow, CardSize = new Vector2(100, 190) });
        require(game.Guard.PublicHistory.SequenceEqual(before) && game.Round == round && game.Phase == phase, "G-018: changing the theme must not affect monitoring or game state.");
        Image themed = scroll.content.Find("GuardHistoryCard-1").GetComponent<Image>();
        require(themed.color == (before[0].Result == PatrolCard.WatchAllCells ? Color.blue : Color.yellow), "G-018: the independent presentation theme must be applied.");
        overlay.SetTheme(originalTheme); overlay.Close();
        require(end.IsInteractable() && game.Guard.PublicHistory.SequenceEqual(before), "G-016: closing restores controls and preserves results.");
    }

    public static void PreparePreview(GameManager game)
    {
        var inert = ScriptableObject.CreateInstance<TurnCheckCard>(); inert.name = "CHECK_PreviewCard";
        var policy = ScriptableObject.CreateInstance<TurnCheckPolicy>(); policy.name = "CHECK_PreviewPolicy";
        game.Configure(2, Enumerable.Repeat(inert, 64), null, policy); game.Guard.MonitoringSource = null;
        game.InitializeGame(); game.StartFirstTurn();
        game.Prisoners[0].Deploy(Vector2Int.zero); game.Prisoners[3].Deploy(new Vector2Int(2, 2));
        game.Grid.RefreshPiecePositions(game.Prisoners);
        for (int i = 1; i <= 65; i++) game.Guard.TryDrawMonitoring(i, out _, out _);
        game.GetComponentInChildren<GuardHistoryOverlay>(true).Open();
    }
}
