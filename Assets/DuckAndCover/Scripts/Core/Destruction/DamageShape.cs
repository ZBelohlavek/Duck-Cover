using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>What caused a piece of damage. Decides how much each material takes and which limits apply.</summary>
    public enum DamageSource : byte
    {
        Bullet = 0,
        Melee = 1,
        Grenade = 2,
        StandardCharge = 3,
        HeavyCharge = 4,
    }

    public static class DamageSourceExtensions
    {
        /// <summary>Small damage can only open peek holes (the bullet cap). Charges open breaches and cost breach points.</summary>
        public static bool IsSmall(this DamageSource s) => s == DamageSource.Bullet || s == DamageSource.Melee || s == DamageSource.Grenade;
        public static bool IsCharge(this DamageSource s) => s == DamageSource.StandardCharge || s == DamageSource.HeavyCharge;
    }

    public enum ShapeKind : byte
    {
        /// <summary>The one cell under the point (a bullet or a melee hit).</summary>
        Point = 0,
        /// <summary>Every cell whose centre is within <see cref="DamageShape.radius"/> (a blast).</summary>
        Circle = 1,
        /// <summary>Every cell whose centre is inside the rectangle (a charge). Moved inside the surface
        /// when it hangs over an edge, so a charge always opens its full size.</summary>
        Rect = 2,
    }

    /// <summary>
    /// A shape on one surface's grid, in surface-local metres (u across from the left edge, v up from
    /// the bottom edge). Game code converts the world hit point; Core never sees world space here.
    /// </summary>
    [Serializable]
    public struct DamageShape
    {
        public ShapeKind kind;
        public float u;
        public float v;
        public float radius;
        public float width;
        public float height;

        public static DamageShape Point(float u, float v) => new DamageShape { kind = ShapeKind.Point, u = u, v = v };
        public static DamageShape Circle(float u, float v, float radius) => new DamageShape { kind = ShapeKind.Circle, u = u, v = v, radius = radius };
        public static DamageShape Rect(float u, float v, float width, float height) =>
            new DamageShape { kind = ShapeKind.Rect, u = u, v = v, width = width, height = height };

        /// <summary>The standard shape for a source, centred on (u, v), sized from the ruleset.</summary>
        public static DamageShape For(DestructionRuleset r, DamageSource source, float u, float v)
        {
            switch (source)
            {
                case DamageSource.Grenade: return Circle(u, v, r.grenadeRadius);
                case DamageSource.StandardCharge: return Rect(u, v, r.standardChargeWidth, r.standardChargeHeight);
                case DamageSource.HeavyCharge: return Rect(u, v, r.heavyChargeWidth, r.heavyChargeHeight);
                default: return Point(u, v);
            }
        }

        /// <summary>The cells the shape covers on a grid, in row-major order (bottom row first). This
        /// order is part of the rules: when a zone floor stops a blast part way, both machines stop on the same cell.</summary>
        public void Cells(int gridWidth, int gridHeight, float cellSize, List<int> into)
        {
            into.Clear();
            int cu = (int)Math.Floor(u / cellSize), cv = (int)Math.Floor(v / cellSize);
            switch (kind)
            {
                case ShapeKind.Point:
                    if (cu >= 0 && cv >= 0 && cu < gridWidth && cv < gridHeight) into.Add(cv * gridWidth + cu);
                    return;
                case ShapeKind.Circle:
                {
                    float r2 = radius * radius;
                    for (int y = 0; y < gridHeight; y++)
                        for (int x = 0; x < gridWidth; x++)
                        {
                            float dx = (x + 0.5f) * cellSize - u, dy = (y + 0.5f) * cellSize - v;
                            if (dx * dx + dy * dy <= r2 || (x == cu && y == cv)) into.Add(y * gridWidth + x);
                        }
                    return;
                }
                case ShapeKind.Rect:
                {
                    int w = Math.Min(gridWidth, Math.Max(1, (int)Math.Round(width / cellSize)));
                    int h = Math.Min(gridHeight, Math.Max(1, (int)Math.Round(height / cellSize)));
                    int x0 = (int)Math.Round(u / cellSize - w / 2f);
                    int y0 = (int)Math.Round(v / cellSize - h / 2f);
                    x0 = Math.Max(0, Math.Min(gridWidth - w, x0));
                    y0 = Math.Max(0, Math.Min(gridHeight - h, y0));
                    for (int y = y0; y < y0 + h; y++)
                        for (int x = x0; x < x0 + w; x++) into.Add(y * gridWidth + x);
                    return;
                }
            }
        }
    }

    /// <summary>One piece of damage to apply. <see cref="sourceTag"/> names the gadget or hero tool for events and later data rules.</summary>
    [Serializable]
    public struct DamageRequest
    {
        public int surfaceId;
        public DamageShape shape;
        public DamageSource source;
        public string sourceTag;
        public TeamId team;
        public int playerId;
        public int tick;
    }

    /// <summary>One cell that changed, with its new state and hit points. What WP3 sends and WP5 redraws.</summary>
    [Serializable]
    public struct CellChange
    {
        public SurfaceCell cell;
        public CellState state;
        public byte hp;
    }
}
