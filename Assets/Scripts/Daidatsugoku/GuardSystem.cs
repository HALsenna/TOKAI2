using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public enum PatrolCard { WatchAllCells, NotWatching }

    public sealed class GuardSystem : MonoBehaviour
    {
        // Preserve the existing prototype deck until SYS-GUARD-001 data is supplied.
        [SerializeField] private List<PatrolCard> initialDeck = new()
        {
            PatrolCard.WatchAllCells, PatrolCard.WatchAllCells, PatrolCard.WatchAllCells, PatrolCard.WatchAllCells,
            PatrolCard.WatchAllCells, PatrolCard.WatchAllCells, PatrolCard.WatchAllCells, PatrolCard.WatchAllCells
        };
        private readonly Queue<PatrolCard> deck = new();
        public void ResetDeck()
        {
            deck.Clear();
            foreach (PatrolCard card in initialDeck) deck.Enqueue(card);
        }
        public PatrolCard DrawCard()
        {
            if (deck.Count == 0) ResetDeck();
            if (deck.Count == 0) throw new System.InvalidOperationException("Guard deck must contain at least one card.");
            return deck.Dequeue();
        }
        public string GetCardName(PatrolCard card) => card == PatrolCard.NotWatching ? "見とらん" : "見とる";
        public List<Prisoner> Targets(PatrolCard card, GridManager grid, IReadOnlyList<Prisoner> prisoners, int owner)
        {
            if (card != PatrolCard.WatchAllCells && card != PatrolCard.NotWatching) throw new System.ArgumentOutOfRangeException(nameof(card));
            List<Prisoner> targets = new();
            if (card == PatrolCard.NotWatching) return targets;
            foreach (Prisoner prisoner in prisoners)
                if (prisoner.Owner == owner && prisoner.IsOnBoard && !grid.IsProtected(prisoner.Cell)) targets.Add(prisoner);
            return targets;
        }
        public int Resolve(PatrolCard card, GridManager grid, IReadOnlyList<Prisoner> prisoners)
        {
            if (card == PatrolCard.NotWatching) return 0;
            if (card != PatrolCard.WatchAllCells) throw new System.ArgumentOutOfRangeException(nameof(card));
            int captured = 0;
            foreach (Prisoner prisoner in prisoners)
            {
                if (!prisoner.IsOnBoard || grid.IsProtected(prisoner.Cell)) continue;
                prisoner.Capture(); captured++;
            }
            return captured;
        }
    }
}
