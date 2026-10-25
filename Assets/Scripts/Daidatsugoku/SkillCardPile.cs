using System;
using System.Collections.Generic;

namespace Daidatsugoku
{
    public sealed class SkillCardPile
    {
        private readonly List<SkillCardDefinition> draw = new();
        private readonly List<SkillCardDefinition> used = new();
        private Func<int, int> next;
        public int DrawCount => draw.Count;
        public IReadOnlyList<SkillCardDefinition> Used => used.AsReadOnly();
        public void Reset(IEnumerable<SkillCardDefinition> cards, Func<int, int> randomIndex)
        {
            next = randomIndex ?? throw new ArgumentNullException(nameof(randomIndex));
            draw.Clear(); used.Clear();
            foreach (var card in cards) if (card != null) draw.Add(card);
            Shuffle();
        }
        private void Shuffle()
        {
            for (int i = draw.Count - 1; i > 0; i--)
            {
                int j = next(i + 1);
                (draw[i], draw[j]) = (draw[j], draw[i]);
            }
        }
        public bool TryDraw(out SkillCardDefinition card)
        {
            if (draw.Count == 0 && used.Count > 0) { draw.AddRange(used); used.Clear(); Shuffle(); }
            card = null;
            if (draw.Count == 0) return false;
            int index = draw.Count - 1; card = draw[index]; draw.RemoveAt(index); return true;
        }
        public void Discard(SkillCardDefinition card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            used.Add(card);
        }
    }
}
