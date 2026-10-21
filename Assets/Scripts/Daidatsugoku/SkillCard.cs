using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public enum FlowEvent { FreePlay, NormalAction, SkillCard, GuardCapture }
    public enum TurnPhase { Draw, CardResolution, ActionWait, ChainResolution, ActionCompleted, TurnEnd }

    // Dedicated card specifications supply concrete assets; this layer invents no card effects.
    public abstract class SkillCard : ScriptableObject
    {
        public virtual string DisplayName => name;
        public virtual bool Immediate => false;
        public virtual int TargetPlayer(GameManager game, int owner) => -1;
        public abstract bool CanPlay(GameManager game, int owner, FlowContext context);
        public abstract void Resolve(GameManager game, int owner, FlowContext context);
    }

    public sealed class FlowContext
    {
        public FlowEvent Event { get; internal set; }
        public FlowEvent OriginalEvent { get; }
        public int Actor { get; }
        public int Target { get; }
        public PlayerAction? Action { get; }
        public Prisoner Prisoner { get; }
        public Vector2Int? Destination { get; }
        public FenceBoundary? Boundary { get; }
        public IReadOnlyList<Prisoner> CaptureTargets { get; }
        public bool Cancelled { get; set; }
        public FlowContext(FlowEvent kind, int actor, int target = -1, PlayerAction? action = null, Prisoner prisoner = null,
            Vector2Int? destination = null, FenceBoundary? boundary = null, IReadOnlyList<Prisoner> captureTargets = null)
        {
            Event = OriginalEvent = kind; Actor = actor; Target = target; Action = action; Prisoner = prisoner;
            Destination = destination; Boundary = boundary; CaptureTargets = captureTargets;
        }
    }
}
