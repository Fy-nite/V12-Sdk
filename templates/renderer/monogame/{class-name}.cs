using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using V12.Rendering;

namespace {identifier};

/// <summary>
/// Backend-free V12 renderer skeleton. Implements the IRenderer contract from
/// V12.Rendering; fill in the resource uploads and frame realization for the
/// backend you are targeting.
/// </summary>
public sealed class {class-name} : IRenderer, IRendererLifecycle
{
    private readonly object _gate = new();
    private DeviceEpoch _epoch = DeviceEpoch.Initial;
    private uint _nextId;
    private bool _disposed;
    private RenderFrameLease? _lastFrame;

    public RendererCapabilities Capabilities { get; } = RendererCapabilities.All;

    public DeviceEpoch CurrentEpoch
    {
        get { lock (_gate) return _epoch; }
    }

    /// <summary>The latest accepted frame. Owned by this renderer until replaced.</summary>
    public RenderFrameLease? LastFrame
    {
        get { lock (_gate) return _lastFrame; }
    }

    public event Action<RendererDeviceEvent>? DeviceEvent;

    public void Submit(RenderFrameLease frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        RenderFrameLease? previous;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (frame.Epoch != _epoch)
                throw new RendererContractException(
                    $"Frame {frame.Sequence} belongs to {frame.Epoch}, current epoch is {_epoch}.");

            previous = _lastFrame;
            _lastFrame = frame;
        }

        // TODO: realize frame.Frame with the backend (draw now, or store for a
        // Draw call). The frame stays owned by this renderer until replaced.
        previous?.Release();
    }

    /// <summary>Called from the host's Draw() on the graphics thread.</summary>
    public void DrawFrame(GraphicsDevice device)
    {
        // TODO: draw LastFrame with MonoGame here.
        device.Clear(Color.CornflowerBlue);
    }

    public MeshHandle CreateMesh(in MeshData data)
    {
        lock (_gate) { return new MeshHandle(NewHandle()); }
    }

    public void UpdateMesh(MeshHandle mesh, in MeshData data)
    {
        // TODO: upload the updated geometry.
    }

    public void DestroyMesh(MeshHandle mesh)
    {
        // TODO: release the GPU mesh.
    }

    public TextureHandle CreateTexture(in TextureData data)
    {
        lock (_gate) { return new TextureHandle(NewHandle()); }
    }

    public void UpdateTexture(TextureHandle texture, in TextureData data)
    {
        // TODO: upload the updated texture.
    }

    public void DestroyTexture(TextureHandle texture)
    {
        // TODO: release the GPU texture.
    }

    public ShaderHandle CreateShader(in ShaderProgram program)
    {
        lock (_gate) { return new ShaderHandle(NewHandle()); }
    }

    public void DestroyShader(ShaderHandle shader)
    {
        // TODO: release the shader.
    }

    public MaterialHandle CreateMaterial(ShaderHandle shader, in MaterialDesc desc)
    {
        lock (_gate) { return new MaterialHandle(NewHandle()); }
    }

    public void UpdateMaterial(MaterialHandle material, in MaterialDesc desc)
    {
        // TODO: update the material state.
    }

    public void DestroyMaterial(MaterialHandle material)
    {
        // TODO: release the material.
    }

    public RenderTargetHandle CreateRenderTarget(in RenderTargetDesc desc)
    {
        lock (_gate) { return new RenderTargetHandle(NewHandle()); }
    }

    public void ResizeRenderTarget(RenderTargetHandle target, int width, int height)
    {
        // TODO: resize the target.
    }

    public void DestroyRenderTarget(RenderTargetHandle target)
    {
        // TODO: release the target.
    }

    public TextureHandle GetRenderTargetTexture(RenderTargetHandle target)
    {
        lock (_gate) { return new TextureHandle(NewHandle()); }
    }

    public ValueTask<ReadbackResult> RequestReadbackAsync(
        RenderTargetHandle? target,
        CancellationToken cancellationToken = default)
        => new(new ReadbackResult(false, 0, 0, TextureFormat.Rgba8Unorm, ReadOnlyMemory<byte>.Empty));

    /// <summary>Report a lost device. Handles from the old epoch become invalid.</summary>
    public void LoseDevice()
    {
        DeviceEpoch epoch;
        lock (_gate)
        {
            ThrowIfDisposed();
            epoch = _epoch;
        }

        DeviceEvent?.Invoke(new RendererDeviceEvent(RendererDeviceEventKind.Lost, epoch));
    }

    /// <summary>Recover after device loss and start a new epoch.</summary>
    public void RecoverDevice()
    {
        DeviceEpoch epoch;
        lock (_gate)
        {
            ThrowIfDisposed();
            _epoch = new DeviceEpoch(_epoch.Value + 1);
            _lastFrame?.Release();
            _lastFrame = null;
            epoch = _epoch;
        }

        DeviceEvent?.Invoke(new RendererDeviceEvent(RendererDeviceEventKind.Recovering, epoch));
        DeviceEvent?.Invoke(new RendererDeviceEvent(RendererDeviceEventKind.Ready, epoch));
    }

    public void Dispose()
    {
        RenderFrameLease? last;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            last = _lastFrame;
            _lastFrame = null;
        }

        last?.Release();
    }

    private ResourceHandle NewHandle() => new(++_nextId, 1, _epoch);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof({class-name}));
    }
}
