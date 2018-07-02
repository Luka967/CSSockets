using System;
using System.Collections.Generic;

namespace CSSockets.Http.Definition
{
    public struct Query
    {
        public string Key { get;set; }
        public string Value { get; set; }
        public Query(string key, string value)
        {
            Key = key;
            Value = value;
        }
        public override string ToString()
            => Key + "=" + Value;
    }
    public sealed class Queries
    {
        private readonly Dictionary<string, Query> Tokens = new Dictionary<string, Query>();
        private readonly List<string> Keys = new List<string>();
        public int Length => Keys.Count;

        public string this[string key]
        {
            get => Get(key)?.Value;
            set => Set(key, value);
        }
        public Query? this[int index] => Get(Keys[index]);

        public Query? Get(string key)
        {
            if (!Tokens.TryGetValue(key, out Query item))
                return null;
            return item;
        }
        public bool Set(string key, string value)
        {
            if (!Tokens.ContainsKey(key)) Keys.Add(key);
            Tokens[key] = new Query(key, value);
            return true;
        }
        public bool Remove(string key)
        {
            if (!Tokens.ContainsKey(key)) return false;
            Tokens.Remove(key);
            Keys.Remove(key);
            return true;
        }
        public bool Clear()
        {
            Tokens.Clear();
            Keys.Clear();
            return true;
        }

        public static bool TryParse(string s, out Queries result)
        {
            Queries temp = new Queries();
            result = null;
            if (s.Length == 0)
            {
                // empty
                result = temp;
                return true;
            }
            if (!s.StartsWith("?")) return false;
            string[] split = s.Substring(1).Split("&");
            for (int i = 0; i < split.Length; i++)
            {
                string queryToken = split[i];
                string[] queryTokenSplit = queryToken.Split("=");
                if (queryTokenSplit.Length != 2) return false;
                temp.Set(queryTokenSplit[0], queryTokenSplit[1]);
            }
            result = temp;
            return true;
        }
        public static Queries Parse(string s)
        {
            if (!TryParse(s, out Queries result))
                throw new ArgumentException("Invalid query string");
            return result;
        }

        public override string ToString()
        {
            string s = null;
            for (int i = 0; i < Keys.Count; i++)
            {
                s = s ?? "?";
                s += Keys[i] + "=" + Tokens[Keys[i]].Value + "&";
            }
            return s == null ? "" : s.Substring(0, s.Length);
        }
    }
    public sealed class Path
    {
        private enum TraverseResult : byte
        {
            Success = 0,
            NotApath = 1,
            NotRpath = 2,
            APathRdirs = 3,
            RpathInvalidRdirs = 4,
            TraverseBeyondRoot = 5
        }

        private List<string> Location { get; set; }
        public string FullPath { get; private set; }
        public string Directory { get; private set; }
        public string Entry { get; private set; }

        private void AssembleStringified()
        {
            FullPath = "/";
            Directory = "/";
            Entry = "";
            int i = 0;
            for (; i < Location.Count - 1; i++)
                FullPath = Directory += Location[i] + "/";
            FullPath += Location.Count > 0 ? (Entry += Location[i++]) : "";
        }

        private TraverseResult InternalInitialize(string path)
        {
            List<string> pathList = new List<string>();
            string[] dirs = path.Split('/');
            if (dirs[0] != "")
                return TraverseResult.NotApath;
            for (int i = 1; i < dirs.Length; i++)
            {
                if (dirs[i] == "." || dirs[i] == "..")
                    return TraverseResult.APathRdirs;
                pathList.Add(dirs[i]);
            }
            Location = pathList;
            AssembleStringified();
            return TraverseResult.Success;
        }
        public bool Initialize(string path = "/") => InternalInitialize(path) == TraverseResult.Success;

        private TraverseResult InternalTraverse(string path)
        {
            List<string> pathList = new List<string>(Location);
            string[] dirs = path.Split('/');
            if (dirs[0] == "")
                return TraverseResult.NotRpath;
            bool acceptingBackwards = true;
            for (int i = 1; i < dirs.Length; i++)
            {
                if (dirs[i] == "..")
                {
                    if (!acceptingBackwards) return TraverseResult.RpathInvalidRdirs;
                    pathList.RemoveAt(pathList.Count - 1);
                }
                else if (dirs[i] == ".") { if (!acceptingBackwards) return TraverseResult.RpathInvalidRdirs; }
                else { pathList.Add(dirs[i]); acceptingBackwards = false; }
            }
            Location = pathList;
            AssembleStringified();
            return TraverseResult.Success;
        }
        public bool Traverse(string path = "/") => InternalTraverse(path) == TraverseResult.Success;

        public bool Contains(Path other)
        {
            int checkLen = Math.Min(Location.Count, other.Location.Count);
            for (int i = 0; i < checkLen; i++) if (Location[i] != other.Location[i]) return false;
            return checkLen == Location.Count;
        }

        public Path() => Initialize();
        public Path(Path other) => Initialize(other.FullPath);
        public Path(string path) => Initialize(path);

        public override string ToString() => FullPath;

        public static implicit operator Path(string value) => new Path(value);
        public static implicit operator string(Path value) => value.FullPath;
    }
    public sealed class URL
    {
        public Path Path { get; set; }
        public string Hash { get; set; }
        public Queries Queries { get; set; }
        public string Query
        {
            get => Queries?.ToString();
            set => Queries = Queries.Parse(value);
        }

        public static URL Parse(string s)
        {
            if (!TryParse(s, out URL result))
                throw new ArgumentException("Invalid query");
            return result;
        }
        public static bool TryParse(string s, out URL result)
        {
            URL temp = new URL();
            result = null;

            string[] splitForHash = s.Split("#");
            if (splitForHash.Length > 1) temp.Hash = splitForHash[1];
            string[] splitForSearch = splitForHash[0].Split("?");
            if (splitForSearch.Length > 1)
            {
                if (!Queries.TryParse("?" + splitForSearch[1], out Queries searches))
                    return false;
                temp.Queries = searches;
            }
            Path tempPath = new Path();
            if (!tempPath.Initialize(splitForSearch[0]))
                return false;
            temp.Path = tempPath;
            result = temp;
            return true;
        }

        public URL()
        {
            Path = null;
            Hash = null;
            Queries = null;
        }
        public URL(string path, string hash, string searchString)
        {
            Path.Traverse(path);
            Hash = hash;
            Query = searchString;
        }
        public URL(Path path, string hash, Queries searches)
        {
            Path = new Path(path.FullPath);
            Hash = hash;
            Queries = searches;
        }
        public URL(string query)
        {
            URL result = Parse(query);
            Path = result.Path;
            Hash = result.Hash;
            Queries = result.Queries;
        }
        public override string ToString() =>
            (Path.ToString() ?? throw new InvalidOperationException("Path in HttpQuery is null"))
            + (Query ?? "")
            + (Hash == null ? "" : "#" + Hash);

        public static implicit operator URL(string value) => Parse(value);
        public static implicit operator string(URL value) => value.ToString();
    }
}
