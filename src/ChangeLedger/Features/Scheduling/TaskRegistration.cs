using System.Security;
using System.Security.Principal;
using System.Xml.Linq;
using ChangeLedger.Shell;

namespace ChangeLedger.Features;

public enum TaskState { Ok, Missing, StalePath, Off, Error }

/// <summary>
/// "ChangeLedger Snapshot (&lt;SID&gt;)" in the scheduler root, like OneDrive's tasks: current user, no elevation, StartWhenAvailable.
/// Debug builds and exes outside Install.Dir never create it on their own: only the Repair button does.
/// </summary>
public static class TaskRegistration
{
    static readonly string Name = $"ChangeLedger Snapshot ({WindowsIdentity.GetCurrent().User!.Value})";
    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public sealed record Info(TaskState State, DateTime? LastRun = null, int? LastResult = null);

    static dynamic Root()
    {
        dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
        svc.Connect();
        return svc.GetFolder(@"\");
    }

    public static Info Status()
    {
        try
        {
            var t = Root().GetTask(Name);
            var exe = XDocument.Parse((string)t.Xml).Descendants(Ns + "Command").FirstOrDefault()?.Value;
            DateTime last = t.LastRunTime;
            return new(string.Equals(exe, AppInfo.ExePath, StringComparison.OrdinalIgnoreCase) ? TaskState.Ok : TaskState.StalePath,
                last.Year > 2000 ? last : null, last.Year > 2000 ? (int)t.LastTaskResult : null);
        }
        catch (Exception e) when (IsNotFound(e)) { return new(Settings.Current.DailySnapshot ? TaskState.Missing : TaskState.Off); }
        catch (Exception e) { Log.Warn("task status", e); return new(TaskState.Error); }
    }

    /// <summary>At every start of an installed exe: the task exists, points at this exe and runs at the set time.</summary>
    public static void Ensure()
    {
        if (!Settings.Current.DailySnapshot) return;
        try
        {
            var t = Root().GetTask(Name);
            var x = XDocument.Parse((string)t.Xml);
            var exe = x.Descendants(Ns + "Command").FirstOrDefault()?.Value;
            var start = x.Descendants(Ns + "StartBoundary").FirstOrDefault()?.Value;
            if (string.Equals(exe, AppInfo.ExePath, StringComparison.OrdinalIgnoreCase) && start?.Contains($"T{Settings.Current.SnapshotTime}:") == true) return;
        }
        catch (Exception e) when (IsNotFound(e)) { }
        catch (Exception e) { Log.Warn("task check", e); return; }
        Repair();
    }

    /// <summary>Creates or updates the task (TASK_CREATE_OR_UPDATE, TASK_LOGON_INTERACTIVE_TOKEN). Throws on failure.</summary>
    public static void Repair()
    {
        Root().RegisterTask(Name, Xml(AppInfo.ExePath, Settings.Current.SnapshotTime, WindowsIdentity.GetCurrent().User!.Value), 6, null, null, 3);
        Log.Info("task registered");
    }

    public static void Remove()
    {
        try { Root().DeleteTask(Name, 0); Log.Info("task removed"); }
        catch (Exception e) when (IsNotFound(e)) { }
    }

    /// <summary>Re-registers with a new time, only if the task is already there.</summary>
    public static void Reschedule()
    {
        if (Status().State is TaskState.Ok or TaskState.StalePath) Repair();
    }

    static bool IsNotFound(Exception e) => e.HResult is unchecked((int)0x80070002) or unchecked((int)0x80070003);

    internal static string Xml(string exe, string time, string sid) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>lojekq</Author>
            <Description>Change Ledger (github.com/{AppInfo.Repo}): daily read-only snapshot of installed apps, drivers, services, tasks, startup items and environment variables. Writes only to %LOCALAPPDATA%\ChangeLedger.</Description>
          </RegistrationInfo>
          <Triggers>
            <CalendarTrigger>
              <StartBoundary>{DateTime.Today:yyyy-MM-dd}T{time}:00</StartBoundary>
              <Enabled>true</Enabled>
              <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
            </CalendarTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{sid}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <StartWhenAvailable>true</StartWhenAvailable>
            <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
            <Priority>7</Priority>
            <WakeToRun>false</WakeToRun>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{SecurityElement.Escape(exe)}</Command>
              <Arguments>--snapshot --reason scheduled</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
}
