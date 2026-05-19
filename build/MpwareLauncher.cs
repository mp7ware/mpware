using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace mpwareLauncher
{
    internal static class Program
    {
        private const string ResourceName = "mpwareRuntimeZip";

        [STAThread]
        private static int Main()
        {
            try
            {
                string scriptPath = ResolveScriptPath();

                if (String.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
                {
                    MessageBox.Show(
                        "mpware runtime was not found. Rebuild mpware.exe or extract the full release zip beside it.",
                        "mpware",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return 1;
                }

                UnblockRuntime(Path.GetDirectoryName(scriptPath));
                return LaunchScript(scriptPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "mpware", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static string ResolveScriptPath()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string sidecar = Path.Combine(exeDir, "_FOLDERMUSTBEONCDRIVE", "mpware.ps1");

            if (File.Exists(sidecar))
            {
                return sidecar;
            }

            return ExtractEmbeddedRuntime();
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
                    localAppData = Path.GetTempPath();
                }

                string cacheRoot = Path.Combine(localAppData, "mpware", "runtime");
                string target = Path.Combine(cacheRoot, hash);
                string script = Path.Combine(target, "mpware.ps1");

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

                string zipPath = Path.Combine(cacheRoot, hash + ".zip");
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

        private static int LaunchScript(string scriptPath)
        {
            string workingDirectory = Path.GetDirectoryName(scriptPath);
            string psArgs = String.Format(
                "-NoProfile -ExecutionPolicy Bypass -File \"{0}\"",
                scriptPath
            );

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = psArgs,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            using (Process ps = Process.Start(psi))
            {
                ps.WaitForExit();
                return ps.ExitCode;
            }
        }

        private static void UnblockRuntime(string runtimeRoot)
        {
            if (String.IsNullOrWhiteSpace(runtimeRoot) || !Directory.Exists(runtimeRoot))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(runtimeRoot, "*", SearchOption.AllDirectories))
            {
                try
                {
                    string adsPath = file + ":Zone.Identifier";
                    if (File.Exists(adsPath))
                    {
                        File.Delete(adsPath);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
