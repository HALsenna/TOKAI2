using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public enum TurnPhase { Setup, Draw, ChooseDiscard, PlayerInput, Response, Resolve, GuardReveal, GuardResponse, Handoff, WaitingForSpecification, Finished }
    public enum TurnEventKind { NormalAction, SkillActivation, GuardCapture, GuardMonitoring }

    public sealed class TurnEvent
    {
        public TurnEventKind Kind { get; }
        public int Actor { get; }
        public int Target { get; }
        public PlayerAction? Action { get; }
        public Prisoner Prisoner { get; }
        public Vector2Int? Destination { get; }
        public FenceBoundary? Boundary { get; }
        public IReadOnlyList<Prisoner> CaptureTargets { get; }
        public SkillCardDefinition Card { get; }
        public TurnEvent Parent { get; }
        public PatrolCard? MonitoringResult { get; }
        public bool Cancelled { get; set; }

        public TurnEvent(TurnEventKind kind, int actor, int target = -1, PlayerAction? action = null,
            Prisoner prisoner = null, Vector2Int? destination = null, FenceBoundary? boundary = null,
            IReadOnlyList<Prisoner> captureTargets = null, SkillCardDefinition card = null, TurnEvent parent = null,
            PatrolCard? monitoringResult = null)
        {
            Kind = kind; Actor = actor; Target = target; Action = action; Prisoner = prisoner;
            Destination = destination; Boundary = boundary; CaptureTargets = captureTargets; Card = card; Parent = parent;
            MonitoringResult = monitoringResult;
        }
    }

    public readonly struct ResponsePlan
    {
        public readonly bool NoResponses;
        public readonly int ParticipantA, ParticipantB, FirstResponder;
        public ResponsePlan(int a, int b, int first) { NoResponses = false; ParticipantA = a; ParticipantB = b; FirstResponder = first; }
        private ResponsePlan(bool noResponses) { NoResponses = noResponses; ParticipantA = ParticipantB = FirstResponder = -1; }
        public static ResponsePlan WithoutResponses => new(true);
    }
}
