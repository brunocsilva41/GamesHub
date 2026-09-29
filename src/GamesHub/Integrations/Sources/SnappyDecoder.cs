using System;
using System.IO;

namespace GamesHub
{
    public static class SnappyDecoder
    {
        /// <summary>Decompresses a raw Snappy block. Throws InvalidDataException on corrupt input.</summary>
        public static byte[] Decompress(byte[] src, int offset, int count)
        {
            int pos = offset, end = offset + count;
            ulong len = ReadVarint(src, ref pos, end);
            if (len > int.MaxValue) throw new InvalidDataException("snappy: length too large");
            var dst = new byte[(int)len];
            int d = 0;
            while (pos < end)
            {
                int tag = src[pos++];
                int kind = tag & 3;
                if (kind == 0)
                {
                    int n = tag >> 2;
                    if (n >= 60)
                    {
                        int bytes = n - 59;
                        if (pos + bytes > end) throw new InvalidDataException("snappy: truncated literal length");
                        n = 0;
                        for (int i = 0; i < bytes; i++) n |= src[pos + i] << (8 * i);
                        pos += bytes;
                    }
                    n += 1;
                    if (n <= 0 || pos + n > end || d + n > dst.Length) throw new InvalidDataException("snappy: bad literal");
                    Buffer.BlockCopy(src, pos, dst, d, n);
                    pos += n; d += n;
                    continue;
                }
                int length, off;
                if (kind == 1)
                {
                    if (pos >= end) throw new InvalidDataException("snappy: truncated copy1");
                    length = ((tag >> 2) & 7) + 4;
                    off = ((tag >> 5) << 8) | src[pos++];
                }
                else if (kind == 2)
                {
                    if (pos + 2 > end) throw new InvalidDataException("snappy: truncated copy2");
                    length = (tag >> 2) + 1;
                    off = src[pos] | (src[pos + 1] << 8);
                    pos += 2;
                }
                else
                {
                    if (pos + 4 > end) throw new InvalidDataException("snappy: truncated copy4");
                    length = (tag >> 2) + 1;
                    off = src[pos] | (src[pos + 1] << 8) | (src[pos + 2] << 16) | (src[pos + 3] << 24);
                    pos += 4;
                }
                if (off <= 0 || off > d || d + length > dst.Length) throw new InvalidDataException("snappy: bad copy");
                // Byte-by-byte: copies may overlap (run-length style).
                for (int i = 0; i < length; i++, d++) dst[d] = dst[d - off];
            }
            if (d != dst.Length) throw new InvalidDataException("snappy: length mismatch");
            return dst;
        }

        public static ulong ReadVarint(byte[] b, ref int pos, int end)
        {
            ulong result = 0;
            for (int shift = 0; shift <= 63; shift += 7)
            {
                if (pos >= end) throw new InvalidDataException("varint: truncated");
                byte x = b[pos++];
                result |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) return result;
            }
            throw new InvalidDataException("varint: too long");
        }
    }
}
