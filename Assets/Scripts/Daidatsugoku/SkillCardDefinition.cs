using UnityEngine;

namespace Daidatsugoku
{
    // Concrete card assets and their conditions come from the individual card specification.
    public abstract class SkillCardDefinition : ScriptableObject
    {
        public virtual string DisplayName => name;
        public virtual bool Immediate => false;
        public virtual int TargetPlayer(GameManager game, int owner) => -1;
        // Inspect all timing conditions, including whose turn it is. Null means free play;
        // otherwise trigger is the event being responded to (not this card's activation).
        public abstract bool CanPlay(GameManager game, int owner, TurnEvent trigger);
        // Receives this card's own SkillActivation. Parent identifies its response target.
        // Set Parent.Cancelled to cancel that event; cancelled activations are discarded without resolving.
        public abstract void Resolve(GameManager game, int owner, TurnEvent trigger);
    }
}
