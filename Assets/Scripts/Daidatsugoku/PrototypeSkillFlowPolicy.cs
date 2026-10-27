using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    /// <summary>Temporary no-interruption policy for the two explicitly requested prototype cards.</summary>
    [CreateAssetMenu(menuName = "Daidatsugoku/Prototype Cards/Flow Policy")]
    public sealed class PrototypeSkillFlowPolicy : TurnFlowPolicy
    {
        public override bool TryPlanResponses(GameManager game, TurnEvent trigger, IReadOnlyList<int> eligibleOwners,
            out ResponsePlan plan, out string reason)
        {
            plan = ResponsePlan.WithoutResponses;
            bool supported = trigger != null && trigger.Parent == null &&
                (trigger.Kind == TurnEventKind.NormalAction ||
                 trigger.Kind == TurnEventKind.SkillActivation &&
                 (trigger.Card is PrototypeDrawOneCard || trigger.Card is PrototypeFlipGuardCard));
            if (supported && eligibleOwners != null && eligibleOwners.Count == 0)
            {
                reason = null; return true;
            }
            reason = "仮カード用設定は通常行動と仮ドロー・看守反転の割り込みなし処理だけに対応しています。追加カードの対応順は正式仕様を設定してください。";
            return false;
        }
    }
}
