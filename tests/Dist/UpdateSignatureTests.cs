using System;
using System.Security.Cryptography;
using System.Text;

namespace GamesHub.Tests
{
    /// <summary>Update authenticity (UpdateVerifier) with a throwaway key generated per test run.</summary>
    public static class UpdateSignatureTests
    {
        private const string Installer = "GamesHub-Setup-2.1.0.exe";
        private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        private sealed class Fixture
        {
            public ECParameters Public;
            public byte[] Manifest, Signature;
        }

        private static Fixture Signed(string manifestText)
        {
            using (ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                byte[] manifest = Encoding.UTF8.GetBytes(manifestText);
                byte[] sig = key.SignData(manifest, HashAlgorithmName.SHA256);
                return new Fixture
                {
                    Public = key.ExportParameters(false),
                    Manifest = manifest,
                    Signature = Encoding.ASCII.GetBytes(Convert.ToBase64String(sig) + "\n"),
                };
            }
        }

        private static Fixture Valid() => Signed(Hash + "  " + Installer + "\n");

        public static void TestAcceptsValidSignatureAndMatchingInstaller()
        {
            Fixture f = Valid();
            Assert.Equal(UpdateVerification.Ok, UpdateVerifier.VerifyManifest(f.Manifest, f.Signature, Installer, f.Public, out string expected));
            Assert.Equal(Hash, expected);
            Assert.Equal(UpdateVerification.Ok, UpdateVerifier.VerifyInstaller(f.Manifest, f.Signature, Installer, Hash.ToUpperInvariant(), f.Public));
        }

        public static void TestRejectsTamperedManifest()
        {
            Fixture f = Valid();
            byte[] tampered = (byte[])f.Manifest.Clone();
            tampered[0] = (byte)(tampered[0] == (byte)'9' ? '8' : '9'); // another hash, same signature
            Assert.Equal(UpdateVerification.InvalidSignature, UpdateVerifier.VerifyInstaller(tampered, f.Signature, Installer, Hash, f.Public));
            byte[] appended = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(f.Manifest) + "\n");
            Assert.Equal(UpdateVerification.InvalidSignature, UpdateVerifier.VerifyInstaller(appended, f.Signature, Installer, Hash, f.Public));
        }

        public static void TestRejectsInstallerWithAnotherHash()
        {
            Fixture f = Valid();
            string other = "60303ae22b998861bce3b28f33eec1be758a213c86c93c076dbe9f558c11c752";
            Assert.Equal(UpdateVerification.HashMismatch, UpdateVerifier.VerifyInstaller(f.Manifest, f.Signature, Installer, other, f.Public));
            Assert.Equal(UpdateVerification.HashMismatch, UpdateVerifier.VerifyInstaller(f.Manifest, f.Signature, Installer, null, f.Public));
        }

        public static void TestRejectsInvalidSignatures()
        {
            Fixture f = Valid();
            Fixture otherKey = Valid(); // same manifest, signed with a different key
            Assert.Equal(UpdateVerification.InvalidSignature, UpdateVerifier.VerifyInstaller(f.Manifest, otherKey.Signature, Installer, Hash, f.Public));
            Assert.Equal(UpdateVerification.InvalidSignature, UpdateVerifier.VerifyInstaller(f.Manifest, f.Signature, Installer, Hash, otherKey.Public));

            byte[] raw = Convert.FromBase64String(Encoding.ASCII.GetString(f.Signature).Trim());
            raw[10] ^= 0x01;
            Assert.Equal(UpdateVerification.InvalidSignature,
                UpdateVerifier.VerifyInstaller(f.Manifest, Encoding.ASCII.GetBytes(Convert.ToBase64String(raw)), Installer, Hash, f.Public));
            foreach (string junk in new[] { "not base64!", Convert.ToBase64String(new byte[64]), Convert.ToBase64String(new byte[32]), " " })
                Assert.Equal(UpdateVerification.InvalidSignature,
                    UpdateVerifier.VerifyInstaller(f.Manifest, Encoding.ASCII.GetBytes(junk), Installer, Hash, f.Public), "junk: " + junk);
        }

        public static void TestRejectsMissingPieces()
        {
            Fixture f = Valid();
            Assert.Equal(UpdateVerification.MissingManifest, UpdateVerifier.VerifyInstaller(null, f.Signature, Installer, Hash, f.Public));
            Assert.Equal(UpdateVerification.MissingManifest, UpdateVerifier.VerifyInstaller(new byte[0], f.Signature, Installer, Hash, f.Public));
            Assert.Equal(UpdateVerification.MissingSignature, UpdateVerifier.VerifyInstaller(f.Manifest, null, Installer, Hash, f.Public));
            Assert.Equal(UpdateVerification.MissingSignature, UpdateVerifier.VerifyInstaller(f.Manifest, new byte[0], Installer, Hash, f.Public));
            Assert.Equal(UpdateVerification.MissingManifest,
                UpdateVerifier.VerifyInstaller(new byte[UpdateVerifier.MaxManifestBytes + 1], f.Signature, Installer, Hash, f.Public), "oversized manifest");
        }

        public static void TestSignedManifestMustNameTheInstaller()
        {
            // A validly signed manifest for another file (e.g. an older release) or a bare hash does not authorize this installer.
            Fixture older = Signed(Hash + "  GamesHub-Setup-2.0.0.exe\n");
            Assert.Equal(UpdateVerification.InstallerNotListed, UpdateVerifier.VerifyInstaller(older.Manifest, older.Signature, Installer, Hash, older.Public));
            Fixture bare = Signed(Hash + "\n");
            Assert.Equal(UpdateVerification.InstallerNotListed, UpdateVerifier.VerifyInstaller(bare.Manifest, bare.Signature, Installer, Hash, bare.Public));
            Fixture ambiguous = Signed(Hash + "  " + Installer + "\n60303ae22b998861bce3b28f33eec1be758a213c86c93c076dbe9f558c11c752  " + Installer + "\n");
            Assert.Equal(UpdateVerification.InstallerNotListed,
                UpdateVerifier.VerifyInstaller(ambiguous.Manifest, ambiguous.Signature, Installer, Hash, ambiguous.Public));
        }

        public static void TestEmbeddedPublicKeyIsAValidP256Point()
        {
            ECParameters p = UpdateSigningKey.Parameters;
            Assert.Equal(32, p.Q.X.Length);
            Assert.Equal(32, p.Q.Y.Length);
            using (ECDsa key = ECDsa.Create(p)) Assert.Equal(256, key.KeySize);
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(UpdateSigningKey.KeyId, "^[0-9a-f]{16}$"), UpdateSigningKey.KeyId);
            // The embedded key must not accept a signature made by any other key.
            Fixture f = Valid();
            Assert.Equal(UpdateVerification.InvalidSignature, UpdateVerifier.VerifyInstaller(f.Manifest, f.Signature, Installer, Hash, p));
        }

        public static void TestParseExactIsStrict()
        {
            Assert.Equal(Hash, Sha256File.ParseExact(Hash.ToUpperInvariant() + " *" + Installer, Installer));
            Assert.Equal(Hash, Sha256File.ParseExact("SHA256 (" + Installer + ") = " + Hash, Installer));
            Assert.Equal(null, Sha256File.ParseExact(Hash, Installer));
            Assert.Equal(null, Sha256File.ParseExact(Hash + "  dist/" + Installer, Installer));
            Assert.Equal(null, Sha256File.ParseExact(Hash + "  " + Installer, null));
        }
    }
}
