using System;

namespace Daidatsugoku
{
    /// <summary>
    /// SYS-GUARD-001 v1.2 monitoring deck. Only consumed results leave this object;
    /// the selected rate, composition and remaining order are private session state.
    /// </summary>
    public sealed class GuardMonitoringDeck
    {
        public const int CardsPerDeck = 30;

        private readonly Func<int, int> randomIndex;
        private PatrolCard[] cards;
        private int drawIndex;

        public GuardMonitoringDeck(Func<int, int> randomIndex = null)
        {
            this.randomIndex = randomIndex ?? new Func<int, int>(new Random().Next);
            ResetSession();
        }

        public void ResetSession() => GenerateDeck();

        public PatrolCard Draw()
        {
            // Preserve the thirtieth result. The next call generates a new deck
            // and consumes exactly its first card.
            if (drawIndex == CardsPerDeck) GenerateDeck();
            return cards[drawIndex++];
        }

        private void GenerateDeck()
        {
            // One independent uniform draw over all seven rates, including the
            // previous rate. No past result or deck composition is an input.
            int watchingCount = 6 + 3 * NextIndex(7);
            PatrolCard[] nextCards = new PatrolCard[CardsPerDeck];
            for (int i = 0; i < nextCards.Length; i++)
                nextCards[i] = i < watchingCount ? PatrolCard.WatchAllCells : PatrolCard.NotWatching;

            // Fisher-Yates runs once at generation; depletion never reshuffles.
            for (int i = nextCards.Length - 1; i > 0; i--)
            {
                int j = NextIndex(i + 1);
                (nextCards[i], nextCards[j]) = (nextCards[j], nextCards[i]);
            }

            cards = nextCards;
            drawIndex = 0;
        }

        private int NextIndex(int exclusiveUpperBound)
        {
            int index = randomIndex(exclusiveUpperBound);
            if (index < 0 || index >= exclusiveUpperBound)
                throw new InvalidOperationException("The monitoring random source returned an index outside its requested range.");
            return index;
        }
    }
}
