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
        private RectTransform safeRoot, sidebar, sidebarContent;
        private CanvasScaler scaler;
        private Text title, help;
        private int layoutWidth, layoutHeight;
        private Rect lastSafeArea;

        public void Initialize(GameManager manager)
        {
            game = manager;
            font = GameFont.Load();
            GameObject canvasObject = new("DaidatsugokuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = 1f;

            safeRoot = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeRoot.SetParent(canvasObject.transform, false); Stretch(safeRoot);
            sidebar = Panel("Sidebar", safeRoot, new Color(.06f, .09f, .14f, .98f));
            sidebar.anchorMax = new Vector2(.29f, 1);
            sidebarContent = new GameObject("SidebarContent", typeof(RectTransform)).GetComponent<RectTransform>();
            sidebarContent.SetParent(sidebar, false); Stretch(sidebarContent);
            title = Label("大脱獄", sidebarContent, 20, 20, 320, 50, 34);
            turn = Label("", sidebarContent, 20, 85, 320, 55, 24);
            score = Label("", sidebarContent, 20, 145, 320, 110, 21);
            mode = Label("", sidebarContent, 20, 260, 320, 38, 21);
            actions = new GameObject("ActionPanel", typeof(RectTransform));
            actions.transform.SetParent(sidebarContent, false); Stretch(actions.GetComponent<RectTransform>());
            deploy = Button("駒を配置", actions.transform, 20, 300, 320, game.SelectPlacePrisoner);
            move = Button("移動", actions.transform, 20, 355, 320, game.SelectMove);
            place = Button("柵を設置", actions.transform, 20, 410, 320, game.SelectPlaceFence);
            remove = Button("柵を破壊", actions.transform, 20, 465, 320, game.SelectRemoveFence);
            forward = Button("右へ／脱獄 →", actions.transform, 20, 520, 320, game.MoveRightOrEscape);
            message = Label("", sidebarContent, 20, 580, 320, 105, 20);
            help = Label("操作：タップ／クリック\n柵は辺をタップして設置\n柵より下の同じ縦筋を保護", sidebarContent, 20, 695, 320, 85, 17);

            handoff = Panel("HandoffPanel", safeRoot, new Color(.03f, .05f, .09f, .96f)).gameObject;
            RectTransform handoffCard = CenterCard(handoff.transform);
            handoffText = Label("", handoffCard, 30, 35, 540, 140, 28);
            Button("次のプレイヤー開始", handoffCard, 100, 210, 400, game.ConfirmHandoff);
            result = Panel("ResultPanel", safeRoot, new Color(.03f, .05f, .09f, .96f)).gameObject;
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
            UpdateLayout();
        }

        private void LateUpdate()
        {
            if (layoutWidth != Screen.width || layoutHeight != Screen.height || lastSafeArea != Screen.safeArea)
                UpdateLayout();
        }

        private void UpdateLayout()
        {
            layoutWidth = Screen.width; layoutHeight = Screen.height; lastSafeArea = Screen.safeArea;
            if (layoutWidth <= 0 || layoutHeight <= 0) return;
            bool portrait = layoutHeight > layoutWidth;
            bool compact = !portrait && (float)layoutWidth / layoutHeight >= 1.7f;
            scaler.referenceResolution = portrait ? new Vector2(800, 1280) : new Vector2(1280, 800);
            scaler.matchWidthOrHeight = 0;
            Rect safe = Screen.safeArea;
            safeRoot.anchorMin = new Vector2(safe.xMin / layoutWidth, safe.yMin / layoutHeight);
            safeRoot.anchorMax = new Vector2(safe.xMax / layoutWidth, safe.yMax / layoutHeight);
            safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
            sidebar.anchorMin = Vector2.zero;
            sidebar.anchorMax = portrait ? new Vector2(1, .44f) : new Vector2(.29f, 1);
            sidebar.offsetMin = sidebar.offsetMax = Vector2.zero;

            // Keep controls inside their panel, including short landscape screens and display cutouts.
            Canvas.ForceUpdateCanvases();
            Vector2 designSize = portrait ? new Vector2(800, 520) : new Vector2(360, compact ? 640 : 800);
            sidebarContent.anchorMin = sidebarContent.anchorMax = new Vector2(.5f, 1);
            sidebarContent.pivot = new Vector2(.5f, 1);
            sidebarContent.sizeDelta = designSize;
            sidebarContent.anchoredPosition = Vector2.zero;
            float fit = Mathf.Min(sidebar.rect.width / designSize.x, sidebar.rect.height / designSize.y);
            sidebarContent.localScale = Vector3.one * fit;

            Position(title, 20, compact ? 0 : 20, 320, 50);
            Position(turn, 20, portrait ? 75 : compact ? 50 : 85, portrait ? 400 : 320, compact ? 35 : 55);
            Position(score, 20, portrait ? 135 : compact ? 90 : 145, portrait ? 400 : 320, compact ? 75 : 110);
            Position(mode, 20, portrait ? 250 : compact ? 165 : 260, portrait ? 400 : 320, 38);
            Position(message, 20, portrait ? 295 : 580, portrait ? 400 : 320, compact ? 60 : 105);
            Position(help, 20, portrait ? 405 : 695, portrait ? 400 : 320, 85);
            help.gameObject.SetActive(!compact);
            score.fontSize = compact ? 18 : 21;
            mode.fontSize = compact ? 18 : 21;
            help.fontSize = portrait ? 20 : 17;
            Button[] buttons = { deploy, move, place, remove, forward };
            for (int i = 0; i < buttons.Length; i++)
                SetRect(buttons[i].GetComponent<RectTransform>(), portrait ? 450 : 20,
                    (portrait ? 20 : compact ? 195 : 300) + i * (portrait ? 100 : compact ? 76 : 55),
                    320, portrait ? 96 : compact ? 74 : 48);
            foreach (Button button in buttons)
            {
                RectTransform label = button.GetComponentInChildren<Text>().rectTransform;
                Stretch(label);
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                Rect board = portrait ? new Rect(0, .44f, 1, .56f) : new Rect(.29f, 0, .71f, 1);
                camera.rect = new Rect((safe.x + safe.width * board.x) / layoutWidth,
                    (safe.y + safe.height * board.y) / layoutHeight,
                    safe.width * board.width / layoutWidth, safe.height * board.height / layoutHeight);
                float aspect = camera.rect.width * layoutWidth / (camera.rect.height * layoutHeight);
                camera.orthographicSize = Mathf.Max(5.5f, 6.4f / aspect);
            }
        }

        private static void Position(Text label, float x, float y, float width, float height)
            => SetRect(label.rectTransform, x, y, width, height);

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
            for (int p = 0; p < 3; p++) score.text += $"P{p + 1}　脱獄 {game.EscapedCount(p)}/3　盤上 {game.OnBoardCount(p)} 手元 {game.ReserveCount(p)}\n";
            message.text = game.Message;
            mode.text = "選択中：" + (game.Action switch { PlayerAction.PlacePrisoner => "駒を配置", PlayerAction.Move => "移動", PlayerAction.PlaceFence => "柵を設置", _ => "柵を破壊" });
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
        }
    }
}
