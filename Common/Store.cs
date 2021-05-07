using System;
using System.Collections.Generic;

namespace DarkMultiPlayerCommon
{
    public class Store
    {
        public static Store singleton = new Store();

        private Dictionary<string, string> store;


        public Store()
        {
            store = new Dictionary<string, string>();
        }

        public string get( string key )
        {
            lock ( store )
            {
                return store.ContainsKey( key ) ? store[ key ] : null;
            }
        }

        public string set( string key, string value )
        {
            lock ( store )
            {
				// if the key is already set,
				// return the previously set value
				if ( store.ContainsKey( key ) ) {
					return store[ key ];
				}

				store[ key ] = value;
				return value;
            }
        }

        public string unset( string key )
        {
            lock ( store )
            {
                if ( store.ContainsKey( key ) )
                {
					string value = store[ key ];
					store.Remove( key );
					return value;
				}

                return null;
            }
        }

        public void unset_by_value(string value)
        {
            lock (store)
            {
                List< string > remove_list = new List< string >();
                foreach ( KeyValuePair< string, string > kvp in store )
                {
                    if ( kvp.Value == value )
                    {
                        remove_list.Add( kvp.Key );
                    }
                }
                foreach (string key in remove_list)
                {
                    store.Remove( key );
                }
            }
        }

        public Dictionary<string, string> as_dictionary()
        {
            lock (store)
            {
                //Return a copy.
                return new Dictionary<string, string>(store);
            }
        }

        public void clear()
        {
            lock (store)
            {
                store.Clear();
            }
        }
    }
}