using UnityEngine;
using UnityEngine.EventSystems;

namespace Daidatsugoku
{
    public sealed class FenceClick : MonoBehaviour, IPointerClickHandler
    {
        private GameManager game;
        private FenceBoundary boundary;
        public void Initialize(GameManager manager, FenceBoundary value) { game = manager; boundary = value; }
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) game.OnBoundaryClicked(boundary);
        }
    }
}
