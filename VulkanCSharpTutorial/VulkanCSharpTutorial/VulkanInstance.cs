using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace VulkanCSharpTutorial
{
    public static unsafe class VulkanInstance
    {
        static readonly ILogger logger_ = LogManager.Create(nameof(VulkanInstance));
        const string applicationName_ = "HelixToolkit";
        const string engineName_ = "HelixToolkit.Graphics.Vulkan";
        static readonly string[][] validationLayerNamesPriorityList_ =
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
        private static bool debuggable_;
        private static Instance vkInstance_;
        private static KhrSurface? vkSurface_;
        private static DebugUtilsMessengerEXT debugUtilsMessenger_;
        private static ExtDebugUtils? debugUtils_;
        private static readonly object lock_ = new object();
        /// <summary>
        /// Initialize the vulkan common instance.
        /// <para>
        /// Call <see cref="Deinit"/> to free native resources in this common instance.
        /// </para>
        /// </summary>
        /// <param name="extension"></param>
        /// <param name="enableDebug"></param>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static void Init(VkGlfwExtension? extension, bool enableDebug = false)
        {
            lock (lock_)
            {
                if (Initialized)
                {
                    return;
                }
                var appInfo = new ApplicationInfo
                {
                    SType = StructureType.ApplicationInfo,
                    PApplicationName = (byte*)applicationName_.NewUnmanagedString(),
                    PEngineName = (byte*)engineName_.NewUnmanagedString(),
                    ApplicationVersion = Vk.MakeVersion(1, 0, 0),
                    EngineVersion = Vk.MakeVersion(1, 0, 0),
                    ApiVersion = Vk.Version12
                };

                var instanceInfo = new InstanceCreateInfo
                {
                    SType = StructureType.InstanceCreateInfo,
                    PApplicationInfo = &appInfo
                };
                var pExtNames = extension == null ? new byte*[1] : new byte*[extension.Names.Length + 1];
                var externalExtCount = 0;
                byte** layerName = null;
                Result vkResult;
                try
                {
                    if (extension != null)
                    {
                        foreach (var ext in extension.Names)
                        {
                            if (Api.IsExtensionPresent(ext))
                            {
                                logger_.LogInformation("Vulkan Ext: {}", ext);
                                pExtNames[externalExtCount++] = (byte*)ext.NewUnmanagedString();
                            }
                        }
                    }
                    pExtNames[externalExtCount++] = (byte*)ExtDebugUtils.ExtensionName.NewUnmanagedString();
                    uint extCount = 0;
                    {
                        byte* b = null;
                        ExtensionProperties* property = null;
                        vkResult = Api.EnumerateInstanceExtensionProperties(b, ref extCount, property);
                        if (vkResult != Result.Success)
                        {
                            throw new InvalidOperationException($"Failed on EnumerateInstanceExtensionProperties. Err: {vkResult}");
                        }
                    }
                    logger_.LogInformation("Vulkan extension count: {}", extCount);
                    if (enableDebug)
                    {
                        var extProperties = new ExtensionProperties[extCount];
                        fixed (ExtensionProperties* property = &extProperties[0])
                        {
                            byte* b = null;
                            vkResult = Api.EnumerateInstanceExtensionProperties(b, ref extCount, property);
                            if (vkResult != Result.Success)
                            {
                                throw new InvalidOperationException($"Failed on EnumerateInstanceExtensionProperties. Err: {vkResult}");
                            }
                        }
                        foreach (var property in extProperties)
                        {
                            var extName = NativeHelper.ToString(property.ExtensionName);
                            logger_.LogInformation("Vulkan extension name: {}", extName);
                        }
                    }

                    if (enableDebug)
                    {
                        var validationLayers = GetOptimalValidationLayers();
                        if (validationLayers is not null)
                        {
                            debuggable_ = true;
                            instanceInfo.EnabledLayerCount = (uint)validationLayers.Length;
                            layerName = (byte**)SilkMarshal.StringArrayToPtr(validationLayers);
                            instanceInfo.PpEnabledLayerNames = layerName;
                        }
                    }
                    fixed (byte** ppNames = &pExtNames[0])
                    {
                        instanceInfo.EnabledExtensionCount = (uint)externalExtCount;
                        instanceInfo.PpEnabledExtensionNames = ppNames;
                        vkResult = Api.CreateInstance(instanceInfo, null, out vkInstance_);
                        if (vkResult != Result.Success)
                        {
                            throw new InvalidOperationException($"Failed to create VK instance. Err: {vkResult}");
                        }
                    }
                    SetupDebugMessenger();
                    if (!Api.TryGetInstanceExtension(vkInstance_, out vkSurface_))
                    {
                        throw new NotSupportedException("KHR_surface extension not found.");
                    }
                    Initialized = true;
                }
                catch (InvalidOperationException ex)
                {
                    throw new InvalidOperationException(ex.Message);
                }
                finally
                {
                    if (layerName != null)
                    {
                        SilkMarshal.Free((nint)layerName);
                    }
                    for (var i = 0; i < pExtNames.Length; ++i)
                    {
                        NativeHelper.FreeUnmanagedString(pExtNames[i]);
                    }
                    NativeHelper.FreeUnmanagedString(appInfo.PApplicationName);
                    NativeHelper.FreeUnmanagedString(appInfo.PEngineName);
                }
            }

        }
        private static string[]? GetOptimalValidationLayers()
        {
            var layerCount = 0u;
            Api.EnumerateInstanceLayerProperties(&layerCount, (LayerProperties*)0);

            var availableLayers = new LayerProperties[layerCount];
            fixed (LayerProperties* availableLayersPtr = availableLayers)
            {
                Api.EnumerateInstanceLayerProperties(&layerCount, availableLayersPtr);
            }

            var availableLayerNames = availableLayers.Select(availableLayer => Marshal.PtrToStringAnsi((nint)availableLayer.LayerName)).ToArray();
            foreach (var validationLayerNameSet in validationLayerNamesPriorityList_)
            {
                if (validationLayerNameSet.All(validationLayerName => availableLayerNames.Contains(validationLayerName)))
                {
                    return validationLayerNameSet;
                }
            }

            return null;
        }
        public static Vk Api
        {
            get;
        } = Vk.GetApi();

        public static bool Debuggable => debuggable_;

        public static Instance VkInstance => vkInstance_;

        public static KhrSurface? VkSurface => vkSurface_;

        public static bool Initialized
        {
            private set;
            get;
        } = false;

        private static void SetupDebugMessenger()
        {
            if (!debuggable_)
            {
                return;
            }
            if (!Api.TryGetInstanceExtension(VkInstance, out debugUtils_))
            {
                return;
            }
            if (debugUtils_ is null)
            {
                return;
            }
            var createInfo = new DebugUtilsMessengerCreateInfoEXT();
            PopulateDebugMessengerCreateInfo(ref createInfo);

            fixed (DebugUtilsMessengerEXT* debugMessenger = &debugUtilsMessenger_)
            {
                if (debugUtils_.CreateDebugUtilsMessenger
                        (VkInstance, &createInfo, null, debugMessenger) != Result.Success)
                {
                    throw new Exception("Failed to create debug messenger.");
                }
            }
        }

        private static void PopulateDebugMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT createInfo)
        {
            createInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
            createInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.InfoBitExt |
                                         DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                         DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
            createInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
            createInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
        }

        private static uint DebugCallback
        (
            DebugUtilsMessageSeverityFlagsEXT messageSeverity,
            DebugUtilsMessageTypeFlagsEXT messageTypes,
            DebugUtilsMessengerCallbackDataEXT* pCallbackData,
            void* pUserData
        )
        {
            var level = LogLevel.None;
            switch (messageSeverity)
            {
                case DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt:
                    level = LogLevel.Trace;
                    break;
                case DebugUtilsMessageSeverityFlagsEXT.InfoBitExt:
                    level = LogLevel.Information;
                    break;
                case DebugUtilsMessageSeverityFlagsEXT.WarningBitExt:
                    level = LogLevel.Warning;
                    break;
                case DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt:
                    level = LogLevel.Error;
                    break;
            }
            logger_.Log(level, "{}: {}", messageTypes, Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));

            return Vk.False;
        }

        #region Dispose
        public static void Deinit()
        {
            lock (lock_)
            {
                if (!Initialized)
                {
                    return;
                }
                debugUtils_?.DestroyDebugUtilsMessenger(VkInstance, debugUtilsMessenger_, null);
                vkSurface_?.Dispose();
                Api.DestroyInstance(vkInstance_, null);
                Initialized = false;
            }
        }
        #endregion
    }
}
