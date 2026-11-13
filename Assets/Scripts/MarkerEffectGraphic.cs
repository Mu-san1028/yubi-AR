using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Yubi
{
    // Small vector icons use the standard UI material, including in player builds.
    // No emoji/font dependency, marker texture changes, per-frame GameObjects or colliders.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MarkerEffectGraphic : MaskableGraphic
    {
        private MarkerEffectPlan plan;
        private float age;
        private Vector2 origin;
        private float unit, rotation, opacity;
        private static readonly Color Green = new Color(.38f, .94f, .63f);
        private static readonly Color Red = new Color(1f, .43f, .48f);
        private static readonly Color Blue = new Color(.43f, .74f, 1f);
        private static readonly Color Purple = new Color(.81f, .65f, 1f);
        private static readonly Color Yellow = new Color(1f, .84f, .35f);
        public bool EmitterOnly { get; set; }
        public bool BurstOnly { get; set; }
        public bool ReadingOnly { get; set; }
        public bool FusionOnly { get; set; }
        public bool OrbitOnly { get; set; }
        public bool OrbitFront { get; set; }
        public bool HighContrast { get; set; } = true;
        private readonly List<UIVertex> contrastVertices = new List<UIVertex>();
        public float Progress { get; set; }
        public float VisualOpacity { get; set; } = 1;
        public Vector2 FusionStartA, FusionStartB, FusionControlA, FusionControlB;
        public MarkerEffectSymbol FusionSymbolA, FusionSymbolB;

        public void Animate(MarkerEffectPlan value, float elapsed)
        {
            plan = value; age = elapsed; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            Populate(vh);
            if (HighContrast && !EmitterOnly && !BurstOnly && !FusionOnly)
                AddContrast(vh);
        }

        private void Populate(VertexHelper vh)
        {
            vh.Clear();
            if (plan.Primary == MarkerEffectSymbol.None) return;
            float entrance = Mathf.Clamp01(age / .38f);
            opacity = Mathf.SmoothStep(0, 1, entrance);
            float pop = 1 + .10f * Mathf.Sin(entrance * Mathf.PI);
            float side = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height);
            origin = rectTransform.rect.center; unit = side * (EmitterOnly ? .40f : .48f) * pop; rotation = 0;
            if (OrbitOnly)
            {
                unit = side * .48f;
                opacity = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - .18f) / .65f));
                DrawOrbits(vh);
                return;
            }
            if (ReadingOnly)
            {
                unit = side * .48f; opacity = 1;
                Color ink = new Color(.94f, .97f, 1f);
                Ring(vh, Vector2.zero, .82f, .035f, WithAlpha(ink, .28f), 0, 360);
                float progress = Mathf.Clamp01(Progress);
                Ring(vh, Vector2.zero, .82f, .09f, ink, 90, 90 - Mathf.Max(2, progress * 360));
                float angle = (90 - progress * 360) * Mathf.Deg2Rad;
                Disc(vh, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .82f, .075f, ink);
                return;
            }
            if (FusionOnly)
            {
                unit = side * .48f; opacity = VisualOpacity;
                float travel = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Progress - .12f) / .88f));
                DrawFusionPath(vh, FusionStartA, FusionControlA, Tint(FusionSymbolA), travel);
                DrawFusionPath(vh, FusionStartB, FusionControlB, Tint(FusionSymbolB), travel);
                float impact = Mathf.Clamp01((Progress - .8f) / .36f);
                if (impact > 0 && impact < 1)
                {
                    Ring(vh, Vector2.zero, .08f + impact * .55f, .035f, WithAlpha(Color.white, Mathf.Sin(impact * Mathf.PI)), 0, 360);
                    Disc(vh, Vector2.zero, .10f * Mathf.Sin(impact * Mathf.PI), WithAlpha(Color.white, .8f));
                }
                return;
            }
            opacity *= VisualOpacity;
            if (BurstOnly)
            {
                float burstAge = plan.Sequence && age >= 1.65f ? age - 1.65f : age;
                MarkerEffectSymbol symbol = plan.Sequence && age >= 1.65f ? plan.Accent :
                    plan.Accent == MarkerEffectSymbol.None || plan.Sequence ? plan.Primary : plan.Accent;
                opacity = Mathf.Clamp01(burstAge / .06f);
                DrawArrivalBurst(vh, symbol, burstAge);
                return;
            }
            if (EmitterOnly)
            {
                Color light = Tint(plan.Accent == MarkerEffectSymbol.None || plan.Sequence ? plan.Primary : plan.Accent);
                float pulse = .5f + .5f * Mathf.Sin(age * 2);
                Disc(vh, Vector2.zero, .76f, WithAlpha(light, .08f), 48);
                Ring(vh, Vector2.zero, .65f, .055f, WithAlpha(light, .75f), 0, 360);
                Ring(vh, Vector2.zero, .80f + .14f * pulse, .025f, WithAlpha(light, .35f), 0, 360);
                float impact = Mathf.Clamp01(age / .8f);
                if (impact < 1)
                {
                    Ring(vh, Vector2.zero, .35f + impact * 1.9f, .09f * (1 - impact) + .01f,
                        WithAlpha(light, (1 - impact) * .85f), 0, 360);
                    Ring(vh, Vector2.zero, .15f + impact * 1.5f, .035f, WithAlpha(Color.white, (1 - impact) * .55f), 0, 360);
                }
                return;
            }

            MarkerEffectSymbol primary = plan.Primary;
            bool secondPhase = plan.Sequence && age >= 1.65f;
            if (secondPhase) primary = plan.Accent;
            if (plan.Sequence)
            {
                // A one-way wait -> help story, not an endless promise to wait again.
                float progress = Mathf.Clamp01(age / 1.65f);
                Ring(vh, Vector2.zero, .97f, .035f, Yellow, -90, -90 + Mathf.Max(1, progress * 360));
            }
            float floatUp = primary == MarkerEffectSymbol.Thanks ? .06f * Mathf.Sin(age * 1.8f) : 0;
            origin += new Vector2(0, floatUp * unit);
            float mainUnit = unit;
            unit *= plan.Accent != MarkerEffectSymbol.None && !plan.Sequence ? .68f : .90f;
            if (plan.Accent != MarkerEffectSymbol.None && !plan.Sequence) origin.x -= mainUnit * .15f;
            DrawSymbol(vh, primary, secondPhase ? age - 1.65f : age);

            origin = rectTransform.rect.center; unit = mainUnit; rotation = 0;
            if (plan.Accent != MarkerEffectSymbol.None && !plan.Sequence)
            {
                origin += new Vector2(unit * .68f, unit * .68f);
                unit *= .46f;
                DrawSymbol(vh, plan.Accent, age);
            }

            origin = rectTransform.rect.center; unit = mainUnit; rotation = 0;
            Color glow = Tint(plan.Accent == MarkerEffectSymbol.None || plan.Sequence ? primary : plan.Accent);
            for (int i = 0; i < 5; i++)
            {
                float angle = (i * 72 + 18) * Mathf.Deg2Rad;
                float burst = Mathf.Clamp01(age / 1.1f);
                float radius = .80f + burst * .31f;
                Vector2 at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Color light = glow; light.a = (1 - burst) * .8f;
                Spark(vh, at, .045f + .025f * (1 - burst), light);
            }
        }

        // Two tilted circles have actual local Z depth. Split each segment and
        // bead between canvases behind/in front of the symbol, so transparent UI
        // sorting cannot draw the far half over the icon.
        private void DrawOrbits(VertexHelper vh)
        {
            MarkerEffectSymbol primary = plan.Sequence && age >= 1.65f ? plan.Accent : plan.Primary;
            for (int ring = 0; ring < 2; ring++)
            {
                Color tint = Tint(ring == 1 && plan.Accent != MarkerEffectSymbol.None && !plan.Sequence ? plan.Accent : primary);
                Quaternion plane = Quaternion.Euler(58 + ring * 20, ring == 0 ? 18 : -28,
                    (ring == 0 ? -24 : 42) + Mathf.Sin(age * .25f) * 8);
                float radius = ring == 0 ? 1.02f : .90f;
                for (int segment = 0; segment < 128; segment++)
                {
                    float a = segment * Mathf.PI * 2 / 128, b = (segment + 1) * Mathf.PI * 2 / 128;
                    Vector3 directionA = plane * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    Vector3 directionB = plane * new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0);
                    bool front = (directionA.z + directionB.z) < 0;
                    if (front != OrbitFront) continue;
                    Color light = WithAlpha(tint, front ? .65f : .26f);
                    float thickness = ring == 0 ? .012f : .010f;
                    OrbitTriangle(vh, directionA * (radius - thickness), directionA * (radius + thickness), directionB * (radius + thickness), light);
                    OrbitTriangle(vh, directionA * (radius - thickness), directionB * (radius + thickness), directionB * (radius - thickness), light);
                }
                for (int bead = 0; bead < 3; bead++)
                {
                    float angle = age * (ring == 0 ? .95f : -.72f) + bead * Mathf.PI * 2 / 3 + ring;
                    Vector3 at = plane * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
                    if ((at.z < 0) != OrbitFront) continue;
                    // Camera-facing glow and core retain the orbit's world depth.
                    OrbitDisc(vh, at, .065f, WithAlpha(tint, OrbitFront ? .15f : .07f));
                    OrbitDisc(vh, at, OrbitFront ? .026f : .020f, WithAlpha(tint, OrbitFront ? 1 : .45f));
                    OrbitDisc(vh, at, .010f, WithAlpha(Color.white, OrbitFront ? 1 : .55f));
                }
            }
        }

        private void OrbitDisc(VertexHelper vh, Vector3 at, float radius, Color tint)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2 / 12, b = (i + 1) * Mathf.PI * 2 / 12;
                OrbitTriangle(vh, at, at + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius,
                    at + new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0) * radius, tint);
            }
        }

        private void OrbitTriangle(VertexHelper vh, Vector3 a, Vector3 b, Vector3 c, Color tint)
        {
            int start = vh.currentVertCount; tint.a *= opacity;
            Vector3 center = origin;
            vh.AddVert(center + unit * a, tint, Vector2.zero);
            vh.AddVert(center + unit * b, tint, Vector2.zero);
            vh.AddVert(center + unit * c, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
        }

        private void AddContrast(VertexHelper vh)
        {
            contrastVertices.Clear(); vh.GetUIVertexStream(contrastVertices); vh.Clear();
            // Reuse the exact silhouette: no filled badge behind the marker.
            float edge = OrbitOnly ? .8f : 3;
            Color contour = new Color(.025f, .04f, .08f, OrbitOnly ? .48f : .88f);
            ContrastPass(vh, OrbitOnly ? new Vector2(1, -2) : new Vector2(4, -7), new Color(.025f, .04f, .08f, .28f));
            ContrastPass(vh, new Vector2(-edge, -edge), contour);
            ContrastPass(vh, new Vector2(-edge, edge), contour);
            ContrastPass(vh, new Vector2(edge, -edge), contour);
            ContrastPass(vh, new Vector2(edge, edge), contour);
            ContrastPass(vh, Vector2.zero, Color.white, true);
        }

        private void ContrastPass(VertexHelper vh, Vector2 offset, Color tint, bool original = false)
        {
            int start = vh.currentVertCount;
            float alpha = tint.a;
            for (int i = 0; i < contrastVertices.Count; i++)
            {
                UIVertex vertex = contrastVertices[i]; vertex.position += (Vector3)offset;
                if (!original) { tint.a = (vertex.color.a / 255f) * alpha; vertex.color = tint; }
                vh.AddVert(vertex);
            }
            for (int i = 0; i < contrastVertices.Count; i += 3) vh.AddTriangle(start + i, start + i + 1, start + i + 2);
        }

        private void DrawArrivalBurst(VertexHelper vh, MarkerEffectSymbol symbol, float time)
        {
            if (time < 0 || time >= 1.25f) return;
            Color tint = Tint(symbol);
            Vector2 start = new Vector2(0, -.72f);
            float wave = Mathf.Clamp01(time / .85f);
            Ring(vh, start, .10f + wave * 1.25f, .035f, WithAlpha(tint, (1 - wave) * .65f), 12, 168);
            for (int i = 0; i < 14; i++)
            {
                float elapsed = time - (i % 4) * .025f;
                if (elapsed <= 0 || elapsed >= 1.15f) continue;
                float t = elapsed / 1.15f;
                float angle = (22 + i * (136f / 13)) * Mathf.Deg2Rad;
                float speed = 1.15f + (i % 3) * .28f;
                Vector2 velocity = new Vector2(Mathf.Cos(angle) * .85f, Mathf.Sin(angle)) * speed;
                Vector2 at = start + velocity * t + Vector2.down * (.70f * t * t);
                float previous = Mathf.Max(0, t - .10f);
                Vector2 tail = start + velocity * previous + Vector2.down * (.70f * previous * previous);
                float fade = Mathf.Sin(t * Mathf.PI);
                Color color = WithAlpha(i % 4 == 0 ? Color.white : tint, fade * .9f);
                Segment(vh, tail, at, .018f * (1 - t), WithAlpha(tint, fade * .4f));
                float size = (.035f + (i % 3) * .012f) * (.65f + fade * .35f);
                if (symbol == MarkerEffectSymbol.Thanks && i % 3 == 0) Heart(vh, at, size * 1.7f, color);
                else if (symbol == MarkerEffectSymbol.No)
                    Segment(vh, at - (at - tail).normalized * size, at + (at - tail).normalized * size, .025f, color);
                else if (symbol == MarkerEffectSymbol.Wait || symbol == MarkerEffectSymbol.Uncertain) Disc(vh, at, size * .65f, color);
                else Spark(vh, at, size, color);
            }
        }

        private void DrawFusionPath(VertexHelper vh, Vector2 start, Vector2 control, Color tint, float travel)
        {
            Vector2 previous = start;
            for (int i = 1; i <= 24; i++)
            {
                float t = i / 24f;
                Vector2 next = (1 - t) * (1 - t) * start + 2 * (1 - t) * t * control;
                Segment(vh, previous, next, .016f, WithAlpha(tint, .32f)); previous = next;
            }
            for (int i = 0; i < 6; i++)
            {
                float t = Mathf.Max(0, travel - i * .045f);
                Vector2 at = (1 - t) * (1 - t) * start + 2 * (1 - t) * t * control;
                Disc(vh, at, .04f * (1 - i / 7f), WithAlpha(tint, 1 - i / 7f));
            }
        }

        private void DrawSymbol(VertexHelper vh, MarkerEffectSymbol symbol, float t)
        {
            Color c = Tint(symbol);
            switch (symbol)
            {
                case MarkerEffectSymbol.Yes:
                    Ring(vh, Vector2.zero, .82f + .10f * Mathf.Clamp01(t / .7f), .035f, WithAlpha(c, .5f), -80, 225);
                    Stroke(vh, .15f, c, new Vector2(-.53f, -.02f), new Vector2(-.13f, -.42f), new Vector2(.62f, .43f));
                    break;
                case MarkerEffectSymbol.No:
                    rotation = Mathf.Sin(t * 15) * 10 * Mathf.Clamp01(1 - t / .85f);
                    Stroke(vh, .15f, c, new Vector2(-.5f, -.5f), new Vector2(.5f, .5f));
                    Stroke(vh, .15f, c, new Vector2(-.5f, .5f), new Vector2(.5f, -.5f));
                    rotation = 0;
                    break;
                case MarkerEffectSymbol.Thanks:
                    Heart(vh, Vector2.zero, .9f, c);
                    Spark(vh, new Vector2(.71f, .60f), .13f, Color.white);
                    break;
                case MarkerEffectSymbol.Please:
                    rotation = -8 * Mathf.Sin(Mathf.Clamp01(t / .95f) * Mathf.PI);
                    // Two palms with sleeves: the same prayer motif as the purple marker.
                    Stroke(vh, .10f, c, new Vector2(-.57f, -.36f), new Vector2(-.30f, -.05f), new Vector2(-.11f, .70f), new Vector2(0, .77f), new Vector2(0, -.15f), new Vector2(-.27f, -.48f));
                    Stroke(vh, .10f, c, new Vector2(.57f, -.36f), new Vector2(.30f, -.05f), new Vector2(.11f, .70f), new Vector2(0, .77f), new Vector2(0, -.15f), new Vector2(.27f, -.48f));
                    Stroke(vh, .17f, c, new Vector2(-.68f, -.45f), new Vector2(-.35f, -.70f));
                    Stroke(vh, .17f, c, new Vector2(.68f, -.45f), new Vector2(.35f, -.70f));
                    rotation = 0;
                    break;
                case MarkerEffectSymbol.Help:
                    rotation = 5 * Mathf.Sin(Mathf.Clamp01(t / 1.0f) * Mathf.PI * 2) * Mathf.Clamp01(1 - t);
                    Stroke(vh, .16f, c, new Vector2(-.82f, .26f), new Vector2(-.58f, .49f), new Vector2(-.32f, .30f));
                    Stroke(vh, .16f, c, new Vector2(.82f, .26f), new Vector2(.58f, .49f), new Vector2(.31f, .31f));
                    Stroke(vh, .09f, c, new Vector2(-.64f, .13f), new Vector2(-.29f, -.26f), new Vector2(.03f, -.50f), new Vector2(.17f, -.43f), new Vector2(.52f, -.10f), new Vector2(.64f, .14f));
                    Stroke(vh, .10f, c, new Vector2(-.32f, .30f), new Vector2(-.06f, .40f), new Vector2(.32f, .26f), new Vector2(.48f, .07f));
                    Stroke(vh, .10f, c, new Vector2(.31f, .31f), new Vector2(.07f, .26f), new Vector2(-.14f, .04f), new Vector2(-.29f, .09f), new Vector2(-.18f, .26f));
                    Stroke(vh, .07f, c, new Vector2(-.21f, -.14f), new Vector2(.09f, -.38f));
                    Stroke(vh, .07f, c, new Vector2(-.03f, -.03f), new Vector2(.25f, -.27f));
                    Stroke(vh, .07f, c, new Vector2(.14f, .08f), new Vector2(.39f, -.14f));
                    rotation = 0;
                    break;
                case MarkerEffectSymbol.Wait:
                    Stroke(vh, .10f, c, new Vector2(-.48f, .70f), new Vector2(.48f, .70f));
                    Stroke(vh, .10f, c, new Vector2(-.48f, -.70f), new Vector2(.48f, -.70f));
                    Stroke(vh, .08f, c, new Vector2(-.39f, .65f), new Vector2(-.34f, .36f), new Vector2(.18f, -.18f), new Vector2(.34f, -.38f), new Vector2(.39f, -.65f));
                    Stroke(vh, .08f, c, new Vector2(.39f, .65f), new Vector2(.34f, .36f), new Vector2(-.18f, -.18f), new Vector2(-.34f, -.38f), new Vector2(-.39f, -.65f));
                    float sand = .5f - .2f * Mathf.Sin(t * 1.6f);
                    Triangle(vh, new Vector2(-.23f, sand), new Vector2(.23f, sand), new Vector2(0, .12f), WithAlpha(c, .85f));
                    Triangle(vh, new Vector2(-.27f, -.57f), new Vector2(.27f, -.57f), new Vector2(0, -.25f - .15f * sand), WithAlpha(c, .85f));
                    Stroke(vh, .035f, c, new Vector2(0, .08f), new Vector2(0, -.32f));
                    break;
                case MarkerEffectSymbol.Uncertain:
                    Ring(vh, new Vector2(0, .27f), .35f, .12f, c, -30, 210);
                    Stroke(vh, .12f, c, new Vector2(.30f, .09f), new Vector2(0, -.18f), new Vector2(0, -.31f));
                    Disc(vh, new Vector2(0, -.57f), .085f, c, 16);
                    Disc(vh, new Vector2(-.71f, 0), .12f, WithAlpha(Green, .5f + .4f * Mathf.Sin(t * 2)), 16);
                    Disc(vh, new Vector2(.71f, 0), .12f, WithAlpha(Red, .5f - .4f * Mathf.Sin(t * 2)), 16);
                    break;
            }
        }

        private static Color Tint(MarkerEffectSymbol symbol)
        {
            switch (symbol)
            {
                case MarkerEffectSymbol.Yes: return Green;
                case MarkerEffectSymbol.No: return Red;
                case MarkerEffectSymbol.Thanks: return Blue;
                case MarkerEffectSymbol.Please: return Purple;
                case MarkerEffectSymbol.Help: return Yellow;
                default: return new Color(.91f, .95f, 1f);
            }
        }

        private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
        private Vector2 Point(Vector2 value)
        {
            float angle = rotation * Mathf.Deg2Rad;
            return origin + unit * new Vector2(value.x * Mathf.Cos(angle) - value.y * Mathf.Sin(angle), value.x * Mathf.Sin(angle) + value.y * Mathf.Cos(angle));
        }

        private void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int start = vh.currentVertCount; color.a *= opacity;
            vh.AddVert(Point(a), color, Vector2.zero); vh.AddVert(Point(b), color, Vector2.zero); vh.AddVert(Point(c), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
        }

        private void Disc(VertexHelper vh, Vector2 at, float radius, Color color, int steps = 12)
        {
            if (color.a <= 0) return;
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2 / steps, b = (i + 1) * Mathf.PI * 2 / steps;
                Triangle(vh, at, at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, at + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, color);
            }
        }

        private void Segment(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 delta = (b - a).normalized;
            Vector2 side = new Vector2(-delta.y, delta.x) * width * .5f;
            Triangle(vh, a - side, a + side, b + side, color);
            Triangle(vh, a - side, b + side, b - side, color);
        }

        private void Stroke(VertexHelper vh, float width, Color color, params Vector2[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                Disc(vh, points[i], width * .5f, color);
                if (i > 0) Segment(vh, points[i - 1], points[i], width, color);
            }
        }

        private void Ring(VertexHelper vh, Vector2 center, float radius, float width, Color color, float from, float to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(to - from) / 8));
            Vector2 previous = new Vector2(Mathf.Cos(from * Mathf.Deg2Rad), Mathf.Sin(from * Mathf.Deg2Rad));
            for (int i = 1; i <= steps; i++)
            {
                float angle = Mathf.Lerp(from, to, i / (float)steps) * Mathf.Deg2Rad;
                Vector2 next = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 a = center + previous * (radius - width * .5f), b = center + previous * (radius + width * .5f);
                Vector2 c = center + next * (radius + width * .5f), d = center + next * (radius - width * .5f);
                Triangle(vh, a, b, c, color); Triangle(vh, a, c, d, color); previous = next;
            }
        }

        private void Heart(VertexHelper vh, Vector2 center, float size, Color color)
        {
            Vector2 previous = Vector2.zero;
            for (int i = 0; i <= 48; i++)
            {
                float t = i * Mathf.PI * 2 / 48;
                Vector2 next = center + size * new Vector2(16 * Mathf.Pow(Mathf.Sin(t), 3),
                    13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t)) / 18;
                if (i > 0) Triangle(vh, center, previous, next, color);
                previous = next;
            }
        }

        private void Spark(VertexHelper vh, Vector2 at, float size, Color color)
        {
            Triangle(vh, at + Vector2.up * size, at + Vector2.right * size * .3f, at + Vector2.down * size, color);
            Triangle(vh, at + Vector2.up * size, at + Vector2.left * size * .3f, at + Vector2.down * size, color);
            Triangle(vh, at + Vector2.left * size, at + Vector2.up * size * .3f, at + Vector2.right * size, color);
            Triangle(vh, at + Vector2.left * size, at + Vector2.down * size * .3f, at + Vector2.right * size, color);
        }
    }
}
