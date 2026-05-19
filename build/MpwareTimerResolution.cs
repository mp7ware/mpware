using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;

internal static class MpwareTimerResolution
{
    private const string TaskName = "mpware SetTimerResolution";
    private const string LegacyTaskName = "mpware timer resolution";

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
            File.Copy(current, target, true);
        }

        string xmlPath = Path.Combine(Path.GetTempPath(), "mpware-timer-resolution-task.xml");
        File.WriteAllText(xmlPath, BuildTaskXml(target, "--hold --resolution " + resolution), Encoding.Unicode);
        RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\" /F");
        TryRunSchtasks("/Run /TN \"" + TaskName + "\"");
        TryRunSchtasks("/Delete /TN \"" + LegacyTaskName + "\" /F");
        StartHidden(target, "--hold --resolution " + resolution);
        return 0;
    }

    private static int SelfTest(uint resolution)
    {
        string xml = BuildTaskXml(@"C:\ProgramData\mpware\SetTimerResolution.exe", "--hold --resolution " + resolution);
        if (xml.IndexOf("<BootTrigger>", StringComparison.Ordinal) < 0 ||
            xml.IndexOf("<UserId>S-1-5-18</UserId>", StringComparison.Ordinal) < 0 ||
            xml.IndexOf("SetTimerResolution.exe", StringComparison.Ordinal) < 0)
        {
            return 2;
        }
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

    private static string BuildTaskXml(string command, string arguments)
    {
        StringBuilder xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
        xml.AppendLine("<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
        xml.AppendLine("  <RegistrationInfo>");
        xml.AppendLine("    <Description>mpware 0.5ms timer resolution helper</Description>");
        xml.AppendLine("  </RegistrationInfo>");
        xml.AppendLine("  <Triggers>");
        xml.AppendLine("    <BootTrigger>");
        xml.AppendLine("      <Enabled>true</Enabled>");
        xml.AppendLine("    </BootTrigger>");
        xml.AppendLine("  </Triggers>");
        xml.AppendLine("  <Principals>");
        xml.AppendLine("    <Principal id=\"Author\">");
        xml.AppendLine("      <UserId>S-1-5-18</UserId>");
        xml.AppendLine("      <RunLevel>HighestAvailable</RunLevel>");
        xml.AppendLine("    </Principal>");
        xml.AppendLine("  </Principals>");
        xml.AppendLine("  <Settings>");
        xml.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
        xml.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
        xml.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
        xml.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
        xml.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
        xml.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
        xml.AppendLine("    <IdleSettings>");
        xml.AppendLine("      <StopOnIdleEnd>false</StopOnIdleEnd>");
        xml.AppendLine("      <RestartOnIdle>false</RestartOnIdle>");
        xml.AppendLine("    </IdleSettings>");
        xml.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
        xml.AppendLine("    <Enabled>true</Enabled>");
        xml.AppendLine("    <Hidden>true</Hidden>");
        xml.AppendLine("    <RunOnlyIfIdle>false</RunOnlyIfIdle>");
        xml.AppendLine("    <WakeToRun>false</WakeToRun>");
        xml.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
        xml.AppendLine("    <Priority>4</Priority>");
        xml.AppendLine("  </Settings>");
        xml.AppendLine("  <Actions Context=\"Author\">");
        xml.AppendLine("    <Exec>");
        xml.AppendLine("      <Command>" + SecurityElement.Escape(command) + "</Command>");
        xml.AppendLine("      <Arguments>" + SecurityElement.Escape(arguments) + "</Arguments>");
        xml.AppendLine("    </Exec>");
        xml.AppendLine("  </Actions>");
        xml.AppendLine("</Task>");
        return xml.ToString();
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
