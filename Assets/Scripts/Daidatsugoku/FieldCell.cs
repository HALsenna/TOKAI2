using System;

namespace Daidatsugoku
{
    /// <summary>SYS-FIELD-001 coordinate: origin at the lower left; X right, Y up.</summary>
    [Serializable]
    public struct FieldCell : IEquatable<FieldCell>
    {
        public int X;
        public int Y;

        public FieldCell(int x, int y) { X = x; Y = y; }
        public bool Equals(FieldCell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FieldCell other && Equals(other);
        public override int GetHashCode() { unchecked { return (X * 397) ^ Y; } }
        public override string ToString() => $"({X}, {Y})";
        public static bool operator ==(FieldCell left, FieldCell right) => left.Equals(right);
        public static bool operator !=(FieldCell left, FieldCell right) => !left.Equals(right);
    }

    /// <summary>A continuous inclusive span of existing cells on one Y coordinate.</summary>
    [Serializable]
    public struct FieldRow
    {
        public int Y;
        public int StartX;
        public int EndX;

        public FieldRow(int y, int startX, int endX) { Y = y; StartX = startX; EndX = endX; }
    }
}
