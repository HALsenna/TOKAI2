using UnityEngine;

namespace Daidatsugoku
{
    // Integration point for prisoner/fence specifications and ongoing card restrictions.
    public abstract class FlowRules : ScriptableObject
    {
        public virtual bool AllowsAction(GameManager game, int owner, PlayerAction action) => true;
        public virtual bool AllowsDeployment(GameManager game, int owner, Vector2Int cell) => true;
        public virtual bool AllowsMovement(GameManager game, Prisoner prisoner, Vector2Int destination) => true;
        public virtual bool AllowsFence(GameManager game, int owner, FenceBoundary boundary, bool placing) => true;
    }
}
