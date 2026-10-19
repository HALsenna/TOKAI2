using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public enum PatrolCard { WatchAllCells }

    public sealed class GuardSystem : MonoBehaviour
    {
        private readonly Queue<PatrolCard> deck = new();
        public void ResetDeck()
        {
            deck.Clear();
            for (int i = 0; i < 8; i++) deck.Enqueue(PatrolCard.WatchAllCells);
        }
        public PatrolCard DrawCard()
        {
            if (deck.Count == 0) ResetDeck();
            return deck.Dequeue();
        }
        public string GetCardName(PatrolCard card) => "全マスを監視";
        public int Resolve(PatrolCard card, GridManager grid, IReadOnlyList<Prisoner> prisoners)
        {
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
