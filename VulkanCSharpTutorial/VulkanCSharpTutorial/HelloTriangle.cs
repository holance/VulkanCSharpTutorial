﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Image = Silk.NET.Vulkan.Image;
using Vortice.Dxc;
using System.Text;
using System.Numerics;
using Buffer = Silk.NET.Vulkan.Buffer;
using System.Xml;

namespace VulkanCSharpTutorial
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    unsafe struct Vertex
    {
        public Vector3 position;
        public Vector4 color = new (1, 1, 1, 1);

        public Vertex() { }
        public Vertex(in Vector3 position) { this.position = position; }
        public Vertex(in Vector3 position, in Vector4 color) { this.position = position; this.color = color; }  

        public static VertexInputBindingDescription getBindingDescription()
        {
            VertexInputBindingDescription bindingDesc;
            bindingDesc.Binding = 0;
            bindingDesc.Stride = (uint)sizeof(Vertex);
            bindingDesc.InputRate = VertexInputRate.Vertex;      
            return bindingDesc;
        }

        public static VertexInputAttributeDescription[] GetVertexInputAttributes()
        {
            var attributes = new VertexInputAttributeDescription[2];
            attributes[0].Binding = 0;
            attributes[0].Location = 0;
            attributes[0].Format = Format.R32G32B32Sfloat;
            attributes[0].Offset = 0;

            attributes[1].Binding = 0;
            attributes[1].Location = 1;
            attributes[1].Format = Format.R32G32B32A32Sfloat;
            attributes[1].Offset = (uint)sizeof(Vector3);
            return attributes;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    unsafe struct GlobalVar
    {
        public Matrix4x4 view;
        public Matrix4x4 projection;
        public Matrix4x4 viewProj;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    unsafe struct ModelVar
    {
        public Matrix4x4 world;
    }

    public class HelloTriangleApplication
    {
        public const bool EnableValidationLayers = true;
        public const int MaxFramesInFlight = 3;
        public const bool EventBasedRendering = false;

        public void Run()
        {
            InitWindow();
            InitVulkan();
            MainLoop();
            Cleanup();
        }

        private IWindow _window;

        private Instance _instance;
        private DebugUtilsMessengerEXT _debugMessenger;
        private SurfaceKHR _surface;

        private PhysicalDevice _physicalDevice;
        private Device _device;

        private Queue _graphicsQueue;
        private Queue _presentQueue;

        private SwapchainKHR _swapchain;
        private Image[] _swapchainImages;
        private Format _swapchainImageFormat;
        private Extent2D _swapchainExtent;
        private ImageView[] _swapchainImageViews;
        private Framebuffer[] _swapchainFramebuffers;

        private RenderPass _renderPass;
        private DescriptorSetLayout _descriptorSetLayout;
        private PipelineLayout _pipelineLayout;
        private Pipeline _graphicsPipeline;

        private CommandPool _commandPool;
        private CommandBuffer[] _commandBuffers;

        private Semaphore[] _imageAvailableSemaphores;
        private Semaphore[] _renderFinishedSemaphores;
        private Fence[] _inFlightFences;
        private Fence[] _imagesInFlight;
        private uint _currentFrame;

        private bool _framebufferResized = false;

        private Vk _vk;
        private KhrSurface _vkSurface;
        private KhrSwapchain _vkSwapchain;
        private ExtDebugUtils _debugUtils;
        private string[][] _validationLayerNamesPriorityList =
        {
            new [] { "VK_LAYER_KHRONOS_validation" },
            new [] { "VK_LAYER_LUNARG_standard_validation" },
            new []
            {
                "VK_LAYER_GOOGLE_threading",
                "VK_LAYER_LUNARG_parameter_validation",
                "VK_LAYER_LUNARG_object_tracker",
                "VK_LAYER_LUNARG_core_validation",
                "VK_LAYER_GOOGLE_unique_objects",
            }
        };
        private string[] _validationLayers;
        private string[] _instanceExtensions = { ExtDebugUtils.ExtensionName };
        private string[] _deviceExtensions = { KhrSwapchain.ExtensionName };


        static Vertex[] _vertices = new Vertex[] { 
            new Vertex(new Vector3(1, 1, 0), new Vector4(1, 0, 0, 1)),
            new Vertex(new Vector3(-1, 1, 0)), 
            new Vertex(new Vector3(0, -1, 0)), 
            new Vertex(new Vector3(-1, 1, 0)),
            new Vertex(new Vector3(1, 1, 0), new Vector4(1, 0, 0, 1)),
        };
        static Buffer _vertexBufferStaging;
        static DeviceMemory _vertexMemStaging;
        static Buffer _vertexBuffer;
        static DeviceMemory _vertexMem;

        static Buffer[] uniformBuffers;
        static DeviceMemory[] uniformBufferMemories;
        static DescriptorPool _descriptorPool;
        static DescriptorSet[] descriptorSets;

        private void InitWindow()
        {
            var opts = WindowOptions.DefaultVulkan;
            opts.IsEventDriven = EventBasedRendering;

            // Uncomment the line below to use SDL
            // Window.PrioritizeSdl();

            _window = Window.Create(opts);
            _window.Initialize(); // For safety the window should be initialized before querying the VkSurface

            if (_window?.VkSurface is null)
            {
                throw new NotSupportedException("Windowing platform doesn't support Vulkan.");
            }

            _window.FramebufferResize += OnFramebufferResize;
        }

        private void OnFramebufferResize(Vector2D<int> size)
        {
            _framebufferResized = true;
            RecreateSwapChain();
            _window.DoRender();
        }

        private void InitVulkan()
        {
            CreateInstance();
            SetupDebugMessenger();
            CreateSurface();
            PickPhysicalDevice();
            CreateLogicalDevice();
            CreateSwapChain();
            CreateImageViews();
            CreateRenderPass();
            CreateUniforms();
            CreateDescriptorSetLayout();
            CreateDescriptorPool();
            CreateDescriptorSets();
            CreateGraphicsPipeline();
            CreateFramebuffers();
            CreateCommandPool();
            CreateBuffers();
            CreateCommandBuffers();
            CreateSyncObjects();
        }

        private void MainLoop()
        {
            _window.Render += DrawFrame;
            _window.Run();
            _vk.DeviceWaitIdle(_device);
        }

        private unsafe void DrawFrame(double obj)
        {
            var fence = _inFlightFences[_currentFrame];
            _vk.WaitForFences(_device, 1, in fence, Vk.True, ulong.MaxValue);

            uint imageIndex;
            Result result = _vkSwapchain.AcquireNextImage
                (_device, _swapchain, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, &imageIndex);

            if (result == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapChain();
                return;
            }
            else if (result != Result.Success && result != Result.SuboptimalKhr)
            {
                throw new Exception("failed to acquire swap chain image!");
            }

            if (_imagesInFlight[imageIndex].Handle != 0)
            {
                _vk.WaitForFences(_device, 1, in _imagesInFlight[imageIndex], Vk.True, ulong.MaxValue);
            }

            _imagesInFlight[imageIndex] = _inFlightFences[_currentFrame];
            UpdateUniformBuffer(_currentFrame);
            SubmitInfo submitInfo = new SubmitInfo { SType = StructureType.SubmitInfo };

            Semaphore[] waitSemaphores = { _imageAvailableSemaphores[_currentFrame] };
            PipelineStageFlags[] waitStages = { PipelineStageFlags.ColorAttachmentOutputBit };
            submitInfo.WaitSemaphoreCount = 1;
            var signalSemaphore = _renderFinishedSemaphores[_currentFrame];
            RecordCommands(_currentFrame);
            fixed (Semaphore* waitSemaphoresPtr = waitSemaphores)
            {
                fixed (PipelineStageFlags* waitStagesPtr = waitStages)
                {
                    submitInfo.PWaitSemaphores = waitSemaphoresPtr;
                    submitInfo.PWaitDstStageMask = waitStagesPtr;

                    submitInfo.CommandBufferCount = 1;
                    var buffer = _commandBuffers[imageIndex];
                    submitInfo.PCommandBuffers = &buffer;

                    submitInfo.SignalSemaphoreCount = 1;
                    submitInfo.PSignalSemaphores = &signalSemaphore;

                    _vk.ResetFences(_device, 1, &fence);

                    if (_vk.QueueSubmit
                            (_graphicsQueue, 1, &submitInfo, _inFlightFences[_currentFrame]) != Result.Success)
                    {
                        throw new Exception("failed to submit draw command buffer!");
                    }
                }
            }

            fixed (SwapchainKHR* swapchain = &_swapchain)
            {
                PresentInfoKHR presentInfo = new PresentInfoKHR
                {
                    SType = StructureType.PresentInfoKhr,
                    WaitSemaphoreCount = 1,
                    PWaitSemaphores = &signalSemaphore,
                    SwapchainCount = 1,
                    PSwapchains = swapchain,
                    PImageIndices = &imageIndex
                };

                result = _vkSwapchain.QueuePresent(_presentQueue, &presentInfo);
            }

            if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr || _framebufferResized)
            {
                _framebufferResized = false;
                RecreateSwapChain();
            }
            else if (result != Result.Success)
            {
                throw new Exception("failed to present swap chain image!");
            }

            _currentFrame = (_currentFrame + 1) % MaxFramesInFlight;
        }

        unsafe void UpdateUniformBuffer(uint currentImage)
        {
            var globalVar = new GlobalVar();
            globalVar.view = Matrix4x4.Identity;
            globalVar.projection = Matrix4x4.Identity;
            globalVar.viewProj = Matrix4x4.Identity;
            void* data = null;
            _vk.MapMemory(_device, uniformBufferMemories[currentImage], 0, (uint)sizeof(GlobalVar), 0, ref data);
            NativeHelper.MemoryCopy((IntPtr)data, (IntPtr)(void*)&globalVar, (uint)sizeof(GlobalVar));
            _vk.UnmapMemory(_device, uniformBufferMemories[currentImage]);
        }

        private unsafe void Cleanup()
        {
            CleanupSwapchain();

            for (var i = 0; i < MaxFramesInFlight; i++)
            {
                _vk.DestroySemaphore(_device, _renderFinishedSemaphores[i], null);
                _vk.DestroySemaphore(_device, _imageAvailableSemaphores[i], null);
                _vk.DestroyFence(_device, _inFlightFences[i], null);
            }

            _vk.DestroyCommandPool(_device, _commandPool, null);

            _vk.DestroyDevice(_device, null);

            if (EnableValidationLayers)
            {
                _debugUtils.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
            }

            _vkSurface.DestroySurface(_instance, _surface, null);
            _vk.DestroyInstance(_instance, null);
        }

        private unsafe void CleanupSwapchain()
        {
            foreach (var framebuffer in _swapchainFramebuffers)
            {
                _vk.DestroyFramebuffer(_device, framebuffer, null);
            }

            fixed (CommandBuffer* buffers = _commandBuffers)
            {
                _vk.FreeCommandBuffers(_device, _commandPool, (uint)_commandBuffers.Length, buffers);
            }

            _vk.DestroyPipeline(_device, _graphicsPipeline, null);
            _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
            _vk.DestroyRenderPass(_device, _renderPass, null);

            foreach (var imageView in _swapchainImageViews)
            {
                _vk.DestroyImageView(_device, imageView, null);
            }

            _vkSwapchain.DestroySwapchain(_device, _swapchain, null);
        }

        private unsafe string[]? GetOptimalValidationLayers()
        {
            var layerCount = 0u;
            _vk.EnumerateInstanceLayerProperties(&layerCount, (LayerProperties*)0);

            var availableLayers = new LayerProperties[layerCount];
            fixed (LayerProperties* availableLayersPtr = availableLayers)
            {
                _vk.EnumerateInstanceLayerProperties(&layerCount, availableLayersPtr);
            }

            var availableLayerNames = availableLayers.Select(availableLayer => Marshal.PtrToStringAnsi((nint)availableLayer.LayerName)).ToArray();
            foreach (var validationLayerNameSet in _validationLayerNamesPriorityList)
            {
                if (validationLayerNameSet.All(validationLayerName => availableLayerNames.Contains(validationLayerName)))
                {
                    return validationLayerNameSet;
                }
            }

            return null;
        }

        private unsafe void CreateInstance()
        {
            _vk = Vk.GetApi();

            if (EnableValidationLayers)
            {
                _validationLayers = GetOptimalValidationLayers();
                if (_validationLayers is null)
                {
                    throw new NotSupportedException("Validation layers requested, but not available!");
                }
            }

            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)Marshal.StringToHGlobalAnsi("Hello Triangle"),
                ApplicationVersion = new Version32(1, 0, 0),
                PEngineName = (byte*)Marshal.StringToHGlobalAnsi("No Engine"),
                EngineVersion = new Version32(1, 0, 0),
                ApiVersion = Vk.Version11
            };

            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo
            };

            var extensions = _window.VkSurface!.GetRequiredExtensions(out var extCount);
            // TODO Review that this count doesn't realistically exceed 1k (recommended max for stackalloc)
            // Should probably be allocated on heap anyway as this isn't super performance critical.
            var newExtensions = stackalloc byte*[(int)(extCount + _instanceExtensions.Length)];
            for (var i = 0; i < extCount; i++)
            {
                newExtensions[i] = extensions[i];
            }

            for (var i = 0; i < _instanceExtensions.Length; i++)
            {
                newExtensions[extCount + i] = (byte*)SilkMarshal.StringToPtr(_instanceExtensions[i]);
            }

            extCount += (uint)_instanceExtensions.Length;
            createInfo.EnabledExtensionCount = extCount;
            createInfo.PpEnabledExtensionNames = newExtensions;

            if (EnableValidationLayers)
            {
                createInfo.EnabledLayerCount = (uint)_validationLayers.Length;
                createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(_validationLayers);
            }
            else
            {
                createInfo.EnabledLayerCount = 0;
                createInfo.PNext = null;
            }

            fixed (Instance* instance = &_instance)
            {
                if (_vk.CreateInstance(&createInfo, null, instance) != Result.Success)
                {
                    throw new Exception("Failed to create instance!");
                }
            }

            _vk.CurrentInstance = _instance;

            if (!_vk.TryGetInstanceExtension(_instance, out _vkSurface))
            {
                throw new NotSupportedException("KHR_surface extension not found.");
            }

            Marshal.FreeHGlobal((nint)appInfo.PApplicationName);
            Marshal.FreeHGlobal((nint)appInfo.PEngineName);

            if (EnableValidationLayers)
            {
                SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
            }
        }

        private unsafe void SetupDebugMessenger()
        {
            if (!EnableValidationLayers) return;
            if (!_vk.TryGetInstanceExtension(_instance, out _debugUtils)) return;

            var createInfo = new DebugUtilsMessengerCreateInfoEXT();
            PopulateDebugMessengerCreateInfo(ref createInfo);

            fixed (DebugUtilsMessengerEXT* debugMessenger = &_debugMessenger)
            {
                if (_debugUtils.CreateDebugUtilsMessenger
                        (_instance, &createInfo, null, debugMessenger) != Result.Success)
                {
                    throw new Exception("Failed to create debug messenger.");
                }
            }
        }

        private unsafe void PopulateDebugMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT createInfo)
        {
            createInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
            createInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                         DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                         DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
            createInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
            createInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
        }

        private unsafe uint DebugCallback
        (
            DebugUtilsMessageSeverityFlagsEXT messageSeverity,
            DebugUtilsMessageTypeFlagsEXT messageTypes,
            DebugUtilsMessengerCallbackDataEXT* pCallbackData,
            void* pUserData
        )
        {
            if (messageSeverity > DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt)
            {
                Console.WriteLine
                    ($"{messageSeverity} {messageTypes}" + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));

            }

            return Vk.False;
        }

        private unsafe void CreateSurface()
        {
            _surface = _window.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();
        }

        private unsafe void PickPhysicalDevice()
        {
            var devices = _vk.GetPhysicalDevices(_instance);

            if (!devices.Any())
            {
                throw new NotSupportedException("Failed to find GPUs with Vulkan support.");
            }

            _physicalDevice = devices.FirstOrDefault(device =>
            {
                var indices = FindQueueFamilies(device);

                var extensionsSupported = CheckDeviceExtensionSupport(device);

                var swapChainAdequate = false;
                if (extensionsSupported)
                {
                    var swapChainSupport = QuerySwapChainSupport(device);
                    swapChainAdequate = swapChainSupport.Formats.Length != 0 && swapChainSupport.PresentModes.Length != 0;
                }

                return indices.IsComplete() && extensionsSupported && swapChainAdequate;
            });

            if (_physicalDevice.Handle == 0)
                throw new Exception("No suitable device.");
        }

        // Caching the returned values breaks the ability for resizing the window
        private unsafe SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice device)
        {
            var details = new SwapChainSupportDetails();
            _vkSurface.GetPhysicalDeviceSurfaceCapabilities(device, _surface, out var surfaceCapabilities);
            details.Capabilities = surfaceCapabilities;

            var formatCount = 0u;
            _vkSurface.GetPhysicalDeviceSurfaceFormats(device, _surface, &formatCount, null);

            if (formatCount != 0)
            {
                details.Formats = new SurfaceFormatKHR[formatCount];

                using var mem = GlobalMemory.Allocate((int)formatCount * sizeof(SurfaceFormatKHR));
                var formats = (SurfaceFormatKHR*)Unsafe.AsPointer(ref mem.GetPinnableReference());

                _vkSurface.GetPhysicalDeviceSurfaceFormats(device, _surface, &formatCount, formats);

                for (var i = 0; i < formatCount; i++)
                {
                    details.Formats[i] = formats[i];
                }
            }

            var presentModeCount = 0u;
            _vkSurface.GetPhysicalDeviceSurfacePresentModes(device, _surface, &presentModeCount, null);

            if (presentModeCount != 0)
            {
                details.PresentModes = new PresentModeKHR[presentModeCount];

                using var mem = GlobalMemory.Allocate((int)presentModeCount * sizeof(PresentModeKHR));
                var modes = (PresentModeKHR*)Unsafe.AsPointer(ref mem.GetPinnableReference());

                _vkSurface.GetPhysicalDeviceSurfacePresentModes(device, _surface, &presentModeCount, modes);

                for (var i = 0; i < presentModeCount; i++)
                {
                    details.PresentModes[i] = modes[i];
                }
            }

            return details;
        }

        private unsafe bool CheckDeviceExtensionSupport(PhysicalDevice device)
        {
            return _deviceExtensions.All(ext => _vk.IsDeviceExtensionPresent(device, ext));
        }

        // Caching these values might have unintended side effects
        private unsafe QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
        {
            var indices = new QueueFamilyIndices();

            uint queryFamilyCount = 0;
            _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queryFamilyCount, null);

            using var mem = GlobalMemory.Allocate((int)queryFamilyCount * sizeof(QueueFamilyProperties));
            var queueFamilies = (QueueFamilyProperties*)Unsafe.AsPointer(ref mem.GetPinnableReference());

            _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queryFamilyCount, queueFamilies);
            for (var i = 0u; i < queryFamilyCount; i++)
            {
                var queueFamily = queueFamilies[i];
                // note: HasFlag is slow on .NET Core 2.1 and below.
                // if you're targeting these versions, use ((queueFamily.QueueFlags & QueueFlags.QueueGraphicsBit) != 0)
                if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                {
                    indices.GraphicsFamily = i;
                }

                _vkSurface.GetPhysicalDeviceSurfaceSupport(device, i, _surface, out var presentSupport);

                if (presentSupport == Vk.True)
                {
                    indices.PresentFamily = i;
                }

                if (indices.IsComplete())
                {
                    break;
                }
            }

            return indices;
        }

        public struct QueueFamilyIndices
        {
            public uint? GraphicsFamily { get; set; }
            public uint? PresentFamily { get; set; }

            public bool IsComplete()
            {
                return GraphicsFamily.HasValue && PresentFamily.HasValue;
            }
        }

        public struct SwapChainSupportDetails
        {
            public SurfaceCapabilitiesKHR Capabilities { get; set; }
            public SurfaceFormatKHR[] Formats { get; set; }
            public PresentModeKHR[] PresentModes { get; set; }
        }

        private unsafe void CreateLogicalDevice()
        {
            var indices = FindQueueFamilies(_physicalDevice);
            var uniqueQueueFamilies = indices.GraphicsFamily.Value == indices.PresentFamily.Value
                ? new[] { indices.GraphicsFamily.Value }
                : new[] { indices.GraphicsFamily.Value, indices.PresentFamily.Value };

            using var mem = GlobalMemory.Allocate((int)uniqueQueueFamilies.Length * sizeof(DeviceQueueCreateInfo));
            var queueCreateInfos = (DeviceQueueCreateInfo*)Unsafe.AsPointer(ref mem.GetPinnableReference());

            var queuePriority = 1f;
            for (var i = 0; i < uniqueQueueFamilies.Length; i++)
            {
                var queueCreateInfo = new DeviceQueueCreateInfo
                {
                    SType = StructureType.DeviceQueueCreateInfo,
                    QueueFamilyIndex = uniqueQueueFamilies[i],
                    QueueCount = 1,
                    PQueuePriorities = &queuePriority
                };
                queueCreateInfos[i] = queueCreateInfo;
            }

            var deviceFeatures = new PhysicalDeviceFeatures();

            var createInfo = new DeviceCreateInfo();
            createInfo.SType = StructureType.DeviceCreateInfo;
            createInfo.QueueCreateInfoCount = (uint)uniqueQueueFamilies.Length;
            createInfo.PQueueCreateInfos = queueCreateInfos;
            createInfo.PEnabledFeatures = &deviceFeatures;
            createInfo.EnabledExtensionCount = (uint)_deviceExtensions.Length;

            var enabledExtensionNames = SilkMarshal.StringArrayToPtr(_deviceExtensions);
            createInfo.PpEnabledExtensionNames = (byte**)enabledExtensionNames;

            if (EnableValidationLayers)
            {
                createInfo.EnabledLayerCount = (uint)_validationLayers.Length;
                createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(_validationLayers);
            }
            else
            {
                createInfo.EnabledLayerCount = 0;
            }

            fixed (Device* device = &_device)
            {
                if (_vk.CreateDevice(_physicalDevice, &createInfo, null, device) != Result.Success)
                {
                    throw new Exception("Failed to create logical device.");
                }
            }

            fixed (Queue* graphicsQueue = &_graphicsQueue)
            {
                _vk.GetDeviceQueue(_device, indices.GraphicsFamily.Value, 0, graphicsQueue);
            }

            fixed (Queue* presentQueue = &_presentQueue)
            {
                _vk.GetDeviceQueue(_device, indices.PresentFamily.Value, 0, presentQueue);
            }

            _vk.CurrentDevice = _device;

            if (!_vk.TryGetDeviceExtension(_instance, _device, out _vkSwapchain))
            {
                throw new NotSupportedException("KHR_swapchain extension not found.");
            }

            Console.WriteLine($"{_vk.CurrentInstance?.Handle} {_vk.CurrentDevice?.Handle}");
        }

        private unsafe bool CreateSwapChain()
        {
            var swapChainSupport = QuerySwapChainSupport(_physicalDevice);

            var surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
            var presentMode = ChooseSwapPresentMode(swapChainSupport.PresentModes);
            var extent = ChooseSwapExtent(swapChainSupport.Capabilities);

            // TODO: On SDL minimizing the window does not affect the frameBufferSize.
            // This check can be removed if it does
            if (extent.Width == 0 || extent.Height == 0)
                return false;

            var imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
            if (swapChainSupport.Capabilities.MaxImageCount > 0 &&
                imageCount > swapChainSupport.Capabilities.MaxImageCount)
            {
                imageCount = swapChainSupport.Capabilities.MaxImageCount;
            }

            var createInfo = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit
            };

            var indices = FindQueueFamilies(_physicalDevice);
            uint[] queueFamilyIndices = { indices.GraphicsFamily.Value, indices.PresentFamily.Value };

            fixed (uint* qfiPtr = queueFamilyIndices)
            {
                if (indices.GraphicsFamily != indices.PresentFamily)
                {
                    createInfo.ImageSharingMode = SharingMode.Concurrent;
                    createInfo.QueueFamilyIndexCount = 2;
                    createInfo.PQueueFamilyIndices = qfiPtr;
                }
                else
                {
                    createInfo.ImageSharingMode = SharingMode.Exclusive;
                }

                createInfo.PreTransform = swapChainSupport.Capabilities.CurrentTransform;
                createInfo.CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr;
                createInfo.PresentMode = presentMode;
                createInfo.Clipped = Vk.True;

                createInfo.OldSwapchain = default;

                if (!_vk.TryGetDeviceExtension(_instance, _vk.CurrentDevice.Value, out _vkSwapchain))
                {
                    throw new NotSupportedException("KHR_swapchain extension not found.");
                }

                fixed (SwapchainKHR* swapchain = &_swapchain)
                {
                    if (_vkSwapchain.CreateSwapchain(_device, &createInfo, null, swapchain) != Result.Success)
                    {
                        throw new Exception("failed to create swap chain!");
                    }
                }
            }

            _vkSwapchain.GetSwapchainImages(_device, _swapchain, &imageCount, null);
            _swapchainImages = new Image[imageCount];
            fixed (Image* swapchainImage = _swapchainImages)
            {
                _vkSwapchain.GetSwapchainImages(_device, _swapchain, &imageCount, swapchainImage);
            }

            _swapchainImageFormat = surfaceFormat.Format;
            _swapchainExtent = extent;

            return true;
        }

        private unsafe void RecreateSwapChain()
        {
            Vector2D<int> framebufferSize = _window.FramebufferSize;

            while (framebufferSize.X == 0 || framebufferSize.Y == 0)
            {
                framebufferSize = _window.FramebufferSize;
                _window.DoEvents();
            }

            _ = _vk.DeviceWaitIdle(_device);

            CleanupSwapchain();

            // TODO: On SDL it is possible to get an invalid swap chain when the window is minimized.
            // This check can be removed when the above frameBufferSize check catches it.
            while (!CreateSwapChain())
            {
                _window.DoEvents();
            }

            CreateImageViews();
            CreateRenderPass();
            CreateGraphicsPipeline();
            CreateFramebuffers();
            CreateCommandBuffers();

            _imagesInFlight = new Fence[_swapchainImages.Length];
        }

        private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities)
        {
            if (capabilities.CurrentExtent.Width != uint.MaxValue)
            {
                return capabilities.CurrentExtent;
            }

            var actualExtent = new Extent2D
            { Height = (uint)_window.FramebufferSize.Y, Width = (uint)_window.FramebufferSize.X };
            actualExtent.Width = new[]
            {
                capabilities.MinImageExtent.Width,
                new[] {capabilities.MaxImageExtent.Width, actualExtent.Width}.Min()
            }.Max();
            actualExtent.Height = new[]
            {
                capabilities.MinImageExtent.Height,
                new[] {capabilities.MaxImageExtent.Height, actualExtent.Height}.Min()
            }.Max();

            return actualExtent;
        }

        private PresentModeKHR ChooseSwapPresentMode(PresentModeKHR[] presentModes)
        {
            foreach (var availablePresentMode in presentModes)
            {
                if (availablePresentMode == PresentModeKHR.MailboxKhr)
                {
                    return availablePresentMode;
                }
            }

            return PresentModeKHR.FifoKhr;
        }

        private SurfaceFormatKHR ChooseSwapSurfaceFormat(SurfaceFormatKHR[] formats)
        {
            foreach (var format in formats)
            {
                if (format.Format == Format.B8G8R8A8Unorm)
                {
                    return format;
                }
            }

            return formats[0];
        }

        private unsafe void CreateImageViews()
        {
            _swapchainImageViews = new ImageView[_swapchainImages.Length];

            for (var i = 0; i < _swapchainImages.Length; i++)
            {
                var createInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = _swapchainImages[i],
                    ViewType = ImageViewType.Type2D,
                    Format = _swapchainImageFormat,
                    Components =
                    {
                        R = ComponentSwizzle.Identity,
                        G = ComponentSwizzle.Identity,
                        B = ComponentSwizzle.Identity,
                        A = ComponentSwizzle.Identity
                    },
                    SubresourceRange =
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    }
                };

                ImageView imageView = default;
                if (_vk.CreateImageView(_device, &createInfo, null, &imageView) != Result.Success)
                {
                    throw new Exception("failed to create image views!");
                }

                _swapchainImageViews[i] = imageView;
            }
        }

        private unsafe void CreateRenderPass()
        {
            var colorAttachment = new AttachmentDescription
            {
                Format = _swapchainImageFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr
            };

            var colorAttachmentRef = new AttachmentReference
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal
            };

            var subpass = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &colorAttachmentRef
            };

            var dependency = new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = 0,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit
            };

            var renderPassInfo = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 1,
                PAttachments = &colorAttachment,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 1,
                PDependencies = &dependency
            };

            fixed (RenderPass* renderPass = &_renderPass)
            {
                if (_vk.CreateRenderPass(_device, &renderPassInfo, null, renderPass) != Result.Success)
                {
                    throw new Exception("failed to create render pass!");
                }
            }
        }

        unsafe string GetString(byte* ptr)
        {
            int length = 0;
            while (length < 4096 && ptr[length] != 0)
                length++;
            // Decode UTF-8 bytes to string.
            return Encoding.UTF8.GetString(ptr, length);
        }

        unsafe void CreateDescriptorSetLayout()
        {
            var uboLayoutBinding = new DescriptorSetLayoutBinding()
            {
                Binding = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.VertexBit
            };
            var layoutInfo = new DescriptorSetLayoutCreateInfo()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings = &uboLayoutBinding
            };
            fixed(DescriptorSetLayout* pLayout = &_descriptorSetLayout)
            {
                if(_vk.CreateDescriptorSetLayout(_device, &layoutInfo, null, pLayout) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout.");
                }
            }
        }

        private unsafe void CreateGraphicsPipeline()
        {
            using var vertShaderFS = File.Open("shader.vert.spv", FileMode.Open);
            using var fragShaderFS = File.Open("shader.frag.spv", FileMode.Open);
            var vertShaderCode = new byte[vertShaderFS.Length];
            var fragShaderCode = new byte[fragShaderFS.Length];
            vertShaderFS.Read(vertShaderCode, 0, vertShaderCode.Length);
            fragShaderFS.Read(fragShaderCode, 0, fragShaderCode.Length);

            var vertShaderModule = CreateShaderModule(vertShaderCode);
            var fragShaderModule = CreateShaderModule(fragShaderCode);

            var vertShaderStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            var fragShaderStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };

            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = vertShaderStageInfo;
            shaderStages[1] = fragShaderStageInfo;

            var inputBinding = Vertex.getBindingDescription();
            var inputAttributes = Vertex.GetVertexInputAttributes();

            using var pInputAttributes = inputAttributes.AsMemory().Pin();

            var vertexInputInfo = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                VertexAttributeDescriptionCount = (uint)inputAttributes.Length,
                PVertexBindingDescriptions = &inputBinding,
                PVertexAttributeDescriptions = (VertexInputAttributeDescription*)pInputAttributes.Pointer
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
                PrimitiveRestartEnable = Vk.False
            };

            var viewport = new Viewport
            {
                X = 0.0f,
                Y = 0.0f,
                Width = _swapchainExtent.Width,
                Height = _swapchainExtent.Height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };

            var scissor = new Rect2D { Offset = default, Extent = _swapchainExtent };

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                PViewports = &viewport,
                ScissorCount = 1,
                PScissors = &scissor
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = Vk.False,
                RasterizerDiscardEnable = Vk.False,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = CullModeFlags.BackBit,
                FrontFace = FrontFace.Clockwise,
                DepthBiasEnable = Vk.False
            };

            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                SampleShadingEnable = Vk.False,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };

            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit |
                                 ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit |
                                 ColorComponentFlags.ABit,
                BlendEnable = Vk.False
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = Vk.False,
                LogicOp = LogicOp.Copy,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment
            };

            colorBlending.BlendConstants[0] = 0.0f;
            colorBlending.BlendConstants[1] = 0.0f;
            colorBlending.BlendConstants[2] = 0.0f;
            colorBlending.BlendConstants[3] = 0.0f;

            var push_constant = new PushConstantRange
            {
                Offset = 0,
                Size = (uint)sizeof(ModelVar),
                StageFlags = ShaderStageFlags.VertexBit
            };



            fixed(DescriptorSetLayout* pDescLayout = &_descriptorSetLayout)
            {
                var pipelineLayoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1, 
                    PSetLayouts = pDescLayout,
                    PushConstantRangeCount = 1, 
                    PPushConstantRanges = &push_constant
                };

                fixed (PipelineLayout* pipelineLayout = &_pipelineLayout)
                {
                    if (_vk.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, pipelineLayout) != Result.Success)
                    {
                        throw new Exception("failed to create pipeline layout!");
                    }
                }
            }

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = shaderStages,
                PVertexInputState = &vertexInputInfo,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisampling,
                PColorBlendState = &colorBlending,
                Layout = _pipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0,
                BasePipelineHandle = default
            };

            fixed (Pipeline* graphicsPipeline = &_graphicsPipeline)
            {
                if (_vk.CreateGraphicsPipelines
                        (_device, default, 1, &pipelineInfo, null, graphicsPipeline) != Result.Success)
                {
                    throw new Exception("failed to create graphics pipeline!");
                }
            }

            _vk.DestroyShaderModule(_device, fragShaderModule, null);
            _vk.DestroyShaderModule(_device, vertShaderModule, null);
        }

        private unsafe void CreateBuffers()
        {
            ulong bufferSize = (ulong)(sizeof(Vertex) * _vertices.Length);
            CreateBuffer(bufferSize, BufferUsageFlags.TransferSrcBit, 
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, ref _vertexBufferStaging, ref _vertexMemStaging);

            void* data;
            _vk.MapMemory(_device, _vertexMemStaging, 0, bufferSize, 0, &data);
            using var vertMem = _vertices.AsMemory().Pin();
            NativeHelper.MemoryCopy((IntPtr)data, (IntPtr)vertMem.Pointer, (uint)bufferSize);
            _vk.UnmapMemory(_device, _vertexMemStaging);

            CreateBuffer(bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.DeviceLocalBit, ref _vertexBuffer, ref _vertexMem);
            CopyBuffer(ref _vertexBufferStaging, ref _vertexBuffer, bufferSize);
        }

        private unsafe void CreateUniforms()
        {
            var bufferSize = sizeof(GlobalVar);
            uniformBuffers = new Buffer[MaxFramesInFlight];
            uniformBufferMemories = new DeviceMemory[MaxFramesInFlight];
            for (var i = 0; i < MaxFramesInFlight; ++i)
            {
                CreateBuffer((ulong)bufferSize, BufferUsageFlags.UniformBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                    ref uniformBuffers[i], ref uniformBufferMemories[i]);
            }
        }

        private unsafe void CreateDescriptorPool()
        {
            var poolSize = new DescriptorPoolSize()
            {
                Type = DescriptorType.UniformBuffer,
                DescriptorCount = MaxFramesInFlight
            };
            var poolInfo = new DescriptorPoolCreateInfo()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = 1,
                PPoolSizes = &poolSize,
                MaxSets = MaxFramesInFlight
            };
            fixed (DescriptorPool* pPool = &_descriptorPool)
            {
                if(_vk.CreateDescriptorPool(_device, &poolInfo, null, pPool) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool.");
                }
            }
        }

        private unsafe void CreateDescriptorSets()
        {
            var layouts = new DescriptorSetLayout[MaxFramesInFlight];
            for(var i = 0; i < MaxFramesInFlight; ++i)
            {
                layouts[i] = _descriptorSetLayout;
            }
            descriptorSets = new DescriptorSet[MaxFramesInFlight];
            fixed (DescriptorSetLayout* pLayouts = &layouts[0])
            {
                fixed(DescriptorSet* pSet = &descriptorSets[0])
                {
                    var info = new DescriptorSetAllocateInfo()
                    {
                        SType = StructureType.DescriptorSetAllocateInfo,
                        DescriptorPool = _descriptorPool,
                        DescriptorSetCount = MaxFramesInFlight,
                        PSetLayouts = pLayouts
                    };
                    if(_vk.AllocateDescriptorSets(_device, &info, pSet) != Result.Success)
                    {
                        throw new Exception("Failed to allocate descriptor sets.");
                    }
                }
            }
            for(var i = 0; i <MaxFramesInFlight; ++i)
            {
                var bufferInfo = new DescriptorBufferInfo { 
                    Buffer = uniformBuffers[i], 
                    Range = (ulong)sizeof(GlobalVar) 
                };
                var descriptorWrite = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 0,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.UniformBuffer,
                    DescriptorCount = 1,
                    PBufferInfo = &bufferInfo,
                };
                _vk.UpdateDescriptorSets(_device, 1, &descriptorWrite, 0, null);
            }
        }

        private unsafe void CreateBuffer(ulong bufferSize, BufferUsageFlags usage, MemoryPropertyFlags properties, ref Buffer buf, ref DeviceMemory bufMem)
        {
            BufferCreateInfo vertexBufferInfo;
            vertexBufferInfo.SType = StructureType.BufferCreateInfo;
            vertexBufferInfo.Size = bufferSize;
            vertexBufferInfo.Usage = usage;
            vertexBufferInfo.SharingMode = SharingMode.Exclusive;
            fixed (Buffer* pBuf = &buf)
            {
                if (_vk.CreateBuffer(_device, &vertexBufferInfo, null, pBuf) != Result.Success)
                {
                    throw new Exception("Failed to create vertex buffer.");
                }
            }

            MemoryRequirements memReq;
            _vk.GetBufferMemoryRequirements(_device, buf, &memReq);
            MemoryAllocateInfo allocInfo;
            allocInfo.SType = StructureType.MemoryAllocateInfo;
            allocInfo.AllocationSize = memReq.Size;
            allocInfo.MemoryTypeIndex = FindMemoryType(memReq.MemoryTypeBits, properties);
            fixed (DeviceMemory* pVertMem = &bufMem)
            {
                if (_vk.AllocateMemory(_device, &allocInfo, null, pVertMem) != Result.Success)
                {
                    throw new Exception("Failed to allocate mem for vertex buffer.");
                }
            }
            if (_vk.BindBufferMemory(_device, buf, bufMem, 0) != Result.Success)
            {
                throw new Exception("Failed to bind vertex buffer to memory.");
            }
        }

        private unsafe void CopyBuffer(ref Buffer srcBuffer, ref Buffer dstBuffer, ulong size)
        {
            var allocateInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = _commandPool,
                CommandBufferCount = 1
            };
            CommandBuffer commandBuffer;
            if(_vk.AllocateCommandBuffers(_device, in allocateInfo, &commandBuffer) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffer for copy.");
            }
            var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
            _vk.BeginCommandBuffer(commandBuffer, in beginInfo);
            var copyRegion = new BufferCopy { Size = size };
            _vk.CmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, copyRegion);
            _vk.EndCommandBuffer(commandBuffer);
            var submitInfo = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &commandBuffer };
            _vk.QueueSubmit(_graphicsQueue, 1, in submitInfo, new Fence());
            _vk.QueueWaitIdle(_graphicsQueue);
            _vk.FreeCommandBuffers(_device, _commandPool, 1, in commandBuffer);
        }

        private unsafe uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
        {
            PhysicalDeviceMemoryProperties memProps;
            _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, &memProps);
            for(int i = 0; i < memProps.MemoryTypeCount; ++i)
            {
                if((typeFilter & (1 << i)) != 0 && (memProps.MemoryTypes[i].PropertyFlags & properties) == properties)
                {
                    return (uint)i;
                }
            }
            throw new Exception("Failed to find memory type.");
        }

        private unsafe ShaderModule CreateShaderModule(byte[] code)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length
            };
            fixed (byte* codePtr = code)
            {
                createInfo.PCode = (uint*)codePtr;
            }

            var shaderModule = new ShaderModule();
            if (_vk.CreateShaderModule(_device, &createInfo, null, &shaderModule) != Result.Success)
            {
                throw new Exception("failed to create shader module!");
            }

            return shaderModule;
        }

        private unsafe void CreateFramebuffers()
        {
            _swapchainFramebuffers = new Framebuffer[_swapchainImageViews.Length];

            for (var i = 0; i < _swapchainImageViews.Length; i++)
            {
                var attachment = _swapchainImageViews[i];
                var framebufferInfo = new FramebufferCreateInfo
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = _renderPass,
                    AttachmentCount = 1,
                    PAttachments = &attachment,
                    Width = _swapchainExtent.Width,
                    Height = _swapchainExtent.Height,
                    Layers = 1
                };

                var framebuffer = new Framebuffer();
                if (_vk.CreateFramebuffer(_device, &framebufferInfo, null, &framebuffer) != Result.Success)
                {
                    throw new Exception("failed to create framebuffer!");
                }

                _swapchainFramebuffers[i] = framebuffer;
            }
        }

        private unsafe void CreateCommandPool()
        {
            var queueFamilyIndices = FindQueueFamilies(_physicalDevice);

            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = queueFamilyIndices.GraphicsFamily.Value, Flags = CommandPoolCreateFlags.ResetCommandBufferBit
            };

            fixed (CommandPool* commandPool = &_commandPool)
            {
                if (_vk.CreateCommandPool(_device, &poolInfo, null, commandPool) != Result.Success)
                {
                    throw new Exception("failed to create command pool!");
                }
            }
        }

        private unsafe void CreateCommandBuffers()
        {
            _commandBuffers = new CommandBuffer[_swapchainFramebuffers.Length];

            var allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = (uint)_commandBuffers.Length
            };

            fixed (CommandBuffer* commandBuffers = _commandBuffers)
            {
                if (_vk.AllocateCommandBuffers(_device, &allocInfo, commandBuffers) != Result.Success)
                {
                    throw new Exception("failed to allocate command buffers!");
                }
            }

            //for (var i = 0; i < _commandBuffers.Length; i++)
            //{
            //    var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };

            //    if (_vk.BeginCommandBuffer(_commandBuffers[i], &beginInfo) != Result.Success)
            //    {
            //        throw new Exception("failed to begin recording command buffer!");
            //    }


            //    var renderPassInfo = new RenderPassBeginInfo
            //    {
            //        SType = StructureType.RenderPassBeginInfo,
            //        RenderPass = _renderPass,
            //        Framebuffer = _swapchainFramebuffers[i],
            //        RenderArea = { Offset = new Offset2D { X = 0, Y = 0 }, Extent = _swapchainExtent }
            //    };

            //    var clearColor = new ClearValue
            //    { Color = new ClearColorValue { Float32_0 = 0, Float32_1 = 0, Float32_2 = 0, Float32_3 = 1 } };
            //    renderPassInfo.ClearValueCount = 1;
            //    renderPassInfo.PClearValues = &clearColor;

            //    _vk.CmdBeginRenderPass(_commandBuffers[i], &renderPassInfo, SubpassContents.Inline);

            //    _vk.CmdBindPipeline(_commandBuffers[i], PipelineBindPoint.Graphics, _graphicsPipeline);

            //    _vk.CmdBindVertexBuffers(_commandBuffers[i], 0, 1, _vertexBuffer, 0);

            //    _vk.CmdBindDescriptorSets(_commandBuffers[i], PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, descriptorSets[i], 0, null);

            //    _vk.CmdDraw(_commandBuffers[i], (uint)_vertices.Length, 1, 0, 0);

            //    _vk.CmdEndRenderPass(_commandBuffers[i]);

            //    if (_vk.EndCommandBuffer(_commandBuffers[i]) != Result.Success)
            //    {
            //        throw new Exception("failed to record command buffer!");
            //    }
            //}
        }
        uint idx = 0;
        private unsafe void RecordCommands(uint frameIdx)
        {
            _vk.ResetCommandBuffer(_commandBuffers[frameIdx], CommandBufferResetFlags.None);
            var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = 0 };

            if (_vk.BeginCommandBuffer(_commandBuffers[frameIdx], &beginInfo) != Result.Success)
            {
                throw new Exception("failed to begin recording command buffer!");
            }


            var renderPassInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _renderPass,
                Framebuffer = _swapchainFramebuffers[frameIdx],
                RenderArea = { Offset = new Offset2D { X = 0, Y = 0 }, Extent = _swapchainExtent }
            };
            var modelVar = new ModelVar { world = Matrix4x4.CreateRotationY((++idx) / 180f * (float)Math.PI) };
            var clearColor = new ClearValue
            { Color = new ClearColorValue { Float32_0 = 0, Float32_1 = 0, Float32_2 = 0, Float32_3 = 1 } };
            renderPassInfo.ClearValueCount = 1;
            renderPassInfo.PClearValues = &clearColor;

            _vk.CmdBeginRenderPass(_commandBuffers[frameIdx], &renderPassInfo, SubpassContents.Inline);

            _vk.CmdBindPipeline(_commandBuffers[frameIdx], PipelineBindPoint.Graphics, _graphicsPipeline);

            _vk.CmdBindVertexBuffers(_commandBuffers[frameIdx], 0, 1, _vertexBuffer, 0);

            _vk.CmdBindDescriptorSets(_commandBuffers[frameIdx], PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, descriptorSets[frameIdx], 0, null);

            _vk.CmdPushConstants(_commandBuffers[frameIdx], _pipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)sizeof(ModelVar), &modelVar);

            _vk.CmdDraw(_commandBuffers[frameIdx], (uint)_vertices.Length, 1, 0, 0);

            _vk.CmdEndRenderPass(_commandBuffers[frameIdx]);

            if (_vk.EndCommandBuffer(_commandBuffers[frameIdx]) != Result.Success)
            {
                throw new Exception("failed to record command buffer!");
            }
        }

        private unsafe void CreateSyncObjects()
        {
            _imageAvailableSemaphores = new Semaphore[MaxFramesInFlight];
            _renderFinishedSemaphores = new Semaphore[MaxFramesInFlight];
            _inFlightFences = new Fence[MaxFramesInFlight];
            _imagesInFlight = new Fence[MaxFramesInFlight];

            SemaphoreCreateInfo semaphoreInfo = new SemaphoreCreateInfo();
            semaphoreInfo.SType = StructureType.SemaphoreCreateInfo;

            FenceCreateInfo fenceInfo = new FenceCreateInfo();
            fenceInfo.SType = StructureType.FenceCreateInfo;
            fenceInfo.Flags = FenceCreateFlags.SignaledBit;

            for (var i = 0; i < MaxFramesInFlight; i++)
            {
                Semaphore imgAvSema, renderFinSema;
                Fence inFlightFence;
                if (_vk.CreateSemaphore(_device, &semaphoreInfo, null, &imgAvSema) != Result.Success ||
                    _vk.CreateSemaphore(_device, &semaphoreInfo, null, &renderFinSema) != Result.Success ||
                    _vk.CreateFence(_device, &fenceInfo, null, &inFlightFence) != Result.Success)
                {
                    throw new Exception("failed to create synchronization objects for a frame!");
                }

                _imageAvailableSemaphores[i] = imgAvSema;
                _renderFinishedSemaphores[i] = renderFinSema;
                _inFlightFences[i] = inFlightFence;
            }
        }
    }
}