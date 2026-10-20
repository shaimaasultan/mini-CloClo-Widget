using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace MiniCloClo;

// Signature checks with Windows' own WinVerifyTrust, offline only: voice
// commands may start only Microsoft-signed programs, and a Quran recitation
// pack is read only when it carries the same signer as the exe (or, for an
// unsigned build, no signature at all).
//
// The full CloClo also pins and re-verifies every native DLL a single-file
// build unpacks into %TEMP%. Mini CloClo is published as a plain folder
// instead (nothing is unpacked), so that check isn't needed here.
public static class NativeLibraryCheck
{
    // Microsoft's code-signing roots, matched by exact name.
    private static readonly HashSet<string> MicrosoftRoots = new(StringComparer.Ordinal)
    {
        "Microsoft Root Certificate Authority",
        "Microsoft Root Certificate Authority 2010",
        "Microsoft Root Certificate Authority 2011",
    };

    private static bool IsMicrosoftSigned(string path)
    {
        if (WinVerifyTrustFile(path) != 0) return false; // missing, tampered or untrusted
        try
        {
#pragma warning disable SYSLIB0057 // only the signer certificate is read; the file's integrity came from WinVerifyTrust
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            // Microsoft signs these under several names ("Microsoft Windows",
            // ".NET"…) but always as the organization Microsoft Corporation,
            // and always under one of Microsoft's own roots — which no other
            // certificate authority can issue into.
            if (!IsMicrosoftCorporation(signer)) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            // This chain only identifies the root. Whether the signature is
            // valid was already settled by WinVerifyTrust, which honours the
            // signing timestamp — several of these DLLs were signed with
            // certificates that have since expired, which is fine.
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid | X509VerificationFlags.IgnoreCtlNotTimeValid;
            if (!chain.Build(signer)) return false;
            var root = chain.ChainElements[^1].Certificate;
            return IsMicrosoftCorporation(root)
                && MicrosoftRoots.Contains(root.GetNameInfo(X509NameType.SimpleName, false) ?? "");
        }
        catch
        {
            return false;
        }
    }

    // Voice commands may launch only fixed Microsoft executables in protected
    // Windows or Program Files locations. Reuse the existing signature check.
    internal static bool IsTrustedMicrosoftExecutable(string path) => IsMicrosoftSigned(path);

    // A data file packed and signed at install (a Quran recitation pack): its
    // signature must be intact and its signer the exe's own certificate; an
    // unsigned exe accepts only an unsigned pack. CloClo's certificate is
    // usually self-signed and deliberately not trusted by Windows, so
    // WinVerifyTrust's "untrusted root" answers are accepted - they come only
    // after the file's digest has matched its signature. A changed byte gives
    // TRUST_E_BAD_DIGEST instead and is refused.
    internal static bool IsSignedLikeExe(string path)
    {
        const int Trusted = 0;
        const int UntrustedRoot = unchecked((int)0x800B0109);   // CERT_E_UNTRUSTEDROOT
        const int Chaining = unchecked((int)0x800B010A);        // CERT_E_CHAINING
        const int NoSignature = unchecked((int)0x800B0100);     // TRUST_E_NOSIGNATURE
        var result = WinVerifyTrustFile(path);
        if (result == NoSignature) return HasSameSignerAsExe(path); // true only when the exe is unsigned too
        return result is Trusted or UntrustedRoot or Chaining && HasSameSignerAsExe(path);
    }

    // Hash matching is mandatory, including for self-signed CloClo builds.
    // A signed installation additionally requires each Whisper DLL to carry
    // the exact signer certificate of the executable. The hash was compiled
    // into that executable after the DLL copy was signed.
    private static bool HasSameSignerAsExe(string path)
    {
        X509Certificate2? exeSigner;
        try
        {
#pragma warning disable SYSLIB0057 // signers only; the pinned hash checks file integrity
            exeSigner = new X509Certificate2(X509Certificate.CreateFromSignedFile(Environment.ProcessPath!));
#pragma warning restore SYSLIB0057
        }
        catch
        {
            // Unsigned local build: only accept an unsigned pinned DLL.
            try
            {
#pragma warning disable SYSLIB0057
                using var dllSigner = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
                return false;
            }
            catch { return true; }
        }
        using (exeSigner)
        {
            try
            {
#pragma warning disable SYSLIB0057
                using var dllSigner = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
                return exeSigner.Thumbprint == dllSigner.Thumbprint;
            }
            catch { return false; }
        }
    }

    private static bool IsMicrosoftCorporation(X509Certificate2 cert) =>
        cert.SubjectName.EnumerateRelativeDistinguishedNames()
            .Any(rdn => rdn.GetSingleElementType().Value == "2.5.4.10" // O=
                && rdn.GetSingleElementValue() == "Microsoft Corporation");

    // ---- WinVerifyTrust: checks the file's hash against its signature and
    // that the certificate chains to a trusted root. Offline only (no
    // revocation fetch), so startup never waits on the network.

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private static int WinVerifyTrustFile(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
        };
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,            // WTD_UI_NONE
                fdwRevocationChecks = 0,   // WTD_REVOKE_NONE
                dwUnionChoice = 1,         // WTD_CHOICE_FILE
                pFile = fileInfoPtr,
                dwStateAction = 0,         // WTD_STATEACTION_IGNORE
                dwProvFlags = 0x1000,      // WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            var action = GenericVerifyV2;
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data);
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(fileInfoPtr);
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
