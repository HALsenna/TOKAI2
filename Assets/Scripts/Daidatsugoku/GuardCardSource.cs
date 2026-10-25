using UnityEngine;

namespace Daidatsugoku
{
    /// <summary>
    /// Supplies monitoring cards and owns their generation, draw rules and history.
    /// A concrete source must follow SYS-GUARD-001 v1.1; the game flow does not
    /// choose a monitoring probability or substitute the legacy prototype deck.
    /// </summary>
    public abstract class GuardCardSource : ScriptableObject
    {
        public abstract void ResetSession();
        public abstract bool TryDraw(int round, out PatrolCard card, out string reason);
    }
}
