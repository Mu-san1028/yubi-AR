using System;
using UnityEngine;
using Vuforia;
using Object = UnityEngine.Object;

namespace Yubi
{
    // The billboard rises from the tracked surface; its light stays on the marker.
    public sealed class MarkerMessageEffects : IDisposable
    {
        private readonly ObserverBehaviour[] original, additional;
        private readonly MarkerEffectGraphic graphic, emitterGraphic, burstGraphic;
        private readonly RectTransform emitter, burst;
        private readonly Canvas canvas;
        private readonly GameObject readingRoot;
        private readonly MarkerEffectGraphic[] readers = new MarkerEffectGraphic[2], ingredients = new MarkerEffectGraphic[2];
        private readonly MarkerEffectGraphic fusion;
        private readonly MarkerEffectGraphic orbitBack, orbitFront;
        private readonly Vector3[] sourcePoints = new Vector3[2], sourceNormals = new Vector3[2];
        private readonly float[] sourceWidths = new float[2];
        private readonly int[] sourceIds = new int[2];
        private int sourceCount;
        private const float CanvasSize = 400;
        public const float FusionSeconds = 1.1f;
        public GameObject Root { get; private set; }
        public int ActiveMask { get; private set; }
        public float StartedAt { get; private set; }
        public float SizeInMarkers { get; set; } = 3.5f;
        public Vector3 SourcePosition { get; private set; }
        public int ReadingMask { get; private set; }
        public float ReadingProgress { get; private set; }
        public GameObject ReadingRoot { get { return readingRoot; } }

        public MarkerMessageEffects(Transform owner, ObserverBehaviour[] originalTargets, ObserverBehaviour[] additionalTargets)
        {
            original = originalTargets; additional = additionalTargets;
            Root = new GameObject("MarkerMessageEffect", typeof(RectTransform), typeof(Canvas));
            Root.transform.SetParent(owner, false); Root.layer = 5;
            ((RectTransform)Root.transform).sizeDelta = Vector2.one * CanvasSize;
            canvas = Root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 10;
            graphic = Root.AddComponent<MarkerEffectGraphic>(); graphic.raycastTarget = false;
            var emitterObject = new GameObject("MarkerSurfaceLight", typeof(RectTransform), typeof(Canvas));
            emitterObject.transform.SetParent(Root.transform, false); emitterObject.layer = 5;
            emitter = (RectTransform)emitterObject.transform; emitter.sizeDelta = Vector2.one * CanvasSize;
            Canvas emitterCanvas = emitterObject.GetComponent<Canvas>(); emitterCanvas.overrideSorting = true; emitterCanvas.sortingOrder = 9;
            emitterGraphic = emitterObject.AddComponent<MarkerEffectGraphic>();
            emitterGraphic.EmitterOnly = true; emitterGraphic.raycastTarget = false;
            var burstObject = new GameObject("MarkerArrivalBurst", typeof(RectTransform), typeof(Canvas));
            burstObject.transform.SetParent(Root.transform, false); burstObject.layer = 5;
            burst = (RectTransform)burstObject.transform; burst.sizeDelta = Vector2.one * CanvasSize;
            Canvas burstCanvas = burstObject.GetComponent<Canvas>(); burstCanvas.overrideSorting = true; burstCanvas.sortingOrder = 8;
            burstGraphic = burstObject.AddComponent<MarkerEffectGraphic>();
            burstGraphic.BurstOnly = true; burstGraphic.raycastTarget = false;
            readingRoot = new GameObject("MarkerReadingFeedback"); readingRoot.transform.SetParent(owner, false);
            for (int i = 0; i < 2; i++)
            {
                readers[i] = CreateGraphic(readingRoot.transform, "ReadingRing" + i, 9);
                readers[i].ReadingOnly = true;
                ingredients[i] = CreateGraphic(Root.transform, "FusionIngredient" + i, 11);
                ingredients[i].gameObject.SetActive(false);
            }
            fusion = CreateGraphic(Root.transform, "MarkerFusionConnection", 8); fusion.FusionOnly = true;
            orbitBack = CreateGraphic(Root.transform, "HologramBack", 9); orbitBack.OrbitOnly = true;
            orbitFront = CreateGraphic(Root.transform, "HologramFront", 12); orbitFront.OrbitOnly = true; orbitFront.OrbitFront = true;
            fusion.gameObject.SetActive(false); readingRoot.SetActive(false);
            Root.SetActive(false);
        }

        private static MarkerEffectGraphic CreateGraphic(Transform parent, string name, int order)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            item.transform.SetParent(parent, false); item.layer = 5;
            ((RectTransform)item.transform).sizeDelta = Vector2.one * CanvasSize;
            Canvas layer = item.GetComponent<Canvas>(); layer.renderMode = RenderMode.WorldSpace;
            layer.overrideSorting = true; layer.sortingOrder = order;
            MarkerEffectGraphic result = item.AddComponent<MarkerEffectGraphic>(); result.raycastTarget = false;
            return result;
        }

        public void UpdateReading(int mask, float progress, bool simulated, Camera camera)
        {
            if (camera == null || !camera.isActiveAndEnabled || !TrySource(mask, simulated, camera, out Vector3 center, out _, out _))
            { Clear(); return; }
            ActiveMask = 0; Root.SetActive(false); readingRoot.SetActive(true);
            ReadingMask = mask; ReadingProgress = Mathf.Clamp01(progress); SourcePosition = center;
            for (int i = 0; i < readers.Length; i++)
            {
                readers[i].gameObject.SetActive(i < sourceCount);
                if (i >= sourceCount) continue;
                float width = VisibleWidth(sourcePoints[i], sourceWidths[i], camera);
                PoseSurface(readers[i].transform, sourcePoints[i], sourceNormals[i], width, camera);
                readers[i].transform.localScale = Vector3.one * width * 1.8f / CanvasSize;
                readers[i].Progress = ReadingProgress;
                // Neutral white rings cannot reveal whether a practice answer is right.
                readers[i].Animate(new MarkerEffectPlan(MarkerEffectSymbol.Yes), 0);
            }
        }

        public void UpdateEffect(int mask, float now, bool simulated, Camera camera, float screenFraction = .48f)
        {
            MarkerEffectPlan plan = MarkerEffectRules.Resolve(mask);
            if (plan.Primary == MarkerEffectSymbol.None || camera == null || !camera.isActiveAndEnabled ||
                !TrySource(mask, simulated, camera, out Vector3 source, out Vector3 normal, out float markerWidth))
            {
                Clear(); return;
            }
            if (ActiveMask != mask) { ActiveMask = mask; StartedAt = now; }
            ReadingMask = 0; ReadingProgress = 0; readingRoot.SetActive(false);
            float age = Mathf.Max(0, now - StartedAt);
            bool pair = sourceCount == 2;
            float effectAge = pair ? Mathf.Max(0, age - FusionSeconds) : age;
            float entrance = Mathf.Clamp01(effectAge / .85f);
            float back = entrance - 1;
            float rise = 1 + 2.9f * back * back * back + 1.9f * back * back;
            SourcePosition = source;
            // Old databases use much larger authored units. Limit the displacement,
            // too, when a target's declared width would fill the entire camera view.
            markerWidth = VisibleWidth(source, markerWidth, camera);
            Vector3 position = pair ? source + normal * markerWidth * 1.02f + camera.transform.up * markerWidth * .6f :
                source + normal * markerWidth * (.12f + .9f * rise) + camera.transform.up * markerWidth * .6f * rise;
            float depth = Vector3.Dot(position - camera.transform.position, camera.transform.forward);
            if (depth <= camera.nearClipPlane) { Clear(); return; }
            float maxPixels = Mathf.Min(camera.pixelWidth, camera.pixelHeight) * screenFraction;
            float worldPerPixel = camera.orthographic ? 2 * camera.orthographicSize / camera.pixelHeight :
                2 * depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) / camera.pixelHeight;
            float size = Mathf.Min(markerWidth * Mathf.Max(1, SizeInMarkers), maxPixels * worldPerPixel);
            float entranceScale = Mathf.LerpUnclamped(.10f, 1, rise);
            float tilt = effectAge < 1.1f ? 14 * Mathf.Sin(effectAge * 10) * Mathf.Exp(-effectAge * 5) : 0;
            Root.SetActive(true); canvas.worldCamera = camera;
            Quaternion facing = Quaternion.LookRotation(camera.transform.forward, camera.transform.up);
            Root.transform.SetPositionAndRotation(position, facing * Quaternion.AngleAxis(tilt, Vector3.forward));
            Root.transform.localScale = Vector3.one * size * entranceScale / CanvasSize;
            bool formed = !pair || age >= FusionSeconds;
            graphic.enabled = formed;
            graphic.Animate(plan, effectAge);
            orbitBack.gameObject.SetActive(formed); orbitFront.gameObject.SetActive(formed);
            orbitBack.Animate(plan, effectAge); orbitFront.Animate(plan, effectAge);

            // The ground light remains independent of the rising icon's size and pose.
            PoseSurface(emitter, source, normal, markerWidth, camera);
            emitter.localScale = Vector3.one * markerWidth * 1.5f / (size * entranceScale);
            emitterGraphic.Animate(plan, effectAge);
            // A camera-facing fountain starts at the surface, behind the main symbol.
            // Reuse the same objects and mesh; the one-shot burst settles while held.
            float burstAge = plan.Sequence && effectAge >= 1.65f ? effectAge - 1.65f : effectAge;
            burst.gameObject.SetActive(formed && burstAge < 1.25f);
            if (burst.gameObject.activeSelf)
            {
                float burstSize = size * 1.25f;
                burst.SetPositionAndRotation(source + camera.transform.up * burstSize * .35f, facing);
                burst.localScale = Vector3.one * burstSize / (size * entranceScale);
                burstGraphic.Animate(plan, effectAge);
            }
            UpdateFusion(plan, age, pair, size, entranceScale, markerWidth, position, facing, camera);
        }

        private void UpdateFusion(MarkerEffectPlan plan, float age, bool pair, float size, float entranceScale,
            float width, Vector3 destination, Quaternion facing, Camera camera)
        {
            fusion.gameObject.SetActive(pair && age < FusionSeconds + .18f);
            for (int i = 0; i < 2; i++) ingredients[i].gameObject.SetActive(pair && age < FusionSeconds);
            if (!pair || !fusion.gameObject.activeSelf) return;
            float phase = age / FusionSeconds;
            float travel = Mathf.SmoothStep(0, 1, Mathf.Clamp01((phase - .12f) / .88f));
            float span = Mathf.Max(size * 1.2f, Vector3.Distance(sourcePoints[0], sourcePoints[1]) * 1.4f);
            fusion.transform.SetPositionAndRotation(destination, facing);
            fusion.transform.localScale = Vector3.one * span / (size * entranceScale);
            fusion.Progress = phase; fusion.VisualOpacity = 1 - Mathf.Clamp01((age - FusionSeconds) / .18f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 start = sourcePoints[i] + camera.transform.up * width * .45f;
                Vector3 control = (start + destination) * .5f + camera.transform.up * width * .4f;
                Vector3 at = Vector3.Lerp(Vector3.Lerp(start, control, travel), Vector3.Lerp(control, destination, travel), travel);
                ingredients[i].transform.SetPositionAndRotation(at, facing);
                ingredients[i].transform.localScale = Vector3.one * Mathf.Lerp(.65f, .16f, travel) / entranceScale;
                ingredients[i].VisualOpacity = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((travel - .70f) / .30f));
                MarkerEffectPlan ingredient = MarkerEffectRules.Resolve(1 << sourceIds[i]);
                ingredients[i].Animate(ingredient, age + .4f);
                Vector3 localStart = fusion.transform.InverseTransformPoint(start) / (CanvasSize * .48f);
                Vector3 localControl = fusion.transform.InverseTransformPoint(control) / (CanvasSize * .48f);
                if (i == 0) { fusion.FusionStartA = localStart; fusion.FusionControlA = localControl; fusion.FusionSymbolA = ingredient.Primary; }
                else { fusion.FusionStartB = localStart; fusion.FusionControlB = localControl; fusion.FusionSymbolB = ingredient.Primary; }
            }
            fusion.Animate(plan, age);
        }

        private static float VisibleWidth(Vector3 source, float width, Camera camera)
        {
            float depth = Vector3.Dot(source - camera.transform.position, camera.transform.forward);
            float perPixel = camera.orthographic ? 2 * camera.orthographicSize / camera.pixelHeight :
                2 * depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) / camera.pixelHeight;
            return Mathf.Min(width, Mathf.Min(camera.pixelWidth, camera.pixelHeight) * .18f * perPixel);
        }

        private static void PoseSurface(Transform item, Vector3 source, Vector3 normal, float width, Camera camera)
        {
            item.SetPositionAndRotation(source + normal * width * .025f,
                Quaternion.LookRotation(normal, Mathf.Abs(Vector3.Dot(normal, camera.transform.up)) > .95f ? camera.transform.forward : camera.transform.up));
        }

        private bool TrySource(int mask, bool simulated, Camera camera, out Vector3 source, out Vector3 normal, out float width)
        {
            source = Vector3.zero; normal = Vector3.zero; width = 0;
            sourceCount = 0;
            if (!MarkerMessageRules.IsSupportedCombination(mask)) return false;
            int total = (mask & (mask - 1)) == 0 ? 1 : 2;
            int count = 0;
            for (int i = 0; i < MarkerMessageRules.MarkerCount; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                ObserverBehaviour target = simulated ? null : Tracked(additional, i) ?? Tracked(original, i);
                if (!simulated && target == null) return false;
                Vector3 point = simulated ? camera.ViewportToWorldPoint(new Vector3(.5f, .49f, .25f)) +
                    camera.transform.right * (count - (total - 1) * .5f) * .045f : target.transform.position;
                Vector3 screen = camera.WorldToScreenPoint(point);
                if (screen.z <= camera.nearClipPlane || !camera.pixelRect.Contains(screen)) return false;
                source += point;
                Vector3 facing = simulated ? (camera.transform.up - camera.transform.forward * .35f).normalized : target.transform.up;
                if (Vector3.Dot(facing, camera.transform.position - point) < 0) facing = -facing;
                normal += facing;
                var image = target as ImageTargetBehaviour;
                float itemWidth = image != null ? Mathf.Max(.001f, image.GetSize().x * Mathf.Abs(target.transform.lossyScale.x)) : .02f;
                sourcePoints[count] = point; sourceNormals[count] = facing; sourceWidths[count] = itemWidth; sourceIds[count] = i;
                width += itemWidth;
                count++;
            }
            if (count == 0) return false;
            sourceCount = count;
            source /= count; width /= count;
            normal = normal.sqrMagnitude > .001f ? normal.normalized : (camera.transform.position - source).normalized;
            return true;
        }

        private static ObserverBehaviour Tracked(ObserverBehaviour[] targets, int index)
        {
            if (targets == null || index >= targets.Length) return null;
            ObserverBehaviour target = targets[index];
            return target != null && target.isActiveAndEnabled && target.TargetStatus.Status == Status.TRACKED ? target : null;
        }

        public void Clear()
        {
            ActiveMask = 0;
            ReadingMask = 0; ReadingProgress = 0;
            if (readingRoot != null) readingRoot.SetActive(false);
            if (Root != null) Root.SetActive(false);
        }

        public void Dispose()
        {
            Clear();
            if (Root == null) return;
            if (Application.isPlaying) { Object.Destroy(Root); Object.Destroy(readingRoot); }
            else { Object.DestroyImmediate(Root); Object.DestroyImmediate(readingRoot); }
        }
    }
}
