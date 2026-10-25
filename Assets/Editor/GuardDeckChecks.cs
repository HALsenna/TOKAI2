using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Daidatsugoku;

/// <summary>Deterministic checks for the private monitoring deck and public history contract.</summary>
public static class GuardDeckChecks
{
    public static int Run()
    {
        int assertions = 0;
        Action<bool, string> require = (condition, message) =>
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("SYS-GUARD-001: " + message);
        };

        for (int rateIndex = 0; rateIndex < 7; rateIndex++)
        {
            var rng = new RecordingRandom(rateIndex);
            var deck = new GuardMonitoringDeck(rng.Next);
            PatrolCard[] order = Snapshot(deck);
            require(order.Length == 30, "G-001: a new deck must contain thirty cards.");
            require(rng.Bounds.SequenceEqual(GenerationBounds()), "G-003: choose one of seven rates, then shuffle exactly once.");
            require(order.Count(card => card == PatrolCard.WatchAllCells) == 6 + 3 * rateIndex,
                "G-002/G-004: each rate candidate must select its exact WATCHING count.");
            require(order.Count(card => card == PatrolCard.NotWatching) == 24 - 3 * rateIndex,
                "G-004: NOT_WATCHING count must complete the thirty-card deck.");
            for (int i = 0; i < 30; i++)
            {
                require(deck.Draw() == order[i], "G-005/G-008: consume each generated position once in its shuffled order.");
                require(rng.Bounds.Count == 30, "G-005/G-008: drawing must neither reroll nor reshuffle.");
            }
            deck.Draw();
            require(rng.Bounds.Count == 60, "G-009: only the thirty-first draw generates the second deck.");
            PatrolCard[] secondOrder = Snapshot(deck);
            for (int i = 1; i < 30; i++)
                require(deck.Draw() == secondOrder[i], "G-009: regeneration must consume exactly one card from the next deck.");
            require(rng.Bounds.Count == 60, "G-009: the next deck must last another thirty draws.");
        }

        // Supply identical consecutive rate choices, then different choices.
        // Each generation receives the same full seven-way selection request.
        var independentRandom = new RecordingRandom(0, 0, 6, 2);
        var independentDeck = new GuardMonitoringDeck(independentRandom.Next);
        int[] expectedCounts = { 6, 6, 24, 12 };
        foreach (int expected in expectedCounts)
        {
            int watching = 0;
            for (int i = 0; i < 30; i++) if (independentDeck.Draw() == PatrolCard.WatchAllCells) watching++;
            require(watching == expected, "G-010: a repeated rate must remain legal and prior results must not affect new composition.");
        }
        require(independentRandom.Bounds.SequenceEqual(Enumerable.Range(0, 4).SelectMany(_ => GenerationBounds())),
            "G-003/G-010: every deck must independently choose from all seven candidates.");

        var resetRandom = new RecordingRandom(0, 6);
        var resetDeck = new GuardMonitoringDeck(resetRandom.Next);
        for (int i = 0; i < 5; i++) resetDeck.Draw();
        resetDeck.ResetSession();
        int afterReset = 0;
        for (int i = 0; i < 30; i++) if (resetDeck.Draw() == PatrolCard.WatchAllCells) afterReset++;
        require(afterReset == 24 && resetRandom.Bounds.Count == 60, "Reset must replace a partially depleted deck with a new thirty-card session.");

        require(Throws(() => new GuardMonitoringDeck(_ => -1)), "Reject a negative random index.");
        require(Throws(() => new GuardMonitoringDeck(upper => upper)), "Reject an index at the exclusive upper bound.");
        int invalidShuffleCalls = 0;
        require(Throws(() => new GuardMonitoringDeck(upper => ++invalidShuffleCalls == 1 ? 0 : upper)),
            "Validate shuffle indices as well as rate selection.");

        var defaultDeck = new GuardMonitoringDeck();
        int defaultWatching = 0;
        for (int i = 0; i < 30; i++) if (defaultDeck.Draw() == PatrolCard.WatchAllCells) defaultWatching++;
        require(new[] { 6, 9, 12, 15, 18, 21, 24 }.Contains(defaultWatching), "The production random source must generate one of the seven valid compositions.");

        const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        require(typeof(GuardMonitoringDeck).GetProperties(publicInstance).Length == 0 &&
                typeof(GuardMonitoringDeck).GetFields(publicInstance).Length == 0,
            "G-012: the deck API must expose no composition, rate, remaining cards or order.");
        string[] publicHistory = typeof(GuardHistoryEntry).GetProperties(publicInstance).Select(property => property.Name).OrderBy(name => name).ToArray();
        require(publicHistory.SequenceEqual(new[] { "CardIndex", "DeckId", "Result", "SequenceId" }),
            "G-012: history must expose exactly the four permitted public values.");
        require(typeof(GuardHistoryEntry).GetFields(publicInstance).Length == 0 &&
                typeof(GuardHistoryEntry).GetProperties(publicInstance).All(property => property.GetSetMethod(true) == null),
            "G-012: public history must be immutable.");
        var entry = new GuardHistoryEntry(31, 2, 1, PatrolCard.NotWatching);
        require(entry.SequenceId == 31 && entry.DeckId == 2 && entry.CardIndex == 1 && entry.Result == PatrolCard.NotWatching,
            "G-011: immutable history values must preserve their supplied public metadata.");
        return assertions;
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException) { return true; }
    }

    private static PatrolCard[] Snapshot(GuardMonitoringDeck deck)
    {
        // Tests alone inspect private future order. Runtime UI never receives it.
        var field = typeof(GuardMonitoringDeck).GetField("cards", BindingFlags.NonPublic | BindingFlags.Instance);
        return ((PatrolCard[])field.GetValue(deck)).ToArray();
    }

    private static IEnumerable<int> GenerationBounds()
    {
        yield return 7;
        for (int upper = 30; upper >= 2; upper--) yield return upper;
    }

    private sealed class RecordingRandom
    {
        public readonly List<int> Bounds = new();
        private readonly int[] rates;
        public RecordingRandom(params int[] rates) { this.rates = rates; }
        public int Next(int upper)
        {
            int call = Bounds.Count;
            Bounds.Add(upper);
            return call % 30 == 0 ? rates[(call / 30) % rates.Length] : (call * 11 + 3) % upper;
        }
    }
}
