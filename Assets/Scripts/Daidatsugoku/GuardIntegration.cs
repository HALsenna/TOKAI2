namespace Daidatsugoku
{
    // SYS-VISION-001 / SYS-PRISONER-001 are not supplied. These interfaces keep
    // their rules outside SYS-GUARD-001; the defaults below are prototype adapters.
    public interface IGuardVisionService
    {
        bool IsVisible(GridManager grid, Prisoner prisoner);
    }

    public interface IGuardPrisonerService
    {
        void FailEscape(Prisoner prisoner);
    }

    public sealed class PrototypeGuardVisionService : IGuardVisionService
    {
        public bool IsVisible(GridManager grid, Prisoner prisoner) => !grid.IsProtected(prisoner.Cell);
    }

    public sealed class PrototypeGuardPrisonerService : IGuardPrisonerService
    {
        public void FailEscape(Prisoner prisoner) => prisoner.Capture();
    }
}
