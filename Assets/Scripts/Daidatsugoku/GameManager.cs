using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public enum GameState { Setup, Player1Turn, Player2Turn, Player3Turn, Handoff, GuardTurn, Result }
    public enum PlayerAction { Move, PlaceFence, RemoveFence, PlacePrisoner }

    [RequireComponent(typeof(GridManager), typeof(GuardSystem), typeof(GameHud))]
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField, Min(0)] private float guardRevealSeconds = 1.2f;
        public GameState State { get; private set; }
        public GridManager Grid { get; private set; }
        public GuardSystem Guard { get; private set; }
        public IReadOnlyList<Prisoner> Prisoners => prisoners;
        public int CurrentPlayer { get; private set; }
        public int Round { get; private set; }
        public PlayerAction Action { get; private set; }
        public string Message { get; private set; }
        public string ResultMessage { get; private set; }
        public int PendingPlayer { get; private set; }
        public bool IsPlayerTurn => State == GameState.Player1Turn || State == GameState.Player2Turn || State == GameState.Player3Turn;
        public event System.Action Changed;
        public bool MustPlacePrisoner => IsPlayerTurn && OnBoardCount(CurrentPlayer) == 0 && ReserveCount(CurrentPlayer) > 0;
        public int ReserveCount(int owner)
        {
            int count = 0;
            foreach (Prisoner p in prisoners) if (p.Owner == owner && p.IsInReserve) count++;
            return count;
        }
        private readonly List<Prisoner> prisoners = new();
        private readonly int[] escaped = new int[3];
        private Prisoner selected;
        private static readonly Color[] Colors = { new(.95f, .25f, .25f), new(.25f, .55f, 1f), new(1f, .85f, .2f) };

        private void Awake()
        {
            Application.runInBackground = true;
            Grid = GetComponent<GridManager>(); Guard = GetComponent<GuardSystem>();
        }
        private void Start() { GetComponent<GameHud>().Initialize(this); RestartGame(); }
        public int EscapedCount(int player) => escaped[player];
        public int OnBoardCount(int owner)
        {
            int count = 0;
            foreach (Prisoner p in prisoners) if (p.Owner == owner && p.IsOnBoard) count++;
            return count;
        }

        public void RestartGame()
        {
            StopAllCoroutines(); ClearSelection(); State = GameState.Setup; Round = 1;
            ResultMessage = ""; prisoners.Clear(); System.Array.Clear(escaped, 0, 3);
            Grid.Build(this); Guard.ResetDeck();
            for (int player = 0; player < 3; player++)
            for (int i = 0; i < 3; i++) prisoners.Add(Grid.CreatePrisoner(player, new Vector2Int(0, player), Colors[player]));
            Grid.RefreshPiecePositions(prisoners); BeginPlayerTurn(0);
        }

        private void BeginPlayerTurn(int player)
        {
            CurrentPlayer = player;
            State = player switch { 0 => GameState.Player1Turn, 1 => GameState.Player2Turn, _ => GameState.Player3Turn };
            ClearSelection();
            SelectAction(MustPlacePrisoner ? PlayerAction.PlacePrisoner : PlayerAction.Move);
        }

        public void SelectMove() => SelectAction(PlayerAction.Move);
        public void SelectPlacePrisoner() => SelectAction(PlayerAction.PlacePrisoner);
        public void SelectPlaceFence() => SelectAction(PlayerAction.PlaceFence);
        public void SelectRemoveFence() => SelectAction(PlayerAction.RemoveFence);
        private void SelectAction(PlayerAction action)
        {
            if (!IsPlayerTurn) return;
            if (MustPlacePrisoner && action != PlayerAction.PlacePrisoner) { Say("盤面に自分の駒がないため、駒を配置してください。"); return; }
            if (action == PlayerAction.PlacePrisoner && ReserveCount(CurrentPlayer) == 0) { Say("未配置の駒がありません。"); return; }
            ClearSelection(); Action = action;
            Say(action switch {
                PlayerAction.PlacePrisoner => "駒を配置するスタートマス（START）をクリックしてください。",
                PlayerAction.Move => "自分の駒を選択してください。",
                PlayerAction.PlaceFence => "柵を設置するマスの辺をクリックしてください。",
                _ => "破壊する赤い柵をクリックしてください。"
            });
        }

        public void OnPrisonerClicked(Prisoner prisoner)
        {
            if (!IsPlayerTurn || prisoner == null || !prisoners.Contains(prisoner) || !prisoner.IsOnBoard) return;
            if (Action != PlayerAction.Move) { OnCellClicked(prisoner.Cell); return; }
            if (selected != null && Grid.CanMove(selected.Cell, prisoner.Cell)) { TryMove(prisoner.Cell); return; }
            if (prisoner.Owner != CurrentPlayer) { Say("自分の駒を選択してください。"); return; }
            ClearSelection(); selected = prisoner; selected.SetSelected(true);
            Say($"{Grid.CellName(selected.Cell)} の駒を選択中。右・上・下へ移動できます。");
        }

        public void OnCellClicked(Vector2Int cell)
        {
            if (!Grid.IsInside(cell)) return;
            OnCellClicked(cell, Grid.BoundaryOf(cell, CellEdge.North));
        }
        public void OnCellClicked(Vector2Int cell, FenceBoundary boundary)
        {
            if (!IsPlayerTurn || !Grid.IsInside(cell)) return;
            if (MustPlacePrisoner && Action != PlayerAction.PlacePrisoner) return;
            if (Action == PlayerAction.PlacePrisoner)
            {
                if (!Grid.IsStartCell(cell)) { Say("駒を配置できるのはスタートマス（START）だけです。"); return; }
                foreach (Prisoner prisoner in prisoners)
                {
                    if (prisoner.Owner != CurrentPlayer || !prisoner.IsInReserve) continue;
                    prisoner.Deploy(cell); Grid.RefreshPiecePositions(prisoners); CompleteAction(); return;
                }
                Say("未配置の駒がありません。"); return;
            }
            if (Action == PlayerAction.Move) { TryMove(cell); return; }
            OnBoundaryClicked(boundary);
        }

        public void OnBoundaryClicked(FenceBoundary boundary)
        {
            if (!IsPlayerTurn || MustPlacePrisoner || (Action != PlayerAction.PlaceFence && Action != PlayerAction.RemoveFence)) return;
            bool success = Action == PlayerAction.PlaceFence ? Grid.PlaceFence(boundary) : Grid.RemoveFence(boundary);
            if (!success) { Say(Action == PlayerAction.PlaceFence ? "すでに柵があります。" : "柵がありません。"); return; }
            CompleteAction();
        }

        public void MoveRightOrEscape()
        {
            if (!IsPlayerTurn || Action != PlayerAction.Move) return;
            if (selected == null) { Say("先に自分の駒を選択してください。"); return; }
            if (!Grid.IsExitCell(selected.Cell)) { TryMove(selected.Cell + Vector2Int.right); return; }
            selected.Escape(); escaped[CurrentPlayer]++; Grid.RefreshPiecePositions(prisoners); CompleteAction();
        }

        private void TryMove(Vector2Int destination)
        {
            if (selected == null) { Say("先に自分の駒を選択してください。"); return; }
            if (!Grid.CanMove(selected.Cell, destination)) { Say("右・上・下の隣接マスへ移動できます。段差の外へは移動できません。"); return; }
            selected.MoveTo(destination); Grid.RefreshPiecePositions(prisoners); CompleteAction();
        }

        private void CompleteAction()
        {
            ClearSelection();
            if (CheckResult()) return;
            if (CurrentPlayer < 2) { RequestHandoff(CurrentPlayer + 1); return; }
            State = GameState.GuardTurn; Changed?.Invoke(); StartCoroutine(GuardTurnRoutine());
        }

        private IEnumerator GuardTurnRoutine()
        {
            PatrolCard card = Guard.DrawCard(); Say($"見回りカード：{Guard.GetCardName(card)}");
            yield return new WaitForSecondsRealtime(guardRevealSeconds);
            int count = Guard.Resolve(card, Grid, prisoners); Grid.RefreshPiecePositions(prisoners);
            Say($"監視完了：{count}人を捕獲。柵より下の同じ縦筋は保護されます。");
            if (CheckResult()) yield break;
            yield return new WaitForSecondsRealtime(guardRevealSeconds);
            Round++; RequestHandoff(0);
        }

        private void RequestHandoff(int player)
        {
            State = GameState.Handoff; PendingPlayer = player; Changed?.Invoke();
        }
        public void ConfirmHandoff() { if (State == GameState.Handoff) BeginPlayerTurn(PendingPlayer); }

        private bool CheckResult()
        {
            for (int i = 0; i < 3; i++) if (escaped[i] >= 3) { Finish($"P{i + 1} の勝利！\n3人が脱獄しました。"); return true; }
            for (int i = 0; i < 3; i++) if (escaped[i] + OnBoardCount(i) + ReserveCount(i) >= 3) return false;
            Finish("勝者なし\n全員が3人脱獄できなくなりました。"); return true;
        }
        private void Finish(string message) { State = GameState.Result; ClearSelection(); ResultMessage = message; Changed?.Invoke(); }
        private void ClearSelection() { if (selected != null) selected.SetSelected(false); selected = null; }
        private void Say(string message) { Message = message; Changed?.Invoke(); }
    }
}
