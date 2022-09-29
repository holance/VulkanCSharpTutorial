using System;

namespace VulkanCSharpTutorial
{
    internal class Program
    {
        static readonly VkGlfwExtension ext_ = new();
        static IntPtr windowPtr_ = IntPtr.Zero;
        static HelloTriangleApplication app = new ();
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            app.Run();
            //VulkanInstance.Init(ext_, true);
            //windowPtr_ = ext_.CreateWindow(1024, 768, "Vulkan Tests");
            //ext_.MakeWindowCurrent(windowPtr_);
            //while (!ext_.IsWindowClosing(windowPtr_))
            //{
            //    ext_.WindowTick(windowPtr_);
            //    ext_.SwapBuffers(windowPtr_);
            //}
        }
    }
}
