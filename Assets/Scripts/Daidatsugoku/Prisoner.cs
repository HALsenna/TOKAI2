using UnityEngine;
using UnityEngine.EventSystems;

namespace Daidatsugoku
{
    public sealed class Prisoner : MonoBehaviour, IPointerClickHandler
    {
        public int Owner { get; private set; }
        public Vector2Int Cell { get; private set; }
        public bool Captured { get; private set; }
        public bool Escaped { get; private set; }
        public bool IsOnBoard => !Captured && !Escaped;
        private GameManager game;
        private Renderer body;
        private Color color;

        public void Initialize(GameManager manager, int owner, Vector2Int cell, Color tint)
        {
            game = manager; Owner = owner; Cell = cell; color = tint;
            body = GetComponent<Renderer>();
            body.sharedMaterial = manager.Grid.GetMaterial(tint);
            name = $"P{owner + 1}_Prisoner";
        }

        public void MoveTo(Vector2Int cell) => Cell = cell;
        public void SetSelected(bool value) => body.sharedMaterial = game.Grid.GetMaterial(value ? Color.white : color);
        public void Capture() { Captured = true; gameObject.SetActive(false); }
        public void Escape() { Escaped = true; gameObject.SetActive(false); }
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) game.OnPrisonerClicked(this);
        }
    }
}
