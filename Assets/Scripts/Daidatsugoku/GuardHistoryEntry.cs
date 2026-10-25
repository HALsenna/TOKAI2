using System;

namespace Daidatsugoku
{
    /// <summary>Immutable public information for one revealed monitoring card.</summary>
    public readonly struct GuardHistoryEntry
    {
        public int SequenceId { get; }
        public int DeckId { get; }
        public int CardIndex { get; }
        public PatrolCard Result { get; }

        public GuardHistoryEntry(int sequenceId, int deckId, int cardIndex, PatrolCard result)
        {
            if (sequenceId < 1) throw new ArgumentOutOfRangeException(nameof(sequenceId));
            if (deckId < 1) throw new ArgumentOutOfRangeException(nameof(deckId));
            if (cardIndex < 1 || cardIndex > GuardMonitoringDeck.CardsPerDeck)
                throw new ArgumentOutOfRangeException(nameof(cardIndex));
            if (result != PatrolCard.WatchAllCells && result != PatrolCard.NotWatching)
                throw new ArgumentOutOfRangeException(nameof(result));
            SequenceId = sequenceId;
            DeckId = deckId;
            CardIndex = cardIndex;
            Result = result;
        }
    }
}
