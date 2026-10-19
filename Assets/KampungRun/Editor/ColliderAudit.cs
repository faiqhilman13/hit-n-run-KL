using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KampungRun.EditorTools
{
    /// <summary>
    /// Finds "invisible floors": static solid surfaces you can stand on with nothing drawn there, so
    /// characters look like they float. Run in Play mode once the city is built, with the environment
    /// variable KR_READABLE_MESHES=1 set before entering Play (generated meshes are GPU-only otherwise):
    /// `ColliderAudit.Start(path)` then poll `ColliderAudit.Status` until it reads "done".
    ///
    /// Every visible static mesh gets a temporary MeshCollider proxy on an unused layer (the
    /// architecture library's cells are rebuilt readable at full detail). A grid of downward
    /// RaycastAll columns then compares each solid top face (static, non-trigger) with the proxies:
    /// one with no drawn surface within `tol` metres of it, in its own column or the neighbouring
    /// ones, is flagged. Flags are clustered and written to a report with the owning collider.
    /// The work runs in slices on EditorApplication.update so remote calls never block for long.
    /// </summary>
    public static class ColliderAudit
    {
        const int ProxyLayer = 30;

        public static string Status = "idle";
        static IEnumerator<string> _job;

        public static string Start(string outPath, float spacing = 2f, float tol = 0.7f, float minY = 0.6f, bool detail = true)
        {
            Stop();
            _job = Job(outPath, spacing, tol, minY, detail);
            Status = "starting";
            EditorApplication.update += Tick;
            return Status;
        }

        public static void Stop()
        {
            EditorApplication.update -= Tick;
            if (_job != null) { _job.Dispose(); _job = null; }
        }

        static void Tick()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (clock.ElapsedMilliseconds < 400)
                {
                    if (!_job.MoveNext()) { Stop(); return; }
                    Status = _job.Current;
                }
            }
            catch (System.Exception e) { Status = "failed: " + e; Stop(); }
        }

        static IEnumerator<string> Job(string outPath, float spacing, float tol, float minY, bool detail)
        {
            var t0 = Time.realtimeSinceStartup;
            var proxies = new List<GameObject>();
            int unreadable = 0;
            float prevScale = Time.timeScale;
            Time.timeScale = 0f;
            try
            {
                var solidMask = ~((1 << Layers.Character) | (1 << Layers.Pickup) | (1 << Layers.Vehicle) | (1 << ProxyLayer) | (1 << 2) | (1 << 5) | (1 << Layers.Ward));
                int k = 0;
                foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!mr.enabled || !mr.gameObject.activeInHierarchy || mr.name.StartsWith("Far_") || mr.name.StartsWith("Near_")) continue;
                    if (mr.GetComponentInParent<Rigidbody>() != null || mr.GetComponentInParent<CharacterController>() != null) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < 3) continue;
                    if (!mf.sharedMesh.isReadable && !AssetDatabase.Contains(mf.sharedMesh)) { unreadable++; continue; }
                    var go = new GameObject("AuditProxy") { layer = ProxyLayer };
                    go.transform.SetPositionAndRotation(mr.transform.position, mr.transform.rotation);
                    go.transform.localScale = mr.transform.lossyScale;
                    go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    proxies.Add(go);
                    if (++k % 50 == 0) yield return $"proxies {k}";
                }
                if (Arch.ArchCity.I != null)
                {
                    foreach (var mesh in Arch.ArchCity.I.ReadableMeshes(detail))
                    {
                        if (mesh.vertexCount < 3) continue;
                        var go = new GameObject("AuditProxyArch") { layer = ProxyLayer };
                        go.transform.SetParent(Arch.ArchCity.I.transform.parent, false);
                        go.AddComponent<MeshCollider>().sharedMesh = mesh;
                        proxies.Add(go);
                        yield return $"arch proxies {proxies.Count}";
                    }
                }
                Physics.SyncTransforms();

                Bounds world = default;
                bool any = false;
                foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                {
                    if (c.isTrigger || c.gameObject.layer == ProxyLayer || c.attachedRigidbody != null || c.bounds.size.x > 3000f) continue;
                    if (c.name == "InvisibleWalls") continue;
                    if (!any) { world = c.bounds; any = true; } else world.Encapsulate(c.bounds);
                }
                float x0 = world.min.x, z0 = world.min.z, x1 = world.max.x, z1 = world.max.z;

                var flags = new List<(Vector3 p, string who, float gap)>();
                var hits = new RaycastHit[32];
                var vis = new RaycastHit[32];
                var tmp = new RaycastHit[32];
                var nb = new[] { new Vector3(0.9f, 0, 0), new Vector3(-0.9f, 0, 0), new Vector3(0, 0, 0.9f), new Vector3(0, 0, -0.9f) };
                int columns = 0;
                float top = world.max.y + 5f;
                for (float x = x0; x <= x1; x += spacing)
                {
                    for (float z = z0; z <= z1; z += spacing)
                    {
                        columns++;
                        var o = new Vector3(x, top, z);
                        int n = Physics.RaycastNonAlloc(o, Vector3.down, hits, top + 50f, solidMask, QueryTriggerInteraction.Ignore);
                        if (n == 0) continue;
                        int nv = -1;
                        for (int i = 0; i < n; i++)
                        {
                            var h = hits[i];
                            if (h.collider.attachedRigidbody != null || h.normal.y < 0.5f || h.point.y < minY || h.collider.name == "InvisibleWalls") continue;
                            if (nv < 0) nv = Physics.RaycastNonAlloc(o, Vector3.down, vis, top + 50f, 1 << ProxyLayer, QueryTriggerInteraction.Ignore);
                            if (Seen(vis, nv, h.point.y, tol)) continue;
                            bool near = false;
                            foreach (var d in nb)
                            {
                                int m = Physics.RaycastNonAlloc(o + d, Vector3.down, tmp, top + 50f, 1 << ProxyLayer, QueryTriggerInteraction.Ignore);
                                if (Seen(tmp, m, h.point.y, tol)) { near = true; break; }
                            }
                            if (near) continue;
                            float below = float.PositiveInfinity;          // drop to the nearest drawn surface under it
                            float above = float.PositiveInfinity;          // a drawn surface just over it hides it (you'd sink, not float)
                            for (int j = 0; j < nv; j++)
                            {
                                float dy = h.point.y - vis[j].point.y;
                                if (dy > 0f && dy < below) below = dy;
                                if (dy < 0f && -dy < above) above = -dy;
                            }
                            if (above < 3.5f) below = 0f;
                            flags.Add((h.point, Path(h.collider.transform) + " [" + h.collider.GetType().Name + "]", below));
                        }
                    }
                    yield return $"scan x={x:F0}/{x1:F0} flags {flags.Count}";
                }

                var groups = flags.GroupBy(f => (f.who, Mathf.FloorToInt(f.p.x / 24f), Mathf.FloorToInt(f.p.z / 24f)))
                    .Select(g => (g.Key.who, n: g.Count(), c: new Vector3(g.Average(f => f.p.x), g.Average(f => f.p.y), g.Average(f => f.p.z)),
                        ymin: g.Min(f => f.p.y), ymax: g.Max(f => f.p.y), gap: g.Average(f => Mathf.Min(f.gap, 99f))))
                    .OrderByDescending(g => g.n).ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"ColliderAudit: {columns} columns at {spacing} m over x {x0:F0}..{x1:F0} z {z0:F0}..{z1:F0}, {proxies.Count} drawn meshes " +
                              $"({unreadable} unreadable skipped), {flags.Count} unseen solid hits in {groups.Count} clusters, {Time.realtimeSinceStartup - t0:F1}s");
                sb.AppendLine("by owner (all / floating more than 2.5 m over anything drawn):");
                foreach (var g in flags.GroupBy(f => f.who).OrderByDescending(g => g.Count(f => f.gap > 2.5f)))
                    sb.AppendLine($"  {g.Count(),6}  {g.Count(f => f.gap > 2.5f),6}  ~{g.Count(f => f.gap > 2.5f) * spacing * spacing,7:F0} m2  {g.Key}");
                sb.AppendLine("clusters (24 m cells):");
                foreach (var g in groups.Where(g => g.gap > 2.5f).Take(400))
                    sb.AppendLine($"  {g.n,5}  at ({g.c.x:F1}, {g.c.y:F1}, {g.c.z:F1})  y {g.ymin:F1}..{g.ymax:F1}  gap {g.gap:F1}  {g.who}");
                sb.AppendLine("small mismatches (gap under 2.5 m, e.g. a flat solid over a pitched roof):");
                foreach (var g in groups.Where(g => g.gap <= 2.5f).Take(200))
                    sb.AppendLine($"  {g.n,5}  at ({g.c.x:F1}, {g.c.y:F1}, {g.c.z:F1})  y {g.ymin:F1}..{g.ymax:F1}  gap {g.gap:F1}  {g.who}");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath, sb.ToString());
                yield return "done: " + outPath;
            }
            finally
            {
                foreach (var p in proxies)
                {
                    var mc = p.GetComponent<MeshCollider>();
                    if (p.name == "AuditProxyArch" && mc != null) Object.DestroyImmediate(mc.sharedMesh);
                    Object.DestroyImmediate(p);
                }
                Time.timeScale = prevScale;
            }
        }

        static bool Seen(RaycastHit[] vis, int n, float y, float tol)
        {
            for (int j = 0; j < n; j++) if (Mathf.Abs(vis[j].point.y - y) <= tol) return true;
            return false;
        }

        static string Path(Transform t)
        {
            var s = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}
