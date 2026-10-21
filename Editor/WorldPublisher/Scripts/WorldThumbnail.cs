using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WorldPublisher
{
    /// <summary>
    /// The world's 1280x720 JPEG thumbnail: a Scene view capture or a creator image, cover-cropped to 16:9.
    /// Every method returns null on failure. A thumbnail never blocks a publish.
    /// </summary>
    public static class WorldThumbnail
    {
        public const int Width = 1280;
        public const int Height = 720;

        /// <summary>Renders what the last active Scene view camera sees, lit and post-processed like the game.</summary>
        public static Texture2D CaptureSceneView()
        {
            Camera source = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            if (source == null) { return null; }

            RenderTexture rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prevActive = RenderTexture.active;
            GameObject camGo = EditorUtility.CreateGameObjectWithHideFlags("__WorldThumbnailCam", HideFlags.HideAndDontSave, typeof(Camera));
            try
            {
                Camera cam = camGo.GetComponent<Camera>();
                cam.CopyFrom(source); // pose, fov, clip planes, culling mask (also its targetTexture, overridden below)
                cam.enabled = false;
                cam.cameraType = CameraType.Game;
                cam.aspect = (float)Width / Height;
                cam.targetTexture = rt;
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                cam.Render();
                return ReadBack(rt);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldPublisher] Scene view thumbnail capture failed: {ex.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        /// <summary>Cover-crops any texture (compressed or non-readable too) to 1280x720. Never stretches.</summary>
        public static Texture2D FromImage(Texture src)
        {
            if (src == null || src.width <= 0 || src.height <= 0) { return null; }

            RenderTexture rt = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                float srcAspect = (float)src.width / src.height;
                float dstAspect = (float)Width / Height;
                Vector2 scale = srcAspect > dstAspect ? new Vector2(dstAspect / srcAspect, 1f) : new Vector2(1f, srcAspect / dstAspect);
                Graphics.Blit(src, rt, scale, (Vector2.one - scale) * 0.5f);
                return ReadBack(rt);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldPublisher] Could not read thumbnail image '{src.name}': {ex.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>Writes the JPEG under the project's Temp folder (survives the publish's domain reloads) and returns its path.</summary>
        public static string SaveJpg(Texture2D tex)
        {
            if (tex == null) { return null; }
            try
            {
                string dir = Path.GetFullPath(Path.Combine("Temp", "VVWorldThumbnails"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".jpg");
                File.WriteAllBytes(path, tex.EncodeToJPG(85));
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldPublisher] Could not save the thumbnail: {ex.Message}");
                return null;
            }
        }

        private static Texture2D ReadBack(RenderTexture rt)
        {
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGB24, mipChain: false) { hideFlags = HideFlags.HideAndDontSave };
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            return tex;
        }
    }
}
