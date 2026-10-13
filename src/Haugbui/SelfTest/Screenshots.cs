#if DEBUG
using System.Collections;
using System.IO;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>Renders fixed-angle pictures with a temporary camera at forced noon, independent of the player view.</summary>
    internal static class Screenshots
    {
        private const int Width = 1024;
        private const int Height = 768;
        private const float Pitch = 50f;

        private static readonly int Blockers = LayerMask.GetMask("Default", "static_solid", "Default_small", "terrain", "vehicle");

        /// <summary>First of eight directions, looking down at 50°, with nothing between the camera and the target.</summary>
        private static Vector3 ClearViewpoint(Vector3 look, float distance)
        {
            Vector3 fallback = Vector3.zero;
            for (int i = 0; i < 8; i++)
            {
                Quaternion direction = Quaternion.Euler(Pitch, 45f + i * 45f, 0);
                Vector3 position = look - direction * Vector3.forward * distance;
                if (i == 0) fallback = position;
                if (!Physics.Linecast(look, position, Blockers)) return position;
            }
            return fallback;
        }

        public static IEnumerator Capture(string folder, string name, Vector3 target, float distance = 3f)
        {
            EnvMan env = EnvMan.instance;
            bool debugTime = env.m_debugTimeOfDay;
            float time = env.m_debugTime;
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.5f;
            yield return SelfTestContext.WaitFrames(3);

            var holder = new GameObject("HaugbuiSelfTestCamera");
            Camera camera = holder.AddComponent<Camera>();
            Camera main = Camera.main;
            if (main != null) camera.CopyFrom(main);
            camera.enabled = false;
            Vector3 look = target + Vector3.up * 0.7f;
            holder.transform.position = ClearViewpoint(look, distance);
            holder.transform.LookAt(look);

            var texture = new RenderTexture(Width, Height, 24);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                Object.Destroy(texture);
                Object.Destroy(image);
                Object.Destroy(holder);
                env.m_debugTimeOfDay = debugTime;
                env.m_debugTime = time;
            }
        }
    }
}
#endif
