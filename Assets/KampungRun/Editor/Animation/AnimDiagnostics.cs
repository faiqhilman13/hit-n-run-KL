using System.Text;
using UnityEditor;
using UnityEngine;

namespace KampungRun.EditorTools.Anim
{
    /// <summary>
    /// Reference checks for authoring the motion library: Unity's default Humanoid muscle ranges (and which
    /// bone axis drives which muscle), and how each pose channel maps onto muscles on the reference rig - so a
    /// pose can be kept inside the ranges (Unity clamps anything past them on playback).
    ///   -executeMethod KampungRun.EditorTools.Anim.AnimDiagnostics.Limits | .Sweep
    /// </summary>
    public static class AnimDiagnostics
    {
        /// <summary>Default muscle ranges, and which muscle each bone's x/y/z limit drives.</summary>
        public static void Limits()
        {
            var sb = new StringBuilder("[limits]\n");
            for (int m = 0; m < HumanTrait.MuscleCount; m++)
            {
                string n = HumanTrait.MuscleName[m];
                if (n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little")) continue;
                sb.AppendLine($"{m,3} {n,-34} {HumanTrait.GetMuscleDefaultMin(m),6:F0} {HumanTrait.GetMuscleDefaultMax(m),6:F0}");
            }
            foreach (var b in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.Spine, HumanBodyBones.Head })
            {
                sb.Append($"{b}:");
                for (int dof = 0; dof < 3; dof++)
                {
                    int mm = HumanTrait.MuscleFromBone((int)b, dof);
                    sb.Append($"  [{"xyz"[dof]}] {(mm >= 0 ? HumanTrait.MuscleName[mm] : "-")}");
                }
                sb.AppendLine();
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>Muscle values for sweeps of single channels on the reference rig.</summary>
        public static void Sweep()
        {
            var rig = new AnimRig(AssetDatabase.LoadAssetAtPath<GameObject>(AnimationStudio.ReferenceModel));
            var sb = new StringBuilder("[sweep]\n");
            int M(string n) => System.Array.IndexOf(HumanTrait.MuscleName, n);
            void Row(string label, System.Action<Pose, float> set, float[] vals, params string[] muscles)
            {
                sb.Append(label + ":");
                foreach (var v in vals)
                {
                    var p = new Pose();
                    foreach (int s in new[] { -1, 1 }) { p[s].armDown = 80f; p[s].elbow = 10f; }
                    set(p, v);
                    rig.Apply(p);
                    var hp = rig.Capture();
                    sb.Append($"  {v,5:F0}->");
                    foreach (var m in muscles) sb.Append($"{hp.muscles[M(m)],6:F2}");
                }
                sb.AppendLine();
            }
            Row("elbow", (p, v) => p.R.elbow = v, new[] { 0f, 30f, 60f, 90f, 120f, 140f, 160f }, "Right Forearm Stretch");
            Row("armDown", (p, v) => p.R.armDown = v, new[] { 90f, 60f, 30f, 0f, -30f, -50f, -70f, -90f }, "Right Arm Down-Up", "Right Arm Front-Back");
            Row("armFwd(down80)", (p, v) => p.R.armFwd = v, new[] { -60f, -30f, 0f, 30f, 60f, 90f, 120f }, "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out");
            Row("armTwist", (p, v) => p.R.armTwist = v, new[] { -60f, -30f, 0f, 30f, 60f, 90f }, "Right Arm Twist In-Out");
            Row("shrug", (p, v) => p.R.shrug = v, new[] { -10f, 0f, 10f, 20f, 30f, 40f }, "Right Shoulder Down-Up");
            Row("protract", (p, v) => p.R.protract = v, new[] { -15f, 0f, 10f, 15f, 20f }, "Right Shoulder Front-Back");
            Row("thighFwd(foot z)", (p, v) => { p.autoHip = 0; p.R.foot = new Vector3(0, 0.3f, v); }, new[] { -0.4f, -0.2f, 0f, 0.2f, 0.4f }, "Right Upper Leg Front-Back", "Right Lower Leg Stretch");
            Row("footPitch", (p, v) => p.R.footPitch = v, new[] { -50f, -30f, 0f, 20f, 40f }, "Right Foot Up-Down");
            Row("hipsDown", (p, v) => p.hips.y = -v * 0.01f, new[] { 0f, 10f, 20f, 30f }, "Right Upper Leg Front-Back", "Right Lower Leg Stretch", "Right Foot Up-Down");
            Row("spinePitch", (p, v) => p.spinePitch = v, new[] { -30f, 0f, 30f, 45f }, "Spine Front-Back");
            Row("headPitch", (p, v) => p.headPitch = v, new[] { -40f, 0f, 30f, 45f }, "Head Nod Down-Up");
            rig.Dispose();
            Debug.Log(sb.ToString());
        }
    }
}
