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
        private GameManager game;
        private Font font;
        private Text turn, score, message, mode, handoffText, resultText;
        private GameObject actions, handoff, result;
        private Button deploy, move, place, remove, forward;
        private bool showHand;
        private int handPage;
        private int lastInputPlayer = -1;
        private int viewedPlayer;

        public void Initialize(GameManager manager)
        {
            game = manager;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic", "Meiryo", "Arial" }, 22);
            GameObject canvasObject = new("DaidatsugokuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = 1f;

            RectTransform sidebar = Panel("Sidebar", canvasObject.transform, new Color(.06f, .09f, .14f, .98f));
            sidebar.anchorMax = new Vector2(.29f, 1);
            Label("大脱獄", sidebar, 20, 20, 320, 50, 34);
            turn = Label("", sidebar, 20, 85, 320, 55, 24);
            score = Label("", sidebar, 20, 145, 320, 110, 21);
            score.raycastTarget = true;
            score.gameObject.AddComponent<Button>().onClick.AddListener(() => {
                if (!game.IsPlayerTurn || game.IsChainOpen || game.OverflowPlayer >= 0) return;
                viewedPlayer = (viewedPlayer + 1) % game.PlayerCount; showHand = true; handPage = 0; Refresh();
            });
            mode = Label("", sidebar, 20, 260, 320, 38, 21);
            mode.raycastTarget = true;
            mode.gameObject.AddComponent<Button>().onClick.AddListener(() => { showHand = !showHand; handPage = 0; Refresh(); });
            actions = new GameObject("ActionPanel", typeof(RectTransform));
            actions.transform.SetParent(sidebar, false); Stretch(actions.GetComponent<RectTransform>());
            deploy = Button("駒を配置", actions.transform, 20, 300, 320, () => ClickSlot(0));
            move = Button("移動", actions.transform, 20, 355, 320, () => ClickSlot(1));
            place = Button("柵を設置", actions.transform, 20, 410, 320, () => ClickSlot(2));
            remove = Button("柵を破壊", actions.transform, 20, 465, 320, () => ClickSlot(3));
            forward = Button("右へ／脱獄 →", actions.transform, 20, 520, 320, () => ClickSlot(4));
            message = Label("", sidebar, 20, 580, 320, 105, 20);
            Label("操作：左クリックのみ\n手札：見出し／スコアで切替\n柵より下の同じ縦筋を保護", sidebar, 20, 695, 320, 85, 17);

            handoff = Panel("HandoffPanel", canvasObject.transform, new Color(.03f, .05f, .09f, .96f)).gameObject;
            RectTransform handoffCard = CenterCard(handoff.transform);
            handoffText = Label("", handoffCard, 30, 35, 540, 140, 28);
            Button("次のプレイヤー開始", handoffCard, 100, 210, 400, game.ConfirmHandoff);
            result = Panel("ResultPanel", canvasObject.transform, new Color(.03f, .05f, .09f, .96f)).gameObject;
            RectTransform resultCard = CenterCard(result.transform);
            resultText = Label("", resultCard, 30, 35, 540, 140, 28);
            Button("Restart／もう一度", resultCard, 100, 210, 400, game.RestartGame);
            handoff.SetActive(false); result.SetActive(false);

            if (EventSystem.current == null)
            {
                GameObject events = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            game.Changed += Refresh;
        }

        private void Refresh()
        {
            turn.text = game.State switch
            {
                GameState.Setup => "準備中",
                GameState.Handoff => $"ラウンド {game.Round}：交代待ち",
                GameState.GuardTurn => "監視官：自動処理中",
                GameState.Result => "ゲーム終了",
                _ => $"ラウンド {game.Round}：P{game.CurrentPlayer + 1}"
            };
            score.text = "";
            score.fontSize = game.PlayerCount == 4 ? 18 : 21;
            for (int p = 0; p < game.PlayerCount; p++) score.text += $"P{p + 1}　脱獄 {game.EscapedCount(p)}/3　盤上 {game.OnBoardCount(p)} 手元 {game.ReserveCount(p)}\n";
            message.text = game.Message;
            mode.text = "選択中：" + (game.Action switch { PlayerAction.PlacePrisoner => "駒を配置", PlayerAction.Move => "移動", PlayerAction.PlaceFence => "柵を設置", _ => "柵を破壊" });
            actions.SetActive(game.IsPlayerTurn || game.IsChainOpen || game.OverflowPlayer >= 0);
            if (lastInputPlayer != game.InputPlayer || game.State == GameState.Handoff || game.State == GameState.Result)
            { showHand = false; handPage = 0; lastInputPlayer = viewedPlayer = game.InputPlayer; }
            RefreshSlots();
            handoff.SetActive(game.State == GameState.Handoff);
            handoffText.text = $"次のプレイヤー P{game.PendingPlayer + 1} へ\n交代してください。\n準備ができたら開始ボタンを押します。";
            result.SetActive(game.State == GameState.Result);
            resultText.text = game.ResultMessage;
        }

        private bool HandVisible => showHand || game.IsChainOpen || game.OverflowPlayer >= 0;
        private int HandPlayer => game.IsChainOpen || game.OverflowPlayer >= 0 ? game.InputPlayer : viewedPlayer;
        private void RefreshSlots()
        {
            Button[] slots = { deploy, move, place, remove, forward };
            if (HandVisible)
            {
                int player = HandPlayer;
                var hand = game.Hand(player);
                // Four card slots plus one navigation/response slot; layout and styling stay unchanged.
                if (handPage * 4 >= hand.Count) handPage = 0;
                mode.text = $"P{player + 1} 手札 {hand.Count}/5：" + (game.OverflowPlayer >= 0 ? "捨てる" : "使用／戻る");
                for (int i = 0; i < 4; i++)
                {
                    int index = handPage * 4 + i;
                    SetSlot(slots[i], index < hand.Count ? hand[index].DisplayName : "―", index < hand.Count && (game.OverflowPlayer >= 0 || game.CanUseCard(player, index)));
                }
                if (hand.Count > 4 && handPage == 0) SetSlot(forward, "次の手札 →", true);
                else if (game.IsChainOpen) SetSlot(forward, "対応をパス", true);
                else if (game.OverflowPlayer >= 0) SetSlot(forward, "← 最初の手札", handPage > 0);
                else SetSlot(forward, game.CanEndTurn && player == game.CurrentPlayer ? "ターン終了" : "通常行動に戻る", true);
                return;
            }
            SetSlot(deploy, "駒を配置", game.CanOperate && game.CanPerform(PlayerAction.PlacePrisoner));
            SetSlot(move, "移動", game.CanOperate && game.CanPerform(PlayerAction.Move));
            SetSlot(place, "柵を設置", game.CanOperate && game.CanPerform(PlayerAction.PlaceFence));
            SetSlot(remove, "柵を破壊", game.CanOperate && game.CanPerform(PlayerAction.RemoveFence));
            SetSlot(forward, game.CanEndTurn ? (game.ActionConsumed ? "ターン終了" : "パス") : "右へ／脱獄 →",
                game.CanEndTurn || (game.CanOperate && game.Action == PlayerAction.Move));
        }
        private static void SetSlot(Button button, string text, bool enabled)
        {
            button.GetComponentInChildren<Text>().text = text; button.interactable = enabled;
        }
        private void ClickSlot(int slot)
        {
            if (HandVisible)
            {
                if (slot < 4)
                {
                    int player = HandPlayer, index = handPage * 4 + slot;
                    if (game.OverflowPlayer >= 0) game.DiscardOverflow(player, index); else game.UseCard(player, index);
                }
                else if (game.Hand(HandPlayer).Count > 4 && handPage == 0) { handPage = 1; Refresh(); }
                else if (game.IsChainOpen) game.PassChain(game.Responder);
                else if (game.OverflowPlayer >= 0) { handPage = 0; Refresh(); }
                else if (game.CanEndTurn && HandPlayer == game.CurrentPlayer) game.EndTurn();
                else { showHand = false; handPage = 0; Refresh(); }
                return;
            }
            switch (slot)
            {
                case 0: game.SelectPlacePrisoner(); break;
                case 1: game.SelectMove(); break;
                case 2: game.SelectPlaceFence(); break;
                case 3: game.SelectRemoveFence(); break;
                case 4: if (game.CanEndTurn) game.EndTurn(); else game.MoveRightOrEscape(); break;
            }
        }

        private RectTransform Panel(string title, Transform parent, Color color)
        {
            GameObject obj = new(title, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = color;
            RectTransform rect = obj.GetComponent<RectTransform>(); Stretch(rect); return rect;
        }
        private RectTransform CenterCard(Transform parent)
        {
            RectTransform rect = Panel("Card", parent, new Color(.10f, .15f, .22f));
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(600, 320); rect.anchoredPosition = Vector2.zero; return rect;
        }
        private Text Label(string text, Transform parent, float x, float y, float width, float height, int size)
        {
            GameObject obj = new("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            SetRect(obj.GetComponent<RectTransform>(), x, y, width, height);
            Text label = obj.GetComponent<Text>(); label.font = font; label.fontSize = size;
            label.text = text; label.color = Color.white; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }
        private Button Button(string text, Transform parent, float x, float y, float width, UnityAction click)
        {
            GameObject obj = new(text, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
            SetRect(obj.GetComponent<RectTransform>(), x, y, width, 48);
            obj.GetComponent<Image>().color = new Color(.18f, .35f, .49f);
            Button button = obj.GetComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>(); button.onClick.AddListener(click);
            Text label = Label(text, obj.transform, 0, 0, width, 48, 23); label.alignment = TextAnchor.MiddleCenter;
            return button;
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
