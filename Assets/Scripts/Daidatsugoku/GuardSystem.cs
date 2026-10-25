using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public enum PatrolCard { WatchAllCells = 0, NotWatching = 1 }

    public sealed class GuardSystem : MonoBehaviour
    {
        private readonly Queue<PatrolCard> deck = new();
        [SerializeField] private GuardCardSource monitoringSource;
        public GuardCardSource MonitoringSource
        {
            get => monitoringSource;
            set => monitoringSource = value;
        }

        public void ResetDeck()
        {
            deck.Clear();
            for (int i = 0; i < 8; i++) deck.Enqueue(PatrolCard.WatchAllCells);
            monitoringSource?.ResetSession();
        }
        public PatrolCard DrawCard()
        {
            if (deck.Count == 0) ResetDeck();
            return deck.Dequeue();
        }
        public bool TryDrawMonitoring(int round, out PatrolCard card, out string reason)
        {
            if (monitoringSource == null)
            {
                card = default;
                reason = "SYS-GUARD-001 v1.1 に従う監視カード供給元が未設定です。";
                return false;
            }
            return monitoringSource.TryDraw(round, out card, out reason);
        }

        public string GetCardName(PatrolCard card) => card switch
        {
            PatrolCard.WatchAllCells => "見とる",
            PatrolCard.NotWatching => "見とらん",
            _ => throw new System.ArgumentOutOfRangeException(nameof(card))
        };

        public List<Prisoner> FindTargets(PatrolCard card, GridManager grid, IReadOnlyList<Prisoner> prisoners)
        {
            List<Prisoner> targets = new();
            if (card == PatrolCard.NotWatching) return targets;
            if (card != PatrolCard.WatchAllCells) throw new System.ArgumentOutOfRangeException(nameof(card));
            foreach (Prisoner prisoner in prisoners)
            {
                if (!prisoner.IsOnBoard || grid.IsProtected(prisoner.Cell)) continue;
                targets.Add(prisoner);
            }
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
