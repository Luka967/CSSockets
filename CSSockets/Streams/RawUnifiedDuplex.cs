namespace CSSockets.Streams
{
    public class RawUnifiedDuplex : UnifiedDuplex
    {
        public override byte[] Read()
        {
            ThrowIfPipedOrAsync();
            return Bread();
        }
        public override byte[] Read(int length)
        {
            ThrowIfPipedOrAsync();
            return Bread(length);
        }
        public override void Write(byte[] data) => Bhandle(data);
    }
}
