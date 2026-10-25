using System;
using System.Collections.Generic;

namespace Daidatsugoku
{
    /// <summary>
    /// Alternating responses between two participants. The caller supplies the
    /// first responder because response priority belongs to the event rules.
    /// </summary>
    public sealed class ResponseChain<T>
    {
        private readonly int participantA;
        private readonly int participantB;
        private readonly List<T> entries = new List<T>();

        public int Responder { get; private set; }
        public int ConsecutivePasses { get; private set; }
        public int Count => entries.Count;
        public bool Closed { get; private set; }

        public ResponseChain(int participantA, int participantB, int firstResponder)
        {
            if (participantA == participantB)
                throw new ArgumentException("A response chain requires two distinct participants.", nameof(participantB));
            if (firstResponder != participantA && firstResponder != participantB)
                throw new ArgumentException("The first responder must be a chain participant.", nameof(firstResponder));

            this.participantA = participantA;
            this.participantB = participantB;
            Responder = firstResponder;
        }

        public bool Contains(int player) => player == participantA || player == participantB;

        public bool TryAdd(int player, T entry)
        {
            if (Closed || player != Responder)
                return false;

            entries.Add(entry);
            ConsecutivePasses = 0;
            AdvanceResponder();
            return true;
        }

        public bool TryPass(int player)
        {
            if (Closed || player != Responder)
                return false;

            ConsecutivePasses++;
            Closed = ConsecutivePasses == 2;
            AdvanceResponder();
            return true;
        }

        /// <summary>Returns a read-only LIFO snapshot after both participants pass.</summary>
        public IReadOnlyList<T> ResolutionOrder()
        {
            if (!Closed)
                throw new InvalidOperationException("The response chain must close before resolution.");

            var snapshot = new T[entries.Count];
            for (int index = 0; index < entries.Count; index++)
                snapshot[index] = entries[entries.Count - index - 1];
            return Array.AsReadOnly(snapshot);
        }

        private void AdvanceResponder()
        {
            Responder = Responder == participantA ? participantB : participantA;
        }
    }
}
