﻿using System;
using System.Text;
using Verse.DecoderDescriptors.Base;
using Verse.DecoderDescriptors.Tree;
using Verse.Schemas.Protobuf.Definition;

namespace Verse.Schemas.Protobuf
{
    static class Reader
    {
        public static uint ReadInt32(ReaderState state)
        {
            uint u32;

            u32 = (uint)state.Stream.ReadByte();
            u32 += (uint)state.Stream.ReadByte() << 8;
            u32 += (uint)state.Stream.ReadByte() << 16;
            u32 += (uint)state.Stream.ReadByte() << 24;

            return u32;
        }

        public static ulong ReadInt64(ReaderState state)
        {
            ulong u64;

            u64 = (ulong)state.Stream.ReadByte();
            u64 += (ulong)state.Stream.ReadByte() << 8;
            u64 += (ulong)state.Stream.ReadByte() << 16;
            u64 += (ulong)state.Stream.ReadByte() << 24;
            u64 += (ulong)state.Stream.ReadByte() << 32;
            u64 += (ulong)state.Stream.ReadByte() << 40;
            u64 += (ulong)state.Stream.ReadByte() << 48;
            u64 += (ulong)state.Stream.ReadByte() << 56;

            return u64;
        }

        public static ulong ReadVarInt(ReaderState state)
        {
            byte current;
            var shift = 0;
            var u64 = 0UL;
    
            do
            {
                current = (byte)state.Stream.ReadByte();
                u64 += (current & 127u) << shift;
                shift += 7;
            }
            while ((current & 128u) != 0);

            return u64;
        }
    }

    class Reader<TEntity> : TreeReader<TEntity, ReaderState, ProtobufValue>
    {
        private static readonly Reader<TEntity> emptyReader = new Reader<TEntity>(new ProtoBinding[0], false);

    	private EntityReader<TEntity, ReaderState> array = null;

        private readonly ProtoBinding[] bindings;

        private readonly EntityReader<TEntity, ReaderState>[] fields;

    	private readonly ulong maximumLength = 128 * 1024 * 1024;

        private readonly bool rejectUnknown;

        public Reader(ProtoBinding[] bindings, bool rejectUnknown)
        {
            this.bindings = bindings;
            this.fields = new EntityReader<TEntity, ReaderState>[bindings.Length];
            this.rejectUnknown = rejectUnknown;
        }

        public override BrowserMove<TEntity> Browse(Func<TEntity> constructor, ReaderState state)
        {
            throw new NotImplementedException();
        }

		public override TreeReader<TField, ReaderState, ProtobufValue> HasField<TField>(string name, EntityReader<TEntity, ReaderState> enter)
		{
			int index = Array.FindIndex(this.bindings, binding => binding.Name == name);

            if (index < 0)
            	throw new ArgumentOutOfRangeException("name", name, "field doesn't exist in proto definition");

            this.fields[index] = enter;

            return new Reader<TField>(this.bindings[index].Fields, this.rejectUnknown);
		}

		public override TreeReader<TItem, ReaderState, ProtobufValue> HasItems<TItem>(EntityReader<TEntity, ReaderState> enter)
		{
			if (this.array != null)
				throw new InvalidOperationException("can't declare array twice on same descriptor");

			this.array = enter;

			return new Reader<TItem>(this.bindings, this.rejectUnknown);
		}

        public override bool Read(ref TEntity entity, ReaderState state)
        {
            byte[] buffer;
            int current;
            ProtoBinding field;
            uint u32;
            ulong u64;

            if (this.array != null)
                return this.array(ref entity, state);

            if (this.HoldValue)
            {
            	entity = this.ConvertValue(state.Value);

                return true;
            }

            current = state.Stream.ReadByte();

            if (current < 0)
                return true;

            // Read field number and wire type
            var index = (current >> 3) & 15;
            var wire = (WireType)(current & 7);

            while ((current & 128) != 0)
            {
                current = state.Stream.ReadByte();
                index = (index << 8) + current;
            }

            // Decode value
            if (index >= 0 && index < this.bindings.Length && this.bindings[index].Type != ProtoType.Undefined)
                field = this.bindings[index];
            else if (!this.rejectUnknown)
                field = ProtoBinding.Empty;
            else
            {
                state.RaiseError("field {0} with wire type {1} is unknown", index, wire);

                return false;
            }

            switch (wire)
            {
                case WireType.Fixed32:
                    u32 = Reader.ReadInt32(state);

                    switch (field.Type)
                    {
                        case ProtoType.Fixed32:
                            state.Value = new ProtobufValue(u32);

                            break;

                        case ProtoType.Float:
                            unsafe
                            {
                                state.Value = new ProtobufValue(*((float*)&u32));
                            }

                            break;

                        case ProtoType.SFixed32:
                            state.Value = new ProtobufValue((int)u32);

                            break;

                        case ProtoType.Undefined:
                            break;

                        default:
                            state.RaiseError("field {0} is incompatible with wire type {1}", field.Name, wire);

                            return false;
                    }

                    break;

                case WireType.Fixed64:
                    u64 = Reader.ReadInt64(state);

                    switch (field.Type)
                    {
                        case ProtoType.Double:
                            unsafe
                            {
                                state.Value = new ProtobufValue(*((double*)&u64));
                            }

                            break;

                        case ProtoType.Fixed64:
                            state.Value = new ProtobufValue(u64);

                            break;

                        case ProtoType.SFixed64:
                            state.Value = new ProtobufValue((long)u64);

                            break;

                        case ProtoType.Undefined:
                            break;

                        default:
                            state.RaiseError("field {0} is incompatible with wire type {1}", field.Name, wire);

                            return false;
                    }

                    break;

                case WireType.GroupBegin:
                    if (field.Type != ProtoType.Undefined)
                    {
                        state.RaiseError("groups are not supported");

                        return false;
                    }

                    break;

                case WireType.GroupEnd:
                    if (field.Type != ProtoType.Undefined)
                    {
                        state.RaiseError("groups are not supported");

                        return false;
                    }

                    break;

                case WireType.LengthDelimited:
                    switch (field.Type)
                    {
                        case ProtoType.Custom:
                            throw new NotImplementedException();

                        case ProtoType.String:
                            u64 = Reader.ReadVarInt(state);

                            if (u64 > this.maximumLength)
                            {
                                state.RaiseError("number of bytes in field {0} ({1}) exceeds allowed maximum ({2})", field.Name, u64, this.maximumLength);

                                return false;
                            }

                            buffer = new byte[u64];

                            if (state.Stream.Read(buffer, 0, (int)u64) != (int)u64)
                                return false;

                            state.Value = new ProtobufValue(Encoding.UTF8.GetString(buffer));

                            break;

                        case ProtoType.Undefined:
                            break;

                        default:
                            state.RaiseError("field {0} is incompatible with wire type {1}", field.Name, wire);

                            return false;
                    }

                    return false;

                case WireType.VarInt:
                    u64 = Reader.ReadVarInt(state);

                    switch (field.Type)
                    {
                        case ProtoType.Boolean:
                            state.Value = new ProtobufValue(u64 != 0);

                            break;

                        case ProtoType.Int32:
                            state.Value = new ProtobufValue((int)u64);

                            break;

                        case ProtoType.Int64:
                            state.Value = new ProtobufValue((long)u64);

                            break;

                        case ProtoType.SInt32:
                        case ProtoType.SInt64:
                            state.Value = new ProtobufValue((-(long)(u64 & 1)) ^ (long)(u64 >> 1));

                            break;

                        case ProtoType.UInt32:
                        case ProtoType.UInt64:                        
                            state.Value = new ProtobufValue(u64);

                            break;

                        case ProtoType.Undefined:
                            break;

                        default:
                            state.RaiseError("field {0} is incompatible with wire type {1}", field.Name, wire);

                            return false;
                    }

                    break;

                default:
                    state.RaiseError("field {0} has unsupported wire type {1}", field.Name, wire);

                    return false;
            }

            return this.fields[index] != null
                ? this.fields[index](ref entity, state)
                : Reader<TEntity>.Ignore(state);
        }

		private static bool Ignore(ReaderState state)
		{
			var dummy = default(TEntity);

			return Reader<TEntity>.emptyReader.Read(ref dummy, state);
		}
    }
}
