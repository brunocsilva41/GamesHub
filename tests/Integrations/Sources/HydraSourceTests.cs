using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GamesHub.Tests
{
    public static class HydraSourceTests
    {
        private const string RideJson =
            "{\"title\":\"RIDE 4\",\"objectId\":\"1259980\",\"shop\":\"steam\",\"remoteId\":\"QKvN737r\",\"isDeleted\":false," +
            "\"playTimeInMilliseconds\":25741.4533,\"lastTimePlayed\":\"2026-09-29T07:50:42.132Z\"," +
            "\"addedToLibraryAt\":\"2026-09-29T04:54:05.004Z\",\"platform\":null," +
            "\"executablePath\":\"D:\\\\Games\\\\RIDE 4\\\\ride4\\\\Binaries\\\\Win64\\\\ride4-Win64-Shipping.exe\"}";

        private static bool Yes(string p) => true;
        private static bool No(string p) => false;

        public static void TestParseSteamRecord()
        {
            Game g = HydraRecords.Parse("!games!steam:1259980", RideJson, Yes);
            Assert.NotNull(g);
            Assert.Equal("hydra:steam:1259980", g.Id);
            Assert.Equal("RIDE 4", g.Name);
            Assert.Equal("hydra", g.Source);
            Assert.Equal("Steam", g.Platform);
            Assert.Equal("1259980", g.SteamAppId);
            Assert.Equal(@"D:\Games\RIDE 4\ride4\Binaries\Win64\ride4-Win64-Shipping.exe", g.LaunchTarget);
            Assert.Equal(g.LaunchTarget, g.Exe);
            Assert.Equal(@"D:\Games\RIDE 4", g.InstallDir, "unreal layout → game root");
            Assert.Equal(25L, g.PlaySeconds);
            Assert.True(g.LastPlayed.HasValue);
            Assert.Equal(new DateTime(2026, 9, 29, 7, 50, 42, 132, DateTimeKind.Utc).ToLocalTime(), g.LastPlayed.Value);
            Assert.Equal("", g.LaunchArgs);
        }

        public static void TestSkipsDeletedMissingAndNotInstalled()
        {
            Assert.True(HydraRecords.Parse("k", RideJson, No) == null, "exe missing on disk");
            Assert.True(HydraRecords.Parse("k", RideJson.Replace("\"isDeleted\":false", "\"isDeleted\":true"), Yes) == null, "deleted");
            Assert.True(HydraRecords.Parse("!games!steam:1", "{\"title\":\"X\",\"objectId\":\"1\",\"shop\":\"steam\",\"executablePath\":null}", Yes) == null, "null exe");
            Assert.True(HydraRecords.Parse("!games!steam:1", "{\"title\":\"X\",\"objectId\":\"1\",\"shop\":\"steam\"}", Yes) == null, "no exe");
            Assert.True(HydraRecords.Parse("k", "not json", Yes) == null, "bad json");
            Assert.True(HydraRecords.Parse("k", "", Yes) == null, "empty");
            Assert.True(HydraRecords.Parse("k", "{\"objectId\":\"1\",\"executablePath\":\"C:\\\\a.exe\"}", Yes) == null, "no shop anywhere");
        }

        public static void TestNonSteamShopAndKeyFallback()
        {
            Game g = HydraRecords.Parse("!games!custom:abc-123",
                "{\"title\":\"  My Game \",\"executablePath\":\"E:\\\\Stuff\\\\My Game\\\\game.exe\",\"lastTimePlayed\":null}", Yes);
            Assert.NotNull(g);
            Assert.Equal("hydra:custom:abc-123", g.Id);
            Assert.Equal("My Game", g.Name);
            Assert.Equal("PC", g.Platform);
            Assert.Equal("", g.SteamAppId);
            Assert.Equal(@"E:\Stuff\My Game", g.InstallDir);
            Assert.False(g.LastPlayed.HasValue);
            Assert.Equal(0L, g.PlaySeconds);
        }

        public static void TestUtf8TitleAndIdHelpers()
        {
            string json = "{\"title\":\"Call of Duty\u00AE: Black Ops II\",\"objectId\":\"202970\",\"shop\":\"steam\",\"executablePath\":\"D:\\\\CoD\\\\t6zm.exe\"}";
            Game g = HydraRecords.Parse("!games!steam:202970", json, Yes);
            Assert.Equal("Call of Duty\u00AE: Black Ops II", g.Name);
            Assert.Equal(@"D:\CoD", g.InstallDir);
            Assert.Equal("hydralauncher://run?shop=steam&objectId=2567870", HydraRecords.RunUri("steam", "2567870"));
            Assert.Equal("hydra:steam:730", HydraRecords.GameId("steam", "730"));
        }

        public static void TestInstallDirFromExe()
        {
            Assert.Equal(@"F:\Jogos\CT\Chained Together", HydraRecords.InstallDirFromExe(@"F:\Jogos\CT\Chained Together\ChainedTogether\Binaries\Win64\X-Win64-Shipping.exe"));
            Assert.Equal(@"D:\Games\DIRT 5", HydraRecords.InstallDirFromExe(@"D:\Games\DIRT 5\game_release.exe"));
            Assert.Equal("", HydraRecords.InstallDirFromExe(@"D:\game.exe"), "never a drive root");
            Assert.Equal(@"D:\Proj", HydraRecords.InstallDirFromExe(@"D:\Proj\Binaries\Win64\x.exe"), "project directly under drive root");
        }

        // ------------------------------------------------------------------ Snappy

        public static void TestSnappyOverlappingCopy()
        {
            // len 9, literal "abc", copy(len 6, offset 3) with 1-byte offset
            byte[] c = { 0x09, 0x08, 0x61, 0x62, 0x63, 0x09, 0x03 };
            Assert.Equal("abcabcabc", Encoding.ASCII.GetString(SnappyDecoder.Decompress(c, 0, c.Length)));
        }

        public static void TestSnappyLongLiteralAndCopy2()
        {
            var text = new string('x', 70) + "0123456789";
            var ms = new MemoryStream();
            ms.WriteByte(100);                          // uncompressed length
            ms.WriteByte(60 << 2); ms.WriteByte(79);    // literal, 1-byte length (80 - 1)
            byte[] lit = Encoding.ASCII.GetBytes(text);
            ms.Write(lit, 0, lit.Length);
            ms.WriteByte((byte)(((20 - 1) << 2) | 2)); ms.WriteByte(10); ms.WriteByte(0);   // copy2 len 20 offset 10
            byte[] c = ms.ToArray();
            Assert.Equal(text + "01234567890123456789", Encoding.ASCII.GetString(SnappyDecoder.Decompress(c, 0, c.Length)));
        }

        public static void TestSnappyRejectsCorruptInput()
        {
            bool threw = false;
            try { SnappyDecoder.Decompress(new byte[] { 0x05, 0x09, 0x07 }, 0, 3); }   // copy before any output
            catch (InvalidDataException) { threw = true; }
            Assert.True(threw, "expected InvalidDataException");
        }

        // ------------------------------------------------------------------ LevelDB

        private static void Varint(Stream s, long v)
        {
            while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; }
            s.WriteByte((byte)v);
        }

        private static byte[] InternalKey(string key, ulong seq, bool deletion)
        {
            var ms = new MemoryStream();
            byte[] k = Encoding.UTF8.GetBytes(key);
            ms.Write(k, 0, k.Length);
            ms.Write(BitConverter.GetBytes((seq << 8) | (deletion ? 0UL : 1UL)), 0, 8);
            return ms.ToArray();
        }

        /// <summary>Block with full prefix compression against the previous key and a single restart point.</summary>
        private static byte[] Block(List<KeyValuePair<byte[], byte[]>> entries)
        {
            var ms = new MemoryStream();
            byte[] last = new byte[0];
            foreach (var e in entries)
            {
                int shared = 0;
                while (shared < last.Length && shared < e.Key.Length && last[shared] == e.Key[shared]) shared++;
                Varint(ms, shared); Varint(ms, e.Key.Length - shared); Varint(ms, e.Value.Length);
                ms.Write(e.Key, shared, e.Key.Length - shared);
                ms.Write(e.Value, 0, e.Value.Length);
                last = e.Key;
            }
            ms.Write(BitConverter.GetBytes(0), 0, 4);   // restart[0] = 0
            ms.Write(BitConverter.GetBytes(1), 0, 4);   // num_restarts
            return ms.ToArray();
        }

        private static byte[] SnappyLiteral(byte[] raw)
        {
            var ms = new MemoryStream();
            Varint(ms, raw.Length);
            ms.WriteByte(61 << 2);                       // literal with 2-byte length
            ms.WriteByte((byte)((raw.Length - 1) & 0xFF)); ms.WriteByte((byte)((raw.Length - 1) >> 8));
            ms.Write(raw, 0, raw.Length);
            return ms.ToArray();
        }

        private static byte[] Table(List<KeyValuePair<byte[], byte[]>> entries, bool compress)
        {
            var file = new MemoryStream();
            byte[] data = Block(entries);
            byte[] stored = compress ? SnappyLiteral(data) : data;
            long dataOff = file.Position;
            file.Write(stored, 0, stored.Length);
            file.WriteByte(compress ? (byte)1 : (byte)0); file.Write(new byte[4], 0, 4);
            var handle = new MemoryStream(); Varint(handle, dataOff); Varint(handle, stored.Length);
            byte[] index = Block(new List<KeyValuePair<byte[], byte[]>> { new KeyValuePair<byte[], byte[]>(entries[entries.Count - 1].Key, handle.ToArray()) });
            long idxOff = file.Position;
            file.Write(index, 0, index.Length);
            file.WriteByte(0); file.Write(new byte[4], 0, 4);
            var footer = new MemoryStream();
            Varint(footer, 0); Varint(footer, 0);           // metaindex (unused)
            Varint(footer, idxOff); Varint(footer, index.Length);
            footer.SetLength(40);
            footer.Position = 40;
            footer.Write(BitConverter.GetBytes(0xdb4775248b80fb57UL), 0, 8);
            byte[] f = footer.ToArray();
            file.Write(f, 0, f.Length);
            return file.ToArray();
        }

        private static KeyValuePair<byte[], byte[]> KV(string key, ulong seq, string value)
            => new KeyValuePair<byte[], byte[]>(InternalKey(key, seq, value == null), value == null ? new byte[0] : Encoding.UTF8.GetBytes(value));

        private static byte[] LogFile(ulong seq, params (string key, string value)[] ops)
        {
            var batch = new MemoryStream();
            batch.Write(BitConverter.GetBytes(seq), 0, 8);
            batch.Write(BitConverter.GetBytes(ops.Length), 0, 4);
            foreach (var op in ops)
            {
                batch.WriteByte(op.value == null ? (byte)0 : (byte)1);
                byte[] k = Encoding.UTF8.GetBytes(op.key);
                Varint(batch, k.Length); batch.Write(k, 0, k.Length);
                if (op.value != null) { byte[] v = Encoding.UTF8.GetBytes(op.value); Varint(batch, v.Length); batch.Write(v, 0, v.Length); }
            }
            byte[] payload = batch.ToArray();
            var log = new MemoryStream();
            log.Write(new byte[4], 0, 4);                                   // crc (not checked)
            log.WriteByte((byte)(payload.Length & 0xFF)); log.WriteByte((byte)(payload.Length >> 8));
            log.WriteByte(1);                                               // FULL
            log.Write(payload, 0, payload.Length);
            log.Write(new byte[16], 0, 16);                                 // zero padding
            return log.ToArray();
        }

        public static void TestLevelDbTablesAndLogNewestWins()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gameshub-test-ldb-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                // Older compressed table: two games + an unrelated key.
                File.WriteAllBytes(Path.Combine(dir, "000005.ldb"), Table(new List<KeyValuePair<byte[], byte[]>>
                {
                    KV("!games!steam:1", 10, "{\"title\":\"Old\"}"),
                    KV("!games!steam:2", 11, "{\"title\":\"Two\"}"),
                    KV("language", 12, "\"pt-BR\""),
                }, true));
                // Newer uncompressed table overrides steam:1.
                File.WriteAllBytes(Path.Combine(dir, "000007.sst"), Table(new List<KeyValuePair<byte[], byte[]>>
                {
                    KV("!games!steam:1", 20, "{\"title\":\"New\"}"),
                }, false));
                // Log: deletes steam:2, adds steam:3.
                File.WriteAllBytes(Path.Combine(dir, "000008.log"), LogFile(30, ("!games!steam:2", null), ("!games!steam:3", "{\"title\":\"Three\"}")));
                File.WriteAllText(Path.Combine(dir, "CURRENT"), "MANIFEST-000001\n");

                var all = LevelDbReader.ReadAll(dir, "!games!");
                Assert.Equal(2, all.Count, "live game keys");
                Assert.Equal("{\"title\":\"New\"}", all["!games!steam:1"]);
                Assert.Equal("{\"title\":\"Three\"}", all["!games!steam:3"]);
                Assert.False(all.ContainsKey("!games!steam:2"), "deleted by log");
                var everything = LevelDbReader.ReadAll(dir);
                Assert.Equal(3, everything.Count, "all live keys without prefix filter");
                Assert.Equal("\"pt-BR\"", everything["language"]);

                // Corrupt table is skipped, not fatal.
                File.WriteAllBytes(Path.Combine(dir, "000009.ldb"), new byte[] { 1, 2, 3 });
                Assert.Equal(2, LevelDbReader.ReadAll(dir, "!games!").Count, "corrupt file tolerated");
                Assert.Equal(0, LevelDbReader.ReadAll(Path.Combine(dir, "missing")).Count);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
            }
        }

        public static void TestHydraScanFixture()
        {
            string root = Path.Combine(Path.GetTempPath(), "gameshub-test-hydra-" + Guid.NewGuid().ToString("N"));
            try
            {
                string exe = Path.Combine(root, "Games", "Liars Bar", "Liar's Bar.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(exe));
                File.WriteAllText(exe, "");
                string db = Path.Combine(root, "hydra-db");
                Directory.CreateDirectory(db);
                string installed = "{\"title\":\"Liar's Bar\",\"objectId\":\"3097560\",\"shop\":\"steam\",\"executablePath\":" + Json.Serialize(exe) + "}";
                string gone = "{\"title\":\"PEAK\",\"objectId\":\"3527290\",\"shop\":\"steam\",\"executablePath\":\"Z:\\\\nope\\\\PEAK.exe\"}";
                File.WriteAllBytes(Path.Combine(db, "000003.log"), LogFile(1,
                    ("!games!steam:3097560", installed), ("!games!steam:3527290", gone), ("!downloads!steam:3097560", "{}")));

                var games = new HydraSource(root).Scan();
                Assert.Equal(1, games.Count);
                Assert.Equal("hydra:steam:3097560", games[0].Id);
                Assert.Equal(exe, games[0].LaunchTarget);
                Assert.Equal(Path.GetDirectoryName(exe), games[0].InstallDir);
                Assert.Equal(0, new HydraSource(Path.Combine(root, "missing")).Scan().Count);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
            }
        }
    }
}
