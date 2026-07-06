using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AIToUGUIDashedBorderGraphic : MaskableGraphic
    {
        [SerializeField] private float _thickness = 2f;
        [SerializeField] private float _dashLength = 14f;
        [SerializeField] private float _gapLength = 8f;
        [SerializeField] private float _cornerRadius;
        [SerializeField] private bool _forceEllipse;

        public void Configure(float thickness, Color borderColor, float cornerRadius, bool forceEllipse, float dashLength = 14f, float gapLength = 8f)
        {
            _thickness = Mathf.Max(0.5f, thickness);
            _cornerRadius = Mathf.Max(0f, cornerRadius);
            _forceEllipse = forceEllipse;
            _dashLength = Mathf.Max(1f, Mathf.Round(dashLength));
            _gapLength = Mathf.Max(0f, Mathf.Round(gapLength));
            color = borderColor;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var drawColor = color;
            if (drawColor.a <= 0.001f || _thickness <= 0.001f)
            {
                return;
            }

            var rect = rectTransform.rect;
            if (rect.width <= _thickness || rect.height <= _thickness)
            {
                return;
            }

            var points = _forceEllipse || IsEllipse(rect, _cornerRadius)
                ? BuildEllipsePath(rect, Mathf.Max(48, Mathf.CeilToInt((rect.width + rect.height) * 0.2f)))
                : BuildRoundedRectPath(rect, _cornerRadius);

            if (points.Count < 2)
            {
                return;
            }

            points.Add(points[0]);
            var dashPeriod = Mathf.Max(1f, _dashLength + _gapLength);
            var carriedDistance = 0f;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var start = points[i];
                var end = points[i + 1];
                var segment = end - start;
                var segmentLength = segment.magnitude;
                if (segmentLength <= 0.001f)
                {
                    continue;
                }

                var direction = segment / segmentLength;
                var remaining = segmentLength;
                var cursor = 0f;
                while (remaining > 0.001f)
                {
                    var phase = Mathf.Repeat(carriedDistance, dashPeriod);
                    var visibleRemaining = phase < _dashLength ? _dashLength - phase : 0f;
                    var hiddenRemaining = phase >= _dashLength ? dashPeriod - phase : 0f;
                    var step = visibleRemaining > 0f ? Mathf.Min(visibleRemaining, remaining) : Mathf.Min(hiddenRemaining, remaining);
                    if (step <= 0.001f)
                    {
                        break;
                    }

                    if (visibleRemaining > 0f)
                    {
                        var dashStart = start + direction * cursor;
                        var dashEnd = dashStart + direction * step;
                        AddLineQuad(vh, dashStart, dashEnd, _thickness, drawColor);
                    }

                    cursor += step;
                    carriedDistance += step;
                    remaining -= step;
                }
            }
        }

        private static bool IsEllipse(Rect rect, float cornerRadius)
        {
            var targetRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
            return rect.width > 0.001f &&
                   rect.height > 0.001f &&
                   Mathf.Abs(rect.width - rect.height) <= Mathf.Max(2f, rect.width * 0.04f) &&
                   cornerRadius >= targetRadius - 1f;
        }

        private static List<Vector2> BuildEllipsePath(Rect rect, int segments)
        {
            var points = new List<Vector2>(segments);
            var center = rect.center;
            var radiusX = Mathf.Max(0f, rect.width * 0.5f);
            var radiusY = Mathf.Max(0f, rect.height * 0.5f);
            for (var i = 0; i < segments; i++)
            {
                var angle = (i / (float)segments) * Mathf.PI * 2f;
                points.Add(new Vector2(
                    center.x + Mathf.Cos(angle) * radiusX,
                    center.y + Mathf.Sin(angle) * radiusY));
            }

            return points;
        }

        private static List<Vector2> BuildRoundedRectPath(Rect rect, float radius)
        {
            var clampedRadius = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
            if (clampedRadius <= 0.001f)
            {
                return new List<Vector2>
                {
                    new Vector2(rect.xMin, rect.yMax),
                    new Vector2(rect.xMax, rect.yMax),
                    new Vector2(rect.xMax, rect.yMin),
                    new Vector2(rect.xMin, rect.yMin),
                };
            }

            var points = new List<Vector2>();
            // Walk the perimeter clockwise so dash spacing stays on the outer edge.
            AddArc(points, new Vector2(rect.xMax - clampedRadius, rect.yMax - clampedRadius), clampedRadius, 0f, 90f);
            AddArc(points, new Vector2(rect.xMin + clampedRadius, rect.yMax - clampedRadius), clampedRadius, 90f, 180f);
            AddArc(points, new Vector2(rect.xMin + clampedRadius, rect.yMin + clampedRadius), clampedRadius, 180f, 270f);
            AddArc(points, new Vector2(rect.xMax - clampedRadius, rect.yMin + clampedRadius), clampedRadius, 270f, 360f);
            return points;
        }

        private static void AddArc(List<Vector2> points, Vector2 center, float radius, float startDegrees, float endDegrees)
        {
            var steps = Mathf.Max(4, Mathf.CeilToInt(radius * 0.35f));
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var angle = Mathf.Lerp(startDegrees, endDegrees, t) * Mathf.Deg2Rad;
                points.Add(new Vector2(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius));
            }
        }

        private static void AddLineQuad(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color color)
        {
            var segment = end - start;
            if (segment.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var direction = segment.normalized;
            var normal = new Vector2(-direction.y, direction.x);
            var half = thickness * 0.5f;
            var feather = Mathf.Min(1.25f, Mathf.Max(0.35f, half * 0.55f));
            var coreHalf = Mathf.Max(0f, half - feather);

            if (coreHalf > 0.001f)
            {
                AddQuad(vh, start - normal * coreHalf, start + normal * coreHalf, end + normal * coreHalf, end - normal * coreHalf, color, color, color, color);
            }

            var transparent = color;
            transparent.a = 0f;

            AddQuad(
                vh,
                start + normal * coreHalf,
                start + normal * (half + feather),
                end + normal * (half + feather),
                end + normal * coreHalf,
                color,
                transparent,
                transparent,
                color);

            AddQuad(
                vh,
                start - normal * (half + feather),
                start - normal * coreHalf,
                end - normal * coreHalf,
                end - normal * (half + feather),
                transparent,
                color,
                color,
                transparent);
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color colorA, Color colorB, Color colorC, Color colorD)
        {
            var vertex = UIVertex.simpleVert;
            var index = vh.currentVertCount;

            vertex.position = a;
            vertex.color = colorA;
            vh.AddVert(vertex);

            vertex.position = b;
            vertex.color = colorB;
            vh.AddVert(vertex);

            vertex.position = c;
            vertex.color = colorC;
            vh.AddVert(vertex);

            vertex.position = d;
            vertex.color = colorD;
            vh.AddVert(vertex);

            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
