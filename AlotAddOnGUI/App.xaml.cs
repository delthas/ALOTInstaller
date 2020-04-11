﻿using AlotAddOnGUI.classes;
using CommandLine;
using Microsoft.Win32;
using Serilog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;

namespace AlotAddOnGUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static bool POST_STARTUP = false;
        public static bool BootMEUITMMode = false;
        public static string PreloadedME1Path = null;
        public static string PreloadedME2Path = null;
        public static string PreloadedME3Path = null;
        public static string LogsDirectory;
        public static MainWindow mainWindow;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string name);
        public const string MEMendFileMarker = "ThisIsMEMEndOfFileMarker";
        //Windows 8.1 Update 1
        public static readonly Version MIN_SUPPORTED_OS = new Version("6.3.9600");

        [STAThread]
        public static void Main()
        {
            UnblockLibFiles();
            Debug.WriteLine("Setting assembly resolve");
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            Directory.SetCurrentDirectory(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location));
            try
            {
                var application = new App();
                application.InitializeComponent();
                application.Run();
            }
            catch (Exception e)
            {
                OnFatalCrash(e);
                DelayedResolveTrackError(e);
                //Crashes.TrackError(e);
                throw e;
            }
        }

        /// <summary>
        /// This has to be in subroutine because the library resolution code will not have executed by the time the assembly for Crashes is required to be loaded.
        /// </summary>
        /// <param name="e"></param>
        private static void DelayedResolveTrackError(Exception e)
        {
            Crashes.TrackError(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            Debug.WriteLine("Setting onstartup");
            base.OnStartup(e);
            if (APIKeys.HasAppCenterKey)
            {
                //Setup App Center
                Crashes.SendingErrorReport += (object sender, SendingErrorReportEventArgs ea) =>
                {
                    // Your code, e.g. to present a custom UI.
                    mainWindow?.ShowStatus("Uploading crash log");
                };
                Crashes.SentErrorReport += (object sender, SentErrorReportEventArgs ea) =>
                {
                    mainWindow?.ShowStatus("Uploaded crash log");
                };
                Crashes.FailedToSendErrorReport += (object sender, FailedToSendErrorReportEventArgs ea) =>
                {
                    mainWindow?.ShowStatus("Failed to upload crash log");
                };
                Crashes.GetErrorAttachments = (ErrorReport report) =>
                {

                    var attachments = new List<ErrorAttachmentLog>();
                    // Attach some text.
                    var date = report.AppErrorTime.LocalDateTime.ToString("yyyyMMdd");
                    var logfile = Path.Combine(LogsDirectory, $"alotinstaller-{date}.txt");
                    if (File.Exists(logfile))
                    {
                        string log = Utilities.ReadLockedTextFile(logfile);
                        if (log.Length < ByteSizeLib.ByteSize.BytesInMegaByte * 7)
                        {
                            attachments.Add(ErrorAttachmentLog.AttachmentWithText(log, "crashlog.txt"));
                        }
                    }
                    // Attach binary data.
                    //var fakeImage = System.Text.Encoding.Default.GetBytes("Fake image");
                    //ErrorAttachmentLog binaryLog = ErrorAttachmentLog.AttachmentWithBinary(fakeImage, "ic_launcher.jpeg", "image/jpeg");

                    return attachments;
                };
                AppCenter.Start(APIKeys.AppCenterKey, typeof(Analytics), typeof(Crashes));
            }
        }

        /// <summary>
        /// Removes ADS streams from files in the lib folder. This prevents startup crash caused by inability for dlls to load from "the internet" if extracted via windows explorer.
        /// </summary>
        private static void UnblockLibFiles()
        {
            var probingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "lib");
            var files = Directory.GetFiles(probingPath);
            foreach (string file in files)
            {
                DeleteFile(file + ":Zone.Identifier");
            }
        }

        public App() : base()
        {
            string[] args = Environment.GetCommandLineArgs();
            List<string> preLogMessages = new List<string>();
            Parsed<Options> parsedCommandLineArgs = null;
            string baseDirectory = System.AppDomain.CurrentDomain.BaseDirectory;
            string updateDestinationPath = null;
            if (args.Length > 1)
            {
                var result = Parser.Default.ParseArguments<Options>(args);
                if (result.GetType() == typeof(Parsed<Options>))
                {
                    //Parsing succeeded - have to do update check to keep logs in order...
                    parsedCommandLineArgs = (Parsed<Options>)result;
                    if (parsedCommandLineArgs.Value.UpdateDest != null)
                    {
                        if (Directory.Exists(parsedCommandLineArgs.Value.UpdateDest))
                        {
                            updateDestinationPath = parsedCommandLineArgs.Value.UpdateDest;
                            baseDirectory = updateDestinationPath;
                        }
                        else
                        {
                            preLogMessages.Add("Directory doesn't exist for update: " + parsedCommandLineArgs.Value.UpdateDest);
                        }
                    }
                    if (parsedCommandLineArgs.Value.BootMEUITMMode)
                    {
                        Log.Information("We are booting into MEUITM mode.");
                        BootMEUITMMode = true;
                    }
                    #region Mod Manager pathing
                    if (parsedCommandLineArgs.Value.ME1Path != null && Directory.Exists(parsedCommandLineArgs.Value.ME1Path))
                    {
                        PreloadedME1Path = parsedCommandLineArgs.Value.ME1Path;
                        //get MassEffectModder.ini
                        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MassEffectModder");
                        string _iniPath = Path.Combine(path, "MassEffectModder.ini");
                        if (!Directory.Exists(path))
                        {
                            preLogMessages.Add("Preset ME1 path, ini folder doesn't exist, creating.");
                            Directory.CreateDirectory(path);
                        }
                        if (!File.Exists(_iniPath))
                        {
                            preLogMessages.Add("Preset ME1 path, ini doesn't exist, creating.");
                            File.Create(_iniPath);
                        }

                        IniFile ini = new IniFile(_iniPath);
                        ini.Write("ME1", parsedCommandLineArgs.Value.ME1Path, "GameDataPath");
                        preLogMessages.Add("Wrote preset ME1 path to " + parsedCommandLineArgs.Value.ME1Path);
                    }
                    if (parsedCommandLineArgs.Value.ME2Path != null && Directory.Exists(parsedCommandLineArgs.Value.ME2Path))
                    {
                        PreloadedME2Path = parsedCommandLineArgs.Value.ME2Path;
                        //get MassEffectModder.ini
                        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MassEffectModder");
                        string _iniPath = Path.Combine(path, "MassEffectModder.ini");
                        if (!Directory.Exists(path))
                        {
                            preLogMessages.Add("Preset ME2 path, ini folder doesn't exist, creating.");
                            Directory.CreateDirectory(path);
                        }
                        if (!File.Exists(_iniPath))
                        {
                            preLogMessages.Add("Preset ME2 path, ini doesn't exist, creating.");
                            File.Create(_iniPath);
                        }

                        IniFile ini = new IniFile(_iniPath);
                        ini.Write("ME2", parsedCommandLineArgs.Value.ME2Path, "GameDataPath");
                        preLogMessages.Add("Wrote preset ME2 path to " + parsedCommandLineArgs.Value.ME2Path);
                    }
                    if (parsedCommandLineArgs.Value.ME3Path != null && Directory.Exists(parsedCommandLineArgs.Value.ME3Path))
                    {
                        PreloadedME3Path = parsedCommandLineArgs.Value.ME3Path;
                        //get MassEffectModder.ini
                        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MassEffectModder");
                        string _iniPath = Path.Combine(path, "MassEffectModder.ini");
                        if (!Directory.Exists(path))
                        {
                            preLogMessages.Add("Preset ME3 path, ini folder doesn't exist, creating.");
                            Directory.CreateDirectory(path);
                        }
                        if (!File.Exists(_iniPath))
                        {
                            preLogMessages.Add("Preset ME3 path, ini doesn't exist, creating.");
                            File.Create(_iniPath);
                        }

                        IniFile ini = new IniFile(_iniPath);
                        ini.Write("ME3", parsedCommandLineArgs.Value.ME3Path, "GameDataPath");
                        preLogMessages.Add("Wrote preset ME3 path to " + parsedCommandLineArgs.Value.ME3Path);
                    }
                    #endregion
                    if (parsedCommandLineArgs.Value.BootingNewUpdate)
                    {
                        Log.Information("Booting an update");
                        preLogMessages.Add("Booting an update.");
                        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "*.pdb", SearchOption.AllDirectories))
                        {
                            File.Delete(file);
                        }
                    }
                }
            }

            LogsDirectory = Directory.CreateDirectory(baseDirectory + "\\logs").FullName;
            Log.Logger = new LoggerConfiguration()
                   .MinimumLevel.Debug()
                .WriteTo.RollingFile(Path.Combine(LogsDirectory, "alotinstaller-{Date}.txt"), flushToDiskInterval: new TimeSpan(0, 0, 15))
#if DEBUG
                   .WriteTo.Debug()
#endif
              .CreateLogger();
            this.Dispatcher.UnhandledException += OnDispatcherUnhandledException;
            POST_STARTUP = true;
            Log.Information("=====================================================");
            Log.Information("Logger Started for ALOT Installer.");
            preLogMessages.ForEach(x => Log.Information("Prelogger boot message: " + x));
            if (args.Length > 0)
            {
                string commandlineargs = "";
                for (int i = 0; i < args.Length; i++)
                {
                    commandlineargs += args[i] + " ";
                }
                Log.Information("Command line arguments: " + commandlineargs);
            }
            Log.Information("Working directory: " + Directory.GetCurrentDirectory());

            //Update Mode
            if (updateDestinationPath != null)
            {
                Thread.Sleep(2000); //SLEEP WHILE WE WAIT FOR PARENT PROCESS TO STOP.
                Log.Information("In update mode. Update destination: " + updateDestinationPath);
                int i = 0;
                while (i < 5)
                {

                    i++;
                    try
                    {
                        Log.Information("Applying update");
                        CopyDir.CopyAll(new DirectoryInfo(System.AppDomain.CurrentDomain.BaseDirectory), new DirectoryInfo(updateDestinationPath));
                        break;
                    }
                    catch (Exception e)
                    {
                        Log.Error("Error applying update: " + e.Message);
                        if (i < 5)
                        {
                            Thread.Sleep(1000);
                            Log.Information("Attempt #" + (i + 1));
                        }
                        else
                        {
                            Log.Fatal("Unable to apply update after 5 attempts. We are giving up.");
                            MessageBox.Show("Update was unable to apply. See the logs directory for more information. If this continues to happen please come to the ALOT discord or download a new copy from GitHub.");
                            Environment.Exit(1);
                        }
                    }
                }
                Log.Information("Update files have been applied.");
                updateDestinationPath += "\\"; //add slash
                Log.Information("Performing update migrations...");
                if (Directory.Exists(updateDestinationPath + "MEM_Packages") && !Directory.Exists(updateDestinationPath + @"Data\MEM_Packages"))
                {
                    Log.Information("Migrating MEM_Packages folder into subfolder");
                    Directory.Move(updateDestinationPath + "MEM_Packages", updateDestinationPath + @"Data\MEM_Packages");
                }


                if (Directory.Exists(updateDestinationPath + "music") && !Directory.Exists(updateDestinationPath + @"Data\Music"))
                {
                    Log.Information("Migrating music folder into subfolder");
                    Directory.Move(updateDestinationPath + "music", updateDestinationPath + @"Data\Music");
                }

                if (Directory.Exists(updateDestinationPath + "bin"))
                {
                    Log.Information("Deleting old top level bin folder: " + (updateDestinationPath + "bin"));
                    Utilities.DeleteFilesAndFoldersRecursively(updateDestinationPath + "bin");
                }

                if (Directory.Exists(updateDestinationPath + "lib"))
                {
                    Log.Information("Deleting old top level lib folder");
                    Utilities.DeleteFilesAndFoldersRecursively(updateDestinationPath + "lib");
                }

                if (Directory.Exists(updateDestinationPath + "Extracted_Mods"))
                {
                    Log.Information("Deleting leftover Extracted_Mods folder");
                    Utilities.DeleteFilesAndFoldersRecursively(updateDestinationPath + "Extracted_Mods");
                }

                if (File.Exists(updateDestinationPath + "manifest.xml"))
                {
                    Log.Information("Deleting leftover manifest.xml file");
                    File.Delete(updateDestinationPath + "manifest.xml");
                }

                if (File.Exists(updateDestinationPath + "ALOTInstaller.exe.config"))
                {
                    Log.Information("Deleting leftover config file");
                    File.Delete(updateDestinationPath + "ALOTInstaller.exe.config");
                }

                if (File.Exists(updateDestinationPath + "manifest-bundled.xml"))
                {
                    Log.Information("Deleting leftover manifest-bundled.xml file");
                    File.Delete(updateDestinationPath + "manifest-bundled.xml");
                }

                if (File.Exists(updateDestinationPath + "DEV_MODE"))
                {
                    Log.Information("Pulling application out of developer mode");
                    File.Delete(updateDestinationPath + "DEV_MODE");
                }
                Log.Information("Rebooting into normal mode to complete update: " + updateDestinationPath + System.AppDomain.CurrentDomain.FriendlyName);
                ProcessStartInfo psi = new ProcessStartInfo(updateDestinationPath + System.AppDomain.CurrentDomain.FriendlyName);
                psi.WorkingDirectory = updateDestinationPath;
                psi.Arguments = "--completing-update";
                Process.Start(psi);
                Environment.Exit(0);
                System.Windows.Application.Current.Shutdown();
            }

            //Normal Mode
            ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(UIElement),
                new FrameworkPropertyMetadata(15000));
            ToolTipService.ShowOnDisabledProperty.OverrideMetadata(
            typeof(Control),
            new FrameworkPropertyMetadata(true));
            if (Directory.Exists(baseDirectory + "Update"))
            {
                Thread.Sleep(1000);
                Log.Information("Removing Update directory");
                Directory.Delete(baseDirectory + "Update", true);
            }
            if (File.Exists(baseDirectory + "ALOTAddonBuilder.exe"))
            {
                Log.Information("Deleting Update Shim ALOTAddonBuilder.exe");
                File.Delete(baseDirectory + "ALOTAddonBuilder.exe");
            }

            if (parsedCommandLineArgs != null && parsedCommandLineArgs.Value != null && parsedCommandLineArgs.Value.BootingNewUpdate)
            {
                //turn off debug mode
                Utilities.WriteRegistryKey(Registry.CurrentUser, AlotAddOnGUI.MainWindow.REGISTRY_KEY, AlotAddOnGUI.MainWindow.SETTINGSTR_DEBUGLOGGING, 0);
                Analytics.TrackEvent("Update Completed", new Dictionary<string, string> {
                    { "NewVersion", System.Reflection.Assembly.GetEntryAssembly().GetName().Version.ToString() }
                });
            }

            Log.Information("Program Version: " + System.Reflection.Assembly.GetEntryAssembly().GetName().Version);
            try
            {
                Log.Information("System information:\n" + Utilities.GetOperatingSystemInfo());
                Utilities.GetAntivirusInfo();
            }
            catch (Exception e)
            {
                Log.Error("UNABLE TO GET SYSTEM INFORMATION OR ANTIVIRUS INFO. This is indiciative that system may not be stable or that WMI is corrupt.");
                Log.Error(App.FlattenException(e));
            }
            //string releaseId = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId", "").ToString();
        }

        /// <summary>
        /// Called when an unhandled exception occurs. This method can only be invoked after startup has completed. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">Exception to process</param>
        void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            string errorMessage = string.Format("ALOT Installer has crashed! This is the exception that caused the crash:");
            string st = FlattenException(e.Exception);
            Log.Fatal(errorMessage);
            Log.Fatal(st);
            Log.Information("Forcing beta mode off before exiting...");
            Utilities.WriteRegistryKey(Registry.CurrentUser, AlotAddOnGUI.MainWindow.REGISTRY_KEY, AlotAddOnGUI.MainWindow.SETTINGSTR_BETAMODE, 0);
            Log.CloseAndFlush();

            //Handled by App Center
            //if (Directory.Exists("Data") && !File.Exists(@"Data\APP_CRASH"))
            //{
            //    File.Create(@"Data\APP_CRASH");
            //}
        }

        /// <summary>
        /// Called when a fatal crash occurs. Only does something if startup has not completed.
        /// </summary>
        /// <param name="e">The fatal exception.</param>
        public static void OnFatalCrash(Exception e)
        {
            if (!POST_STARTUP)
            {
                string errorMessage = string.Format("ALOT Installer has encountered a fatal startup crash:\n" + FlattenException(e));
                File.WriteAllText("FATAL_STARTUP_CRASH.txt", errorMessage);
            }
        }

        /// <summary>
        /// Flattens an exception into a printable string
        /// </summary>
        /// <param name="exception">Exception to flatten</param>
        /// <returns>Printable string</returns>
        public static string FlattenException(Exception exception)
        {
            var stringBuilder = new StringBuilder();

            while (exception != null)
            {
                stringBuilder.AppendLine(exception.GetType().Name + ": " + exception.Message);
                stringBuilder.AppendLine(exception.StackTrace);

                exception = exception.InnerException;
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// Called when the application is exiting normally
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Application_Exit(object sender, ExitEventArgs e)
        {
            var exists = System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location)).Count() > 1;
            if (!exists)
            {
                Log.Information("Only instance running, killing other apps...");
                Utilities.runProcess("cmd.exe", "/c taskkill /F /IM MassEffectModderNoGui.exe /T", true);
                Utilities.runProcess("cmd.exe", "/c taskkill /F /IM 7z.exe /T", true);
            }
            Log.Information("Closing application via AppClosing()");
        }

        /// <summary>
        /// Resolves assemblies in Data/lib.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private static System.Reflection.Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var probingPath = AppDomain.CurrentDomain.BaseDirectory + @"Data\lib";
            var assyName = new AssemblyName(args.Name);

            var newPath = Path.Combine(probingPath, assyName.Name);
            if (!newPath.EndsWith(".dll"))
            {
                newPath = newPath + ".dll";
            }
            if (File.Exists(newPath))
            {

                var assy = Assembly.LoadFile(newPath);
                return assy;
            }
            return null;
        }
    }

    class Options
    {
        [Option('u', "update-dest",
          HelpText = "Copies ALOTInstaller and everything in the current directory (and subdirectories) into the listed directory, then reboots using the new EXE.")]
        public string UpdateDest { get; set; }

        [Option('c', "completing-update",
            HelpText = "Indicates that we are booting a new copy of ALOTInstaller that has just been upgraded")]
        public bool BootingNewUpdate { get; set; }

        [Option('m', "meuitm-mode",
            HelpText = "Not used. Kept for legacy purposes for now.")]
        public bool BootMEUITMMode { get; set; }

        [Option("me1path",
            HelpText = "Sets the path for ME1 when the application boots")]
        public string ME1Path { get; set; }

        [Option("me2path",
            HelpText = "Sets the path for ME2 when the application boots")]
        public string ME2Path { get; set; }

        [Option("me3path",
            HelpText = "Sets the path for ME3 when the application boots")]
        public string ME3Path { get; set; }
    }
}
