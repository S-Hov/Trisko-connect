using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MarcusW.VncClient;
using MarcusW.VncClient.Protocol.Implementation.MessageTypes.Outgoing;
using MarcusW.VncClient.Rendering;
using RfbSize = MarcusW.VncClient.Size;
using RfbScreen = MarcusW.VncClient.Screen;
using RfbPixelFormat = MarcusW.VncClient.PixelFormat;

namespace v2rayN.Desktop.Geksa.Vnc;

/// <summary>A native Avalonia control; no external viewer window or window reparenting.</summary>
public sealed partial class VncView : Control, MarcusW.VncClient.Rendering.IRenderTarget, IDisposable
{
    private readonly object _frameGate = new();
    private IntPtr _pixels;
    private RfbSize _frameSize;
    private WriteableBitmap? _bitmap;
    private bool _disposed;
    private RfbConnection? _connection;
    private int _invalidatePending;
    private Avalonia.Size _measuredSize = new(1, 1);

    public RfbConnection? Connection
    {
        get => _connection;
        set
        {
            ResetKeyPresses();
            if (_connection != null) _connection.RenderTarget = null;
            _connection = value;
            if (value != null) value.RenderTarget = this;
        }
    }

    public VncView()
    {
        Focusable = true;
        ClipToBounds = true;
        LostFocus += (_, _) => ReleaseInput();
    }

    public IFramebufferReference GrabFramebufferReference(RfbSize size, IImmutableSet<RfbScreen> layout)
    {
        Monitor.Enter(_frameGate);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Bound allocations from remote size information and guard multiplication overflow.
            if (size.Width < 1 || size.Height < 1 || (long)size.Width * size.Height > 64 * 1024 * 1024)
                throw new IOException(global::ServiceLib.Geksa.TriskoLocalization.Text("Разрешение удалённого экрана слишком велико."));
            if (_pixels == IntPtr.Zero || _frameSize != size)
            {
                var next = Marshal.AllocHGlobal(checked(size.Width * size.Height * 4));
                unsafe { new Span<byte>((void*)next, checked(size.Width * size.Height * 4)).Clear(); }
                if (_pixels != IntPtr.Zero) Marshal.FreeHGlobal(_pixels);
                _pixels = next;
                _frameSize = size;
            }
            return new FrameReference(_pixels, size, () =>
            {
                Monitor.Exit(_frameGate);
                if (Interlocked.Exchange(ref _invalidatePending, 1) == 0)
                    Dispatcher.UIThread.Post(() =>
                    {
                        Interlocked.Exchange(ref _invalidatePending, 0);
                        if (_disposed) return;
                        InvalidateMeasure();
                        InvalidateVisual();
                    });
            });
        }
        catch { Monitor.Exit(_frameGate); throw; }
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        if (!Monitor.TryEnter(_frameGate)) return _measuredSize;
        try { return _measuredSize = new Avalonia.Size(Math.Max(1, _frameSize.Width), Math.Max(1, _frameSize.Height)); }
        finally { Monitor.Exit(_frameGate); }
    }

    public override unsafe void Render(DrawingContext context)
    {
        base.Render(context);
        // A slow network may hold a frame reference while decoding. Keep the UI responsive.
        if (!Monitor.TryEnter(_frameGate))
        {
            if (_bitmap != null) context.DrawImage(_bitmap, new Rect(Bounds.Size));
            return;
        }
        try
        {
            if (_disposed || _pixels == IntPtr.Zero) return;
            var size = new PixelSize(_frameSize.Width, _frameSize.Height);
            if (_bitmap == null || _bitmap.PixelSize != size)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(size, new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }
            using (var target = _bitmap.Lock())
            {
                var rowBytes = checked(_frameSize.Width * 4);
                for (var row = 0; row < _frameSize.Height; row++)
                    Buffer.MemoryCopy((byte*)_pixels + row * rowBytes,
                        (byte*)target.Address + row * target.RowBytes, target.RowBytes, rowBytes);
            }
            context.DrawImage(_bitmap, new Rect(Bounds.Size));
        }
        finally { Monitor.Exit(_frameGate); }
    }

    public void SendCtrlAltDelete()
    {
        if (Connection == null) return;
        foreach (var symbol in new[] { KeySymbol.Control_L, KeySymbol.Alt_L, KeySymbol.Delete })
            Connection.EnqueueMessage(new KeyEventMessage(true, symbol));
        foreach (var symbol in new[] { KeySymbol.Delete, KeySymbol.Alt_L, KeySymbol.Control_L })
            Connection.EnqueueMessage(new KeyEventMessage(false, symbol));
    }

    public void ReleaseInput()
    {
        ResetKeyPresses();
        Connection?.EnqueueMessage(new PointerEventMessage(new Position(0, 0), MouseButtons.None));
    }

    public void Dispose()
    {
        Connection = null;
        lock (_frameGate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_pixels != IntPtr.Zero) Marshal.FreeHGlobal(_pixels);
            _pixels = IntPtr.Zero;
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }

    private sealed class FrameReference(IntPtr address, RfbSize size, Action release) : IFramebufferReference
    {
        private Action? _release = release;
        public IntPtr Address => address;
        public RfbSize Size => size;
        public RfbPixelFormat Format => new("BGRA", 32, 24, false, true, false, 255, 255, 255, 0, 16, 8, 0, 0);
        public double HorizontalDpi => 96;
        public double VerticalDpi => 96;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
