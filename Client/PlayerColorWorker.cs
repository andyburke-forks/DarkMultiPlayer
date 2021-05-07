using System;
using System.Collections.Generic;
using UnityEngine;
using MessageStream2;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    //Damn you americans - You're making me spell 'colour' wrong!
    public class PlayerColorWorker
    {
        //Services
        private DMPGame dmpGame;
        private PlayerStatusWindow playerStatusWindow;
        private NetworkWorker networkWorker;
        private bool registered;
        public bool workerEnabled;
        private NamedAction updateEvent;
        private const int FRAMES_TO_DELAY_RETRY = 1;
        private Dictionary<Vessel, FrameCount> delaySetColors = new Dictionary<Vessel, FrameCount>();
        private List<Vessel> delayRemoveList = new List<Vessel>();
        private Dictionary<string, Color> playerColors = new Dictionary<string, Color>();
        private object playerColorLock = new object();
        //Can't declare const - But no touchy.
        public readonly Color DEFAULT_COLOR = Color.grey;

        public PlayerColorWorker(DMPGame dmpGame, NetworkWorker networkWorker)
        {
            this.dmpGame = dmpGame;
            this.networkWorker = networkWorker;
            updateEvent = new NamedAction(Update);
            dmpGame.updateEvent.Add(updateEvent);
        }

        public void SetDependencies(PlayerStatusWindow playerStatusWindow)
        {
            this.playerStatusWindow = playerStatusWindow;
        }

        public void Update()
        {
            if (workerEnabled && !registered)
            {
                RegisterGameHooks();
            }
            if (!workerEnabled && registered)
            {
                UnregisterGameHooks();
            }
            if (!workerEnabled)
            {
                return;
            }
            if (!FlightGlobals.ready)
            {
                return;
            }
            DelaySetColors();
        }

        public void DetectNewVessel(Vessel v)
        {
            if (!delaySetColors.ContainsKey(v))
            {
                delaySetColors.Add(v, new FrameCount(FRAMES_TO_DELAY_RETRY));
            }
        }

        private void DelaySetColors()
        {
            foreach (KeyValuePair<Vessel, FrameCount> kvp in delaySetColors)
            {
                FrameCount frameCount = kvp.Value;
                frameCount.number = frameCount.number - 1;
                if (frameCount.number == 0)
                {
                    delayRemoveList.Add(kvp.Key);
                }
            }
            foreach (Vessel setVessel in delayRemoveList)
            {
                delaySetColors.Remove(setVessel);
                if (FlightGlobals.fetch.vessels.Contains(setVessel))
                {
                    SetVesselColor(setVessel);
                }
            }
            if (delayRemoveList.Count > 0)
            {
                delayRemoveList.Clear();
            }
        }

        private void SetVesselColor(Vessel colorVessel)
        {
            if (colorVessel == null)
            {
                return;
            }
            if (workerEnabled)
            {
                if (colorVessel.orbitRenderer != null)
                {
                    colorVessel.orbitRenderer.SetColor(DEFAULT_COLOR);
                }
            }
        }

        private void on_set(string key, string value, string result)
        {
            if (workerEnabled)
            {
                UpdateVesselColorsFromLockName(key);
            }
        }

        private void on_unset(string key, bool was_unset)
        {
            if (workerEnabled)
            {
                UpdateVesselColorsFromLockName(key);
            }
        }

        private void UpdateVesselColorsFromLockName(string key)
        {
            if (key.StartsWith("position-updater-", StringComparison.Ordinal))
            {
                string vesselID = key.Substring(8);
                foreach (Vessel findVessel in FlightGlobals.fetch.vessels)
                {
                    if (findVessel.id.ToString() == vesselID)
                    {
                        SetVesselColor(findVessel);
                    }
                }
            }
        }

        private void UpdateAllVesselColors()
        {
            foreach (Vessel updateVessel in FlightGlobals.fetch.vessels)
            {
                SetVesselColor(updateVessel);
            }
        }

        public Color GetPlayerColor(string playerName)
        {
            lock (playerColorLock)
            {
                if (playerName == Settings.singleton.playerName)
                {
                    return Settings.singleton.playerColor;
                }
                if (playerColors.ContainsKey(playerName))
                {
                    return playerColors[playerName];
                }
                return DEFAULT_COLOR;
            }
        }

        public void HandlePlayerColorMessage(ByteArray messageData)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                PlayerColorMessageType messageType = (PlayerColorMessageType)mr.Read<int>();
                switch (messageType)
                {
                    case PlayerColorMessageType.LIST:
                        {
                            int numOfEntries = mr.Read<int>();
                            lock (playerColorLock)
                            {
                                playerColors = new Dictionary<string, Color>();
                                for (int i = 0; i < numOfEntries; i++)
                                {

                                    string playerName = mr.Read<string>();
                                    Color playerColor = ConvertFloatArrayToColor(mr.Read<float[]>());
                                    playerColors.Add(playerName, playerColor);
                                    playerStatusWindow.colorEventHandled = false;
                                }
                            }
                        }
                        break;
                    case PlayerColorMessageType.SET:
                        {
                            lock (playerColorLock)
                            {
                                string playerName = mr.Read<string>();
                                Color playerColor = ConvertFloatArrayToColor(mr.Read<float[]>());
                                DarkLog.Debug("Color message, name: " + playerName + " , color: " + playerColor.ToString());
                                playerColors[playerName] = playerColor;
                                UpdateAllVesselColors();
                                playerStatusWindow.colorEventHandled = false;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        public void SendPlayerColorToServer()
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)PlayerColorMessageType.SET);
                mw.Write<string>(Settings.singleton.playerName);
                mw.Write<float[]>(ConvertColorToFloatArray(Settings.singleton.playerColor));
                networkWorker.SendPlayerColorMessage(mw.GetMessageBytes());
            }
        }
        //Helpers
        public static float[] ConvertColorToFloatArray(Color convertColour)
        {
            float[] returnArray = new float[3];
            returnArray[0] = convertColour.r;
            returnArray[1] = convertColour.g;
            returnArray[2] = convertColour.b;
            return returnArray;
        }

        public static Color ConvertFloatArrayToColor(float[] convertArray)
        {
            return new Color(convertArray[0], convertArray[1], convertArray[2]);
        }
        //Adapted from KMP
        public static Color GenerateRandomColor()
        {
            System.Random rand = new System.Random();
            int seed = rand.Next();
            Color returnColor = Color.white;
            switch (seed % 17)
            {
                case 0:
                    return Color.red;
                case 1:
                    return new Color(1, 0, 0.5f, 1); //Rosy pink
                case 2:
                    return new Color(0.6f, 0, 0.5f, 1); //OU Crimson
                case 3:
                    return new Color(1, 0.5f, 0, 1); //Orange
                case 4:
                    return Color.yellow;
                case 5:
                    return new Color(1, 0.84f, 0, 1); //Gold
                case 6:
                    return Color.green;
                case 7:
                    return new Color(0, 0.651f, 0.576f, 1); //Persian Green
                case 8:
                    return new Color(0, 0.651f, 0.576f, 1); //Persian Green
                case 9:
                    return new Color(0, 0.659f, 0.420f, 1); //Jade
                case 10:
                    return new Color(0.043f, 0.855f, 0.318f, 1); //Malachite
                case 11:
                    return Color.cyan;
                case 12:
                    return new Color(0.537f, 0.812f, 0.883f, 1); //Baby blue;
                case 13:
                    return new Color(0, 0.529f, 0.741f, 1); //NCS blue
                case 14:
                    return new Color(0.255f, 0.412f, 0.882f, 1); //Royal Blue
                case 15:
                    return new Color(0.5f, 0, 1, 1); //Violet
                default:
                    return Color.magenta;
            }
        }

        private void RegisterGameHooks()
        {
            if (!registered)
            {
                registered = true;
                GameEvents.onVesselCreate.Add(this.DetectNewVessel);
                Store.singleton.add_on_set_listener( this.on_set );
                Store.singleton.add_on_unset_listener( this.on_unset );
            }
        }

        private void UnregisterGameHooks()
        {
            if (registered)
            {
                registered = false;
                GameEvents.onVesselCreate.Remove(this.DetectNewVessel);
            }
        }

        public void Stop()
        {
            workerEnabled = false;
            UnregisterGameHooks();
        }

        //Can't modify a dictionary so we're going to have to make a reference class...
        private class FrameCount
        {
            public int number;

            public FrameCount(int number)
            {
                this.number = number;
            }
        }
    }
}

