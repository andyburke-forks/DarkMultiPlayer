using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class DMPGame
    {
        public bool running;
        public bool startGame;
        public bool forceQuit;
        public GameMode gameMode;
        public bool serverAllowCheats = true;
        // Server setting
        public GameDifficulty serverDifficulty;
        public GameParameters serverParameters;
        public List<NamedAction> updateEvent = new List<NamedAction>();
        public List<NamedAction> fixedUpdateEvent = new List<NamedAction>();
        public List<NamedAction> drawEvent = new List<NamedAction>();
        private List<Action> stopEvent = new List<Action>();
        public readonly UniverseSyncCache universeSyncCache;
        public readonly ConnectionWindow connectionWindow;
        public readonly PlayerStatusWindow playerStatusWindow;
        public readonly ConfigNodeSerializer configNodeSerializer;
        public readonly ScenarioWorker scenarioWorker;
        public readonly NetworkWorker networkWorker;
        public readonly FlagSyncer flagSyncer;
        public readonly VesselWorker vesselWorker;
        public readonly TimeSyncer timeSyncer;
        public readonly WarpWorker warpWorker;
        public readonly PlayerStatusWorker playerStatusWorker;
        public readonly DynamicTickWorker dynamicTickWorker;
        public readonly DebugWindow debugWindow;
        public readonly PlayerColorWorker playerColorWorker;
        public readonly PartKiller partKiller;
        public readonly KerbalReassigner kerbalReassigner;
        public readonly AsteroidWorker asteroidWorker;
        public readonly PosistionStatistics posistionStatistics;
        public readonly Profiler profiler;
        public readonly VesselRangeBumper vesselRangeBumper;
        private DMPModInterface dmpModInterface;

        public DMPGame(UniverseSyncCache universeSyncCache, ConnectionWindow connectionWindow, DMPModInterface dmpModInterface, ToolbarSupport toolbarSupport, OptionsWindow optionsWindow, Profiler profiler)
        {
            this.universeSyncCache = universeSyncCache;
            this.connectionWindow = connectionWindow;
            this.dmpModInterface = dmpModInterface;
            this.profiler = profiler;
            this.configNodeSerializer = new ConfigNodeSerializer();
            this.posistionStatistics = new PosistionStatistics();
            this.vesselRangeBumper = new VesselRangeBumper(this);
            this.networkWorker = new NetworkWorker(this, connectionWindow, configNodeSerializer, profiler, vesselRangeBumper);
            this.flagSyncer = new FlagSyncer(this, networkWorker);
            this.partKiller = new PartKiller();
            this.dynamicTickWorker = new DynamicTickWorker(this, networkWorker);
            this.kerbalReassigner = new KerbalReassigner();
            this.playerColorWorker = new PlayerColorWorker(this, networkWorker);
            this.vesselWorker = new VesselWorker(this, networkWorker, configNodeSerializer, dynamicTickWorker, kerbalReassigner, partKiller, posistionStatistics, profiler, vesselRangeBumper, playerColorWorker);
            this.scenarioWorker = new ScenarioWorker(this, vesselWorker, configNodeSerializer, networkWorker);
            this.playerStatusWorker = new PlayerStatusWorker(this, vesselWorker, networkWorker);
            this.timeSyncer = new TimeSyncer(this, networkWorker, vesselWorker);
            this.warpWorker = new WarpWorker(this, timeSyncer, networkWorker, playerStatusWorker);
            this.debugWindow = new DebugWindow(this, timeSyncer, networkWorker, vesselWorker, dynamicTickWorker, warpWorker, posistionStatistics, optionsWindow, profiler);
            this.asteroidWorker = new AsteroidWorker(this, networkWorker, vesselWorker);
            this.playerStatusWindow = new PlayerStatusWindow(this, warpWorker, timeSyncer, playerStatusWorker, optionsWindow, playerColorWorker);
            this.playerColorWorker.SetDependencies(playerStatusWindow);
            this.vesselWorker.SetDependencies(timeSyncer, warpWorker, asteroidWorker, playerStatusWorker);
            this.networkWorker.SetDependencies(timeSyncer, warpWorker, playerColorWorker, flagSyncer, partKiller, kerbalReassigner, asteroidWorker, vesselWorker, playerStatusWorker, scenarioWorker, dynamicTickWorker, toolbarSupport, dmpModInterface, universeSyncCache);
            optionsWindow.SetDependencies(this, networkWorker, playerColorWorker);
            this.dmpModInterface.DMPRun(networkWorker);
            this.stopEvent.Add(this.debugWindow.Stop);
            this.stopEvent.Add(this.dynamicTickWorker.Stop);
            this.stopEvent.Add(this.flagSyncer.Stop);
            this.stopEvent.Add(this.kerbalReassigner.Stop);
            this.stopEvent.Add(this.playerColorWorker.Stop);
            this.stopEvent.Add(this.playerStatusWindow.Stop);
            this.stopEvent.Add(this.playerStatusWorker.Stop);
            this.stopEvent.Add(this.partKiller.Stop);
            this.stopEvent.Add(this.scenarioWorker.Stop);
            this.stopEvent.Add(this.timeSyncer.Stop);
            this.stopEvent.Add(toolbarSupport.Stop);
            this.stopEvent.Add(optionsWindow.Stop);
            this.stopEvent.Add(this.vesselWorker.Stop);
            this.stopEvent.Add(this.warpWorker.Stop);
            this.stopEvent.Add(this.asteroidWorker.Stop);
            this.stopEvent.Add(this.vesselRangeBumper.Stop);
        }

        public void Stop()
        {
            dmpModInterface.DMPStop();
            foreach (Action stopAction in stopEvent)
            {
                try
                {
                    stopAction();
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Threw in DMPGame.Stop, exception: " + e);
                }
            }
        }
    }
}
