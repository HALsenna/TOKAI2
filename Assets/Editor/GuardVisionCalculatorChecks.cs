using System;
using System.Collections.Generic;
using System.Linq;
using Daidatsugoku;

/// <summary>Deterministic geometry tests for SYS-VISION-001 v1.0; no scene or engine dependency.</summary>
public static class GuardVisionCalculatorChecks
{
    public static int Run()
    {
        int assertions = 0;
        Action<bool, string> require = (condition, message) =>
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("SYS-VISION-001: " + message);
        };
        var empty = Array.Empty<VisionCell>();
        var expectedExample = new HashSet<VisionCell>();
        for (int row = 0; row <= 2; row++) foreach (int column in new[] { 2, 3, 4 }) expectedExample.Add(new VisionCell(row, column));
        for (int row = 3; row <= 4; row++) foreach (int column in new[] { 1, 2, 3, 4, 5 }) expectedExample.Add(new VisionCell(row, column));
        var example = GuardVisionCalculator.CalculateBaseVisibleCells(5, 6, 3, GuardSide.Top);
        require(example.Count == 19 && example.SetEquals(expectedExample), "The six-column, five-row TOP example must contain its exact nineteen cells.");
        require(GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, empty).SetEquals(example), "An empty fence set must preserve base vision.");
        var bottomExample = GuardVisionCalculator.CalculateBaseVisibleCells(5, 6, 3, GuardSide.Bottom);
        require(bottomExample.SetEquals(example.Select(cell => new VisionCell(4 - cell.Row, cell.Column))), "BOTTOM base vision must mirror TOP along rows.");

        int[] widths = { 3, 3, 3, 5, 5, 5, 7, 7, 7, 9, 9, 9, 11, 11, 11 };
        foreach (GuardSide side in new[] { GuardSide.Top, GuardSide.Bottom })
        {
            var cells = GuardVisionCalculator.CalculateBaseVisibleCells(15, 21, 10, side);
            for (int distance = 1; distance <= widths.Length; distance++)
            {
                int row = side == GuardSide.Top ? distance - 1 : 15 - distance;
                int[] columns = cells.Where(cell => cell.Row == row).Select(cell => cell.Column).OrderBy(value => value).ToArray();
                int half = widths[distance - 1] / 2;
                require(columns.SequenceEqual(Enumerable.Range(10 - half, widths[distance - 1])), "Vision must widen every three cells, including beyond twelve cells.");
            }
        }
        var left = GuardVisionCalculator.CalculateBaseVisibleCells(15, 6, 0, GuardSide.Top);
        var right = GuardVisionCalculator.CalculateBaseVisibleCells(15, 6, 5, GuardSide.Top);
        require(left.All(cell => cell.Row >= 0 && cell.Row < 15 && cell.Column >= 0 && cell.Column < 6), "Vision must exclude every off-board coordinate.");
        require(right.SetEquals(left.Select(cell => new VisionCell(cell.Row, 5 - cell.Column))), "Clipping must be symmetric at the left and right board edges.");
        require(GuardVisionCalculator.CalculateBaseVisibleCells(15, 1, 0, GuardSide.Bottom).Count == 15, "A one-column board must contain one unique visible coordinate per row.");

        var offCone = new[] { new VisionCell(1, 1) };
        require(GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, offCone).SetEquals(example), "An off-cone fence must not block deeper cells entering the cone.");
        var topFence = new[] { new VisionCell(3, 1) };
        var shadedTop = GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, topFence);
        require(shadedTop.Count == 17 && !shadedTop.Contains(new VisionCell(3, 1)) && !shadedTop.Contains(new VisionCell(4, 1)), "TOP must hide the fence's own cell and deeper cells in its column.");
        require(shadedTop.Where(cell => cell.Column != 1).ToHashSet().SetEquals(example.Where(cell => cell.Column != 1)), "A fence must not shade an adjacent column.");
        var shadedBottom = GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Bottom, new[] { new VisionCell(1, 3) });
        require(shadedBottom.Contains(new VisionCell(1, 3)) && !shadedBottom.Contains(new VisionCell(0, 3)), "BOTTOM must keep the fence's own cell and hide only cells above it.");

        var topFences = new List<VisionCell> { new(3, 3), new(0, 3) };
        var nearestTop = GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, topFences);
        require(nearestTop.All(cell => cell.Column != 3), "TOP must use the nearest valid fence when several share a column.");
        topFences.Remove(new VisionCell(0, 3));
        var afterTopRemoval = GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, topFences);
        require(afterTopRemoval.Where(cell => cell.Column == 3).Select(cell => cell.Row).OrderBy(value => value).SequenceEqual(new[] { 0, 1, 2 }), "After destroying the nearest TOP fence, the next valid fence must determine shading.");
        topFences.Clear();
        require(GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, topFences).SetEquals(example), "Destroying all fences must restore base vision on the next calculation.");

        var bottomFences = new List<VisionCell> { new(1, 3), new(4, 3) };
        var nearestBottom = GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Bottom, bottomFences);
        require(nearestBottom.Where(cell => cell.Column == 3).Select(cell => cell.Row).SequenceEqual(new[] { 4 }), "BOTTOM must use the greatest valid fence row and preserve that row.");
        bottomFences.Remove(new VisionCell(4, 3));
        var afterBottomRemoval = GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Bottom, bottomFences);
        require(afterBottomRemoval.Where(cell => cell.Column == 3).Select(cell => cell.Row).OrderBy(value => value).SequenceEqual(new[] { 1, 2, 3, 4 }), "After destroying the nearest BOTTOM fence, the next valid fence must determine shading.");
        require(afterBottomRemoval.Count > nearestBottom.Count, "A fresh calculation must reflect the latest fence set rather than a cached prior result.");

        var noisyFences = new[] { new VisionCell(-1, 3), new VisionCell(5, 3), new VisionCell(2, 6), new VisionCell(3, 1), new VisionCell(3, 1), new VisionCell(1, 1) };
        var noisyBefore = noisyFences.ToArray();
        require(GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, noisyFences).SetEquals(shadedTop), "Invalid, duplicate and off-cone fence coordinates must not add shading.");
        require(GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, noisyFences.Reverse()).SetEquals(shadedTop), "Fence enumeration order must not change final vision.");
        require(noisyFences.SequenceEqual(noisyBefore), "Calculating vision must not mutate caller-owned fence input.");
        var fresh = GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, empty); fresh.Clear();
        require(GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, empty).SetEquals(example), "Returned sets must not expose a cached calculation used by future calls.");

        var position = new GuardPosition(3, GuardSide.Top);
        for (int flip = 1; flip <= 8; flip++)
        {
            position.Flip();
            require(position.Column == 3 && position.Side == (flip % 2 == 1 ? GuardSide.Bottom : GuardSide.Top), "Each flip must preserve the column and persist its alternating side.");
        }
        require(new VisionCell(1, 2) == new VisionCell(1, 2) && new VisionCell(1, 2) != new VisionCell(2, 1), "Logical row/column equality must preserve coordinate orientation.");
        require(new HashSet<VisionCell> { new(1, 2), new(1, 2) }.Count == 1, "Coordinate sets must eliminate duplicates.");

        require(Throws<ArgumentOutOfRangeException>(() => GuardVisionCalculator.CalculateBaseVisibleCells(0, 6, 3, GuardSide.Top)), "Reject a zero-height field.");
        require(Throws<ArgumentOutOfRangeException>(() => GuardVisionCalculator.CalculateBaseVisibleCells(5, 0, 0, GuardSide.Top)), "Reject a zero-width field.");
        require(Throws<ArgumentOutOfRangeException>(() => GuardVisionCalculator.CalculateBaseVisibleCells(5, 6, -1, GuardSide.Top)), "Reject a negative guard column.");
        require(Throws<ArgumentOutOfRangeException>(() => GuardVisionCalculator.CalculateBaseVisibleCells(5, 6, 6, GuardSide.Top)), "Reject a guard column beyond the field.");
        require(Throws<ArgumentOutOfRangeException>(() => GuardVisionCalculator.CalculateBaseVisibleCells(5, 6, 3, (GuardSide)2)), "Reject an undefined guard side.");
        require(Throws<ArgumentNullException>(() => GuardVisionCalculator.CalculateVisibleCells(5, 6, 3, GuardSide.Top, null)), "Reject missing fence data rather than guessing it.");
        require(Throws<ArgumentOutOfRangeException>(() => new GuardPosition(-1, GuardSide.Top)), "Reject a negative stored guard column.");
        require(Throws<ArgumentOutOfRangeException>(() => new GuardPosition(0, (GuardSide)2)), "Reject an undefined stored guard side.");
        return assertions;
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}
