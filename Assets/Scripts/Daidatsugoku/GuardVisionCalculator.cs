using System;
using System.Collections.Generic;

namespace Daidatsugoku
{
    /// <summary>SYS-VISION-001 v1.0 geometry, independent of Unity, rendering and prisoner state.</summary>
    public static class GuardVisionCalculator
    {
        public static HashSet<VisionCell> CalculateBaseVisibleCells(int rowCount, int columnCount,
            int guardColumn, GuardSide side)
        {
            if (rowCount <= 0) throw new ArgumentOutOfRangeException(nameof(rowCount));
            if (columnCount <= 0) throw new ArgumentOutOfRangeException(nameof(columnCount));
            if (guardColumn < 0 || guardColumn >= columnCount) throw new ArgumentOutOfRangeException(nameof(guardColumn));
            if (side != GuardSide.Top && side != GuardSide.Bottom) throw new ArgumentOutOfRangeException(nameof(side));

            var visible = new HashSet<VisionCell>();
            for (int row = 0; row < rowCount; row++)
            {
                int distance = side == GuardSide.Top ? row + 1 : rowCount - row;
                int range = (distance - 1) / 3 + 1;
                int minColumn = Math.Max(0, guardColumn - range);
                int maxColumn = (int)Math.Min(columnCount - 1, (long)guardColumn + range);
                for (int column = minColumn; column <= maxColumn; column++)
                    visible.Add(new VisionCell(row, column));
            }
            return visible;
        }

        public static HashSet<VisionCell> CalculateVisibleCells(int rowCount, int columnCount,
            int guardColumn, GuardSide side, IEnumerable<VisionCell> fencePositions)
        {
            if (fencePositions == null) throw new ArgumentNullException(nameof(fencePositions));
            HashSet<VisionCell> visible = CalculateBaseVisibleCells(rowCount, columnCount, guardColumn, side);
            var nearestFenceRows = new Dictionary<int, int>();
            foreach (VisionCell fence in fencePositions)
            {
                // The complete base set must be established before filtering fences.
                // An off-cone fence cannot shade a deeper cell that enters the cone.
                if (!visible.Contains(fence)) continue;
                if (!nearestFenceRows.TryGetValue(fence.Column, out int previous) ||
                    (side == GuardSide.Top ? fence.Row < previous : fence.Row > previous))
                    nearestFenceRows[fence.Column] = fence.Row;
            }
            visible.RemoveWhere(cell => nearestFenceRows.TryGetValue(cell.Column, out int fenceRow) &&
                (side == GuardSide.Top ? cell.Row >= fenceRow : cell.Row < fenceRow));
            return visible;
        }
    }
}
