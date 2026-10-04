using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.EditorTools.Anim
{
    /// <summary>
    /// How each kind of person carries themselves, the way a crowd in a classic game reads at a glance: the
    /// heavy waddle (Pak Mat in his sarong, Datuk Mega), the aunty's neat short steps and swinging hips, the
    /// kids' bounce, the pakcik's slow stroll with his hands behind his back. Each style has its own idle,
    /// walk, jog, run and sprint at the same natural speeds as the default, so they share one speed table
    /// and blend the same way; skirted styles keep their strides short so the cloth isn't torn apart.
    /// </summary>
    public static partial class AnimLibrary
    {
        /// <summary>Style ids, in the order the controller's style blend uses them.</summary>
        public static readonly string[] Styles = { "", "heavy", "lady", "kid", "elder" };

        static IEnumerable<ClipDef> StyleClips()
        {
            yield return new ClipDef("idle_heavy", 4f, true, IdleHeavy);
            yield return new ClipDef("idle_lady", 4f, true, IdleLady);
            yield return new ClipDef("idle_kid", 2f, true, IdleKid);
            yield return new ClipDef("idle_elder", 4f, true, IdleElder);
            foreach (var (style, mod) in new (string, System.Action<GaitSpec, string>)[] { ("heavy", Heavy), ("lady", Lady), ("kid", Kid), ("elder", Elder) })
            {
                foreach (var (gait, spec) in new (string, System.Func<GaitSpec>)[] { ("walk", Walk), ("jog", Jog), ("run", Run), ("sprint", Sprint) })
                {
                    var g = spec();
                    mod(g, gait);
                    yield return Gait($"{gait}_{style}", g);
                }
            }
        }

        // ------------------------------------------------------------------ gait styles
        /// <summary>A big man: wide feet turned out, a waddle that rocks the shoulders over each foot, belly
        /// first, arms swinging round the belly; short steps (the sarong) taken a little quicker.</summary>
        static void Heavy(GaitSpec g, string gait)
        {
            float stride = gait == "walk" ? 0.8f : 0.72f;
            Shorten(g, stride);
            g.width += 0.035f; g.toeOut += 7f; g.kneeOut += 6f;
            g.sway *= 1.9f; g.pelvisRoll *= 1.5f; g.waddle = gait == "walk" ? 5f : 3f;
            g.pelvisYaw *= 0.7f; g.twist *= 0.8f;
            // belly first on a stroll; a run still throws the weight forward (all of it)
            g.lean += gait == "walk" ? -6f : gait == "jog" ? 0f : 4f; g.pelvisTilt -= gait == "walk" ? 2f : 0f;
            g.bob *= 1.3f;
            g.armDown -= 8f; g.armIn = -6f; g.armSwing *= 0.85f; g.elbow += 6f;
            g.swingBack *= 0.7f; g.swingFront *= 0.75f;
            g.headBob *= 1.3f;
        }

        /// <summary>The aunty: neat, narrow steps that cross toward the middle, the hips swinging, elbows in,
        /// hands a little forward; short strides so the baju kurung moves as one.</summary>
        static void Lady(GaitSpec g, string gait)
        {
            Shorten(g, gait == "walk" ? 0.8f : 0.66f);
            g.width -= 0.03f; g.toeOut -= 3f;
            g.pelvisRoll *= 1.7f; g.sway *= 1.4f; g.pelvisYaw *= 1.2f;
            g.armSwing *= 0.62f; g.armDown += 4f; g.armIn += 6f; g.armMid += 4f;
            g.elbow += gait == "walk" ? 10f : 0f; g.wrist += 10f;
            g.swingBack *= 0.6f; g.swingFront *= 0.6f;
            g.lean *= 0.6f; g.headBob *= 0.7f;
            g.strike *= 0.7f;
        }

        /// <summary>Kids: bouncy, flappy and quick.</summary>
        static void Kid(GaitSpec g, string gait)
        {
            Shorten(g, 0.9f);
            g.bob *= 1.4f; g.headBob *= 1.6f; g.chestDip *= 1.4f;
            g.armSwing *= 1.25f; g.armDown -= 6f; g.elbow -= gait == "walk" ? 4f : 18f; g.wristLag *= 1.5f;
            if (gait == "walk") g.swingFront *= 1.2f;
            else { g.swingBack *= 0.9f; g.rearLift += 0.02f; }
            g.pelvisYaw *= 1.1f;
        }

        /// <summary>The pakcik: stooped, short careful steps, hands clasped behind the back (until he runs).</summary>
        static void Elder(GaitSpec g, string gait)
        {
            Shorten(g, gait == "walk" ? 0.75f : 0.7f);
            g.lean += 10f; g.headDown -= 12f; g.headCounter = 0.9f;
            g.bob *= 0.6f; g.swingBack *= 0.6f; g.swingFront *= 0.6f; g.toeOff *= 0.7f; g.strike *= 0.7f;
            g.kneeOut += 4f; g.height -= 0.015f;
            g.armSwing *= gait == "walk" ? 0.55f : 0.8f;
        }

        /// <summary>Shorter strides at the same speed: quicker steps, the same contact length per step.</summary>
        static void Shorten(GaitSpec g, float k)
        {
            g.cycle *= k;
        }

        // ------------------------------------------------------------------ idles
        static Pose IdleHeavy(float u)
        {
            var p = Idle(u);
            float shift = Sin(u);
            foreach (int s in Sides) { p[s].foot.x = s * 0.05f; p[s].footYaw = 16f; p[s].kneeOut = 10f; }
            p.hips.x *= 1.3f;
            p.spinePitch -= 5f; p.chestPitch -= 4f; p.neckPitch += 2f; p.headPitch += 3f;
            // a hand resting on the belly that gives it a couple of pats; the other thumb hooked in the waist
            float pat = Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - 0.55f) / 0.08f)) + Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - 0.66f) / 0.08f));
            Reach(p.R, 0, Hip(0.07f, 0.25f, 0.24f + 0.03f * pat), new Vector3(1f, -0.4f, -0.2f));
            p.R.wrist = 26f - 20f * pat;
            Reach(p.L, 0, Hip(-0.2f, 0.17f, 0.06f), new Vector3(1f, 0f, -0.2f));
            p.L.wrist = 20f;
            p.headYaw = 12f * Sin(u + 0.3f);
            p.chestYaw = 3f * shift;
            return p;
        }

        static Pose IdleLady(float u)
        {
            var p = Idle(u);
            float shift = Sin(u);
            foreach (int s in Sides) { p[s].foot.x = s * -0.008f; p[s].footYaw = 6f; }
            // hip popped over the standing leg, the free knee bent in
            p.hips.x = 0.04f * shift;
            p.hipRoll = -5f * shift;
            p.spineRoll = 3.5f * shift; p.chestRoll = 1.5f * shift;
            foreach (int s in Sides) p[s].kneeOut = -4f + 4f * Pos(-s * shift);
            // hands loosely clasped in front
            foreach (int s in Sides) { Reach(p[s], 0, Hip(s * 0.035f, 0.22f, 0.2f), new Vector3(1f, -0.5f, -0.3f)); p[s].wrist = 18f; }
            p.headRoll = -6f * Sin(u - 0.05f);
            p.headYaw = 8f * Sin(u + 0.3f);
            return p;
        }

        static Pose IdleKid(float u)
        {
            // a kid can't stand still: bobbing on the knees, swinging the arms, rocking on the feet
            var p = Idle(u);
            float b = Sin(u * 2f);
            p.hips.y = -0.025f + 0.012f * b;
            p.hips.x = 0.025f * Sin(u);
            foreach (int s in Sides)
            {
                var d = p[s];
                d.armFwd = 10f + 18f * Sin(u + (s > 0 ? 0.5f : 0f));
                d.armDown = 76f;
                d.elbow = 16f + 10f * Sin(u * 2f + 0.2f);
                d.wrist = 14f + 12f * Sin(u * 2f - 0.1f);
                d.footPitch = -10f * Pos(Sin(u + (s > 0 ? 0f : 0.5f)));
            }
            p.chestYaw = 8f * Sin(u);
            p.spineYaw = 4f * Sin(u);
            p.headYaw = 16f * Sin(u + 0.2f);
            p.headRoll = 6f * Sin(u * 2f);
            p.headPitch = -4f + 3f * b;
            return p;
        }

        static Pose IdleElder(float u)
        {
            var p = Idle(u);
            p.spinePitch += 10f; p.chestPitch += 6f; p.neckPitch -= 8f; p.headPitch -= 6f;
            p.hips.y -= 0.01f;
            // hands folded over the belly, the way a pakcik waits for the bus
            foreach (int s in Sides) { p[s].kneeOut = 8f; Reach(p[s], 0, Hip(s * 0.03f, 0.2f, 0.19f), new Vector3(1f, -0.6f, -0.2f)); p[s].wrist = 22f; }
            p.headYaw = 14f * Sin(u + 0.2f);
            return p;
        }

        /// <summary>Hands clasped behind the lower back.</summary>
        static void HandsBehind(Side d, int s, float w)
        {
            Reach(d, 0, Hip(s * 0.035f, 0.18f, -0.17f), new Vector3(1f, -0.3f, -0.6f), w);
            d.wrist = 10f;
        }
    }
}
