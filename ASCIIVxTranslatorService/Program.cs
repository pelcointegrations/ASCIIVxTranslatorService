using System;
using System.Collections;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using VxEventServer;

namespace ASCIIVxTranslatorService
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (domain, e) => OnUnhandledException((Exception)e.ExceptionObject);
            
            try
            {
                if (args.Any(arg => Regex.IsMatch(arg, "[/-]i(nstall){0,1}")))
                {
                    InstallService();
                    return;
                }

                if (args.Any(arg => Regex.IsMatch(arg, "[/-]u(ninstall){0,1}")))
                {
                    UninstallService();
                    return;
                }


                if (Environment.UserInteractive)
                {
                    Start(RunInteractive);
                }
                else
                {
                    Start(() => ServiceBase.Run(new ASCIIVxTranslatorService()));
                }
            }
            catch (Exception e)
            {
                OnUnhandledException(e);
            }
        }

        private static void InstallService()
        {
            var ti = GetInstaller();
            ti.Install(new Hashtable());
        }

        private static void UninstallService()
        {
            var ti = GetInstaller();
            ti.Uninstall(null);
        }

        private static TransactedInstaller GetInstaller()
        {
            var ti = new TransactedInstaller();
            ti.Installers.Add(new ASCIIVxTranslatorServiceInstaller());
            var path = String.Format("/assemblypath={0}", System.Reflection.Assembly.GetExecutingAssembly().Location);
            ti.Context = new InstallContext("", new string[] {path});
            return ti;
        }

        private static void Start(Action run)
        {
            run();
        }

        private static void RunInteractive()
        {
            Trace.Listeners.Add(new ConsoleTraceListener());

            using (VxEventServer.VxEventServerManager.Instance.Init())
            {
                if (!VxEventServer.VxEventServerManager.Instance.Initialized)
                    return;

                string command = string.Empty;
                CommandHelp();
                do
                {
                    try
                    {
                        command = Console.ReadLine();
                        if (command.ToUpper() == "STATUS")
                        {
                            VxEventServer.VxEventServerManager.Instance.PrintStatus();
                        }
                        else if ((command.ToUpper() == "H") || (command.ToUpper() == "?"))
                        {
                            CommandHelp();
                        }
                        else if (command.ToUpper().Contains("CMD"))
                        {
                            command = command.Replace("CMD ", "");
                            command = command.Replace("cmd ", "");
                            string response = VxEventServer.VxEventServerManager.Instance.PassTestString(command);
                            Console.WriteLine(response);
                        }
                        else if (command.ToUpper().Contains("TESTFILE"))
                        {
                            var commands = command.Split(' ');
                            string param = string.Empty;
                            if (commands.Length > 1)
                                param = commands[1];
                            if (param != string.Empty)
                            {
                                VxEventServer.VxEventServerManager.Instance.InterpretTestFile(param);
                                Console.WriteLine("Test File " + param + " Processed");
                            }
                            else Console.WriteLine("Invalid File Name");
                        }
                        else if (command.ToUpper().Contains("DATASOURCE"))
                        {
                            var commands = command.Split(' ');
                            string param = string.Empty;
                            if (commands.Length > 1)
                                param = commands[1];
                            string response = VxEventServer.VxEventServerManager.Instance.ListDataSources(param);
                            Console.WriteLine(response);
                        }
                        else if (command.ToUpper().Contains("MONITOR"))
                        {
                            var commands = command.Split(' ');
                            string param = string.Empty;
                            if (commands.Length > 1)
                                param = commands[1];
                            string response = VxEventServer.VxEventServerManager.Instance.ListMonitors(param);
                            Console.WriteLine(response);
                        }
                        else if (command.ToUpper().Contains("SITUATIONS"))
                        {
                            var commands = command.Split(' ');
                            string param = string.Empty;
                            if (commands.Length > 1)
                                param = commands[1];
                            string response = VxEventServer.VxEventServerManager.Instance.ListSituations(param);
                            Console.WriteLine(response);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Exception in console command processing: " + e.Message);
                    }
                } while (command.ToUpper() != "QUIT");
            }
        }

        private static void OnUnhandledException(Exception exception)
        {
            Console.WriteLine(exception.Message);
        }

        private static void CommandHelp()
        {
            Console.WriteLine("Type  \"quit\" to close" );
            Console.WriteLine("Type \"Status\" to print current status" );
            Console.WriteLine("Type \"CMD\" followed by an ASCII cmd to have it interpreted");
            Console.WriteLine("Type \"TESTFILE\" followed by a filename to have it interpreted");
            Console.WriteLine("Type \"DATASOURCE\" optionally followed by part of a camera name to list datasources");
            Console.WriteLine("Type \"MONITOR\" optionally followed by part of a monitor name to list monitors");
            Console.WriteLine("Type \"SITUATIONS\" followed by part of a situation name to list situations\n" +
                                "                  containing that substring.\n" +
                                "                  (if blank, all situations will be listed)\n");
            Console.WriteLine("Press \"H\" for help");
        }

    }
}
