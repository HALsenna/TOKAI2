using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public sealed class GridManager : MonoBehaviour
    {
        [SerializeField] private Shader boardShader;
        public const int Width = 7, RowCount = 5;
        private static readonly int[] RowLengths = { 3, 4, 5, 6, 7 };
        private static readonly int[] RowOffsets = { 2, 1, 1, 0, 0 };
        private static readonly Vector2Int[] StartingFences =
        {
            new(0, 1), new(0, 2), new(2, 2), new(4, 3), new(2, 4)
        };
        private const float Spacing = 1.6f;
        private readonly Dictionary<FenceBoundary, GameObject> fences = new();
        private readonly Dictionary<Color, Material> materials = new();
        private Transform generated;
        private GameManager game;
        private Font boardFont;

        public int GetRowLength(int row) => row >= 0 && row < RowCount ? RowLengths[row] : 0;
        public bool IsInside(Vector2Int c) => c.x >= 0 && c.x < GetRowLength(c.y);
        public bool IsExitCell(Vector2Int c) => IsInside(c) && c.x == GetRowLength(c.y) - 1;
        public int GlobalColumn(Vector2Int c) => c.x + RowOffsets[c.y];
        public bool CanMove(Vector2Int from, Vector2Int to)
        {
            if (!IsInside(from) || !IsInside(to)) return false;
            if (from.y == to.y) return to.x == from.x + 1;
            return Mathf.Abs(to.y - from.y) == 1 && GlobalColumn(from) == GlobalColumn(to);
        }
        public string CellName(Vector2Int c) => $"{(char)('A' + c.y)}{c.x}";
        public Vector3 WorldPosition(Vector2Int c) => transform.position + new Vector3((GlobalColumn(c) - 3) * Spacing, 0, (RowCount - 1 - c.y) * Spacing);
        public FenceBoundary BoundaryOf(Vector2Int cell, CellEdge edge)
        {
            int column = GlobalColumn(cell);
            return edge switch
            {
                CellEdge.North => new FenceBoundary(column, cell.y),
                CellEdge.South => new FenceBoundary(column, cell.y + 1),
                CellEdge.West => new FenceBoundary(column, cell.y, true),
                _ => new FenceBoundary(column + 1, cell.y, true)
            };
        }
        public bool TryGetCell(int column, int row, out Vector2Int cell)
        {
            cell = new Vector2Int(row >= 0 && row < RowCount ? column - RowOffsets[row] : -1, row);
            return IsInside(cell);
        }
        public bool IsValidBoundary(FenceBoundary b)
        {
            if (b.Vertical) return TryGetCell(b.Column - 1, b.Line, out _) || TryGetCell(b.Column, b.Line, out _);
            return TryGetCell(b.Column, b.Line - 1, out _) || TryGetCell(b.Column, b.Line, out _);
        }
        public bool HasFence(FenceBoundary b) => fences.ContainsKey(b);
        // Convenience overload: the north boundary of a cell (not cell occupancy).
        public bool HasFence(Vector2Int cell) => IsInside(cell) && HasFence(BoundaryOf(cell, CellEdge.North));
        public bool IsProtected(Vector2Int cell)
        {
            if (!IsInside(cell)) return false;
            for (int line = 0; line <= cell.y; line++)
                if (HasFence(new FenceBoundary(GlobalColumn(cell), line))) return true;
            return false;
        }
        public FenceBoundary BoundaryAtPoint(Vector2Int cell, Vector3 point)
        {
            Vector3 delta = point - WorldPosition(cell);
            CellEdge edge = Mathf.Abs(delta.x) > Mathf.Abs(delta.z)
                ? (delta.x > 0 ? CellEdge.East : CellEdge.West)
                : (delta.z > 0 ? CellEdge.North : CellEdge.South);
            return BoundaryOf(cell, edge);
        }
        public int FenceCount => fences.Count;

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
            game = manager;
            if (generated != null) { generated.gameObject.SetActive(false); Destroy(generated.gameObject); }
            fences.Clear();
            generated = new GameObject("GeneratedBoard").transform;
            generated.SetParent(transform, false);
            for (int row = 0; row < RowCount; row++)
            for (int x = 0; x < GetRowLength(row); x++)
            {
                Vector2Int cell = new(x, row);
                Color color = (GlobalColumn(cell) + row) % 2 == 0 ? new Color(.24f, .30f, .29f) : new Color(.33f, .39f, .36f);
                GameObject tile = Primitive(PrimitiveType.Cube, CellName(cell), WorldPosition(cell), new Vector3(1.45f, .15f, 1.45f), color);
                tile.AddComponent<GridCell>().Initialize(game, cell);
                Label(CellName(cell), WorldPosition(cell) + new Vector3(-.5f, .09f, -.5f));
                if (x == 0) Label("START", WorldPosition(cell) + new Vector3(0, .09f, .15f));
            }
            for (int row = 0; row < RowCount; row++)
            {
                Vector3 edge = WorldPosition(new Vector2Int(GetRowLength(row) - 1, row)) + new Vector3(.8f, .1f, 0);
                GameObject goal = Primitive(PrimitiveType.Cube, $"EscapeGoal_{(char)('A' + row)}", edge, new Vector3(.1f, .1f, 1.45f), new Color(.35f, .66f, .49f));
                goal.GetComponent<Collider>().enabled = false;
            }
            Vector3 guardPosition = WorldPosition(new Vector2Int(1, 0)) + new Vector3(0, 0, Spacing);
            GameObject guardPost = Primitive(PrimitiveType.Cube, "GuardPost", guardPosition, new Vector3(1.45f, .15f, 1.45f), new Color(.17f, .2f, .27f));
            guardPost.GetComponent<Collider>().enabled = false;
            Label("監視官", guardPosition + Vector3.up * .09f);
            foreach (Vector2Int cell in StartingFences) PlaceFence(cell);
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
            if (!IsValidBoundary(boundary) || HasFence(boundary)) return false;
            Vector3 position = transform.position + new Vector3((boundary.Column - 3) * Spacing, .3f, (RowCount - 1 - boundary.Line) * Spacing);
            position += boundary.Vertical ? new Vector3(-Spacing / 2, 0, 0) : new Vector3(0, 0, Spacing / 2);
            Vector3 scale = boundary.Vertical ? new Vector3(.12f, .45f, 1.45f) : new Vector3(1.45f, .45f, .12f);
            GameObject fence = Primitive(PrimitiveType.Cube, $"Fence_{boundary}", position, scale, new Color(.78f, .32f, .31f));
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
            Dictionary<Vector2Int, List<Prisoner>> groups = new();
            foreach (Prisoner p in prisoners)
            {
                if (!p.IsOnBoard) continue;
                if (!groups.TryGetValue(p.Cell, out var list)) { list = new List<Prisoner>(); groups.Add(p.Cell, list); }
                list.Add(p);
            }
            foreach (var entry in groups)
            {
                int columns = Mathf.Min(3, entry.Value.Count);
                int rows = Mathf.CeilToInt(entry.Value.Count / 3f);
                for (int i = 0; i < entry.Value.Count; i++)
                    entry.Value[i].transform.position = WorldPosition(entry.Key) + new Vector3((i % 3 - (columns - 1) * .5f) * .4f, .4f, (i / 3 - (rows - 1) * .5f) * .32f);
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

        private void Label(string text, Vector3 position)
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
        }

        private void OnDestroy()
        {
            foreach (Material material in materials.Values) if (material != null) Destroy(material);
            if (boardFont != null) Destroy(boardFont);
        }
    }
}
