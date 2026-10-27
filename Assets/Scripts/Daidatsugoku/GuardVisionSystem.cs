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
        private GuardPosition position;
        private FieldStage initializedStage;
        // A caller cannot bypass Flip's display/event updates by mutating this snapshot.
        public GuardPosition Position => position == null ? null : new GuardPosition(position.Column, position.Side);
        public event Action PositionChanged;

        public bool TryResetPosition(out string reason)
        {
            position = null;
            initializedStage = null;
            GridManager grid = GetComponent<GridManager>();
            grid?.HideGuardPosition();
            if (!TryValidateField(grid, out FieldStage stage, out reason)) return false;
            position = new GuardPosition(stage.GuardColumn, stage.GuardSide);
            initializedStage = stage;
            UpdatePositionDisplay(); PositionChanged?.Invoke(); return true;
        }

        public void Flip()
        {
            if (position == null || !ReferenceEquals(initializedStage, GetComponent<GridManager>()?.Field))
                throw new InvalidOperationException("看守の初期位置が未設定、またはステージが変更されています。選択ステージで初期化してください。");
            position.Flip();
            UpdatePositionDisplay(); PositionChanged?.Invoke();
        }

        public IReadOnlyCollection<VisionCell> CalculateVisibleCells(GridManager grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (position == null) throw new InvalidOperationException("看守の位置を初期化してから視界を計算してください。");
            if (!TryValidateField(grid, out FieldStage stage, out string reason)) throw new InvalidOperationException(reason);
            if (!ReferenceEquals(stage, initializedStage))
                throw new InvalidOperationException("選択ステージが変更されています。看守位置を初期化してから視界を計算してください。");
            var visible = GuardVisionCalculator.CalculateVisibleCells(stage.Height, stage.Width,
                position.Column, position.Side, grid.GetVisionFrontFences());
            // The geometry engine counts rows down from TOP. Field XY counts up
            // from the bottom-left, and only cells in the selected stage exist.
            visible.RemoveWhere(cell => !grid.TryGetVisionCell(cell.Column, cell.Row, out _));
            return new List<VisionCell>(visible).AsReadOnly();
        }

        public bool IsVisible(GridManager grid, Prisoner prisoner)
        {
            if (grid == null || prisoner == null || !prisoner.IsOnBoard || !grid.IsInside(prisoner.Cell)) return false;
            VisionCell cell = grid.ToVisionCell(prisoner.Cell);
            foreach (VisionCell visible in CalculateVisibleCells(grid)) if (visible.Equals(cell)) return true;
            return false;
        }

        private bool TryValidateField(GridManager grid, out FieldStage stage, out string reason)
        {
            stage = grid?.Field;
            if (grid == null || grid != GetComponent<GridManager>())
            {
                reason = "看守と同じフィールドの盤面情報が必要です。";
                return false;
            }
            IReadOnlyList<string> errors = FieldStageValidator.Validate(stage);
            if (errors.Count > 0) { reason = string.Join(" / ", errors); return false; }
            reason = null; return true;
        }

        private void UpdatePositionDisplay()
        {
            GridManager grid = GetComponent<GridManager>();
            if (grid != null) grid.UpdateGuardPosition(position.Column, position.Side);
        }
    }
}
