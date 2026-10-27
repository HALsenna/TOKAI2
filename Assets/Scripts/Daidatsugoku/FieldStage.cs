using System;
using System.Collections;
using System.Collections.Generic;

namespace Daidatsugoku
{
    public enum FieldMoveKind { Invalid, Land, Escape, ReturnToUnplaced }

    /// <summary>
    /// Immutable stage-data snapshot. No maximum dimensions are imposed as game rules.
    /// Invalid input is retained for validation and cannot be used through geometry queries.
    /// </summary>
    public sealed class FieldStage
    {
        public string StageId { get; }
        public string StageName { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<FieldRow> Rows { get; }
        public int GuardColumn { get; }
        public GuardSide GuardSide { get; }
        public bool InitialFencesDefined { get; }
        public IReadOnlyList<FieldCell> InitialFences { get; }

        private IReadOnlyList<string> validationErrors;
        private Dictionary<int, FieldRow> rowsByY;
        private IReadOnlyList<FieldRow> orderedRows;
        private IReadOnlyCollection<FieldCell> cells, startCells, exitCells, forbiddenCells;
        private long cellCount;

        public FieldStage(string stageId, string stageName, int width, int height,
            IEnumerable<FieldRow> rows, int guardColumn, GuardSide guardSide,
            bool initialFencesDefined, IEnumerable<FieldCell> initialFences)
        {
            StageId = stageId; StageName = stageName; Width = width; Height = height;
            Rows = rows == null ? null : new List<FieldRow>(rows).AsReadOnly();
            GuardColumn = guardColumn; GuardSide = guardSide; InitialFencesDefined = initialFencesDefined;
            // A missing confirmed list is distinct from an explicitly confirmed empty list.
            InitialFences = initialFences == null ? null : new List<FieldCell>(initialFences).AsReadOnly();
        }

        public long CellCount { get { EnsureGeometry(); return cellCount; } }
        public IReadOnlyCollection<FieldCell> Cells { get { EnsureGeometry(); return cells; } }
        public IReadOnlyCollection<FieldCell> StartCells { get { EnsureGeometry(); return startCells; } }
        public IReadOnlyCollection<FieldCell> ExitCells { get { EnsureGeometry(); return exitCells; } }
        public IReadOnlyCollection<FieldCell> FenceForbiddenCells { get { EnsureGeometry(); return forbiddenCells; } }

        public bool TryGetRow(int y, out FieldRow row)
        {
            EnsureGeometry(); return rowsByY.TryGetValue(y, out row);
        }

        public bool IsInside(FieldCell cell) => TryGetRow(cell.Y, out var row) && cell.X >= row.StartX && cell.X <= row.EndX;
        public bool IsStart(FieldCell cell) => TryGetRow(cell.Y, out var row) && cell.X == row.StartX;
        public bool IsExit(FieldCell cell) => TryGetRow(cell.Y, out var row) && cell.X == row.EndX;

        /// <summary>
        /// Classifies a proposed step after prisoner-specific direction/distance checks.
        /// Crossing the source row's right edge takes priority over a missing destination,
        /// including diagonal movement beyond the upper or lower outline.
        /// </summary>
        public FieldMoveKind ClassifyMove(FieldCell from, FieldCell to)
        {
            if (!IsInside(from)) return FieldMoveKind.Invalid;
            TryGetRow(from.Y, out var row);
            if (to.X > from.X && to.X > row.EndX) return FieldMoveKind.Escape;
            return IsInside(to) ? FieldMoveKind.Land : FieldMoveKind.Invalid;
        }

        /// <summary>Classifies a horizontal push; fight direction, distance and chaining belong to prisoners.</summary>
        public FieldMoveKind ClassifyPush(FieldCell from, FieldCell to)
        {
            if (!IsInside(from) || to.Y != from.Y) return FieldMoveKind.Invalid;
            TryGetRow(from.Y, out var row);
            if (to.X < row.StartX) return FieldMoveKind.ReturnToUnplaced;
            if (to.X > row.EndX) return FieldMoveKind.Escape;
            return IsInside(to) ? FieldMoveKind.Land : FieldMoveKind.Invalid;
        }

        private void EnsureGeometry()
        {
            if (rowsByY != null) return;
            if (validationErrors == null) validationErrors = FieldStageValidator.Validate(this);
            if (validationErrors.Count > 0)
                throw new InvalidOperationException("Invalid field stage: " + string.Join(" / ", validationErrors));

            var rowIndex = new Dictionary<int, FieldRow>();
            var sorted = new List<FieldRow>(Rows);
            sorted.Sort((left, right) => left.Y.CompareTo(right.Y));
            var starts = new List<FieldCell>(); var exits = new List<FieldCell>();
            long total = 0;
            foreach (FieldRow row in sorted)
            {
                rowIndex.Add(row.Y, row);
                total += (long)row.EndX - row.StartX + 1;
                starts.Add(new FieldCell(row.StartX, row.Y));
                exits.Add(new FieldCell(row.EndX, row.Y));
            }
            var forbidden = new List<FieldCell>();
            for (int offset = -1; offset <= 1; offset++)
            {
                int column = GuardColumn + offset;
                for (int i = sorted.Count - 1; i >= 0; i--)
                {
                    FieldRow row = sorted[i];
                    if (column < row.StartX || column > row.EndX) continue;
                    forbidden.Add(new FieldCell(column, row.Y)); break;
                }
            }
            cellCount = total; orderedRows = sorted.AsReadOnly();
            cells = new CellCollection(this);
            startCells = starts.AsReadOnly(); exitCells = exits.AsReadOnly(); forbiddenCells = forbidden.AsReadOnly();
            // Publish the fully constructed index last.
            rowsByY = rowIndex;
        }

        private sealed class CellCollection : IReadOnlyCollection<FieldCell>
        {
            private readonly FieldStage stage;
            public CellCollection(FieldStage stage) { this.stage = stage; }
            // IReadOnlyCollection uses an Int32 count. This is a .NET interface
            // representation limit, not a maximum stage size or validation rule.
            public int Count => checked((int)stage.cellCount);
            public IEnumerator<FieldCell> GetEnumerator()
            {
                foreach (FieldRow row in stage.orderedRows)
                    for (int x = row.StartX; x <= row.EndX; x++) yield return new FieldCell(x, row.Y);
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
