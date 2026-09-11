using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Rune.Direwolf
{
    internal sealed class DirewolfSkin
    {
        internal Mesh Mesh;
        internal Texture2D Albedo;
        internal string[] Paths;
        private static Sprite mountIcon;
        internal static Sprite LoadMountIcon()
        {
            if (mountIcon) return mountIcon;
            using (var stream=typeof(DirewolfSkin).Assembly.GetManifestResourceStream("RuneDirewolf.mountIcon"))
            using (var memory=new MemoryStream())
            {
                (stream ?? throw new InvalidDataException("Missing direwolf mount icon")).CopyTo(memory);
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false) { name="RuneDirewolfMountIcon", wrapMode=TextureWrapMode.Clamp };
                if (!ImageConversion.LoadImage(texture,memory.ToArray())) throw new InvalidDataException("Invalid direwolf mount icon");
                mountIcon=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
                mountIcon.name="RuneDirewolfMountIcon";return mountIcon;
            }
        }

        internal static DirewolfSkin Load()
        {
            var result = new DirewolfSkin();
            using (var stream = typeof(DirewolfSkin).Assembly.GetManifestResourceStream("RuneDirewolf.skin"))
            using (var r = new BinaryReader(stream ?? throw new InvalidDataException("Missing direwolf skin")))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RDW1") throw new InvalidDataException("Invalid direwolf skin");
                int vc = Count(r, 60000), ic = Count(r, 300000), bc = Count(r, 128);
                if (ic % 3 != 0) throw new InvalidDataException("Invalid triangle count");
                result.Paths = new string[bc];
                var bind = new Matrix4x4[bc];
                for (int i = 0; i < bc; i++)
                {
                    int length = Count(r, 1024);
                    result.Paths[i] = Encoding.UTF8.GetString(r.ReadBytes(length));
                    for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) bind[i][row, col] = Float(r);
                }
                if (result.Paths.Distinct().Count() != bc) throw new InvalidDataException("Duplicate bone path");
                var vertices = new Vector3[vc]; var normals = new Vector3[vc]; var uv = new Vector2[vc];
                for (int i = 0; i < vc; i++) vertices[i] = new Vector3(Float(r), Float(r), Float(r));
                for (int i = 0; i < vc; i++) normals[i] = new Vector3(Float(r), Float(r), Float(r));
                for (int i = 0; i < vc; i++) uv[i] = new Vector2(Float(r), Float(r));
                var weights = new BoneWeight[vc];
                for (int i = 0; i < vc; i++) weights[i] = new BoneWeight { boneIndex0 = Index(r, bc), boneIndex1 = Index(r, bc), boneIndex2 = Index(r, bc), boneIndex3 = Index(r, bc) };
                for (int i = 0; i < vc; i++)
                {
                    var w = weights[i];
                    w.weight0 = Float(r); w.weight1 = Float(r); w.weight2 = Float(r); w.weight3 = Float(r);
                    if (w.weight0 < 0 || w.weight1 < 0 || w.weight2 < 0 || w.weight3 < 0 || Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1) > .001f)
                        throw new InvalidDataException("Invalid skin weights");
                    weights[i] = w;
                }
                var indices = new int[ic]; for (int i = 0; i < ic; i++) indices[i] = Index(r, vc);
                if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Unexpected skin data");
                result.Mesh = new Mesh { name = "RuneDirewolfSkin", vertices = vertices, normals = normals, uv = uv, boneWeights = weights, bindposes = bind, triangles = indices };
                result.Mesh.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), vc).ToArray();
                result.Mesh.RecalculateTangents();
                result.Mesh.RecalculateBounds();
            }
            using (var stream = typeof(DirewolfSkin).Assembly.GetManifestResourceStream("RuneDirewolf.albedo"))
            using (var memory = new MemoryStream())
            {
                (stream ?? throw new InvalidDataException("Missing texture")).CopyTo(memory);
                result.Albedo = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "RuneDirewolfAlbedo" };
                if (!ImageConversion.LoadImage(result.Albedo, memory.ToArray())) throw new InvalidDataException("Invalid texture");
            }
            return result;
        }

        // Reuse the game's live skeleton and Animator so events, transitions and attack timing stay native.
        internal Transform Apply(GameObject prefab)
        {
            var original = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(x => x.transform.Find("CG") != null)
                ?? prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
            if (!original) throw new InvalidOperationException("Wolf renderer unavailable");
            var skeleton = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.Find(Paths[0]) != null && x.Find(Paths[0]).name == "CG");
            if (!skeleton) throw new InvalidOperationException("Wolf skeleton unavailable");
            var bones = Paths.Select(p => skeleton.Find(p) ?? throw new InvalidOperationException("Missing native wolf bone: " + p)).ToArray();
            var holder = new GameObject("RuneDirewolfSkin"); holder.transform.SetParent(skeleton, false);
            var renderer = holder.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = Mesh; renderer.bones = bones; renderer.rootBone = bones[0];
            renderer.quality = SkinQuality.Bone4;
            renderer.localBounds = new Bounds(new Vector3(0, .6f, 0), new Vector3(2.5f, 2.5f, 3.5f));
            var material = new Material(original.sharedMaterial) { name = "RuneDirewolfFur", mainTexture = Albedo };
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", Texture2D.normalTexture);
            if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", 0);
            if (material.HasProperty("_BumpStrength")) material.SetFloat("_BumpStrength", 0);
            if (material.HasProperty("_MetallicGlossMap")) material.SetTexture("_MetallicGlossMap", null);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .15f);
            renderer.sharedMaterial = material;
            foreach (var old in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (old != renderer) old.enabled = false;
            return skeleton;
        }

        private static int Count(BinaryReader r, int max) { int n=r.ReadInt32(); if(n<=0||n>max) throw new InvalidDataException("Invalid count"); return n; }
        private static int Index(BinaryReader r, int count) { int n=r.ReadInt32(); if(n<0||n>=count) throw new InvalidDataException("Invalid index"); return n; }
        private static float Float(BinaryReader r) { float v=r.ReadSingle(); if(float.IsNaN(v)||float.IsInfinity(v)) throw new InvalidDataException("Invalid float"); return v; }
    }
}
