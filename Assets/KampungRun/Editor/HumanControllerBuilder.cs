using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// Builds the shared humanoid AnimatorController. The motion library (Assets/KampungRun/Animation/Clips,
    /// baked by Anim.AnimationStudio) supplies the locomotion, air, combat and street-life clips; the seated
    /// and riding clips still come from chr_aiman's takes. Because every clip is Humanoid (muscle space),
    /// every KL character uses this one controller.
    ///   Floats: Speed (normalised to the reference stride, see <see cref="Gaits"/>), LocoSpeed (playback),
    ///           Style (0 default, 1 heavy, 2 lady, 3 kid, 4 elder), VelY, GestureId, FidgetId
    ///   Bools:  Grounded, Riding, Sitting, Wave, Panic, Skid, Gesture
    ///   Ints:   Combo (1-3)
    ///   Triggers: Punch, Kick, Knock, Hit, Mount, Dismount, Flip, Pound, PoundLand, Land, Fidget
    /// The controller is rebuilt in place so its GUID (and the scene's and GameAssets' references) survive.
    /// </summary>
    public static class HumanControllerBuilder
    {
        public const string Path = "Assets/KampungRun/Animation/KL_Human.controller";
        const string Temp = "Assets/KampungRun/Animation/_KL_Human_build.controller";
        const string Source = "Assets/Models/KL/chr_aiman.fbx";
        const string ClipDir = "Assets/KampungRun/Animation/Clips";

        /// <summary>Gestures, in GestureId order.</summary>
        public static readonly string[] Gestures = { "talk", "talk2", "cheer", "angry", "watch_cross", "watch_hips", "film", "point_laugh", "fan" };
        /// <summary>Fidgets, in FidgetId order.</summary>
        public static readonly string[] Fidgets = { "fidget_look", "fidget_stretch", "fidget_wipe", "fidget_scratch", "fidget_watch" };

        [MenuItem("Kampung Run/Build Humanoid Animator")]
        public static RuntimeAnimatorController Build()
        {
            var fbx = AssetDatabase.LoadAllAssetRepresentationsAtPath(Source).OfType<AnimationClip>().ToDictionary(c => c.name, c => c);
            AnimationClip C(string name)
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir}/{name}.anim");
                if (c == null && fbx.TryGetValue(name, out var f)) c = f;
                if (c == null) Debug.LogError("[KL] missing clip " + name);
                return c;
            }
            if (!fbx.ContainsKey("sit") || AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir}/idle.anim") == null)
            {
                Debug.LogError("[KL] clips missing: bake the motion library (Kampung Run/Animation/Bake Motion Library) first");
                return null;
            }

            Directory.CreateDirectory("Assets/KampungRun/Animation");
            AssetDatabase.DeleteAsset(Temp);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(Temp);
            void Param(string n, AnimatorControllerParameterType t) => ac.AddParameter(n, t);
            foreach (var n in new[] { "Speed", "LocoSpeed", "Style", "VelY", "GestureId", "FidgetId" }) Param(n, AnimatorControllerParameterType.Float);
            foreach (var n in new[] { "Grounded", "Riding", "Sitting", "Wave", "Panic", "Skid", "Gesture" }) Param(n, AnimatorControllerParameterType.Bool);
            Param("Combo", AnimatorControllerParameterType.Int);
            foreach (var t in new[] { "Punch", "Kick", "Knock", "Hit", "Mount", "Dismount", "Flip", "Pound", "PoundLand", "Land", "Fidget" })
                Param(t, AnimatorControllerParameterType.Trigger);
            ac.parameters = ac.parameters.Select(p =>
            {
                if (p.name == "Grounded") p.defaultBool = true;
                if (p.name == "LocoSpeed") p.defaultFloat = 1f;
                if (p.name == "Combo") p.defaultInt = 1;
                return p;
            }).ToArray();

            var sm = ac.layers[0].stateMachine;
            BlendTree Tree(string name, string param)
            {
                var bt = new BlendTree { name = name, blendParameter = param, blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(bt, ac);
                return bt;
            }

            // ---------------------------------------------------------------- locomotion: by style, then by speed
            // Each style's speed tree: idle, a walk that also covers the slow end (LocoSpeed slows it to match),
            // jog, run, sprint at their natural speeds - the feet stay planted all the way up.
            var loco = sm.AddState("Locomotion");
            var styles = Tree("Styles", "Style");
            string[] suffix = { "", "_heavy", "_lady", "_kid", "_elder" };
            for (int s = 0; s < suffix.Length; s++)
            {
                var sp = Tree("Speed" + suffix[s], "Speed");
                sp.AddChild(C("idle" + suffix[s]), 0f);
                sp.AddChild(C("walk" + suffix[s]), Gaits.WalkFrom);
                sp.AddChild(C("walk" + suffix[s]), Gaits.Walk);
                sp.AddChild(C("jog" + suffix[s]), Gaits.Jog);
                sp.AddChild(C("run" + suffix[s]), Gaits.Run);
                sp.AddChild(C("sprint" + suffix[s]), Gaits.Sprint);
                styles.AddChild(sp, s);
            }
            loco.motion = styles;
            loco.speedParameter = "LocoSpeed";
            loco.speedParameterActive = true;
            sm.defaultState = loco;

            AnimatorState S(string name, Motion m)
            {
                var st = sm.AddState(name);
                st.motion = m;
                return st;
            }
            // ---------------------------------------------------------------- air: a blend on vertical speed
            var air = Tree("Air", "VelY");
            air.AddChild(C("air_flail"), -16f);
            air.AddChild(C("air_fall"), -6f);
            air.AddChild(C("air_apex"), 0f);
            air.AddChild(C("jump"), 6f);
            var airS = S("Air", air);
            var flip = S("Flip", C("flip"));
            var pound = S("Pound", C("pound"));
            var poundLand = S("PoundLand", C("pound_land"));
            var land = S("Land", C("land"));
            var skid = S("Skid", C("skid"));
            // ---------------------------------------------------------------- fighting
            var punch1 = S("Punch", C("punch"));
            var punch2 = S("Punch2", C("punch2"));
            var punch3 = S("Punch3", C("punch3"));
            var kick = S("Kick", C("kick"));
            var knock = S("Knockdown", C("knockdown"));
            var hit = S("Hit", C("hit"));
            // ---------------------------------------------------------------- vehicles (the original takes)
            var ride = S("Ride", C("ride"));
            var mount = S("Mount", C("mount"));
            var dismount = S("Dismount", C("dismount"));
            var sit = S("Sit", C("sit"));
            // ---------------------------------------------------------------- street life
            var wave = S("Wave", C("wave"));
            var panic = S("Panic", C("panic"));
            panic.speedParameter = "LocoSpeed";
            panic.speedParameterActive = true;
            var gest = Tree("Gestures", "GestureId");
            for (int i = 0; i < Gestures.Length; i++) gest.AddChild(C(Gestures[i]), i);
            var gesture = S("Gesture", gest);
            var fid = Tree("Fidgets", "FidgetId");
            for (int i = 0; i < Fidgets.Length; i++) fid.AddChild(C(Fidgets[i]), i);
            var fidget = S("Fidget", fid);

            AnimatorStateTransition T(AnimatorState from, AnimatorState to, float dur = 0.12f, bool exit = false, float exitTime = 0.9f)
            {
                var t = from.AddTransition(to);
                t.duration = dur;
                t.hasExitTime = exit;
                t.exitTime = exitTime;
                t.hasFixedDuration = true;
                return t;
            }
            AnimatorStateTransition Any(AnimatorState to, float dur = 0.08f, bool self = false)
            {
                var t = sm.AddAnyStateTransition(to);
                t.duration = dur;
                t.canTransitionToSelf = self;
                t.hasFixedDuration = true;
                return t;
            }
            const AnimatorConditionMode If = AnimatorConditionMode.If, IfNot = AnimatorConditionMode.IfNot;

            // air: off the ground into the velocity blend, and back (a hard landing plays its own absorb)
            T(loco, airS, 0.14f).AddCondition(IfNot, 0, "Grounded");
            T(skid, airS, 0.1f).AddCondition(IfNot, 0, "Grounded");
            T(airS, land, 0.04f).AddCondition(If, 0, "Land");
            T(airS, loco, 0.1f).AddCondition(If, 0, "Grounded");
            var landOut = T(land, loco, 0.18f, true, 0.78f);
            var landRun = T(land, loco, 0.14f);
            landRun.AddCondition(AnimatorConditionMode.Greater, 2.5f, "Speed");
            T(land, airS, 0.08f).AddCondition(IfNot, 0, "Grounded");
            Any(flip, 0.05f, true).AddCondition(If, 0, "Flip");
            T(flip, airS, 0.12f, true, 0.92f);
            T(flip, loco, 0.1f).AddCondition(If, 0, "Grounded");
            Any(pound, 0.05f).AddCondition(If, 0, "Pound");
            T(pound, poundLand, 0.03f).AddCondition(If, 0, "PoundLand");
            T(pound, loco, 0.1f).AddCondition(If, 0, "Grounded");
            T(poundLand, airS, 0.15f, true, 0.55f).AddCondition(IfNot, 0, "Grounded");
            T(poundLand, loco, 0.15f, true, 0.9f);
            // skidding to a stop
            T(loco, skid, 0.06f).AddCondition(If, 0, "Skid");
            T(skid, loco, 0.14f).AddCondition(IfNot, 0, "Skid");

            // attacks and knockdowns from anywhere (not while seated); the combo picks the punch
            var p1 = Any(punch1, 0.05f, true); p1.AddCondition(If, 0, "Punch"); p1.AddCondition(AnimatorConditionMode.Equals, 1, "Combo");
            var p2 = Any(punch2, 0.05f, true); p2.AddCondition(If, 0, "Punch"); p2.AddCondition(AnimatorConditionMode.Equals, 2, "Combo");
            var p3 = Any(punch3, 0.05f, true); p3.AddCondition(If, 0, "Punch"); p3.AddCondition(AnimatorConditionMode.Equals, 3, "Combo");
            Any(kick, 0.05f, true).AddCondition(If, 0, "Kick");
            Any(knock, 0.05f, true).AddCondition(If, 0, "Knock");
            Any(hit, 0.03f, true).AddCondition(If, 0, "Hit");
            foreach (var a in new[] { punch1, punch2 }) T(a, loco, 0.12f, true, 0.8f);
            T(punch3, loco, 0.14f, true, 0.85f);
            T(kick, loco, 0.14f, true, 0.85f);
            T(hit, loco, 0.12f, true, 0.88f);
            T(knock, loco, 0.25f, true, 0.95f);
            foreach (var a in new[] { punch1, punch2, punch3, kick }) T(a, airS, 0.12f, true, 0.6f).AddCondition(IfNot, 0, "Grounded");

            // vehicles
            Any(mount, 0.1f).AddCondition(If, 0, "Mount");
            T(mount, ride, 0.1f, true, 0.95f);
            T(loco, ride, 0.15f).AddCondition(If, 0, "Riding");
            T(ride, dismount, 0.1f).AddCondition(If, 0, "Dismount");
            T(ride, loco, 0.15f).AddCondition(IfNot, 0, "Riding");
            T(dismount, loco, 0.15f, true, 0.9f);
            Any(sit, 0.4f).AddCondition(If, 0, "Sitting");
            T(sit, loco, 0.4f).AddCondition(IfNot, 0, "Sitting");

            // street life
            T(loco, wave, 0.2f).AddCondition(If, 0, "Wave");
            T(wave, loco, 0.25f).AddCondition(IfNot, 0, "Wave");
            T(loco, panic, 0.15f).AddCondition(If, 0, "Panic");
            T(panic, loco, 0.2f).AddCondition(IfNot, 0, "Panic");
            var gIn = T(loco, gesture, 0.3f); gIn.AddCondition(If, 0, "Gesture"); gIn.AddCondition(AnimatorConditionMode.Less, 0.6f, "Speed");
            T(gesture, loco, 0.3f).AddCondition(IfNot, 0, "Gesture");
            T(gesture, loco, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.9f, "Speed");
            T(gesture, wave, 0.25f).AddCondition(If, 0, "Wave");
            var fIn = T(loco, fidget, 0.3f); fIn.AddCondition(If, 0, "Fidget"); fIn.AddCondition(AnimatorConditionMode.Less, 0.2f, "Speed");
            T(fidget, loco, 0.35f, true, 0.9f);
            T(fidget, loco, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.5f, "Speed");
            T(fidget, gesture, 0.3f).AddCondition(If, 0, "Gesture");
            T(fidget, wave, 0.25f).AddCondition(If, 0, "Wave");

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();

            // swap the new graph into the existing asset, keeping its GUID
            if (File.Exists(Path))
            {
                File.Copy(Temp, Path, true);
                AssetDatabase.DeleteAsset(Temp);
                AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            else AssetDatabase.MoveAsset(Temp, Path);
            AssetDatabase.SaveAssets();
            var result = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Path);
            Debug.Log($"[KL] Humanoid controller built: {Path} ({result.animationClips.Length} clip uses)");
            return result;
        }

        /// <summary>Batch entry: bake the motion library, then rebuild the controller.</summary>
        public static void BakeAndBuild()
        {
            Anim.AnimationStudio.Bake(null);
            AssetDatabase.Refresh();
            Build();
        }
    }
}
