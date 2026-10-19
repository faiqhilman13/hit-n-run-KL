using UnityEngine;

namespace KampungRun
{
    /// <summary>Generated meshes go to the GPU only, unless the environment variable
    /// KR_READABLE_MESHES=1 asks to keep their CPU copy (Editor/ColliderAudit needs it).</summary>
    public static class MeshKeep
    {
        public static readonly bool Readable = System.Environment.GetEnvironmentVariable("KR_READABLE_MESHES") == "1";

        public static void Upload(Mesh m) => m.UploadMeshData(!Readable);
    }
}
