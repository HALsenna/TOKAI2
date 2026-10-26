using System.Collections.Generic;

namespace Daidatsugoku
{
    // Vision uses SYS-VISION-001. Prisoner state remains a separate service.
    public interface IGuardVisionService
    {
        bool IsVisible(GridManager grid, Prisoner prisoner);
    }

    public interface IGuardPrisonerService
    {
        void FailEscape(Prisoner prisoner);
    }

    // One snapshot per discovery, rather than recalculating once per prisoner.
    public interface IGuardVisionSnapshotService : IGuardVisionService
    {
        IReadOnlyCollection<VisionCell> CalculateVisibleCells(GridManager grid);
    }

    public interface IGuardPrisonerBatchService : IGuardPrisonerService
    {
        void FailEscape(IReadOnlyList<Prisoner> prisoners);
    }

    public sealed class PrototypeGuardVisionService : IGuardVisionService
    {
        public bool IsVisible(GridManager grid, Prisoner prisoner) => !grid.IsProtected(prisoner.Cell);
    }

    public sealed class PrototypeGuardPrisonerService : IGuardPrisonerBatchService
    {
        public void FailEscape(Prisoner prisoner) => prisoner.Capture();
        public void FailEscape(IReadOnlyList<Prisoner> prisoners)
        {
            foreach (Prisoner prisoner in prisoners) prisoner.Capture();
        }
    }
}
