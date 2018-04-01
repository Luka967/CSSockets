namespace CSSockets.WebSockets
{
    public partial class WebSocket
    {
        public struct FrameBehavior
        {
            public bool? IncomingRSV1 { get; set; }
            public bool? IncomingRSV2 { get; set; }
            public bool? IncomingRSV3 { get; set; }
            public bool? IncomingMask { get; set; }
            public bool OutgoingRSV1 { get; set; }
            public bool OutgoingRSV2 { get; set; }
            public bool OutgoingRSV3 { get; set; }
            public bool OutgoingMask { get; set; }

            public FrameBehavior(bool? incomingRSV1, bool? incomingRSV2, bool? incomingRSV3, bool? incomingMask, bool outgoingRSV1, bool outgoingRSV2, bool outgoingRSV3, bool outgoingMask) : this()
            {
                IncomingRSV1 = incomingRSV1;
                IncomingRSV2 = incomingRSV2;
                IncomingRSV3 = incomingRSV3;
                IncomingMask = incomingMask;
                OutgoingRSV1 = outgoingRSV1;
                OutgoingRSV2 = outgoingRSV2;
                OutgoingRSV3 = outgoingRSV3;
                OutgoingMask = outgoingMask;
            }

            public bool Test(Frame frame)
            {
                if (IncomingRSV1 != null && frame.RSV1 != IncomingRSV1) return false;
                if (IncomingRSV2 != null && frame.RSV2 != IncomingRSV2) return false;
                if (IncomingRSV3 != null && frame.RSV3 != IncomingRSV3) return false;
                if (IncomingMask != null && frame.Masked != IncomingMask) return false;
                return true;
            }
            public Frame Get(bool fin, byte opcode, byte[] payload) => new Frame(fin, opcode, OutgoingMask, payload, OutgoingRSV1, OutgoingRSV2, OutgoingRSV3);
        }
    }
}
