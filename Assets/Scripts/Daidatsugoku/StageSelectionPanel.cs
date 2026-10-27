using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Daidatsugoku
{
    /// <summary>Minimal pre-game selection. Unresolved stages stay visible but cannot start.</summary>
    public sealed class StageSelectionPanel : MonoBehaviour
    {
        private GameManager game;
        private Font font;
        private Text hint;
        private Button start;
        private readonly List<Button> choices = new();
        public bool IsOpen => gameObject.activeSelf;

        public void Initialize(GameManager manager, Transform canvas, Font textFont)
        {
            game = manager; font = textFont; transform.SetParent(canvas, false);
            var root = (RectTransform)transform;
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            gameObject.AddComponent<Image>().color = new Color(.03f, .05f, .09f, .96f);
            var card = new GameObject("StageSelectionCard", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(transform, false); card.GetComponent<Image>().color = new Color(.17f, .21f, .24f);
            var rect = card.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(840, 680);
            Text("ステージ選択", rect, 25, 20, 790, 48, 30);
            var scrollObject = new GameObject("StageScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollObject.transform.SetParent(rect, false); Place(scrollObject.GetComponent<RectTransform>(), 25, 84, 790, 380);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(scrollObject.transform, false); Place(viewport.GetComponent<RectTransform>(), 0, 0, 790, 380);
            viewport.GetComponent<Image>().color = Color.clear;
            var content = new GameObject("Stages", typeof(RectTransform)); content.transform.SetParent(viewport.transform, false);
            Place(content.GetComponent<RectTransform>(), 0, 0, 790, game.Stages.Count * 76);
            ScrollRect scroll = scrollObject.GetComponent<ScrollRect>(); scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = content.GetComponent<RectTransform>(); scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 36;
            int index = 0;
            foreach (FieldStage stage in game.Stages)
            {
                var errors = FieldStageValidator.Validate(stage);
                string details = errors.Count > 0 ? "データ不正" : $"{stage.Width}列 × {stage.Height}行 ／ {stage.CellCount}マス ／ "
                    + (game.CanStartStage(stage, out _) ? "開始可能" : stage.InitialFencesDefined ? "仕様確認待ち・対局不可" : "初期柵未確定・対局不可");
                Button button = Button($"Stage-{stage.StageId}", $"{stage.StageId}　{stage.StageName}\n{details}", content.transform, 0, index * 76, 790, 68);
                string id = stage.StageId; button.onClick.AddListener(() => game.SelectStage(id));
                button.interactable = errors.Count == 0; choices.Add(button); index++;
            }
            hint = Text("", rect, 25, 475, 790, 90, 20);
            start = Button("StartSelectedStage", "このステージで開始", rect, 220, 590, 400, 52);
            start.onClick.AddListener(() => game.StartSelectedStage());
            Refresh();
        }

        public void Refresh()
        {
            gameObject.SetActive(game.IsSelectingStage);
            if (!game.IsSelectingStage) return;
            hint.text = game.StageSelectionMessage;
            start.interactable = game.CanStartStage(game.SelectedStage, out _);
            for (int i = 0; i < choices.Count; i++)
                choices[i].GetComponent<Image>().color = game.SelectedStage == game.Stages[i]
                    ? new Color(.20f, .43f, .43f) : new Color(.18f, .35f, .49f);
        }

        private Text Text(string value, Transform parent, float x, float y, float width, float height, int size)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            Place(obj.GetComponent<RectTransform>(), x, y, width, height);
            Text label = obj.GetComponent<Text>(); label.font = font; label.fontSize = size;
            label.text = value; label.color = Color.white; label.raycastTarget = false;
            label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
        private Button Button(string name, string value, Transform parent, float x, float y, float width, float height)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
            Place(obj.GetComponent<RectTransform>(), x, y, width, height);
            obj.GetComponent<Image>().color = new Color(.18f, .35f, .49f);
            Button button = obj.GetComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>();
            Text label = Text(value, obj.transform, 0, 0, width, height, 21); label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
    }
}
