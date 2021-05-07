using System;
using System.Collections.Generic;
using System.IO;
using DarkMultiPlayerCommon;
using UnityEngine;

// checking vessel parts maybe for mod state sending
// ProtoVessel checkProto = checkVessel.BackupVessel();
// foreach (ProtoPartSnapshot part in checkProto.protoPartSnapshots)
// {
// }

namespace DarkMultiPlayer
{
    public class VesselWorker
    {
        public bool workerEnabled;
        //Hooks enabled
        private bool registered;
        //Update frequency
        private const float VESSEL_PROTOVESSEL_UPDATE_INTERVAL = 1f;
        //Debug delay
        public float delayTime = 0f;
        private const float UPDATE_SCREEN_MESSAGE_INTERVAL = 1f;
        private float lastDockingMessageUpdate;
        private ScreenMessage dockingMessage;
        //Incoming queue
        private object updateQueueLock = new object();
        private Dictionary<Guid, Queue<VesselRemoveEntry>> vesselRemoveQueue = new Dictionary<Guid, Queue<VesselRemoveEntry>>();
        private Dictionary<Guid, Queue<VesselProtoUpdate>> vesselProtoQueue = new Dictionary<Guid, Queue<VesselProtoUpdate>>();
        internal Dictionary<Guid, Queue<VesselUpdate>> vesselUpdateQueue = new Dictionary<Guid, Queue<VesselUpdate>>();
        internal Dictionary<Guid, Queue<VesselUpdate>> vesselUpdateMeshQueue = new Dictionary<Guid, Queue<VesselUpdate>>();
        private Dictionary<string, Queue<KerbalEntry>> kerbalProtoQueue = new Dictionary<string, Queue<KerbalEntry>>();
        private Dictionary<Guid, VesselUpdate> previousUpdates = new Dictionary<Guid, VesselUpdate>();
        //Incoming revert support
        private Dictionary<Guid, List<VesselRemoveEntry>> vesselRemoveHistory = new Dictionary<Guid, List<VesselRemoveEntry>>();
        private Dictionary<Guid, double> vesselRemoveHistoryTime = new Dictionary<Guid, double>();
        private Dictionary<Guid, List<VesselProtoUpdate>> vesselProtoHistory = new Dictionary<Guid, List<VesselProtoUpdate>>();
        private Dictionary<Guid, double> vesselProtoHistoryTime = new Dictionary<Guid, double>();
        //private Dictionary<Guid, List<VesselUpdate>> vesselUpdateHistory = new Dictionary<Guid, List<VesselUpdate>>();
        //private Dictionary<Guid, double> vesselUpdateHistoryTime = new Dictionary<Guid, double>();
        private Dictionary<string, List<KerbalEntry>> kerbalProtoHistory = new Dictionary<string, List<KerbalEntry>>();
        private Dictionary<string, double> kerbalProtoHistoryTime = new Dictionary<string, double>();
        private Dictionary<Guid, VesselCtrlUpdate> vesselControlUpdates = new Dictionary<Guid, VesselCtrlUpdate>();
        private double lastUniverseTime = double.NegativeInfinity;
        //Vessel tracking
        private Queue<ActiveVesselEntry> newActiveVessels = new Queue<ActiveVesselEntry>();
        private HashSet<Guid> serverVessels = new HashSet<Guid>();
        //Vessel state tracking
        private Guid lastVesselID;
        private Dictionary<Guid, int> vesselPartCount = new Dictionary<Guid, int>();
        private Dictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();
        private Dictionary<Guid, VesselType> vesselTypes = new Dictionary<Guid, VesselType>();
        private Dictionary<Guid, Vessel.Situations> vesselSituations = new Dictionary<Guid, Vessel.Situations>();
        //Known kerbals
        private Dictionary<string, string> serverKerbals = new Dictionary<string, string>();
        //Known vessels and last send/receive time
        private Dictionary<Guid, float> serverVesselsProtoUpdate = new Dictionary<Guid, float>();
        private Dictionary<Guid, float> serverVesselsPositionUpdate = new Dictionary<Guid, float>();
        private Dictionary<Guid, float> serverVesselsPositionUpdateMesh = new Dictionary<Guid, float>();
        private Dictionary<Guid, float> serverVesselsPositionUpdateMeshReceive = new Dictionary<Guid, float>();
        //Track when the vessel was last controlled.
        private Dictionary<Guid, double> latestVesselUpdate = new Dictionary<Guid, double>();
        private Dictionary<Guid, double> latestUpdateSent = new Dictionary<Guid, double>();
        //KillVessel tracking
        private Dictionary<Guid, double> lastKillVesselDestroy = new Dictionary<Guid, double>();
        private Dictionary<Guid, double> lastLoadVessel = new Dictionary<Guid, double>();
        private List<Vessel> delayKillVessels = new List<Vessel>();
        private List<Guid> ignoreVessels = new List<Guid>();
        //Docking related
        private Vessel newActiveVessel;
        private int activeVesselLoadUpdates;
        private Guid fromDockedVesselID;
        private Guid toDockedVesselID;
        private bool sentDockingDestroyUpdate;
        //Services
        private DMPGame dmpGame;
        private NetworkWorker networkWorker;
        private WarpWorker warpWorker;
        private TimeSyncer timeSyncer;
        private DynamicTickWorker dynamicTickWorker;
        private ConfigNodeSerializer configNodeSerializer;
        private KerbalReassigner kerbalReassigner;
        private AsteroidWorker asteroidWorker;
        private PartKiller partKiller;
        private PlayerStatusWorker playerStatusWorker;
        private PosistionStatistics posistionStatistics;
        private NamedAction fixedUpdateAction;
        private NamedAction updateAction;
        private Profiler profiler;
        private VesselRangeBumper vesselRangeBumper;
        private PlayerColorWorker playerColorWorker;

        public VesselWorker(DMPGame dmpGame, NetworkWorker networkWorker, ConfigNodeSerializer configNodeSerializer, DynamicTickWorker dynamicTickWorker, KerbalReassigner kerbalReassigner, PartKiller partKiller, PosistionStatistics posistionStatistics, Profiler profiler, VesselRangeBumper vesselRangeBumper, PlayerColorWorker playerColorWorker)
        {
            this.dmpGame = dmpGame;
            this.networkWorker = networkWorker;
            this.configNodeSerializer = configNodeSerializer;
            this.dynamicTickWorker = dynamicTickWorker;
            this.kerbalReassigner = kerbalReassigner;
            this.partKiller = partKiller;
            this.posistionStatistics = posistionStatistics;
            this.profiler = profiler;
            this.vesselRangeBumper = vesselRangeBumper;
            this.playerColorWorker = playerColorWorker;
            fixedUpdateAction = new NamedAction(FixedUpdate);
            dmpGame.fixedUpdateEvent.Add(fixedUpdateAction);
            updateAction = new NamedAction(Update);
            dmpGame.updateEvent.Add(updateAction);
        }

        public void SetDependencies(TimeSyncer timeSyncer, WarpWorker warpWorker, AsteroidWorker asteroidWorker, PlayerStatusWorker playerStatusWorker)
        {
            this.timeSyncer = timeSyncer;
            this.warpWorker = warpWorker;
            this.asteroidWorker = asteroidWorker;
            this.playerStatusWorker = playerStatusWorker;
        }

        private bool PreUpdateCheck()
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                return false;
            }

            if (Time.timeSinceLevelLoad < 1f)
            {
                return false;
            }

            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                if (!FlightGlobals.ready)
                {
                    return false;
                }
            }

            if (workerEnabled && !registered)
            {
                RegisterGameHooks();
            }
            if (!workerEnabled && registered)
            {
                UnregisterGameHooks();
            }

            //If we aren't in a DMP game don't do anything.
            if (!workerEnabled)
            {
                return false;
            }

            return true;
        }

        private void FixedUpdate()
        {
            if (!PreUpdateCheck())
            {
                return;
            }

            lock (updateQueueLock)
            {
                ProcessNewVesselMessages(true);
            }

            //Send updates of needed vessels
            SendVesselUpdates(true);
        }

        private void Update()
        {
            if (!PreUpdateCheck())
            {
                return;
            }

            //Switch to a new active vessel if needed.
            if (newActiveVessel != null)
            {
                if (FlightGlobals.fetch.vessels.Contains(newActiveVessel))
                {
                    //Hold unpack so we don't collide with the old copy
                    try
                    {
                        OrbitPhysicsManager.HoldVesselUnpack(2);
                    }
                    catch
                    {
                    }
                    //If the vessel failed to load in a reasonable time, go through the loading screen
                    if (activeVesselLoadUpdates > 100)
                    {
                        DarkLog.Debug("Active vessel must not be within load distance, go through the loading screen method");
                        activeVesselLoadUpdates = 0;
                        FlightGlobals.ForceSetActiveVessel(newActiveVessel);
                        newActiveVessel = null;
                    }
                    if (!newActiveVessel.loaded)
                    {
                        activeVesselLoadUpdates++;
                        return;
                    }
                    //Wait 10 updates maybe?
                    if (activeVesselLoadUpdates < 10)
                    {
                        activeVesselLoadUpdates++;
                        return;
                    }
                    activeVesselLoadUpdates = 0;
                    DarkLog.Debug("Switching to active vessel!");
                    FlightGlobals.ForceSetActiveVessel(newActiveVessel);
                    newActiveVessel = null;
                    return;
                }
                else
                {
                    DarkLog.Debug("switchActiveVesselOnNextUpdate Vessel failed to spawn into game!");
                    ScreenMessages.PostScreenMessage("Active vessel update failed to load into the game!", 5, ScreenMessageStyle.UPPER_CENTER);
                    HighLogic.LoadScene(GameScenes.TRACKSTATION);
                }
            }

            //Kill any queued vessels
            foreach (Vessel dyingVessel in delayKillVessels.ToArray())
            {
                if (FlightGlobals.fetch.vessels.Contains(dyingVessel) && dyingVessel.state != Vessel.State.DEAD)
                {
                    DarkLog.Debug("Delay killing " + dyingVessel.id);
                    KillVessel(dyingVessel);
                }
                else
                {
                    delayKillVessels.Remove(dyingVessel);
                }
            }

            if (fromDockedVesselID != Guid.Empty || toDockedVesselID != Guid.Empty)
            {
                HandleDocking();
            }

            lock (updateQueueLock)
            {
                ProcessNewVesselMessages(false);
            }

            //Tell other players we have taken a vessel
            UpdateActiveVesselStatus();

            //Check for vessel changes
            CheckVesselHasChanged();

            //Send updates of needed vessels
            SendVesselUpdates(true);
        }

        private void RegisterGameHooks()
        {
            registered = true;
            GameEvents.onVesselRecovered.Add(this.OnVesselRecovered);
            GameEvents.onVesselTerminated.Add(this.OnVesselTerminated);
            GameEvents.onVesselDestroy.Add(this.OnVesselDestroyed);
            GameEvents.onVesselRename.Add(this.OnVesselRenamed);
            GameEvents.onPartCouple.Add(this.OnVesselDock);
            GameEvents.onCrewBoardVessel.Add(this.OnCrewBoard);
            GameEvents.onKerbalRemoved.Add(OnKerbalRemoved);
        }

        private void UnregisterGameHooks()
        {
            registered = false;
            GameEvents.onVesselRecovered.Remove(this.OnVesselRecovered);
            GameEvents.onVesselTerminated.Remove(this.OnVesselTerminated);
            GameEvents.onVesselDestroy.Remove(this.OnVesselDestroyed);
            GameEvents.onVesselRename.Remove(this.OnVesselRenamed);
            GameEvents.onPartCouple.Remove(this.OnVesselDock);
            GameEvents.onCrewBoardVessel.Remove(this.OnCrewBoard);
            GameEvents.onKerbalRemoved.Remove(OnKerbalRemoved);
        }

        private void HandleDocking()
        {
            if (sentDockingDestroyUpdate)
            {
                //One of them will be null, the other one will be the docked craft.
                Guid dockedID = fromDockedVesselID != Guid.Empty ? fromDockedVesselID : toDockedVesselID;
                //Find the docked craft
                Vessel dockedVessel = FlightGlobals.fetch.vessels.FindLast(v => v.id == dockedID);
                if (dockedVessel != null ? !dockedVessel.packed : false)
                {
                    ProtoVessel sendProto = new ProtoVessel(dockedVessel);
                    if (sendProto != null)
                    {
                        DarkLog.Debug("Sending docked protovessel " + dockedID);
                        //Mark the vessel as sent
                        serverVesselsProtoUpdate[dockedID] = Client.realtimeSinceStartup;
                        serverVesselsPositionUpdate[dockedID] = Client.realtimeSinceStartup;
                        RegisterServerVessel(dockedID);
                        vesselPartCount[dockedID] = dockedVessel.parts.Count;
                        vesselNames[dockedID] = dockedVessel.vesselName;
                        vesselTypes[dockedID] = dockedVessel.vesselType;
                        vesselSituations[dockedID] = dockedVessel.situation;
                        //Update status if it's us.
                        if (dockedVessel == FlightGlobals.fetch.activeVessel)
                        {
                            if (lastVesselID != FlightGlobals.fetch.activeVessel.id)
                            {
                                Store.singleton.unset( "position-updater-" + lastVesselID );
                                lastVesselID = FlightGlobals.fetch.activeVessel.id;
                            }
                            Store.singleton.unset( "position-updater-" + dockedID );
                            playerStatusWorker.myPlayerStatus.vesselText = FlightGlobals.fetch.activeVessel.vesselName;
                        }
                        fromDockedVesselID = Guid.Empty;
                        toDockedVesselID = Guid.Empty;
                        sentDockingDestroyUpdate = false;

                        bool isFlyingUpdate = (sendProto.situation == Vessel.Situations.FLYING);
                        networkWorker.SendVesselProtoMessage(sendProto, true, isFlyingUpdate);
                        if (dockingMessage != null)
                        {
                            dockingMessage.duration = 0f;
                        }
                        dockingMessage = ScreenMessages.PostScreenMessage("Docked!", 3f, ScreenMessageStyle.UPPER_CENTER);
                        DarkLog.Debug("Docking event over!");
                    }
                    else
                    {
                        DarkLog.Debug("Error sending protovessel!");
                        PrintDockingInProgress();
                    }
                }
                else
                {
                    PrintDockingInProgress();
                }
            }
            else
            {
                PrintDockingInProgress();
            }
        }

        private void PrintDockingInProgress()
        {
            if ((Client.realtimeSinceStartup - lastDockingMessageUpdate) > 1f)
            {
                lastDockingMessageUpdate = Client.realtimeSinceStartup;
                if (dockingMessage != null)
                {
                    dockingMessage.duration = 0f;
                }
                dockingMessage = ScreenMessages.PostScreenMessage("Docking in progress...", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void ProcessNewVesselMessages(bool isFixedUpdate)
        {
            double interpolatorDelay = 0f;
            if (Settings.singleton.interpolatorType == InterpolatorType.INTERPOLATE1S)
            {
                interpolatorDelay = 1f;
            }
            if (Settings.singleton.interpolatorType == InterpolatorType.INTERPOLATE3S)
            {
                interpolatorDelay = 3f;
            }

            double thisPlanetTime = Planetarium.GetUniversalTime();
            double thisDelayTime = delayTime - interpolatorDelay;
            if (thisDelayTime < 0f)
            {
                thisDelayTime = 0f;
            }

            if (!isFixedUpdate)
            {
                Dictionary<Guid, double> removeList = new Dictionary<Guid, double>();
                lock (vesselRemoveQueue)
                {
                    foreach (KeyValuePair<Guid, Queue<VesselRemoveEntry>> vesselRemoveSubspace in vesselRemoveQueue)
                    {
                        while (vesselRemoveSubspace.Value.Count > 0 && ((vesselRemoveSubspace.Value.Peek().planetTime + thisDelayTime + interpolatorDelay) < thisPlanetTime))
                        {
                            VesselRemoveEntry removeVessel = vesselRemoveSubspace.Value.Dequeue();
                            RemoveVessel(removeVessel.vesselID, removeVessel.isDockingUpdate, removeVessel.dockingPlayer);
                            removeList[removeVessel.vesselID] = removeVessel.planetTime;
                        }
                    }
                }

                foreach (KeyValuePair<string, Queue<KerbalEntry>> kerbalProtoSubspace in kerbalProtoQueue)
                {
                    while (kerbalProtoSubspace.Value.Count > 0 && ((kerbalProtoSubspace.Value.Peek().planetTime + thisDelayTime + interpolatorDelay) < thisPlanetTime))
                    {
                        KerbalEntry kerbalEntry = kerbalProtoSubspace.Value.Dequeue();
                        LoadKerbal(kerbalEntry.kerbalNode);
                    }
                }

                foreach (KeyValuePair<Guid, Queue<VesselProtoUpdate>> vesselQueue in vesselProtoQueue)
                {
                    VesselProtoUpdate vpu = null;
                    //Get the latest proto update
                    bool applyDelay = vesselUpdateQueue.ContainsKey(vesselQueue.Key) && vesselUpdateQueue[vesselQueue.Key].Count > 0 && vesselUpdateQueue[vesselQueue.Key].Peek().isSurfaceUpdate;
                    double thisInterpolatorDelay = 0f;
                    if (applyDelay)
                    {
                        thisInterpolatorDelay = interpolatorDelay;
                    }
                    while (vesselQueue.Value.Count > 0)
                    {
                        if ((vesselQueue.Value.Peek().planetTime + thisDelayTime + thisInterpolatorDelay) < thisPlanetTime)
                        {
                            VesselProtoUpdate newVpu = vesselQueue.Value.Dequeue();
                            if (newVpu != null)
                            {
                                //Skip any protovessels that have been removed in the future
                                if (!removeList.ContainsKey(vesselQueue.Key) || removeList[vesselQueue.Key] < newVpu.planetTime)
                                {
                                    vpu = newVpu;
                                }
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    //Apply it if there is any
                    if (vpu != null && vpu.vesselNode != null)
                    {
                        LoadVessel(vpu.vesselNode, vpu.vesselID, false);
                    }
                }
            }

            if (isFixedUpdate)
            {
                foreach (KeyValuePair<Guid, Queue<VesselUpdate>> vesselQueue in vesselUpdateQueue)
                {
                    //If we are receiving messages from mesh vessels, do not apply normal updates, and clear them
                    if (serverVesselsPositionUpdateMeshReceive.ContainsKey(vesselQueue.Key) && (Client.realtimeSinceStartup - serverVesselsPositionUpdateMeshReceive[vesselQueue.Key] < 10f))
                    {
                        while (vesselQueue.Value.Count > 0)
                        {
                            VesselUpdate deleteVu = vesselQueue.Value.Dequeue();
                            Recycler<VesselUpdate>.ReleaseObject(deleteVu);
                        }
                        continue;
                    }
                    VesselUpdate vu = null;
                    //Get the latest position update
                    while (vesselQueue.Value.Count > 0)
                    {
                        double thisInterpolatorDelay = 0f;
                        if (vesselQueue.Value.Peek().isSurfaceUpdate)
                        {
                            thisInterpolatorDelay = interpolatorDelay;
                        }
                        if ((vesselQueue.Value.Peek().planetTime + thisDelayTime + thisInterpolatorDelay) < thisPlanetTime)
                        {
                            //More than 1 update applied in a single update, eg if we come out of timewarp.
                            if (vu != null)
                            {
                                Recycler<VesselUpdate>.ReleaseObject(vu);
                            }
                            vu = vesselQueue.Value.Dequeue();
                        }
                        else
                        {
                            break;
                        }
                    }
                    //Apply it if there is any
                    if (vu != null)
                    {
                        VesselUpdate previousUpdate = null;
                        if (previousUpdates.ContainsKey(vu.vesselID))
                        {
                            previousUpdate = previousUpdates[vu.vesselID];
                        }
                        VesselUpdate nextUpdate = null;
                        if (vesselQueue.Value.Count > 0)
                        {
                            nextUpdate = vesselQueue.Value.Peek();
                        }
                        vesselRangeBumper.ReportVesselUpdate(vu.vesselID);
                        vu.Apply(posistionStatistics, vesselControlUpdates, previousUpdate, nextUpdate);
                        if (previousUpdate != null)
                        {
                            Recycler<VesselUpdate>.ReleaseObject(previousUpdate);
                        }
                        previousUpdates[vu.vesselID] = vu;
                    }
                }

                foreach (KeyValuePair<Guid, Queue<VesselUpdate>> vesselQueue in vesselUpdateMeshQueue)
                {
                    VesselUpdate vu = null;
                    //Get the latest position update
                    while (vesselQueue.Value.Count > 0)
                    {
                        double thisInterpolatorDelay = 0f;
                        if (vesselQueue.Value.Peek().isSurfaceUpdate)
                        {
                            thisInterpolatorDelay = interpolatorDelay;
                        }
                        if ((vesselQueue.Value.Peek().planetTime + thisDelayTime + thisInterpolatorDelay) < thisPlanetTime)
                        {
                            if (vu != null)
                            {
                                Recycler<VesselUpdate>.ReleaseObject(vu);
                            }
                            vu = vesselQueue.Value.Dequeue();
                        }
                        else
                        {
                            break;
                        }
                    }
                    //Apply it if there is any
                    if (vu != null)
                    {
                        VesselUpdate previousUpdate = null;
                        if (previousUpdates.ContainsKey(vu.vesselID))
                        {
                            previousUpdate = previousUpdates[vu.vesselID];
                        }
                        VesselUpdate nextUpdate = null;
                        if (vesselQueue.Value.Count > 0)
                        {
                            nextUpdate = vesselQueue.Value.Peek();
                        }
                        vu.Apply(posistionStatistics, vesselControlUpdates, previousUpdate, nextUpdate);
                        if (previousUpdate != null)
                        {
                            Recycler<VesselUpdate>.ReleaseObject(previousUpdate);
                        }
                        previousUpdates[vu.vesselID] = vu;
                    }
                }
            }
        }

        public void DetectReverting()
        {
            double newUniverseTime = Planetarium.GetUniversalTime();
            //10 second fudge to ignore TimeSyncer skips
            if (newUniverseTime < (lastUniverseTime - 10f))
            {
                int updatesReverted = 0;
                DarkLog.Debug("Revert detected!");
                timeSyncer.UnlockSubspace();
                if (!Settings.singleton.revertEnabled)
                {
                    DarkLog.Debug("Unsafe revert detected!");
                    ScreenMessages.PostScreenMessage("Unsafe revert detected!", 5f, ScreenMessageStyle.UPPER_CENTER);
                }
                else
                {
                    kerbalProtoQueue.Clear();
                    vesselProtoQueue.Clear();
                    vesselUpdateQueue.Clear();
                    vesselUpdateMeshQueue.Clear();
                    vesselRemoveQueue.Clear();
                    //Kerbal queue
                    KerbalEntry lastKerbalEntry = null;
                    foreach (KeyValuePair<string, List<KerbalEntry>> kvp in kerbalProtoHistory)
                    {
                        bool adding = false;
                        kerbalProtoQueue.Add(kvp.Key, new Queue<KerbalEntry>());
                        foreach (KerbalEntry ke in kvp.Value)
                        {
                            if (ke.planetTime > newUniverseTime)
                            {
                                if (!adding)
                                {
                                    //One shot - add the previous update before the time to apply instantly
                                    if (lastKerbalEntry != null)
                                    {
                                        kerbalProtoQueue[kvp.Key].Enqueue(lastKerbalEntry);
                                        updatesReverted++;
                                    }
                                }
                                adding = true;
                            }
                            if (adding)
                            {
                                kerbalProtoQueue[kvp.Key].Enqueue(ke);
                                updatesReverted++;
                            }
                            lastKerbalEntry = ke;
                        }
                    }
                    //Vessel proto queue
                    VesselProtoUpdate lastVesselProtoEntry = null;
                    foreach (KeyValuePair<Guid, List<VesselProtoUpdate>> kvp in vesselProtoHistory)
                    {
                        bool adding = false;
                        vesselProtoQueue.Add(kvp.Key, new Queue<VesselProtoUpdate>());
                        foreach (VesselProtoUpdate vpu in kvp.Value)
                        {
                            if (vpu.planetTime > newUniverseTime)
                            {
                                if (!adding)
                                {
                                    //One shot - add the previous update before the time to apply instantly
                                    if (lastVesselProtoEntry != null)
                                    {
                                        vesselProtoQueue[kvp.Key].Enqueue(lastVesselProtoEntry);
                                        updatesReverted++;
                                    }
                                }
                                adding = true;
                            }
                            if (adding)
                            {
                                vesselProtoQueue[kvp.Key].Enqueue(vpu);
                                updatesReverted++;
                            }
                            lastVesselProtoEntry = vpu;
                        }
                    }
                    //Vessel update queue
                    /*
                    VesselUpdate lastVesselUpdateEntry = null;
                    foreach (KeyValuePair<Guid, List<VesselUpdate>> kvp in vesselUpdateHistory)
                    {
                        bool adding = false;
                        vesselUpdateQueue.Add(kvp.Key, new Queue<VesselUpdate>());
                        foreach (VesselUpdate vu in kvp.Value)
                        {
                            if (vu.planetTime > newUniverseTime)
                            {
                                if (!adding)
                                {
                                    //One shot - add the previous update before the time to apply instantly
                                    if (lastVesselUpdateEntry != null)
                                    {
                                        vesselUpdateQueue[kvp.Key].Enqueue(lastVesselUpdateEntry);
                                        updatesReverted++;
                                    }
                                }
                                adding = true;
                            }
                            if (adding)
                            {
                                vesselUpdateQueue[kvp.Key].Enqueue(vu);
                                updatesReverted++;
                            }
                            lastVesselUpdateEntry = vu;
                        }
                    }
                    */
                    //Remove entries
                    VesselRemoveEntry lastRemoveEntry = null;
                    foreach (KeyValuePair<Guid, List<VesselRemoveEntry>> kvp in vesselRemoveHistory)
                    {
                        bool adding = false;
                        vesselRemoveQueue.Add(kvp.Key, new Queue<VesselRemoveEntry>());
                        foreach (VesselRemoveEntry vre in kvp.Value)
                        {
                            if (vre.planetTime > newUniverseTime)
                            {
                                if (!adding)
                                {
                                    //One shot - add the previous update before the time to apply instantly
                                    if (lastRemoveEntry != null)
                                    {
                                        vesselRemoveQueue[kvp.Key].Enqueue(lastRemoveEntry);
                                        updatesReverted++;
                                    }
                                }
                                adding = true;
                            }
                            if (adding)
                            {
                                vesselRemoveQueue[kvp.Key].Enqueue(vre);
                                updatesReverted++;
                            }
                            lastRemoveEntry = vre;
                        }
                    }
                }
                DarkLog.Debug("Reverted " + updatesReverted + " updates");
                ScreenMessages.PostScreenMessage("Reverted " + updatesReverted + " updates", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
            lastUniverseTime = newUniverseTime;
        }

        private void UpdateActiveVesselStatus()
        {
            bool isActiveVesselOk = FlightGlobals.fetch.activeVessel != null ? (FlightGlobals.fetch.activeVessel.loaded && !FlightGlobals.fetch.activeVessel.packed) : false;

            if (HighLogic.LoadedScene == GameScenes.FLIGHT && isActiveVesselOk)
            {
                //When we change vessel, send the previous flown vessel as soon as possible.
                if (lastVesselID != FlightGlobals.fetch.activeVessel.id)
                {
                    if (lastVesselID != Guid.Empty)
                    {
                        DarkLog.Debug("Resetting last send time for " + lastVesselID);
                        serverVesselsProtoUpdate[lastVesselID] = 0f;
                        Store.singleton.unset( "position-updater-" + lastVesselID );
                    }

                    //Reset the send time of the vessel we just switched to
                    serverVesselsProtoUpdate[FlightGlobals.fetch.activeVessel.id] = 0f;

                    //Nobody else is updating the vessel, we'll take it
                    Store.singleton.set( "position-updater-" + FlightGlobals.fetch.activeVessel.id, Settings.singleton.playerName );

                    playerStatusWorker.myPlayerStatus.vesselText = FlightGlobals.fetch.activeVessel.vesselName;
                    lastVesselID = FlightGlobals.fetch.activeVessel.id;
                }
            }
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                //Release the vessel if we aren't in flight anymore.
                if (lastVesselID != Guid.Empty)
                {
                    Store.singleton.unset( "position-updater-" + lastVesselID );
                    lastVesselID = Guid.Empty;
                    playerStatusWorker.myPlayerStatus.vesselText = "";
                }
            }
        }

        private void CheckVesselHasChanged()
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.fetch.activeVessel != null)
            {
                //Check all vessel for part count changes
                foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
                {
                    if (checkVessel.loaded && !checkVessel.packed)
                    {
                        bool partCountChanged = vesselPartCount.ContainsKey(checkVessel.id) ? checkVessel.parts.Count != vesselPartCount[checkVessel.id] : true;

                        if (partCountChanged)
                        {
                            serverVesselsProtoUpdate[checkVessel.id] = 0f;
                            vesselPartCount[checkVessel.id] = checkVessel.parts.Count;
                        }
                        //Add entries to dictionaries if needed
                        if (!vesselNames.ContainsKey(checkVessel.id))
                        {
                            vesselNames.Add(checkVessel.id, checkVessel.vesselName);
                        }
                        if (!vesselTypes.ContainsKey(checkVessel.id))
                        {
                            vesselTypes.Add(checkVessel.id, checkVessel.vesselType);
                        }
                        if (!vesselSituations.ContainsKey(checkVessel.id))
                        {
                            vesselSituations.Add(checkVessel.id, checkVessel.situation);
                        }
                        //Check active vessel for situation/renames. Throttle send to 10 seconds.
                        bool vesselNotRecentlyUpdated = serverVesselsPositionUpdate.ContainsKey(checkVessel.id) ? ((Client.realtimeSinceStartup - serverVesselsProtoUpdate[checkVessel.id]) > 10f) : true;
                        bool recentlyLanded = vesselSituations[checkVessel.id] != Vessel.Situations.LANDED && checkVessel.situation == Vessel.Situations.LANDED;
                        bool recentlySplashed = vesselSituations[checkVessel.id] != Vessel.Situations.SPLASHED && checkVessel.situation == Vessel.Situations.SPLASHED;
                        if (vesselNotRecentlyUpdated || recentlyLanded || recentlySplashed)
                        {
                            bool nameChanged = (vesselNames[checkVessel.id] != checkVessel.vesselName);
                            bool typeChanged = (vesselTypes[checkVessel.id] != checkVessel.vesselType);
                            bool situationChanged = (vesselSituations[checkVessel.id] != checkVessel.situation);
                            if (nameChanged || typeChanged || situationChanged)
                            {
                                vesselNames[checkVessel.id] = checkVessel.vesselName;
                                vesselTypes[checkVessel.id] = checkVessel.vesselType;
                                vesselSituations[checkVessel.id] = checkVessel.situation;
                                serverVesselsProtoUpdate[checkVessel.id] = 0f;
                            }
                        }
                    }
                }
            }
        }

        private bool PreSendVesselUpdatesCheck()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                //We aren't in flight so we have nothing to send
                return false;
            }
            if (FlightGlobals.fetch.activeVessel == null)
            {
                //We don't have an active vessel
                return false;
            }
            if (!FlightGlobals.fetch.activeVessel.loaded || FlightGlobals.fetch.activeVessel.packed)
            {
                //We haven't loaded into the game yet
                return false;
            }
            if (fromDockedVesselID != Guid.Empty || toDockedVesselID != Guid.Empty)
            {
                //Don't send updates while docking
                return false;
            }
            return true;
        }

        SortedList<double, Vessel> secondryVessels = new SortedList<double, Vessel>();
        private void SendVesselUpdates(bool sendProtoUpdates)
        {
            if (!PreSendVesselUpdatesCheck())
            {
                return;
            }

            SendVesselUpdateIfNeeded(FlightGlobals.fetch.activeVessel, sendProtoUpdates);

            secondryVessels.Clear();
            foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
            {
                if (ignoreVessels.Contains(checkVessel.id))
                {
                    continue;
                }

                //Only update the vessel if it's loaded and unpacked (not on rails). Skip our vessel.
                if (checkVessel.loaded && !checkVessel.packed && (checkVessel.id != FlightGlobals.fetch.activeVessel.id) && (checkVessel.state != Vessel.State.DEAD))
                {
                    double currentDistance = Vector3d.Distance(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), checkVessel.GetWorldPos3D());
                    //If there's 2 vessels at the exact same distance.
                    if (!secondryVessels.ContainsKey(currentDistance) && !secondryVessels.ContainsValue(checkVessel))
                    {
                        secondryVessels.Add(currentDistance, checkVessel);
                    }
                }
            }
            int currentSend = 0;
            foreach (KeyValuePair<double, Vessel> secondryVessel in secondryVessels)
            {
                currentSend++;
                if (currentSend > dynamicTickWorker.maxSecondryVesselsPerTick)
                {
                    break;
                }
                SendVesselUpdateIfNeeded(secondryVessel.Value, sendProtoUpdates);
            }
        }

        public void SendVesselUpdateIfNeeded(Vessel checkVessel, bool sendProtoUpdates)
        {
            if (checkVessel.state == Vessel.State.DEAD)
            {
                //Don't send dead vessels
                return;
            }

            if (checkVessel.vesselType == VesselType.Flag && checkVessel.id == Guid.Empty && checkVessel.vesselName != "Flag")
            {
                DarkLog.Debug("Fixing flag GUID for " + checkVessel.vesselName);
                checkVessel.id = Guid.NewGuid();
            }

            bool notRecentlySentProtoUpdate = !serverVesselsProtoUpdate.ContainsKey(checkVessel.id) || ((Client.realtimeSinceStartup - serverVesselsProtoUpdate[checkVessel.id]) > VESSEL_PROTOVESSEL_UPDATE_INTERVAL);
            bool notRecentlySentPositionUpdate = !serverVesselsPositionUpdate.ContainsKey(checkVessel.id) || ((Client.realtimeSinceStartup - serverVesselsPositionUpdate[checkVessel.id]) > (1f / DynamicTickWorker.SEND_TICK_RATE));

            bool notRecentlySentPositionUpdateForMesh = !serverVesselsPositionUpdateMesh.ContainsKey(checkVessel.id) || ((Client.realtimeSinceStartup - serverVesselsPositionUpdateMesh[checkVessel.id]) > .02f); // 100Hz
            bool sendNormal = false;
            bool sendMesh = false;

            string position_updater = Store.singleton.get( "position-updater-" + checkVessel.id );

            //Check that is hasn't been recently sent
            if (notRecentlySentProtoUpdate && sendProtoUpdates)
            {
                if (checkVessel.id != Guid.Empty)
                {
                    ProtoVessel checkProto = checkVessel.BackupVessel();
                    //TODO: Fix sending of flying vessels.
                    if (checkProto != null)
                    {
                        if (checkProto.vesselID != Guid.Empty)
                        {
                            //Also check for kerbal state changes
                            foreach (ProtoPartSnapshot part in checkProto.protoPartSnapshots)
                            {
                                foreach (ProtoCrewMember pcm in part.protoModuleCrew)
                                {
                                    SendKerbalIfDifferent(pcm);
                                }
                            }
                            RegisterServerVessel(checkProto.vesselID);
                            //Mark the update as sent
                            serverVesselsProtoUpdate[checkVessel.id] = Client.realtimeSinceStartup;
                            //Also delay the position send
                            serverVesselsPositionUpdate[checkVessel.id] = Client.realtimeSinceStartup;
                            latestUpdateSent[checkVessel.id] = Client.realtimeSinceStartup;
                            bool isFlyingUpdate = (checkProto.situation == Vessel.Situations.FLYING);
                            networkWorker.SendVesselProtoMessage(checkProto, false, isFlyingUpdate);
                        }
                        else
                        {
                            DarkLog.Debug(checkVessel.vesselName + "(proto) does not have a guid!");
                        }
                    }
                }
                else
                {
                    DarkLog.Debug(checkVessel.vesselName + "(vessel) does not have a guid!");
                }
            }

            if ( position_updater == Settings.singleton.playerName && notRecentlySentPositionUpdate && !sendProtoUpdates && checkVessel.vesselType != VesselType.Flag)
            {
                //Send a position update - Except for flags. They aren't exactly known for their mobility.
                serverVesselsPositionUpdate[checkVessel.id] = Client.realtimeSinceStartup;
                latestUpdateSent[checkVessel.id] = Client.realtimeSinceStartup;
                sendNormal = true;
            }
            //Mesh send
            List<string> clientsInSubspace = null;
            if ( position_updater == Settings.singleton.playerName && notRecentlySentPositionUpdateForMesh && !sendProtoUpdates && checkVessel.vesselType != VesselType.Flag)
            {
                //Send a position update - Except for flags. They aren't exactly known for their mobility.
                serverVesselsPositionUpdateMesh[checkVessel.id] = Client.realtimeSinceStartup;
                clientsInSubspace = warpWorker.GetClientsInSubspace(timeSyncer.currentSubspace);
                if (clientsInSubspace.Count > 1)
                {
                    sendMesh = true;
                }
            }
            if (sendNormal || sendMesh)
            {
                VesselUpdate update = Recycler<VesselUpdate>.GetObject();
                update.SetVesselWorker(this);
                update.CopyFromVessel(checkVessel);
                if (update.updateOK)
                {
                    if (sendNormal)
                    {
                        networkWorker.SendVesselUpdate(update);
                    }
                    if (sendMesh)
                    {
                        networkWorker.SendVesselUpdateMesh(update, clientsInSubspace);
                    }
                }
                Recycler<VesselUpdate>.ReleaseObject(update);
            }
        }

        public void SendKerbalIfDifferent(ProtoCrewMember pcm)
        {
            ConfigNode kerbalNode = new ConfigNode();
            pcm.Save(kerbalNode);
            if (pcm.type == ProtoCrewMember.KerbalType.Tourist || pcm.type == ProtoCrewMember.KerbalType.Unowned)
            {
                ConfigNode dmpNode = new ConfigNode();
                dmpNode.AddValue("contractOwner", Settings.singleton.playerPublicKey);
                kerbalNode.AddNode("DarkMultiPlayer", dmpNode);
            }
            ByteArray kerbalBytes = configNodeSerializer.Serialize(kerbalNode);
            if (kerbalBytes == null || kerbalBytes.Length == 0)
            {
                DarkLog.Debug("VesselWorker: Error sending kerbal - bytes are null or 0");
                return;
            }
            string kerbalHash = Common.CalculateSHA256Hash(kerbalBytes);
            bool kerbalDifferent = false;
            if (!serverKerbals.ContainsKey(pcm.name))
            {
                //New kerbal
                DarkLog.Debug("Found new kerbal, sending...");
                kerbalDifferent = true;
            }
            else if (serverKerbals[pcm.name] != kerbalHash)
            {
                DarkLog.Debug("Found changed kerbal (" + pcm.name + "), sending...");
                kerbalDifferent = true;
            }
            if (kerbalDifferent)
            {
                serverKerbals[pcm.name] = kerbalHash;
                networkWorker.SendKerbalProtoMessage(pcm.name, kerbalBytes);
            }
            ByteRecycler.ReleaseObject(kerbalBytes);
        }

        public void SendKerbalRemove(string kerbalName)
        {
            if (serverKerbals.ContainsKey(kerbalName))
            {
                DarkLog.Debug("Found kerbal " + kerbalName + ", sending remove...");
                serverKerbals.Remove(kerbalName);
                networkWorker.SendKerbalRemove(kerbalName);
            }
        }
        //Also called from PlayerStatusWorker

        //Called from main
        public void LoadKerbalsIntoGame()
        {
            DarkLog.Debug("Loading kerbals into game");

            foreach (KeyValuePair<string, Queue<KerbalEntry>> kerbalQueue in kerbalProtoQueue)
            {
                while (kerbalQueue.Value.Count > 0)
                {
                    KerbalEntry kerbalEntry = kerbalQueue.Value.Dequeue();
                    LoadKerbal(kerbalEntry.kerbalNode);
                }
            }

            if (serverKerbals.Count == 0)
            {
                KerbalRoster newRoster = KerbalRoster.GenerateInitialCrewRoster(HighLogic.CurrentGame.Mode);
                foreach (ProtoCrewMember pcm in newRoster.Crew)
                    SendKerbalIfDifferent(pcm);
            }

            int generateKerbals = 0;
            if (serverKerbals.Count < 20)
            {
                generateKerbals = 20 - serverKerbals.Count;
                DarkLog.Debug("Generating " + generateKerbals + " new kerbals");
            }

            while (generateKerbals > 0)
            {
                ProtoCrewMember protoKerbal = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);
                SendKerbalIfDifferent(protoKerbal);
                generateKerbals--;
            }
            DarkLog.Debug("Kerbals loaded");
        }

        private void LoadKerbal(ConfigNode crewNode)
        {
            if (crewNode == null)
            {
                DarkLog.Debug("crewNode is null!");
                return;
            }

            if (crewNode.GetValue("type") == "Tourist")
            {
                ConfigNode dmpNode = null;
                if (crewNode.TryGetNode("DarkMultiPlayer", ref dmpNode))
                {
                    string dmpOwner = null;
                    if (dmpNode.TryGetValue("contractOwner", ref dmpOwner))
                    {
                        // if (dmpOwner != Settings.singleton.playerPublicKey)
                        // {
                        //     DarkLog.Debug("Skipping load of tourist that belongs to another player");
                        //     return;
                        // }
                    }
                }
            }

            ProtoCrewMember protoCrew = null;
            string kerbalName = null;
            //Debugging for damaged kerbal bug
            try
            {
                kerbalName = crewNode.GetValue("name");
                protoCrew = new ProtoCrewMember(HighLogic.CurrentGame.Mode, crewNode);
            }
            catch
            {
                DarkLog.Debug("protoCrew creation failed for " + crewNode.GetValue("name") + " (damaged kerbal type 1)");

            }
            if (protoCrew == null)
            {
                DarkLog.Debug("protoCrew is null!");
                return;
            }
            if (string.IsNullOrEmpty(protoCrew.name))
            {
                DarkLog.Debug("protoName is blank!");
                return;
            }
            if (!HighLogic.CurrentGame.CrewRoster.Exists(protoCrew.name))
            {
                HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoCrew);
                ConfigNode kerbalNode = new ConfigNode();
                protoCrew.Save(kerbalNode);
                ByteArray kerbalBytes = configNodeSerializer.Serialize(kerbalNode);
                if (kerbalBytes != null && kerbalBytes.Length != 0)
                {
                    serverKerbals[protoCrew.name] = Common.CalculateSHA256Hash(kerbalBytes);
                }
                ByteRecycler.ReleaseObject(kerbalBytes);
            }
            else
            {
                ConfigNode careerLogNode = crewNode.GetNode("CAREER_LOG");
                if (careerLogNode != null)
                {
                    //Insert wolf howling at the moon here
                    HighLogic.CurrentGame.CrewRoster[protoCrew.name].careerLog.Entries.Clear();
                    HighLogic.CurrentGame.CrewRoster[protoCrew.name].careerLog.Load(careerLogNode);
                }
                else
                {
                    DarkLog.Debug("Career log node for " + protoCrew.name + " is empty!");
                }

                ConfigNode flightLogNode = crewNode.GetNode("FLIGHT_LOG");
                if (flightLogNode != null)
                {
                    //And here. Someone "cannot into" lists and how to protect them.
                    HighLogic.CurrentGame.CrewRoster[protoCrew.name].flightLog.Entries.Clear();
                    HighLogic.CurrentGame.CrewRoster[protoCrew.name].flightLog.Load(flightLogNode);
                }

                HighLogic.CurrentGame.CrewRoster[protoCrew.name].courage = protoCrew.courage;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].experience = protoCrew.experience;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].experienceLevel = protoCrew.experienceLevel;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].experienceTrait = protoCrew.experienceTrait;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].gender = protoCrew.gender;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].gExperienced = protoCrew.gExperienced;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].hasToured = protoCrew.hasToured;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].isBadass = protoCrew.isBadass;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].KerbalRef = protoCrew.KerbalRef;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].outDueToG = protoCrew.outDueToG;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].rosterStatus = protoCrew.rosterStatus;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].seat = protoCrew.seat;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].seatIdx = protoCrew.seatIdx;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].stupidity = protoCrew.stupidity;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].trait = protoCrew.trait;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].type = protoCrew.type;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].UTaR = protoCrew.UTaR;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].veteran = protoCrew.veteran;
            }
        }
        //Called from main
        public void LoadVesselsIntoGame()
        {
            lock (updateQueueLock)
            {
                DarkLog.Debug("Loading vessels into game");
                int numberOfLoads = 0;

                foreach (KeyValuePair<Guid, Queue<VesselProtoUpdate>> vesselQueue in vesselProtoQueue)
                {
                    while (vesselQueue.Value.Count > 0)
                    {
                        VesselProtoUpdate vpu = vesselQueue.Value.Dequeue();

                        ProtoVessel pv = CreateSafeProtoVesselFromConfigNode(vpu.vesselNode, vpu.vesselID);
                        if (pv != null && pv.vesselID == vpu.vesselID)
                        {
                            RegisterServerVessel(pv.vesselID);
                            RegisterServerAsteriodIfVesselIsAsteroid(pv);
                            HighLogic.CurrentGame.flightState.protoVessels.Add(pv);
                            numberOfLoads++;
                        }
                        else
                        {
                            DarkLog.Debug("WARNING: Protovessel " + vpu.vesselID + " is DAMAGED!. Skipping load.");
                        }
                    }
                }
                DarkLog.Debug("Vessels (" + numberOfLoads + ") loaded into game");
            }
        }

        //Also called from QuickSaveLoader
        public void LoadVessel(ConfigNode vesselNode, Guid protovesselID, bool ignoreFlyingKill)
        {
            Vessel oldVessel = FlightGlobals.fetch.vessels.Find(v => v.id == protovesselID);

            if (vesselNode == null)
            {
                DarkLog.Debug("vesselNode is null!");
                return;
            }

            //Free up the persistent ID's so they don't get reassigned...
            if (oldVessel != null)
            {
                FlightGlobals.RemoveVessel(oldVessel);
            }
            //Load the proto
            ProtoVessel currentProto = CreateSafeProtoVesselFromConfigNode(vesselNode, protovesselID);
            //Bring it back
            if (oldVessel != null)
            {
                FlightGlobals.AddVessel(oldVessel);
            }

            if (currentProto == null)
            {
                DarkLog.Debug("protoVessel is null!");
                return;
            }

            //Skip already loaded EVA's
            if ((currentProto.vesselType == VesselType.EVA) && (oldVessel != null))
            {
                return;
            }

            //Skip flying vessel that are too far away
            if (currentProto.situation == Vessel.Situations.FLYING)
            {
                DarkLog.Debug("Got a flying update for " + currentProto.vesselID + ", name: " + currentProto.vesselName);
                if (!HighLogic.LoadedSceneIsFlight)
                {
                    //Skip hackyload if we aren't in flight.
                    DarkLog.Debug("Skipping flying vessel load - We are not in flight");
                    return;
                }
                if (currentProto.orbitSnapShot == null)
                {
                    DarkLog.Debug("Skipping flying vessel load - Protovessel does not have an orbit snapshot");
                    return;
                }
                if (FlightGlobals.fetch.activeVessel == null)
                {
                    DarkLog.Debug("Skipping flying vessel load - active vessel is null");
                    return;
                }
                Orbit protoOrbit = currentProto.orbitSnapShot.Load();
                Vector3d protoPos = protoOrbit.getTruePositionAtUT(Planetarium.GetUniversalTime());
                Vector3d ourPos = FlightGlobals.fetch.activeVessel.GetWorldPos3D();
                double flyingDistance = Vector3d.Distance(protoPos, ourPos);
                if (flyingDistance > VesselRangeBumper.BUMP_FLYING_LOAD_DISTANCE)
                {
                    //Vessel is out of range but we can still spawn it if it's above the kill limit.
                    CelestialBody updateBody = FlightGlobals.fetch.bodies[currentProto.orbitSnapShot.ReferenceBodyIndex];
                    if (updateBody == null)
                    {
                        DarkLog.Debug("Skipping flying vessel load - Could not find celestial body index " + currentProto.orbitSnapShot.ReferenceBodyIndex);
                        return;
                    }
                    if (updateBody.atmosphere)
                    {
                        double atmoPressure = updateBody.GetPressure(currentProto.altitude);
                        DarkLog.Debug("Proto load is: " + currentProto.altitude + " alt high, pressure: " + atmoPressure);
                        //KSP magic cut off limit for killing vessels. Works out to be ~23km on kerbin.
                        if (atmoPressure > 1f)
                        {
                            DarkLog.Debug("Skipping flying vessel load - Vessel will get killed in the atmosphere");
                            return;
                        }
                    }
                }
            }

            RegisterServerAsteriodIfVesselIsAsteroid(currentProto);
            RegisterServerVessel(currentProto.vesselID);
            DarkLog.Debug("Loading " + currentProto.vesselID + ", name: " + currentProto.vesselName + ", type: " + currentProto.vesselType);

            bool wasActive = false;
            bool wasTarget = false;

            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                if (FlightGlobals.fetch.VesselTarget != null && FlightGlobals.fetch.VesselTarget.GetVessel() != null)
                {
                    wasTarget = FlightGlobals.fetch.VesselTarget.GetVessel().id == currentProto.vesselID;
                }
                if (wasTarget)
                {
                    DarkLog.Debug("ProtoVessel update for target vessel!");
                }
                wasActive = (FlightGlobals.fetch.activeVessel != null) && (FlightGlobals.fetch.activeVessel.id == currentProto.vesselID);
            }

            bool killedLoadedVessel = false;

            if (oldVessel != null)
            {
                //Don't replace the vessel if it's unpacked, not landed, close to the ground, and has the same amount of parts.
                double hft = oldVessel.GetHeightFromTerrain();
                if (oldVessel.loaded && !oldVessel.packed && !oldVessel.Landed && (hft > 0) && (hft < 1000) && (currentProto.protoPartSnapshots.Count == oldVessel.parts.Count))
                {
                    DarkLog.Debug("Skipped loading protovessel " + currentProto.vesselID + " because it is flying close to the ground and may get destroyed");
                    return;
                }
                //Don't kill the active vessel - Kill it after we switch.
                //Killing the active vessel causes all sorts of crazy problems.

                if (wasActive)
                {
                    delayKillVessels.Add(oldVessel);
                }
                else
                {
                    if (oldVessel.loaded)
                    {
                        killedLoadedVessel = true;
                    }
                    KillVessel(oldVessel);
                }
            }

            vesselPartCount[currentProto.vesselID] = currentProto.protoPartSnapshots.Count;
            serverVesselsProtoUpdate[currentProto.vesselID] = Client.realtimeSinceStartup;
            lastLoadVessel[currentProto.vesselID] = Client.realtimeSinceStartup;

            currentProto.Load(HighLogic.CurrentGame.flightState);

            if (currentProto.vesselRef == null)
            {
                DarkLog.Debug("Protovessel " + currentProto.vesselID + " failed to create a vessel!");
                return;
            }
            else
            {
                if (killedLoadedVessel)
                {
                    currentProto.vesselRef.Load();
                }
            }
            if (wasActive)
            {
                DarkLog.Debug("ProtoVessel update for active vessel!");
                try
                {
                    OrbitPhysicsManager.HoldVesselUnpack(5);
                    FlightGlobals.fetch.activeVessel.GoOnRails();
                    //Put our vessel on rails so we don't collide with the new copy
                }
                catch
                {
                    DarkLog.Debug("WARNING: Something very bad happened trying to replace the vessel, skipping update!");
                    return;
                }
                newActiveVessel = currentProto.vesselRef;
            }
            if (wasTarget)
            {
                DarkLog.Debug("Set docking target");
                FlightGlobals.fetch.SetVesselTarget(currentProto.vesselRef);
            }
            vesselRangeBumper.SetVesselRanges(currentProto.vesselRef);
            playerColorWorker.DetectNewVessel(currentProto.vesselRef);
            DarkLog.Debug("Protovessel Loaded");
        }

        private ProtoVessel CreateSafeProtoVesselFromConfigNode(ConfigNode inputNode, Guid protovesselID)
        {
            ProtoVessel pv = null;
            try
            {
                DodgeVesselActionGroups(inputNode);
                DodgeVesselLandedStatus(inputNode);
                kerbalReassigner.DodgeKerbals(inputNode, protovesselID);
                pv = new ProtoVessel(inputNode, HighLogic.CurrentGame);
                ConfigNode cn = new ConfigNode();
                pv.Save(cn);
                //List<string> partsList = null;
                PartResourceLibrary partResourceLibrary = PartResourceLibrary.Instance;
                foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
                {
                    if (pps.partInfo == null)
                    {
                        DarkLog.Debug("WARNING: Protovessel " + protovesselID + " (" + pv.vesselName + ") contains the missing part '" + pps.partName + "'!. Skipping load.");
                        ScreenMessages.PostScreenMessage("Cannot load '" + pv.vesselName + "' - you are missing " + pps.partName, 10f, ScreenMessageStyle.UPPER_CENTER);
                        pv = null;
                        break;
                    }
                    foreach (ProtoPartResourceSnapshot resource in pps.resources)
                    {
                        if (!partResourceLibrary.resourceDefinitions.Contains(resource.resourceName))
                        {
                            DarkLog.Debug("WARNING: Protovessel " + protovesselID + " (" + pv.vesselName + ") contains the missing resource '" + resource.resourceName + "'!. Skipping load.");
                            ScreenMessages.PostScreenMessage("Cannot load '" + pv.vesselName + "' - you are missing the resource " + resource.resourceName, 10f, ScreenMessageStyle.UPPER_CENTER);
                            pv = null;
                            break;
                        }
                    }
                    //Fix up flag URLS.
                    if (pps.flagURL.Length != 0)
                    {
                        string flagFile = Path.Combine(Client.dmpClient.gameDataDir, pps.flagURL + ".png");
                        if (!File.Exists(flagFile))
                        {
                            DarkLog.Debug("Flag '" + pps.flagURL + "' doesn't exist, setting to default!");
                            pps.flagURL = "Squad/Flags/default";
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Damaged vessel " + protovesselID + ", exception: " + e);
                pv = null;
            }
            return pv;
        }

        private void RegisterServerAsteriodIfVesselIsAsteroid(ProtoVessel possibleAsteroid)
        {
            //Register asteroids from other players
            if (possibleAsteroid.vesselType == VesselType.SpaceObject)
            {
                if (possibleAsteroid.protoPartSnapshots != null)
                {
                    if (possibleAsteroid.protoPartSnapshots.Count == 1)
                    {
                        if (possibleAsteroid.protoPartSnapshots[0].partName == "PotatoRoid")
                        {
                            //Noise. Ugh.
                            //DarkLog.Debug("Registering remote server asteroid");
                            asteroidWorker.RegisterServerAsteroid(possibleAsteroid.vesselID);
                        }
                    }
                }
            }
        }

        private void DodgeVesselActionGroups(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                ConfigNode actiongroupNode = vesselNode.GetNode("ACTIONGROUPS");
                if (actiongroupNode != null)
                {
                    foreach (string keyName in actiongroupNode.values.DistinctNames())
                    {
                        string valueCurrent = actiongroupNode.GetValue(keyName);
                        string valueDodge = DodgeValueIfNeeded(valueCurrent);
                        if (valueCurrent != valueDodge)
                        {
                            DarkLog.Debug("Dodged actiongroup " + keyName);
                            actiongroupNode.SetValue(keyName, valueDodge);
                        }
                    }
                }
            }
        }

        private void DodgeVesselLandedStatus(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                string situation = vesselNode.GetValue("sit");
                switch (situation)
                {
                    case "LANDED":
                        vesselNode.SetValue("landed", "True");
                        vesselNode.SetValue("splashed", "False");
                        break;
                    case "SPLASHED":
                        vesselNode.SetValue("splashed", "True");
                        vesselNode.SetValue("landed", "False");
                        break;
                }
            }
        }

        private void RemoveManeuverNodesFromProtoVessel(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                ConfigNode flightPlanNode = vesselNode.GetNode("FLIGHTPLAN");
                if (flightPlanNode != null)
                {
                    flightPlanNode.ClearData();
                }
            }
        }

        private void FixVesselManeuverNodes(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                ConfigNode flightPlanNode = vesselNode.GetNode("FLIGHTPLAN");
                List<ConfigNode> expiredManeuverNodes = new List<ConfigNode>();
                if (flightPlanNode != null)
                {
                    foreach (ConfigNode maneuverNode in flightPlanNode.GetNodes("MANEUVER"))
                    {
                        double maneuverUT = double.Parse(maneuverNode.GetValue("UT"));
                        double currentTime = Planetarium.GetUniversalTime();
                        if (currentTime > maneuverUT)
                            expiredManeuverNodes.Add(maneuverNode);
                    }

                    if (expiredManeuverNodes.Count != 0)
                    {
                        foreach (ConfigNode removeNode in expiredManeuverNodes)
                        {
                            DarkLog.Debug("Removed maneuver node from vessel, it was expired!");
                            flightPlanNode.RemoveNode(removeNode);
                        }
                    }

                }
            }
        }

        private string DodgeValueIfNeeded(string input)
        {
            string boolValue = input.Substring(0, input.IndexOf(", ", StringComparison.Ordinal));
            string timeValue = input.Substring(input.IndexOf(", ", StringComparison.Ordinal) + 1);
            double vesselPlanetTime = Double.Parse(timeValue);
            double currentPlanetTime = Planetarium.GetUniversalTime();
            if (vesselPlanetTime > currentPlanetTime)
            {
                return boolValue + ", " + currentPlanetTime;
            }
            return input;
        }

        public void OnVesselRenamed(GameEvents.HostedFromToAction<Vessel, string> eventData)
        {
            Vessel renamedVessel = eventData.host;
            string toName = eventData.to;
            DarkLog.Debug("Sending vessel [" + renamedVessel.name + "] renamed to [" + toName + "]");
            SendVesselUpdateIfNeeded(renamedVessel, true);
        }

        public void OnVesselDestroyed(Vessel dyingVessel)
        {
            Guid dyingVesselID = dyingVessel.id;
            //Docking destructions
            if (dyingVesselID == fromDockedVesselID || dyingVesselID == toDockedVesselID)
            {
                DarkLog.Debug("Removing vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + " from the server: Docked");
                if (serverVessels.Contains(dyingVesselID))
                {
                    serverVessels.Remove(dyingVesselID);
                }
                networkWorker.SendVesselRemove(dyingVesselID, true);
                if (serverVesselsProtoUpdate.ContainsKey(dyingVesselID))
                {
                    serverVesselsProtoUpdate.Remove(dyingVesselID);
                }
                if (serverVesselsPositionUpdate.ContainsKey(dyingVesselID))
                {
                    serverVesselsPositionUpdate.Remove(dyingVesselID);
                }
                if (fromDockedVesselID == dyingVesselID)
                {
                    fromDockedVesselID = Guid.Empty;
                }
                if (toDockedVesselID == dyingVesselID)
                {
                    toDockedVesselID = Guid.Empty;
                }
                sentDockingDestroyUpdate = true;
                return;
            }
            if (dyingVessel.state != Vessel.State.DEAD)
            {
                //This is how we can make KSP tell the truth about when a vessel is really dead!
                return;
            }
            if (VesselRecentlyLoaded(dyingVesselID))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", vessel has been recently loaded.");
                return;
            }
            if (VesselRecentlyKilled(dyingVesselID))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", vessel has been recently killed.");
                return;
            }
            if (VesselUpdatedInFuture(dyingVesselID))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", vessel has been changed in the future.");
                return;
            }

            if (!serverVessels.Contains(dyingVessel.id))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", not a server vessel.");
                return;
            }

            DarkLog.Debug("Removing vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + " from the server: Destroyed");
            SendKerbalsInVessel(dyingVessel);
            serverVessels.Remove(dyingVesselID);
            if (serverVesselsProtoUpdate.ContainsKey(dyingVesselID))
            {
                serverVesselsProtoUpdate.Remove(dyingVesselID);
            }
            if (serverVesselsPositionUpdate.ContainsKey(dyingVesselID))
            {
                serverVesselsPositionUpdate.Remove(dyingVesselID);
            }
            networkWorker.SendVesselRemove(dyingVesselID, false);
        }

        //TODO: I don't know what this bool does?
        public void OnVesselRecovered(ProtoVessel recoveredVessel, bool something)
        {
            Guid recoveredVesselID = recoveredVessel.vesselID;


            if (VesselUpdatedInFuture(recoveredVesselID))
            {
                ScreenMessages.PostScreenMessage("Cannot recover vessel, the vessel been changed in the future.", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (!serverVessels.Contains(recoveredVesselID))
            {
                DarkLog.Debug("Cannot recover a non-server vessel!");
                return;
            }

            DarkLog.Debug("Removing vessel " + recoveredVesselID + ", name: " + recoveredVessel.vesselName + " from the server: Recovered");
            SendKerbalsInVessel(recoveredVessel);
            serverVessels.Remove(recoveredVesselID);
            networkWorker.SendVesselRemove(recoveredVesselID, false);
        }

        public void OnVesselTerminated(ProtoVessel terminatedVessel)
        {
            Guid terminatedVesselID = terminatedVessel.vesselID;

            if (VesselUpdatedInFuture(terminatedVesselID))
            {
                ScreenMessages.PostScreenMessage("Cannot terminate vessel, the vessel been changed in the future.", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (!serverVessels.Contains(terminatedVesselID))
            {
                DarkLog.Debug("Cannot terminate a non-server vessel!");
                return;
            }

            DarkLog.Debug("Removing vessel " + terminatedVesselID + ", name: " + terminatedVessel.vesselName + " from the server: Terminated");
            SendKerbalsInVessel(terminatedVessel);
            serverVessels.Remove(terminatedVesselID);
            networkWorker.SendVesselRemove(terminatedVesselID, false);
        }

        public void SendKerbalsInVessel(ProtoVessel vessel)
        {
            if (vessel == null)
            {
                return;
            }
            if (vessel.protoPartSnapshots == null)
            {
                return;
            }
            foreach (ProtoPartSnapshot part in vessel.protoPartSnapshots)
            {
                if (part == null)
                {
                    continue;
                }
                foreach (ProtoCrewMember pcm in part.protoModuleCrew)
                {
                    // Ignore the tourists except those that haven't yet toured
                    if ((pcm.type == ProtoCrewMember.KerbalType.Tourist && !pcm.hasToured) || pcm.type != ProtoCrewMember.KerbalType.Tourist)
                        SendKerbalIfDifferent(pcm);
                }
            }
        }

        public void SendKerbalsInVessel(Vessel vessel)
        {
            if (vessel == null)
            {
                return;
            }
            if (vessel.parts == null)
            {
                return;
            }
            foreach (Part part in vessel.parts)
            {
                if (part == null)
                {
                    continue;
                }
                foreach (ProtoCrewMember pcm in part.protoModuleCrew)
                {
                    // Ignore the tourists except those that haven't yet toured
                    if ((pcm.type == ProtoCrewMember.KerbalType.Tourist && !pcm.hasToured) || pcm.type != ProtoCrewMember.KerbalType.Tourist)
                        SendKerbalIfDifferent(pcm);
                }
            }
        }

        public bool VesselRecentlyLoaded(Guid vesselID)
        {
            return lastLoadVessel.ContainsKey(vesselID) && ((Client.realtimeSinceStartup - lastLoadVessel[vesselID]) < 10f);
        }

        public bool VesselRecentlyKilled(Guid vesselID)
        {
            return lastKillVesselDestroy.ContainsKey(vesselID) && ((Client.realtimeSinceStartup - lastKillVesselDestroy[vesselID]) < 10f);
        }

        public bool VesselUpdatedInFuture(Guid vesselID)
        {
            return latestVesselUpdate.ContainsKey(vesselID) && ((latestVesselUpdate[vesselID] + 3f) > Planetarium.GetUniversalTime());
        }

        public bool LenientVesselUpdatedInFuture(Guid vesselID)
        {
            return latestVesselUpdate.ContainsKey(vesselID) && ((latestVesselUpdate[vesselID] - 3f) > Planetarium.GetUniversalTime());
        }

        public void OnVesselDock(GameEvents.FromToAction<Part, Part> partAction)
        {
            DarkLog.Debug("Vessel docking detected!");
            if (partAction.from.vessel != null && partAction.to.vessel != null)
            {
                DarkLog.Debug("Vessel docking, from: " + partAction.from.vessel.id + ", name: " + partAction.from.vessel.vesselName);
                DarkLog.Debug("Vessel docking, to: " + partAction.to.vessel.id + ", name: " + partAction.to.vessel.vesselName);
                if (FlightGlobals.fetch.activeVessel != null)
                {
                    DarkLog.Debug("Vessel docking, our vessel: " + FlightGlobals.fetch.activeVessel.id);
                }
                fromDockedVesselID = partAction.from.vessel.id;
                toDockedVesselID = partAction.to.vessel.id;
                PrintDockingInProgress();
            }
        }

        private void OnCrewBoard(GameEvents.FromToAction<Part, Part> partAction)
        {
            DarkLog.Debug("Crew boarding detected!");
            DarkLog.Debug("EVA Boarding, from: " + partAction.from.vessel.id + ", name: " + partAction.from.vessel.vesselName);
            DarkLog.Debug("EVA Boarding, to: " + partAction.to.vessel.id + ", name: " + partAction.to.vessel.vesselName);
            fromDockedVesselID = partAction.from.vessel.id;
            toDockedVesselID = partAction.to.vessel.id;
        }

        private void OnKerbalRemoved(ProtoCrewMember pcm)
        {
            SendKerbalRemove(pcm.name);
        }

        public void KillVessel(Vessel killVessel)
        {
            if (killVessel != null)
            {
                DarkLog.Debug("Killing vessel: " + killVessel.id);

                //Forget the dying vessel
                partKiller.ForgetVessel(killVessel);

                //Try to unload the vessel first.
                if (killVessel.loaded)
                {
                    try
                    {
                        killVessel.Unload();
                    }
                    catch (Exception unloadException)
                    {
                        DarkLog.Debug("Error unloading vessel: " + unloadException);
                    }
                }

                //Remove the kerbal from the craft
                foreach (ProtoCrewMember pcm in killVessel.GetVesselCrew().ToArray())
                {
                    killVessel.RemoveCrew(pcm);
                }

                lastKillVesselDestroy[killVessel.id] = Client.realtimeSinceStartup;

                try
                {
                    killVessel.Die();
                }
                catch (Exception killException)
                {
                    DarkLog.Debug("Error destroying vessel: " + killException);
                }
            }
        }

        private void RemoveVessel(Guid vesselID, bool isDockingUpdate, string dockingPlayer)
        {
            for (int i = FlightGlobals.fetch.vessels.Count - 1; i >= 0; i--)
            {
                Vessel checkVessel = FlightGlobals.fetch.vessels[i];
                if (checkVessel.id == vesselID)
                {
                    if (isDockingUpdate)
                    {
                        if (FlightGlobals.fetch.activeVessel != null ? FlightGlobals.fetch.activeVessel.id == checkVessel.id : false)
                        {
                            HighLogic.LoadScene(GameScenes.TRACKSTATION);
                            ScreenMessages.PostScreenMessage("Docking occurred, kicked to tracking to avoid duplicate vessels.");
                        }
                        DarkLog.Debug("Removing docked vessel: " + vesselID);
                        KillVessel(checkVessel);
                    }
                    else
                    {
                        DarkLog.Debug("Removing vessel: " + vesselID);
                        KillVessel(checkVessel);
                    }
                }
            }
        }
        //Called from networkWorker
        public void QueueKerbal(double planetTime, string kerbalName, ConfigNode kerbalNode)
        {
            lock (updateQueueLock)
            {
                KerbalEntry newEntry = new KerbalEntry();
                newEntry.planetTime = planetTime;
                newEntry.kerbalNode = kerbalNode;
                if (!kerbalProtoQueue.ContainsKey(kerbalName))
                {
                    kerbalProtoQueue.Add(kerbalName, new Queue<KerbalEntry>());
                }

                Queue<KerbalEntry> keQueue = kerbalProtoQueue[kerbalName];
                if (kerbalProtoHistoryTime.ContainsKey(kerbalName))
                {
                    //If we get a remove older than the current queue peek, then someone has gone back in time and the timeline needs to be fixed.
                    if (planetTime < kerbalProtoHistoryTime[kerbalName])
                    {
                        DarkLog.Debug("Kerbal " + kerbalName + " went back in time - rewriting the remove history for it.");
                        Queue<KerbalEntry> newQueue = new Queue<KerbalEntry>();
                        while (keQueue.Count > 0)
                        {
                            KerbalEntry oldKe = keQueue.Dequeue();
                            //Save the updates from before the revert
                            if (oldKe.planetTime < planetTime)
                            {
                                newQueue.Enqueue(oldKe);
                            }
                        }
                        keQueue = newQueue;
                        kerbalProtoQueue[kerbalName] = newQueue;
                        //Clean the history too
                        if (Settings.singleton.revertEnabled)
                        {
                            if (kerbalProtoHistory.ContainsKey(kerbalName))
                            {
                                List<KerbalEntry> keh = kerbalProtoHistory[kerbalName];
                                foreach (KerbalEntry oldKe in keh.ToArray())
                                {
                                    if (oldKe.planetTime > planetTime)
                                    {
                                        keh.Remove(oldKe);
                                    }
                                }
                            }
                        }
                    }
                }

                keQueue.Enqueue(newEntry);
                if (Settings.singleton.revertEnabled)
                {
                    if (!kerbalProtoHistory.ContainsKey(kerbalName))
                    {
                        kerbalProtoHistory.Add(kerbalName, new List<KerbalEntry>());
                    }
                    kerbalProtoHistory[kerbalName].Add(newEntry);
                }
                kerbalProtoHistoryTime[kerbalName] = planetTime;
            }
        }
        //Called from networkWorker
        public void QueueVesselRemove(Guid vesselID, double planetTime, bool isDockingUpdate, string dockingPlayer)
        {
            lock (updateQueueLock)
            {

                if (!vesselRemoveQueue.ContainsKey(vesselID))
                {
                    vesselRemoveQueue.Add(vesselID, new Queue<VesselRemoveEntry>());
                }

                Queue<VesselRemoveEntry> vrQueue = vesselRemoveQueue[vesselID];
                if (vesselRemoveHistoryTime.ContainsKey(vesselID))
                {
                    //If we get a remove older than the current queue peek, then someone has gone back in time and the timeline needs to be fixed.
                    if (planetTime < vesselRemoveHistoryTime[vesselID])
                    {
                        DarkLog.Debug("Vessel " + vesselID + " went back in time - rewriting the remove history for it.");
                        Queue<VesselRemoveEntry> newQueue = new Queue<VesselRemoveEntry>();
                        while (vrQueue.Count > 0)
                        {
                            VesselRemoveEntry oldVre = vrQueue.Dequeue();
                            //Save the updates from before the revert
                            if (oldVre.planetTime < planetTime)
                            {
                                newQueue.Enqueue(oldVre);
                            }
                        }
                        vrQueue = newQueue;
                        vesselRemoveQueue[vesselID] = newQueue;
                        //Clean the history too
                        if (Settings.singleton.revertEnabled)
                        {
                            if (vesselRemoveHistory.ContainsKey(vesselID))
                            {
                                List<VesselRemoveEntry> vrh = vesselRemoveHistory[vesselID];
                                foreach (VesselRemoveEntry oldVr in vrh.ToArray())
                                {
                                    if (oldVr.planetTime > planetTime)
                                    {
                                        vrh.Remove(oldVr);
                                    }
                                }
                            }
                        }
                    }
                }

                VesselRemoveEntry vre = new VesselRemoveEntry();
                vre.planetTime = planetTime;
                vre.vesselID = vesselID;
                vre.isDockingUpdate = isDockingUpdate;
                vre.dockingPlayer = dockingPlayer;
                vrQueue.Enqueue(vre);
                if (latestVesselUpdate.ContainsKey(vesselID) ? latestVesselUpdate[vesselID] < planetTime : true)
                {
                    latestVesselUpdate[vesselID] = planetTime;
                }
                if (Settings.singleton.revertEnabled)
                {
                    if (!vesselRemoveHistory.ContainsKey(vesselID))
                    {
                        vesselRemoveHistory.Add(vesselID, new List<VesselRemoveEntry>());
                    }
                    vesselRemoveHistory[vesselID].Add(vre);
                }
                vesselRemoveHistoryTime[vesselID] = planetTime;
            }
        }

        public void QueueVesselProto(Guid vesselID, double planetTime, ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                lock (updateQueueLock)
                {
                    if (!vesselProtoQueue.ContainsKey(vesselID))
                    {
                        vesselProtoQueue.Add(vesselID, new Queue<VesselProtoUpdate>());
                    }
                    Queue<VesselProtoUpdate> vpuQueue = vesselProtoQueue[vesselID];
                    if (vesselProtoHistoryTime.ContainsKey(vesselID))
                    {
                        //If we get an update older than the current queue peek, then someone has gone back in time and the timeline needs to be fixed.
                        if (planetTime < vesselProtoHistoryTime[vesselID])
                        {
                            DarkLog.Debug("Vessel " + vesselID + " went back in time - rewriting the proto update history for it.");
                            Queue<VesselProtoUpdate> newQueue = new Queue<VesselProtoUpdate>();
                            while (vpuQueue.Count > 0)
                            {
                                VesselProtoUpdate oldVpu = vpuQueue.Dequeue();
                                //Save the updates from before the revert
                                if (oldVpu.planetTime < planetTime)
                                {
                                    newQueue.Enqueue(oldVpu);
                                }
                            }
                            vpuQueue = newQueue;
                            vesselProtoQueue[vesselID] = newQueue;
                            //Clean the history too
                            if (Settings.singleton.revertEnabled)
                            {
                                if (vesselProtoHistory.ContainsKey(vesselID))
                                {
                                    List<VesselProtoUpdate> vpuh = vesselProtoHistory[vesselID];
                                    foreach (VesselProtoUpdate oldVpu in vpuh.ToArray())
                                    {
                                        if (oldVpu.planetTime > planetTime)
                                        {
                                            vpuh.Remove(oldVpu);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    //Create new VPU to be stored.
                    VesselProtoUpdate vpu = new VesselProtoUpdate();
                    vpu.vesselID = vesselID;
                    vpu.planetTime = planetTime;
                    vpu.vesselNode = vesselNode;
                    vpuQueue.Enqueue(vpu);
                    //Revert support
                    if (Settings.singleton.revertEnabled)
                    {
                        if (!vesselProtoHistory.ContainsKey(vesselID))
                        {
                            vesselProtoHistory.Add(vesselID, new List<VesselProtoUpdate>());
                        }
                        vesselProtoHistory[vesselID].Add(vpu);
                    }
                    vesselProtoHistoryTime[vesselID] = planetTime;
                }
            }
            else
            {
                DarkLog.Debug("Refusing to queue proto for " + vesselID + ", it has a null config node");
            }
        }

        public void QueueVesselUpdate(VesselUpdate update, bool fromMesh)
        {
            lock (updateQueueLock)
            {
                if (!fromMesh)
                {
                    if (!vesselUpdateQueue.ContainsKey(update.vesselID))
                    {
                        vesselUpdateQueue.Add(update.vesselID, new Queue<VesselUpdate>());
                    }
                    Queue<VesselUpdate> vuQueue = vesselUpdateQueue[update.vesselID];
                    /*
                    if (vesselUpdateHistoryTime.ContainsKey(update.vesselID))
                    {
                        //If we get an update older than the current queue peek, then someone has gone back in time and the timeline needs to be fixed.
                        if (update.planetTime < vesselUpdateHistoryTime[update.vesselID])
                        {
                            DarkLog.Debug("Vessel " + update.vesselID + " went back in time - rewriting the update history for it.");
                            Queue<VesselUpdate> newQueue = new Queue<VesselUpdate>();
                            while (vuQueue.Count > 0)
                            {
                                VesselUpdate oldVu = vuQueue.Dequeue();
                                //Save the updates from before the revert
                                if (oldVu.planetTime < update.planetTime)
                                {
                                    newQueue.Enqueue(oldVu);
                                }
                                else
                                {
                                    Recycler<VesselUpdate>.ReleaseObject(oldVu);
                                }
                            }
                            vuQueue = newQueue;
                            vesselUpdateQueue[update.vesselID] = newQueue;
                            //Clean the history too
                            if (Settings.singleton.revertEnabled)
                            {
                                if (vesselUpdateHistory.ContainsKey(update.vesselID))
                                {
                                    List<VesselUpdate> vuh = vesselUpdateHistory[update.vesselID];
                                    foreach (VesselUpdate oldVu in vuh.ToArray())
                                    {
                                        if (oldVu.planetTime > update.planetTime)
                                        {
                                            vuh.Remove(oldVu);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    */
                    vuQueue.Enqueue(update);
                    //Mark the last update time
                    if (latestVesselUpdate.ContainsKey(update.vesselID) ? latestVesselUpdate[update.vesselID] < update.planetTime : true)
                    {
                        latestVesselUpdate[update.vesselID] = update.planetTime;
                    }
                    //Revert support
                    /*
                    if (Settings.singleton.revertEnabled)
                    {
                        if (!vesselUpdateHistory.ContainsKey(update.vesselID))
                        {
                            vesselUpdateHistory.Add(update.vesselID, new List<VesselUpdate>());
                        }
                        //There is no way we can track thousands of these in the Recycler<VesselUpdate>, it will also slow down the search for free vesselupdates.
                        VesselUpdate copyVU = new VesselUpdate();
                        copyVU.CopyFromUpdate(update);
                        vesselUpdateHistory[update.vesselID].Add(copyVU);
                    }
                    vesselUpdateHistoryTime[update.vesselID] = update.planetTime;
                    */
                }
                else
                {
                    serverVesselsPositionUpdateMeshReceive[update.vesselID] = Client.realtimeSinceStartup;
                    if (!vesselUpdateMeshQueue.ContainsKey(update.vesselID))
                    {
                        vesselUpdateMeshQueue.Add(update.vesselID, new Queue<VesselUpdate>());
                    }
                    Queue<VesselUpdate> vuQueue = vesselUpdateMeshQueue[update.vesselID];
                    VesselUpdate peekUpdate = null;
                    if (vuQueue.Count > 0)
                    {
                        peekUpdate = vuQueue.Peek();
                    }
                    //Clear the update queue if a revert is detected
                    if (peekUpdate != null && peekUpdate.planetTime - update.planetTime > 10f)
                    {
                        while (vuQueue.Count > 1)
                        {
                            VesselUpdate deleteUpdate = vuQueue.Dequeue();
                            Recycler<VesselUpdate>.ReleaseObject(deleteUpdate);
                        }
                        peekUpdate = null;
                    }
                    if (peekUpdate == null || peekUpdate.planetTime < update.planetTime)
                    {
                        vuQueue.Enqueue(update);
                    }
                }
            }
        }

        public void QueueActiveVessel(string player, Guid vesselID)
        {
            ActiveVesselEntry ave = new ActiveVesselEntry();
            ave.player = player;
            ave.vesselID = vesselID;
            newActiveVessels.Enqueue(ave);
        }

        public void IgnoreVessel(Guid vesselID)
        {
            if (ignoreVessels.Contains(vesselID))
            {
                ignoreVessels.Add(vesselID);
            }
        }

        public void RegisterServerVessel(Guid vesselID)
        {
            if (!serverVessels.Contains(vesselID))
            {
                serverVessels.Add(vesselID);
            }
        }

        public void Stop()
        {
            workerEnabled = false;
            dmpGame.fixedUpdateEvent.Remove(fixedUpdateAction);
            if (registered)
            {
                registered = false;
                UnregisterGameHooks();
            }
        }

        public int GetStatistics(string statType)
        {
            switch (statType)
            {
                case "StoredFutureUpdates":
                    {
                        int futureUpdates = 0;
                        foreach (KeyValuePair<Guid, Queue<VesselUpdate>> vUQ in vesselUpdateQueue)
                        {
                            futureUpdates += vUQ.Value.Count;
                        }
                        return futureUpdates;
                    }
                case "StoredFutureProtoUpdates":
                    {
                        int futureProtoUpdates = 0;
                        foreach (KeyValuePair<Guid, Queue<VesselProtoUpdate>> vPQ in vesselProtoQueue)
                        {
                            futureProtoUpdates += vPQ.Value.Count;
                        }
                        return futureProtoUpdates;
                    }
            }
            return 0;
        }
    }

    class ActiveVesselEntry
    {
        public string player;
        public Guid vesselID;
    }

    class VesselRemoveEntry
    {
        public Guid vesselID;
        public double planetTime;
        public bool isDockingUpdate;
        public string dockingPlayer;
    }

    class KerbalEntry
    {
        public double planetTime;
        public ConfigNode kerbalNode;
    }
}

