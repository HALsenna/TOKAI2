using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public enum PatrolCard { WatchAllCells = 0, NotWatching = 1 }

    public sealed class GuardSystem : MonoBehaviour
    {
        private readonly List<GuardHistoryEntry> history = new();
        private readonly Dictionary<int, PatrolCard> revealedRounds = new();
        private GuardMonitoringDeck deck;
        private GuardCardSource sessionSource;
        private bool sourceLocked;
        private int lastRound;
        [SerializeField] private GuardCardSource monitoringSource;
        // Only already revealed results cross the public/UI boundary.
        public IReadOnlyList<GuardHistoryEntry> PublicHistory => history.AsReadOnly();
        public IGuardVisionService VisionService { get; set; }
        public IGuardPrisonerService PrisonerService { get; set; } = new PrototypeGuardPrisonerService();
        public event System.Action<IReadOnlyList<Prisoner>> PrisonersDiscovered;

        private void Awake()
        {
            VisionService = GetComponent<GuardVisionSystem>() ?? gameObject.AddComponent<GuardVisionSystem>();
        }
        public GuardCardSource MonitoringSource
        {
            get => monitoringSource;
            // After a successful draw, configuration changes apply on ResetDeck.
            set => monitoringSource = value;
        }

        public void ResetDeck()
        {
            history.Clear(); revealedRounds.Clear(); lastRound = 0; sourceLocked = false; sessionSource = null;
            if (deck == null) deck = new GuardMonitoringDeck();
            else deck.ResetSession();
            monitoringSource?.ResetSession();
        }
        public bool TryDrawMonitoring(int round, out PatrolCard card, out string reason)
        {
            reason = null;
            if (revealedRounds.TryGetValue(round, out card)) return true;
            if (round < 1 || round <= lastRound)
            {
                card = default;
                reason = "監視ラウンドは1以上で、確定済みラウンドより後である必要があります。";
                return false;
            }
            var source = sourceLocked ? sessionSource : monitoringSource;
            if (source != null)
            {
                if (!source.TryDraw(round, out card, out reason)) return false;
            }
            else
            {
                if (deck == null) deck = new GuardMonitoringDeck();
                card = deck.Draw();
            }
            if (card != PatrolCard.WatchAllCells && card != PatrolCard.NotWatching)
            {
                reason = "監視カード供給元が不正なカード種別を返しました。";
                return false;
            }
            int index = history.Count;
            history.Add(new GuardHistoryEntry(index + 1, index / GuardMonitoringDeck.CardsPerDeck + 1,
                index % GuardMonitoringDeck.CardsPerDeck + 1, card));
            sessionSource = source; sourceLocked = true;
            revealedRounds.Add(round, card); lastRound = round;
            return true;
        }

        public string GetCardName(PatrolCard card) => card switch
        {
            PatrolCard.WatchAllCells => "見とる",
            PatrolCard.NotWatching => "見とらん",
            _ => throw new System.ArgumentOutOfRangeException(nameof(card))
        };

        public List<Prisoner> FindTargets(PatrolCard card, GridManager grid, IReadOnlyList<Prisoner> prisoners, bool monitoringEnabled = true)
        {
            List<Prisoner> targets = new();
            if (!monitoringEnabled || card == PatrolCard.NotWatching) return targets;
            if (card != PatrolCard.WatchAllCells) throw new System.ArgumentOutOfRangeException(nameof(card));
            HashSet<VisionCell> visible = VisionService is IGuardVisionSnapshotService snapshot
                ? new HashSet<VisionCell>(snapshot.CalculateVisibleCells(grid)) : null;
            foreach (Prisoner prisoner in prisoners)
            {
                if (!prisoner.IsOnBoard || !grid.IsInside(prisoner.Cell)) continue;
                bool inSight = visible != null ? visible.Contains(grid.ToVisionCell(prisoner.Cell))
                    : VisionService.IsVisible(grid, prisoner);
                if (!inSight) continue;
                targets.Add(prisoner);
            }
            return targets;
        }

        public int Resolve(PatrolCard card, GridManager grid, IReadOnlyList<Prisoner> prisoners)
        {
            return ApplyMonitoringFailure(FindTargets(card, grid, prisoners));
        }

        public int ApplyMonitoringFailure(IReadOnlyList<Prisoner> targets)
        {
            List<Prisoner> discovered = new();
            HashSet<Prisoner> unique = new();
            foreach (Prisoner prisoner in targets)
            {
                if (prisoner.IsOnBoard && unique.Add(prisoner)) discovered.Add(prisoner);
            }
            if (discovered.Count == 0) return 0;
            var notification = discovered.AsReadOnly();
            // The complete set is published together before state transitions.
            PrisonersDiscovered?.Invoke(notification);
            if (PrisonerService is IGuardPrisonerBatchService batch) batch.FailEscape(notification);
            else foreach (Prisoner prisoner in notification) PrisonerService.FailEscape(prisoner);
            return discovered.Count;
        }
    }
}
