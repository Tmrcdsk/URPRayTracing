using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering; // 用于 RTHandle

public class OutputColorRenderPass : ScriptableRenderPass
{
    private RayTracingShader _ray_tracing_shader = null;
    private RTHandle _output_target;

    public OutputColorRenderPass()
    {
        _ray_tracing_shader = Resources.Load<RayTracingShader>("Shaders/OutputColor");
    }

    private static class GpuParams
    {
        public static readonly int OutputTarget = Shader.PropertyToID("_OutputTarget");
    }

    public void Setup()
    {
    }

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        var outputColorDesc = cameraTextureDescriptor;
        outputColorDesc.enableRandomWrite = true;
        outputColorDesc.colorFormat = RenderTextureFormat.ARGBFloat;
        outputColorDesc.depthBufferBits = 0;

        if (_output_target != null)
        {
            _output_target.Release();
            _output_target = null;
        }

        RenderingUtils.ReAllocateIfNeeded(ref _output_target, outputColorDesc);

        // 把它标记成本 Pass 的目标之一（虽然 RayTracing 会直接写它，
        // 但 ConfigureTarget 可以让 URP 知道它是我们这个 Pass 的输出）
        ConfigureTarget(_output_target);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get("OutputColorPass");

        if (_ray_tracing_shader == null)
        {
            Debug.LogError("[OutputColor] RayTracingShader 加载失败，请检查 Resources/Shaders/OutputColor.raytrace 是否存在并且已被正确导入。");
            return;
        }

        using (new ProfilingScope(cmd, new ProfilingSampler("RayTracing")))
        {
            cmd.SetRayTracingTextureParam(_ray_tracing_shader, GpuParams.OutputTarget, _output_target);
            cmd.SetRayTracingShaderPass(_ray_tracing_shader, "RayTracing");

            var cameraDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            cmd.DispatchRays(_ray_tracing_shader, "OutputColorRayGenShader", (uint)cameraDescriptor.width, (uint)cameraDescriptor.height, 1);
        }

        using (new ProfilingScope(cmd, new ProfilingSampler("FinalBlit")))
        {
            cmd.Blit(_output_target, BuiltinRenderTextureType.CameraTarget);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public void Release()
    {
        if (_output_target != null)
        {
            _output_target.Release();
            _output_target = null;
        }
    }
}
