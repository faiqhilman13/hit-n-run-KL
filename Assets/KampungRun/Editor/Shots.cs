using System.IO;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>Play-mode stills from any viewpoint (for audits and reference comparisons):
    /// `Shots.Take(eye, target, path)`. Builds the architecture detail round the eye first.</summary>
    public static class Shots
    {
        public static string Take(Vector3 eye, Vector3 target, string path, float fov = 55f, int w = 1280, int h = 720)
        {
            if (Arch.ArchCity.I != null) Arch.ArchCity.I.Prime(eye);
            var go = new GameObject("ShotCam");
            try
            {
                var cam = go.AddComponent<Camera>();
                if (Camera.main != null) cam.CopyFrom(Camera.main);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                cam.fieldOfView = fov;
                cam.farClipPlane = 3000f;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                return path;
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
