using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Daidatsugoku
{
    /// <summary>Presentation settings only; changing these never draws a monitoring card.</summary>
    [Serializable]
    public sealed class GuardHistoryTheme
    {
        public Color Background = new(0, 0, 0, .7f);
        public Color Watching = new(220f / 255, 38f / 255, 38f / 255);
        public Color NotWatching = new(22f / 255, 163f / 255, 74f / 255);
        public Color Text = Color.white;
        public Vector2 CardSize = new(94, 176);
        public float Spacing = 16;
        public int CardFontSize = 28;
        public int MetadataFontSize = 16;
    }

    /// <summary>
    /// SYS-GUARD-001 v1.2 read-only history view. It receives only published draws.
    /// Progression is deliberately left to the game flow; opening this view never
    /// advances a turn, draws a card, pauses the clock or changes game state.
    /// </summary>
    public sealed class GuardHistoryOverlay : MonoBehaviour
    {
        [SerializeField] private GuardHistoryTheme theme = new();
        private GuardSystem guard;
        private Font font;
        private GameObject overlay, previousSelection;
        private CanvasGroup gameplayGroup;
        private bool previousInteractable, previousBlocksRaycasts, previousGroupEnabled;
        private Button launcher, closeButton;
        private Text title;
        private RectTransform viewport, content;
        private ScrollRect scroll;
        private int renderedCount = -1, renderedThemeHash;
        private GuardHistoryEntry lastEntry;
        private float renderedWidth;

        public bool IsOpen => overlay != null && overlay.activeSelf;
        public GuardHistoryTheme Theme => theme;

        public void Initialize(Transform canvas, GuardSystem monitoring, Font textFont)
        {
            guard = monitoring != null ? monitoring : throw new ArgumentNullException(nameof(monitoring));
            font = textFont;
            transform.SetParent(canvas, false);
            Stretch((RectTransform)transform);
            // All ordinary HUD controls inherit this group. The history root can
            // remain interactive while those controls reject pointer and submit input.
            gameplayGroup = canvas.GetComponent<CanvasGroup>();
            if (gameplayGroup == null) gameplayGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            CanvasGroup modalGroup = gameObject.AddComponent<CanvasGroup>();
            modalGroup.ignoreParentGroups = true;

            launcher = MakeButton("GuardHistoryButton", "監視履歴", transform, Open);
            PlaceTopRight((RectTransform)launcher.transform, 24, 20, 180, 46);

            overlay = MakeRect("GuardHistoryOverlay", transform, typeof(Image)).gameObject;
            Stretch((RectTransform)overlay.transform);
            overlay.GetComponent<Image>().color = theme.Background;
            title = MakeText("GuardHistoryTitle", "監視履歴", overlay.transform, 28, TextAnchor.MiddleLeft);
            RectTransform titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0, 1); titleRect.anchorMax = Vector2.one;
            titleRect.pivot = new Vector2(0, 1); titleRect.offsetMin = new Vector2(28, -82); titleRect.offsetMax = new Vector2(-220, -20);
            closeButton = MakeButton("GuardHistoryCloseButton", "閉じる", overlay.transform, Close);
            PlaceTopRight((RectTransform)closeButton.transform, 24, 20, 180, 46);

            RectTransform scrollArea = MakeRect("GuardHistoryScrollRect", overlay.transform, typeof(ScrollRect));
            Stretch(scrollArea); scrollArea.offsetMin = new Vector2(28, 24); scrollArea.offsetMax = new Vector2(-28, -104);
            scroll = scrollArea.GetComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36;

            viewport = MakeRect("GuardHistoryViewport", scrollArea, typeof(Image), typeof(RectMask2D));
            Stretch(viewport); viewport.offsetMax = new Vector2(-24, 0);
            viewport.GetComponent<Image>().color = Color.clear;
            content = MakeRect("GuardHistoryContent", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0, 1); content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            scroll.viewport = viewport; scroll.content = content;

            RectTransform scrollbarRect = MakeRect("GuardHistoryScrollbar", scrollArea, typeof(Image), typeof(Scrollbar));
            scrollbarRect.anchorMin = new Vector2(1, 0); scrollbarRect.anchorMax = Vector2.one;
            scrollbarRect.pivot = Vector2.one; scrollbarRect.offsetMin = new Vector2(-14, 0); scrollbarRect.offsetMax = Vector2.zero;
            scrollbarRect.GetComponent<Image>().color = new Color(1, 1, 1, .12f);
            RectTransform handle = MakeRect("GuardHistoryScrollHandle", scrollbarRect, typeof(Image));
            Stretch(handle); handle.GetComponent<Image>().color = new Color(1, 1, 1, .7f);
            Scrollbar scrollbar = scrollbarRect.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle; scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            overlay.SetActive(false);
        }

        public void Open()
        {
            if (overlay == null || IsOpen) return;
            previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            previousInteractable = gameplayGroup.interactable;
            previousBlocksRaycasts = gameplayGroup.blocksRaycasts;
            previousGroupEnabled = gameplayGroup.enabled;
            gameplayGroup.enabled = true;
            gameplayGroup.interactable = false; gameplayGroup.blocksRaycasts = false;
            transform.SetAsLastSibling(); overlay.SetActive(true);
            Canvas.ForceUpdateCanvases();
            renderedCount = -1; Refresh(); scroll.StopMovement(); scroll.verticalNormalizedPosition = 1;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(closeButton.gameObject);
        }

        public void Close()
        {
            if (!IsOpen) return;
            overlay.SetActive(false);
            RestoreGameplayInput();
            if (EventSystem.current == null) return;
            Selectable previous = previousSelection != null ? previousSelection.GetComponent<Selectable>() : null;
            GameObject destination = previous != null && previous.IsActive() && previous.IsInteractable()
                ? previousSelection : launcher.gameObject;
            EventSystem.current.SetSelectedGameObject(destination);
            previousSelection = null;
        }

        public void SetTheme(GuardHistoryTheme presentation)
        {
            theme = presentation ?? throw new ArgumentNullException(nameof(presentation));
            renderedCount = -1; Refresh();
        }

        public void Refresh()
        {
            if (!IsOpen || guard == null) return;
            var history = guard.PublicHistory;
            GuardHistoryEntry latest = history.Count > 0 ? history[history.Count - 1] : default;
            float width = Mathf.Max(1, viewport.rect.width);
            int themeHash = ThemeHash();
            if (renderedCount == history.Count && latest.Equals(lastEntry) && Mathf.Abs(renderedWidth - width) < .5f && renderedThemeHash == themeHash) return;
            float position = scroll.verticalNormalizedPosition;
            // Detach obsolete rows immediately so a refresh in this frame has
            // exactly one visible copy, even though Destroy is deferred by Unity.
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject row = content.GetChild(i).gameObject;
                row.SetActive(false); row.transform.SetParent(null, false); Destroy(row);
            }
            overlay.GetComponent<Image>().color = theme.Background;
            title.color = theme.Text; title.text = $"監視履歴　{history.Count}回";
            float cardWidth = Mathf.Max(50, theme.CardSize.x), cardHeight = Mathf.Max(100, theme.CardSize.y);
            float spacing = Mathf.Max(0, theme.Spacing);
            int columns = Mathf.Max(1, Mathf.FloorToInt((width + spacing) / (cardWidth + spacing)));
            float rowHeight = cardHeight + 54 + spacing, y = 0;
            int currentDeck = -1, column = 0;
            if (history.Count == 0)
            {
                Text empty = MakeText("GuardHistoryEmpty", "監視履歴はまだありません。", content, 22, TextAnchor.MiddleLeft);
                Place((RectTransform)empty.transform, 0, 0, width, 60); y = 60;
            }
            foreach (GuardHistoryEntry entry in history)
            {
                if (entry.DeckId != currentDeck)
                {
                    if (column > 0) { y += rowHeight; column = 0; }
                    if (currentDeck >= 0) y += spacing;
                    currentDeck = entry.DeckId;
                    Text heading = MakeText($"GuardHistoryDeck-{entry.DeckId}", $"山札 {entry.DeckId}", content, 22, TextAnchor.MiddleLeft);
                    heading.fontStyle = FontStyle.Bold; Place((RectTransform)heading.transform, 0, y, width, 42); y += 48;
                }
                float x = column * (cardWidth + spacing);
                RectTransform card = MakeRect($"GuardHistoryCard-{entry.SequenceId}", content, typeof(Image));
                Place(card, x, y, cardWidth, cardHeight);
                bool watching = entry.Result == PatrolCard.WatchAllCells;
                card.GetComponent<Image>().color = watching ? theme.Watching : theme.NotWatching;
                Text result = MakeText($"GuardHistoryResult-{entry.SequenceId}", watching ? "見\nと\nる" : "見\nと\nら\nん", card, Mathf.Max(12, theme.CardFontSize), TextAnchor.MiddleCenter);
                result.fontStyle = FontStyle.Bold; Stretch((RectTransform)result.transform);
                Text number = MakeText($"GuardHistoryNumber-{entry.SequenceId}", $"通算 {entry.SequenceId}\n{entry.CardIndex} / 30", content, Mathf.Max(10, theme.MetadataFontSize), TextAnchor.UpperCenter);
                Place((RectTransform)number.transform, x, y + cardHeight + 6, cardWidth, 48);
                column++;
                if (column == columns) { y += rowHeight; column = 0; }
            }
            if (column > 0) y += rowHeight;
            content.sizeDelta = new Vector2(0, y);
            renderedCount = history.Count; lastEntry = latest; renderedWidth = width; renderedThemeHash = themeHash;
            scroll.verticalNormalizedPosition = position;
        }

        private void LateUpdate()
        {
            if (!IsOpen) return;
            Refresh();
            EventSystem events = EventSystem.current;
            if (events != null && (events.currentSelectedGameObject == null || !events.currentSelectedGameObject.transform.IsChildOf(overlay.transform)))
                events.SetSelectedGameObject(closeButton.gameObject);
        }

        private void RestoreGameplayInput()
        {
            if (gameplayGroup == null) return;
            gameplayGroup.interactable = previousInteractable;
            gameplayGroup.blocksRaycasts = previousBlocksRaycasts;
            gameplayGroup.enabled = previousGroupEnabled;
        }

        private void OnDisable()
        {
            if (IsOpen) Close();
        }

        private int ThemeHash()
        {
            unchecked
            {
                int hash = theme.Background.GetHashCode();
                hash = hash * 31 + theme.Watching.GetHashCode(); hash = hash * 31 + theme.NotWatching.GetHashCode();
                hash = hash * 31 + theme.Text.GetHashCode(); hash = hash * 31 + theme.CardSize.GetHashCode();
                hash = hash * 31 + theme.Spacing.GetHashCode(); hash = hash * 31 + theme.CardFontSize; return hash * 31 + theme.MetadataFontSize;
            }
        }

        private static RectTransform MakeRect(string name, Transform parent, params Type[] components)
        {
            GameObject obj = new(name, typeof(RectTransform)); obj.transform.SetParent(parent, false);
            foreach (Type component in components) obj.AddComponent(component);
            return (RectTransform)obj.transform;
        }

        private Text MakeText(string name, string value, Transform parent, int size, TextAnchor alignment)
        {
            RectTransform rect = MakeRect(name, parent, typeof(Text));
            Text text = rect.GetComponent<Text>(); text.font = font; text.fontSize = size; text.color = theme.Text;
            text.text = value; text.alignment = alignment; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private Button MakeButton(string name, string text, Transform parent, UnityAction action)
        {
            RectTransform rect = MakeRect(name, parent, typeof(Image), typeof(Button));
            Image image = rect.GetComponent<Image>(); image.color = new Color(.18f, .35f, .49f);
            Button button = rect.GetComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Text label = MakeText(name + "Text", text, rect, 21, TextAnchor.MiddleCenter); Stretch((RectTransform)label.transform);
            return button;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }

        private static void PlaceTopRight(RectTransform rect, float right, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = Vector2.one; rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-right, -top); rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
