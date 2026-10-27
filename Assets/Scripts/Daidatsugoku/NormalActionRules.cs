using UnityEngine;

namespace Daidatsugoku
{
    public abstract class NormalActionRules : ScriptableObject
    {
        public abstract bool CanDeploy(GameManager game, int owner, Vector2Int cell);
        public abstract bool CanMove(GameManager game, Prisoner prisoner, Vector2Int destination);
        public abstract bool CanEscape(GameManager game, Prisoner prisoner);
        public abstract bool CanChangeFence(GameManager game, int owner, FenceBoundary boundary, bool placing);
    }

    // Field placement/occupancy constraints with the existing prototype movement directions.
    // The turn controller does not define coordinates, movement distance or sight geometry.
    public sealed class PrototypeActionQuery
    {
        private readonly GameManager game;
        public PrototypeActionQuery(GameManager game) { this.game = game; }
        public bool CanDeploy(int owner, Vector2Int cell) => game.Grid.CanPlacePrisoner(cell) && game.ReserveCount(owner) > 0;
        public bool CanMove(Prisoner prisoner, Vector2Int destination) => prisoner != null && prisoner.IsOnBoard && game.Grid.CanMove(prisoner.Cell, destination);
        public bool CanEscape(Prisoner prisoner) => prisoner != null && prisoner.IsOnBoard && game.Grid.IsExitCell(prisoner.Cell);
        public bool CanChangeFence(FenceBoundary boundary, bool placing) => game.Grid.IsValidBoundary(boundary)
            && (!placing || !game.Grid.IsFenceForbidden(new Vector2Int(boundary.Column, boundary.Line))) && game.Grid.HasFence(boundary) != placing;
    }
}
