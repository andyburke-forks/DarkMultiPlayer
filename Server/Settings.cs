using System;
using System.IO;
using System.Net;
using DarkMultiPlayerCommon;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
using SettingsParser;

namespace DarkMultiPlayerServer
{
    public class Settings
    {
        private static ConfigParser<SettingsStore> serverSettings;
        public static SettingsStore settingsStore
        {
            get
            {
                return serverSettings.Settings;
            }
        }

        public static void Reset()
        {
            serverSettings = new ConfigParser<SettingsStore>(new SettingsStore(), Path.Combine(Server.configDirectory, "Settings.txt"));
        }

        public static void Load()
        {
            serverSettings.LoadSettings();
        }

        public static void Save()
        {
            serverSettings.SaveSettings();
        }
    }

    public class SettingsStore
    {
        [Description("The address the server listens on.\nWARNING: You do not need to change this unless you are running 2 servers on the same port.\nChanging this setting from 0.0.0.0 will only give you trouble if you aren't running multiple servers.\nChange this setting to :: to listen on IPv4 and IPv6.")]
        public string address = "0.0.0.0";
        [Description("The port the server listens on.")]
        public int port = 6702;
        [Description("Specify the warp type.")]
        public WarpMode warpMode = WarpMode.SUBSPACE;
        [Description("Specify the game type.")]
        public GameMode gameMode = GameMode.SANDBOX;
/*
        [Description("Use shared science")]
        public bool sharedScience = false;
*/
        [Description("Specify the gameplay difficulty of the server.")]
        public GameDifficulty gameDifficulty = GameDifficulty.NORMAL;
        [Description("If true, sends the player to the latest subspace upon connecting. If false, sends the player to the previous time they were in.\nNOTE: This may cause time-paradoxes.")]
        public bool sendPlayerToLatestSubspace = true;
        [Description("Use UTC instead of system time in the log.")]
        public bool useUTCTimeInLog = false;
        [Description("Minimum log level.")]
        public DarkLog.LogLevels logLevel = DarkLog.LogLevels.DEBUG;
        public bool cheats = true;
        [Description("HTTP port for server status. 0 = Disabled")]
        public int httpPort = 0;
        [Description("Name of the server.")]
        public string serverName = "DMP Server";
        [Description("Maximum amount of players that can join the server.")]
        public int maxPlayers = 20;
        [Description("Specify in minutes how often /nukeksc automatically runs. 0 = Disabled")]
        public int autoNuke = 0;
        [Description("Specify in minutes how often /dekessler automatically runs. 0 = Disabled")]
        public int autoDekessler = 30;
        [Description("How many untracked asteroids to spawn into the universe. 0 = Disabled")]
        public int numberOfAsteroids = 30;
        [Description("Specify the name that will appear when you send a message using the server's console.")]
        public string consoleIdentifier = "Server";
        [Description("Specify the server's MOTD (message of the day).")]
        public string serverMotd = "Welcome, %name%!";
        [Description("Specify whether to enable compression. Decreases bandwidth usage but increases CPU usage. 0 = Disabled")]
        public bool compressionEnabled = true;
        [Description("Specify the amount of days a log file should be considered as expired and deleted. 0 = Disabled")]
        public double expireLogs = 0;
    }
}