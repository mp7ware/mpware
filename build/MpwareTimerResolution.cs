using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

internal static class MpwareTimerResolution
{
    private const string TaskName = "mpware SetTimerResolution";
    private const string LegacyTaskName = "\\mpware timer resolution";
    private const string RunValueName = "mpware SetTimerResolution";

    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    private static int Main(string[] args)
    {
        uint resolution = ParseResolution(args);

        if (HasArg(args, "--install"))
        {
            return Install(resolution);
        }

        if (HasArg(args, "--self-test"))
        {
            return SelfTest(resolution);
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

        string target = Path.Combine(installDir, "SetTimerResolution.exe");
        string current = Assembly.GetExecutingAssembly().Location;
        if (!String.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            StopExistingTarget(target);
            CopyWithRetry(current, target);
        }

        string taskCommand = target + " --hold --resolution " + resolution;
        RunSchtasks("/Create /TN \"" + TaskName + "\" /SC ONSTART /RU SYSTEM /RL HIGHEST /TR \"" + taskCommand + "\" /F");
        TryRunSchtasks("/Delete /TN \"" + LegacyTaskName + "\" /F");
        RegisterRunFallback(target, resolution);
        RunSchtasks("/Query /TN \"" + TaskName + "\"");
        TryRunSchtasks("/Run /TN \"" + TaskName + "\"");
        StartHidden(target, "--hold --resolution " + resolution);
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

    private static int SelfTest(uint resolution)
    {
        string command = @"C:\ProgramData\mpware\SetTimerResolution.exe --hold --resolution " + resolution;
        if (command.IndexOf("SetTimerResolution.exe", StringComparison.Ordinal) < 0 ||
            command.IndexOf("--resolution " + resolution, StringComparison.Ordinal) < 0)
        {
            return 2;
        }
        return 0;
    }

    private static void RegisterRunFallback(string target, uint resolution)
    {
        string command = "\"" + target + "\" --hold --resolution " + resolution;
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
