/*
    Re-Volt Track Editor - Unity Edition
    A version of the track editor re-built from the ground up in Unity
    Copyright (C) 2022 Dummiesman

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using ReVolt.TrackUnit.Unity;
using System.Collections.Generic;
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Renders preview images of track modules (the real meshes and textures from the track unit)
    /// for the module library. Rendering is spread over frames and done on demand.
    /// </summary>
    public sealed class ModuleThumbnails
    {
        // far away from the track; the off-screen camera only sees what is placed here
        private static readonly Vector3 StagePosition = new Vector3(-5000f, 0f, -5000f);

        private readonly Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();
        private readonly Queue<int> pending = new Queue<int>();
        private readonly HashSet<int> queued = new HashSet<int>();

        private Camera camera;
        private int size = 128;
        private Color background = Palette.Raised;

        public void Configure(int pixelSize, Color backgroundColor)
        {
            pixelSize = Mathf.Clamp(pixelSize, 32, 512);
            if (pixelSize != size || backgroundColor != background)
            {
                size = pixelSize;
                background = backgroundColor;
                Clear();
            }
        }

        /// <summary>Returns the thumbnail, or null if it hasn't been rendered yet (it is queued).</summary>
        public Texture2D Get(int moduleId)
        {
            if (cache.TryGetValue(moduleId, out var tex))
                return tex;

            if (queued.Add(moduleId))
                pending.Enqueue(moduleId);
            return null;
        }

        /// <summary>Renders up to <paramref name="budget"/> queued thumbnails. Call from LateUpdate.</summary>
        public void Process(int budget)
        {
            while (budget-- > 0 && pending.Count > 0)
            {
                int id = pending.Dequeue();
                queued.Remove(id);
                if (!cache.ContainsKey(id))
                    cache[id] = Render(id);
            }
        }

        private void EnsureCamera()
        {
            if (camera != null)
                return;

            var go = new GameObject("Studio Thumbnail Camera") { hideFlags = HideFlags.HideAndDontSave };
            camera = go.AddComponent<Camera>();
            camera.enabled = false; // rendered manually
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 28f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 60f;
            camera.allowMSAA = true;
            camera.allowHDR = false;
        }

        private Texture2D Render(int moduleId)
        {
            var templates = TrackEditor.ProcessedTrackUnit?.UnityModules;
            if (templates == null || moduleId < 0 || moduleId >= templates.Count)
                return null;

            EnsureCamera();

            var instance = Object.Instantiate(templates[moduleId]);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.position = StagePosition;
            instance.GetComponent<ModuleInstance>().SetModuleScreenConfig();
            instance.SetActive(true);

            Texture2D result = null;
            RenderTexture rt = null;
            RenderTexture resolved = null;
            var previousActive = RenderTexture.active;
            try
            {
                var bounds = new Bounds(StagePosition, Vector3.zero);
                bool any = false;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                        continue;
                    if (any)
                        bounds.Encapsulate(renderer.bounds);
                    else
                        bounds = renderer.bounds;
                    any = true;
                }

                // three-quarter view, like the module screen in the original editor
                float radius = Mathf.Max(0.5f, bounds.extents.magnitude);
                float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
                var direction = Quaternion.Euler(34f, -38f, 0f) * Vector3.forward;
                camera.transform.position = bounds.center - direction * distance;
                camera.transform.rotation = Quaternion.LookRotation(direction);
                camera.backgroundColor = background;

                int msaa = QualitySettings.antiAliasing > 0 ? QualitySettings.antiAliasing : 4;
                rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, msaa);
                camera.targetTexture = rt;
                camera.Render();

                // resolve multisampling explicitly before reading back
                resolved = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(rt, resolved);
                RenderTexture.active = resolved;
                result = new Texture2D(size, size, TextureFormat.RGB24, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                result.Apply(false, true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"ModuleThumbnails: could not render module {moduleId}: {ex.Message}");
                result = null;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousActive;
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
                if (resolved != null)
                    RenderTexture.ReleaseTemporary(resolved);

                // Destroy is deferred to the end of the frame; hide it now so the next thumbnail doesn't include it
                instance.SetActive(false);
                Object.Destroy(instance);
            }

            return result;
        }

        public void Clear()
        {
            foreach (var tex in cache.Values)
            {
                if (tex != null)
                    Object.Destroy(tex);
            }
            cache.Clear();
            pending.Clear();
            queued.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (camera != null)
                Object.Destroy(camera.gameObject);
            camera = null;
        }
    }
}
