using System;
using System.Collections.Generic;
using System.Text;

namespace CSSockets.Streams
{
    sealed public class WrappedReadable : IBufferedReadable
    {
        private IBufferedDuplex Base { get; }

        public IWritable PipedTo => Base.PipedTo;
        public bool Ended => Base.Ended;
        public long ReadBytes => Base.ReadBytes;
        public int IncomingBuffered => Base.IncomingBuffered;
        public bool Paused => Base.Paused;

        public event DataHandler OnData
        {
            add => Base.OnData += value;
            remove => Base.OnData -= value;
        }

        public void End() => Base.End();
        public void Pipe(IWritable to) => Base.Pipe(to);
        public byte[] Read() => Base.Read();
        public byte[] Read(int length) => Base.Read(length);
        public void Unpipe() => Base.Unpipe();
        public void Pause() => Base.Pause();
        public void Resume() => Base.Resume();
    }

    sealed public class WrappedWritble : IBufferedWritable
    {
        private IBufferedDuplex Base { get; }
        public long WrittenBytes => Base.WrittenBytes;
        public int OutgoingBuffered => Base.OutgoingBuffered;
        public bool Ended => Base.Ended;
        public bool Corked => Base.Corked;

        public void Cork() => Base.Cork();
        public void End() => Base.End();
        public void Uncork() => Base.Uncork();
        public void Unpipe(IReadable from) => Base.Unpipe(from);
        public void Write(byte[] data) => Base.Write(data);
        public void Write(byte[] data, int offset, int length) => Base.Write(data, offset, length);
    }
}
