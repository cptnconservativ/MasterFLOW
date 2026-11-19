# MasterFlow Premium licensing

MasterFlow Free keeps every tool it has today. Premium tools are listed in the Design tools window with
"★ Premium" after their name; without a license they do not run and say which feature they need (E-LIC-001).

## Entering a key

Design tools window → **License**: paste the key (whitespace and line breaks are ignored), press **Activate**.
A valid key is kept in the user settings (`%APPDATA%\MasterFlow\settings.json`) and checked again at every start.
An empty box + Activate returns to Free. A rejected key is not saved and the previous license stays in force.

## Key format and checking (offline)

`MF1-` + base64url(JSON payload) + `.` + base64url(signature). The signature is ECDSA P-256 / SHA-256 over the payload
bytes. The application holds only the issuer's **public** key (`LicenseKey.IssuerPublicKey`), so it can check a key with
no network access but cannot make one. Nothing is sent anywhere, and the key is not tied to a computer.

Payload: `licensee`, `features` (`*` = every premium feature, present and future, or a list of ids below), `seats`
(informational; a contract term, not enforced), `issued`, `expires` (last valid day, inclusive; omitted = perpetual).

| Feature id | Unlocks |
|---|---|
| `hydrograph-routing` | Hydrograph network routing (NRCS unit hydrograph, TR-55, pipe and pond routing) |
| `drainage-report` | Automated drainage report builder |
| `network-optimizer` | Storm network optimizer |
| `water-quality` | Water quality and LID sizing |
| `cost-estimate` | Quantity takeoff and cost estimate with bid tab |
| `batch-scenarios` | Batch scenario manager |

Which tool belongs to which feature is one list, `PremiumFeatures.All`; tools know nothing about licensing, so a tool
can move between Free and Premium by editing that list. Every path that runs a tool for the user goes through
`PremiumFeatures.Run`.

## Issuing keys

`tools/MasterFlow.LicenseTool` (not shipped with the application):

```
dotnet run --project tools/MasterFlow.LicenseTool -- issue <private.pem> "Firm name" --expires 2027-12-31 --features *
dotnet run --project tools/MasterFlow.LicenseTool -- check <key>
dotnet run --project tools/MasterFlow.LicenseTool -- keygen <folder>      # new issuer key pair
```

The issuer private key is **never** committed. Whoever holds it can make keys. If it is exposed, run `keygen`, put the
new public key in `LicenseKey.IssuerPublicKey` and release; every key issued with the old private key then stops working
and must be re-issued.

## Decisions

- D-065: offline signed keys (no activation server) because MasterFlow is a desktop tool used on project networks and in
  the field; an asymmetric signature means the shipped program cannot be used to mint keys. Seat counts and machine
  binding are left to the license agreement: binding adds support load (new PCs, VMs) for little protection.
- D-066: premium status is a list of tool ids in one place, not a property of each tool, so premium tools can be built
  on their own branches without depending on the licensing code, and the Free/Premium split can change without
  touching the tools.
- D-067: the gate is at the point a tool runs for the user (the window, and later the report builder and batch manager),
  not inside each tool; tests and validation call tools directly.

## A note on strength

Any .NET program can be decompiled and patched to skip a check; offline keys stop casual sharing and make the license
terms clear, they do not stop a determined cracker. Obfuscation or a signed-binary check can be added later if needed.
