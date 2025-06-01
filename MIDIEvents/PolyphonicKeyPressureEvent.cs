using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MIDIModificationFramework.MIDIEvents
{
    public class PolyphonicKeyPressureEvent : NoteEvent
    {
        public byte Pressure { get; set; }

        public PolyphonicKeyPressureEvent(double delta, byte channel, byte key, byte pressure) : base(delta, key, channel)
        {
            Key = key;
            Pressure = pressure;
        }

        public override MIDIEvent Clone()
        {
            return new PolyphonicKeyPressureEvent(DeltaTime, Channel, Key, Pressure);
        }

        public override byte[] GetData()
        {
            return new byte[]
            {
                (byte)(0b10100000 | Channel),
                Key,
                Pressure
            };
        }
    }
}
