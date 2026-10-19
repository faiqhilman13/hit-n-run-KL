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
    /// The same five in-game shots of Pak Mat at home, for comparing character art passes: a close portrait,
    /// full body, the ordinary chase camera, mid-walk, and at the wheel of the family Saga. Written to
    /// Tools/promo_frames/x_pakmat/&lt;tag&gt;/. Explicit: only when asked for by name.
    /// </summary>
    [Explicit, Category("Promo")]
    public class PakMatCapture
    {
        const int W = 1600, H = 1000, FPS = 30;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "x_pakmat"));

        RenderTexture _rt;
        Texture2D _tex;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Seconds(float s) => Frames(Mathf.RoundToInt(s * FPS));

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

        /// <summary>Hold the camera at a fixed spot looking at a point (the chase camera switched off).</summary>
        static void Aim(Vector3 eye, Vector3 at, float fov = 40f)
        {
            var cam = Camera.main;
            ChaseCamera.I.enabled = false;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            cam.fieldOfView = fov;
        }

        [UnityTest] public IEnumerator Before() => Shoot("before");
        [UnityTest] public IEnumerator After() => Shoot("after");

        IEnumerator Shoot(string tag)
        {
            Time.captureFramerate = 0;
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(40);
            HUD.I.DebugSkipDialogue();
            GameInput.Locked = false;
            TouchControls.ForceOn = true;
            foreach (var c in Object.FindObjectsByType<Canvas>()) c.enabled = false;
            Time.captureFramerate = FPS;
            _rt = new RenderTexture(W, H, 24);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var dir = Path.Combine(Root, tag);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            yield return Frames(10);

            var gm = GameManager.I;
            var p = gm.Player;
            var cc = ChaseCamera.I;
            var cam = Camera.main;
            float fov0 = cam.fieldOfView;
            Debug.Log($"[PakMat] player {p.def?.name} model {p.def?.model}");

            // a quiet spot on the pavement by the house, facing along it
            var city = gm.City;
            var at = city.Sidewalk(city.places["Home"], 0f);
            var along = city.Sidewalk(city.places["Home"], 6f) - at; along.y = 0f;
            var fwd = along.sqrMagnitude > 0.01f ? along.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            p.Teleport(at + Vector3.up * 0.3f, Quaternion.LookRotation(fwd));
            yield return Seconds(2.5f);
            var feet = p.transform.position;
            float h = p.GetComponent<CharacterController>() ? p.GetComponent<CharacterController>().height : 1.7f;

            // 1. portrait: head and shoulders, a little off to his left
            var face = feet + Vector3.up * h * 0.86f;
            Aim(face + fwd * 1.35f + right * 0.55f + Vector3.up * 0.05f, face, 30f);
            yield return Frames(3);
            Grab(Path.Combine(dir, "1_portrait.png"));
            // straight on, at eye level (and what the face is doing right now)
            Aim(face + fwd * 1.5f, face, 30f);
            yield return Frames(3);
            Grab(Path.Combine(dir, "1b_front.png"));
            foreach (var smr in p.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = smr.sharedMesh;
                var w = new System.Text.StringBuilder();
                for (int i = 0; m && i < m.blendShapeCount; i++) w.Append($"{m.GetBlendShapeName(i)}={smr.GetBlendShapeWeight(i):F0} ");
                var lod = smr.GetComponentInParent<LODGroup>();
                Debug.Log($"[PakMat] {smr.name} visible={smr.isVisible} {w}");
            }

            // 2. full body three-quarter
            var mid = feet + Vector3.up * h * 0.5f;
            Aim(mid + fwd * 3.6f + right * 2.0f + Vector3.up * 0.25f, mid, 40f);
            yield return Frames(3);
            Grab(Path.Combine(dir, "2_fullbody.png"));

            // 3. the game's own camera, swung round to see him from the front-quarter
            cc.enabled = true;
            cam.fieldOfView = fov0;
            cc.target = p.transform;
            cc.SnapBehind();
            cc.yaw += 150f;
            yield return Seconds(1.2f);
            Grab(Path.Combine(dir, "3_gamecam.png"));

            // 4. mid-stride: walk him along the pavement and catch it from the side
            p.Teleport(feet + Vector3.up * 0.3f, Quaternion.LookRotation(fwd));
            yield return Frames(5);
            TouchControls.DebugStick = new Vector2(0f, 0.55f);
            yield return Seconds(1.4f);
            var walkAt = p.transform.position + Vector3.up * h * 0.5f;
            Aim(walkAt + right * 3.4f + fwd * 1.2f + Vector3.up * 0.2f, walkAt, 40f);
            yield return Frames(2);
            Grab(Path.Combine(dir, "4_walking.png"));
            TouchControls.DebugStick = Vector2.zero;
            yield return Seconds(0.6f);

            // 5. at the wheel of the Saga
            var car = gm.LastCar;
            if (car != null)
            {
                car.transform.SetPositionAndRotation(feet + right * -4f + Vector3.up * 0.5f, Quaternion.LookRotation(fwd));
                car.Body.linearVelocity = Vector3.zero;
                yield return Seconds(1f);
                p.EnterVehicle(car, true);
                yield return Seconds(1.5f);
                var seat = car.transform.position + Vector3.up * 1.1f;
                var side = car.transform.right;
                // looking in through the open window on the driver's side (right-hand drive: the right)
                Aim(seat + side * 2.3f + car.transform.forward * 1.0f + Vector3.up * 0.25f, seat + side * 0.3f, 38f);
                yield return Frames(3);
                Grab(Path.Combine(dir, "5_driving.png"));
            }
            cc.enabled = true;
            TouchControls.ForceOn = false;
            Time.captureFramerate = 0;
            Debug.Log($"[PakMat] wrote {dir}");
        }
    }
}
