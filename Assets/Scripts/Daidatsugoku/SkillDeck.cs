using System;
using System.Collections.Generic;

namespace Daidatsugoku
{
    public sealed class SkillDeck
    {
        private readonly List<SkillCard> draw = new();
        private readonly List<SkillCard> discard = new();
        public int DrawCount => draw.Count;
        public IReadOnlyList<SkillCard> Discard => discard.AsReadOnly();
        public void Reset(IEnumerable<SkillCard> cards)
        {
            draw.Clear(); discard.Clear();
            foreach (SkillCard card in cards) if (card != null) draw.Add(card);
            Shuffle();
        }
        private void Shuffle()
        {
            for (int i = draw.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (draw[i], draw[j]) = (draw[j], draw[i]);
            }
        }
        public SkillCard Draw()
        {
            if (draw.Count == 0)
            {
                draw.AddRange(discard); discard.Clear(); Shuffle();
            }
            // SYS-TURN-001 leaves both piles empty undefined. Skip the draw, never fabricate cards.
            if (draw.Count == 0) return null;
            int last = draw.Count - 1;
            SkillCard card = draw[last]; draw.RemoveAt(last); return card;
        }
        public void DiscardCard(SkillCard card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            discard.Add(card);
        }
    }
}
