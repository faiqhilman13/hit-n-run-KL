using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// Import rules for the Blender-generated FBX files in Assets/Models:
    ///  * legacy procedural models: bake axis conversion, per-colour LatInk materials;
    ///  * KL handoff assets (Assets/Models/KL): Humanoid avatar + named looping clips for
    ///    characters, palette-based materials (CharacterSoft for humans, LatInk for the city);
    ///  * smoothed normals for the inverted-hull outline: UV3 for rigid meshes, the
    ///    tangent channel (w = 2) for skinned meshes, because Unity skins tangents.
    /// </summary>
    public class ModelImportPipeline : AssetPostprocessor
    {
        const string ModelFolder = "Assets/Models/";
        const string KLFolder = "Assets/Models/KL/";
        const string PalettePath = "Assets/Models/KL/kl_palette.png";
        const string CharacterPalettePath = "Assets/Models/KL/kl_character_palette.png";
        const string ShaderName = "KampungRun/LatInk";
        const string CharacterShaderName = "KampungRun/CharacterSoft";
        static readonly string[] LoopClips = { "idle", "walk", "run", "ride", "wave", "sit", "panic" };
        static readonly string[] HumanBones = { "Hips", "Spine", "Chest", "Neck", "Head",
            "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
            "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes" };

        // bump to force every model to reimport after changing these rules or shader defaults
        public override uint GetVersion() => 15;
        const string SurfaceFolder = "Assets/KampungRun/Textures/Surfaces/";

        string P => assetPath.Replace('\\', '/');
        bool IsOurs => P.StartsWith(ModelFolder);
        bool IsKL => P.StartsWith(KLFolder);
        bool IsKLCharacter => IsKL && System.IO.Path.GetFileName(P).StartsWith("chr_");
        // A character that brings its own baked textures (the MPFB cast: Tools/mpfb_characters) keeps them in
        // a chr_<name>_tex folder beside the FBX, one PNG per material name, instead of the shared palette.
        string TexDir => KLFolder + System.IO.Path.GetFileNameWithoutExtension(P) + "_tex";
        bool HasOwnTextures => IsKLCharacter && AssetDatabase.IsValidFolder(TexDir);
        static bool IsCardMaterial(string name) => name.EndsWith("_brows") || name.EndsWith("_lashes");

        // MPFB characters also map their fingers (SeatFit sizes the palm from the middle finger)
        static readonly (string human, string bone)[] FingerBones =
        {
            ("Thumb", "thumb"), ("Index", "index"), ("Middle", "middle"), ("Ring", "ring"), ("Little", "pinky"),
        };

        static IEnumerable<HumanBone> MpfbFingers()
        {
            string[] seg = { "Proximal", "Intermediate", "Distal" };
            foreach (var (side, s) in new[] { ("Left", "l"), ("Right", "r") })
                foreach (var (human, bone) in FingerBones)
                    for (int i = 0; i < 3; i++)
                        yield return new HumanBone { humanName = $"{side} {human} {seg[i]}", boneName = $"{bone}_0{i + 1}_{s}",
                            limit = new HumanLimit { useDefaultValues = true } };
        }

        void OnPreprocessTexture()
        {
            if (P.StartsWith(SurfaceFolder))
            {
                // painted-surface detail maps: linear greyscale, tiling, mipmapped, readable (packed into an array)
                var st = (TextureImporter)assetImporter;
                st.sRGBTexture = false;
                st.isReadable = true;
                st.mipmapEnabled = true;
                st.wrapMode = TextureWrapMode.Repeat;
                st.filterMode = FilterMode.Trilinear;
                st.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (P.StartsWith(KLFolder) && P.Contains("_tex/"))
            {
                // a character's own baked atlas: mipmapped, readable for runtime costume recolours
                var ct = (TextureImporter)assetImporter;
                bool isMask = P.EndsWith("_mask.png");
                ct.sRGBTexture = !isMask;
                ct.isReadable = true;
                ct.mipmapEnabled = true;
                ct.wrapMode = TextureWrapMode.Clamp;
                ct.filterMode = FilterMode.Trilinear;
                ct.anisoLevel = 4;
                ct.maxTextureSize = 4096;
                ct.textureCompression = isMask ? TextureImporterCompression.Compressed : TextureImporterCompression.CompressedHQ;
                if (isMask) { ct.mipmapEnabled = false; ct.filterMode = FilterMode.Point; }
                ct.alphaIsTransparency = P.EndsWith("_brows.png") || P.EndsWith("_lashes.png");
                return;
            }
            if (P != PalettePath && P != CharacterPalettePath) return;
            var ti = (TextureImporter)assetImporter;
            ti.filterMode = FilterMode.Point;      // flat colour cells, no bleeding
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.sRGBTexture = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            // Runtime costume swaps retain the character atlas's tuned, unswapped colours.
            if (P == CharacterPalettePath) ti.isReadable = true;
        }

        void OnPreprocessModel()
        {
            if (!IsOurs) return;
            var mi = (ModelImporter)assetImporter;
            mi.useFileScale = true;
            mi.globalScale = 1f;
            mi.importCameras = false;
            mi.importLights = false;
            mi.addCollider = false;
            mi.isReadable = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.importNormals = ModelImporterNormals.Import;
            if (IsKL)
            {
                mi.bakeAxisConversion = !IsKLCharacter;   // clean identity rotations on rigid parts
                mi.importBlendShapes = true;
                mi.importTangents = ModelImporterTangents.CalculateMikk;
                if (IsKLCharacter)
                {
                    mi.animationType = ModelImporterAnimationType.Human;
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    mi.importAnimation = true;
                    // Keep the imported skeleton and name every humanoid bone;
                    // Unity automapping can drop the chest, shoulders and toes.
                    var skeleton = mi.humanDescription.skeleton;
                    var human = HumanBones.Select(n => new HumanBone { boneName = n, humanName = n,
                        limit = new HumanLimit { useDefaultValues = true } });
                    if (HasOwnTextures)
                    {
                        human = human.Concat(MpfbFingers());
                        // a new rig (MPFB's, renamed to the humanoid names, rest = T-pose): rebuild the skeleton
                        // from the file rather than keeping the last import's
                        if (skeleton == null || !skeleton.Any(b => b.name == "spine_03")) skeleton = new SkeletonBone[0];
                    }
                    mi.humanDescription = new HumanDescription
                    {
                        human = human.ToArray(),
                        skeleton = skeleton,
                        armStretch = 0.05f, legStretch = 0.05f,
                        upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
                        upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                        feetSpacing = 0f, hasTranslationDoF = false,
                    };
                }
                else
                {
                    mi.animationType = ModelImporterAnimationType.None;
                    mi.importAnimation = false;
                }
                return;
            }
            mi.bakeAxisConversion = true;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
        }

        void OnPreprocessAnimation()
        {
            if (!IsKLCharacter) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            var previous = mi.clipAnimations.ToDictionary(c => c.name);
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                // Blender takes come in as "<rig>|<clip>"
                string n = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                // Keep Unity's internal clip ID when Blender's wrapper/take name
                // changes, or the shared controller's existing references break.
                if (previous.TryGetValue(n, out var existing))
                {
                    existing.takeName = c.takeName;
                    existing.firstFrame = c.firstFrame;
                    existing.lastFrame = c.lastFrame;
                    c = clips[i] = existing;
                }
                c.name = n;
                c.loopTime = LoopClips.Contains(n);
                c.lockRootRotation = true;
                c.lockRootHeightY = false;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!IsOurs) return;
            var shader = Shader.Find(IsKLCharacter ? CharacterShaderName : ShaderName);
            if (shader == null) return;
            material.shader = shader;
            material.enableInstancing = true;
            if (HasOwnTextures)
            {
                var texPath = $"{TexDir}/{material.name}.png";
                context.DependsOnSourceAsset(texPath);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Gloss", 1f);
                if (tex != null) material.SetTexture("_BaseMap", tex);
                var maskPath = $"{TexDir}/{material.name}_mask.png";
                context.DependsOnSourceAsset(maskPath);
                var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
                if (mask != null) material.SetTexture("_MaskMap", mask);
                if (IsCardMaterial(material.name))
                {
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetFloat("_AlphaClip", 1f);
                    material.SetFloat("_Cull", 0f);
                    material.renderQueue = 2450;    // AlphaTest
                }
                return;
            }
            if (IsKL)
            {
                material.SetColor("_BaseColor", Color.white);
                // Hit & Run shading: the atlas alpha says which colours shine (paint, chrome, glass)
                material.SetFloat("_Gloss", 1f);
                string palettePath = IsKLCharacter ? CharacterPalettePath : PalettePath;
                context.DependsOnSourceAsset(palettePath);
                var pal = AssetDatabase.LoadAssetAtPath<Texture2D>(palettePath);
                // Keep a usable preview during a staged import before the character atlas arrives.
                if (pal == null && IsKLCharacter) pal = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
                if (pal != null) material.SetTexture("_BaseMap", pal);
                return;
            }
            Color c = Color.white;
            if (description.TryGetProperty("DiffuseColor", out Vector4 d)) c = d;
            else if (description.TryGetProperty("BaseColor", out Vector4 b)) c = b;
            // The Blender palette was authored as display (sRGB) values, so use as-is.
            c.a = 1f;
            material.SetColor("_BaseColor", c);
            // legacy buildings / props get painted textures from their colour; vehicles and people don't
            string file = System.IO.Path.GetFileName(P);
            int surf = file.StartsWith("Bld_") || file.StartsWith("Prop_") ? SurfaceKinds.Classify(c) : 0;
            // kampung houses are painted timber: their walls get boards, not plaster
            if ((file.StartsWith("Bld_KampungHouse") || file.StartsWith("Bld_Surau")) && surf == SurfaceKinds.Plaster)
                surf = SurfaceKinds.Timber;
            material.SetFloat("_Surface", surf);
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!IsOurs) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) WriteSmoothNormals(mf.sharedMesh, false);
            foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (sk.sharedMesh != null) WriteSmoothNormals(sk.sharedMesh, true);
                // brow and lash cards: no rectangular shadows on the face (the shadow pass doesn't alpha-test)
                if (sk.name.Contains("_cards")) sk.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // KL distance versions: Unity builds a LODGroup from the *_LOD0 / *_LOD1 siblings.
            // Full detail until the object is under 18% of screen height; LOD1 is never culled
            // (streets must not pop holes).
            foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = g.GetLODs();
                if (lods.Length < 2) continue;
                lods[0].screenRelativeTransitionHeight = 0.18f;
                for (int i = 1; i < lods.Length; i++) lods[i].screenRelativeTransitionHeight = 0f;
                g.SetLODs(lods);
                g.fadeMode = LODFadeMode.None;
            }
            if (IsKL && System.IO.Path.GetFileName(P).StartsWith("veh_")) SetupCarLods(root);
        }

        // Hit & Run cars (Tools/blender/kl/kl_cars.py) bring one light stand-in for the whole car: Body_LOD1 (the
        // shut, low-poly body with painted windows) and Tyre_XX_LOD1 under each wheel. Everything else is the
        // full-detail car (LOD0), except the lamps and roof furniture, which stay drawn at any distance so lights
        // still glow down the street, and optional Var_ parts (VehicleVariety) unless they are named *_LOD0.
        // Busy streets: full detail only within about 40 m.
        static readonly HashSet<string> CarAlwaysDrawn = new HashSet<string> { "HeadLights", "BrakeLights", "ReverseLights", "LightBar", "RoofSign" };

        static void SetupCarLods(GameObject root)
        {
            var all = root.GetComponentsInChildren<Renderer>(true);
            if (!all.Any(r => r.name == "Body_LOD1")) return;
            foreach (var g in root.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(g);
            var near = new List<Renderer>();
            var far = new List<Renderer>();
            foreach (var r in all)
            {
                if (r.name.StartsWith("COL_")) continue;
                if (r.name.EndsWith("_LOD1")) far.Add(r);
                else if (CarAlwaysDrawn.Contains(r.name) || (r.name.StartsWith("Var_") && !r.name.EndsWith("_LOD0"))) continue;
                else near.Add(r);
            }
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.1f, near.ToArray()), new LOD(0f, far.ToArray()) });
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
            // optional parts arrive switched off: VehicleVariety turns some on per car
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Var_")) t.gameObject.SetActive(false);
        }

        /// <summary>Average normals of vertices sharing a position (for the outline hull).</summary>
        public static void WriteSmoothNormals(Mesh mesh, bool intoTangents)
        {
            var verts = mesh.vertices;
            var normals = mesh.normals;
            if (normals == null || normals.Length != verts.Length) return;
            var sums = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 v) => new Vector3Int(Mathf.RoundToInt(v.x * 1000f), Mathf.RoundToInt(v.y * 1000f),
                Mathf.RoundToInt(v.z * 1000f));
            for (int i = 0; i < verts.Length; i++)
            {
                var k = Key(verts[i]);
                sums.TryGetValue(k, out var s);
                sums[k] = s + normals[i];
            }
            if (intoTangents)
            {
                var tan = new Vector4[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    var n = sums[Key(verts[i])];
                    n = n.sqrMagnitude > 1e-6f ? n.normalized : normals[i];
                    tan[i] = new Vector4(n.x, n.y, n.z, 2f);   // w = 2 flags "smooth normal" for LatInk
                }
                mesh.tangents = tan;
                return;
            }
            var smooth = new List<Vector3>(verts.Length);
            for (int i = 0; i < verts.Length; i++)
            {
                var n = sums[Key(verts[i])];
                smooth.Add(n.sqrMagnitude > 1e-6f ? n.normalized : normals[i]);
            }
            mesh.SetUVs(3, smooth);
        }
    }
}
