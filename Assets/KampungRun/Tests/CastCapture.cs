using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KampungRun.Tests
{
    /// <summary>
    /// The whole cast in game: all 13 characters lined up on the padang by Pak Mat's house (family first),
    /// shot as a lineup plus a close-up of each face. Written to Tools/promo_frames/x_cast/&lt;tag&gt;/.
    /// Explicit: only when asked for by name.
    /// </summary>
    [Explicit, Category("Promo")]
    public class CastCapture
    {
        const int W = 1920, H = 1080, FPS = 30;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "x_cast"));
        static readonly string[] Cast = { "chr_pakmat", "chr_maksom", "chr_along", "chr_adik", "chr_aiman", "chr_mei", "chr_ravi",
            "chr_townman", "chr_townaunty", "chr_pakcik", "chr_kid", "chr_polis", "chr_datukmega" };

        RenderTexture _rt;
        Texture2D _tex;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        void Grab(string file)
        {
            var cam = Camera.main;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = _rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(file, _tex.EncodeToPNG());
        }

        [UnityTest] public IEnumerator Lineup() => Shoot("after");

        IEnumerator Shoot(string tag)
        {
            Time.captureFramerate = 0;
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(40);
            HUD.I.DebugSkipDialogue();
            foreach (var c in Object.FindObjectsByType<Canvas>()) c.enabled = false;
            Time.captureFramerate = FPS;
            _rt = new RenderTexture(W, H, 24);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var dir = Path.Combine(Root, tag);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var gm = GameManager.I;
            var city = gm.City;
            // an open stretch of the padang beside the house; the player steps out of shot
            var home = city.places["Home"];
            var spot = city.Sidewalk(home, 0f);
            var along = city.Sidewalk(home, 6f) - spot; along.y = 0f;
            var fwd = along.sqrMagnitude > 0.01f ? along.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            gm.Player.Teleport(spot - fwd * 30f + Vector3.up * 0.3f, Quaternion.LookRotation(fwd));
            var face = Quaternion.LookRotation(-right);          // everyone faces the camera
            float gap = 0.95f;
            var spawned = new GameObject[Cast.Length];
            for (int i = 0; i < Cast.Length; i++)
            {
                var pos = spot + fwd * ((i - (Cast.Length - 1) * 0.5f) * gap) + Vector3.up * 0.25f;
                if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 6f, 1 << Layers.World)) pos.y = hit.point.y;
                var npc = NPC.Spawn("cast" + i, Cast[i], Cast[i], pos, face, null);
                spawned[i] = npc.gameObject;
            }
            yield return Frames(60);
            var cam = Camera.main;
            ChaseCamera.I.enabled = false;
            var mid = spot + Vector3.up * 0.9f;
            cam.fieldOfView = 36f;
            float back = (Cast.Length * gap) * 1.25f;
            cam.transform.SetPositionAndRotation(mid - right * back + Vector3.up * 0.9f, Quaternion.LookRotation(right * back - Vector3.up * 0.75f));
            yield return Frames(3);
            Grab(Path.Combine(dir, "00_lineup.png"));
            // a close-up of each face
            cam.fieldOfView = 26f;
            for (int i = 0; i < Cast.Length; i++)
            {
                var anim = spawned[i].GetComponentInChildren<Animator>();
                var head = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : spawned[i].transform;
                var at = head.position + Vector3.up * 0.06f;
                cam.transform.SetPositionAndRotation(at - right * 1.25f + fwd * 0.35f + Vector3.up * 0.05f,
                    Quaternion.LookRotation(right * 1.25f - fwd * 0.35f - Vector3.up * 0.05f));
                yield return Frames(2);
                Grab(Path.Combine(dir, $"{i + 1:00}_{Cast[i]}.png"));
            }
            ChaseCamera.I.enabled = true;
            Time.captureFramerate = 0;
            Debug.Log($"[Cast] wrote {dir}");
        }
    }
}
