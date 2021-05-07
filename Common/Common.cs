using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DarkMultiPlayerCommon
{
    public class Common
    {
        //Timeouts in milliseconds
        public const long HEART_BEAT_INTERVAL = 5000;
        public const long INITIAL_CONNECTION_TIMEOUT = 5000;
        public const long CONNECTION_TIMEOUT = 20000;
        //Any message bigger than 64MB will be invalid
        public const int MAX_MESSAGE_SIZE = 67108864;
        //Split messages into 8kb chunks so higher priority messages have more injection points into the TCP stream.
        public const int SPLIT_MESSAGE_LENGTH = 8192;
        //Bump this every time there is a network change (Basically, if MessageWriter or MessageReader is touched).
        public const int PROTOCOL_VERSION = 54;
        //Program version. This is written in the build scripts.
        public const string PROGRAM_VERSION = "Custom";
        //Compression threshold
        public const int COMPRESSION_THRESHOLD = 4096;

        public static string CalculateSHA256Hash(string fileName)
        {
            return CalculateSHA256Hash(File.ReadAllBytes(fileName));
        }

        public static string CalculateSHA256Hash(ByteArray fileData)
        {
            return CalculateSHA256Hash(fileData.data, fileData.Length);
        }

        public static string CalculateSHA256Hash(byte[] fileData)
        {
            return CalculateSHA256Hash(fileData, fileData.Length);
        }

        public static string CalculateSHA256Hash(byte[] fileData, int length)
        {
            StringBuilder sb = new StringBuilder();
            using (SHA256Managed sha = new SHA256Managed())
            {
                byte[] fileHashData = sha.ComputeHash(fileData, 0, length);
                //Byte[] to string conversion adapted from MSDN...
                for (int i = 0; i < fileHashData.Length; i++)
                {
                    sb.Append(fileHashData[i].ToString("x2"));
                }
            }
            return sb.ToString();
        }

        public static ByteArray PrependNetworkFrame(int messageType, ByteArray messageData)
        {
            ByteArray returnBytes;
            //Get type bytes
            byte[] typeBytes = BitConverter.GetBytes(messageType);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(typeBytes);
            }
            if (messageData == null || messageData.Length == 0)
            {
                returnBytes = ByteRecycler.GetObject(8);
                typeBytes.CopyTo(returnBytes.data, 0);
                returnBytes.data[4] = 0;
                returnBytes.data[5] = 0;
                returnBytes.data[6] = 0;
                returnBytes.data[7] = 0;
            }
            else
            {
                //Get length bytes if we have a payload
                byte[] lengthBytes = BitConverter.GetBytes(messageData.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthBytes);
                }

                returnBytes = ByteRecycler.GetObject(8 + messageData.Length);
                typeBytes.CopyTo(returnBytes.data, 0);
                lengthBytes.CopyTo(returnBytes.data, 4);
                Array.Copy(messageData.data, 0, returnBytes.data, 8, messageData.Length);
            }
            return returnBytes;
        }

        public static byte[] PrependNetworkFrame(int messageType, byte[] messageData)
        {
            byte[] returnBytes;
            //Get type bytes
            byte[] typeBytes = BitConverter.GetBytes(messageType);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(typeBytes);
            }
            if (messageData == null || messageData.Length == 0)
            {
                returnBytes = new byte[8];
                typeBytes.CopyTo(returnBytes, 0);
            }
            else
            {
                //Get length bytes if we have a payload
                byte[] lengthBytes = BitConverter.GetBytes(messageData.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthBytes);
                }

                returnBytes = new byte[8 + messageData.Length];
                typeBytes.CopyTo(returnBytes, 0);
                lengthBytes.CopyTo(returnBytes, 4);
                messageData.CopyTo(returnBytes, 8);
            }
            return returnBytes;
        }

        public static string ConvertConfigStringToGUIDString(string configNodeString)
        {
            if (configNodeString == null || configNodeString.Length != 32)
            {
                return null;
            }
            string[] returnString = new string[5];
            returnString[0] = configNodeString.Substring(0, 8);
            returnString[1] = configNodeString.Substring(8, 4);
            returnString[2] = configNodeString.Substring(12, 4);
            returnString[3] = configNodeString.Substring(16, 4);
            returnString[4] = configNodeString.Substring(20);
            return String.Join("-", returnString);
        }

        public static long GetCurrentUnixTime()
        {
            return ((DateTime.UtcNow.Ticks - new DateTime(1970, 1, 1).Ticks) / TimeSpan.TicksPerSecond);
        }
    }

    public enum CraftType
    {
        VAB,
        SPH,
        SUBASSEMBLY
    }

    public enum ClientMessageType
    {
        HEARTBEAT,
        HANDSHAKE_RESPONSE,
        PLAYER_STATUS,
        PLAYER_COLOR,
        SCENARIO_DATA,
        KERBALS_REQUEST,
        KERBAL_PROTO,
        KERBAL_REMOVE,
        VESSELS_REQUEST,
        VESSEL_PROTO,
        VESSEL_UPDATE,
        VESSEL_REMOVE,
        FLAG_SYNC,
        SYNC_TIME_REQUEST,
        PING_REQUEST,
        MOTD_REQUEST,
        WARP_CONTROL,
        LOCK_SYSTEM,
        MOD_DATA,
        SPLIT_MESSAGE,
        CONNECTION_END,
        STORE_MESSAGE
    }

    public enum MeshMessageType
    {
        SET_PLAYER,
        VESSEL_UPDATE
    }

    public enum ServerMessageType
    {
        HEARTBEAT,
        HANDSHAKE_CHALLANGE,
        HANDSHAKE_REPLY,
        SERVER_SETTINGS,
        PLAYER_STATUS,
        PLAYER_COLOR,
        PLAYER_JOIN,
        PLAYER_DISCONNECT,
        SCENARIO_DATA,
        KERBAL_REPLY,
        KERBAL_COMPLETE,
        KERBAL_REMOVE,
        VESSEL_LIST,
        VESSEL_PROTO,
        VESSEL_UPDATE,
        VESSEL_COMPLETE,
        VESSEL_REMOVE,
        FLAG_SYNC,
        SET_SUBSPACE,
        SYNC_TIME_REPLY,
        PING_REPLY,
        MOTD_REPLY,
        WARP_CONTROL,
        MOD_DATA,
        SPLIT_MESSAGE,
        CONNECTION_END,
        STORE_MESSAGE
    }

    public enum ConnectionStatus
    {
        DISCONNECTED,
        CONNECTING,
        CONNECTED
    }

    public enum ClientState
    {
        DISCONNECTED,
        CONNECTING,
        CONNECTED,
        HANDSHAKING,
        AUTHENTICATED,
        TIME_SYNCING,
        TIME_SYNCED,
        SYNCING_KERBALS,
        KERBALS_SYNCED,
        SYNCING_VESSELS,
        VESSELS_SYNCED,
        TIME_LOCKING,
        TIME_LOCKED,
        STARTING,
        RUNNING,
        DISCONNECTING
    }

    public enum WarpMode
    {
        MCW_FORCE,
        MCW_VOTE,
        MCW_LOWEST,
        SUBSPACE_SIMPLE,
        SUBSPACE,
        NONE
    }

    public enum GameMode
    {
        SANDBOX,
        SCIENCE,
        CAREER
    }

    public enum WarpMessageType
    {
        //MCW_VOTE
        REQUEST_VOTE,
        REPLY_VOTE,
        //ALL
        CHANGE_WARP,
        //MCW_VOTE/FORCE
        REQUEST_CONTROLLER,
        RELEASE_CONTROLLER,
        //MCW_VOTE/FORCE/LOWEST
        IGNORE_WARP,
        SET_CONTROLLER,
        //ALL
        NEW_SUBSPACE,
        CHANGE_SUBSPACE,
        RELOCK_SUBSPACE,
        REPORT_RATE
    }

    public enum StoreMessageType
    {
        LIST,
        SET,
        UNSET,
    }

    public enum FlagMessageType
    {
        LIST,
        FLAG_DATA,
        UPLOAD_FILE,
        DELETE_FILE,
    }

    public enum PlayerColorMessageType
    {
        LIST,
        SET,
    }

    public class ClientMessage
    {
        public bool handled;
        public ClientMessageType type;
        public byte[] data;
    }

    public class ServerMessage
    {
        public ServerMessageType type;
        public byte[] data;
    }

    public class PlayerStatus
    {
        public string playerName;
        public string vesselText;
        public string statusText;
    }

    public class Subspace
    {
        public long serverClock;
        public double planetTime;
        public float subspaceSpeed;
    }

    public class PlayerWarpRate
    {
        public bool isPhysWarp = false;
        public int rateIndex = 0;
        public long serverClock = 0;
        public double planetTime = 0;
    }

    public enum HandshakeReply
    {
        HANDSHOOK_SUCCESSFULLY = 0,
        PROTOCOL_MISMATCH = 1,
        ALREADY_CONNECTED = 2,
        RESERVED_NAME = 3,
        INVALID_KEY = 4,
        SERVER_FULL = 6,
        INVALID_PLAYERNAME = 98,
        MALFORMED_HANDSHAKE = 99
    }

    public enum GameDifficulty
    {
        EASY,
        NORMAL,
        MODERATE,
        HARD,
        CUSTOM
    }
}
