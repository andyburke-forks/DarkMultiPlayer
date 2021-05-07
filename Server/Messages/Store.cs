using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class Store
    {
        public static void send_all(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.STORE_MESSAGE;
            //Send the dictionary as 2 string[]'s.
            Dictionary<string,string> store = DarkMultiPlayerCommon.Store.singleton.as_dictionary();
            List<string> keys = new List<string>(store.Keys);
            List<string> values = new List<string>(store.Values);
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write((int)StoreMessageType.LIST);
                mw.Write<string[]>(keys.ToArray());
                mw.Write<string[]>(values.ToArray());
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }

        public static void HandleStoreMessage(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                //All of the messages need replies, let's create a message for it.
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.STORE_MESSAGE;
                //Read the lock-system message type
                StoreMessageType store_message_type = (StoreMessageType)mr.Read<int>();
                switch (store_message_type)
                {
                    case StoreMessageType.SET:
                        {
                            string key = mr.Read<string>();
                            string value = mr.Read<string>();

                            string result = DarkMultiPlayerCommon.Store.singleton.set( key, value );
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write((int)StoreMessageType.SET);
                                mw.Write(key);
                                mw.Write(value);
                                newMessage.data = mw.GetMessageBytes();
                            }
                            //Send to all clients
                            ClientHandler.SendToAll(null, newMessage, true);
                            if (result == value)
                            {
                                DarkLog.Debug( key + " => " + value );
                            }
                            else
                            {
                                DarkLog.Debug( key + " =>! " + value + " (" + result + ")" );
                            }
                        }
                        break;
                    case StoreMessageType.UNSET:
                        {
                            string key = mr.Read<string>();

                            string unset_value = DarkMultiPlayerCommon.Store.singleton.unset( key );

							using (MessageWriter mw = new MessageWriter())
							{
								mw.Write((int)StoreMessageType.UNSET);
								mw.Write(key);
								newMessage.data = mw.GetMessageBytes();
							}
							//Send to all clients
							ClientHandler.SendToAll(null, newMessage, true);

                            if ( unset_value != null )
                            {
                                DarkLog.Debug( key + " =>X" );
                            }
                        }
                        break;
                }
            }
        }
    }
}
