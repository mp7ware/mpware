using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

internal static class MpwareTimerResolution
{
    private const string TaskName = "mpware SetTimerResolution";
    private const string LegacyTaskName = "\\mpware timer resolution";
    private const string RunValueName = "mpware SetTimerResolution";

    private static int Main(string[] args)
    {
        if (HasArg(args, "--install"))
        {
            return Install();
        }

        if (HasArg(args, "--self-test"))
        {
            return SelfTest();
        }

        return StartBundledTimer();
    }

    private static int Install()
    {
        string installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "mpware");
        Directory.CreateDirectory(installDir);

        string bundledTimer = ResolveBundledTimer();
        if (String.IsNullOrWhiteSpace(bundledTimer) || !File.Exists(bundledTimer))
        {
            throw new FileNotFoundException("Bundled TimerResolution.exe was not found.");
        }

        string target = Path.Combine(installDir, "TimerResolution.exe");
        StopExistingTarget(Path.Combine(installDir, "SetTimerResolution.exe"));
        StopExistingTarget(target);
        CopyWithRetry(bundledTimer, target);

        string taskCommand = target;
        RunSchtasks("/Create /TN \"" + TaskName + "\" /SC ONSTART /RU SYSTEM /RL HIGHEST /TR \"" + taskCommand + "\" /F");
        TryRunSchtasks("/Delete /TN \"" + LegacyTaskName + "\" /F");
        RegisterRunFallback(target);
        RunSchtasks("/Query /TN \"" + TaskName + "\"");
        TryRunSchtasks("/Run /TN \"" + TaskName + "\"");
        return 0;
    }

    private static int StartBundledTimer()
    {
        string bundledTimer = ResolveBundledTimer();
        if (String.IsNullOrWhiteSpace(bundledTimer) || !File.Exists(bundledTimer))
        {
            return 2;
        }
        StartHidden(bundledTimer, "");
        return 0;
    }

    private static void StopExistingTarget(string target)
    {
        string processName = Path.GetFileNameWithoutExtension(target);
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                string path = process.MainModule.FileName;
                if (String.Equals(path, target, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void CopyWithRetry(string source, string target)
    {
        Exception last = null;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                File.Copy(source, target, true);
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException ex)
            {
                last = ex;
                Thread.Sleep(250);
            }
        }
        throw new IOException("Could not update " + target, last);
    }

    private static int SelfTest()
    {
        string bundledTimer = ResolveBundledTimer();
        if (String.IsNullOrWhiteSpace(bundledTimer) || !File.Exists(bundledTimer))
        {
            return 2;
        }
        string command = @"C:\ProgramData\mpware\TimerResolution.exe";
        if (command.IndexOf("TimerResolution.exe", StringComparison.Ordinal) < 0)
        {
            return 3;
        }
        return 0;
    }

    private static string ResolveBundledTimer()
    {
        string current = Assembly.GetExecutingAssembly().Location;
        string currentDir = Path.GetDirectoryName(current);
        if (String.IsNullOrWhiteSpace(currentDir))
        {
            currentDir = AppDomain.CurrentDomain.BaseDirectory;
        }

        string nested = Path.Combine(currentDir, "TimerResolution", "TimerResolution.exe");
        if (File.Exists(nested))
        {
            return nested;
        }

        string sidecar = Path.Combine(currentDir, "TimerResolution.exe");
        if (File.Exists(sidecar))
        {
            return sidecar;
        }

        return null;
    }

    private static void RegisterRunFallback(string target)
    {
        string command = "\"" + target + "\"";
        using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (RegistryKey key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
        {
            if (key == null)
            {
                throw new InvalidOperationException("HKLM Run key was not available");
            }
            key.SetValue(RunValueName, command, RegistryValueKind.String);
        }
    }

    private static void RunSchtasks(string arguments)
    {
        ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", arguments);
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using (Process process = Process.Start(psi))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                string detail = (output + " " + error).Trim();
                if (detail.Length == 0)
                {
                    detail = "schtasks.exe failed with exit code " + process.ExitCode;
                }
                throw new InvalidOperationException(detail);
            }
        }
    }

    private static void TryRunSchtasks(string arguments)
    {
        try
        {
            RunSchtasks(arguments);
        }
        catch
        {
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

}
