using UnityEngine;

namespace Daidatsugoku
{
    // SYS-PRISONER-001 is awaiting approval. The field reports collisions without
    // inventing push probabilities, ability effects, or a second occupant.
    public interface IPrisonerCollisionResolver
    {
        bool TryResolve(GameManager game, Prisoner incoming, Prisoner stationary,
            Vector2Int destination, out string reason);
    }
}
