﻿using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Views;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;

namespace Veldrid.SampleGallery
{
    public class VeldridSurfaceView : SurfaceView, ISurfaceHolderCallback, IGalleryDriver
    {
        private readonly GraphicsBackend _backend;
        protected GraphicsDeviceOptions DeviceOptions { get; }
        private bool _surfaceDestroyed;
        private bool _paused;
        private bool _enabled;
        private bool _needsResize;
        private bool _surfaceCreated;
        private Vector2 _prevTouchPos;
        private readonly InputState _inputState = new InputState();
        private AdvancedFrameLoop _frameLoop;
        private Stopwatch _sw;
        private double _previousSeconds;

        public GraphicsDevice Device { get; protected set; }
        public Swapchain MainSwapchain { get; protected set; }
        uint IGalleryDriver.Width => (uint)Width;
        uint IGalleryDriver.Height => (uint)Height;

        public uint FrameIndex => _frameLoop.FrameIndex;

        public uint BufferCount => MainSwapchain.BufferCount;

        public bool SupportsImGui => true;

        public event Action DeviceCreated;
        public event Action DeviceDisposed;
        public event Action Resized;
        public event Action<double> Update;
        public event Func<double, CommandBuffer[]> Render;

        public VeldridSurfaceView(Context context, GraphicsBackend backend)
            : this(context, backend, new GraphicsDeviceOptions())
        {
        }

        public VeldridSurfaceView(Context context, GraphicsBackend backend, GraphicsDeviceOptions deviceOptions) : base(context)
        {
            if (!(backend == GraphicsBackend.Vulkan || backend == GraphicsBackend.OpenGLES))
            {
                throw new NotSupportedException($"{backend} is not supported on Android.");
            }

            _backend = backend;
            DeviceOptions = deviceOptions;
            Holder.AddCallback(this);
        }

        public void Disable()
        {
            _enabled = false;
        }

        public void SurfaceCreated(ISurfaceHolder holder)
        {
            bool deviceCreated = false;
            if (_backend == GraphicsBackend.Vulkan)
            {
                if (Device == null)
                {
                    Device = GraphicsDevice.CreateVulkan(DeviceOptions);
                    deviceCreated = true;
                }

                MainSwapchain?.Dispose();

                SwapchainSource ss = SwapchainSource.CreateAndroidSurface(holder.Surface.Handle, JNIEnv.Handle);
                SwapchainDescription sd = new SwapchainDescription(
                    ss,
                    (uint)Width,
                    (uint)Height,
                    DeviceOptions.SwapchainDepthFormat,
                    DeviceOptions.SyncToVerticalBlank,
                    DeviceOptions.SwapchainSrgbFormat);
                MainSwapchain = Device.ResourceFactory.CreateSwapchain(sd);
            }
            else
            {
                Debug.Assert(Device == null && MainSwapchain == null);
                SwapchainSource ss = SwapchainSource.CreateAndroidSurface(holder.Surface.Handle, JNIEnv.Handle);
                SwapchainDescription sd = new SwapchainDescription(
                    ss,
                    (uint)Width,
                    (uint)Height,
                    DeviceOptions.SwapchainDepthFormat,
                    DeviceOptions.SyncToVerticalBlank,
                    DeviceOptions.SwapchainSrgbFormat);
                Device = GraphicsDevice.CreateOpenGLES(DeviceOptions, sd);
                MainSwapchain = Device.MainSwapchain;
                deviceCreated = true;
            }

            _frameLoop = new AdvancedFrameLoop(Device, MainSwapchain);

            if (deviceCreated)
            {
                DeviceCreated?.Invoke();
            }

            _surfaceCreated = true;
        }

        public void RunContinuousRenderLoop()
        {
            Task.Factory.StartNew(() => RenderLoop(), TaskCreationOptions.LongRunning);
        }

        public void SurfaceDestroyed(ISurfaceHolder holder)
        {
            _surfaceDestroyed = true;
        }

        public void SurfaceChanged(ISurfaceHolder holder, [GeneratedEnum] Format format, int width, int height)
        {
            _needsResize = true;
        }

        private void RenderLoop()
        {
            _sw = Stopwatch.StartNew();
            _previousSeconds = _sw.Elapsed.TotalSeconds;
            _enabled = true;
            while (_enabled)
            {
                try
                {
                    if (_paused || !_surfaceCreated) { continue; }

                    if (_surfaceDestroyed)
                    {
                        HandleSurfaceDestroyed();
                        continue;
                    }

                    if (_needsResize)
                    {
                        _needsResize = false;
                        MainSwapchain.Resize((uint)Width, (uint)Height);
                        Resized?.Invoke();
                    }

                    FlushInput();

                    if (Device != null)
                    {
                        _frameLoop.RunFrame(HandleFrame);
                    }
                }
                catch (Exception e) when (!Debugger.IsAttached)
                {
                    Debug.WriteLine("Encountered an error while rendering: " + e);
                    throw;
                }
            }
        }

        private CommandBuffer[] HandleFrame(uint frameIndex, Framebuffer fb)
        {
            double currentSeconds = _sw.Elapsed.TotalSeconds;
            double elapsed = currentSeconds - _previousSeconds;
            _previousSeconds = currentSeconds;

            Update?.Invoke(elapsed);
            return Render?.Invoke(elapsed);
        }

        private void FlushInput()
        {
            lock (_inputState)
            {
                _inputState.MouseDelta = _inputState.MousePosition - _prevTouchPos;
                _prevTouchPos = _inputState.MousePosition;
            }
        }

        private void HandleSurfaceDestroyed()
        {
            if (_backend == GraphicsBackend.Vulkan)
            {
                MainSwapchain.Dispose();
                MainSwapchain = null;
            }
            else
            {
                Device.Dispose();
                Device = null;
                MainSwapchain = null;
                DeviceDisposed?.Invoke();
            }

            _enabled = false;
        }

        public void OnPause()
        {
            _paused = true;
        }

        public void OnResume()
        {
            _paused = false;
        }

        public InputStateView GetInputState() => _inputState.View;

        public override bool OnTouchEvent(MotionEvent e)
        {
            lock (_inputState)
            {
                switch (e.Action)
                {
                    case MotionEventActions.Down:
                        _inputState.MouseDown[0] = true;
                        _inputState.MousePosition = new Vector2(e.GetX(), e.GetY());
                        _prevTouchPos = _inputState.MousePosition;
                        break;
                    case MotionEventActions.Up:
                        _inputState.MouseDown[0] = false;
                        _inputState.MousePosition = new Vector2(e.GetX(), e.GetY());
                        break;
                    case MotionEventActions.Move:
                        _inputState.MousePosition = new Vector2(e.GetX(), e.GetY());
                        break;
                }

                return true;
            }
        }
    }
}
