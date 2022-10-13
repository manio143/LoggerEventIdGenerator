using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LoggerEventIdGenerator
{
    // MIT License Copyright (c) 2017-2022 Tommaso Belluzzo
    // https://github.com/TommasoBelluzzo/FastHashes
    /// <summary>
    /// Definition of a hashing algorithm MetroHash resulting in a 64bit hash value.
    /// </summary>
    public static class MetroHash64
    {
        private const ulong K0 = 0xD6D018F5ul;
        private const ulong K1 = 0xA2AA033Bul;
        private const ulong K2 = 0x62992FC1ul;
        private const ulong K3 = 0x30BC5B29ul;

        public static ulong Run(string input) => 
            Run(MemoryMarshal.Cast<char, byte>(input.AsSpan()));

        public static ulong Run(ReadOnlySpan<byte> input)
        {
            int offset = 0;
            int count = input.Length;

            ulong hash = K2 * K0;

            if (count == 0)
            {
                hash ^= RotateRight(hash, 33);
                hash *= K0;
                hash ^= RotateRight(hash, 33);

                return hash;
            }

            hash += (ulong)count;

            if (count >= 32)
            {
                ulong v1 = hash;
                ulong v2 = hash;
                ulong v3 = hash;
                ulong v4 = hash;

                do
                {
                    ulong z1 = Read64(input, offset);
                    offset += 8;
                    ulong z2 = Read64(input, offset);
                    offset += 8;
                    ulong z3 = Read64(input, offset);
                    offset += 8;
                    ulong z4 = Read64(input, offset);
                    offset += 8;

                    v1 = Mix256(v1, z1, v3, 29, K0);
                    v2 = Mix256(v2, z2, v4, 29, K1);
                    v3 = Mix256(v3, z3, v1, 29, K2);
                    v4 = Mix256(v4, z4, v2, 29, K3);
                }
                while ((count - 32) >= offset);

                v3 ^= RotateRight(((v1 + v4) * K0) + v2, 33) * K1;
                v4 ^= RotateRight(((v2 + v3) * K1) + v1, 33) * K0;
                v1 ^= RotateRight(((v1 + v3) * K0) + v4, 33) * K1;
                v2 ^= RotateRight(((v2 + v4) * K1) + v3, 33) * K0;

                hash += v1 ^ v2;
            }

            if ((count - offset) >= 16)
            {
                ulong z1 = Read64(input, offset);
                offset += 8;
                ulong z2 = Read64(input, offset);
                offset += 8;

                ulong v1 = hash;
                ulong v2 = hash;

                v1 = Mix128(v1, z1, 33, K0, K1);
                v2 = Mix128(v2, z2, 33, K1, K2);

                v1 ^= RotateRight(v1 * K0, 35) + v2;
                v2 ^= RotateRight(v2 * K3, 35) + v1;

                hash += v2;
            }

            if ((count - offset) >= 8)
            {
                ulong z = Read64(input, offset);
                offset += 8;

                hash = Mix64(hash, z, 33, K3, K1);
            }

            if ((count - offset) >= 4)
            {
                uint z = Read32(input, offset);
                offset += 4;

                hash = Mix32(hash, z, 15, K3, K1);
            }

            if ((count - offset) >= 2)
            {
                ushort z = Read16(input, offset);
                offset += 2;

                hash = Mix16(hash, z, 13, K3, K1);
            }

            if ((count - offset) >= 1)
                hash = Mix8(hash, input[offset], 25, K3, K1);

            hash ^= RotateRight(hash, 33);
            hash *= K0;
            hash ^= RotateRight(hash, 33);

            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix8(ulong v1, byte v2, int r, ulong k1, ulong k2)
        {
            v1 += v2 * k1;
            v1 ^= RotateRight(v1, r) * k2;

            return v1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix16(ulong v1, ushort v2, int r, ulong k1, ulong k2)
        {
            v1 += v2 * k1;
            v1 ^= RotateRight(v1, r) * k2;

            return v1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix32(ulong v1, uint v2, int r, ulong k1, ulong k2)
        {
            v1 += v2 * k1;
            v1 ^= RotateRight(v1, r) * k2;

            return v1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix64(ulong v1, ulong v2, int r, ulong k1, ulong k2)
        {
            v1 += v2 * k1;
            v1 ^= RotateRight(v1, r) * k2;

            return v1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix128(ulong v1, ulong v2, int r, ulong k1, ulong k2)
        {
            v1 += v2 * k1;
            v1 = RotateRight(v1, r) * k2;

            return v1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix256(ulong v1, ulong v2, ulong v3, int r, ulong k)
        {
            v1 += v2 * k;
            v1 = RotateRight(v1, r) + v3;

            return v1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Read64(ReadOnlySpan<byte> buffer, int offset)
        {
            ReadOnlySpan<byte> slice = buffer.Slice(offset, 8);
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(slice);

            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Read32(ReadOnlySpan<byte> buffer, int offset)
        {
            ReadOnlySpan<byte> slice = buffer.Slice(offset, 4);
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(slice);

            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort Read16(ReadOnlySpan<byte> buffer, int offset)
        {
            ReadOnlySpan<byte> slice = buffer.Slice(offset, 2);
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(slice);

            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateRight(ulong value, int rotation)
        {
            rotation &= 0x3F;
            return (value >> rotation) | (value << (64 - rotation));
        }
    }
}
