using System;
using System.Collections.Generic;
using System.IO;

namespace GamesHub.Tests
{
    /// <summary>Untrusted third-party files (Riot ProgramData, Hydra LevelDB, Snappy blocks): paths, names and sizes.</summary>
    public static class SourceHardeningTests
    {
        public static void TestRiotProductAndPatchlineMustBeIdentifiers()
        {
            Assert.True(RiotCatalog.TrySplitProductDir("League_Of-Legends.PBE-2", out string p, out string l), "letters, digits, _ and - (any case)");
            Assert.Equal("League_Of-Legends", p);
            Assert.Equal("PBE-2", l);
            Assert.False(RiotCatalog.TrySplitProductDir("lol.live --launch-product=x", out p, out l), "space");
            Assert.False(RiotCatalog.TrySplitProductDir("lol&calc.live", out p, out l), "shell metachar in product");
            Assert.False(RiotCatalog.TrySplitProductDir("lol.live\"x", out p, out l), "quote in patchline");
            Assert.False(RiotCatalog.TrySplitProductDir("lol.li%ve", out p, out l), "percent");
            Assert.False(RiotCatalog.TrySplitProductDir("lol.live\n", out p, out l), "trailing newline (\\z, not $)");
            Assert.False(RiotCatalog.TrySplitProductDir("lolé.live", out p, out l), "non-ASCII");
            Assert.Equal("", RiotCatalog.LaunchArgs("a b", "live"), "never builds args from a non-identifier");
            Assert.Equal("--launch-product=valorant --launch-patchline=live", RiotCatalog.LaunchArgs("valorant", "live"));
        }

        public static void TestRiotClientMustBeLocalNamedAndSigned()
        {
            string root = Path.Combine(Path.GetTempPath(), "gameshub-test-riotclient-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string good = Path.Combine(root, "RiotClientServices.exe");
                string other = Path.Combine(root, "calc.exe");
                File.WriteAllText(good, "");
                File.WriteAllText(other, "");
                var asked = new List<string>();
                Func<string, bool> yes = x => { asked.Add(x); return true; };

                Assert.True(RiotSource.IsTrustedClient(good, yes), "local, right name, signed");
                Assert.False(RiotSource.IsTrustedClient(good, x => false), "signature rejected");
                Assert.False(RiotSource.IsTrustedClient(other, yes), "wrong file name");
                Assert.False(RiotSource.IsTrustedClient(@"\\evil\share\RiotClientServices.exe", yes), "UNC");
                Assert.False(RiotSource.IsTrustedClient(@"\\?\C:\x\RiotClientServices.exe", yes), "device path");
                Assert.False(RiotSource.IsTrustedClient(@"Riot\RiotClientServices.exe", yes), "relative");
                Assert.False(RiotSource.IsTrustedClient(Path.Combine(root, "missing", "RiotClientServices.exe"), yes), "missing");
                Assert.Equal(1, asked.Count, "the signature is only checked for local, correctly named, existing files");

                Assert.Equal(good, RiotSource.FirstTrusted(new[] { @"\\evil\s\RiotClientServices.exe", other, good }, yes), "first trusted wins");
                Assert.Equal("", RiotSource.FirstTrusted(new[] { good }, x => false));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
            }
        }

        public static void TestRiotUncInstallPathIsIgnored()
        {
            string root = Path.Combine(Path.GetTempPath(), "gameshub-test-riotunc-" + Guid.NewGuid().ToString("N"));
            try
            {
                string client = Path.Combine(root, "Client", "RiotClientServices.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(client));
                File.WriteAllText(client, "");
                string data = Path.Combine(root, "ProgramData");
                string d = Path.Combine(data, "Metadata", "valorant.live");
                Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(data, "RiotClientInstalls.json"), "{\"rc_live\":\"" + client.Replace('\\', '/') + "\"}");
                File.WriteAllText(Path.Combine(d, "valorant.live.product_settings.yaml"),
                    "product_install_full_path: \"//attacker/share/VALORANT/live\"\n");
                Assert.Equal(0, new RiotSource(data, p => true).Scan().Count, "UNC install folder: product skipped, never probed");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
            }
        }

        public static void TestHydraRejectsUncExecutable()
        {
            bool probed = false;
            Func<string, bool> exists = p => { probed = true; return true; };
            Game g = HydraRecords.Parse("!games!steam:1",
                "{\"title\":\"X\",\"objectId\":\"1\",\"shop\":\"steam\",\"executablePath\":\"\\\\\\\\evil\\\\share\\\\x.exe\"}", exists);
            Assert.True(g == null, "UNC executable: record ignored");
            Assert.False(probed, "fileExists must not be called on a UNC path");
            Assert.Equal("", HydraRecords.InstallDirFromExe(@"\\evil\share\Game\Binaries\Win64\x.exe"));
            Assert.Equal(@"D:\Games\X", HydraRecords.InstallDirFromExe(@"D:\Games\X\x.exe"));
        }

        // ------------------------------------------------------------------ Snappy / LevelDB limits

        private static void Throws<T>(Action a, string msg) where T : Exception
        {
            try { a(); }
            catch (T) { return; }
            throw new Exception(msg + ": expected " + typeof(T).Name);
        }

        private static byte[] Varint(ulong v)
        {
            var b = new List<byte>();
            while (v >= 0x80) { b.Add((byte)(v | 0x80)); v >>= 7; }
            b.Add((byte)v);
            return b.ToArray();
        }

        public static void TestSnappyRejectsImplausibleDeclaredLength()
        {
            var ok = new List<byte>(Varint(3)) { 0x08, (byte)'a', (byte)'b', (byte)'c' };   // literal "abc"
            Assert.Equal("abc", System.Text.Encoding.ASCII.GetString(SnappyDecoder.Decompress(ok.ToArray(), 0, ok.Count)));

            var bomb = new List<byte>(Varint(100UL * 1024 * 1024)) { 0x08, (byte)'a', (byte)'b', (byte)'c' };
            Throws<InvalidDataException>(() => SnappyDecoder.Decompress(bomb.ToArray(), 0, bomb.Count), "100 MB from 8 bytes");

            var huge = new List<byte>(Varint(300UL * 1024 * 1024));
            huge.AddRange(new byte[10 * 1024 * 1024]);                                     // ratio ok, but over the 256 MB cap
            Throws<InvalidDataException>(() => SnappyDecoder.Decompress(huge.ToArray(), 0, huge.Count), "over 256 MB");
        }

        public static void TestLevelDbSkipsHugeFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gameshub-test-ldb-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                string big = Path.Combine(dir, "000001.ldb");
                using (var fs = new FileStream(big, FileMode.CreateNew)) fs.SetLength(LevelDbReader.MaxFileBytes + 1);
                Throws<IOException>(() => LevelDbReader.ReadShared(big), "never buffers a > 64 MB file");
                Assert.Equal(0, LevelDbReader.ReadAll(dir).Count, "skipped without throwing");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
            }
        }
    }
}
