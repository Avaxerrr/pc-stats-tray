using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace HwMonTray
{
    internal static class StartupTaskService
    {
        private const string TaskName = "HwMonTray_Startup";

        public static bool IsConfigured()
        {
            try
            {
                Type type = Type.GetTypeFromProgID("Schedule.Service")!;
                dynamic scheduler = Activator.CreateInstance(type)!;
                scheduler.Connect();
                dynamic folder = scheduler.GetFolder("\\");
                var task = folder.GetTask(TaskName);
                return task != null;
            }
            catch
            {
                return false;
            }
        }

        public static void Toggle()
        {
            Type type = Type.GetTypeFromProgID("Schedule.Service")!;
            dynamic scheduler = Activator.CreateInstance(type)!;
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder("\\");

            if (IsConfigured())
            {
                folder.DeleteTask(TaskName, 0);
                return;
            }

            dynamic taskDef = scheduler.NewTask(0);
            taskDef.RegistrationInfo.Description = "Hardware Monitor Tray Icon Startup";
            taskDef.Triggers.Create(9);

            dynamic action = taskDef.Actions.Create(0);
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
            action.Path = exePath;
            action.WorkingDirectory = Path.GetDirectoryName(exePath);

            taskDef.Principal.RunLevel = 1;
            taskDef.Settings.DisallowStartIfOnBatteries = false;
            taskDef.Settings.StopIfGoingOnBatteries = false;
            taskDef.Settings.ExecutionTimeLimit = "PT0S";

            folder.RegisterTaskDefinition(TaskName, taskDef, 6, null, null, 3, null);
        }
    }
}
