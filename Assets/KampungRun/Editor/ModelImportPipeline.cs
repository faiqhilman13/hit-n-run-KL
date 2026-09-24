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
    ///    characters, one LatInk material driven by the shared kl_palette.png atlas;
    ///  * smoothed normals for the inverted-hull outline: UV3 for rigid meshes, the
    ///    tangent channel (w = 2) for skinned meshes, because Unity skins tangents.
    /// </summary>
    public class ModelImportPipeline : AssetPostprocessor
    {
        const string ModelFolder = "Assets/Models/";
        const string KLFolder = "Assets/Models/KL/";
        const string PalettePath = "Assets/Models/KL/kl_palette.png";
        const string ShaderName = "KampungRun/LatInk";
        static readonly string[] LoopClips = { "idle", "walk", "run", "ride", "wave", "sit", "panic" };

        // bump to force every model to reimport after changing these rules or shader defaults
        public override uint GetVersion() => 12;
        const string SurfaceFolder = "Assets/KampungRun/Textures/Surfaces/";

        string P => assetPath.Replace('\\', '/');
        bool IsOurs => P.StartsWith(ModelFolder);
        bool IsKL => P.StartsWith(KLFolder);
        bool IsKLCharacter => IsKL && System.IO.Path.GetFileName(P).StartsWith("chr_");

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
            if (P != PalettePath) return;
            var ti = (TextureImporter)assetImporter;
            ti.filterMode = FilterMode.Point;      // flat colour cells, no bleeding
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.sRGBTexture = true;
            ti.wrapMode = TextureWrapMode.Clamp;
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
            foreach (var c in clips)
            {
                // Blender takes come in as "<rig>|<clip>"
                string n = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
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
            var shader = Shader.Find(ShaderName);
            if (shader == null) return;
            material.shader = shader;
            material.enableInstancing = true;
            if (IsKL)
            {
                material.SetColor("_BaseColor", Color.white);
                // Hit & Run shading: the atlas alpha says which colours shine (paint, chrome, glass)
                material.SetFloat("_Gloss", 1f);
                var pal = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
                if (pal != null) material.SetTexture("_BaseMap", pal);
                else context.DependsOnSourceAsset(PalettePath);
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
                if (sk.sharedMesh != null) WriteSmoothNormals(sk.sharedMesh, true);
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
