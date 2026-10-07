using System.Collections.Generic;
using System.IO;
using KampungRun.Arch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// Review renders for the architecture library: a street of generated buildings in an empty scene with the
    /// game's level-1 light and the baked city shade, shot from the pavement, across the road, close up and from
    /// above, near (detail) and far (distance version) versions side by side. Writes PNGs to Tools/arch_review.
    ///   Unity -batchmode -projectPath . -executeMethod KampungRun.EditorTools.ArchStudio.ReviewBatch -quit
    ///   [-archSeed 7] [-archOut path]
    /// </summary>
    public static class ArchStudio
    {
        const int W = 1600, H = 900;

        static string Arg(string key, string def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == key) return a[i + 1];
            return def;
        }

        public static void ReviewBatch()
        {
            string outDir = Path.GetFullPath(Arg("-archOut", Path.Combine(Application.dataPath, "..", "Tools", "arch_review")));
            int seed = int.Parse(Arg("-archSeed", "7"));
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = Stage();
            var root = new GameObject("Street").transform;
            var stats = new System.Text.StringBuilder();

            // two facing rows of shophouses along a 14 m street (pavements included), lots 3.8 to 6 m wide
            var rng = new System.Random(seed);
            const float street = 14f;
            var specs = new List<BuildingSpec>();
            for (int side = 0; side < 2; side++)
            {
                float x = -40f;
                int lot = 0;
                while (x < 40f)
                {
                    float w = 3.8f + (float)rng.NextDouble() * 2.2f, d = 14f + (float)rng.NextDouble() * 8f;
                    int lv = rng.NextDouble() < 0.15 ? 4 : rng.NextDouble() < 0.6 ? 3 : 2;
                    if (rng.NextDouble() < 0.08) lv = 5;
                    float h = lv * 3.4f;
                    bool first = lot == 0, last = x + w >= 40f;
                    Vector2[] ring;
                    EdgeFlags[] edges;
                    if (side == 0)
                    {
                        // south side, front at z = 0 facing -z (the street)
                        ring = new[] { new Vector2(x, 0f), new Vector2(x + w, 0f), new Vector2(x + w, d), new Vector2(x, d) };
                        edges = new[] { EdgeFlags.Street, last ? EdgeFlags.None : EdgeFlags.Party, EdgeFlags.None, first ? EdgeFlags.None : EdgeFlags.Party };
                    }
                    else
                    {
                        // north side, front at z = -street facing +z
                        float zf = -street;
                        ring = new[] { new Vector2(x + w, zf), new Vector2(x, zf), new Vector2(x, zf - d), new Vector2(x + w, zf - d) };
                        edges = new[] { EdgeFlags.Street, first ? EdgeFlags.None : EdgeFlags.Party, EdgeFlags.None, last ? EdgeFlags.None : EdgeFlags.Party };
                    }
                    specs.Add(new BuildingSpec { ring = ring, edges = edges, height = h, levels = lv, kind = BuildingKind.Shop, seed = rng.Next() });
                    x += w;
                    lot++;
                }
            }

            // past the rows: a wide-fronted shophouse block, flats, an office tower, a civic hall, a temple
            Vector2[] Box(float x0, float z0, float x1, float z1) => new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };
            EdgeFlags[] FrontSouth() => new[] { EdgeFlags.Street, EdgeFlags.None, EdgeFlags.None, EdgeFlags.None };
            specs.Add(new BuildingSpec { ring = Box(44f, 0f, 74f, 16f), edges = FrontSouth(), height = 3 * 3.4f, levels = 3, kind = BuildingKind.Shop, seed = 11 });
            specs.Add(new BuildingSpec { ring = Box(80f, 2f, 112f, 18f), edges = FrontSouth(), height = 13.6f + 4 * 3.4f * 0.62f, levels = 8, kind = BuildingKind.Flats, seed = 12, colour = 2 });
            specs.Add(new BuildingSpec { ring = Box(44f, -street - 26f, 70f, -street - 2f), edges = new[] { EdgeFlags.None, EdgeFlags.None, EdgeFlags.Street, EdgeFlags.None },
                                         height = 13.6f + 20 * 3.4f * 0.62f, levels = 24, kind = BuildingKind.Office, seed = 13, colour = 9 });
            specs.Add(new BuildingSpec { ring = Box(76f, -street - 22f, 112f, -street - 2f), edges = new[] { EdgeFlags.None, EdgeFlags.None, EdgeFlags.Street, EdgeFlags.None },
                                         height = 3 * 3.4f, levels = 3, kind = BuildingKind.Civic, seed = 14, colour = 6 });
            specs.Add(new BuildingSpec { ring = Box(-74f, 2f, -52f, 16f), edges = FrontSouth(), height = 2 * 3.4f, levels = 2, kind = BuildingKind.Worship, seed = 15 });
            specs.Add(new BuildingSpec { ring = Box(-76f, -street - 30f, -50f, -street - 2f), edges = new[] { EdgeFlags.None, EdgeFlags.None, EdgeFlags.Street, EdgeFlags.None },
                                         height = 13.6f + 10 * 3.4f * 0.62f, levels = 14, kind = BuildingKind.Office, seed = 16, colour = 12 });

            var near = new ArchMesh();
            var far = new ArchMesh();
            int maxNear = 0, sumNear = 0, sumFar = 0;
            foreach (var s in specs)
            {
                int before = near.T.Count;
                ArchGen.Build(s, near, true);
                int tris = (near.T.Count - before) / 3;
                maxNear = Mathf.Max(maxNear, tris);
                sumNear += tris;
                int fb = far.T.Count;
                ArchGen.Build(s, far, false);
                sumFar += (far.T.Count - fb) / 3;
                if (s.kind != BuildingKind.Shop) stats.AppendLine($"  {s.kind} {s.levels} storeys: {tris} tris near, {(far.T.Count - fb) / 3} far");
            }
            stats.AppendLine($"{specs.Count} lots: detail {sumNear} tris ({sumNear / specs.Count} a lot, max {maxNear}), distance {sumFar} tris ({sumFar / specs.Count} a lot); verts detail {near.Count}, far {far.Count}");

            // behind the south row: a kampung garden with two houses and one of every plant
            var garden = new List<IArchItem>
            {
                new HouseSpec { pos = new Vector3(-22f, 0f, 52f), yaw = 180f, width = 8f, seed = 3 },
                new HouseSpec { pos = new Vector3(-8f, 0f, 54f), yaw = 160f, width = 6f, seed = 4 },
                new FloraSpec { pos = new Vector3(8f, 0f, 50f), kind = FloraKind.RainTree, scale = 1.1f, seed = 1 },
                new FloraSpec { pos = new Vector3(22f, 0f, 46f), kind = FloraKind.Angsana, scale = 1.0f, seed = 2 },
                new FloraSpec { pos = new Vector3(30f, 0f, 54f), kind = FloraKind.Palm, scale = 1.0f, seed = 3 },
                new FloraSpec { pos = new Vector3(34f, 0f, 47f), kind = FloraKind.Palm, scale = 0.9f, seed = 4 },
                new FloraSpec { pos = new Vector3(16f, 0f, 59f), kind = FloraKind.Frangipani, scale = 1.1f, seed = 5 },
                new FloraSpec { pos = new Vector3(-14f, 0f, 44f), kind = FloraKind.Banana, scale = 1.0f, seed = 6 },
                new FloraSpec { pos = new Vector3(-30f, 0f, 46f), kind = FloraKind.Banana, scale = 1.1f, seed = 7 },
                new FloraSpec { pos = new Vector3(2f, 0f, 41f), kind = FloraKind.Shrub, scale = 1.0f, seed = 8 },
                new FloraSpec { pos = new Vector3(-2f, 0f, 43f), kind = FloraKind.Bougainvillea, scale = 1.0f, seed = 9 },
            };
            for (int i = 0; i < 24; i++)
                garden.Add(new FloraSpec { pos = new Vector3(-36f + (i % 8) * 9f + (float)rng.NextDouble() * 4f, 0f, 38f + (i / 8) * 9f), kind = FloraKind.Tufts, seed = 100 + i });
            // bridges: a jejantas over the street, a sign gantry, and a stretch of flyover deck west of the town
            garden.Add(new JejantasSpec { a = new Vector3(20f, 0.18f, -1.6f), b = new Vector3(20f, 0.18f, -street + 1.6f), seed = 5 });
            garden.Add(new GantrySpec { centre = new Vector3(-28f, 0f, -street * 0.5f), across = Vector3.forward, halfWidth = street * 0.5f - 0.4f, signs = 2 });
            const float flyY = 6.8f;
            foreach (float s in new[] { -1f, 1f })
                garden.Add(new ParapetRun { pts = new[] { new Vector3(-150f, flyY, -7f + s * 9.56f), new Vector3(-118f, flyY, -7f + s * 9.56f), new Vector3(-86f, flyY, -7f + s * 9.56f) },
                                            raised = new[] { true, true }, outward = new Vector3(0f, 0f, s), deckDepth = 1.4f });
            foreach (float x in new[] { -140f, -116f, -92f })
                garden.Add(new PierSpec { foot = new Vector3(x, 0f, -7f), along = Vector3.right, top = flyY - 1.4f, capWidth = 9.6f * 1.6f });
            int gn = 0, gf = 0;
            foreach (var g in garden)
            {
                int a = near.T.Count, b = far.T.Count;
                g.Build(near, true);
                g.Build(far, false);
                gn += (near.T.Count - a) / 3;
                gf += (far.T.Count - b) / 3;
                if (!(g is FloraSpec fs && fs.kind == FloraKind.Tufts))
                    stats.AppendLine($"  {(g is HouseSpec ? "kampung house" : g is FloraSpec fl ? fl.kind.ToString() : g.GetType().Name)}: {(near.T.Count - a) / 3} tris near, {(far.T.Count - b) / 3} far");
            }
            stats.AppendLine($"garden: {gn} tris near, {gf} far");

            var nearGo = Show(near.ToMesh("ArchNear"), root, "Near");
            var farGo = Show(far.ToMesh("ArchFar"), root, "Far");
            Ground(root, street);
            ArchKit.Init();
            CityShade.Bake(root, new Rect(-160f, -80f, 300f, 140f), 0.5f);

            void Shot(string name, Vector3 eye, Vector3 look, float fov, bool detail)
            {
                nearGo.SetActive(detail);
                farGo.SetActive(!detail);
                cam.fieldOfView = fov;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                Capture(cam, Path.Combine(outDir, name + ".png"));
            }
            Shot("01_pavement", new Vector3(-30f, 1.7f, -3.2f), new Vector3(10f, 4f, 2f), 60f, true);
            Shot("02_across", new Vector3(0f, 6f, -street + 2f), new Vector3(0f, 6f, 5f), 62f, true);
            Shot("03_close", new Vector3(-6f, 3.5f, -6f), new Vector3(-4f, 6.5f, 0f), 55f, true);
            Shot("04_arcade", new Vector3(-20f, 1.6f, 1.25f), new Vector3(10f, 1.9f, 1.25f), 64f, true);
            Shot("05_aerial", new Vector3(-55f, 38f, 48f), new Vector3(0f, 0f, -8f), 50f, true);
            Shot("06_aerial_far", new Vector3(-55f, 38f, 48f), new Vector3(0f, 0f, -8f), 50f, false);
            Shot("07_north_row", new Vector3(10f, 2.2f, -1.5f), new Vector3(-10f, 6f, -street), 60f, true);
            Shot("08_roofs", new Vector3(20f, 22f, 12f), new Vector3(0f, 8f, -6f), 55f, true);
            Shot("09_front_aerial", new Vector3(-30f, 30f, -60f), new Vector3(0f, 4f, 0f), 50f, true);
            Shot("11_blocks_south", new Vector3(78f, 4f, -street + 1f), new Vector3(78f, 10f, 10f), 70f, true);
            Shot("12_blocks_north", new Vector3(60f, 3f, -2f), new Vector3(78f, 18f, -street - 10f), 70f, true);
            Shot("13_blocks_aerial", new Vector3(30f, 45f, -70f), new Vector3(78f, 5f, -6f), 55f, true);
            Shot("14_west_end", new Vector3(-45f, 3f, -7f), new Vector3(-63f, 8f, -12f), 70f, true);
            Shot("15_garden", new Vector3(0f, 2.6f, 27f), new Vector3(0f, 3.5f, 52f), 66f, true);
            Shot("16_house", new Vector3(-17f, 2.2f, 40f), new Vector3(-22f, 3.4f, 52f), 60f, true);
            Shot("17_trees", new Vector3(14f, 2.0f, 33f), new Vector3(22f, 5f, 52f), 66f, true);
            Shot("18_garden_far", new Vector3(0f, 2.6f, 27f), new Vector3(0f, 3.5f, 52f), 66f, false);
            Shot("19_garden_air", new Vector3(-10f, 26f, 18f), new Vector3(0f, 0f, 50f), 55f, true);
            Shot("20_jejantas", new Vector3(-4f, 2.0f, -7f), new Vector3(20f, 5f, -7f), 62f, true);
            Shot("21_flyover", new Vector3(-104f, 1.8f, -26f), new Vector3(-118f, 5f, -7f), 64f, true);
            Shot("22_flyover_top", new Vector3(-80f, 8.6f, -7f), new Vector3(-130f, 7f, -7f), 64f, true);
            Shot("10_top", new Vector3(0f, 120f, -7.01f), new Vector3(0f, 0f, -7f), 60f, true);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.name != "Near" && r.name != "Far") stats.AppendLine($"{r.name}: bounds {r.bounds.min} .. {r.bounds.max}, mat {r.sharedMaterial.name} {r.sharedMaterial.GetColor("_BaseColor")}");
            File.WriteAllText(Path.Combine(outDir, "stats.txt"), stats.ToString());
            Debug.Log("[ArchStudio] " + stats + " -> " + outDir);
        }

        static GameObject Show(Mesh mesh, Transform root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ArchKit.Material;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        /// <summary>Road, kerbs and pavements under the street, in the game's palette colours.</summary>
        static void Ground(Transform root, float street)
        {
            void Slab(string name, Vector3 c, Vector3 size, Color col)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(root, false);
                go.transform.localPosition = c;
                go.transform.localScale = size;
                go.GetComponent<Renderer>().sharedMaterial = LatMaterials.Get(col, 0f);
            }
            Slab("Road", new Vector3(0f, -0.05f, -street * 0.5f), new Vector3(260f, 0.1f, street - 6.4f), LatMaterials.Pal.Road);
            Slab("PavementS", new Vector3(0f, 0.09f, -1.6f), new Vector3(260f, 0.18f, 3.2f), LatMaterials.Pal.Pavement);
            Slab("PavementN", new Vector3(0f, 0.09f, -street + 1.6f), new Vector3(260f, 0.18f, 3.2f), LatMaterials.Pal.Pavement);
            Slab("Back", new Vector3(0f, -0.06f, 0f), new Vector3(320f, 0.1f, 160f), LatMaterials.Pal.Grass);
            // a stretch of flyover deck (its parapets and piers come from the architecture library)
            Slab("Deck", new Vector3(-118f, 6.8f - 0.7f, -7f), new Vector3(64f, 1.4f, 19.2f), LatMaterials.Pal.Road);
        }

        /// <summary>An empty stage lit like level 1 (sun, hemisphere ambient, LatInk's Hit &amp; Run mode).</summary>
        static Camera Stage()
        {
            var l = GameData.Levels[1];
            var sun = new GameObject("Matahari").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(l.sunEuler);
            sun.color = l.sun;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            float k = l.paper.grayscale;
            Shader.SetGlobalFloat("_ShadeMode", 1f);
            Shader.SetGlobalFloat("_ShadeOutline", 0f);
            Shader.SetGlobalColor("_ShadeSky", Color.Lerp(l.fog, l.zenith, 0.3f) * 0.58f * k + new Color(0.06f, 0.06f, 0.06f) * k);
            Shader.SetGlobalColor("_ShadeGround", Color.Lerp(l.shadowTint, new Color(0.55f, 0.6f, 0.4f), 0.4f) * 0.4f * k);
            Shader.SetGlobalVector("_LatStyle", new Vector4(0.55f, 0f, 0.25f, 0.12f));
            Shader.SetGlobalVector("_LatStyle2", new Vector4(0f, 1.05f, 0f, 0f));
            Shader.SetGlobalFloat("_LatStyleSet", 1f);
            var paper = l.paper; paper.a = 1f;
            var shadow = l.shadowTint; shadow.a = 1f;
            Shader.SetGlobalColor("_LatPaper", paper);
            Shader.SetGlobalColor("_LatShadow", shadow);
            if (GameAssets.I.surfaces != null) Shader.SetGlobalTexture("_SurfaceArray", GameAssets.I.surfaces);
            Shader.SetGlobalFloat("_SurfaceStrength", GameAssets.I.surfaces != null ? 1f : 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = l.paper;
            RenderSettings.fog = false;
            var cg = new GameObject("Camera");
            var cam = cg.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.Lerp(l.fog, l.zenith, 0.5f);
            cam.farClipPlane = 600f;
            var data = cg.AddComponent<UniversalAdditionalCameraData>();
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.shadowDistance = 160f;
            return cam;
        }

        static void Capture(Camera cam, string path)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var img = new Texture2D(W, H, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            img.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(path, img.EncodeToPNG());
            Object.DestroyImmediate(img);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
