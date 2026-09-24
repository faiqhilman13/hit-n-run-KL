using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// Builds the shared humanoid AnimatorController from chr_aiman's clips. Because the
    /// clips are Humanoid (muscle space) every KL character can use this one controller.
    ///   Params: Speed (m/s), Grounded, Riding, Sitting, Wave, Panic,
    ///           Punch / Kick / Knock / Mount / Dismount (triggers)
    /// </summary>
    public static class HumanControllerBuilder
    {
        public const string Path = "Assets/KampungRun/Animation/KL_Human.controller";
        public const float WalkSpeed = 2.55f, RunSpeed = 6.5f;   // natural stride speeds of the walk / run clips
        const string Source = "Assets/Models/KL/chr_aiman.fbx";

        [MenuItem("Kampung Run/Build Humanoid Animator")]
        public static RuntimeAnimatorController Build()
        {
            var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(Source).OfType<AnimationClip>()
                .ToDictionary(c => c.name, c => c);
            if (!clips.ContainsKey("idle")) { Debug.LogError("[KL] chr_aiman clips missing"); return null; }
            System.IO.Directory.CreateDirectory("Assets/KampungRun/Animation");
            AssetDatabase.DeleteAsset(Path);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(Path);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Riding", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Sitting", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Wave", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Panic", AnimatorControllerParameterType.Bool);
            ac.AddParameter("LocoSpeed", AnimatorControllerParameterType.Float);
            foreach (var t in new[] { "Punch", "Kick", "Knock", "Hit", "Mount", "Dismount" })
                ac.AddParameter(t, AnimatorControllerParameterType.Trigger);
            ac.parameters = ac.parameters.Select(p =>
            {
                if (p.name == "Grounded") p.defaultBool = true;
                if (p.name == "LocoSpeed") p.defaultFloat = 1f;
                return p;
            }).ToArray();

            var sm = ac.layers[0].stateMachine;
            // locomotion blend tree: idle -> walk -> run by speed. The thresholds are the clips'
            // natural stride speeds (walk 16 f ~2.55 m/s, run 12 f ~6.5 m/s); LocoSpeed scales
            // playback outside that range so the feet don't skate.
            var loco = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips["idle"], 0f);
            tree.AddChild(clips["walk"], WalkSpeed);
            tree.AddChild(clips["run"], RunSpeed);
            loco.speedParameter = "LocoSpeed";
            loco.speedParameterActive = true;
            sm.defaultState = loco;

            AnimatorState S(string name, string clip)
            {
                var s = sm.AddState(name);
                s.motion = clips[clip];
                return s;
            }
            var jump = S("Jump", "jump");
            var punch = S("Punch", "punch");
            var kick = S("Kick", "kick");
            var knock = S("Knockdown", "knockdown");
            var hit = S("Hit", clips.ContainsKey("hit") ? "hit" : "knockdown");
            var ride = S("Ride", "ride");
            var mount = S("Mount", "mount");
            var dismount = S("Dismount", "dismount");
            var sit = S("Sit", "sit");
            var wave = S("Wave", "wave");
            var panic = S("Panic", clips.ContainsKey("panic") ? "panic" : "run");

            AnimatorStateTransition T(AnimatorState from, AnimatorState to, float dur = 0.12f, bool exit = false, float exitTime = 0.9f)
            {
                var t = from.AddTransition(to);
                t.duration = dur;
                t.hasExitTime = exit;
                t.exitTime = exitTime;
                return t;
            }
            AnimatorStateTransition Any(AnimatorState to, float dur = 0.08f, bool self = false)
            {
                var t = sm.AddAnyStateTransition(to);
                t.duration = dur;
                t.canTransitionToSelf = self;
                return t;
            }
            // air
            T(loco, jump).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            T(jump, loco, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            // attacks + knockdown from anywhere (not while riding)
            Any(punch).AddCondition(AnimatorConditionMode.If, 0, "Punch");
            Any(kick).AddCondition(AnimatorConditionMode.If, 0, "Kick");
            // hits and knockdowns restart themselves: every smack re-plays the reaction
            Any(knock, 0.05f, true).AddCondition(AnimatorConditionMode.If, 0, "Knock");
            Any(hit, 0.03f, true).AddCondition(AnimatorConditionMode.If, 0, "Hit");
            T(hit, loco, 0.1f, true, 0.9f);
            T(punch, loco, 0.1f, true, 0.85f);
            T(kick, loco, 0.1f, true, 0.85f);
            T(knock, loco, 0.2f, true, 0.95f);
            // vehicles
            Any(mount, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Mount");
            T(mount, ride, 0.1f, true, 0.95f);
            T(loco, ride, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "Riding");   // instant seat (NPCs, tests)
            T(ride, dismount, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Dismount");
            T(ride, loco, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "Riding");
            T(dismount, loco, 0.15f, true, 0.9f);
            Any(sit, 0.4f).AddCondition(AnimatorConditionMode.If, 0, "Sitting");          // ease down onto the seat
            T(sit, loco, 0.4f).AddCondition(AnimatorConditionMode.IfNot, 0, "Sitting");   // and up again
            // wave / panic (NPCs)
            T(loco, wave, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "Wave");
            T(wave, loco, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Wave");
            T(loco, panic, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "Panic");
            T(panic, loco, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Panic");

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            Debug.Log("[KL] Humanoid controller built: " + Path);
            return ac;
        }
    }
}
