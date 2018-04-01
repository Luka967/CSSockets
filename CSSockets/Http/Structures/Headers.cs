using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;

namespace CSSockets.Http.Structures
{
    public struct Header
    {
        public string Name { get; }
        public string Value { get; }
        public Header(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }
    public sealed class HeaderCollection : IEnumerable<Header>
    {
        public static HashSet<string> DuplicatesIgnored = new HashSet<string>()
            { "age", "authorization", "content-length", "content-type", "etag", "expires",
              "from", "host", "if-modified-since", "if-unmodified-since", "last-modified",
              "location", "max-forwards", "proxy-authorization", "referer", "retry-after",
              "user-agent" };
        private StringDictionary list { get; } = new StringDictionary();
        private List<string> headersAdded { get; } = new List<string>();
        public int Count => headersAdded.Count;
        public string LastHeaderName => headersAdded.Count == 0 ? null : headersAdded[headersAdded.Count - 1];

        public HeaderCollection() { }
        public HeaderCollection(IEnumerable<Header> collection)
        {
            foreach (Header header in collection) Add(header);
        }

        public void Add(Header header) => this[header.Name] = header.Value;

        public string Get(string name) => list[name.ToLower()];
        public string GetHeaderName(int index) => headersAdded[index];
        public IReadOnlyList<Header> AsCollection()
        {
            List<Header> list = new List<Header>();
            for (int i = 0; i < headersAdded.Count; i++)
                list.Add(new Header(headersAdded[i], Get(headersAdded[i])));
            return new ReadOnlyCollection<Header>(list);
        }

        public void Set(string name, string value, bool overwrite = false)
        {
            name = name.ToLower();
            string prevValue = Get(name);
            if (prevValue != null && DuplicatesIgnored.Contains(name))
                return;
            else if (prevValue != null && !overwrite)
                list[name] = prevValue + ", " + value.Trim();
            else
            {
                list[name] = value.Trim();
                headersAdded.Add(name);
            }
        }
        public void Remove(string name)
        {
            if (Get(name) == null) return;
            list.Remove(name);
            headersAdded.Remove(name);
        }

        public IEnumerator<Header> GetEnumerator() => AsCollection().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => AsCollection().GetEnumerator();

        public string this[string headerName]
        {
            get => Get(headerName);
            set { if (value == null) Remove(headerName); else Set(headerName, value); }
        }
        public Header this[int index]
        {
            get
            {
                string key = headersAdded[index];
                return new Header(key, list[key]);
            }
            set
            {
                if (value.Value == null)
                    Remove(headersAdded[index]);
                else Set(headersAdded[index], value.Value);
            }
        }
    }
}
