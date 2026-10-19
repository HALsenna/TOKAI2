using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    public sealed class GridManager : MonoBehaviour
    {
        public const int Width = 5, RowCount = 4;
        private const float Spacing = 1.6f;
        private readonly Dictionary<Vector2Int, GameObject> fences = new();
        private readonly Dictionary<Color, Material> materials = new();
        private Transform generated;
        private GameManager game;

        public bool IsInside(Vector2Int c) => c.x >= 0 && c.x < Width && c.y >= 0 && c.y < RowCount;
        public string CellName(Vector2Int c) => $"{(char)('A' + c.y)}{c.x}";
        public Vector3 WorldPosition(Vector2Int c) => transform.position + new Vector3((c.x - 2) * Spacing, 0, c.y * Spacing);
        public bool HasFence(Vector2Int c) => fences.ContainsKey(c);
        public int FenceCount => fences.Count;

        public Material GetMaterial(Color color)
        {
            if (!materials.TryGetValue(color, out Material material))
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
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
            for (int x = 0; x < Width; x++)
            {
                Vector2Int cell = new(x, row);
                Color color = (x + row) % 2 == 0 ? new Color(.28f, .34f, .42f) : new Color(.42f, .49f, .57f);
                GameObject tile = Primitive(PrimitiveType.Cube, CellName(cell), WorldPosition(cell), new Vector3(1.45f, .15f, 1.45f), color);
                tile.AddComponent<GridCell>().Initialize(game, cell);
                Label(CellName(cell), WorldPosition(cell) + new Vector3(-.5f, .09f, -.5f));
            }
            GameObject goal = Primitive(PrimitiveType.Cube, "EscapeGoal", WorldPosition(new Vector2Int(4, 0)) + new Vector3(.8f, .1f, 2.4f), new Vector3(.15f, .1f, 6.4f), Color.green);
            goal.GetComponent<Collider>().enabled = false;
            Label("GOAL >", WorldPosition(new Vector2Int(4, 0)) + new Vector3(1.2f, .1f, -.8f));
        }

        public Prisoner CreatePrisoner(int owner, Vector2Int cell, Color color)
        {
            GameObject piece = Primitive(PrimitiveType.Sphere, "Prisoner", WorldPosition(cell) + Vector3.up * .4f, Vector3.one * .35f, color);
            Prisoner prisoner = piece.AddComponent<Prisoner>();
            prisoner.Initialize(game, owner, cell, color);
            return prisoner;
        }

        public bool PlaceFence(Vector2Int cell)
        {
            if (!IsInside(cell) || HasFence(cell)) return false;
            GameObject fence = Primitive(PrimitiveType.Cube, $"Fence_{CellName(cell)}", WorldPosition(cell) + new Vector3(0, .4f, .58f), new Vector3(1.2f, .65f, .12f), new Color(.75f, .44f, .19f));
            fence.AddComponent<GridCell>().Initialize(game, cell);
            fences.Add(cell, fence);
            return true;
        }

        public bool RemoveFence(Vector2Int cell)
        {
            if (!fences.TryGetValue(cell, out GameObject fence)) return false;
            fences.Remove(cell); fence.SetActive(false); Destroy(fence); return true;
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
            label.text = text; label.characterSize = .1f; label.fontSize = 40; label.color = Color.white;
            label.anchor = TextAnchor.MiddleCenter;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials.Values) if (material != null) Destroy(material);
        }
    }
}
