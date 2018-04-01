namespace CSSockets.Streams
{
    public class MemoryDuplex : UnifiedDuplex
    {
        public override byte[] Read() => Bread();
        public override byte[] Read(ulong length) => Bread(length);
        public override ulong Read(byte[] destination) => Bread(destination);

        public override bool Write(byte[] source) => Bhandle(source);
        public override bool Write(byte[] source, ulong start, ulong end)
        {
            lock (Wlock) return Write(PrimitiveBuffer.Slice(source, start, end));
        }
    }
}
