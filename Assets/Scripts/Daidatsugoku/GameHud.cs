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
        private Text turn, message, mode, handoffText, resultText;
        private readonly Text[] scores = new Text[3];
        private readonly Image[] cards = new Image[3];
        private GameObject actions, handoff, result;
        private Button deploy, move, place, remove, forward;

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

            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.rect = new Rect(0, .25f, 1, .56f);
                camera.backgroundColor = new Color(.085f, .11f, .13f);
            }
            RectTransform header = Panel("Header", canvasObject.transform, Surface);
            header.anchorMin = new Vector2(0, .81f);
            header = ContentRow(header);
            Label("大脱獄", header, 32, 14, 260, 50, 34);
            Label("TABLE GAME / 01", header, 34, 66, 260, 24, 14);
            turn = Label("", header, 32, 102, 300, 38, 20);
            for (int p = 0; p < 3; p++)
            {
                RectTransform card = Panel("Player" + (p + 1), header, Card);
                SetRect(card, 350 + p * 300, 20, 280, 110);
                cards[p] = card.GetComponent<Image>();
                Text title = Label("PLAYER " + (p + 1), card, 16, 10, 248, 28, 18);
                title.color = p == 0 ? new Color(.90f, .56f, .52f) : p == 1 ? new Color(.53f, .72f, .91f) : new Color(.85f, .75f, .46f);
                scores[p] = Label("", card, 16, 44, 248, 60, 18);
            }
            RectTransform tray = Panel("ActionTray", canvasObject.transform, Surface);
            tray.anchorMax = new Vector2(1, .25f);
            tray = ContentRow(tray);
            mode = Label("", tray, 32, 14, 290, 32, 20);
            message = Label("", tray, 330, 14, 918, 40, 20);
            actions = new GameObject("ActionPanel", typeof(RectTransform));
            actions.transform.SetParent(tray, false); Stretch(actions.GetComponent<RectTransform>());
            deploy = Button("駒を配置", actions.transform, 32, 64, 228, game.SelectPlacePrisoner);
            move = Button("移動", actions.transform, 279, 64, 228, game.SelectMove);
            place = Button("柵を設置", actions.transform, 526, 64, 228, game.SelectPlaceFence);
            remove = Button("柵を破壊", actions.transform, 773, 64, 228, game.SelectRemoveFence);
            forward = Button("脱獄へ →", actions.transform, 1020, 64, 228, game.MoveRightOrEscape);
            Label("あそびかた：左クリックで操作　 /　柵はマスの辺を選択　 /　柵より下の同じ縦筋は安全", tray, 32, 146, 1216, 32, 16);

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
            for (int p = 0; p < 3; p++)
            {
                scores[p].text = $"脱獄  {game.EscapedCount(p)} / 3\n盤上 {game.OnBoardCount(p)}　手元 {game.ReserveCount(p)}";
                cards[p].color = game.IsPlayerTurn && game.CurrentPlayer == p ? new Color(.20f, .32f, .32f) : Card;
            }
            message.text = game.Message;
            mode.text = "選択中：" + (game.Action switch { PlayerAction.PlacePrisoner => "駒を配置", PlayerAction.Move => "移動", PlayerAction.PlaceFence => "柵を設置", _ => "柵を破壊" });
            Highlight(deploy, game.Action == PlayerAction.PlacePrisoner);
            Highlight(move, game.Action == PlayerAction.Move);
            Highlight(place, game.Action == PlayerAction.PlaceFence);
            Highlight(remove, game.Action == PlayerAction.RemoveFence);
            actions.SetActive(game.IsPlayerTurn);
            deploy.interactable = game.IsPlayerTurn && game.ReserveCount(game.CurrentPlayer) > 0;
            bool canAct = game.IsPlayerTurn && !game.MustPlacePrisoner;
            move.interactable = canAct && game.OnBoardCount(game.CurrentPlayer) > 0; place.interactable = canAct;
            remove.interactable = canAct; forward.interactable = canAct && game.Action == PlayerAction.Move;
            handoff.SetActive(game.State == GameState.Handoff);
            handoffText.text = $"次のプレイヤー P{game.PendingPlayer + 1} へ\n交代してください。\n準備ができたら開始ボタンを押します。";
            result.SetActive(game.State == GameState.Result);
            resultText.text = game.ResultMessage;
        }

        private RectTransform Panel(string title, Transform parent, Color color)
        {
            GameObject obj = new(title, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = color;
            RectTransform rect = obj.GetComponent<RectTransform>(); Stretch(rect); return rect;
        }
        private static RectTransform ContentRow(RectTransform parent)
        {
            GameObject obj = new("Content", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.5f, 0); rect.anchorMax = new Vector2(.5f, 1);
            rect.sizeDelta = new Vector2(1280, 0); rect.anchoredPosition = Vector2.zero;
            return rect;
        }
        private RectTransform CenterCard(Transform parent)
        {
            RectTransform rect = Panel("Card", parent, Card);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(600, 320); rect.anchoredPosition = Vector2.zero; return rect;
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
        private Button Button(string text, Transform parent, float x, float y, float width, UnityAction click)
        {
            GameObject obj = new(text, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
            SetRect(obj.GetComponent<RectTransform>(), x, y, width, 62);
            obj.GetComponent<Image>().color = Accent;
            Button button = obj.GetComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>(); button.onClick.AddListener(click);
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.pressedColor = new Color(.8f, .8f, .8f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.6f, .6f, .6f);
            button.colors = colors;
            Text label = Label(text, obj.transform, 0, 0, width, 62, 23); label.alignment = TextAnchor.MiddleCenter; label.color = TextColor;
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
