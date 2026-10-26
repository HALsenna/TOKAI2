using UnityEngine;

namespace Daidatsugoku
{
    /// <summary>
    /// Field-owned vision configuration. No map size or starting position is
    /// assumed until a field asset explicitly supplies these values.
    /// </summary>
    [CreateAssetMenu(menuName = "Daidatsugoku/Guard Vision Field")]
    public sealed class GuardVisionFieldDefinition : ScriptableObject
    {
        [SerializeField, Min(1)] private int rowCount;
        [SerializeField, Min(1)] private int columnCount;
        [SerializeField, Min(0)] private int guardColumn;
        [SerializeField] private GuardSide guardSide;

        public int RowCount { get => rowCount; set => rowCount = value; }
        public int ColumnCount { get => columnCount; set => columnCount = value; }
        public int GuardColumn { get => guardColumn; set => guardColumn = value; }
        public GuardSide GuardSide { get => guardSide; set => guardSide = value; }

        public bool TryValidate(out string reason)
        {
            if (rowCount <= 0 || columnCount <= 0)
            {
                reason = "視界用フィールドの行数・列数は1以上に設定してください。"; return false;
            }
            if (guardColumn < 0 || guardColumn >= columnCount)
            {
                reason = "看守の初期基準列がフィールドの列範囲外です。"; return false;
            }
            if (guardSide != Daidatsugoku.GuardSide.Top && guardSide != Daidatsugoku.GuardSide.Bottom)
            {
                reason = "看守の初期方向はTOPまたはBOTTOMである必要があります。"; return false;
            }
            reason = null; return true;
        }
    }
}
