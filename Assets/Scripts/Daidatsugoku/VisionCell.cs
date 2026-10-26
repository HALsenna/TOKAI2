using System;

namespace Daidatsugoku
{
    /// <summary>Zero-based logical field coordinate; rows increase from top to bottom.</summary>
    public readonly struct VisionCell : IEquatable<VisionCell>
    {
        public int Row { get; }
        public int Column { get; }

        public VisionCell(int row, int column) { Row = row; Column = column; }
        public bool Equals(VisionCell other) => Row == other.Row && Column == other.Column;
        public override bool Equals(object obj) => obj is VisionCell other && Equals(other);
        public override int GetHashCode() { unchecked { return (Row * 397) ^ Column; } }
        public override string ToString() => $"({Row}, {Column})";
        public static bool operator ==(VisionCell left, VisionCell right) => left.Equals(right);
        public static bool operator !=(VisionCell left, VisionCell right) => !left.Equals(right);
    }
}
