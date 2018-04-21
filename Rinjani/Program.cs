using System.Reflection;
using Topshelf;
using System.Runtime.InteropServices;
using System.IO;
using Newtonsoft.Json;

namespace Rinjani
{
    public class Program
    {
        public delegate bool ControlCtrlDelegate(int CtrlType);
        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleCtrlHandler(ControlCtrlDelegate HandlerRoutine, bool Add);
        private static ControlCtrlDelegate cancelHandler = new ControlCtrlDelegate(HandlerRoutine);

        public static bool HandlerRoutine(int CtrlType)
        {
            var s = File.ReadAllText("config.json");
            ConfigRoot Config = JsonConvert.DeserializeObject<ConfigRoot>(s);
            EmailHelper.SendMailUse(Config.EmailAddress, "Rinjnai程序退出", "Rinjnai程序退出");
            return false;
        }

        private static void Main(string[] args)
        {
            SetConsoleCtrlHandler(cancelHandler, true);
            var serviceName = Assembly.GetExecutingAssembly().GetName().Name;
            HostFactory.Run(
                hostConfig =>
                {
                    hostConfig.Service<AppRoot>(
                        serviceConfig =>
                        {
                            serviceConfig.ConstructUsing(name => new AppRoot());
                            serviceConfig.WhenStarted(service => service.Start());
                            serviceConfig.WhenStopped(service => service.Stop());
                        });
                    hostConfig.RunAsLocalSystem();
                    hostConfig.SetDescription(serviceName);
                    hostConfig.SetDisplayName(serviceName);
                    hostConfig.SetServiceName(serviceName);
                });
        }
    }
}