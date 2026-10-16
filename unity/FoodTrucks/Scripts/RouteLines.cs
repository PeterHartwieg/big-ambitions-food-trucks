#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    // The straight lines joining a route's stops on the vehicle page's route map, drawn in the graphic's own rectangle so
    // they follow its size: points are fractions of the rectangle (u from the left, v from the bottom).
    internal sealed class RouteLines : MaskableGraphic
    {
        private readonly List<Vector2> points = new List<Vector2>();
        private bool loops;
        private float thickness = 3f;

        internal void Set(IEnumerable<Vector2> path, bool closed, float width)
        {
            points.Clear();
            points.AddRange(path);
            loops = closed;
            thickness = width;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            var count = loops ? points.Count : points.Count - 1;
            for (var i = 0; i < count; i++)
            {
                var a = Place(points[i], rect);
                var b = Place(points[(i + 1) % points.Count], rect);
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
