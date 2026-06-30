using System;
using UnityEditor;
using UnityEngine;

namespace VirtualVenues.Editor.AssetPackPublisher
{
    /// <summary>
    /// Renders a crisp, isolated icon for a prefab using <see cref="PreviewRenderUtility"/> — a controlled
    /// off-screen camera + lights (higher quality than the Inspector's 128px <c>AssetPreview</c>). Used by
    /// the Asset Pack Publisher both to show a preview in each asset row and to upload a per-asset
    /// <c>thumbnailUrl</c> at publish time.
    ///
    /// Editor-only. Every method is failure-tolerant (returns null rather than throwing) so a prefab that
    /// renders pink/empty under URP never strands the publish flow.
    /// </summary>
    public static class AssetThumbnailBaker
    {
        /// <summary>Renders <paramref name="prefab"/> to a readable square <see cref="Texture2D"/> (caller owns it
        /// and must Destroy it), or null on failure.</summary>
        public static Texture2D BakeTexture(GameObject prefab, int size = 256)
        {
            if (prefab == null || size <= 0) { return null; }

            var pru = new PreviewRenderUtility();
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab);
                instance.hideFlags = HideFlags.HideAndDontSave;
                pru.AddSingleGO(instance);

                Bounds bounds = ComputeRendererBounds(instance);
                float radius = bounds.extents.magnitude;
                if (radius < 0.0001f) { radius = 0.5f; }

                Camera cam = pru.camera;
                cam.clearFlags = CameraClearFlags.SolidColor;
                // Transparent background (alpha 0) so the icon cuts out cleanly over any UI.
                cam.backgroundColor = new Color(0.16f, 0.16f, 0.18f, 0f);
                cam.fieldOfView = 30f;
                cam.nearClipPlane = Mathf.Max(0.01f, radius * 0.01f);
                cam.farClipPlane = radius * 25f + 100f;

                // Frame the bounding sphere: distance so it fits the vertical FOV, viewed from the
                // front-top-left corner. The camera sits at center - dir, so its offset from the object is
                // -dir = (-x, +y, +z) = left / above / in-front. (dir.z is negative because the camera looks
                // back toward an object whose front faces +Z.)
                float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
                float distance = radius / Mathf.Sin(fovRad * 0.5f);
                Vector3 dir = new Vector3(0.5f, -0.5f, -1f).normalized;
                cam.transform.position = bounds.center - dir * distance * 1.1f;
                cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

                // Light relative to the CAMERA, not world axes. The camera already sits at the object's
                // front-upper-left, so a light defined in world space ("front-left") lands almost on the
                // camera axis and reads as flat / head-on. Building the key from the camera's own
                // right/up puts it a true ~45° to the VIEWER's left instead.
                Vector3 camRight = cam.transform.right;
                Vector3 camUp = cam.transform.up;
                Vector3 camFwd = cam.transform.forward;

                // Key: source at the viewer's upper-left → it shines into the scene (camFwd), toward
                // screen-right (+camRight) and downward (-camUp). ~45° off-axis to the left, ~22° above —
                // so the lit side and the highlight land on the upper-left, where the camera is looking.
                pru.lights[0].intensity = 1.15f;
                pru.lights[0].transform.rotation = Quaternion.LookRotation(
                    (camFwd + camRight * 0.9f - camUp * 0.4f).normalized, Vector3.up);
                // Fill: softer, from the viewer's lower-right, to keep the shadow side off pure black.
                pru.lights[1].intensity = 0.4f;
                pru.lights[1].transform.rotation = Quaternion.LookRotation(
                    (camFwd - camRight * 0.7f + camUp * 0.2f).normalized, Vector3.up);
                pru.ambientColor = new Color(0.3f, 0.3f, 0.32f, 1f);

                pru.BeginStaticPreview(new Rect(0, 0, size, size));
                // allowScriptableRenderPipeline: true so URP actually shades the prefab (else it renders unlit/pink).
                pru.Render(true);
                Texture2D tex = pru.EndStaticPreview() as Texture2D;
                return tex;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetThumbnailBaker] Failed to bake preview for '{prefab.name}': {ex.Message}");
                return null;
            }
            finally
            {
                if (instance != null) { UnityEngine.Object.DestroyImmediate(instance); }
                pru.Cleanup();
            }
        }

        /// <summary>Renders <paramref name="prefab"/> and returns PNG bytes, or null on failure.</summary>
        public static byte[] BakePng(GameObject prefab, int size = 256)
        {
            Texture2D tex = BakeTexture(prefab, size);
            if (tex == null) { return null; }
            try { return tex.EncodeToPNG(); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetThumbnailBaker] Failed to encode preview for '{prefab.name}': {ex.Message}");
                return null;
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        // Combined world-space bounds of all enabled, non-particle renderers under the instance.
        private static Bounds ComputeRendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default;
            bool has = false;
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) { continue; }
                if (!has) { bounds = r.bounds; has = true; }
                else { bounds.Encapsulate(r.bounds); }
            }
            if (!has) { return new Bounds(go.transform.position, Vector3.one); }
            return bounds;
        }
    }
}
