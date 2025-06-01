using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering; // 用于 RTHandle

public class OutputColorRenderPass : ScriptableRenderPass
{
    private RayTracingShader _ray_tracing_shader = null;
    private RayTracingAccelerationStructure _acceleration_structure = null;
    private RTHandle _output_target;
    private Camera m_Camera;
    private Vector4 m_OutputTargetSize;
    private int m_FrameIndex = 0;
    private ComputeBuffer m_PRNGStates = null;

    public OutputColorRenderPass()
    {
        _ray_tracing_shader = Resources.Load<RayTracingShader>("Shaders/OutputColor");
        _acceleration_structure = new RayTracingAccelerationStructure();
    }

    private static class GpuParams
    {
        public static readonly int OutputTarget = Shader.PropertyToID("_OutputTarget");
        public static readonly int OutputTargetSize = Shader.PropertyToID("_OutputTargetSize");
        public static readonly int FrameIndex = Shader.PropertyToID("_FrameIndex");
        public static readonly int AccelerationStructure = Shader.PropertyToID("_AccelerationStructure");
        public static readonly int PRNGStates = Shader.PropertyToID("_PRNGStates");
    }

    private static class CameraShaderParams
    {
        public static readonly int _InvCameraViewProj = Shader.PropertyToID("_InvCameraViewProj");
        public static readonly int _WorldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
        public static readonly int _CameraFarDistance = Shader.PropertyToID("_CameraFarDistance");
    }

    public void RequirePRNGStates(Camera camera)
    {
        // 只在缓冲区为空或相机分辨率改变时重新创建缓冲区
        if (m_PRNGStates == null || 
            (m_PRNGStates != null && m_PRNGStates.count != camera.pixelWidth * camera.pixelHeight))
        {
            // 释放旧的缓冲区
            if (m_PRNGStates != null)
            {
                m_PRNGStates.Release();
                m_PRNGStates = null;
            }
            
            // 创建新的缓冲区
            m_PRNGStates = new ComputeBuffer(camera.pixelWidth * camera.pixelHeight, 4 * 4, ComputeBufferType.Structured, ComputeBufferMode.Immutable);

            var mt = new MersenneTwister();
            mt.InitGenRand((uint)DateTime.Now.Ticks);

            var data = new uint[camera.pixelWidth * camera.pixelHeight * 4];
            for (var i = 0; i < camera.pixelWidth * camera.pixelHeight * 4; ++i)
                data[i] = mt.GenRandInt32();
            m_PRNGStates.SetData(data);
        }
    }

    private void resetFrame()
    {
        m_FrameIndex = 0;
        // 清除渲染目标，防止累积效果
        if (_output_target != null)
        {
            CommandBuffer cmd = CommandBufferPool.Get("ResetRenderTarget");
            cmd.SetRenderTarget(_output_target);
            cmd.ClearRenderTarget(false, true, Color.black);
            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
    
    private Vector3 m_LastCameraPosition;
    private Quaternion m_LastCameraRotation;
    
    private void updateFrame()
    {
        // 检测相机是否移动或旋转，如果是则重置帧计数
        if (m_Camera != null)
        {
            if (Vector3.Distance(m_LastCameraPosition, m_Camera.transform.position) > 0.001f ||
                Quaternion.Angle(m_LastCameraRotation, m_Camera.transform.rotation) > 0.1f)
            {
                resetFrame();
            }
            
            m_LastCameraPosition = m_Camera.transform.position;
            m_LastCameraRotation = m_Camera.transform.rotation;
        }
    }

    public void Setup()
    {
        // 初始化相机位置和旋转信息
        if (Camera.main != null)
        {
            m_LastCameraPosition = Camera.main.transform.position;
            m_LastCameraRotation = Camera.main.transform.rotation;
        }
        
        // 重置帧索引
        resetFrame();
    }

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        #region [Acceleration Structure]
        var allRenderers = GameObject.FindObjectsOfType<MeshRenderer>();
        foreach (var rnd in allRenderers) {
            // 提前给这些 Renderer 打上同一个 Layer，或者用 RayTracingLayer 来筛选
            _acceleration_structure.AddInstance(rnd);
        }
        #endregion

        #region [Output Target Reallocation]
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
        #endregion

        // 把它标记成本 Pass 的目标之一（虽然 RayTracing 会直接写它，
        // 但 ConfigureTarget 可以让 URP 知道它是我们这个 Pass 的输出）
        ConfigureTarget(_output_target);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        m_Camera = renderingData.cameraData.camera;
        m_OutputTargetSize = new Vector4(m_Camera.pixelWidth, m_Camera.pixelHeight, 1.0f / m_Camera.pixelWidth, 1.0f / m_Camera.pixelHeight);
        RequirePRNGStates(m_Camera);
        
        // 检测相机移动并在必要时重置帧计数
        updateFrame();
        CommandBuffer cmd = CommandBufferPool.Get("OutputColorPass");

        if (_ray_tracing_shader == null)
        {
            Debug.LogError("[OutputColor] RayTracingShader 加载失败，请检查 Resources/Shaders/OutputColor.raytrace 是否存在并且已被正确导入。");
            return;
        }

        // 限制最大帧累积数量为1000，防止画面变得过暗
        if (m_FrameIndex < 1000)
        {
            using (new ProfilingScope(cmd, new ProfilingSampler("RayTracing")))
            {
                cmd.BuildRayTracingAccelerationStructure(_acceleration_structure);
                cmd.SetRayTracingAccelerationStructure(_ray_tracing_shader, GpuParams.AccelerationStructure, _acceleration_structure);
                cmd.SetRayTracingShaderPass(_ray_tracing_shader, "RayTracing");

                cmd.SetGlobalVector(CameraShaderParams._WorldSpaceCameraPos, m_Camera.transform.position);
                var projMatrix = GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix, false);
                var viewMatrix = m_Camera.worldToCameraMatrix;
                var viewProjMatrix = projMatrix * viewMatrix;
                var invViewProjMatrix = Matrix4x4.Inverse(viewProjMatrix);
                cmd.SetGlobalMatrix(CameraShaderParams._InvCameraViewProj, invViewProjMatrix);
                cmd.SetGlobalFloat(CameraShaderParams._CameraFarDistance, m_Camera.farClipPlane);

                cmd.SetRayTracingTextureParam(_ray_tracing_shader, GpuParams.OutputTarget, _output_target);
                cmd.SetRayTracingVectorParam(_ray_tracing_shader, GpuParams.OutputTargetSize, m_OutputTargetSize);
                cmd.SetRayTracingIntParam(_ray_tracing_shader, GpuParams.FrameIndex, m_FrameIndex);
                cmd.SetRayTracingBufferParam(_ray_tracing_shader, GpuParams.PRNGStates, m_PRNGStates);

                var cameraDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                cmd.DispatchRays(_ray_tracing_shader, "AntialiasingRayGenShader", (uint)cameraDescriptor.width, (uint)cameraDescriptor.height, 1);
            }
            context.ExecuteCommandBuffer(cmd);
            if (m_Camera.cameraType == CameraType.Game)
                m_FrameIndex++;
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
        
        if (m_PRNGStates != null)
        {
            m_PRNGStates.Release();
            m_PRNGStates = null;
        }
        
        if (_acceleration_structure != null)
        {
            _acceleration_structure.Dispose();
            _acceleration_structure = null;
        }
    }
}
