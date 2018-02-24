using System;
using System.Collections.Generic;
using System.Text;

namespace CSSockets.Http.Base
{
    public delegate void OutputterHandler<T>(T item);
    public interface IBlockingOutputter<T>
    {
        event OutputterHandler<T> OnOutput;
        T Next();
    }
    public interface IQueueingOutputter<T> : IBlockingOutputter<T>
    {
        int Queued { get; }
    }
}
