using System;
using System.Diagnostics;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Rendering.RenderGraphModule
{
    /// <summary>
    /// Use this struct to set up a new Render Pass.
    /// </summary>
    [MovedFrom(true, "UnityEngine.Experimental.Rendering.RenderGraphModule", "UnityEngine.Rendering.RenderGraphModule")]
    [Obsolete("RenderGraphBuilder is deprecated, use IComputeRenderGraphBuilder/IRasterRenderGraphBuilder/IUnsafeRenderGraphBuilder instead.")]
    public struct RenderGraphBuilder : IDisposable
    {
        RenderGraphPass m_RenderPass;
        RenderGraphResourceRegistry m_Resources;
        RenderGraph m_RenderGraph;

        /// <summary>
        /// 是否已清理
        /// </summary>
        bool m_Disposed;

        #region Public Interface
        /// <summary>
        /// Specify that the pass will use a Texture resource as a color render target.
        /// This has the same effect as WriteTexture and also automatically sets the Texture to use as a render target.
        /// 与WriteTexture相同，但会在通道开始时在提供的绑定索引处自动将纹理绑定为渲染纹理。
        /// </summary>
        /// <param name="input">The Texture resource to use as a color render target.</param>
        /// <param name="index">Index for multiple render target usage.</param>
        /// <returns>An updated resource handle to the input resource.</returns>
        public TextureHandle UseColorBuffer(in TextureHandle input, int index)
        {
            CheckResource(input.handle, false);
            m_Resources.IncrementWriteCount(input.handle);
            m_RenderPass.SetColorBuffer(input, index);
            return input;
        }

        /// <summary>
        /// Specify that the pass will use a Texture resource as a depth buffer.
        /// </summary>
        /// <param name="input">The Texture resource to use as a depth buffer during the pass.</param>
        /// <param name="flags">Specify the access level for the depth buffer. This allows you to say whether you will read from or write to the depth buffer, or do both.</param>
        /// <returns>An updated resource handle to the input resource.</returns>
        public TextureHandle UseDepthBuffer(in TextureHandle input, DepthAccess flags)
        {
            CheckResource(input.handle, false);

            if ((flags & DepthAccess.Write) != 0)
                m_Resources.IncrementWriteCount(input.handle);
            if ((flags & DepthAccess.Read) != 0)
            {
                if (!m_Resources.IsRenderGraphResourceImported(input.handle) && m_Resources.TextureNeedsFallback(input))
                    WriteTexture(input);
            }

            m_RenderPass.SetDepthBuffer(input, flags);
            return input;
        }

        /// <summary>
        /// Specify a Texture resource to read from during the pass.
        /// 声明渲染通道会读取传入的纹理input
        /// ReadTexture方法用于指定渲染通道将在执行期间读取纹理资源。此声明允许渲染图系统跟踪通道之间的资源依赖关系并优化资源分配。
        /// </summary>
        /// <param name="input">The Texture resource to read from during the pass.</param>
        /// <returns>An updated resource handle to the input resource.</returns>
        public TextureHandle ReadTexture(in TextureHandle input)
        {
            CheckResource(input.handle);

            // Fallback Handling: The method checks if the resource is imported and whether it needs a fallback texture
            if (!m_Resources.IsRenderGraphResourceImported(input.handle) && m_Resources.TextureNeedsFallback(input))
            {
                var textureResource = m_Resources.GetTextureResource(input.handle);

                // Ensure we get a fallback to black
                // Black Texture Fallback: If a texture is read from but never written to, it ensures a fallback to a black texture
                textureResource.desc.clearBuffer = true;
                textureResource.desc.clearColor = Color.black;

                // If texture is read from but never written to, return a fallback black texture to have valid reads
                // Return one from the preallocated default textures if possible
                if (m_RenderGraph.GetImportedFallback(textureResource.desc, out var fallback))
                    return fallback;

                // If not, simulate a write to the texture so that it gets allocated
                // Write Simulation: If no preallocated fallback is available, it simulates a write to the texture by calling WriteTexture(input)
                // to ensure the texture gets properly allocated.
                WriteTexture(input);
            }

            // Dependency Registration: Finally, it registers the resource as a read dependency for the current pass
            m_RenderPass.AddResourceRead(input.handle);
            return input;
        }

        /// <summary>
        /// Specify a Texture resource to write to during the pass.
        /// 声明渲染通道会写入传入的纹理input
        /// </summary>
        /// <param name="input">The Texture resource to write to during the pass.</param>
        /// <returns>An updated resource handle to the input resource.</returns>
        public TextureHandle WriteTexture(in TextureHandle input)
        {
            CheckResource(input.handle);
            // Write Count Tracking: The method increments the write count for the resource through the resource registry,
            // which is used for resource lifetime management and optimization
            m_Resources.IncrementWriteCount(input.handle);
            // Pass Dependency Tracking: The method registers the resource write with the render pass, adding it to the
            // pass's resource write list for dependency analysis during graph compilation
            m_RenderPass.AddResourceWrite(input.handle);
            return input;
        }

        /// <summary>
        /// Specify a Texture resource to read and write to during the pass.
        /// </summary>
        /// <param name="input">The Texture resource to read and write to during the pass.</param>
        /// <returns>An updated resource handle to the input resource.</returns>
        public TextureHandle ReadWriteTexture(in TextureHandle input)
        {
            CheckResource(input.handle);
            m_Resources.IncrementWriteCount(input.handle);
            m_RenderPass.AddResourceWrite(input.handle);
            m_RenderPass.AddResourceRead(input.handle);
            return input;
        }

        /// <summary>
        /// Create a new Render Graph Texture resource.
        /// This texture will only be available for the current pass and will be assumed to be both written and read so users don't need to add explicit read/write declarations.
        /// </summary>
        /// <param name="desc">Texture descriptor.</param>
        /// <returns>A new transient TextureHandle.</returns>
        public TextureHandle CreateTransientTexture(in TextureDesc desc)
        {
            var result = m_Resources.CreateTexture(desc, m_RenderPass.index);
            m_RenderPass.AddTransientResource(result.handle);
            return result;
        }

        /// <summary>
        /// Create a new Render Graph Texture resource using the descriptor from another texture.
        /// This texture will only be available for the current pass and will be assumed to be both written and read so users don't need to add explicit read/write declarations.
        /// </summary>
        /// <param name="texture">Texture from which the descriptor should be used.</param>
        /// <returns>A new transient TextureHandle.</returns>
        public TextureHandle CreateTransientTexture(in TextureHandle texture)
        {
            var desc = m_Resources.GetTextureResourceDesc(texture.handle);
            var result = m_Resources.CreateTexture(desc, m_RenderPass.index);
            m_RenderPass.AddTransientResource(result.handle);
            return result;
        }

        /// <summary>
        /// Specify a RayTracingAccelerationStructure resource to build during the pass.
        /// </summary>
        /// <param name="input">The RayTracingAccelerationStructure resource to build during the pass.</param>
        /// <returns>An updated resource handle to the input resource.</returns>
        public RayTracingAccelerationStructureHandle WriteRayTracingAccelerationStructure(in RayTracingAccelerationStructureHandle input)
        {
            CheckResource(input.handle);
            m_Resources.IncrementWriteCount(input.handle);
            m_RenderPass.AddResourceWrite(input.handle);
            return input;
        }

        /// <summary>
        /// Specify a RayTracingAccelerationStructure resource to use during the pass.
        /// </summary>
        /// <param name="input">The RayTracingAccelerationStructure resource to use during the pass.</param>
        /// <returns>An updated resource handle to the input resource.</returns>
        public RayTracingAccelerationStructureHandle ReadRayTracingAccelerationStructure(in RayTracingAccelerationStructureHandle input)
        {
            CheckResource(input.handle);
            m_RenderPass.AddResourceRead(input.handle);
            return input;
        }

        /// <summary>
        /// Specify a Renderer List resource to use during the pass.
        /// </summary>
        /// <param name="input">The Renderer List resource to use during the pass.</param>
        /// <returns>An updated resource handle to the input resource.</returns>
        public RendererListHandle UseRendererList(in RendererListHandle input)
        {
            if(input.IsValid())
                m_RenderPass.UseRendererList(input);
            return input;
        }

        /// <summary>
        /// Specify a Graphics Buffer resource to read from during the pass.
        /// </summary>
        /// <param name="input">The Graphics Buffer resource to read from during the pass.</param>
        /// <returns>An updated resource handle to the input resource.</returns>
        public BufferHandle ReadBuffer(in BufferHandle input)
        {
            CheckResource(input.handle);
            m_RenderPass.AddResourceRead(input.handle);
            return input;
        }

        /// <summary>
        /// Specify a Graphics Buffer resource to write to during the pass.
        /// </summary>
        /// <param name="input">The Graphics Buffer resource to write to during the pass.</param>
        /// <returns>An updated resource handle to the input resource.</returns>
        public BufferHandle WriteBuffer(in BufferHandle input)
        {
            CheckResource(input.handle);
            m_RenderPass.AddResourceWrite(input.handle);
            m_Resources.IncrementWriteCount(input.handle);
            return input;
        }

        /// <summary>
        /// Create a new Render Graph Graphics Buffer resource.
        /// This Graphics Buffer will only be available for the current pass and will be assumed to be both written and read so users don't need to add explicit read/write declarations.
        /// </summary>
        /// <param name="desc">Graphics Buffer descriptor.</param>
        /// <returns>A new transient GraphicsBufferHandle.</returns>
        public BufferHandle CreateTransientBuffer(in BufferDesc desc)
        {
            var result = m_Resources.CreateBuffer(desc, m_RenderPass.index);
            m_RenderPass.AddTransientResource(result.handle);
            return result;
        }

        /// <summary>
        /// Create a new Render Graph Graphics Buffer resource using the descriptor from another Graphics Buffer.
        /// This Graphics Buffer will only be available for the current pass and will be assumed to be both written and read so users don't need to add explicit read/write declarations.
        /// </summary>
        /// <param name="graphicsbuffer">Graphics Buffer from which the descriptor should be used.</param>
        /// <returns>A new transient GraphicsBufferHandle.</returns>
        public BufferHandle CreateTransientBuffer(in BufferHandle graphicsbuffer)
        {
            var desc = m_Resources.GetBufferResourceDesc(graphicsbuffer.handle);
            var result = m_Resources.CreateBuffer(desc, m_RenderPass.index);
            m_RenderPass.AddTransientResource(result.handle);
            return result;
        }

        /// <summary>
        /// Specify the render function to use for this pass.
        /// A call to this is mandatory for the pass to be valid.
        /// </summary>
        /// <typeparam name="PassData">The Type of the class that provides data to the Render Pass.</typeparam>
        /// <param name="renderFunc">Render function for the pass.</param>
        public void SetRenderFunc<PassData>(BaseRenderFunc<PassData, RenderGraphContext> renderFunc)
            where PassData : class, new()
        {
            ((RenderGraphPass<PassData>)m_RenderPass).renderFunc = renderFunc;
        }

        /// <summary>
        /// Enable asynchronous compute for this pass.
        /// </summary>
        /// <param name="value">Set to true to enable asynchronous compute.</param>
        public void EnableAsyncCompute(bool value)
        {
            m_RenderPass.EnableAsyncCompute(value);
        }

        /// <summary>
        /// Allow or not pass culling
        /// By default all passes can be culled out if the render graph detects it's not actually used.
        /// In some cases, a pass may not write or read any texture but rather do something with side effects (like setting a global texture parameter for example).
        /// This function can be used to tell the system that it should not cull this pass.
        /// </summary>
        /// <param name="value">True to allow pass culling.</param>
        public void AllowPassCulling(bool value)
        {
            m_RenderPass.AllowPassCulling(value);
        }

        /// <summary>
        /// Enable foveated rendering for this pass.
        /// </summary>
        /// <param name="value">True to enable foveated rendering.</param>
        public void EnableFoveatedRasterization(bool value)
        {
            m_RenderPass.EnableFoveatedRasterization(value);
        }

        /// <summary>
        /// Dispose the RenderGraphBuilder instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Allow or not pass culling based on renderer list results
        /// By default all passes can be culled out if the render graph detects they are using a renderer list that is empty (does not draw any geometry)
        /// In some cases, a pass may not write or read any texture but rather do something with side effects (like setting a global texture parameter for example).
        /// This function can be used to tell the system that it should not cull this pass.
        /// </summary>
        /// <param name="value">True to allow pass culling.</param>
        public void AllowRendererListCulling(bool value)
        {
            m_RenderPass.AllowRendererListCulling(value);
        }

        /// <summary>
        /// Used to indicate that a pass depends on an external renderer list (that is not directly used in this pass).
        /// handle external renderer list dependencies in Unity's Render Graph system
        /// </summary>
        /// <param name="input">The renderer list handle this pass depends on.</param>
        /// <returns>A <see cref="RendererListHandle"/></returns>
        public RendererListHandle DependsOn(in RendererListHandle input)
        {
            m_RenderPass.UseRendererList(input);
            return input;
        }

        #endregion

        #region Internal Interface
        internal RenderGraphBuilder(RenderGraphPass renderPass, RenderGraphResourceRegistry resources, RenderGraph renderGraph)
        {
            m_RenderPass = renderPass;
            m_Resources = resources;
            m_RenderGraph = renderGraph;
            m_Disposed = false;
        }

        /// <summary>
        /// ensure builders are properly disposed
        /// The Dispose method is designed to be used with C#'s using statement pattern,
        /// ensuring automatic cleanup when the builder goes out of scope. The disposal
        /// process is when the actual pass configuration is finalized and registered
        /// with the render graph system. ensuring proper resource management and pass registration
        /// </summary>
        /// <param name="disposing"></param>
        void Dispose(bool disposing)
        {
            // State Management: Checking disposal state and preventing double disposal
            if (m_Disposed)
                return;

            // Pass Registration: Setting the render graph state and registering the pass
            m_RenderGraph.RenderGraphState = RenderGraphState.RecordingGraph;
            m_RenderGraph.OnPassAdded(m_RenderPass);
            m_Disposed = true;
        }

        /// <summary>
        /// 对资源有效性进行检查
        /// </summary>
        /// <param name="res">要检查的资源</param>
        /// <param name="checkTransientReadWrite">是否检查瞬态资源的读写</param>
        /// <exception cref="ArgumentException"></exception>
        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        void CheckResource(in ResourceHandle res, bool checkTransientReadWrite = true)
        {
            if(RenderGraph.enableValidityChecks)
            {
                // Basic Validity Check, verifies that the resource handle is valid
                if (res.IsValid())
                {
                    // Transient Resource Validation
                    int transientIndex = m_Resources.GetRenderGraphResourceTransientIndex(res);
                    // We have dontCheckTransientReadWrite here because users may want to use UseColorBuffer/UseDepthBuffer API to benefit from render target auto binding. In this case we don't want to raise the error.
                    if (transientIndex == m_RenderPass.index && checkTransientReadWrite)
                    {
                        Debug.LogError($"Trying to read or write a transient resource at pass {m_RenderPass.name}.Transient resource are always assumed to be both read and written.");
                    }
                    // Cross-Pass Transient Usage Check: It ensures that transient resources are not used across different render passes, throwing an exception if a transient resource from one pass is used in another
                    if (transientIndex != -1 && transientIndex != m_RenderPass.index)
                    {
                        throw new ArgumentException($"Trying to use a transient texture (pass index {transientIndex}) in a different pass (pass index {m_RenderPass.index}).");
                    }
                }
                else
                {
                    throw new ArgumentException($"Trying to use an invalid resource (pass {m_RenderPass.name}).");
                }
            }
        }

        /// <summary>
        /// 生成调试数据
        /// </summary>
        /// <param name="value"></param>
        internal void GenerateDebugData(bool value)
        {
            m_RenderPass.GenerateDebugData(value);
        }

        #endregion
    }
}
