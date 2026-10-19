using UnityEngine;
using UnityEngine.EventSystems;

namespace Daidatsugoku
{
    public sealed class GridCell : MonoBehaviour, IPointerClickHandler
    {
        public Vector2Int Cell { get; private set; }
        private GameManager game;
        public void Initialize(GameManager manager, Vector2Int cell) { game = manager; Cell = cell; }
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left)
                game.OnCellClicked(Cell, game.Grid.BoundaryAtPoint(Cell, e.pointerCurrentRaycast.worldPosition));
        }
    }
}
