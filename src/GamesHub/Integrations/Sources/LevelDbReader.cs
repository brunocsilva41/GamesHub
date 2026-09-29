// OWNER: SOURCES agent. Tiny read-only LevelDB reader: scans every table (.ldb/.sst) and write-ahead log
// (.log) in a database folder and returns the newest live value of each key (by sequence number).
// Files are opened with full sharing, so it works while the owning app has the DB open. It does not
// consult the MANIFEST; obsolete files left behind by an interrupted compaction are harmless because
// newer sequence numbers always win.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GamesHub
{
    public static class LevelDbReader
    {
        private const ulong TableMagic = 0xdb4775248b80fb57UL;
        private const int LogBlockSize = 32768;

        private struct Entry { public ulong Seq; public bool Deleted; public byte[] Value; }

        /// <summary>Returns key → value (UTF-8 decoded) for all live keys, optionally filtered by key prefix.
        /// Never throws: unreadable files are logged and skipped.</summary>
        public static Dictionary<string, string> ReadAll(string dir, string keyPrefix = null)
        {
            var merged = new Dictionary<string, Entry>(StringComparer.Ordinal);
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception ex) { Log.Warn("LevelDB: cannot list " + dir, ex); return new Dictionary<string, string>(); }

            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".ldb" && ext != ".sst" && ext != ".log") continue;
                byte[] data;
                try { data = ReadShared(f); }
                catch (Exception ex) { Log.Warn("LevelDB: cannot read " + f, ex); continue; }
                try
                {
                    if (ext == ".log") ParseLog(data, (k, s, del, v) => Put(merged, k, s, del, v, keyPrefix));
                    else ParseTable(data, (k, s, del, v) => Put(merged, k, s, del, v, keyPrefix));
                }
                catch (Exception ex) { Log.Warn("LevelDB: parse failed " + f, ex); }
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in merged)
                if (!kv.Value.Deleted && kv.Value.Value != null)
                    result[kv.Key] = Encoding.UTF8.GetString(kv.Value.Value);
            return result;
        }

        private static void Put(Dictionary<string, Entry> map, byte[] userKey, ulong seq, bool deleted, byte[] value, string prefix)
        {
            string key = Encoding.UTF8.GetString(userKey);
            if (prefix != null && !key.StartsWith(prefix, StringComparison.Ordinal)) return;
            if (map.TryGetValue(key, out Entry cur) && cur.Seq > seq) return;
            map[key] = new Entry { Seq = seq, Deleted = deleted, Value = value };
        }

        private static byte[] ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var buf = new byte[fs.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read == buf.Length) return buf;
                var trimmed = new byte[read];
                Buffer.BlockCopy(buf, 0, trimmed, 0, read);
                return trimmed;
            }
        }

        public delegate void RecordSink(byte[] userKey, ulong seq, bool deleted, byte[] value);

        // ------------------------------------------------------------------ tables (.ldb / .sst)

        public static void ParseTable(byte[] file, RecordSink sink)
        {
            if (file.Length < 48) throw new InvalidDataException("table too small");
            int footer = file.Length - 48;
            if (BitConverter.ToUInt64(file, file.Length - 8) != TableMagic) throw new InvalidDataException("bad table magic");
            int p = footer;
            ReadHandle(file, ref p, file.Length); // metaindex (unused)
            var (idxOff, idxSize) = ReadHandle(file, ref p, file.Length);
            byte[] index = ReadBlock(file, idxOff, idxSize);
            ParseBlock(index, (k, v) =>
            {
                int vp = 0;
                var (off, size) = ReadHandle(v, ref vp, v.Length);
                byte[] block;
                try { block = ReadBlock(file, off, size); }
                catch (Exception ex) { Log.Warn("LevelDB: skipping unreadable block", ex); return; }
                ParseBlock(block, (ik, val) =>
                {
                    if (ik.Length < 8) return;
                    ulong tag = BitConverter.ToUInt64(ik, ik.Length - 8);
                    var uk = new byte[ik.Length - 8];
                    Buffer.BlockCopy(ik, 0, uk, 0, uk.Length);
                    bool deleted = (tag & 0xFF) == 0;
                    sink(uk, tag >> 8, deleted, deleted ? null : val);
                });
            });
        }

        private static (long, long) ReadHandle(byte[] b, ref int p, int end)
            => ((long)SnappyDecoder.ReadVarint(b, ref p, end), (long)SnappyDecoder.ReadVarint(b, ref p, end));

        private static byte[] ReadBlock(byte[] file, long off, long size)
        {
            if (off < 0 || size < 0 || off + size + 5 > file.Length) throw new InvalidDataException("block out of range");
            byte type = file[off + size];
            if (type == 0)
            {
                var raw = new byte[size];
                Buffer.BlockCopy(file, (int)off, raw, 0, (int)size);
                return raw;
            }
            if (type == 1) return SnappyDecoder.Decompress(file, (int)off, (int)size);
            throw new InvalidDataException("unsupported block compression " + type);
        }

        /// <summary>Iterates a table block's entries (prefix-compressed keys, restart array at the end).</summary>
        public static void ParseBlock(byte[] block, Action<byte[], byte[]> onEntry)
        {
            if (block.Length < 4) return;
            int numRestarts = BitConverter.ToInt32(block, block.Length - 4);
            long limitL = block.Length - 4 - 4L * numRestarts;
            if (numRestarts < 0 || limitL < 0) throw new InvalidDataException("bad restart array");
            int limit = (int)limitL, p = 0;
            byte[] lastKey = new byte[0];
            while (p < limit)
            {
                int shared = (int)SnappyDecoder.ReadVarint(block, ref p, limit);
                int nonShared = (int)SnappyDecoder.ReadVarint(block, ref p, limit);
                int valueLen = (int)SnappyDecoder.ReadVarint(block, ref p, limit);
                if (shared > lastKey.Length || nonShared < 0 || valueLen < 0 || p + nonShared + valueLen > limit)
                    throw new InvalidDataException("bad block entry");
                var key = new byte[shared + nonShared];
                Buffer.BlockCopy(lastKey, 0, key, 0, shared);
                Buffer.BlockCopy(block, p, key, shared, nonShared);
                p += nonShared;
                var value = new byte[valueLen];
                Buffer.BlockCopy(block, p, value, 0, valueLen);
                p += valueLen;
                lastKey = key;
                onEntry(key, value);
            }
        }

        // ------------------------------------------------------------------ write-ahead log (.log)

        public static void ParseLog(byte[] file, RecordSink sink)
        {
            var pending = new MemoryStream();
            bool inFragment = false;
            int blockStart = 0;
            while (blockStart < file.Length)
            {
                int blockEnd = Math.Min(blockStart + LogBlockSize, file.Length);
                int p = blockStart;
                while (p + 7 <= blockEnd)
                {
                    int length = file[p + 4] | (file[p + 5] << 8);
                    byte type = file[p + 6];
                    if (type == 0 && length == 0) break;            // zero padding (preallocated)
                    if (p + 7 + length > blockEnd) break;          // truncated tail (being written)
                    int payload = p + 7;
                    p = payload + length;
                    switch (type)
                    {
                        case 1: // FULL
                            ApplyBatch(file, payload, length, sink);
                            inFragment = false; pending.SetLength(0);
                            break;
                        case 2: // FIRST
                            pending.SetLength(0); pending.Write(file, payload, length); inFragment = true;
                            break;
                        case 3: // MIDDLE
                            if (inFragment) pending.Write(file, payload, length);
                            break;
                        case 4: // LAST
                            if (inFragment)
                            {
                                pending.Write(file, payload, length);
                                byte[] rec = pending.ToArray();
                                ApplyBatch(rec, 0, rec.Length, sink);
                            }
                            inFragment = false; pending.SetLength(0);
                            break;
                        default:
                            inFragment = false; pending.SetLength(0);
                            break;
                    }
                }
                blockStart += LogBlockSize;
            }
        }

        private static void ApplyBatch(byte[] b, int off, int len, RecordSink sink)
        {
            if (len < 12) return;
            int end = off + len;
            ulong seq = BitConverter.ToUInt64(b, off);
            int count = BitConverter.ToInt32(b, off + 8);
            int p = off + 12;
            for (int i = 0; i < count && p < end; i++)
            {
                byte tag = b[p++];
                byte[] key = ReadSlice(b, ref p, end);
                if (tag == 1) sink(key, seq + (ulong)i, false, ReadSlice(b, ref p, end));
                else if (tag == 0) sink(key, seq + (ulong)i, true, null);
                else return; // unknown tag: stop parsing this batch
            }
        }

        private static byte[] ReadSlice(byte[] b, ref int p, int end)
        {
            int n = (int)SnappyDecoder.ReadVarint(b, ref p, end);
            if (n < 0 || p + n > end) throw new InvalidDataException("bad slice");
            var r = new byte[n];
            Buffer.BlockCopy(b, p, r, 0, n);
            p += n;
            return r;
        }
    }
}
