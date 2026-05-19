using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using IOPath = System.IO.Path;

namespace mpwareLauncher
{
    internal static class Program
    {
        private const string ResourceName = "mpwareRuntimeZip";

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args != null && args.Length > 0 && String.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    return SelfTest();
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                return app.Run(new TerminalDashboardWindow());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "mpware", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        internal static string ResolveScriptPath()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string sidecar = IOPath.Combine(exeDir, "_FOLDERMUSTBEONCDRIVE", "mpware.ps1");

            if (File.Exists(sidecar))
            {
                return sidecar;
            }

            return ExtractEmbeddedRuntime();
        }

        private static int SelfTest()
        {
            string script = ResolveScriptPath();
            if (String.IsNullOrWhiteSpace(script) || !File.Exists(script))
            {
                return 2;
            }

            string root = IOPath.GetDirectoryName(script);
            if (!File.Exists(IOPath.Combine(root, "zFunctions.psm1")))
            {
                return 3;
            }

            if (!File.Exists(IOPath.Combine(root, "winfetch.psm1")))
            {
                return 4;
            }

            return 0;
        }

        private static string ExtractEmbeddedRuntime()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream resource = assembly.GetManifestResourceStream(ResourceName))
            {
                if (resource == null)
                {
                    return null;
                }

                byte[] zipBytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    resource.CopyTo(ms);
                    zipBytes = ms.ToArray();
                }

                string hash = Sha256(zipBytes).Substring(0, 16);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (String.IsNullOrWhiteSpace(localAppData))
                {
                    localAppData = IOPath.GetTempPath();
                }

                string cacheRoot = IOPath.Combine(localAppData, "mpware", "runtime");
                string target = IOPath.Combine(cacheRoot, hash);
                string script = IOPath.Combine(target, "mpware.ps1");

                if (File.Exists(script))
                {
                    return script;
                }

                Directory.CreateDirectory(cacheRoot);
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }
                Directory.CreateDirectory(target);

                string zipPath = IOPath.Combine(cacheRoot, hash + ".zip");
                File.WriteAllBytes(zipPath, zipBytes);
                ZipFile.ExtractToDirectory(zipPath, target);
                File.Delete(zipPath);

                return File.Exists(script) ? script : null;
            }
        }

        private static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
