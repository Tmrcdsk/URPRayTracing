using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

public class ShadowmapGeneration : ScriptableRendererFeature
{
    private RTHandle m_ShadowmapTexHandle; // Shadowmap指针
    private const string k_ShadowmapTexName = "_ShadowMap"; // Shadowmap在Shader中的引用名

    class CustomRenderPass : ScriptableRenderPass
    {
        private RTHandle m_ShadowmapTexHandle;

        // This method is called before executing the render pass.
        // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
        // When empty this render pass will render to the active camera render target.
        // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
        // The render pipeline will ensure target setup and clearing happens in a performant manner.
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        // Here you can implement the rendering logic.
        // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
        // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Shadowmap Generation");
            cmd.Clear();

            // ShadowTagId指定使用哪个Shader进行本次绘制
            var drawSettings = new DrawingSettings(new ShaderTagId("CustomShadowCaster"), new SortingSettings(renderingData.cameraData.camera) { criteria = SortingCriteria.CommonOpaque });
            var filterSettings = new FilteringSettings(RenderQueueRange.opaque);

            // === 1. View Matrix ===
            Light sun = RenderSettings.sun;
            Vector3 lightDirWS = sun.transform.forward;

            float biasCoeff = -0.77f;

            float texelSizeWS = (15f - 0.01f) / 1024f;
            float biasWS = biasCoeff * texelSizeWS;

            Matrix4x4 view = sun.transform.worldToLocalMatrix;

            // === 2. Projection Matrix ===
            const float size = 6f, near = 0.01f, far = 15f;
            Matrix4x4 projCPU = Matrix4x4.Ortho(-size, size, -size, size, near, far);
            Matrix4x4 projGPU = GL.GetGPUProjectionMatrix(projCPU, true);

            Matrix4x4 LightVPMatrix = projGPU * view;

            cmd.SetGlobalVector("_MainLightDirWS", lightDirWS);
            cmd.SetGlobalFloat("_CustomShadowBias", biasWS);
            cmd.SetGlobalMatrix("_LightVPMatrix", LightVPMatrix);

            // 绘制指令
            context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }

        public void SetRTHandles(ref RTHandle tex)
        {
            m_ShadowmapTexHandle = tex;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureTarget(m_ShadowmapTexHandle);
            ConfigureClear(ClearFlag.Depth, Color.clear);
        }
    }

    CustomRenderPass m_ShadowmapRenderPass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ShadowmapRenderPass = new CustomRenderPass();

        // Configures where the render pass should be injected.
        m_ShadowmapRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingShadows;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_ShadowmapRenderPass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        var desc = new RenderTextureDescriptor(1024, 1024, RenderTextureFormat.Shadowmap, 24);
        RenderingUtils.ReAllocateIfNeeded(ref m_ShadowmapTexHandle, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: k_ShadowmapTexName);
        // 将Shadowmap传入RenderPass
        m_ShadowmapRenderPass.SetRTHandles(ref m_ShadowmapTexHandle);
    }

}


