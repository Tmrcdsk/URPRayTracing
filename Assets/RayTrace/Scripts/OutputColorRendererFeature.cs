using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class OutputColorRendererFeature : ScriptableRendererFeature
{
    private OutputColorRenderPass m_OutputColorRenderPass;

    public override void Create()
    {
        if (!isActive)
        {
            SafeReleaseFeatureResources();
            return;
        }

        m_OutputColorRenderPass ??= new OutputColorRenderPass();
    }

    protected override void Dispose(bool disposing)
    {
        SafeReleaseFeatureResources();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.isPreviewCamera) return;
        m_OutputColorRenderPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing + 1;
        m_OutputColorRenderPass.Setup();
        
        renderer.EnqueuePass(m_OutputColorRenderPass);
    }

    private void SafeReleaseFeatureResources()
    {
        m_OutputColorRenderPass?.Release(); m_OutputColorRenderPass = null;
    }

}
