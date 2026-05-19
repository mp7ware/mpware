using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

internal static class MpwareTimerResolution
{
    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    private static int Main(string[] args)
    {
        uint resolution = ParseResolution(args);

        if (HasArg(args, "--install"))
        {
            return Install(resolution);
        }

        if (HasArg(args, "--hold"))
        {
            return Hold(resolution);
        }

        return Hold(resolution);
    }

    private static int Install(uint resolution)
    {
        string installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "mpware");
        Directory.CreateDirectory(installDir);

        string target = Path.Combine(installDir, "mpware-timer-resolution.exe");
        string current = Assembly.GetExecutingAssembly().Location;
        if (!String.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(current, target, true);
        }

        string taskArgs = String.Format("\"{0}\" --hold --resolution {1}", target, resolution);
        RunSchtasks("/Create /TN \"mpware timer resolution\" /SC ONLOGON /RL HIGHEST /TR \"" + taskArgs.Replace("\"", "\\\"") + "\" /F");
        StartHidden(target, "--hold --resolution " + resolution);
        return 0;
    }

    private static int Hold(uint resolution)
    {
        bool created;
        using (Mutex mutex = new Mutex(true, "Global\\mpware-timer-resolution", out created))
        {
            if (!created)
            {
                return 0;
            }

            uint current;
            int status = NtSetTimerResolution(resolution, true, out current);
            if (status != 0)
            {
                return status;
            }

            Thread.Sleep(Timeout.Infinite);
            return 0;
        }
    }

    private static void RunSchtasks(string arguments)
    {
        ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", arguments);
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        using (Process process = Process.Start(psi))
        {
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("schtasks.exe failed with exit code " + process.ExitCode);
            }
        }
    }

    private static void StartHidden(string fileName, string arguments)
    {
        ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
        psi.CreateNoWindow = true;
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        psi.UseShellExecute = false;
        Process.Start(psi);
    }

    private static bool HasArg(string[] args, string name)
    {
        foreach (string arg in args)
        {
            if (String.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static uint ParseResolution(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (String.Equals(args[i], "--resolution", StringComparison.OrdinalIgnoreCase))
            {
                uint value;
                if (UInt32.TryParse(args[i + 1], out value) && value > 0)
                {
                    return value;
                }
            }
        }
        return 5000;
    }
}
