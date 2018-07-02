using System.Collections.Generic;
using System.Collections.Specialized;

namespace CSSockets.Http.Definition
{
    public struct Header
    {
        public string Key { get; }
        public string Value { get; }
        public Header(string key, string value) : this() { Key = key; Value = value; }
    }
    public sealed class HeaderCollection
    {
        private readonly StringDictionary Collection = new StringDictionary();
        private readonly StringCollection Keys = new StringCollection();
        public int Length => Keys.Count;

        public HeaderCollection() { }
        public HeaderCollection(IDictionary<string, string> headers)
        {
            foreach (KeyValuePair<string, string> header in headers) Add(header.Key, header.Value);
        }
        public HeaderCollection(IEnumerable<Header> headers)
        {
            foreach (Header header in headers) Add(header.Key, header.Value);
        }
        public HeaderCollection(params Header[] headers)
        {
            foreach (Header header in headers) Add(header.Key, header.Value);
        }

        public string this[string key]
        {
            get => Collection[key.ToLower()];
            set
            {
                if (key == null) Remove(key);
                else if (!Keys.Contains(key)) Insert(key, value);
                else if (value != string.Empty) Collection[key] = value;
                else Remove(key);
            }
        }
        public Header this[int index] => new Header(Keys[index], Collection[Keys[index]]);

        public bool Exists(string key) => Keys.Contains(key.ToLower());
        public bool Add(Header header)
        {
            if (Keys.Contains(header.Key.ToLower())) return false;
            return Insert(header.Key.ToLower(), header.Value);
        }
        public bool Add(string key, string value)
        {
            if (Keys.Contains(key = key.ToLower())) return false;
            return Insert(key, value);
        }
        private bool Insert(string key, string value)
        {
            Keys.Add(key);
            Collection[key] = value;
            return true;
        }
        public bool Remove(string key)
        {
            if (!Keys.Contains(key)) return false;
            Keys.Remove(key);
            Collection.Remove(key);
            return true;
        }
        public bool Clear()
        {
            Collection.Clear();
            Keys.Clear();
            return true;
        }
    }
}
