# Code Signing (internal, self-signed)

The SG Revit Addin installer is signed with a **self-signed** Authenticode
code-signing certificate so Windows shows **Lee** as the publisher instead of
"Unknown publisher." This is an internal-trust setup: it removes the warnings on
machines that **trust the certificate**, at zero cost. It does **not** make the
installer trusted on arbitrary outside PCs (that needs a paid CA / EV cert).

## The two warnings, and what signing does

| Warning | Cause | Self-signed fix |
|---|---|---|
| "Unknown publisher" (UAC / SmartScreen line) | installer not signed | ✅ Signed → shows **Lee** as publisher **on machines that trust the cert** |
| "...isn't downloaded often / may be dangerous" | SmartScreen reputation | ⚠️ Not removed by self-signing. See *SmartScreen* below |

## Files

- `tools/make-codesign-cert.ps1` — create the **Lee** cert (run once, on the release PC). Private key stays in that user's cert store; only the public `.cer` is exported.
- `tools/sign-release.ps1` — sign a built installer (SHA-256 + RFC-3161 timestamp).
- `tools/trust-codesign-cert.ps1` — trust the cert on an office PC (run once per PC, **as Administrator**).
- `installer/codesign/Lee-CodeSign.cer` — the **public** certificate (safe to share; contains no private key).

## Release flow (on the signing PC)

```
# one time only:
powershell -File tools/make-codesign-cert.ps1

# every release, AFTER ISCC compiles the installer:
powershell -File tools/sign-release.ps1            # signs the newest installer/Output exe
# then: gh release upload ... the signed exe
```

`sign-release.ps1` prints `Status: UnknownError` on a PC that hasn't trusted the
cert — that is normal; the signature **is** applied. It reads `Valid` on a PC
that trusts the cert.

## Trusting the cert on each office PC (one time, Administrator)

```
powershell -ExecutionPolicy Bypass -File tools/trust-codesign-cert.ps1
```

This imports `Lee-CodeSign.cer` into **Trusted Root Certification Authorities**
and **Trusted Publishers** (LocalMachine). After that, a signed SG installer
shows "Lee" as a verified publisher with no unknown-publisher prompt.

**Bulk deploy (many PCs):** push `Lee-CodeSign.cer` to those two stores via Group
Policy — *Computer Configuration → Policies → Windows Settings → Security
Settings → Public Key Policies → Trusted Root Certification Authorities* (and
*Trusted Publishers*). Then the warning is gone fleet-wide with no per-PC step.

## SmartScreen ("not downloaded often")

SmartScreen is a separate, Microsoft-cloud reputation check that fires on files
carrying the **Mark-of-the-Web** (i.e. downloaded through a browser). A
self-signed cert has no cloud reputation, so SmartScreen can still warn even
after the cert is trusted. To avoid it internally:

- **Distribute via a network share or direct copy** (UNC `\\server\...`), not a browser download — files copied this way usually have no Mark-of-the-Web, so SmartScreen doesn't engage.
- Or, on a downloaded copy: right-click the `.exe` → **Properties** → check **Unblock** → OK (or `Unblock-File installer.exe`).
- Removing SmartScreen *everywhere, instantly* requires an **EV** code-signing certificate (paid, business identity). That's the only thing that grants instant SmartScreen reputation.

## Security notes

- The private key never leaves the signing PC's user certificate store and is
  **never** committed. Only the public `.cer` is in the repo.
- The public `.cer` lets people *verify/trust* signatures made by the real key;
  it cannot be used to sign anything.
- If the signing PC is replaced, re-run `make-codesign-cert.ps1` (new cert →
  re-trust on PCs), or back up the existing cert to a password-protected `.pfx`
  beforehand (keep that `.pfx` out of the repo).
