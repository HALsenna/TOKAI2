using System;
using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public sealed class GridManager : MonoBehaviour
    {
        [SerializeField] private Shader boardShader;
        private const float Spacing = 1.6f;
        private readonly Dictionary<FenceBoundary, GameObject> fences = new();
        private readonly Dictionary<Color, Material> materials = new();
        private Transform generated;
        private Transform guardPost;
        private TextMesh guardPostLabel;
        private GameManager game;
        private Font boardFont;
        private Camera boardCamera;
        private int fittedScreenWidth, fittedScreenHeight;

        public FieldStage Field { get; private set; }
        public int Width => Field?.Width ?? 0;
        public int RowCount => Field?.Height ?? 0;

        public void SetStage(FieldStage stage) => Field = stage ?? throw new ArgumentNullException(nameof(stage));
        public int GetRowLength(int y) => Field != null && Field.TryGetRow(y, out FieldRow row) ? row.EndX - row.StartX + 1 : 0;
        public int GetRowStart(int y) => Field != null && Field.TryGetRow(y, out FieldRow row) ? row.StartX : -1;
        public int GetRowEnd(int y) => Field != null && Field.TryGetRow(y, out FieldRow row) ? row.EndX : -1;
        public bool IsInside(Vector2Int c) => Field != null && Field.IsInside(new FieldCell(c.x, c.y));
        public bool IsStartCell(Vector2Int c) => Field != null && Field.IsStart(new FieldCell(c.x, c.y));
        public bool IsExitCell(Vector2Int c) => Field != null && Field.IsExit(new FieldCell(c.x, c.y));
        // Prisoner and field coordinates are now the same bottom-left global XY.
        public int GlobalColumn(Vector2Int c) => c.x;
        public VisionCell ToVisionCell(Vector2Int c) => new(RowCount - 1 - c.y, c.x);
        public bool TryGetVisionCell(int column, int topRow, out Vector2Int cell) => TryGetCell(column, RowCount - 1 - topRow, out cell);

        public Prisoner OccupantAt(Vector2Int cell, Prisoner excluding = null)
        {
            if (game == null || !IsInside(cell)) return null;
            foreach (Prisoner prisoner in game.Prisoners)
                if (prisoner != excluding && prisoner.IsOnBoard && prisoner.Cell == cell) return prisoner;
            return null;
        }
        public bool CanPlacePrisoner(Vector2Int cell) => IsStartCell(cell) && OccupantAt(cell) == null;
        public bool CanMove(Vector2Int from, Vector2Int to)
        {
            if (!IsInside(from) || !IsInside(to)) return false;
            if (from.y == to.y) return to.x == from.x + 1;
            return Mathf.Abs(to.y - from.y) == 1 && from.x == to.x;
        }
        public string CellName(Vector2Int c) => $"({c.x},{c.y})";
        public Vector3 WorldPosition(Vector2Int c) => transform.position + new Vector3((c.x - (Width - 1) * .5f) * Spacing, 0, c.y * Spacing);
        public FenceBoundary BoundaryOf(Vector2Int cell, CellEdge edge)
        {
            return edge switch
            {
                CellEdge.North => new FenceBoundary(cell.x, cell.y),
                CellEdge.South => new FenceBoundary(cell.x, cell.y - 1),
                CellEdge.West => new FenceBoundary(cell.x, cell.y, true),
                _ => new FenceBoundary(cell.x + 1, cell.y, true)
            };
        }
        public bool TryGetCell(int column, int y, out Vector2Int cell)
        {
            cell = new Vector2Int(column, y);
            return IsInside(cell);
        }
        // A live fence key identifies its cell's upper edge, never a side edge.
        public bool IsValidBoundary(FenceBoundary b) => !b.Vertical && IsInside(new Vector2Int(b.Column, b.Line));
        public bool IsFenceForbidden(Vector2Int cell) => Field != null && Contains(Field.FenceForbiddenCells, new FieldCell(cell.x, cell.y));
        public bool HasFence(FenceBoundary b) => fences.ContainsKey(b);
        // Convenience overload: the north boundary of a cell (not cell occupancy).
        public bool HasFence(Vector2Int cell) => IsInside(cell) && HasFence(BoundaryOf(cell, CellEdge.North));
        public bool IsProtected(Vector2Int cell)
        {
            if (!IsInside(cell)) return false;
            // Compatibility-only TOP shielding query. Actual discovery uses
            // SYS-VISION's cone-first calculation with ToVisionCell coordinates.
            for (int y = cell.y; y < RowCount; y++)
                if (HasFence(new FenceBoundary(cell.x, y))) return true;
            return false;
        }
        public FenceBoundary BoundaryAtPoint(Vector2Int cell, Vector3 point)
        {
            // The field specification binds a fence to the clicked cell's top.
            return BoundaryOf(cell, CellEdge.North);
        }
        public int FenceCount => fences.Count;

        // Vision's rows count down from TOP; stage coordinates count up from Y=0.
        public IReadOnlyCollection<VisionCell> GetVisionFrontFences()
        {
            var result = new List<VisionCell>();
            foreach (FenceBoundary boundary in fences.Keys)
                if (!boundary.Vertical && TryGetCell(boundary.Column, boundary.Line, out _))
                    result.Add(new VisionCell(RowCount - 1 - boundary.Line, boundary.Column));
            return result.AsReadOnly();
        }

        public Vector3 GuardWorldPosition(int column, GuardSide side)
        {
            if (column < 0 || column >= Width) throw new System.ArgumentOutOfRangeException(nameof(column));
            if (side != GuardSide.Top && side != GuardSide.Bottom) throw new System.ArgumentOutOfRangeException(nameof(side));
            return transform.position + new Vector3((column - (Width - 1) * .5f) * Spacing, 0, side == GuardSide.Top ? RowCount * Spacing : -Spacing);
        }

        public void UpdateGuardPosition(int column, GuardSide side)
        {
            if (guardPost == null || guardPostLabel == null) return;
            Vector3 position = GuardWorldPosition(column, side);
            guardPost.position = position;
            guardPostLabel.transform.position = position + Vector3.up * .09f;
            guardPostLabel.text = $"看守 {side.ToString().ToUpperInvariant()}\n列 {column}";
            guardPost.gameObject.SetActive(true); guardPostLabel.gameObject.SetActive(true);
        }

        public void HideGuardPosition()
        {
            if (guardPost != null) guardPost.gameObject.SetActive(false);
            if (guardPostLabel != null) guardPostLabel.gameObject.SetActive(false);
        }

        public Material GetMaterial(Color color)
        {
            if (!materials.TryGetValue(color, out Material material))
            {
                Shader shader = boardShader != null ? boardShader : Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { color = color };
                materials.Add(color, material);
            }
            return material;
        }

        public void Build(GameManager manager)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (!FieldStageValidator.TryValidateForPlay(Field, out string reason))
                throw new InvalidOperationException(reason);
            game = manager;
            if (generated != null) { generated.gameObject.SetActive(false); Destroy(generated.gameObject); }
            guardPost = null; guardPostLabel = null;
            fences.Clear();
            generated = new GameObject("GeneratedBoard").transform;
            generated.SetParent(transform, false);
            foreach (FieldCell fieldCell in Field.Cells)
            {
                Vector2Int cell = new(fieldCell.X, fieldCell.Y);
                Color color = IsStartCell(cell) ? new Color(.18f, .44f, .28f) : new Color(.34f, .36f, .38f);
                GameObject tile = Primitive(PrimitiveType.Cube, CellName(cell), WorldPosition(cell), new Vector3(1.45f, .15f, 1.45f), color);
                tile.AddComponent<GridCell>().Initialize(game, cell);
                DrawCellFrame(cell);
                Label(CellName(cell), WorldPosition(cell) + new Vector3(-.5f, .09f, -.5f));
                if (IsStartCell(cell)) Label("START", WorldPosition(cell) + new Vector3(0, .09f, .15f));
            }
            foreach (FieldCell exit in Field.ExitCells)
            {
                Vector3 edge = WorldPosition(new Vector2Int(exit.X, exit.Y)) + new Vector3(.8f, .1f, 0);
                GameObject goal = Primitive(PrimitiveType.Cube, $"EscapeGoal_{exit.Y}", edge, new Vector3(.1f, .1f, 1.45f), new Color(.35f, .66f, .49f));
                goal.GetComponent<Collider>().enabled = false;
            }
            guardPost = Primitive(PrimitiveType.Cube, "GuardPost", transform.position, new Vector3(1.45f, .15f, 1.45f), new Color(.17f, .2f, .27f)).transform;
            guardPost.GetComponent<Collider>().enabled = false;
            guardPostLabel = Label("看守", transform.position);
            // Do not show an invented initial position while field data is absent.
            guardPost.gameObject.SetActive(false); guardPostLabel.gameObject.SetActive(false);
            foreach (FieldCell cell in Field.InitialFences)
                if (!PlaceFence(new Vector2Int(cell.X, cell.Y)))
                    throw new InvalidOperationException($"検証済みの初期柵を配置できませんでした：({cell.X},{cell.Y})");
            boardCamera = Camera.main;
            FitCamera();
        }

        public Prisoner CreatePrisoner(int owner, Vector2Int cell, Color color)
        {
            GameObject piece = Primitive(PrimitiveType.Sphere, "Prisoner", WorldPosition(cell) + Vector3.up * .4f, Vector3.one * .35f, color);
            Prisoner prisoner = piece.AddComponent<Prisoner>();
            prisoner.Initialize(game, owner, cell, color);
            return prisoner;
        }

        public bool PlaceFence(Vector2Int cell) => IsInside(cell) && PlaceFence(BoundaryOf(cell, CellEdge.North));
        public bool PlaceFence(FenceBoundary boundary)
        {
            Vector2Int cell = new(boundary.Column, boundary.Line);
            if (!IsValidBoundary(boundary) || IsFenceForbidden(cell) || HasFence(boundary)) return false;
            Vector3 position = WorldPosition(cell) + new Vector3(0, .3f, Spacing / 2);
            GameObject fence = Primitive(PrimitiveType.Cube, $"Fence_{boundary}", position, new Vector3(1.45f, .45f, .12f), new Color(.58f, .33f, .14f));
            fence.AddComponent<FenceClick>().Initialize(game, boundary);
            fences.Add(boundary, fence);
            return true;
        }

        public bool RemoveFence(Vector2Int cell) => IsInside(cell) && RemoveFence(BoundaryOf(cell, CellEdge.North));
        public bool RemoveFence(FenceBoundary boundary)
        {
            if (!fences.TryGetValue(boundary, out GameObject fence)) return false;
            fences.Remove(boundary); fence.SetActive(false); Destroy(fence); return true;
        }

        public void RefreshPiecePositions(IReadOnlyList<Prisoner> prisoners)
        {
            if (prisoners == null) throw new ArgumentNullException(nameof(prisoners));
            var occupied = new HashSet<Vector2Int>();
            foreach (Prisoner p in prisoners)
            {
                if (!p.IsOnBoard) continue;
                if (!IsInside(p.Cell)) throw new InvalidOperationException($"囚人が存在しないマスに配置されています：{CellName(p.Cell)}");
                if (!occupied.Add(p.Cell)) throw new InvalidOperationException($"1マスに複数の囚人が存在します：{CellName(p.Cell)}");
            }
            foreach (Prisoner p in prisoners)
                if (p.IsOnBoard) p.transform.position = WorldPosition(p.Cell) + Vector3.up * .4f;
        }

        private void FitCamera()
        {
            if (boardCamera == null || !boardCamera.orthographic || Field == null) return;
            float viewportAspect = Mathf.Max(.01f, Screen.width / (float)Mathf.Max(1, Screen.height)
                * boardCamera.rect.width / Mathf.Max(.01f, boardCamera.rect.height));
            float halfHeight = (RowCount + 2f) * Spacing * .5f + .5f;
            float halfWidth = (Width + 1f) * Spacing * .5f + .5f;
            boardCamera.orthographicSize = Mathf.Max(halfHeight, halfWidth / viewportAspect);
            Vector3 position = boardCamera.transform.position;
            position.x = transform.position.x;
            position.z = transform.position.z + (RowCount - 1) * Spacing * .5f;
            boardCamera.transform.position = position;
            fittedScreenWidth = Screen.width; fittedScreenHeight = Screen.height;
        }

        private void LateUpdate()
        {
            if (generated != null && (Screen.width != fittedScreenWidth || Screen.height != fittedScreenHeight)) FitCamera();
        }

        private static bool Contains(IReadOnlyCollection<FieldCell> cells, FieldCell value)
        {
            foreach (FieldCell cell in cells) if (cell.Equals(value)) return true;
            return false;
        }

        private void DrawCellFrame(Vector2Int cell)
        {
            Vector3 center = WorldPosition(cell) + Vector3.up * .085f;
            Color color = new(.12f, .14f, .16f);
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject horizontal = Primitive(PrimitiveType.Cube, $"CellFrame_{cell.x}_{cell.y}_H{side}",
                    center + Vector3.forward * (.70f * side), new Vector3(1.45f, .01f, .035f), color);
                GameObject vertical = Primitive(PrimitiveType.Cube, $"CellFrame_{cell.x}_{cell.y}_V{side}",
                    center + Vector3.right * (.70f * side), new Vector3(.035f, .01f, 1.45f), color);
                horizontal.GetComponent<Collider>().enabled = false;
                vertical.GetComponent<Collider>().enabled = false;
            }
        }

        private GameObject Primitive(PrimitiveType type, string title, Vector3 position, Vector3 scale, Color color)
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = title; obj.transform.SetParent(generated, false);
            obj.transform.position = position; obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = GetMaterial(color);
            return obj;
        }

        private TextMesh Label(string text, Vector3 position)
        {
            GameObject obj = new("Label_" + text);
            obj.transform.SetParent(generated, false);
            obj.transform.position = position; obj.transform.rotation = Quaternion.Euler(90, 0, 0);
            TextMesh label = obj.AddComponent<TextMesh>();
            if (boardFont == null) boardFont = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic", "Meiryo", "Arial" }, 40);
            label.font = boardFont;
            label.GetComponent<MeshRenderer>().sharedMaterial = boardFont.material;
            label.text = text; label.characterSize = .1f; label.fontSize = 40; label.color = new Color(.79f, .83f, .80f);
            label.anchor = TextAnchor.MiddleCenter;
            return label;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials.Values) if (material != null) Destroy(material);
            if (boardFont != null) Destroy(boardFont);
        }
    }
}
