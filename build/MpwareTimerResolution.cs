using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;

internal static class MpwareTimerResolution
{
    private const string ServiceNameValue = "mpwareTimerResolution";
    private const string ServiceDisplayName = "mpware TimerResolution";
    private const string LegacyTaskName = "mpware SetTimerResolution";
    private const string LegacyAltTaskName = "\\mpware timer resolution";
    private const string LegacyRunValueName = "mpware SetTimerResolution";

    private static int Main(string[] args)
    {
        if (HasArg(args, "--service"))
        {
            ServiceBase.Run(new TimerResolutionService());
            return 0;
        }

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

        string currentHelper = Assembly.GetExecutingAssembly().Location;
        string helperTarget = Path.Combine(installDir, "SetTimerResolution.exe");
        string timerTarget = Path.Combine(installDir, "TimerResolution.exe");

        StopServiceIfPresent();
        StopExistingTarget(timerTarget);
        StopExistingTarget(helperTarget);

        CopyWithRetry(currentHelper, helperTarget);
        CopyWithRetry(bundledTimer, timerTarget);

        string serviceCommand = "\"" + helperTarget + "\" --service";
        if (ServiceExists())
        {
            RunSc("config \"" + ServiceNameValue + "\" binPath= \"" + serviceCommand + "\" start= auto DisplayName= \"" + ServiceDisplayName + "\"");
        }
        else
        {
            RunSc("create \"" + ServiceNameValue + "\" binPath= \"" + serviceCommand + "\" start= auto DisplayName= \"" + ServiceDisplayName + "\"");
        }

        TryRunSc("description \"" + ServiceNameValue + "\" \"Launches bundled TimerResolution.exe for mpware timer-resolution settings.\"");
        TryRunSc("failure \"" + ServiceNameValue + "\" reset= 60 actions= restart/5000/restart/5000/\"\"/5000");
        CleanupLegacyStartup();

        RunSc("query \"" + ServiceNameValue + "\"");
        RunSc("start \"" + ServiceNameValue + "\"", IsServiceAlreadyRunning);
        WaitForServiceStatus(ServiceControllerStatus.Running, 15);
        RunSc("query \"" + ServiceNameValue + "\"");
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

    private static int SelfTest()
    {
        string bundledTimer = ResolveBundledTimer();
        if (String.IsNullOrWhiteSpace(bundledTimer) || !File.Exists(bundledTimer))
        {
            return 2;
        }

        string serviceCommand = "\"" + Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "mpware",
            "SetTimerResolution.exe") + "\" --service";
        if (serviceCommand.IndexOf("--service", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return 3;
        }

        return 0;
    }

    private static string ResolveInstalledTimer()
    {
        string currentDir = ResolveCurrentDirectory();
        string sidecar = Path.Combine(currentDir, "TimerResolution.exe");
        if (File.Exists(sidecar))
        {
            return sidecar;
        }

        return ResolveBundledTimer();
    }

    private static string ResolveBundledTimer()
    {
        string currentDir = ResolveCurrentDirectory();

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

    private static string ResolveCurrentDirectory()
    {
        string current = Assembly.GetExecutingAssembly().Location;
        string currentDir = Path.GetDirectoryName(current);
        if (String.IsNullOrWhiteSpace(currentDir))
        {
            currentDir = AppDomain.CurrentDomain.BaseDirectory;
        }
        return currentDir;
    }

    private static bool ServiceExists()
    {
        return TryRunSc("query \"" + ServiceNameValue + "\"");
    }

    private static void StopServiceIfPresent()
    {
        TryRunSc("stop \"" + ServiceNameValue + "\"");
        for (int i = 0; i < 20; i++)
        {
            if (!ServiceExists())
            {
                return;
            }

            ServiceControllerStatus status;
            if (!TryGetServiceStatus(out status))
            {
                return;
            }
            if (status == ServiceControllerStatus.Stopped)
            {
                return;
            }
            Thread.Sleep(250);
        }
    }

    private static void WaitForServiceStatus(ServiceControllerStatus desiredStatus, int seconds)
    {
        using (ServiceController service = new ServiceController(ServiceNameValue))
        {
            service.WaitForStatus(desiredStatus, TimeSpan.FromSeconds(seconds));
            service.Refresh();
            if (service.Status != desiredStatus)
            {
                throw new InvalidOperationException("Service " + ServiceDisplayName + " did not reach " + desiredStatus + ".");
            }
        }
    }

    private static bool TryGetServiceStatus(out ServiceControllerStatus status)
    {
        status = ServiceControllerStatus.Stopped;
        try
        {
            using (ServiceController service = new ServiceController(ServiceNameValue))
            {
                status = service.Status;
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void CleanupLegacyStartup()
    {
        TryRunSchtasks("/Delete /TN \"" + LegacyTaskName + "\" /F");
        TryRunSchtasks("/Delete /TN \"" + LegacyAltTaskName + "\" /F");
        TryRunProcess("reg.exe", "delete \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"" + LegacyRunValueName + "\" /f");
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
        if (String.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Exception last = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                File.Copy(source, target, true);
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException ex)
            {
                last = ex;
                Thread.Sleep(300);
            }
        }
        throw new IOException("Could not update " + target, last);
    }

    private static void RunSc(string arguments)
    {
        RunSc(arguments, null);
    }

    private static void RunSc(string arguments, Func<int, string, bool> ignore)
    {
        RunProcess("sc.exe", arguments, ignore);
    }

    private static bool TryRunSc(string arguments)
    {
        return TryRunProcess("sc.exe", arguments);
    }

    private static void TryRunSchtasks(string arguments)
    {
        TryRunProcess("schtasks.exe", arguments);
    }

    private static bool TryRunProcess(string fileName, string arguments)
    {
        try
        {
            RunProcess(fileName, arguments, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RunProcess(string fileName, string arguments, Func<int, string, bool> ignore)
    {
        ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using (Process process = Process.Start(psi))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            string detail = (output + " " + error).Trim();
            if (process.ExitCode != 0 && (ignore == null || !ignore(process.ExitCode, detail)))
            {
                if (detail.Length == 0)
                {
                    detail = fileName + " failed with exit code " + process.ExitCode;
                }
                throw new InvalidOperationException(detail);
            }
        }
    }

    private static bool IsServiceAlreadyRunning(int exitCode, string detail)
    {
        return detail != null &&
            detail.IndexOf("already been started", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Process StartHidden(string fileName, string arguments)
    {
        ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
        psi.CreateNoWindow = true;
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        psi.UseShellExecute = false;
        return Process.Start(psi);
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

    private sealed class TimerResolutionService : ServiceBase
    {
        private Process _timerProcess;

        public TimerResolutionService()
        {
            ServiceName = ServiceNameValue;
            CanStop = true;
            CanShutdown = true;
        }

        protected override void OnStart(string[] args)
        {
            string timer = ResolveInstalledTimer();
            if (String.IsNullOrWhiteSpace(timer) || !File.Exists(timer))
            {
                throw new FileNotFoundException("TimerResolution.exe was not found.");
            }
            _timerProcess = StartHidden(timer, "");
        }

        protected override void OnStop()
        {
            StopPayload();
        }

        protected override void OnShutdown()
        {
            StopPayload();
            base.OnShutdown();
        }

        private void StopPayload()
        {
            try
            {
                if (_timerProcess != null && !_timerProcess.HasExited)
                {
                    _timerProcess.Kill();
                    _timerProcess.WaitForExit(3000);
                }
            }
            catch
            {
            }
            finally
            {
                if (_timerProcess != null)
                {
                    _timerProcess.Dispose();
                    _timerProcess = null;
                }
            }

            string installed = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "mpware",
                "TimerResolution.exe");
            StopExistingTarget(installed);
        }
    }
}
