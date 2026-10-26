using System;

namespace Daidatsugoku
{
    public enum GuardSide { Top = 0, Bottom = 1 }

    /// <summary>Guard position state only. Flipping never calculates vision or discovers prisoners.</summary>
    public sealed class GuardPosition
    {
        public int Column { get; }
        public GuardSide Side { get; private set; }

        public GuardPosition(int column, GuardSide side)
        {
            if (column < 0) throw new ArgumentOutOfRangeException(nameof(column));
            if (side != GuardSide.Top && side != GuardSide.Bottom) throw new ArgumentOutOfRangeException(nameof(side));
            Column = column; Side = side;
        }

        public void Flip() => Side = Side == GuardSide.Top ? GuardSide.Bottom : GuardSide.Top;
    }
}
