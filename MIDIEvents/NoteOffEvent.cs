using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MIDIModificationFramework.MIDIEvents
{
    public class NoteOffEvent : NoteEvent
    {
        public NoteOffEvent() : this(0, 0, 0)
        { }

        public NoteOffEvent(double delta, byte channel, byte key) : base(delta, key, channel)
        {
            Key = key;
        }

        public override MIDIEvent Clone()
        {
            return new NoteOffEvent(DeltaTime, Channel, Key);
        }

        public override byte[] GetData()
        {
            return new byte[]
            {
                (byte)(0b10000000 | Channel),
                Key,
                0
            };
        }

        public override byte[] GetData(byte[] scratch)
        {
            scratch[0] = (byte)(0b10000000 | Channel);
            scratch[1] = Key;
            scratch[2] = 0;
            return scratch;
        }
    }
}
