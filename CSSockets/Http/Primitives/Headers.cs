using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;

namespace CSSockets.Http.Primitives
{
    sealed public class Header
    {
        public string Name { get; }
        public string Value { get; }
        public Header(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }
    sealed public class HeaderCollection : IEnumerable<Header>
    {
        public static HashSet<string> DuplicatesIgnored = new HashSet<string>()
            { "age", "authorization", "content-length", "content-type", "etag", "expires",
              "from", "host", "if-modified-since", "if-unmodified-since", "last-modified",
              "location", "max-forwards", "proxy-authorization", "referer", "retry-after",
              "user-agent" };
        private StringDictionary List { get; } = new StringDictionary();
        private List<string> HeadersAdded { get; } = new List<string>();
        public int Count => HeadersAdded.Count;
        public string LastHeaderName => HeadersAdded.Count == 0 ? null : HeadersAdded[HeadersAdded.Count - 1];

        public HeaderCollection() { }
        public HeaderCollection(IEnumerable<Header> collection)
        {
            foreach (Header header in collection) Add(header);
        }

        public void Add(Header header) => this[header.Name] = header.Value;

        public string Get(string name) => List[name.ToLower()];
        public string GetHeaderName(int index) => HeadersAdded[index];
        public IReadOnlyList<Header> AsCollection()
        {
            List<Header> list = new List<Header>();
            for (int i = 0; i < HeadersAdded.Count; i++)
                list.Add(new Header(HeadersAdded[i], Get(HeadersAdded[i])));
            return new ReadOnlyCollection<Header>(list);
        }

        public void Set(string name, string value, bool overwrite = false)
        {
            name = name.ToLower();
            string prevValue = Get(name);
            if (prevValue != null && DuplicatesIgnored.Contains(name))
                return;
            else if (prevValue != null && !overwrite)
                List[name] = prevValue + ", " + value.Trim();
            else
            {
                List[name] = value.Trim();
                HeadersAdded.Add(name);
            }
        }
        public void Remove(string name)
        {
            if (Get(name) == null) return;
            List.Remove(name);
            HeadersAdded.Remove(name);
        }

        public IEnumerator<Header> GetEnumerator() => AsCollection().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => AsCollection().GetEnumerator();

        public string this[string headerName]
        {
            get => Get(headerName);
            set { if (value == null) Remove(headerName); else Set(headerName, value); }
        }
        public string this[int index]
        {
            get => Get(HeadersAdded[index]);
            set
            {
                if (value == null)
                    Remove(HeadersAdded[index]);
                else Set(HeadersAdded[index], value);
            }
        }
    }
}
