using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace VulkanCSharpTutorial
{
    public sealed unsafe class VkGlfwExtension
    {
        private readonly Silk.NET.GLFW.Glfw glfw_;
        private readonly string[] names_;
        private bool disposedValue_;
        public VkGlfwExtension()
        {
            glfw_ = Silk.NET.GLFW.Glfw.GetApi();
            if (!glfw_.Init())
            {
                throw new InvalidOperationException("Failed to initialize glfw.");
            }
            if (!glfw_.VulkanSupported())
            {
                throw new NotSupportedException($"Vulkan is not being supported.");
            }
            var extNames = glfw_.GetRequiredInstanceExtensions(out var count);
            names_ = new string[count];
            if (extNames == null)
            {
                return;
            }
            for (var i = 0; i < count; i++)
            {
                if (extNames[i] == null)
                {
                    continue;
                }
                names_[i] = Marshal.PtrToStringAnsi((IntPtr)extNames[i]);
            }
            glfw_.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
        }

        ~VkGlfwExtension()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public Silk.NET.GLFW.Glfw Glfw => glfw_;
        public string[] Names => names_;

        public IntPtr CreateWindow(int width, int height, string title, bool resizable = true)
        {
            glfw_.WindowHint(WindowHintBool.Resizable, resizable);
            var handle = glfw_.CreateWindow(width, height, title, null, null);
            return new IntPtr(handle);
        }

        public IntPtr GetWindowSurface(Instance instance, IntPtr window)
        {
            VkNonDispatchableHandle handle;
            var ret = (Result)glfw_.CreateWindowSurface(instance.ToHandle(), (WindowHandle*)window, null, &handle);
            if (ret != Result.Success)
            {
                throw new InvalidOperationException($"Failed to create window surface. Err: {ret}");
            }
            return (IntPtr)handle.Handle;
        }

        public bool IsWindowClosing(IntPtr window)
        {
            return glfw_.WindowShouldClose((WindowHandle*)window);
        }

        public void WindowTick(IntPtr window)
        {
            glfw_.PollEvents();
        }

        public void MakeWindowCurrent(IntPtr window)
        {
            glfw_.MakeContextCurrent((WindowHandle*)window);
        }

        public void SwapBuffers(IntPtr window)
        {
            glfw_.SwapBuffers((WindowHandle*)window);
        }

        public void CloseWindow(IntPtr window)
        {
            glfw_.SetWindowShouldClose((WindowHandle*)window, true);
        }

        public void DestoryWindow(IntPtr window)
        {
            glfw_.DestroyWindow((WindowHandle*)window);
        }

        public void GetFrameBufferSize(IntPtr window, out int width, out int height)
        {
            glfw_.GetFramebufferSize((WindowHandle*)window, out width, out height);
        }

        public void ResizeWindow(IntPtr window, int width, int height)
        {
            glfw_.SetWindowSize((WindowHandle*)window, width, height);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue_)
            {
                glfw_.Dispose();
                disposedValue_ = true;
            }
        }
    }
}
