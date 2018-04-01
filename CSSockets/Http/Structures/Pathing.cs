using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CSSockets.Http.Structures
{
    internal static class StringJoiner
    {
        public static string Join<T>(this IEnumerable<T> enumerable, string delimiter)
        {
            string s = "";
            foreach (T item in enumerable) s += item.ToString() + delimiter;
            return s.Length == 0 ? s : s.Remove(s.Length - delimiter.Length, delimiter.Length);
        }
    }
    public sealed class SearchToken
    {
        public string Key { get; }
        public string Value { get; set; }
        public SearchToken(string key, string value)
        {
            Key = key;
            Value = value;
        }
        public override string ToString()
            => Key + "=" + Value;
    }
    public sealed class SearchTokenList
    {
        private readonly List<SearchToken> tokens = new List<SearchToken>();

        public int GetIndexFor(string key) => tokens.FindIndex((v) => v.Key == key);
        public SearchToken Get(string key) => tokens.Find((v) => v.Key == key);
        public void Set(string key, string value) => this[key] = value;
        public void Remove(string key) => tokens.RemoveAt(GetIndexFor(key));

        public IReadOnlyList<SearchToken> Collection
            => new ReadOnlyCollection<SearchToken>(tokens);

        public static bool TryParse(string s, out SearchTokenList result)
        {
            SearchTokenList temp = new SearchTokenList();
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
        public static SearchTokenList Parse(string s)
        {
            if (!TryParse(s, out SearchTokenList result))
                throw new ArgumentException("Invalid query string");
            return result;
        }

        public string this[string tokenKey]
        {
            get => Get(tokenKey)?.Value;
            set
            {
                int index = GetIndexFor(tokenKey);
                if (index == -1) tokens.Add(new SearchToken(tokenKey, value));
                else tokens[index].Value = value;
            }
        }

        public override string ToString() => tokens.Count == 0 ? "" : "?" + tokens.Join("&");
    }
    public sealed class Path
    {
        private enum TraverseResult : byte
        {
            Success = 0,
            NotAbsolutePath = 1,
            NotRelativePath = 2,
            AbsolutePathHasRelativeDirs = 3,
            RelativePathHasInvalidRelativeDirs = 4,
            TraversingBeyondRoot = 5
        }
        private void CheckTraverseResult(TraverseResult result)
        {
            switch (result)
            {
                case TraverseResult.AbsolutePathHasRelativeDirs:
                    throw new ArgumentException("Given absolute path contains the relative . and .. directories");
                case TraverseResult.NotAbsolutePath:
                    throw new ArgumentException("Given path is not absolute");
                case TraverseResult.NotRelativePath:
                    throw new ArgumentException("Given path is not relative");
                case TraverseResult.RelativePathHasInvalidRelativeDirs:
                    throw new ArgumentException("Given relative path tries to traverse backwards after first absolute directory name");
                case TraverseResult.TraversingBeyondRoot:
                    throw new ArgumentException("Given relative path traverses beyond root");
                default: break;
            }
        }

        private List<string> Location { get; set; }
        public string FullPath => "/" + Location.Join("/");
        public string Directory
        {
            get
            {
                string s = "";
                for (int i = 0; i < Location.Count - 1; i++)
                    s += Location[i] + "/";
                return s;
            }
        }
        public string Entry => Location[Location.Count - 1];

        private TraverseResult InternalInitialize(string path)
        {
            List<string> pathList = new List<string>();
            string[] dirs = path.Split('/');
            if (dirs[0] != "")
                return TraverseResult.NotAbsolutePath;
            for (int i = 1; i < dirs.Length; i++)
            {
                if (dirs[i] == "." || dirs[i] == "..")
                    return TraverseResult.AbsolutePathHasRelativeDirs;
                pathList.Add(dirs[i]);
            }
            Location = pathList;
            return TraverseResult.Success;
        }
        public void Initialize(string path = "/") => CheckTraverseResult(InternalInitialize(path));
        public bool TryInitialize(string path = "/") => InternalInitialize(path) == TraverseResult.Success;

        private TraverseResult InternalTraverse(string path)
        {
            List<string> pathList = new List<string>(Location);
            string[] dirs = path.Split('/');
            if (dirs[0] == "")
                return TraverseResult.NotRelativePath;
            bool acceptingBackwards = true;
            for (int i = 1; i < dirs.Length; i++)
            {
                if (dirs[i] == "..")
                {
                    if (!acceptingBackwards) return TraverseResult.RelativePathHasInvalidRelativeDirs;
                    pathList.RemoveAt(pathList.Count - 1);
                }
                else if (dirs[i] == ".") { if (!acceptingBackwards) return TraverseResult.RelativePathHasInvalidRelativeDirs; }
                else { pathList.Add(dirs[i]); acceptingBackwards = false; }
            }
            Location = pathList;
            return TraverseResult.Success;
        }
        public void Traverse(string path = "/") => CheckTraverseResult(InternalTraverse(path));
        public bool TryTraverse(string path = "/") => InternalTraverse(path) == TraverseResult.Success;

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
    public sealed class Query
    {
        public Path Path { get; set; }
        public string Hash { get; set; }
        public SearchTokenList Searches { get; set; }
        public string Search
        {
            get => Searches?.ToString();
            set => Searches = SearchTokenList.Parse(value);
        }

        public static Query Parse(string s)
        {
            if (!TryParse(s, out Query result))
                throw new ArgumentException("Invalid query");
            return result;
        }
        public static bool TryParse(string s, out Query result)
        {
            Query temp = new Query();
            result = null;

            string[] splitForHash = s.Split("#");
            if (splitForHash.Length > 1) temp.Hash = splitForHash[1];
            string[] splitForSearch = splitForHash[0].Split("?");
            if (splitForSearch.Length > 1)
            {
                if (!SearchTokenList.TryParse("?" + splitForSearch[1], out SearchTokenList searches))
                    return false;
                temp.Searches = searches;
            }
            Path tempPath = new Path();
            if (!tempPath.TryInitialize(splitForSearch[0]))
                return false;
            temp.Path = tempPath;
            result = temp;
            return true;
        }

        public Query()
        {
            Path = null;
            Hash = null;
            Searches = null;
        }
        public Query(string path, string hash, string searchString)
        {
            Path.Traverse(path);
            Hash = hash;
            Search = searchString;
        }
        public Query(Path path, string hash, SearchTokenList searches)
        {
            Path = new Path(path.FullPath);
            Hash = hash;
            Searches = searches;
        }
        public Query(string query)
        {
            Query result = Parse(query);
            Path = result.Path;
            Hash = result.Hash;
            Searches = result.Searches;
        }
        public override string ToString() =>
            (Path.ToString() ?? throw new InvalidOperationException("Path in HttpQuery is null"))
            + (Search ?? "")
            + (Hash == null ? "" : "#" + Hash);

        public static implicit operator Query(string value) => Parse(value);
        public static implicit operator string(Query value) => value.ToString();
    }
}
