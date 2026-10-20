using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public enum PatrolCard { WatchAllCells, LookAway }

    public sealed class GuardSystem : MonoBehaviour
    {
        private readonly Queue<PatrolCard> deck = new();
        public void ResetDeck()
        {
            deck.Clear();
            PatrolCard[] cards = new PatrolCard[8];
            for (int i = 0; i < cards.Length; i++)
                cards[i] = i < cards.Length / 2 ? PatrolCard.WatchAllCells : PatrolCard.LookAway;
            for (int i = cards.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
            foreach (PatrolCard card in cards) deck.Enqueue(card);
        }
        public PatrolCard DrawCard()
        {
            if (deck.Count == 0) ResetDeck();
            return deck.Dequeue();
        }
        public string GetCardName(PatrolCard card) => card switch
        {
            PatrolCard.WatchAllCells => "全マスを監視（見ている）",
            PatrolCard.LookAway => "見ていない",
            _ => throw new System.ArgumentOutOfRangeException(nameof(card))
        };
        public int Resolve(PatrolCard card, GridManager grid, IReadOnlyList<Prisoner> prisoners)
        {
            if (card == PatrolCard.LookAway) return 0;
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
