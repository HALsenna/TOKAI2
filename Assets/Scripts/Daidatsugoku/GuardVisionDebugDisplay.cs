using System;
using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    /// <summary>
    /// Editor/development inspection only. Refresh explicitly after changing
    /// fences or the guard; drawing a cached snapshot never performs discovery.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GuardVisionDebugDisplay : MonoBehaviour
    {
        [SerializeField] private bool showVision;
        [SerializeField] private Color visibleColor = new(.2f, .85f, 1f, .8f);
        [SerializeField] private Color fenceColor = new(1f, .65f, .15f, 1f);
        private IReadOnlyCollection<VisionCell> visibleCells = Array.Empty<VisionCell>();
        private IReadOnlyCollection<VisionCell> fenceCells = Array.Empty<VisionCell>();
        private GridManager grid;
        private Vector3 guardPosition;
        private string guardLabel = "";

        public bool ShowVision { get => showVision; set => showVision = value; }
        public IReadOnlyCollection<VisionCell> VisibleCells => visibleCells;
        public IReadOnlyCollection<VisionCell> FenceCells => fenceCells;

        [ContextMenu("Refresh Vision Debug Snapshot")]
        public void RefreshDebugView()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            grid = GetComponent<GridManager>();
            GuardVisionSystem vision = GetComponent<GuardVisionSystem>();
            if (grid == null || vision == null || vision.Position == null)
                throw new InvalidOperationException("視界デバッグ表示には初期化済みの盤面と看守位置が必要です。");
            visibleCells = vision.CalculateVisibleCells(grid);
            fenceCells = grid.GetVisionFrontFences();
            guardPosition = grid.GuardWorldPosition(vision.Position.Column, vision.Position.Side);
            guardLabel = $"看守 {vision.Position.Side.ToString().ToUpperInvariant()} / 列 {vision.Position.Column}";
#endif
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!showVision || grid == null) return;
            Gizmos.color = visibleColor;
            foreach (VisionCell cell in visibleCells)
                if (grid.TryGetCell(cell.Column, cell.Row, out Vector2Int local))
                    Gizmos.DrawWireCube(grid.WorldPosition(local) + Vector3.up * .18f, new Vector3(1.35f, .12f, 1.35f));
            Gizmos.color = fenceColor;
            foreach (VisionCell cell in fenceCells)
                if (grid.TryGetCell(cell.Column, cell.Row, out Vector2Int local))
                    Gizmos.DrawWireCube(grid.WorldPosition(local) + new Vector3(0, .35f, .8f), new Vector3(1.45f, .45f, .12f));
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(guardPosition + Vector3.up * .3f, new Vector3(.8f, .6f, .8f));
#if UNITY_EDITOR
            UnityEditor.Handles.Label(guardPosition + Vector3.up * .8f, guardLabel);
#endif
#endif
        }
    }
}
