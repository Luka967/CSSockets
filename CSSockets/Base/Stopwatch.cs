#if DEBUG
using System;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;

namespace CSSockets.Base
{
    class Lapwatch
    {
        private Stopwatch elapsed { get; }
        private Stopwatch lapse { get; }

        public Lapwatch()
        {
            elapsed = new Stopwatch();
            lapse = new Stopwatch();
        }

        public void Start()
        {
            elapsed.Start();
            lapse.Start();
        }

        public TimeSpan Lap()
        {
            TimeSpan passed = lapse.Elapsed;
            lapse.Restart();
            return passed;
        }
        public TimeSpan Elapsed => elapsed.Elapsed;

        public void Stop()
        {
            elapsed.Stop();
            lapse.Stop();
        }
    }
}
#endif