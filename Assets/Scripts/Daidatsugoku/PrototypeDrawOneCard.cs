using UnityEngine;

namespace Daidatsugoku
{
    /// <summary>Temporary, manually played card requested for prototype play.</summary>
    [CreateAssetMenu(menuName = "Daidatsugoku/Prototype Cards/Draw One")]
    public sealed class PrototypeDrawOneCard : SkillCardDefinition
    {
        public override string DisplayName => "仮：カードを1枚引く";

        public override bool CanPlay(GameManager game, int owner, TurnEvent trigger) =>
            game != null && trigger == null && game.IsPlayerTurn &&
            game.Phase == TurnPhase.PlayerInput && game.CurrentPlayer == owner;

        public override void Resolve(GameManager game, int owner, TurnEvent trigger) => game.DrawSkillCard(owner);
    }
}
