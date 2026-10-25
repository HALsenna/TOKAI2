using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    // There is intentionally no default participant/priority rule for undefined events.
    public abstract class TurnFlowPolicy : ScriptableObject
    {
        public abstract bool TryPlanResponses(GameManager game, TurnEvent trigger, IReadOnlyList<int> eligibleOwners,
            out ResponsePlan plan, out string reason);
        public virtual bool TryOrderGuardResponses(IReadOnlyList<int> owners, out int[] order)
        {
            order = owners.Count == 1 ? new[] { owners[0] } : null;
            return order != null;
        }
        public virtual bool TryChooseDiscardOwner(IReadOnlyList<int> owners, out int owner)
        {
            owner = owners.Count == 1 ? owners[0] : -1;
            return owner >= 0;
        }
    }
}
