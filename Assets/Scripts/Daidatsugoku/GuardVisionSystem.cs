using System;
using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    /// <summary>
    /// Unity adapter for SYS-VISION-001. The pure calculator owns the rules;
    /// this adapter supplies the current field, physical cells and front fences.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GuardVisionSystem : MonoBehaviour, IGuardVisionService, IGuardVisionSnapshotService
    {
        [SerializeField] private GuardVisionFieldDefinition field;
        private GuardPosition position;
        public GuardVisionFieldDefinition Field { get => field; set => field = value; }
        // A caller cannot bypass Flip's display/event updates by mutating this snapshot.
        public GuardPosition Position => position == null ? null : new GuardPosition(position.Column, position.Side);
        public event Action PositionChanged;

        public bool TryResetPosition(out string reason)
        {
            position = null;
            GetComponent<GridManager>()?.HideGuardPosition();
            if (!TryValidateField(out reason)) return false;
            position = new GuardPosition(field.GuardColumn, field.GuardSide);
            UpdatePositionDisplay(); PositionChanged?.Invoke(); return true;
        }

        public void Flip()
        {
            if (position == null) throw new InvalidOperationException("看守の初期位置が未設定です。視界用フィールドを設定して初期化してください。");
            position.Flip();
            UpdatePositionDisplay(); PositionChanged?.Invoke();
        }

        public IReadOnlyCollection<VisionCell> CalculateVisibleCells(GridManager grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (position == null) throw new InvalidOperationException("看守の位置を初期化してから視界を計算してください。");
            if (!TryValidateField(out string reason)) throw new InvalidOperationException(reason);
            var visible = GuardVisionCalculator.CalculateVisibleCells(field.RowCount, field.ColumnCount,
                position.Column, position.Side, grid.GetVisionFrontFences());
            // Prototype bridge: the current board is a stepped 25-cell map in
            // a 7x5 global coordinate envelope. Empty positions are not cells.
            visible.RemoveWhere(cell => !grid.TryGetCell(cell.Column, cell.Row, out _));
            return new List<VisionCell>(visible).AsReadOnly();
        }

        public bool IsVisible(GridManager grid, Prisoner prisoner)
        {
            if (grid == null || prisoner == null || !prisoner.IsOnBoard || !grid.IsInside(prisoner.Cell)) return false;
            var cell = new VisionCell(prisoner.Cell.y, grid.GlobalColumn(prisoner.Cell));
            foreach (VisionCell visible in CalculateVisibleCells(grid)) if (visible.Equals(cell)) return true;
            return false;
        }

        private bool TryValidateField(out string reason)
        {
            if (field == null) { reason = "SYS-VISION-001に使用するフィールドデータが未設定です。"; return false; }
            if (!field.TryValidate(out reason)) return false;
            if (field.RowCount != GridManager.RowCount || field.ColumnCount != GridManager.Width)
            {
                reason = "視界用フィールドの寸法が既存プロトタイプ盤面の全体座標範囲（7列×5行）と一致しません。フィールドアダプターを確認してください。";
                return false;
            }
            reason = null; return true;
        }

        private void UpdatePositionDisplay()
        {
            GridManager grid = GetComponent<GridManager>();
            if (grid != null) grid.UpdateGuardPosition(position.Column, position.Side);
        }
    }
}
