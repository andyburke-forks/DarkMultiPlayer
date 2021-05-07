using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayer
{
    public delegate void on_set(string key, string value, string result);
    public delegate void on_unset(string key, bool was_unset);
    public class Store
    {
		public static Store singleton = new Store();

        private DarkMultiPlayerCommon.Store server_store = new DarkMultiPlayerCommon.Store();
        private List<on_set> on_set_listeners = new List<on_set>();
        private List<on_unset> on_unset_listeners = new List<on_unset>();
        private object _lock = new object();
        //String caches
        private Dictionary<Guid, string> controlStringCache = new Dictionary<Guid, string>();
        private Dictionary<Guid, string> updateStringCache = new Dictionary<Guid, string>();

        public Store()
        {
        }

        public string get(string key)
        {
            lock (_lock)
            {
				return server_store.get( key );
            }
        }
        public string set(string key, string value)
        {
            lock (_lock)
            {
				if ( NetworkWorker.singleton != null ) {
					using (MessageWriter mw = new MessageWriter())
					{
						mw.Write<int>((int)StoreMessageType.SET);
						mw.Write<string>(key);
						mw.Write<string>(value);
						NetworkWorker.singleton.SendStoreMessage(mw.GetMessageBytes());
					}
				}
				return server_store.set( key, value );
            }
        }

        public string unset(string key)
        {
            lock (_lock)
            {
				if ( NetworkWorker.singleton != null ) {
					using (MessageWriter mw = new MessageWriter())
					{
						mw.Write<int>((int)StoreMessageType.UNSET);
						mw.Write<string>(key);
						NetworkWorker.singleton.SendStoreMessage(mw.GetMessageBytes());
					}
				}
				return server_store.unset( key );
            }
        }

        public void unset_by_value(string value)
        {
            lock (_lock)
            {
                foreach (KeyValuePair<string, string> kvp in server_store.as_dictionary())
                {
                    if (kvp.Value == value)
                    {
						this.unset( kvp.Key );
                    }
                }
            }
        }

        public void HandleStoreMessage(ByteArray messageData)
        {
            lock (_lock)
            {
                using (MessageReader mr = new MessageReader(messageData.data))
                {
                    StoreMessageType StoreMessageType = (StoreMessageType)mr.Read<int>();
                    switch (StoreMessageType)
                    {
                        case StoreMessageType.LIST:
                            {
                                //We shouldn't need to clear this as LIST is only sent once, but better safe than sorry.
                                server_store.clear();
                                string[] keys = mr.Read<string[]>();
                                string[] values = mr.Read<string[]>();
                                for (int i = 0; i < keys.Length; i++)
                                {
                                    server_store.set(keys[i], values[i]);
                                }
                            }
                            break;
                        case StoreMessageType.SET:
                            {
                                string key = mr.Read<string>();
                                string value = mr.Read<string>();

								string result = server_store.set( key, value );
                                dispatch_on_set( key, value, result );
                            }
                            break;
                        case StoreMessageType.UNSET:
                            {
                                string key = mr.Read<string>();

								string unset = server_store.unset( key );
                                dispatch_on_unset( key, unset != null );
                            }
                            break;
                        default:
                            break;
                    }
                }
            }
        }

        public void add_on_set_listener(on_set callback)
        {
            on_set_listeners.Add(callback);
        }

        public void remove_on_set_listener(on_set callback)
        {
            if (on_set_listeners.Contains(callback))
            {
                on_set_listeners.Remove(callback);
            }
        }

        public void add_on_unset_listener(on_unset callback)
        {
            on_unset_listeners.Add(callback);
        }

        public void remove_on_unset_listener(on_unset callback)
        {
            if (on_unset_listeners.Contains(callback))
            {
                on_unset_listeners.Remove(callback);
            }
        }

        private void dispatch_on_set(string key, string value, string result)
        {
            foreach (on_set callback in on_set_listeners)
            {
                try
                {
                    callback( key, value, result );
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Error thrown in set event, exception " + e);
                }
            }
        }

        private void dispatch_on_unset( string key, bool was_unset )
        {
            foreach (on_unset callback in on_unset_listeners)
            {
                try
                {
                    callback( key, was_unset );
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Error thrown in unset event, exception " + e);
                }
            }
        }
    }
}
