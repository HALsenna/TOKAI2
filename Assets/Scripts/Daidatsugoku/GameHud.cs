using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Daidatsugoku
{
    // Prototype UI is built at runtime; no inspector wiring is required.
    public sealed class GameHud : MonoBehaviour
    {
        private static readonly Color Surface = new(.12f, .15f, .18f);
        private static readonly Color Card = new(.17f, .21f, .24f);
        private static readonly Color TextColor = new(.82f, .85f, .84f);
        private static readonly Color Accent = new(.20f, .43f, .43f);
        private GameManager game;
        private Font font;
        private const int CardsPerPage = 5;
        private Text turn, score, message, mode, handoffText, resultText, handTitle, handHint, pageText, waitingText;
        private GameObject actions, handoff, result, handPanel, waiting;
        private Button deploy, move, place, remove, forward, endTurn, showHand, closeHand, previousPage, nextPage, responsePass;
        private Button retrySpecification;
        private readonly Button[] playerHands = new Button[4];
        private readonly Button[] cardButtons = new Button[CardsPerPage];
        private int handOwner, handPage, lastInputPlayer = -1;
        private bool handVisible;

        public void Initialize(GameManager manager)
        {
            game = manager;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic", "Meiryo", "Arial" }, 22);
            GameObject canvasObject = new("DaidatsugokuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);
            // Fit the whole reference layout on both axes, including 4:3 windows.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            RectTransform sidebar = Panel("Sidebar", canvasObject.transform, new Color(.06f, .09f, .14f, .98f));
            sidebar.anchorMax = new Vector2(.29f, 1);
            Label("大脱獄", sidebar, 20, 20, 320, 50, 34);
            turn = Label("", sidebar, 20, 78, 320, 60, 23);
            score = Label("", sidebar, 20, 142, 330, 100, 18);
            mode = Label("", sidebar, 20, 250, 320, 50, 20);
            actions = new GameObject("ActionPanel", typeof(RectTransform));
            actions.transform.SetParent(sidebar, false); Stretch(actions.GetComponent<RectTransform>());
            deploy = Button("囚人配置", actions.transform, 20, 310, 320, game.SelectPlacePrisoner, 42);
            move = Button("囚人移動", actions.transform, 20, 358, 320, game.SelectMove, 42);
            place = Button("柵設置", actions.transform, 20, 406, 320, game.SelectPlaceFence, 42);
            remove = Button("柵破壊", actions.transform, 20, 454, 320, game.SelectRemoveFence, 42);
            forward = Button("右へ／脱獄 →", actions.transform, 20, 502, 320, game.MoveRightOrEscape, 42);
            endTurn = Button("ターン終了", sidebar, 20, 553, 320, game.EndTurn, 42);
            showHand = Button("手札", sidebar, 20, 601, 320, ToggleHand, 42);
            message = Label("", sidebar, 20, 655, 320, 118, 18);

            BuildHandPanel(canvasObject.transform);
            waiting = Panel("SpecificationWaitingPanel", canvasObject.transform, new Color(.03f, .05f, .09f, .96f)).gameObject;
            RectTransform waitingCard = CenterCard(waiting.transform, 760, 460);
            Label("仕様・設定の確認待ち", waitingCard, 30, 25, 700, 45, 28);
            waitingText = Label("", waitingCard, 30, 85, 700, 255, 21);
            retrySpecification = Button("設定を再確認", waitingCard, 35, 365, 330, game.RetryPendingSpecification);
            Button("対局を初期化", waitingCard, 395, 365, 330, game.RestartGame);

            handoff = Panel("HandoffPanel", canvasObject.transform, new Color(.03f, .05f, .09f, .96f)).gameObject;
            RectTransform handoffCard = CenterCard(handoff.transform);
            handoffText = Label("", handoffCard, 30, 35, 540, 140, 28);
            Button("次のプレイヤー開始", handoffCard, 100, 210, 400, game.ConfirmHandoff);
            result = Panel("ResultPanel", canvasObject.transform, new Color(.03f, .05f, .09f, .96f)).gameObject;
            RectTransform resultCard = CenterCard(result.transform);
            resultText = Label("", resultCard, 30, 35, 540, 140, 28);
            Button("Restart／もう一度", resultCard, 100, 210, 400, game.RestartGame);
            handoff.SetActive(false); result.SetActive(false); waiting.SetActive(false); handPanel.SetActive(false);

            if (EventSystem.current == null)
            {
                GameObject events = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            game.Changed += Refresh;
            Refresh();
        }

        private void BuildHandPanel(Transform canvas)
        {
            handPanel = Panel("HandPanel", canvas, new Color(.03f, .05f, .09f, .94f)).gameObject;
            RectTransform card = CenterCard(handPanel.transform, 760, 640);
            handTitle = Label("手札", card, 25, 18, 570, 40, 26);
            closeHand = Button("閉じる", card, 610, 18, 125, ToggleHand, 40);
            for (int p = 0; p < playerHands.Length; p++)
            {
                int player = p;
                playerHands[p] = Button($"P{p + 1}", card, 25 + p * 180, 70, 165, () => SelectHand(player), 38);
            }
            handHint = Label("", card, 25, 120, 710, 60, 20);
            for (int i = 0; i < cardButtons.Length; i++)
            {
                int slot = i;
                cardButtons[i] = Button("", card, 25, 190 + i * 64, 710, () => SelectCard(slot), 56);
            }
            previousPage = Button("前のページ", card, 25, 520, 190, () => ChangeHandPage(-1), 40);
            pageText = Label("", card, 230, 520, 300, 40, 20);
            pageText.alignment = TextAnchor.MiddleCenter;
            nextPage = Button("次のページ", card, 545, 520, 190, () => ChangeHandPage(1), 40);
            responsePass = Button("対応をパス", card, 205, 580, 350, game.PassResponse, 40);
        }

        private void Refresh()
        {
            turn.text = game.State switch
            {
                GameState.Setup => "準備中",
                GameState.Handoff => $"ラウンド {game.Round}：交代待ち",
                GameState.GuardTurn => $"ラウンド {game.Round}：看守ターン",
                GameState.Result => "ゲーム終了",
                _ => $"ラウンド {game.Round}：P{game.CurrentPlayer + 1}"
            };
            score.text = "";
            for (int p = 0; p < game.PlayerCount; p++) score.text += $"P{p + 1}　脱獄 {game.EscapedCount(p)}/3　盤上 {game.OnBoardCount(p)} 手元 {game.ReserveCount(p)}\n";
            message.text = game.Message;
            string actionState = game.ActionConsumed ? "消費済み" : game.ActionDeclared ? "宣言中" : "未消費";
            mode.text = $"通常行動：{actionState}\n選択：" + (game.Action switch { PlayerAction.PlacePrisoner => "囚人配置", PlayerAction.Move => "囚人移動", PlayerAction.PlaceFence => "柵設置", _ => "柵破壊" });
            actions.SetActive(game.IsPlayerTurn);
            deploy.interactable = game.CanOperate && game.CanPerform(PlayerAction.PlacePrisoner);
            move.interactable = game.CanOperate && game.CanPerform(PlayerAction.Move);
            place.interactable = game.CanOperate && game.CanPerform(PlayerAction.PlaceFence);
            remove.interactable = game.CanOperate && game.CanPerform(PlayerAction.RemoveFence);
            forward.interactable = move.interactable && game.Action == PlayerAction.Move;
            endTurn.gameObject.SetActive(game.IsPlayerTurn);
            endTurn.interactable = game.CanEndTurn;
            SetButtonText(endTurn, game.ActionConsumed || !game.CanEndTurn ? "ターン終了" : "パス");
            showHand.interactable = game.State != GameState.Setup && game.State != GameState.Result;
            SetButtonText(showHand, game.InputPlayer >= 0 ? $"P{game.InputPlayer + 1} の手札を開く" : "手札を開く");
            RefreshHand();
            waiting.SetActive(game.Phase == TurnPhase.WaitingForSpecification);
            waitingText.text = game.Message;
            retrySpecification.interactable = game.CanRetrySpecification;
            handoff.SetActive(game.State == GameState.Handoff);
            handoffText.text = $"次のプレイヤー P{game.PendingPlayer + 1} へ\n交代してください。\n準備ができたら開始ボタンを押します。";
            result.SetActive(game.State == GameState.Result);
            resultText.text = game.ResultMessage;
        }

        private bool ForcedHand => game.OverflowPlayer >= 0 || game.Phase == TurnPhase.Response || game.Phase == TurnPhase.GuardResponse;

        private void RefreshHand()
        {
            int inputPlayer = game.InputPlayer;
            if (inputPlayer != lastInputPlayer)
            {
                lastInputPlayer = inputPlayer;
                if (inputPlayer >= 0) handOwner = inputPlayer;
                handPage = 0;
            }
            if (game.OverflowPlayer >= 0) handOwner = game.OverflowPlayer;
            else if ((game.IsChainOpen || game.IsGuardResponse) && game.Responder >= 0) handOwner = game.Responder;
            handOwner = Mathf.Clamp(handOwner, 0, Mathf.Max(0, game.PlayerCount - 1));
            bool canShow = game.State != GameState.Setup && game.State != GameState.Handoff && game.State != GameState.Result;
            handPanel.SetActive(canShow && (handVisible || ForcedHand));
            closeHand.interactable = !ForcedHand;
            var cards = game.Hand(handOwner);
            int pages = Mathf.Max(1, Mathf.CeilToInt(cards.Count / (float)CardsPerPage));
            handPage = Mathf.Clamp(handPage, 0, pages - 1);
            handTitle.text = $"P{handOwner + 1} の手札：{cards.Count}枚／上限5枚";
            bool discarding = game.OverflowPlayer == handOwner;
            bool responding = game.Phase == TurnPhase.Response || game.Phase == TurnPhase.GuardResponse;
            handHint.text = discarding ? "5枚以下になるまで捨てるカードを選択してください。\n新しく引いたカードも選べます。"
                : responding ? $"P{game.Responder + 1} の対応：発動可能なカードを選択、またはパス。"
                : cards.Count == 0 ? "手札はありません。" : "発動条件を満たすカードを選択できます。";
            for (int p = 0; p < playerHands.Length; p++)
            {
                playerHands[p].gameObject.SetActive(p < game.PlayerCount);
                playerHands[p].interactable = !ForcedHand && p != handOwner;
            }
            for (int slot = 0; slot < cardButtons.Length; slot++)
            {
                int index = handPage * CardsPerPage + slot;
                bool exists = index < cards.Count;
                Button button = cardButtons[slot];
                button.gameObject.SetActive(exists);
                if (!exists) continue;
                SetButtonText(button, $"{index + 1}. {cards[index].DisplayName}" + (discarding ? "　［捨てる］" : ""));
                button.interactable = discarding || game.CanUseCard(handOwner, index);
            }
            previousPage.interactable = handPage > 0;
            nextPage.interactable = handPage + 1 < pages;
            pageText.text = $"{handPage + 1} / {pages}";
            responsePass.gameObject.SetActive(responding && !discarding);
            responsePass.interactable = responding && game.Responder >= 0;
        }

        private void ToggleHand()
        {
            if (ForcedHand) return;
            handVisible = !handVisible;
            RefreshHand();
        }

        private void SelectHand(int player)
        {
            if (ForcedHand || player < 0 || player >= game.PlayerCount) return;
            handOwner = player; handPage = 0; RefreshHand();
        }

        private void ChangeHandPage(int offset) { handPage += offset; RefreshHand(); }

        private void SelectCard(int slot)
        {
            int index = handPage * CardsPerPage + slot;
            if (index < 0 || index >= game.Hand(handOwner).Count) return;
            if (game.OverflowPlayer == handOwner) game.DiscardOverflow(handOwner, index);
            else if (game.CanUseCard(handOwner, index)) game.UseCard(handOwner, index);
        }

        private static void SetButtonText(Button button, string text) => button.GetComponentInChildren<Text>().text = text;

        private RectTransform Panel(string title, Transform parent, Color color)
        {
            GameObject obj = new(title, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = color;
            RectTransform rect = obj.GetComponent<RectTransform>(); Stretch(rect); return rect;
        }
        private RectTransform CenterCard(Transform parent, float width = 600, float height = 320)
        {
            RectTransform rect = Panel("Card", parent, Card);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = Vector2.zero; return rect;
        }
        private Text Label(string text, Transform parent, float x, float y, float width, float height, int size)
        {
            GameObject obj = new("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            SetRect(obj.GetComponent<RectTransform>(), x, y, width, height);
            Text label = obj.GetComponent<Text>(); label.font = font; label.fontSize = size;
            label.text = text; label.color = TextColor; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }
        private Button Button(string text, Transform parent, float x, float y, float width, UnityAction click, float height = 48)
        {
            GameObject obj = new(text, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
            SetRect(obj.GetComponent<RectTransform>(), x, y, width, height);
            obj.GetComponent<Image>().color = new Color(.18f, .35f, .49f);
            Button button = obj.GetComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>(); button.onClick.AddListener(click);
            Text label = Label(text, obj.transform, 0, 0, width, height, 21); label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        private static void Highlight(Button button, bool selected)
        {
            button.GetComponent<Image>().color = selected ? Accent : new Color(.24f, .29f, .32f);
        }
        private static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private void OnDestroy()
        {
            if (game != null) game.Changed -= Refresh;
            if (font != null) Destroy(font);
        }
    }
}
