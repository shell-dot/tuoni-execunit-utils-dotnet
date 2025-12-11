using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ExecUnitUtils
{
    /// <summary>
    /// Represents a Type-Length-Value (TLV) structure, supporting both primitive data and nested children.
    /// Type: 1 byte (high bit indicates parent), Length: 4 bytes (little-endian), Value: data or children.
    /// </summary>
    public class TLV
    {
        public byte Type { get; private set; } = 0;
        public bool IsParent { get; private set; } = false;
        private Dictionary<byte, List<TLV>> _children = null;
        public byte[] Data { get; private set; } = null;
        public uint FullSize { get; private set; } = 0;

        public TLV()
        {
        }

        /// <summary>
        /// Constructs a leaf TLV with primitive data.
        /// </summary>
        public TLV(byte typeIn, byte[] dataIn)
        {
            Type = typeIn;
            Data = dataIn ?? throw new ArgumentNullException(nameof(dataIn));
            IsParent = false;
            FullSize = checked((uint)dataIn.Length + 5);
        }

        /// <summary>
        /// Constructs a parent TLV for holding children.
        /// </summary>
        public TLV(byte typeIn)
        {
            Type = typeIn;
            IsParent = true;
            _children = new Dictionary<byte, List<TLV>>();
            FullSize = 5;
        }

        /// <summary>
        /// Loads a TLV from a buffer starting at the given offset.
        /// </summary>
        /// <param name="buffer">The input buffer.</param>
        /// <param name="offset">Starting position.</param>
        /// <returns>True if loaded successfully.</returns>
        public bool Load(byte[] buffer, int offset = 0)
        {
            if (buffer.Length - offset < 5)
                return false;

            Type = (byte)(buffer[offset] & 0x7F);
            IsParent = (buffer[offset] & 0x80) > 0;
            offset += 1;

            uint len = BitConverter.ToUInt32(buffer, offset);
            offset += 4;

            if (buffer.Length - offset < len)
                return false;

            FullSize = len + 5;

            if (!IsParent)
            {
                Data = new byte[len];
                Array.Copy(buffer, offset, Data, 0, (int)len);
                return true;
            }

            _children = new Dictionary<byte, List<TLV>>();
            uint remaining = len;
            while (remaining != 0)
            {
                TLV child = new TLV();
                if (!child.Load(buffer, offset))
                    return false;

                AddChildInternal(child);
                if (child.FullSize > remaining)
                    return false;

                offset += (int)child.FullSize;
                remaining -= child.FullSize;
            }
            return true;
        }

        /// <summary>
        /// Adds a child TLV to this parent.
        /// </summary>
        /// <param name="child">The child to add.</param>
        /// <returns>True if added.</returns>
        public bool AddChild(TLV child)
        {
            if (!IsParent)
                throw new InvalidOperationException("Cannot add child to a non-parent TLV.");

            AddChildInternal(child);
            FullSize = checked(FullSize + child.FullSize);
            return true;
        }

        private void AddChildInternal(TLV child)
        {
            if (!_children.ContainsKey(child.Type))
                _children.Add(child.Type, new List<TLV>());
            _children[child.Type].Add(child);
        }

        /// <summary>
        /// Gets a child by type and index.
        /// </summary>
        public TLV GetChild(byte childType, int idx = 0)
        {
            if (!_children?.ContainsKey(childType) ?? true)
                return null;
            if (idx < 0 || idx >= _children[childType].Count)
                return null;
            return _children[childType][idx];
        }

        /// <summary>
        /// Gets the count of children for a type.
        /// </summary>
        public int GetChildCount(byte childType)
        {
            return _children?.ContainsKey(childType) ?? false ? _children[childType].Count : 0;
        }

        /// <summary>
        /// Serializes the full TLV to a byte array.
        /// </summary>
        /// <returns>The serialized buffer.</returns>
        public byte[] GetFullBuffer()
        {
            if (FullSize > int.MaxValue)
                throw new InvalidOperationException("TLV too large for MemoryStream (exceeds int.MaxValue).");

            using (var stream = new MemoryStream((int)FullSize))
            {
                using (var writer = new BinaryWriter(stream))
                {
                    WriteFullBuffer(writer);
                    return stream.ToArray();
                }
            }
        }

        private void WriteFullBuffer(BinaryWriter writer)
        {
            writer.Write((byte)(Type | (IsParent ? 0x80 : 0)));

            uint valueLen = FullSize - 5;
            writer.Write(valueLen);

            if (!IsParent)
            {
                writer.Write(Data);
            }
            else
            {
                foreach (var childList in _children.Values)
                    foreach (var child in childList)
                        child.WriteFullBuffer(writer);
            }
        }

        // Data conversion methods

        /// <summary>
        /// Gets data as UTF-8 string.
        /// </summary>
        public string GetAsString()
        {
            if (IsParent || Data == null) return null;
            return Encoding.UTF8.GetString(Data);
        }

        private byte[] PrepareDataForConversion(int expectedLength)
        {
            if (IsParent || Data == null || Data.Length != expectedLength)
                return null;

            byte[] prepared = new byte[expectedLength];
            Array.Copy(Data, prepared, expectedLength);

            return prepared;
        }

        /// <summary>
        /// Gets data as byte.
        /// </summary>
        public byte GetAsByte()
        {
            if (IsParent || Data == null || Data.Length != 1)
                throw new InvalidOperationException("Ivalid data content");
            return Data[0];
        }

        /// <summary>
        /// Gets data as signed byte.
        /// </summary>
        public sbyte GetAsSByte()
        {
            if (IsParent || Data == null || Data.Length != 1)
                throw new InvalidOperationException("Ivalid data content");
            return (sbyte)Data[0];
        }

        /// <summary>
        /// Gets data as boolean (non-zero is true).
        /// </summary>
        public bool GetAsBool()
        {
            if (IsParent || Data == null || Data.Length != 1)
                throw new InvalidOperationException("Ivalid data content");
            return Data[0] != 0;
        }

        /// <summary>
        /// Gets data as Int16.
        /// </summary>
        public short GetAsInt16()
        {
            var prepared = PrepareDataForConversion(2);
            return BitConverter.ToInt16(prepared, 0);
        }

        /// <summary>
        /// Gets data as UInt16.
        /// </summary>
        public ushort GetAsUInt16()
        {
            var prepared = PrepareDataForConversion(2);
            return BitConverter.ToUInt16(prepared, 0);
        }

        /// <summary>
        /// Gets data as Int32.
        /// </summary>
        public int GetAsInt32()
        {
            var prepared = PrepareDataForConversion(4);
            return BitConverter.ToInt32(prepared, 0);
        }

        /// <summary>
        /// Gets data as UInt32.
        /// </summary>
        public uint GetAsUInt32()
        {
            var prepared = PrepareDataForConversion(4);
            return BitConverter.ToUInt32(prepared, 0);
        }

        /// <summary>
        /// Gets data as Int64.
        /// </summary>
        public long GetAsInt64()
        {
            var prepared = PrepareDataForConversion(8);
            return BitConverter.ToInt64(prepared, 0);
        }

        /// <summary>
        /// Gets data as UInt64.
        /// </summary>
        public ulong GetAsUInt64()
        {
            var prepared = PrepareDataForConversion(8);
            return BitConverter.ToUInt64(prepared, 0);
        }

        /// <summary>
        /// Gets data as float (Single).
        /// </summary>
        public float GetAsSingle()
        {
            var prepared = PrepareDataForConversion(4);
            return BitConverter.ToSingle(prepared, 0);
        }

        /// <summary>
        /// Gets data as double.
        /// </summary>
        public double GetAsDouble()
        {
            var prepared = PrepareDataForConversion(8);
            return BitConverter.ToDouble(prepared, 0);
        }

        /// <summary>
        /// Gets a copy of the raw data.
        /// </summary>
        public byte[] GetAsBytes()
        {
            if (IsParent || Data == null) return null;
            byte[] copy = new byte[Data.Length];
            Array.Copy(Data, copy, Data.Length);
            return copy;
        }

        /// <summary>
        /// Returns a string representation for debugging.
        /// </summary>
        public override string ToString()
        {
            return $"TLV(Type={Type}, IsParent={IsParent}, FullSize={FullSize}, ChildrenCount={(_children?.Count ?? 0)})";
        }
    }
}