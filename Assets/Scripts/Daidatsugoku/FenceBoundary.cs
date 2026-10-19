using System;

namespace Daidatsugoku
{
    public enum CellEdge { North, East, South, West }

    // Canonical global coordinates: adjacent cells share the same boundary key.
    [Serializable]
    public struct FenceBoundary : IEquatable<FenceBoundary>
    {
        public int Column;
        public int Line;
        public bool Vertical;
        public FenceBoundary(int column, int line, bool vertical = false)
        { Column = column; Line = line; Vertical = vertical; }
        public bool Equals(FenceBoundary other) => Column == other.Column && Line == other.Line && Vertical == other.Vertical;
        public override bool Equals(object obj) => obj is FenceBoundary other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Column, Line, Vertical);
        public override string ToString() => $"{(Vertical ? "V" : "H")}_{Column}_{Line}";
    }

}
