using System;
using System.IO;
using System.Security.Principal;

namespace OtimizaPC.Core
{
    /// <summary>Informações gerais do aplicativo.</summary>
    public static class AppInfo
    {
        public const string Name = "NutriOtimiza";

        public static string Version => typeof(AppInfo).Assembly.GetName().Version.ToString(3);

        /// <summary>Pasta de dados do app (%LocalAppData%\NutriOtimiza).</summary>
        public static string DataDirectory
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static bool IsAdministrator
        {
            get
            {
                try
                {
                    using (var identity = WindowsIdentity.GetCurrent())
                        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
