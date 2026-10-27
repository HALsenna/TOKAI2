using UnityEngine;

namespace Daidatsugoku
{
    /// <summary>Temporary card that flips the one existing guard without monitoring.</summary>
    [CreateAssetMenu(menuName = "Daidatsugoku/Prototype Cards/Flip Guard")]
    public sealed class PrototypeFlipGuardCard : SkillCardDefinition
    {
        public override string DisplayName => "仮：看守上下反転";

        public override bool CanPlay(GameManager game, int owner, TurnEvent trigger) =>
            game != null && trigger == null && game.IsPlayerTurn &&
            game.Phase == TurnPhase.PlayerInput && game.CurrentPlayer == owner &&
            game.Vision != null && game.Vision.Position != null;

        public override void Resolve(GameManager game, int owner, TurnEvent trigger) => game.Vision.Flip();
    }
}
