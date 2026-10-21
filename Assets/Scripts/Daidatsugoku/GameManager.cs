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
        [SerializeField] private List<SkillCard> skillCards = new();
        [SerializeField] private FlowRules rules;
        public FlowRules Rules => rules;
        public GameState State { get; private set; }
        public TurnPhase Phase { get; private set; }
        public GridManager Grid { get; private set; }
        public GuardSystem Guard { get; private set; }
        public IReadOnlyList<Prisoner> Prisoners => prisoners.AsReadOnly();
        public IReadOnlyList<int> TurnOrder => turnOrder.AsReadOnly();
        public SkillDeck Cards { get; } = new();
        public int PlayerCount => activePlayerCount;
        public int CurrentPlayer { get; private set; }
        public int Round { get; private set; }
        public int Winner { get; private set; } = -1;
        public PlayerAction Action { get; private set; }
        public string Message { get; private set; }
        public string ResultMessage { get; private set; }
        public int PendingPlayer { get; private set; }
        public bool ActionConsumed { get; private set; }
        public bool IsPlayerTurn => State == GameState.Player1Turn || State == GameState.Player2Turn || State == GameState.Player3Turn || State == GameState.Player4Turn;
        public bool IsChainOpen => context != null && !resolving;
        public int Responder { get; private set; } = -1;
        public int ConsecutivePasses { get; private set; }
        public int ChainCount => chain.Count;
        public SkillCard LastChainCard => chain.Count == 0 ? null : chain[chain.Count - 1].card;
        public int LastChainOwner => chain.Count == 0 ? -1 : chain[chain.Count - 1].owner;
        public FlowContext Context => context;
        public int OverflowPlayer { get; private set; } = -1;
        public int InputPlayer => OverflowPlayer >= 0 ? OverflowPlayer : IsChainOpen ? Responder : CurrentPlayer;
        public bool CanOperate => IsPlayerTurn && !ActionConsumed && context == null && !resolving && OverflowPlayer < 0 && Phase == TurnPhase.ActionWait;
        public event System.Action Changed;

        private readonly List<Prisoner> prisoners = new();
        private readonly List<int> turnOrder = new();
        private readonly List<SkillCard>[] hands = { new(), new(), new(), new() };
        private readonly int[] escaped = new int[4];
        private readonly List<(SkillCard card, int owner)> chain = new();
        private int activePlayerCount = 3, turnIndex, participantA = -1, participantB = -1;
        private Prisoner selected;
        private FlowContext context;
        private System.Action continuation;
        private bool resolving;
        private static readonly Color[] Colors = { new(.95f, .25f, .25f), new(.25f, .55f, 1f), new(1f, .85f, .2f), new(.65f, .35f, .95f) };

        private void Awake()
        {
            Application.runInBackground = true;
            Grid = GetComponent<GridManager>(); Guard = GetComponent<GuardSystem>();
        }
        private void Start() { GetComponent<GameHud>().Initialize(this); RestartGame(); }
        public IReadOnlyList<SkillCard> Hand(int player) => hands[player].AsReadOnly();
        public int EscapedCount(int player) => escaped[player];
        public int ReserveCount(int owner) => Count(owner, p => p.IsInReserve);
        public int OnBoardCount(int owner) => Count(owner, p => p.IsOnBoard);
        private int Count(int owner, Predicate<Prisoner> predicate)
        {
            int count = 0;
            foreach (Prisoner p in prisoners) if (p.Owner == owner && predicate(p)) count++;
            return count;
        }
        public void RestartGame()
        {
            StopAllCoroutines(); ClearSelection(); State = GameState.Setup; Round = 1; Winner = -1;
            ResultMessage = ""; prisoners.Clear(); Array.Clear(escaped, 0, escaped.Length);
            context = null; continuation = null; chain.Clear(); resolving = false;
            Responder = OverflowPlayer = -1; ConsecutivePasses = 0;
            activePlayerCount = Mathf.Clamp(playerCount, 2, 4);
            turnOrder.Clear(); foreach (var hand in hands) hand.Clear();
            for (int p = 0; p < PlayerCount; p++) turnOrder.Add(p);
            for (int i = turnOrder.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (turnOrder[i], turnOrder[j]) = (turnOrder[j], turnOrder[i]);
            }
            Grid.Build(this); Guard.ResetDeck(); Cards.Reset(skillCards);
            for (int p = 0; p < PlayerCount; p++)
            for (int i = 0; i < 3; i++) prisoners.Add(Grid.CreatePrisoner(p, new Vector2Int(0, p), Colors[p]));
            turnIndex = 0; BeginPlayerTurn(turnOrder[0]);
        }
        private void BeginPlayerTurn(int player)
        {
            CurrentPlayer = player; ActionConsumed = false;
            State = player switch { 0 => GameState.Player1Turn, 1 => GameState.Player2Turn, 2 => GameState.Player3Turn, _ => GameState.Player4Turn };
            ClearSelection(); Action = PlayerAction.PlacePrisoner; Phase = TurnPhase.Draw;
            DrawSkillCard(player);
            if (State == GameState.Result) return;
            Phase = TurnPhase.ActionWait; UpdateAvailability();
        }
        public void DrawSkillCard(int player)
        {
            if (State == GameState.Result || player < 0 || player >= PlayerCount) return;
            SkillCard card = Cards.Draw();
            if (card == null) { Message = "スキル山札・使用済みカードが空のためドローを保留しました。"; return; }
            if (card.Immediate)
            {
                TurnPhase previous = Phase; Phase = TurnPhase.CardResolution;
                bool wasResolving = resolving; resolving = true;
                try { card.Resolve(this, player, new FlowContext(FlowEvent.SkillCard, player, card.TargetPlayer(this, player))); }
                finally { Cards.DiscardCard(card); resolving = wasResolving; }
                Phase = previous;
                if (!resolving) CheckResult();
            }
            else hands[player].Add(card);
            RefreshOverflow();
        }
        private void RefreshOverflow()
        {
            OverflowPlayer = -1;
            foreach (int p in turnOrder) if (hands[p].Count > 5) { OverflowPlayer = p; break; }
        }
        public bool DiscardOverflow(int player, int index)
        {
            if (State == GameState.Result || player != OverflowPlayer || index < 0 || index >= hands[player].Count) return false;
            Cards.DiscardCard(hands[player][index]); hands[player].RemoveAt(index); RefreshOverflow();
            UpdateAvailability(); return true;
        }
        public bool Occupied(Vector2Int cell)
        {
            foreach (Prisoner p in prisoners) if (p.IsOnBoard && p.Cell == cell) return true;
            return false;
        }
        private bool Allowed(PlayerAction action) => rules == null || rules.AllowsAction(this, CurrentPlayer, action);
        private bool CanDeploy(Vector2Int cell) => Allowed(PlayerAction.PlacePrisoner) && Grid.IsStartCell(cell) && !Occupied(cell) && ReserveCount(CurrentPlayer) > 0
            && (rules == null || rules.AllowsDeployment(this, CurrentPlayer, cell));
        private bool CanMove(Prisoner p, Vector2Int to) => Allowed(PlayerAction.Move) && p.IsOnBoard && p.Owner == CurrentPlayer && Grid.CanMove(p.Cell, to) && !Occupied(to)
            && (rules == null || rules.AllowsMovement(this, p, to));
        private bool CanEscape(Prisoner p) => Allowed(PlayerAction.Move) && p.IsOnBoard && Grid.IsExitCell(p.Cell)
            && (rules == null || rules.AllowsMovement(this, p, p.Cell + Vector2Int.right));
        private bool CanFence(FenceBoundary boundary, bool placing) => Allowed(placing ? PlayerAction.PlaceFence : PlayerAction.RemoveFence)
            && Grid.IsValidBoundary(boundary) && Grid.HasFence(boundary) != placing
            && (rules == null || rules.AllowsFence(this, CurrentPlayer, boundary, placing));
        public bool CanPerform(PlayerAction action)
        {
            if (!IsPlayerTurn || ActionConsumed || !Allowed(action)) return false;
            for (int row = 0; row < GridManager.RowCount; row++)
            for (int x = 0; x < Grid.GetRowLength(row); x++)
            {
                Vector2Int cell = new(x, row);
                if (action == PlayerAction.PlacePrisoner && CanDeploy(cell)) return true;
                if (action == PlayerAction.Move)
                    foreach (Prisoner p in prisoners)
                        if (p.Owner == CurrentPlayer && p.IsOnBoard && (CanEscape(p) || CanMove(p, cell))) return true;
                if (action == PlayerAction.PlaceFence || action == PlayerAction.RemoveFence)
                    foreach (CellEdge edge in Enum.GetValues(typeof(CellEdge)))
                    {
                        if (CanFence(Grid.BoundaryOf(cell, edge), action == PlayerAction.PlaceFence)) return true;
                    }
            }
            return false;
        }
        public bool HasNormalAction()
        {
            foreach (PlayerAction action in Enum.GetValues(typeof(PlayerAction))) if (CanPerform(action)) return true;
            return false;
        }
        public void SelectMove() => SelectAction(PlayerAction.Move);
        public void SelectPlacePrisoner() => SelectAction(PlayerAction.PlacePrisoner);
        public void SelectPlaceFence() => SelectAction(PlayerAction.PlaceFence);
        public void SelectRemoveFence() => SelectAction(PlayerAction.RemoveFence);
        private void SelectAction(PlayerAction action)
        {
            if (!CanOperate) return;
            if (!CanPerform(action)) { Say("この通常行動は実行できません。"); return; }
            ClearSelection(); Action = action;
            Say(action switch {
                PlayerAction.PlacePrisoner => "空いているスタートマス（START）をクリックしてください。",
                PlayerAction.Move => "自分の駒を選択してください。",
                PlayerAction.PlaceFence => "柵を設置するマスの辺をクリックしてください。",
                _ => "破壊する赤い柵をクリックしてください。"
            });
        }
        public void OnPrisonerClicked(Prisoner prisoner)
        {
            if (!CanOperate || prisoner == null || !prisoners.Contains(prisoner) || !prisoner.IsOnBoard) return;
            if (Action != PlayerAction.Move) { OnCellClicked(prisoner.Cell); return; }
            if (prisoner.Owner != CurrentPlayer) { Say("自分の駒を選択してください。他の囚人のいるマスには移動できません。"); return; }
            ClearSelection(); selected = prisoner; selected.SetSelected(true);
            Say($"{Grid.CellName(selected.Cell)} の駒を選択中。右・上・下へ移動できます。");
        }
        public void OnCellClicked(Vector2Int cell)
        {
            if (Grid.IsInside(cell)) OnCellClicked(cell, Grid.BoundaryOf(cell, CellEdge.North));
        }
        public void OnCellClicked(Vector2Int cell, FenceBoundary boundary)
        {
            if (!CanOperate || !Grid.IsInside(cell)) return;
            if (Action == PlayerAction.PlacePrisoner)
            {
                if (!CanDeploy(cell)) { Say("空いているSTARTと未配置の駒が必要です。"); return; }
                Prisoner reserve = prisoners.Find(p => p.Owner == CurrentPlayer && p.IsInReserve);
                DeclareAction(new FlowContext(FlowEvent.NormalAction, CurrentPlayer, action: Action, prisoner: reserve, destination: cell), () => { if (CanDeploy(cell)) reserve.Deploy(cell); });
            }
            else if (Action == PlayerAction.Move) TryMove(cell);
            else OnBoundaryClicked(boundary);
        }
        public void OnBoundaryClicked(FenceBoundary boundary)
        {
            if (!CanOperate || (Action != PlayerAction.PlaceFence && Action != PlayerAction.RemoveFence) || !Grid.IsValidBoundary(boundary)) return;
            bool place = Action == PlayerAction.PlaceFence;
            if (!CanFence(boundary, place)) { Say("この境界の柵操作は実行できません。"); return; }
            DeclareAction(new FlowContext(FlowEvent.NormalAction, CurrentPlayer, action: Action, boundary: boundary), () => {
                if (CanFence(boundary, place)) { if (place) Grid.PlaceFence(boundary); else Grid.RemoveFence(boundary); }
            });
        }
        public void MoveRightOrEscape()
        {
            if (!CanOperate || Action != PlayerAction.Move) return;
            if (selected == null) { Say("先に自分の駒を選択してください。"); return; }
            if (!Grid.IsExitCell(selected.Cell)) { TryMove(selected.Cell + Vector2Int.right); return; }
            Prisoner piece = selected;
            if (!CanEscape(piece)) return;
            DeclareAction(new FlowContext(FlowEvent.NormalAction, CurrentPlayer, action: Action, prisoner: piece, destination: piece.Cell + Vector2Int.right), () => { if (CanEscape(piece)) ConfirmEscape(piece); });
        }
        private void TryMove(Vector2Int destination)
        {
            if (selected == null) { Say("先に自分の駒を選択してください。"); return; }
            if (!CanMove(selected, destination)) { Say("空いている右・上・下の隣接マスへ移動できます。"); return; }
            Prisoner piece = selected;
            DeclareAction(new FlowContext(FlowEvent.NormalAction, CurrentPlayer, action: Action, prisoner: piece, destination: destination), () => { if (CanMove(piece, destination)) piece.MoveTo(destination); });
        }
        private void DeclareAction(FlowContext trigger, System.Action execute)
        {
            // Validation precedes this method. An interrupted declaration still consumes the action.
            ActionConsumed = true; ClearSelection();
            OpenWindow(trigger, () => {
                if (!trigger.Cancelled) execute();
                Grid.RefreshPiecePositions(prisoners);
                if (CheckResult()) return;
                Phase = TurnPhase.ActionCompleted;
                Say(trigger.Cancelled ? "通常行動は無効化されました。カード使用またはターン終了ができます。" : "通常行動済み。カード使用またはターン終了ができます。");
            });
        }
        public void ConfirmEscape(Prisoner piece)
        {
            if (State == GameState.Result || piece == null || !prisoners.Contains(piece) || !piece.IsOnBoard) return;
            piece.Escape(); escaped[piece.Owner]++;
            if (!resolving && context == null) CheckResult();
        }
        private FlowContext FreeContext(int player) => new(FlowEvent.FreePlay, player);
        public bool CanUseCard(int player, int index)
        {
            if (State == GameState.Result || OverflowPlayer >= 0 || resolving || player < 0 || player >= PlayerCount || index < 0 || index >= hands[player].Count) return false;
            if (IsChainOpen) return player == Responder && hands[player][index].CanPlay(this, player, context);
            return IsPlayerTurn && (Phase == TurnPhase.ActionWait || Phase == TurnPhase.ActionCompleted)
                && hands[player][index].CanPlay(this, player, FreeContext(player));
        }
        public bool HasPlayableCard(int player)
        {
            for (int i = 0; i < hands[player].Count; i++) if (CanUseCard(player, i)) return true;
            return false;
        }
        public bool UseCard(int player, int index)
        {
            if (!CanUseCard(player, index)) return false;
            SkillCard card = hands[player][index];
            if (!IsChainOpen)
            {
                int target = card.TargetPlayer(this, player);
                // No-target and multi-target chains are undefined; resolve once without a two-person chain.
                hands[player].RemoveAt(index);
                context = new FlowContext(FlowEvent.SkillCard, player, target);
                continuation = UpdateAvailability;
                chain.Add((card, player)); Phase = TurnPhase.ChainResolution;
                if (target >= 0 && target < PlayerCount && target != player)
                {
                    participantA = player; participantB = target; Responder = target; ConsecutivePasses = 0; Notify();
                }
                else ResolveChain();
            }
            else
            {
                hands[player].RemoveAt(index);
                if (participantA < 0) { participantA = context.Actor; participantB = player; }
                chain.Add((card, player)); ConsecutivePasses = 0;
                context.Event = FlowEvent.SkillCard;
                Responder = player == participantA ? participantB : participantA;
                Notify();
            }
            return true;
        }
        private bool HasResponse(int player, FlowContext trigger)
        {
            foreach (SkillCard card in hands[player]) if (card.CanPlay(this, player, trigger)) return true;
            return false;
        }
        private void OpenWindow(FlowContext trigger, System.Action after)
        {
            context = trigger; continuation = after; chain.Clear();
            participantA = participantB = -1; ConsecutivePasses = 0;
            if (trigger.Event == FlowEvent.GuardCapture)
            {
                if (HasResponse(trigger.Target, trigger))
                {
                    participantA = participantB = trigger.Target; Responder = trigger.Target;
                    Phase = TurnPhase.ChainResolution; Notify(); return;
                }
            }
            else
                foreach (int player in turnOrder)
                    if (player != trigger.Actor && HasResponse(player, trigger))
                    {
                        Responder = player; Phase = TurnPhase.ChainResolution; Notify(); return;
                    }
            ResolveChain();
        }
        public void PassChain(int player)
        {
            if (!IsChainOpen || OverflowPlayer >= 0 || player != Responder) return;
            if (participantA < 0)
            {
                int current = turnOrder.IndexOf(player);
                for (int i = current + 1; i < turnOrder.Count; i++)
                    if (turnOrder[i] != context.Actor && HasResponse(turnOrder[i], context)) { Responder = turnOrder[i]; Notify(); return; }
                ResolveChain(); return;
            }
            ConsecutivePasses++;
            if (ConsecutivePasses >= (participantA == participantB ? 1 : 2)) { ResolveChain(); return; }
            Responder = player == participantA ? participantB : participantA; Notify();
        }
        private void ResolveChain()
        {
            resolving = true; Phase = TurnPhase.CardResolution;
            try
            {
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    var entry = chain[i];
                    try { entry.card.Resolve(this, entry.owner, context); }
                    finally { Cards.DiscardCard(entry.card); }
                }
            }
            finally { resolving = false; chain.Clear(); context = null; Responder = -1; }
            System.Action after = continuation; continuation = null;
            if (CheckResult()) return;
            RefreshOverflow(); Phase = ActionConsumed ? TurnPhase.ActionCompleted : TurnPhase.ActionWait;
            after?.Invoke();
            if (State != GameState.Result) UpdateAvailability();
        }
        public bool CanEndTurn => IsPlayerTurn && context == null && !resolving && OverflowPlayer < 0
            && (Phase == TurnPhase.ActionWait || Phase == TurnPhase.ActionCompleted) && (ActionConsumed || !HasNormalAction());
        public void EndTurn()
        {
            if (!CanEndTurn) { if (IsPlayerTurn) Say("通常行動・カード対応・手札超過の処理を完了してください。"); return; }
            Phase = TurnPhase.TurnEnd; ClearSelection(); turnIndex++;
            if (turnIndex < turnOrder.Count) RequestHandoff(turnOrder[turnIndex]);
            else { State = GameState.GuardTurn; Notify(); StartCoroutine(GuardTurnRoutine()); }
        }
        private void UpdateAvailability()
        {
            if (State == GameState.Result) return;
            if (OverflowPlayer >= 0) { Say($"P{OverflowPlayer + 1}：手札が5枚になるまで捨てるカードを選んでください。"); return; }
            if (!IsPlayerTurn || context != null || resolving) { Notify(); return; }
            Phase = ActionConsumed ? TurnPhase.ActionCompleted : TurnPhase.ActionWait;
            if (!ActionConsumed && !HasNormalAction())
            {
                if (!HasPlayableCard(CurrentPlayer)) { Message = "通常行動・使用可能カードがないため自動パス。"; EndTurn(); return; }
                Say("通常行動ができません。カード使用またはパスを選べます。"); return;
            }
            Say(ActionConsumed ? "通常行動済み。カード使用またはターン終了ができます。" : "通常行動を1回実行してください。カードも使用できます。");
        }
        private IEnumerator GuardTurnRoutine()
        {
            PatrolCard card = Guard.DrawCard(); Say($"見回りカード：{Guard.GetCardName(card)}");
            yield return new WaitForSecondsRealtime(guardRevealSeconds);
            // Provisional ordering until the dedicated simultaneous-event specification exists.
            foreach (int owner in turnOrder)
            {
                List<Prisoner> targets = Guard.Targets(card, Grid, prisoners, owner);
                if (targets.Count == 0) continue;
                FlowContext trigger = new(FlowEvent.GuardCapture, owner, owner, captureTargets: targets.AsReadOnly());
                OpenWindow(trigger, () => {
                    if (!trigger.Cancelled)
                        foreach (Prisoner piece in targets)
                            if (piece.IsOnBoard && !Grid.IsProtected(piece.Cell)) piece.Capture();
                    Grid.RefreshPiecePositions(prisoners);
                });
                while (context != null || OverflowPlayer >= 0) yield return null;
                if (State == GameState.Result) yield break;
            }
            Say("監視完了。"); yield return new WaitForSecondsRealtime(guardRevealSeconds);
            if (CheckResult()) yield break;
            Round++; turnIndex = 0; RequestHandoff(turnOrder[0]);
        }
        private void RequestHandoff(int player) { State = GameState.Handoff; PendingPlayer = player; Notify(); }
        public void ConfirmHandoff() { if (State == GameState.Handoff) BeginPlayerTurn(PendingPlayer); }
        private bool CheckResult()
        {
            if (State == GameState.Result) return true;
            foreach (int player in turnOrder)
                if (escaped[player] >= 3)
                {
                    Winner = player; State = GameState.Result; ClearSelection();
                    ResultMessage = $"P{player + 1} の勝利！\n3人が脱獄しました。"; Notify(); return true;
                }
            return false;
        }
        private void ClearSelection() { if (selected != null) selected.SetSelected(false); selected = null; }
        private void Notify() => Changed?.Invoke();
        private void Say(string message) { Message = message; Notify(); }
    }
}
