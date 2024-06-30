using Microsoft.Extensions.ObjectPool;
using MIDIModificationFramework.MIDIEvents;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MIDIModificationFramework
{
    public class EventParser : IDisposable
    {
        enum EventType
        {
            TrackStart = 0x00,
            ChannelPrefix = 0x20,
            MIDIPort = 0x21,
            TrackEnd = 0x2F,
            Tempo = 0x51,
            SMPTEOffset = 0x54,
            TimeSignature = 0x58,
            KeySignature = 0x59,

            NoteOff = 0x80,
            NoteOn = 0x90,
            Aftertouch = 0xA0,
            CC = 0xB0,
            PatchChange = 0xC0,
            ChannelPressure = 0xD0,
            PitchBend = 0xE0,

            SystemMessageStart = 0xF0,
            SystemMessageEnd = 0xF7,

            MIDITCQF = 0xF1,
            SongPositionPointer = 0xF2,
            SongSelect = 0xF3,
            TuneRequest = 0xF6,
            TimingClock = 0xF8,
            Start = 0xFA,
            Continue = 0xFB,
            Stop = 0xFC,
            ActiveSensing = 0xFE,
            SystemReset = 0xFF,

            Unknown1 = 0xF1,
            Unknown2 = 0xF4,
            Unknown3 = 0xF5,
            Unknown4 = 0xF9,
            Unknown5 = 0xFD
        };

        class StreamByteReader : IByteReader
        {
            Stream stream;

            public StreamByteReader(Stream s)
            {
                stream = s;
            }

            public void Dispose() => stream.Dispose();
            public byte Read()
            {
                int b = stream.ReadByte();
                if (b == -1) throw new EndOfStreamException();
                return (byte)b;
            }
        }

        IByteReader reader;
        long TrackTime { get; set; } = 0;

        ObjectPool<NoteOnEvent> noteOnPool;
        ObjectPool<NoteOffEvent> noteOffPool;

        public bool Ended { get; private set; } = false;

        internal EventParser(IByteReader reader, ObjectPool<NoteOnEvent> noteOnPool, ObjectPool<NoteOffEvent> noteOffPool)
        {
            this.reader = reader;
            this.noteOnPool = noteOnPool;
            this.noteOffPool = noteOffPool;
        }

        public EventParser(Stream reader)
        {
            this.reader = new StreamByteReader(reader);
        }

        uint ReadVariableLen()
        {
            long n = 0;
            while (true)
            {
                byte curByte = Read();
                n = (n << 7) | (byte)(curByte & 0x7F);
                if ((curByte & 0x80) == 0)
                {
                    break;
                }
            }
            return (uint)n;
        }

        int pushback = -1;
        byte Read()
        {
            if (pushback != -1)
            {
                byte p = (byte)pushback;
                pushback = -1;
                return p;
            }
            return reader.Read();
        }

        byte prevCommand;
        public MIDIEvent ParseNextEvent(ref uint deltaTime)
        {
            if (Ended) return null;

            uint delta = ReadVariableLen();
            TrackTime += delta;
            deltaTime = delta;

            byte command = Read();
            if (command < 0x80)
            {
                pushback = command;
                command = prevCommand;
            }
            prevCommand = command;

            byte status = (byte)(command & 0xF0);
            byte channel = (byte)(command & 0xF);
            EventType eventType = (EventType)command;

            switch ((EventType)status)
            {
                case EventType.NoteOn:
                    {
                        byte note = Read();
                        byte vel = Read();
                        if (vel == 0)
                        {
                            NoteOffEvent ret2 = noteOffPool?.Get() ?? new NoteOffEvent();
                            ret2.DeltaTime = delta;
                            ret2.Channel = channel;
                            ret2.Key = note;
                            return ret2;
                        }
                        NoteOnEvent ret = noteOnPool?.Get() ?? new NoteOnEvent();
                        ret.DeltaTime = delta;
                        ret.Channel = channel;
                        ret.Key = note;
                        ret.Velocity = vel;
                        return ret;
                    }

                case EventType.NoteOff:
                    {
                        NoteOffEvent ret = noteOffPool?.Get() ?? new NoteOffEvent();
                        byte note = Read();
                        Read();
                        ret.DeltaTime = delta;
                        ret.Channel = channel;
                        ret.Key = note;
                        return ret;
                    }

                case EventType.Aftertouch:
                    {
                        byte note = Read();
                        byte vel = Read();
                        return new PolyphonicKeyPressureEvent(delta, channel, note, vel);
                    }

                case EventType.CC:
                    {
                        byte cc = Read();
                        byte vv = Read();
                        return new ControlChangeEvent(delta, command, cc, vv);
                    }

                case EventType.PatchChange:
                    {
                        byte program = Read();
                        return new ProgramChangeEvent(delta, command, program);
                    }

                case EventType.ChannelPressure:
                    {
                        byte pressure = Read();
                        return new ChannelPressureEvent(delta, command, pressure);
                    }

                case EventType.PitchBend:
                    {
                        byte var1 = Read();
                        byte var2 = Read();
                        return new PitchWheelChangeEvent(delta, command, (short)(((var2 << 7) | var1) - 8192));
                    }

                case EventType.SystemMessageStart:
                    {
                        switch (eventType)
                        {
                            case EventType.SystemMessageStart:
                                {
                                    List<byte> data = new List<byte>() { command };
                                    byte b = 0;
                                    while ((EventType)b != EventType.SystemMessageEnd)
                                    {
                                        b = Read();
                                        data.Add(b);
                                    }
                                    return new SystemExclusiveMessageEvent(delta, data.ToArray());
                                }
                            case EventType.MIDITCQF:
                            case EventType.Unknown2:
                            case EventType.Unknown3:
                            case EventType.Unknown4:
                            case EventType.Unknown5:
                                return new UndefinedEvent(delta, command);

                            case EventType.SongPositionPointer:
                                {
                                    byte var1 = Read();
                                    byte var2 = Read();
                                    return new SongPositionPointerEvent(delta, (ushort)((var2 << 7) | var1));
                                }

                            case EventType.SongSelect:
                                {
                                    byte pos = Read();
                                    return new SongSelectEvent(delta, pos);
                                }

                            case EventType.TuneRequest:
                                return new TuneRequestEvent(delta);

                            case EventType.SystemMessageEnd:
                                throw new ArgumentException($"SysEx end with no SysEx start?");

                            case EventType.Start:
                            case EventType.Continue:
                            case EventType.Stop:
                            case EventType.TimingClock:
                            case EventType.ActiveSensing:
                                return new MajorMidiMessageEvent(delta, command);

                            case EventType.SystemReset:
                                {
                                    command = Read();

                                    switch ((EventType)command)
                                    {
                                        case EventType.TrackStart:
                                            {
                                                byte st = Read();
                                                if (st != 2)
                                                    throw new ArgumentException($"TrackStart >> Expected 2 but got {st}");

                                                return new TrackStartEvent();
                                            }

                                        case EventType.TrackEnd:
                                            {
                                                command = Read();
                                                if (command != 0)
                                                    throw new ArgumentException($"TrackEnd >> Expected 0 but got {command}");

                                                Ended = true;
                                                return null;
                                            }

                                        case EventType.ChannelPrefix:
                                            {
                                                command = Read();
                                                if (command != 1)
                                                    throw new ArgumentException($"ChannelPrefix >> Expected 1 but got {command}");

                                                return new ChannelPrefixEvent(delta, Read());
                                            }

                                        case EventType.MIDIPort:
                                            {
                                                command = Read();
                                                if (command != 1)
                                                    throw new ArgumentException($"MIDIPort >> Expected 1 but got {command}");

                                                return new MIDIPortEvent(delta, Read());
                                            }

                                        case EventType.Tempo:
                                            {
                                                command = Read();
                                                if (command != 3)
                                                    throw new ArgumentException($"Tempo >> Expected 3 but got {command}");

                                                int btempo = 0;
                                                for (int i = 0; i != 3; i++)
                                                    btempo = (btempo << 8) | Read();
                                                return new TempoEvent(delta, btempo);
                                            }

                                        case EventType.SMPTEOffset:
                                            {
                                                command = Read();
                                                if (command != 5)
                                                    throw new ArgumentException($"SMPTEOffset >> Expected 5 but got {command}");

                                                byte hr = Read();
                                                byte mn = Read();
                                                byte se = Read();
                                                byte fr = Read();
                                                byte ff = Read();
                                                return new SMPTEOffsetEvent(delta, hr, mn, se, fr, ff);
                                            }

                                        case EventType.TimeSignature:
                                            {
                                                command = Read();
                                                if (command != 4)
                                                    throw new ArgumentException($"TimeSignature >> Expected 4 but got {command}");

                                                byte nn = Read();
                                                byte dd = Read();
                                                byte cc = Read();
                                                byte bb = Read();
                                                return new TimeSignatureEvent(delta, nn, dd, cc, bb);
                                            }

                                        case EventType.KeySignature:
                                            {
                                                command = Read();
                                                if (command != 2)
                                                    throw new ArgumentException($"KeySignature >> Expected 2 but got {command}");

                                                byte sf = Read();
                                                byte mi = Read();
                                                return new KeySignatureEvent(delta, sf, mi);
                                            }
                                    }

                                    if ((command >= 0x01 && command <= 0x0A) || command == 0x7F)
                                    {
                                        int size = (int)ReadVariableLen();
                                        var data = new byte[size];
                                        for (int i = 0; i < size; i++) data[i] = Read();
                                        if (command == 0x0A &&
                                            (size == 8 || size == 12) &&
                                            data[0] == 0x00 && data[1] == 0x0F &&
                                            (data[2] < 16 || data[2] == 7F) &&
                                            data[3] == 0)
                                        {
                                            if (data.Length == 8)
                                            {
                                                return new ColorEvent(delta, data[2], data[4], data[5], data[6], data[7]);
                                            }
                                            return new ColorEvent(delta, data[2], data[4], data[5], data[6], data[7], data[8], data[9], data[10], data[11]);
                                        }
                                        else return new TextEvent(delta, command, data);
                                    }
                                    else break;
                                }                         
                        }

                        return new UndefinedEvent(delta, command);
                    }

                default:
                    return new UndefinedEvent(delta, command);
            }
        }

        public void Dispose()
        {
            reader.Dispose();
        }
    }
}