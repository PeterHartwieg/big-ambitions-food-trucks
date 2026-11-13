#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    internal sealed class RouteLines : MaskableGraphic
    {
        private readonly List<(Vector2 A, Vector2 B)> segments = new List<(Vector2, Vector2)>();
        private float thickness = 3f;

        internal void Set(IList<Vector2> path, bool closed, float width)
        {
            var pieces = new List<(Vector2, Vector2)>();
            var count = closed ? path.Count : path.Count - 1;
            for (var i = 0; i < count; i++) pieces.Add((path[i], path[(i + 1) % path.Count]));
            SetSegments(pieces, width);
        }

        internal void SetSegments(IEnumerable<(Vector2 A, Vector2 B)> lines, float width)
        {
            segments.Clear();
            segments.AddRange(lines);
            thickness = width;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            foreach (var (from, to) in segments)
            {
                var a = Place(from, rect);
                var b = Place(to, rect);
                var along = b - a;
                if (along.sqrMagnitude < 0.01f) continue;
                var side = new Vector2(-along.y, along.x).normalized * (thickness / 2f);
                var start = vh.currentVertCount;
                foreach (var corner in new[] { a - side, a + side, b + side, b - side })
                {
                    var vertex = UIVertex.simpleVert;
                    vertex.position = corner;
                    vertex.color = color;
                    vh.AddVert(vertex);
                }
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }

        private static Vector2 Place(Vector2 point, Rect rect) => new Vector2(rect.xMin + point.x * rect.width, rect.yMin + point.y * rect.height);
    }
}
