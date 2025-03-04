﻿using System;

namespace MIDIModificationFramework.MIDIEvents
{
    public class MIDIPortEvent : ChannelEvent
    {
        public MIDIPortEvent(double delta, byte channel) : base(delta, channel)
        {
        }

        public override MIDIEvent Clone()
        {
            return new MIDIPortEvent(DeltaTime, Channel);
        }

        public override byte[] GetData()
        {
            return new byte[] { 0xFF, 0x20, 0x01, Channel };
        }
    }
}
