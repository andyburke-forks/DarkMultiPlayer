using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Net;
using System.Text;
using DarkMultiPlayerCommon;
using UDPMeshLib;

namespace DarkMultiPlayerServer
{
    public class Server
    {
        public static bool serverRunning;
        public static bool serverStarting;
        public static bool serverRestarting;
        public static string universeDirectory;
        public static string configDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
        public static Stopwatch serverClock;
        public static HttpListener httpListener;
        public static UdpMeshServer meshServer;
        private static Thread meshServerThread;
        private static long ctrlCTime;
        public static int playerCount = 0;
        public static string players = "";
        public static long lastPlayerActivity;
        public static object universeSizeLock = new object();
        private static int day;

        public static void Main()
        {
#if !DEBUG
            try
            {
#endif
            //Start the server clock
            serverClock = new Stopwatch();
            serverClock.Start();

            Settings.Reset();

            //Set the last player activity time to server start
            lastPlayerActivity = serverClock.ElapsedMilliseconds;

            //Periodic garbage collection
            long lastGarbageCollect = 0;

            //Periodic log check
            long lastLogExpiredCheck = 0;

            //Periodic day check
            long lastDayCheck = 0;

            //Set universe directory
            universeDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Universe");

            if (!Directory.Exists(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            string oldSettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DMPServerSettings.txt");
            string newSettingsFile = Path.Combine(configDirectory, "Settings.txt");
            string oldGameplayFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DMPGameplaySettings.txt");
            string newGameplayFile = Path.Combine(configDirectory, "GameplaySettings.txt");

            //Register the server commands
            CommandHandler.RegisterCommand("exit", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("quit", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("shutdown", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("restart", Server.Restart, "Restarts the server");
            CommandHandler.RegisterCommand("kick", KickCommand.KickPlayer, "Kicks a player from the server");
            CommandHandler.RegisterCommand("pm", PMCommand.HandleCommand, "Sends a message to a player");

            //Register the ctrl+c event
            Console.CancelKeyPress += new ConsoleCancelEventHandler(CatchExit);
            serverStarting = true;

            //Remove player tokens

            if (System.Net.Sockets.Socket.OSSupportsIPv6)
            {
                Settings.settingsStore.address = "::";
            }

            DarkLog.Debug("Loading settings...");
            Settings.Load();
            if (Settings.settingsStore.gameDifficulty == GameDifficulty.CUSTOM)
            {
                GameplaySettings.Reset();
                GameplaySettings.Load();
            }

            //Test compression
            if (Settings.settingsStore.compressionEnabled)
            {
                Compression.compressionEnabled = true;
            }

            //Set day for log change
            day = DateTime.Now.Day;

            //Load plugins
            DMPPluginHandler.LoadPlugins();

            Console.Title = "DMPServer " + Common.PROGRAM_VERSION + ", protocol " + Common.PROTOCOL_VERSION;

            while (serverStarting || serverRestarting)
            {
                if (serverRestarting)
                {
                    DarkLog.Debug("Reloading settings...");
                    Settings.Reset();
                    Settings.Load();
                    if (Settings.settingsStore.gameDifficulty == GameDifficulty.CUSTOM)
                    {
                        DarkLog.Debug("Reloading gameplay settings...");
                        GameplaySettings.Reset();
                        GameplaySettings.Load();
                    }
                }

                serverRestarting = false;
                DarkLog.Normal("Starting DMPServer " + Common.PROGRAM_VERSION + ", protocol " + Common.PROTOCOL_VERSION);

                if (Settings.settingsStore.gameDifficulty == GameDifficulty.CUSTOM)
                {
                    //Generate the config file by accessing the object.
                    DarkLog.Debug("Loading gameplay settings...");
                    GameplaySettings.Load();
                }

                //Load universe
                DarkLog.Normal("Loading universe... ");
                CheckUniverse();

                DarkLog.Normal("Starting " + Settings.settingsStore.warpMode + " server on port " + Settings.settingsStore.port + "... ");

                serverRunning = true;
                Thread commandThread = new Thread(new ThreadStart(CommandHandler.ThreadMain));
                Thread clientThread = new Thread(new ThreadStart(ClientHandler.ThreadMain));
                commandThread.Start();
                clientThread.Start();
                while (serverStarting)
                {
                    Thread.Sleep(500);
                }
                StartMeshServer();
                StartHTTPServer();
                DarkLog.Normal("Ready!");
                DMPPluginHandler.FireOnServerStart();
                while (serverRunning)
                {
                    //Run a garbage collection every 30 seconds.
                    if ((serverClock.ElapsedMilliseconds - lastGarbageCollect) > 30000)
                    {
                        lastGarbageCollect = serverClock.ElapsedMilliseconds;
                        GC.Collect();
                    }
                    //Run the log expire function every 10 minutes
                    if ((serverClock.ElapsedMilliseconds - lastLogExpiredCheck) > 600000)
                    {
                        lastLogExpiredCheck = serverClock.ElapsedMilliseconds;
                        LogExpire.ExpireLogs();
                    }
                    // Check if the day has changed, every minute
                    if ((serverClock.ElapsedMilliseconds - lastDayCheck) > 60000)
                    {
                        lastDayCheck = serverClock.ElapsedMilliseconds;
                        if (day != DateTime.Now.Day)
                        {
                            DarkLog.LogFilename = Path.Combine(DarkLog.LogFolder, "dmpserver " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log");
                            DarkLog.WriteToLog("Continued from logfile " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log");
                            day = DateTime.Now.Day;
                        }
                    }

                    Thread.Sleep(500);
                }
                DMPPluginHandler.FireOnServerStop();
                commandThread.Abort();
                clientThread.Join();
            }
            DarkLog.Normal("Goodbye!");
            Environment.Exit(0);
#if !DEBUG
            }
            catch (Exception e)
            {
                DarkLog.Fatal("Error in main server thread, Exception: " + e);
                throw;
            }
#endif
        }

        // Check universe folder size
        public static long GetUniverseSize()
        {
            lock (universeSizeLock)
            {
                long directorySize = 0;
                string[] kerbals = Directory.GetFiles(Path.Combine(universeDirectory, "Kerbals"), "*.*");
                string[] vessels = Directory.GetFiles(Path.Combine(universeDirectory, "Vessels"), "*.*");

                foreach (string kerbal in kerbals)
                {
                    FileInfo kInfo = new FileInfo(kerbal);
                    directorySize += kInfo.Length;
                }

                foreach (string vessel in vessels)
                {
                    FileInfo vInfo = new FileInfo(vessel);
                    directorySize += vInfo.Length;
                }

                return directorySize;
            }
        }
        //Get last disconnect time
        public static long GetLastPlayerActivity()
        {
            if (playerCount > 0)
            {
                return 0;
            }
            return (serverClock.ElapsedMilliseconds - lastPlayerActivity) / 1000;
        }
        //Create universe directories
        private static void CheckUniverse()
        {

            if (!Directory.Exists(universeDirectory))
            {
                Directory.CreateDirectory(universeDirectory);
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Crafts")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Crafts"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Flags")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Flags"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Players")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Players"));
            }
            if (!Settings.settingsStore.sendPlayerToLatestSubspace)
            {
                if (!Directory.Exists(Path.Combine(universeDirectory, "OfflinePlayerTimes")))
                {
                    Directory.CreateDirectory(Path.Combine(universeDirectory, "OfflinePlayerTimes"));
                }
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Kerbals")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Kerbals"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Vessels")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Vessels"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Scenarios")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Scenarios"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Scenarios", "Initial")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Scenarios", "Initial"));
            }
        }
        //Get mod file SHA

        //Shutdown
        public static void ShutDown(string commandArgs)
        {
            if (commandArgs != "")
            {
                DarkLog.Normal("Shutting down - " + commandArgs);
                Messages.ConnectionEnd.SendConnectionEndToAll("Server is shutting down - " + commandArgs);
            }
            else
            {
                DarkLog.Normal("Shutting down");
                Messages.ConnectionEnd.SendConnectionEndToAll("Server is shutting down");
            }
            serverStarting = false;
            serverRunning = false;
            StopHTTPServer();
            StopMeshServer();
        }
        //Restart
        public static void Restart(string reason)
        {
            if (reason != "")
            {
                DarkLog.Normal("Restarting - " + reason);
                Messages.ConnectionEnd.SendConnectionEndToAll("Server is restarting - " + reason);
            }
            else
            {
                DarkLog.Normal("Restarting");
                Messages.ConnectionEnd.SendConnectionEndToAll("Server is restarting");
            }
            serverRestarting = true;
            serverStarting = false;
            serverRunning = false;
            ForceStopHTTPServer();
            StopMeshServer();
        }

        //Gracefully shut down
        private static void CatchExit(object sender, ConsoleCancelEventArgs args)
        {
            //If control+c not pressed within 5 seconds, catch it and shutdown gracefully.
            if ((DateTime.UtcNow.Ticks - ctrlCTime) > 50000000)
            {
                ctrlCTime = DateTime.UtcNow.Ticks;
                args.Cancel = true;
                ShutDown("Caught Ctrl+C");
            }
            else
            {
                DarkLog.Debug("Terminating!");
            }
        }

        private static void StartMeshServer()
        {
            //Only enable mesh server when we can bind on all ports
            if (Settings.settingsStore.address == "0.0.0.0" || Settings.settingsStore.address == "::")
            {
                DarkLog.Debug("Starting mesh server");
                meshServer = new UdpMeshServer(Settings.settingsStore.port, DarkLog.Debug);
                meshServerThread = meshServer.Start();
            }
            else
            {
                DarkLog.Normal("Not starting mesh server because we are bound on a specific address");
            }
        }


        private static void StopMeshServer()
        {
            DarkLog.Debug("Stopping mesh server");
            meshServer.Shutdown();
            meshServerThread = null;
        }

        private static void StartHTTPServer()
        {
            string OS = Environment.OSVersion.Platform.ToString();
            if (Settings.settingsStore.httpPort > 0)
            {
                DarkLog.Normal("Starting HTTP server...");
                httpListener = new HttpListener();
                try
                {
                    if (Settings.settingsStore.address != "0.0.0.0" && Settings.settingsStore.address != "::")
                    {
                        string listenAddress = Settings.settingsStore.address;
                        if (listenAddress.Contains(":"))
                        {
                            //Sorry
                            DarkLog.Error("Error: The server status port does not support specific IPv6 addresses. Sorry.");
                            //listenAddress = "[" + listenAddress + "]";
                            return;

                        }

                        httpListener.Prefixes.Add("http://" + listenAddress + ":" + Settings.settingsStore.httpPort + '/');
                    }
                    else
                    {
                        httpListener.Prefixes.Add("http://*:" + Settings.settingsStore.httpPort + '/');
                    }
                    httpListener.Start();
                    httpListener.BeginGetContext(asyncHTTPCallback, httpListener);
                }
                catch (HttpListenerException e)
                {
                    if (OS == "Win32NT" || OS == "Win32S" || OS == "Win32Windows" || OS == "WinCE") // if OS is Windows
                    {
                        if (e.ErrorCode == 5) // Access Denied
                        {
                            DarkLog.Debug("HTTP Server: access denied.");
                            DarkLog.Debug("Prompting user to switch to administrator mode.");

                            ProcessStartInfo startInfo = new ProcessStartInfo("DMPServer.exe") { Verb = "runas" };
                            Process.Start(startInfo);

                            Environment.Exit(0);
                        }
                    }
                    else
                    {
                        DarkLog.Fatal("Error while starting HTTP server.\n" + e);
                    }
                    throw;
                }
            }
        }

        private static void StopHTTPServer()
        {
            if (Settings.settingsStore.httpPort > 0)
            {
                DarkLog.Normal("Stopping HTTP server...");
                httpListener.Stop();
            }
        }

        private static void ForceStopHTTPServer()
        {
            if (Settings.settingsStore.httpPort > 0)
            {
                DarkLog.Normal("Force stopping HTTP server...");
                if (httpListener != null)
                {
                    try
                    {
                        httpListener.Abort();
                    }
                    catch (Exception e)
                    {
                        DarkLog.Fatal("Error trying to shutdown HTTP server: " + e);
                        throw;
                    }
                }
            }
        }

        private static void asyncHTTPCallback(IAsyncResult result)
        {
            try
            {
                HttpListener listener = (HttpListener)result.AsyncState;

                HttpListenerContext context = listener.EndGetContext(result);
                string responseText = "";

                responseText = new ServerInfo(Settings.settingsStore).GetJSON();

                byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                context.Response.ContentLength64 = buffer.LongLength;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();

                listener.BeginGetContext(asyncHTTPCallback, listener);
            }
            catch (Exception e)
            {
                //Ignore the EngGetContext throw while shutting down the HTTP server.
                if (serverRunning)
                {
                    DarkLog.Error("Exception while listening to HTTP server!, Exception:\n" + e);
                    Thread.Sleep(1000);
                    httpListener.BeginGetContext(asyncHTTPCallback, httpListener);
                }
            }
        }
    }
}

