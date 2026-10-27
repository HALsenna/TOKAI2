using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public enum GameState { Setup, Player1Turn, Player2Turn, Player3Turn, Handoff, GuardTurn, Result, Player4Turn }
    public enum PlayerAction { Move, PlaceFence, RemoveFence, PlacePrisoner }

    [RequireComponent(typeof(GridManager), typeof(GuardSystem), typeof(GameHud))]
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField, Range(2, 4)] private int playerCount = 3;
        [SerializeField, Min(0)] private float guardRevealSeconds = 1.2f;
        [SerializeField] private List<SkillCardDefinition> skillCards = new();
        [SerializeField] private NormalActionRules actionRules;
        [SerializeField] private TurnFlowPolicy flowPolicy;
        public NormalActionRules Rules { get => actionRules; set => actionRules = value; }
        public TurnFlowPolicy FlowPolicy { get => flowPolicy; set => flowPolicy = value; }
        public GameState State { get; private set; }
        public TurnPhase Phase { get; private set; }
        public GridManager Grid { get; private set; }
        public GuardSystem Guard { get; private set; }
        public GuardVisionSystem Vision { get; private set; }
        private readonly List<FieldStage> stages = new();
        private IReadOnlyList<string> stageCatalogErrors;
        private readonly Dictionary<string, string> stageSourceIssues = new();
        public IReadOnlyList<FieldStage> Stages => stages.AsReadOnly();
        public FieldStage SelectedStage { get; private set; }
        public bool IsSelectingStage { get; private set; }
        public string StageSelectionMessage { get; private set; } = "ステージを選択してください。";
        public IPrisonerCollisionResolver CollisionResolver { get; set; }
        public int PlayerCount { get; private set; }
        public int CurrentPlayer { get; private set; }
        public int PendingPlayer { get; private set; }
        public int Round { get; private set; }
        public int Winner { get; private set; } = -1;
        public PlayerAction Action { get; private set; }
        public bool ActionConsumed { get; private set; }
        public bool ActionDeclared { get; private set; }
        public string Message { get; private set; } = "";
        public string ResultMessage { get; private set; } = "";
        public IReadOnlyList<Prisoner> Prisoners => prisoners.AsReadOnly();
        public IReadOnlyList<int> TurnOrder => order.AsReadOnly();
        public SkillCardPile Cards { get; } = new();
        public int OverflowPlayer { get; private set; } = -1;
        public int Responder => responses != null && !responses.Closed ? responses.Responder : IsGuardResponse ? guardEvent.Target : -1;
        public bool IsChainOpen => responses != null && !responses.Closed && Phase == TurnPhase.Response;
        public bool IsGuardResponse => State == GameState.GuardTurn && Phase == TurnPhase.GuardResponse && guardEvent != null;
        public int InputPlayer => OverflowPlayer >= 0 ? OverflowPlayer : Responder >= 0 ? Responder : CurrentPlayer;
        public TurnEvent Context => operation?.Trigger ?? guardEvent;
        public bool CanRetrySpecification => Phase == TurnPhase.WaitingForSpecification && blockedRetry != null;
        public bool IsPlayerTurn => State == GameState.Player1Turn || State == GameState.Player2Turn || State == GameState.Player3Turn || State == GameState.Player4Turn;
        public bool CanOperate => IsPlayerTurn && Phase == TurnPhase.PlayerInput && !ActionConsumed && !ActionDeclared && operation == null;
        public bool CanEndTurn => IsPlayerTurn && Phase == TurnPhase.PlayerInput && operation == null && OverflowPlayer < 0 && (ActionConsumed || !HasNormalAction());
        public event System.Action Changed;
        public event System.Action<int> WinnerConfirmed;

        private readonly List<Prisoner> prisoners = new();
        private readonly List<int> order = new();
        private readonly List<SkillCardDefinition>[] hands = { new(), new(), new(), new() };
        private readonly int[] escaped = new int[4];
        private readonly List<Prisoner> confirmedEscapes = new();
        private readonly List<CardActivation> deferredImmediate = new();
        private readonly Dictionary<int, List<Prisoner>> guardTargets = new();
        private readonly Queue<int> guardOwners = new();
        private PrototypeActionQuery boardRules;
        private Prisoner selected;
        private int turnIndex;
        private bool resolving, beginningTurn, autoPassScheduled, guardTurnStarted, guardMonitoringResolved;
        private bool HistoryInputBlocked => GetComponent<GameHud>().IsHistoryOpen;
        private Operation operation;
        private ResponseChain<CardActivation> responses;
        private TurnEvent responseTrigger, guardEvent, guardMonitoringEvent;
        private string visionSetupReason;
        private System.Action blockedRetry, afterDiscard;
        private string unresolvedDraw;
        private static readonly Color[] Colors = { new(.95f, .25f, .25f), new(.25f, .55f, 1f), new(1f, .85f, .2f), new(.65f, .35f, .95f) };

        private sealed class CardActivation
        {
            public SkillCardDefinition Card;
            public int Owner;
            public TurnEvent Event;
            public CardActivation(SkillCardDefinition card, int owner, TurnEvent activation) { Card = card; Owner = owner; Event = activation; }
        }
        private sealed class Operation
        {
            public TurnEvent Trigger;
            public CardActivation InitialCard;
            public System.Action Complete;
            public bool ConsumesAction;
        }

        private void Awake()
        {
            Application.runInBackground = true;
            Grid = GetComponent<GridManager>(); Guard = GetComponent<GuardSystem>(); boardRules = new PrototypeActionQuery(this);
            Vision = GetComponent<GuardVisionSystem>() ?? gameObject.AddComponent<GuardVisionSystem>();
            foreach (var definition in Resources.LoadAll<FieldStageDefinition>("FieldStages"))
            {
                var stage = definition.ToStage(); stages.Add(stage);
                if (!string.IsNullOrWhiteSpace(definition.SourceIssue) && stage.StageId != null) stageSourceIssues[stage.StageId] = definition.SourceIssue;
            }
            stages.Sort((a, b) => string.CompareOrdinal(a.StageId, b.StageId));
            stageCatalogErrors = FieldStageValidator.ValidateCatalog(stages);
        }
        private void Start() { GetComponent<GameHud>().Initialize(this); ShowStageSelection(); }
        public void ShowStageSelection()
        {
            if (HistoryInputBlocked) return;
            StopAllCoroutines(); ClearSelection(); State = GameState.Setup; Phase = TurnPhase.Setup;
            IsSelectingStage = true;
            StageSelectionMessage = stageCatalogErrors.Count > 0 ? string.Join(" / ", stageCatalogErrors)
                : stages.Count == 0 ? "ステージデータがありません。" : "ステージを選択してください。";
            Notify();
        }
        public bool SelectStage(string stageId)
        {
            if (HistoryInputBlocked || !IsSelectingStage || stageCatalogErrors.Count > 0) return false;
            FieldStage stage = stages.Find(candidate => candidate.StageId == stageId);
            if (stage == null) return false;
            SelectedStage = stage;
            StageSelectionMessage = CanStartStage(stage, out string reason)
                ? $"{stage.StageName}：{stage.CellCount}マス。開始できます。" : reason;
            Notify(); return true;
        }
        public bool StartSelectedStage()
        {
            if (HistoryInputBlocked || !IsSelectingStage || stageCatalogErrors.Count > 0) return false;
            if (!CanStartStage(SelectedStage, out string reason))
            {
                StageSelectionMessage = reason; Notify(); return false;
            }
            IsSelectingStage = false; RestartGame(); return true;
        }
        // Configuration takes effect on initialization, not halfway through a match.
        public void ConfigureStage(FieldStage stage) => SelectedStage = stage;
        public bool CanStartStage(FieldStage stage, out string reason)
        {
            if (stageCatalogErrors.Count > 0) { reason = string.Join(" / ", stageCatalogErrors); return false; }
            if (stage != null && stage.StageId != null && stageSourceIssues.TryGetValue(stage.StageId, out reason)) return false;
            return FieldStageValidator.TryValidateForPlay(stage, out reason);
        }
        public void Configure(int count, IEnumerable<SkillCardDefinition> deck, NormalActionRules actionRules = null, TurnFlowPolicy flowPolicy = null)
        {
            if (count < 2 || count > 4) throw new ArgumentOutOfRangeException(nameof(count));
            playerCount = count; skillCards = new List<SkillCardDefinition>(deck ?? throw new ArgumentNullException(nameof(deck)));
            this.actionRules = actionRules; this.flowPolicy = flowPolicy;
        }
        public IReadOnlyList<SkillCardDefinition> Hand(int player) => hands[player].AsReadOnly();
        public int EscapedCount(int owner) => escaped[owner];
        public int ReserveCount(int owner) => Count(owner, p => p.IsInReserve);
        public int OnBoardCount(int owner) => Count(owner, p => p.IsOnBoard);
        private int Count(int owner, Predicate<Prisoner> predicate)
        {
            int result = 0; foreach (var p in prisoners) if (p.Owner == owner && predicate(p)) result++; return result;
        }

        public void InitializeGame()
        {
            if (!CanStartStage(SelectedStage, out string stageReason))
            {
                State = GameState.Setup;
                WaitForSpecification(stageReason, RestartGame); return;
            }
            Grid.SetStage(SelectedStage); IsSelectingStage = false;
            StopAllCoroutines(); ClearSelection();
            State = GameState.Setup; Phase = TurnPhase.Setup; Round = 1; Winner = -1;
            Message = ResultMessage = ""; ActionConsumed = ActionDeclared = resolving = beginningTurn = autoPassScheduled = guardTurnStarted = guardMonitoringResolved = false;
            unresolvedDraw = null;
            operation = null; responses = null; responseTrigger = guardEvent = guardMonitoringEvent = null; blockedRetry = afterDiscard = null;
            OverflowPlayer = -1; prisoners.Clear(); order.Clear(); confirmedEscapes.Clear(); deferredImmediate.Clear(); guardTargets.Clear(); guardOwners.Clear();
            Array.Clear(escaped, 0, escaped.Length); foreach (var hand in hands) hand.Clear();
            PlayerCount = Mathf.Clamp(playerCount, 2, 4);
            for (int p = 0; p < PlayerCount; p++) order.Add(p);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]);
            }
            Grid.Build(this); Guard.ResetDeck(); Cards.Reset(skillCards, size => UnityEngine.Random.Range(0, size));
            Vision.TryResetPosition(out visionSetupReason);
            for (int p = 0; p < PlayerCount; p++)
                for (int i = 0; i < 3; i++) prisoners.Add(Grid.CreatePrisoner(p, new Vector2Int(0, p), Colors[p]));
            Grid.RefreshPiecePositions(prisoners); turnIndex = 0; CurrentPlayer = order[0]; Notify();
        }
        public void RestartGame() { if (HistoryInputBlocked) return; InitializeGame(); StartFirstTurn(); }
        public void StartFirstTurn()
        {
            if (State != GameState.Setup || Phase != TurnPhase.Setup || IsSelectingStage) return;
            if (visionSetupReason != null) { WaitForSpecification(visionSetupReason, RestartGame); return; }
            BeginTurn(order[0]);
        }
        private void BeginTurn(int player)
        {
            CurrentPlayer = player; ActionConsumed = ActionDeclared = false; ClearSelection(); Action = PlayerAction.PlacePrisoner;
            State = player switch { 0 => GameState.Player1Turn, 1 => GameState.Player2Turn, 2 => GameState.Player3Turn, _ => GameState.Player4Turn };
            Phase = TurnPhase.Draw; DrawAtTurnStart();
        }
        private void DrawAtTurnStart()
        {
            Phase = TurnPhase.Draw; beginningTurn = true;
            bool drawn;
            try { drawn = DrawSkillCard(CurrentPlayer); }
            finally { beginningTurn = false; }
            if (drawn && operation == null && Phase != TurnPhase.WaitingForSpecification) FinishEffects(UpdatePlayerInput);
        }
        public bool DrawSkillCard(int owner)
        {
            if (State == GameState.Result || owner < 0 || owner >= PlayerCount) return false;
            if (!Cards.TryDraw(out var card))
            {
                const string reason = "スキル山札と使用済みカードが空です。SYS-TURN-001 8.4の処理は未確定です。";
                if (resolving || operation != null) unresolvedDraw = reason;
                WaitForSpecification(reason, beginningTurn ? DrawAtTurnStart : null); return false;
            }
            if (card.Immediate)
            {
                var trigger = new TurnEvent(TurnEventKind.SkillActivation, owner, card.TargetPlayer(this, owner), card: card, parent: responseTrigger);
                if (resolving || operation != null) { deferredImmediate.Add(new CardActivation(card, owner, trigger)); return true; }
                StartOperation(trigger, new CardActivation(card, owner, trigger), UpdatePlayerInput, false);
            }
            else
            {
                hands[owner].Add(card);
                if (!resolving && operation == null && !beginningTurn && State != GameState.Setup) FinishEffects(UpdatePlayerInput);
            }
            return true;
        }

        private bool CanDeploy(Vector2Int cell) => Grid.CanPlacePrisoner(cell) && (actionRules != null ? actionRules.CanDeploy(this, CurrentPlayer, cell) : boardRules.CanDeploy(CurrentPlayer, cell));
        private bool CanMove(Prisoner p, Vector2Int cell) => p != null && p.Owner == CurrentPlayer && p.IsOnBoard && (actionRules != null ? actionRules.CanMove(this, p, cell) : boardRules.CanMove(p, cell));
        private bool CanEscape(Prisoner p) => p != null && p.Owner == CurrentPlayer && p.IsOnBoard && (actionRules != null ? actionRules.CanEscape(this, p) : boardRules.CanEscape(p));
        private bool CanFence(FenceBoundary boundary, bool place) => Grid.IsValidBoundary(boundary)
            && (!place || !Grid.IsFenceForbidden(new Vector2Int(boundary.Column, boundary.Line)))
            && (actionRules != null ? actionRules.CanChangeFence(this, CurrentPlayer, boundary, place) : boardRules.CanChangeFence(boundary, place));
        public bool CanPerform(PlayerAction action)
        {
            if (!CanOperate) return false;
            foreach (var p in prisoners) if (action == PlayerAction.Move && CanEscape(p)) return true;
            foreach (FieldCell fieldCell in Grid.Field.Cells)
            {
                Vector2Int cell = new(fieldCell.X, fieldCell.Y);
                if (action == PlayerAction.PlacePrisoner && CanDeploy(cell)) return true;
                if (action == PlayerAction.Move) foreach (var p in prisoners) if (CanMove(p, cell)) return true;
                if (action == PlayerAction.PlaceFence || action == PlayerAction.RemoveFence)
                    foreach (CellEdge edge in Enum.GetValues(typeof(CellEdge))) if (CanFence(Grid.BoundaryOf(cell, edge), action == PlayerAction.PlaceFence)) return true;
            }
            return false;
        }
        public bool HasNormalAction()
        {
            foreach (PlayerAction candidate in Enum.GetValues(typeof(PlayerAction))) if (CanPerform(candidate)) return true;
            return false;
        }
        public void SelectMove() => SelectAction(PlayerAction.Move);
        public void SelectPlacePrisoner() => SelectAction(PlayerAction.PlacePrisoner);
        public void SelectPlaceFence() => SelectAction(PlayerAction.PlaceFence);
        public void SelectRemoveFence() => SelectAction(PlayerAction.RemoveFence);
        private void SelectAction(PlayerAction action)
        {
            if (HistoryInputBlocked || !CanPerform(action)) return;
            ClearSelection(); Action = action; Say(action == PlayerAction.Move ? "自分の囚人を選択してください。" : "対象のマスまたは柵を選択してください。");
        }
        public void OnPrisonerClicked(Prisoner prisoner)
        {
            if (HistoryInputBlocked || !CanOperate || prisoner == null || !prisoners.Contains(prisoner) || !prisoner.IsOnBoard) return;
            if (Action != PlayerAction.Move) { OnCellClicked(prisoner.Cell); return; }
            if (selected != null && CanMove(selected, prisoner.Cell)) { MoveSelected(prisoner.Cell); return; }
            if (prisoner.Owner != CurrentPlayer) return;
            ClearSelection(); selected = prisoner; prisoner.SetSelected(true); Say($"{Grid.CellName(prisoner.Cell)} の囚人を選択中。");
        }
        public void OnCellClicked(Vector2Int cell) { if (Grid.IsInside(cell)) OnCellClicked(cell, Grid.BoundaryOf(cell, CellEdge.North)); }
        public void OnCellClicked(Vector2Int cell, FenceBoundary boundary)
        {
            if (HistoryInputBlocked || !CanOperate || !Grid.IsInside(cell)) return;
            if (Action == PlayerAction.PlacePrisoner && CanDeploy(cell))
            {
                var piece = prisoners.Find(p => p.Owner == CurrentPlayer && p.IsInReserve); if (piece == null) return;
                Declare(new TurnEvent(TurnEventKind.NormalAction, CurrentPlayer, action: Action, prisoner: piece, destination: cell), () => { if (CanDeploy(cell) && piece.IsInReserve) piece.Deploy(cell); });
            }
            else if (Action == PlayerAction.Move) MoveSelected(cell);
            else OnBoundaryClicked(boundary);
        }
        public void OnBoundaryClicked(FenceBoundary boundary)
        {
            if (HistoryInputBlocked || !CanOperate || (Action != PlayerAction.PlaceFence && Action != PlayerAction.RemoveFence)) return;
            bool place = Action == PlayerAction.PlaceFence; if (!CanFence(boundary, place)) return;
            Declare(new TurnEvent(TurnEventKind.NormalAction, CurrentPlayer, action: Action, boundary: boundary), () => { if (CanFence(boundary, place)) { if (place) Grid.PlaceFence(boundary); else Grid.RemoveFence(boundary); } });
        }
        private void MoveSelected(Vector2Int destination)
        {
            if (!CanMove(selected, destination)) return;
            Prisoner piece = selected;
            Prisoner stationary = Grid.OccupantAt(destination, piece);
            if (stationary != null && CollisionResolver == null)
            {
                WaitForSpecification("移動先に囚人がいます。SYS-PRISONER-001の喧嘩処理を接続してください。", () => MoveSelected(destination));
                return;
            }
            Declare(new TurnEvent(TurnEventKind.NormalAction, CurrentPlayer, action: Action, prisoner: piece, destination: destination), () =>
            {
                if (!CanMove(piece, destination)) return;
                Prisoner occupant = Grid.OccupantAt(destination, piece);
                if (occupant == null) piece.MoveTo(destination);
                else if (CollisionResolver == null || !CollisionResolver.TryResolve(this, piece, occupant, destination, out _))
                    WaitForSpecification("喧嘩処理を解決できませんでした。囚人システムの接続を確認して対局を再開始してください。", null);
            });
        }
        public void MoveRightOrEscape()
        {
            if (HistoryInputBlocked || !CanOperate || Action != PlayerAction.Move || selected == null) return;
            if (!Grid.IsExitCell(selected.Cell)) { MoveSelected(selected.Cell + Vector2Int.right); return; }
            if (!CanEscape(selected)) return;
            Prisoner piece = selected;
            Declare(new TurnEvent(TurnEventKind.NormalAction, CurrentPlayer, action: Action, prisoner: piece, destination: piece.Cell + Vector2Int.right), () => { if (CanEscape(piece)) ConfirmEscape(piece); });
        }
        private void Declare(TurnEvent trigger, System.Action execute)
        {
            ActionDeclared = true; ClearSelection(); StartOperation(trigger, null, () => { if (!trigger.Cancelled) execute(); }, true);
        }

        public bool CanUseCard(int owner, int index)
        {
            if (owner < 0 || owner >= PlayerCount || index < 0 || index >= hands[owner].Count || OverflowPlayer >= 0 || resolving || State == GameState.Result) return false;
            var card = hands[owner][index]; if (card.Immediate) return false;
            if (IsMonitoringContext(responseTrigger) && !card.CanInvalidateGuardMonitoring) return false;
            if (IsChainOpen)
            {
                int target = card.TargetPlayer(this, owner);
                return owner == Responder && (target < 0 || responses.Contains(target)) && card.CanPlay(this, owner, responseTrigger);
            }
            if (IsGuardResponse) return owner == guardEvent.Target && card.CanPlay(this, owner, guardEvent);
            return IsPlayerTurn && Phase == TurnPhase.PlayerInput && operation == null && card.CanPlay(this, owner, null);
        }
        public bool HasPlayableCard(int owner)
        {
            for (int i = 0; i < hands[owner].Count; i++) if (CanUseCard(owner, i)) return true;
            return false;
        }
        public bool UseCard(int owner, int index)
        {
            if (HistoryInputBlocked || !CanUseCard(owner, index)) return false;
            var card = hands[owner][index];
            if (IsChainOpen)
            {
                var activation = new TurnEvent(TurnEventKind.SkillActivation, owner, card.TargetPlayer(this, owner), card: card, parent: responseTrigger);
                if (!responses.TryAdd(owner, new CardActivation(card, owner, activation))) return false;
                hands[owner].RemoveAt(index);
                responseTrigger = activation;
                Notify(); return true;
            }
            TurnEvent parent = IsGuardResponse ? guardEvent : null;
            var trigger = new TurnEvent(TurnEventKind.SkillActivation, owner, card.TargetPlayer(this, owner), card: card, parent: parent);
            hands[owner].RemoveAt(index);
            StartOperation(trigger, new CardActivation(card, owner, trigger), parent != null ? () => OpenGuardResponse(parent) : UpdatePlayerInput, false); return true;
        }
        private List<int> EligibleOwners(TurnEvent trigger)
        {
            var eligible = new List<int>();
            for (int owner = 0; owner < PlayerCount; owner++)
            {
                if (trigger.Kind == TurnEventKind.SkillActivation && trigger.Target >= 0 && owner != trigger.Actor && owner != trigger.Target) continue;
                foreach (var card in hands[owner]) if (!card.Immediate && (!IsMonitoringContext(trigger) || card.CanInvalidateGuardMonitoring) && card.CanPlay(this, owner, trigger)) { eligible.Add(owner); break; }
            }
            return eligible;
        }
        private static bool IsMonitoringContext(TurnEvent trigger)
        {
            while (trigger?.Parent != null) trigger = trigger.Parent;
            return trigger?.Kind == TurnEventKind.GuardMonitoring;
        }
        private void StartOperation(TurnEvent trigger, CardActivation initial, System.Action complete, bool consumesAction)
        {
            operation = new Operation { Trigger = trigger, InitialCard = initial, Complete = complete, ConsumesAction = consumesAction };
            responses = null; responseTrigger = trigger; PlanOperation();
        }
        private void PlanOperation()
        {
            var eligible = EligibleOwners(operation.Trigger);
            if (eligible.Count == 0 && operation.InitialCard == null) { ResolveOperation(); return; }
            ResponsePlan plan = default; string reason = "";
            if (flowPolicy == null || !flowPolicy.TryPlanResponses(this, operation.Trigger, eligible.AsReadOnly(), out plan, out reason))
            {
                WaitForSpecification(string.IsNullOrEmpty(reason) ? "チェーン参加者・初回対応権の仕様設定が必要です（SYS-TURN-001 9.6）。" : reason, PlanOperation); return;
            }
            if (plan.NoResponses) { ResolveOperation(); return; }
            var trigger = operation.Trigger;
            bool valid = plan.ParticipantA >= 0 && plan.ParticipantA < PlayerCount && plan.ParticipantB >= 0 && plan.ParticipantB < PlayerCount && plan.ParticipantA != plan.ParticipantB
                && (plan.FirstResponder == plan.ParticipantA || plan.FirstResponder == plan.ParticipantB);
            if (trigger.Kind == TurnEventKind.SkillActivation && trigger.Target >= 0 && trigger.Target != trigger.Actor)
                valid &= (plan.ParticipantA == trigger.Actor && plan.ParticipantB == trigger.Target) || (plan.ParticipantB == trigger.Actor && plan.ParticipantA == trigger.Target);
            if (!valid) { WaitForSpecification("チェーン設定の参加者・対応者が不正です。", PlanOperation); return; }
            responses = new ResponseChain<CardActivation>(plan.ParticipantA, plan.ParticipantB, plan.FirstResponder);
            Phase = TurnPhase.Response; Say($"P{Responder + 1}：対応カードまたはパスを選択してください。");
        }
        public void PassResponse()
        {
            if (HistoryInputBlocked) return;
            if (IsChainOpen)
            {
                if (!responses.TryPass(Responder)) return;
                if (responses.Closed) ResolveOperation(); else Notify();
            }
            else if (IsGuardResponse) CompleteGuardResponse();
        }
        private void ResolveOperation()
        {
            resolving = true; Phase = TurnPhase.Resolve;
            try
            {
                if (responses != null)
                    foreach (var entry in responses.ResolutionOrder())
                    {
                        ResolveCard(entry);
                        if (HoldUnspecifiedEffect()) return;
                    }
                if (operation.InitialCard != null)
                {
                    ResolveCard(operation.InitialCard);
                    if (HoldUnspecifiedEffect()) return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception); WaitForSpecification("カード効果の解決に失敗しました。個別カード実装を確認してください。", null); return;
            }
            finally { resolving = false; }
            responses = null; FinishEffects(ApplyOperation);
        }
        private bool HoldUnspecifiedEffect()
        {
            if (unresolvedDraw == null && deferredImmediate.Count == 0) return false;
            WaitForSpecification(unresolvedDraw ?? "解決中に新規即時イベントが発生しました。優先順位が未確定です（SYS-TURN-001 9.6）。", null);
            return true;
        }
        private void ResolveCard(CardActivation entry)
        {
            try { if (!entry.Event.Cancelled) entry.Card.Resolve(this, entry.Owner, entry.Event); }
            finally { Cards.Discard(entry.Card); }
        }
        private void ApplyOperation()
        {
            var complete = operation;
            if (complete.ConsumesAction)
            {
                complete.Complete();
                if (Phase == TurnPhase.WaitingForSpecification) return;
                ActionConsumed = true; ActionDeclared = false;
            }
            operation = null; responseTrigger = null;
            CommitEscapes(); Grid.RefreshPiecePositions(prisoners);
            if (CheckVictory()) return;
            if (!complete.ConsumesAction) complete.Complete(); else UpdatePlayerInput();
        }
        private void FinishEffects(System.Action complete)
        {
            if (State == GameState.Result) return;
            if (unresolvedDraw != null) { WaitForSpecification(unresolvedDraw, null); return; }
            if (deferredImmediate.Count > 0) { WaitForSpecification("解決中に即時発動イベントが発生しました。新規イベントの優先順位が未確定です（9.6）。", null); return; }
            var owners = new List<int>(); for (int owner = 0; owner < PlayerCount; owner++) if (hands[owner].Count > 5) owners.Add(owner);
            if (owners.Count == 0) { OverflowPlayer = -1; afterDiscard = null; complete(); return; }
            int chosen = owners[0];
            if (owners.Count > 1 && (flowPolicy == null || !flowPolicy.TryChooseDiscardOwner(owners.AsReadOnly(), out chosen) || !owners.Contains(chosen)))
            {
                WaitForSpecification("複数プレイヤーの手札超過処理順が未確定です。", () => FinishEffects(complete)); return;
            }
            OverflowPlayer = chosen; afterDiscard = complete; Phase = TurnPhase.ChooseDiscard;
            Say($"P{chosen + 1}：手札が5枚になるまで、捨てるカードを選択してください。");
        }
        public bool DiscardOverflow(int owner, int index)
        {
            if (HistoryInputBlocked || Phase != TurnPhase.ChooseDiscard || owner != OverflowPlayer || index < 0 || index >= hands[owner].Count) return false;
            var complete = afterDiscard; Cards.Discard(hands[owner][index]); hands[owner].RemoveAt(index);
            OverflowPlayer = -1; FinishEffects(complete); return true;
        }
        private void UpdatePlayerInput()
        {
            if (State == GameState.Result || !IsPlayerTurn || operation != null) return;
            Phase = TurnPhase.PlayerInput;
            if (!ActionConsumed && !HasNormalAction())
            {
                if (!HasPlayableCard(CurrentPlayer))
                {
                    Phase = TurnPhase.Resolve; Say("通常行動も使用可能なカードもないため自動パスします。");
                    if (!autoPassScheduled) { autoPassScheduled = true; StartCoroutine(AutoPass()); } return;
                }
                Say("通常行動ができません。カード使用またはパスを選べます。"); return;
            }
            Say(ActionConsumed ? "通常行動は消費済みです。カード使用またはターン終了を選べます。" : "通常行動を1回実行してください。カードも使用できます。");
        }
        private IEnumerator AutoPass() { yield return null; autoPassScheduled = false; if (IsPlayerTurn && Phase == TurnPhase.Resolve) AdvanceTurn(true); }
        public void EndTurn() { if (!HistoryInputBlocked && CanEndTurn) AdvanceTurn(false); }
        private void AdvanceTurn(bool automatic)
        {
            ClearSelection(); turnIndex++;
            if (turnIndex < order.Count) { if (automatic) BeginTurn(order[turnIndex]); else RequestHandoff(order[turnIndex]); }
            else { State = GameState.GuardTurn; Phase = TurnPhase.GuardReveal; Notify(); BeginGuardTurn(); }
        }
        private void RequestHandoff(int player) { PendingPlayer = player; State = GameState.Handoff; Phase = TurnPhase.Handoff; Notify(); }
        public void ConfirmHandoff() { if (!HistoryInputBlocked && State == GameState.Handoff) BeginTurn(PendingPlayer); }

        private void BeginGuardTurn()
        {
            if (State != GameState.GuardTurn || guardTurnStarted) return;
            if (!Guard.TryDrawMonitoring(Round, out var card, out var reason)) { WaitForSpecification(reason, BeginGuardTurn); return; }
            guardTurnStarted = true;
            guardMonitoringResolved = false;
            guardMonitoringEvent = new TurnEvent(TurnEventKind.GuardMonitoring, -1, monitoringResult: card);
            Phase = TurnPhase.GuardReveal; Say($"監視カード：{Guard.GetCardName(card)}（割り込み解決前）"); StartCoroutine(RevealGuard());
        }
        private IEnumerator RevealGuard()
        {
            yield return new WaitForSecondsRealtime(guardRevealSeconds);
            if (State == GameState.GuardTurn) StartOperation(guardMonitoringEvent, null, FinalizeGuardMonitoring, false);
        }
        private void FinalizeGuardMonitoring()
        {
            if (State != GameState.GuardTurn || guardMonitoringResolved || guardMonitoringEvent == null) return;
            var monitoring = guardMonitoringEvent;
            List<Prisoner> targets;
            try { targets = Guard.FindTargets(monitoring.MonitoringResult.Value, Grid, Prisoners, !monitoring.Cancelled); }
            catch (InvalidOperationException exception) { WaitForSpecification(exception.Message, FinalizeGuardMonitoring); return; }
            guardMonitoringResolved = true;
            Say(monitoring.Cancelled ? "監視は無効になりました。" : $"監視結果確定：{Guard.GetCardName(monitoring.MonitoringResult.Value)}");
            try { Guard.ApplyMonitoringFailure(targets); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                WaitForSpecification("囚人発見の通知・状態更新に失敗しました。途中適用を再実行せず停止しています。関連サービスを確認して対局を再開始してください。", null);
                return;
            }
            Grid.RefreshPiecePositions(prisoners);
            FinishGuardTurn();
        }

        // Compatibility path retained for earlier SYS-TURN integrations. SYS-VISION
        // uses the pre-vision GuardMonitoring chain and one simultaneous notification.
        private void PlanGuardOwners()
        {
            if (State != GameState.GuardTurn) return;
            if (guardTargets.Count == 0) { FinishGuardTurn(); return; }
            var owners = new List<int>(guardTargets.Keys); int[] ordered;
            if (owners.Count == 1) ordered = new[] { owners[0] };
            else if (flowPolicy == null || !flowPolicy.TryOrderGuardResponses(owners.AsReadOnly(), out ordered) || !ValidOrder(ordered, owners))
            {
                WaitForSpecification("複数所有者への監視対応順を設定してください（SYS-TURN-001 10.6）。", PlanGuardOwners); return;
            }
            guardOwners.Clear(); foreach (int owner in ordered) guardOwners.Enqueue(owner); NextGuardOwner();
        }
        private static bool ValidOrder(int[] planned, List<int> owners)
        {
            if (planned == null || planned.Length != owners.Count) return false;
            var unique = new HashSet<int>(planned); return unique.Count == owners.Count && unique.SetEquals(owners);
        }
        private void NextGuardOwner()
        {
            if (State == GameState.Result) return;
            if (guardOwners.Count == 0) { FinishGuardTurn(); return; }
            int owner = guardOwners.Dequeue(); OpenGuardResponse(new TurnEvent(TurnEventKind.GuardCapture, owner, owner, captureTargets: guardTargets[owner].AsReadOnly()));
        }
        private void OpenGuardResponse(TurnEvent trigger)
        {
            guardEvent = trigger; Phase = TurnPhase.GuardResponse;
            if (trigger.Cancelled || !HasPlayableCard(trigger.Target)) { CompleteGuardResponse(); return; }
            Say($"監視対象確定：P{trigger.Target + 1}、{trigger.CaptureTargets.Count}人。救出カードまたは対応パスを選択してください。");
        }
        private void CompleteGuardResponse()
        {
            if (guardEvent == null) return;
            var completed = guardEvent; guardEvent = null;
            if (!completed.Cancelled) Guard.ApplyMonitoringFailure(completed.CaptureTargets);
            Grid.RefreshPiecePositions(prisoners); if (!CheckVictory()) NextGuardOwner();
        }
        private void FinishGuardTurn()
        {
            if (State != GameState.GuardTurn) return;
            guardEvent = null; if (CheckVictory()) return;
            guardTurnStarted = false; Round++; turnIndex = 0; RequestHandoff(order[0]);
        }
        public void ConfirmEscape(Prisoner piece)
        {
            if (State == GameState.Result || piece == null || !prisoners.Contains(piece) || !piece.IsOnBoard || confirmedEscapes.Contains(piece)) return;
            confirmedEscapes.Add(piece);
            if (!resolving && operation == null) { CommitEscapes(); CheckVictory(); Notify(); }
        }
        private void CommitEscapes()
        {
            foreach (var piece in confirmedEscapes) if (piece.IsOnBoard) { piece.Escape(); escaped[piece.Owner]++; }
            confirmedEscapes.Clear();
        }
        private bool CheckVictory()
        {
            if (State == GameState.Result) return true;
            if (operation != null || resolving) return false;
            int winner = -1;
            for (int owner = 0; owner < PlayerCount; owner++) if (escaped[owner] >= 3)
            {
                if (winner >= 0) { WaitForSpecification("同時に複数の勝利者が成立しました。同時イベントの規則が必要です。", null); return true; }
                winner = owner;
            }
            if (winner < 0) return false;
            Winner = winner; State = GameState.Result; Phase = TurnPhase.Finished; ClearSelection(); StopAllCoroutines();
            ResultMessage = $"P{winner + 1} の勝利！\n3人が脱獄しました。"; WinnerConfirmed?.Invoke(winner); Notify(); return true;
        }
        private void WaitForSpecification(string reason, System.Action retry) { Phase = TurnPhase.WaitingForSpecification; blockedRetry = retry; Say(reason); }
        public void RetryPendingSpecification()
        {
            if (HistoryInputBlocked || Phase != TurnPhase.WaitingForSpecification || blockedRetry == null) return;
            var retry = blockedRetry; blockedRetry = null; retry();
        }
        private void ClearSelection() { if (selected != null) selected.SetSelected(false); selected = null; }
        private void Notify() => Changed?.Invoke();
        private void Say(string message) { Message = message; Notify(); }
    }
}
