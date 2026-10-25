using UnityEngine;

namespace Daidatsugoku
{
    /// <summary>
    /// Optional source override for explicit test fixtures or future integrations.
    /// When absent, GuardSystem uses the SYS-GUARD-001 v1.2 thirty-card deck.
    /// GuardSystem records public history and prevents duplicate round consumption.
    /// </summary>
    public abstract class GuardCardSource : ScriptableObject
    {
        public abstract void ResetSession();
        public abstract bool TryDraw(int round, out PatrolCard card, out string reason);
    }
}
