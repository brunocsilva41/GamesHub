// Update authenticity. The release pipeline signs the checksum manifest (GamesHub-Setup-X.Y.Z.exe.sha256, exact bytes)
// with the ECDSA P-256 key held in the GAMESHUB_UPDATE_SIGNING_KEY secret and publishes the signature next to it as
// GamesHub-Setup-X.Y.Z.exe.sha256.sig (Base64 of the 64-byte IEEE P1363 r||s signature, SHA-256). The app embeds the
// public key (UpdateSigningKey.cs) and runs an installer only when its SHA-256 is listed, under its exact file name,
// in a manifest whose signature verifies. Anything missing or different fails closed.
using System;
using System.Security.Cryptography;
using System.Text;

namespace GamesHub
{
    public enum UpdateVerification
    {
        Ok,
        MissingManifest,
        MissingSignature,
        InvalidSignature,
        InstallerNotListed,
        HashMismatch,
    }

    public static class UpdateVerifier
    {
        public const int MaxManifestBytes = 16 * 1024;
        public const int MaxSignatureBytes = 1024;
        private const int P256SignatureBytes = 64;

        public static ECParameters PublicKey(string x, string y) => new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Convert.FromBase64String(x), Y = Convert.FromBase64String(y) },
        };

        /// <summary>True when <paramref name="signatureFile"/> (Base64 text) is a valid ECDSA P-256/SHA-256 signature of
        /// <paramref name="data"/> under <paramref name="key"/>. Never throws.</summary>
        public static bool VerifySignature(byte[] data, byte[] signatureFile, ECParameters key)
        {
            if (data == null || signatureFile == null || signatureFile.Length > MaxSignatureBytes) return false;
            byte[] sig;
            try { sig = Convert.FromBase64String(Encoding.ASCII.GetString(signatureFile).Trim()); }
            catch (FormatException) { return false; }
            if (sig.Length != P256SignatureBytes) return false;
            try
            {
                using (ECDsa ecdsa = ECDsa.Create(key))
                    return ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is ArgumentException || ex is PlatformNotSupportedException)
            {
                return false;
            }
        }

        /// <summary>Checks the manifest's signature and extracts the expected hash (lowercase hex) of installerName.</summary>
        public static UpdateVerification VerifyManifest(byte[] manifest, byte[] signature, string installerName, ECParameters key,
                                                        out string expectedSha256)
        {
            expectedSha256 = null;
            if (manifest == null || manifest.Length == 0 || manifest.Length > MaxManifestBytes) return UpdateVerification.MissingManifest;
            if (signature == null || signature.Length == 0) return UpdateVerification.MissingSignature;
            if (!VerifySignature(manifest, signature, key)) return UpdateVerification.InvalidSignature;
            string text;
            try { text = new UTF8Encoding(false, true).GetString(manifest); }
            catch (ArgumentException) { return UpdateVerification.InstallerNotListed; } // DecoderFallbackException
            expectedSha256 = Sha256File.ParseExact(text, installerName);
            return expectedSha256 == null ? UpdateVerification.InstallerNotListed : UpdateVerification.Ok;
        }

        /// <summary>Full check: signed manifest + the installer's actual SHA-256 (hex) matches the signed one.</summary>
        public static UpdateVerification VerifyInstaller(byte[] manifest, byte[] signature, string installerName, string actualSha256,
                                                         ECParameters key)
        {
            UpdateVerification v = VerifyManifest(manifest, signature, installerName, key, out string expected);
            if (v != UpdateVerification.Ok) return v;
            return !string.IsNullOrEmpty(actualSha256) && string.Equals(expected, actualSha256, StringComparison.OrdinalIgnoreCase)
                ? UpdateVerification.Ok
                : UpdateVerification.HashMismatch;
        }
    }
}
