using System;
using MPS.Time;

namespace MPS.Tests.Util
{
    public class MockMasterClock : IMasterClock
    {
        public DateTime CurrentTime { get; set; }

        public DateTime Now => CurrentTime;
    }
}
