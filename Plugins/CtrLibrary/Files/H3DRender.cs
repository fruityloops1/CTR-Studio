﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using GLFrameworkEngine;
using Toolbox.Core.ViewModels;
using SPICA.Rendering;
using SPICA.Formats.CtrH3D;
using SPICA.Formats.CtrGfx;
using OpenTK.Graphics;
using CtrLibrary.Bcres;
using OpenTK.Graphics.OpenGL;
using SPICA.Formats.CtrH3D.Texture;
using SPICA.Formats.CtrH3D.Model;
using SPICA.Formats.CtrH3D.LUT ;
using SPICA.PICA.Shader;
using Newtonsoft.Json;
using CtrLibrary.UI;

namespace CtrLibrary.Rendering
{
    /// <summary>
    /// Represents a render instance for H3D data used for rendering PICA 3DS data.
    /// </summary>
    public class H3DRender : EditableObject
    {
        /// <summary>
        /// The texture cache of globally loaded textures. This cache is only used for UI purposes to get the H3D instances for viewing.
        /// </summary>
        public static Dictionary<string, H3DTexture> TextureCache = new Dictionary<string, H3DTexture>();

        public static List<Renderer> RenderCache = new List<Renderer>();

        //The H3D scene instance
        private H3D Scene;

        /// <summary>
        /// The renderer instance used to load and render out the scene.
        /// </summary>
        public Renderer Renderer;

        /// <summary>
        /// The skeleton list used to draw and render bone data.
        /// </summary>
        public List<SkeletonRenderer> Skeletons = new List<SkeletonRenderer>();

        public H3DRender(Stream stream, NodeBase parent) : base(parent) {
            Load(Gfx.Open(stream).ToH3D());
        }

        public H3DRender(H3D h3d, NodeBase parent) : base(parent) {
            Load(h3d);
        }

        /// <summary>
        /// Inserts a model into the scene with a given index.
        /// </summary>
        public void InsertModel(H3DModel model, int index)
        {
            if (index != -1)
            {
                Renderer.Models.RemoveAt(index);
                Renderer.Models.Insert(index, new Model(Renderer, model));
            }
            else
            {
                Renderer.Models.Add(new SPICA.Rendering.Model(Renderer, model));
            }
        }

        /// <summary>
        /// Updates all the render uniform data.
        /// </summary>
        public void UpdateAllUniforms()
        {
            Renderer.UpdateAllUniforms();
        }

        /// <summary>
        /// Updates all the renderer shaders.
        /// </summary>
        public void UpdateShaders()
        {
            Renderer.UpdateAllShaders();
            Renderer.UpdateAllUniforms();
        }

        private void Load(H3D h3d)
        {
            //Caches are used to search up globally loaded data within the UI
            //So a file can access the data externally from other files
            foreach (var tex in h3d.Textures)
            {
                if (!TextureCache.ContainsKey(tex.Name))
                    TextureCache.Add(tex.Name, tex);
            }
            foreach (var lut in h3d.LUTs)
            {
                if (!LUTCacheManager.Cache.ContainsKey(lut.Name))
                    LUTCacheManager.Cache.Add(lut.Name, lut);
            }

            Scene = h3d;

            //Local render for workspaces
            Renderer = new Renderer(1, 1);
            RenderCache.Add(Renderer);

            //Configurable scene lighting
            if (File.Exists("CtrScene.json"))
            {
                var lights = JsonConvert.DeserializeObject<Light[]>(File.ReadAllText("CtrScene.json"));
                foreach (var light in Renderer.Lights)
                    Renderer.Lights.Add(light);
            }
            else
            {
                Renderer.Lights.Add(new Light()
                {
                    Ambient = new Color4(0.1f, 0.1f, 0.1f, 1.0f),
                    Diffuse = new Color4(0.4f, 0.4f, 0.4f, 1.0f),
                    Specular0 = new Color4(0.3f, 0.3f, 0.3f, 1.0f),
                    Specular1 = new Color4(0.4f, 0.4f, 0.4f, 1.0f),
                    TwoSidedDiffuse = true,
                    Position = new OpenTK.Vector3(0, 0, 0),
                    Enabled = true,
                    Type = LightType.PerFragment,
                });
            }
            Renderer.Merge(Scene);  

            //Load the render cache for loading globally renderable data (textures, luts)
            foreach (var tex in Renderer.Textures)
                if (!Renderer.TextureCache.ContainsKey(tex.Key))
                    Renderer.TextureCache.Add(tex.Key, tex.Value);

            foreach (var lut in Renderer.LUTs)
                if (!Renderer.LUTs.ContainsKey(lut.Key))
                    Renderer.LUTs.Add(lut.Key, lut.Value);

            //Caches are used to search up globally loaded data within the UI and renders
            //So a file can access the data externally from other files
            LUTCacheManager.Setup(Renderer);
        }

        public override void Dispose()
        {
            base.Dispose();

            foreach (var skel in this.Skeletons)
                skel.Dispose();

            //Remove from global texture cache
            foreach (var tex in Renderer.Textures)
                if (TextureCache.ContainsKey(tex.Key))
                    TextureCache.Remove(tex.Key);
            //Remove from global lut cache
            foreach (var lut in Renderer.LUTs)
                if (LUTCacheManager.Cache.ContainsKey(lut.Key))
                    LUTCacheManager.Cache.Remove(lut.Key);

            Renderer.DeleteAll();
            RenderCache.Remove(this.Renderer);
        }

        public override void DrawModel(GLContext context, Pass pass)
        {
            if (pass != Pass.OPAQUE || Renderer == null)
                return;

            //Setup the debug render data
            PrepareDebugShading();

            for (int i = 0; i < Skeletons.Count; i++)
            {
                if (Renderer.Models.Count <= i)
                    break;

                var skel = Skeletons[i];
                if (Renderer.Models[i].SkeletalAnim != null)
                {
                    var skelAnim = Renderer.Models[i].SkeletalAnim.FrameSkeleton;
                    for (int j = 0; j < skel.Bones.Count; j++)
                    {
                        if (skelAnim.Length <= j)
                            continue;

                        skel.Bones[j].BoneData.AnimationController.Position = skelAnim[j].Translation;
                        skel.Bones[j].BoneData.AnimationController.Rotation = skelAnim[j].Rotation;
                        skel.Bones[j].BoneData.AnimationController.Scale = skelAnim[j].Scale;
                    }
                    skel.Update();
                }
            }

            //Setup the camera
            Renderer.Camera.ProjectionMatrix = context.Camera.ProjectionMatrix;
            Renderer.Camera.ViewMatrix = context.Camera.ViewMatrix;
            Renderer.Camera.Translation = context.Camera.TargetPosition;

            foreach (var model in Renderer.Models)
                model.Transform = this.Transform.TransformMatrix;

            //Draw the models
            Renderer.Render();

            //Draw the skeleton
            foreach (var skeleton in Skeletons)
                skeleton.DrawModel(context, pass);

            //Reset depth state to defaults
            GL.DepthMask(true);
            GL.ColorMask(true, true, true, true);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.Disable(EnableCap.StencilTest);

        }

        private void PrepareDebugShading()
        {
            //Todo. Would be better to have 3ds specific debugging modes than the in tool ones.
            Renderer.DebugShadingMode = (int)DebugShaderRender.DebugRendering;
            //Selected bone for debug rendering weights
            Renderer.SelectedBoneID = Toolbox.Core.Runtime.SelectedBoneIndex;
        }
    }
}
