# Consolidated project summary

The project is a **Windows-side Linux installation orchestrator** built with .NET and Avalonia. The Windows application does not directly format Linux filesystems or deploy Linux while Windows is running. Instead, it gathers every user decision, downloads or extracts the required assets, safely resizes NTFS from Windows, prepares an EFI boot path, and generates a complete unattended installation plan. After reboot, a small Linux environment executes that plan without requiring keyboard or mouse input. 

The design has five defining constraints:

1. No USB drive or other removable installation media.
2. UEFI/GPT systems are the initial target.
3. Windows performs every NTFS resize.
4. The mini-Linux installer is completely non-interactive.
5. Installer payloads must reside on an internal partition readable before Windows unlocks encrypted volumes.

The resulting architecture is:

```text
Avalonia UI
    ↓
Windows installation planner
    ├── System and BitLocker analyzer
    ├── Disk and partition planner
    ├── Asset downloader/verifier
    ├── ESP and boot-entry manager
    └── Configuration generator
             ↓
EFI shim/GRUB — Stage 1 in ESP
             ↓
GRUB Stage 2 + payload on an internal staging volume
             ├── Curated Linux ISO
             └── Custom unattended mini-Linux installer
                         ↓
                  Final Linux system
```

---

# Important technical decisions to correct or formalize

## 1. Use the Windows Storage Management API for partition resizing

The primary storage backend should be the modern Windows Storage Management API in:

```text
root\Microsoft\Windows\Storage
```

Use these classes:

```text
MSFT_Disk
MSFT_Partition
MSFT_Volume
```

For shrinking NTFS:

1. Call `MSFT_Partition.GetSupportedSize`.
2. Verify the requested size is at or above `SizeMin`.
3. Call `MSFT_Partition.Resize`.
4. Re-query the disk and confirm the resulting unallocated region.

Microsoft documents that `Resize` resizes both the partition and its associated filesystem and supports shrinking NTFS. Microsoft also states that the older Virtual Disk Service API is superseded by this Storage Management API. Therefore, a VDS-based third-party library should not be the main backend. Keep `diskpart.exe` as a tested fallback or diagnostic backend, not the default implementation. ([Microsoft Learn][1])

Recommended abstraction:

```csharp
public interface IStorageManager
{
    Task<SystemStorageSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken);

    Task<ResizeRange> GetResizeRangeAsync(
        PartitionIdentity partition,
        CancellationToken cancellationToken);

    Task<ResizeResult> ResizeAsync(
        PartitionIdentity partition,
        ulong newSizeBytes,
        CancellationToken cancellationToken);
}
```

Implementations could be:

```text
WindowsStorageManagementApiBackend   Primary
DiskPartBackend                      Fallback
FakeStorageBackend                    Unit tests
```

## 2. Treat BCD entries and UEFI NVRAM entries as separate concepts

The earlier discussion occasionally treats a Windows Boot Manager entry and a firmware `Boot####` entry as interchangeable. They are not.

* `{bootmgr}` represents Windows Boot Manager.
* UEFI firmware boot options are stored as `Boot####`, `BootOrder`, and `BootNext` variables in NVRAM.
* Copying or changing `{bootmgr}` is not automatically the same as creating a separate firmware boot option.

Do not overwrite the path of the existing Windows Boot Manager. Instead, implement a dedicated boot-entry spike and verify which model works reliably across target machines:

1. A separate firmware `Boot####` entry pointing to your shim.
2. A supported Windows Boot Manager child boot application.
3. A one-time firmware `BootNext` handoff after creating a permanent entry.

UEFI `BootNext` is particularly attractive because it is tried once and then automatically removed, reducing the chance of an installer boot loop. When operating inside Windows Boot Manager rather than firmware, BCDEdit also supports a one-time `/bootsequence`. ([Microsoft Learn][2])

Create this interface early:

```csharp
public interface IBootEntryManager
{
    Task<BootEntryBackup> BackupAsync(CancellationToken cancellationToken);

    Task<BootEntry> CreateInstallerEntryAsync(
        EfiApplicationDescriptor application,
        CancellationToken cancellationToken);

    Task ScheduleOneTimeBootAsync(
        BootEntry entry,
        CancellationToken cancellationToken);

    Task RemoveAsync(
        BootEntry entry,
        CancellationToken cancellationToken);

    Task RestoreAsync(
        BootEntryBackup backup,
        CancellationToken cancellationToken);
}
```

For direct firmware-variable access, C/C++ Windows APIs can be called from C# through P/Invoke. Windows exposes `SetFirmwareEnvironmentVariableEx`, but manually constructing and maintaining UEFI `Boot####` variables is considerably more complex than simply calling a DLL function. Keep that backend behind the interface and develop it only after the BCD/WMI approach is proven insufficient. ([Microsoft Learn][3])

## 3. Secure Boot requires an owned and maintained trust chain

The production trust chain should eventually be:

```text
Firmware Microsoft UEFI CA
    ↓
Your Microsoft-signed shim
    ↓
Your vendor-signed GRUB
    ↓
Your vendor-signed mini-installer kernel
    ↓
Signed or integrity-verified installer userspace
```

Borrowing a distribution's shim and GRUB is suitable for a limited prototype only when the entire following chain matches that distribution's signing policy. A distribution-signed GRUB is intentionally restricted from loading arbitrary unsigned kernels under Secure Boot.

Also, MOK means **Machine Owner Key**, not “Microsoft MOK.” MokManager allows a user to enroll additional keys, but the enrollment process normally requires authenticated user interaction during boot. That conflicts with a completely unattended first boot unless enrollment has already happened. ([Ubuntu Documentation][4])

Recommended release stages:

* **Prototype:** Secure Boot must be disabled.
* **Technical preview:** Support a tightly matched signed distro boot chain.
* **Production:** Submit your own shim for review/signing, embed your vendor certificate, and sign GRUB and the mini-installer kernel yourself.

## 4. Treat every BitLocker-encrypted volume as unavailable to the preboot payload

Do not equate “BitLocker protection suspended” with “the filesystem has been decrypted.” Microsoft's protection-status API can report protection off when the encryption key is available in clear form on disk; that is different from converting the volume into an ordinary unencrypted NTFS volume. The product should not depend on GRUB or the mini-installer being able to use that state reliably. ([Microsoft Learn][5])

The staging-volume rule should therefore be:

```text
BitLocker conversion status != fully decrypted
    → do not use as installer staging storage
```

Recommended message:

> **The selected installer file is stored on a BitLocker-encrypted volume.**
>
> The preboot installer cannot reliably access this volume before Windows starts. Select an unencrypted internal volume, or allow the application to copy the ISO and installer files to an eligible internal volume.
>
> Only the small first-stage boot files are stored in the EFI System Partition. The ISO or root filesystem will not be copied to the EFI System Partition.

The application should present eligible internal partitions with:

* Free space.
* Filesystem.
* BitLocker conversion status.
* Fixed/removable status.
* Disk model.
* Whether GRUB and the mini-installer support the filesystem.

## 5. “Boot any ISO” must become a curated adapter system

GRUB supports loopback devices, loading another configuration file, and searching by filesystem UUID or marker file. Those mechanisms validate the two-stage design. However, loading an actual ISO installer requires distro-specific kernel paths, initrd paths, and kernel parameters. Ubuntu, for example, uses files under `casper`, while other distributions use different layouts and parameters. ([GNU][6])

Do not initially promise arbitrary ISO support. Define three levels:

```text
Supported       Known distro/version with tested boot adapter
Experimental    Recognized family but unverified release
Unsupported     No matching adapter
```

Each adapter should declare:

```json
{
  "id": "ubuntu-desktop",
  "versions": ["supported-version-pattern"],
  "isoDetection": {
    "volumeLabels": ["..."],
    "requiredFiles": [
      "/casper/vmlinuz",
      "/casper/initrd"
    ]
  },
  "boot": {
    "kernelPath": "/casper/vmlinuz",
    "initrdPath": "/casper/initrd",
    "argumentsTemplate": "boot=casper iso-scan/filename={isoPath} ---"
  },
  "secureBoot": {
    "supported": true,
    "policy": "distribution-signed"
  }
}
```

## 6. Use Stitch for design exploration, not automatic Avalonia implementation

Stitch is useful for creating visual directions, high-fidelity mockups, design tokens, and Figma handoff. Google's documented code export path has been HTML/CSS or Figma, not production Avalonia XAML. Treat it as a design tool, then build the actual controls and animations in Avalonia. ([blog.google][7])

Avalonia's built-in themes are Fluent and Simple. Material.Avalonia is listed as a community theme. You can evaluate it as a starting point, but a faithful Material 3 Expressive implementation will probably require your own control themes, shape tokens, motion system, and responsive layouts. ([Avalonia Docs][8])

---

# Final user workflows

## Workflow A: Install from ISO

### Windows phase

1. User launches the portable application.
2. The application requests elevation.
3. The system analyzer checks:

   * UEFI mode.
   * GPT system disk.
   * Secure Boot state.
   * ESP availability and free space.
   * BitLocker conversion state.
   * Internal storage candidates.
   * Hibernation and reboot readiness.
4. User selects an ISO.
5. The application identifies a supported ISO adapter.
6. It verifies the ISO's hash or at least records a complete SHA-256 hash.
7. If the ISO is on an ineligible encrypted partition, the application offers to copy it to an unencrypted internal staging volume.
8. Optionally, the application lets the user shrink NTFS from Windows and prepare unallocated space before launching the official installer.
9. It creates Stage 2 configuration beside the ISO.
10. It writes Stage 1 and the bootloader files to its own directory on the ESP.
11. It creates the boot entry and schedules a one-time boot.
12. It writes a transaction journal and requests reboot.

### Preboot phase

1. Firmware or Windows Boot Manager loads shim/GRUB.
2. Stage 1 locates the exact staging filesystem and installation ID.
3. Stage 1 loads Stage 2.
4. Stage 2 loads the ISO's kernel and initrd using the selected adapter.
5. The official installer starts.

The official installer remains interactive. Your application cannot guarantee that an arbitrary third-party installer will avoid modifying NTFS, so the Windows app should encourage the user to prepare unallocated space in advance.

---

## Workflow B: Automated rootfs installation

### Windows planning phase

1. User selects a curated distro.
2. User chooses automatic or manual partition planning.
3. User configures:

   * Hostname.
   * Locale.
   * Time zone.
   * Keyboard layout.
   * Initial user.
   * Password policy or first-boot setup.
4. The application downloads or extracts:

   * Boot bundle.
   * Mini-installer kernel/initramfs.
   * Rootfs archive.
5. Every asset is cryptographically verified.
6. The user selects a target disk.
7. Windows queries the supported NTFS shrink range.
8. The app displays its partition map.
9. The app resizes the selected NTFS partition.
10. It re-reads the disk layout.
11. The user plans Linux partitions only inside the newly created unallocated region.
12. The application generates an exact installation plan.
13. It installs the temporary EFI boot path and schedules one-time boot.

### Mini-Linux execution phase

The mini-installer must never ask for input. It runs a deterministic state machine:

```text
Locate staging volume
    ↓
Load and validate install plan
    ↓
Verify disk identity and partition-table fingerprint
    ↓
Verify exact free region still exists
    ↓
Create planned GPT partitions
    ↓
Format filesystems
    ↓
Mount target hierarchy
    ↓
Verify and extract rootfs
    ↓
Generate fstab and system configuration
    ↓
Install final signed bootloader
    ↓
Create final Linux boot entry
    ↓
Write success status and logs
    ↓
Reboot
```

On failure, it must:

1. Stop destructive work immediately.
2. Write a machine-readable failure record.
3. Save human-readable logs to the staging volume.
4. Avoid automatically retrying the installer.
5. Reboot or return so that Windows remains the next normal boot target.

Because the installer was scheduled through a one-time boot operation, a failed installation should not create an infinite reboot cycle.

---

# Partition-planning design

## Automatic “install alongside” mode

For the first release, keep the automatic layout simple:

```text
Existing Windows partitions
Unallocated region created by shrinking NTFS
    └── Linux root partition, ext4
```

Use a swap file inside the root filesystem rather than a dedicated swap partition. That keeps the initial product aligned with the stated “ext4 only” scope and reduces partitioning complexity.

The automatic UI needs:

* A size slider.
* Minimum recommended Linux size.
* Current Windows free-space estimate.
* Supported NTFS shrink limit.
* A visual before/after disk map.
* Final destructive-operation review.

## Manual mode

The manual UI should initially support only:

* Shrinking an existing NTFS partition.
* Planning an ext4 root partition.
* Planning an optional ext4 `/home` partition.
* Adjusting sizes within the newly created free region.

Do not initially support:

* Moving existing partitions.
* Deleting arbitrary existing partitions.
* Shrinking non-NTFS filesystems.
* Dynamic disks.
* Storage Spaces.
* Firmware RAID.
* MBR-to-GPT conversion.
* LUKS encryption.
* Multi-disk Linux layouts.

The UI should model planned Linux partitions without creating them from Windows. The mini-installer creates them at the exact offsets specified in the plan.

---

# Build variants

## Netinstall

Bundled:

```text
Application executable
Public manifest-verification key
Configuration schemas
UI resources
Recovery and diagnostic code
```

Downloaded on demand:

```text
Boot bundle: shim, GRUB, optional MokManager
Mini-installer kernel and initramfs
Selected rootfs
```

This follows your requested definition of a truly small netinstall application.

## Full

Bundled:

```text
Application executable
Boot bundle
Mini-installer kernel and initramfs
Public manifest-verification key
Configuration schemas
```

Downloaded:

```text
Selected rootfs
```

A later third variant could be a distro-specific offline image that also bundles one rootfs, but that would produce a very large executable and complicate updates.

Use an MSBuild property rather than scattered conditional code:

```xml
<PropertyGroup>
  <InstallerFlavor Condition="'$(InstallerFlavor)' == ''">
    NetInstall
  </InstallerFlavor>
</PropertyGroup>

<ItemGroup Condition="'$(InstallerFlavor)' == 'Full'">
  <EmbeddedResource Include="Assets\Boot\**\*" />
  <EmbeddedResource Include="Assets\MiniInstaller\**\*" />
</ItemGroup>
```

Example builds:

```powershell
dotnet publish -c Release -r win-x64 `
  -p:InstallerFlavor=NetInstall `
  -p:PublishSingleFile=true `
  -p:SelfContained=true

dotnet publish -c Release -r win-x64 `
  -p:InstallerFlavor=Full `
  -p:PublishSingleFile=true `
  -p:SelfContained=true
```

The application should expose the flavor in build metadata and diagnostics rather than conditionally changing business logic throughout the codebase.

---

# Configuration examples

## Stage 1 GRUB configuration

Use a generated installation identifier and a known filesystem UUID rather than searching for a generic filename across every disk:

```grub
insmod part_gpt
insmod ntfs
insmod fat

set timeout=3

search --fs-uuid --set=stage2root <STAGING_FILESYSTEM_UUID>

if [ -f ($stage2root)/.linux-installer/<INSTALL_ID>/stage2.cfg ]; then
    configfile ($stage2root)/.linux-installer/<INSTALL_ID>/stage2.cfg
else
    echo "Installer payload not found."
    echo "Installation ID: <INSTALL_ID>"
    sleep 8
    exit
fi
```

The generated GRUB image must contain, or be able to load, every filesystem and partition module required by Stage 1.

## Automated installation plan

Do not tell the mini-installer to “find the largest unallocated area.” Give it an exact region and verify that nothing changed after Windows created it.

```json
{
  "schemaVersion": 1,
  "installId": "8c0147c5-180a-4cc7-b9bd-209ddad5f03a",
  "operation": "rootfs-install",
  "createdUtc": "2026-07-18T08:00:00Z",

  "staging": {
    "filesystemUuid": "STAGING-FS-UUID",
    "directory": "/.linux-installer/8c0147c5-180a-4cc7-b9bd-209ddad5f03a"
  },

  "target": {
    "diskUniqueId": "WINDOWS-STORAGE-UNIQUE-ID",
    "partitionStyle": "gpt",
    "layoutFingerprint": "sha256:...",
    "freeRegion": {
      "startBytes": 536870912000,
      "lengthBytes": 107374182400
    }
  },

  "partitions": [
    {
      "role": "root",
      "filesystem": "ext4",
      "mountPoint": "/",
      "startBytes": 536870912000,
      "sizeBytes": 107374182400
    }
  ],

  "assets": {
    "rootfs": {
      "path": "assets/distro.rootfs.tar.zst",
      "sha256": "..."
    },
    "installerVersion": "1.0.0"
  },

  "system": {
    "hostname": "linux-pc",
    "locale": "en_US.UTF-8",
    "timezone": "Asia/Bangkok",
    "keyboard": "us",
    "swapFileBytes": 8589934592
  }
}
```

Do not store a plaintext account password in this file. Store a suitably generated password hash, or run a first-boot account-creation wizard inside the installed operating system.

## Installer status protocol

The unattended installer should update a separate status file atomically:

```json
{
  "installId": "8c0147c5-180a-4cc7-b9bd-209ddad5f03a",
  "state": "extracting-rootfs",
  "step": 7,
  "totalSteps": 11,
  "lastUpdatedUtc": "2026-07-18T08:15:00Z",
  "error": null
}
```

Final states:

```text
success
failed-validation
failed-partitioning
failed-formatting
failed-extraction
failed-bootloader
cancelled-before-destructive-step
```

---

# Recommended solution structure

```text
src/
  Installer.App/
      Avalonia views, view models, navigation

  Installer.Core/
      Installation workflows
      Domain models
      Validation rules
      Transaction orchestration

  Installer.Windows/
      Storage Management API
      BitLocker provider
      ESP mounting
      BCD and firmware providers
      Privilege handling
      Power and reboot handling

  Installer.Assets/
      Manifest parsing
      Downloading
      Hash and signature verification
      Embedded-resource extraction

  Installer.Boot/
      GRUB template generation
      ISO adapters
      Boot-entry transaction logic

  Installer.Contracts/
      install-plan schema
      status schema
      distro manifest schema

installer/
  MiniInstaller/
      Separate source or submodule
      Build recipes
      Boot scripts
      State machine

manifests/
  distros/
  iso-adapters/
  boot-assets/

tests/
  Unit/
  Integration/
  VirtualMachine/
  DestructiveLab/
```

Keep all platform operations behind interfaces so the planner can be tested without touching real disks or boot configuration.

---

# Multi-phase development plan

## Phase 0 — Product specification and safety model

Define the supported matrix before writing destructive code.

Initial scope should be:

```text
Windows 10/11
x86-64
UEFI
GPT
Basic fixed disks
Existing ESP
NTFS Windows source partition
ext4 Linux target
One target disk per installation
```

Document explicit exclusions. Establish these non-negotiable invariants:

1. Never overwrite Microsoft's EFI files.
2. Never modify a partition not identified in the reviewed plan.
3. Never infer a destructive target from size alone.
4. Never continue if the disk layout fingerprint changed.
5. Always preserve a Windows boot path.
6. Always have a transaction journal and rollback path.
7. Never automatically repeat a failed destructive installation.

**Exit gate:** A written compatibility matrix, threat model, boot-entry decision document, and installation-plan schema.

## Phase 1 — Application foundation and UI system

Build the Avalonia shell using MVVM and dependency injection.

Screens:

```text
Welcome
System analysis
Workflow selection
Distro or ISO selection
Staging-volume selection
Partition planning
System configuration
Review
Execution progress
Ready to reboot
Recovery and cleanup
```

Create a design-token layer:

```text
Colors
Typography
Corner shapes
Spacing
Elevation
Motion durations and easing
State layers
Light/dark variants
```

Build the partition map as a dedicated custom control. It should represent block sizes proportionally, distinguish existing partitions from planned partitions, and make destructive operations visually obvious.

**Exit gate:** The complete workflow is navigable with mocked system services and no disk modifications.

## Phase 2 — Read-only system analyzer

Implement only discovery:

* Administrator status.
* Firmware mode.
* Secure Boot state.
* ESP location and capacity.
* Physical disks and stable identities.
* Partition topology.
* Volume/filesystem mapping.
* BitLocker conversion and protection states.
* Internal versus removable media.
* Supported resize limits.
* Hibernation/reboot readiness.

Add “Export diagnostics” so test users can provide a redacted JSON report.

**Exit gate:** On every test machine, the app's disk map matches Windows disk-management tools without making any changes.

## Phase 3 — Asset and release pipeline

Define a signed release manifest. Do not directly trust a mutable `latest` asset URL. It can return the latest signed manifest, but that manifest should contain pinned asset versions, lengths, hashes, and signatures.

Features:

* Resumable downloads.
* `.partial` files.
* SHA-256 verification.
* Manifest-signature verification.
* Atomic rename after verification.
* Download retry with backoff.
* Embedded-resource extraction for Full builds.
* Storage-space estimation before download.
* Version compatibility checks between app, boot bundle, installer, and rootfs.

**Exit gate:** Netinstall and Full artifacts are reproducibly produced, and corrupted or substituted assets are rejected.

## Phase 4 — Transactional EFI boot proof

Do not begin with partitioning. First prove the boot chain using a harmless mini-environment that writes a success marker and returns.

Implement:

1. BCD backup.
2. ESP mount.
3. Free-space check.
4. Atomic creation of your EFI directory.
5. Stage 1 generation.
6. Boot-entry creation.
7. Entry re-enumeration and verification.
8. One-time boot scheduling.
9. ESP unmount.
10. Rollback after any failure.

The test mini-environment should only:

```text
Boot
Locate staging volume
Write boot-success.json
Reboot
```

**Exit gate:** Repeated boot tests succeed across virtual and physical systems, Windows remains bootable after every induced failure, and cleanup restores the original state.

## Phase 5 — Curated ISO MVP

Support exactly one ISO family and release line initially.

Features:

* ISO selection.
* Distro adapter detection.
* ISO hash recording.
* BitLocker staging relocation.
* Stage 2 generation.
* Optional Windows-side NTFS shrink.
* One-time boot.
* Temporary-entry cleanup.

Do not expose “any ISO” in the primary UI yet.

**Exit gate:** The selected supported ISO boots consistently from an internal, unencrypted staging partition with no removable media.

## Phase 6 — Windows partition planner

Implement the automatic and manual workflows using the Storage Management API.

Automatic:

```text
Select Windows partition
Query SizeMin
Choose Linux allocation
Show before/after
Resize
Re-scan
Create exact plan
```

Manual:

```text
Resize NTFS
Display resulting free region
Plan root and optional home
Validate alignment and minimum sizes
Generate exact offsets
```

Every resize must be followed by a full storage rescan. The installation plan is generated only from the post-resize topology.

**Exit gate:** The app creates predictable free regions on all supported test systems and never resizes beyond the API-reported limit.

## Phase 7 — Mini-installer project

Maintain the mini-installer as a separate project with reproducible builds.

A practical first implementation is:

```text
Broadly compatible distro kernel
Custom initramfs using dracut or initramfs-tools
BusyBox or minimal userspace
sfdisk or parted
e2fsprogs
zstd/tar
blkid
mount
chroot
bootloader tools
JSON parser or a very small custom parser
```

Using a broadly packaged kernel first reduces hardware-driver risk. A more tightly minimized Buildroot userspace can come after the installation state machine is stable.

Implement the mini-installer as explicit, restart-aware phases. Every step writes status before and after execution.

**Exit gate:** In a VM, the installer can consume a plan, create ext4, extract a test rootfs, configure it, install its bootloader, and return success with no input.

## Phase 8 — Automated distro integration

Connect the Windows planner to the mini-installer.

Add:

* Distro catalog.
* Rootfs manifest.
* Rootfs archive validation.
* User/system settings.
* Exact disk plan.
* Final bootloader configuration.
* First-boot or pre-created user flow.
* Success/failure result display when Windows or Linux next starts.

Rootfs archives must preserve:

```text
Numeric ownership
Permissions
Symlinks
Extended attributes
Linux capabilities
Device-node policy
```

**Exit gate:** A supported distro installs from selection to first boot without removable media or mini-installer interaction.

## Phase 9 — Production Secure Boot

Replace the prototype trust model with the production chain.

Work includes:

* Vendor key management.
* Reproducible shim and GRUB builds.
* Shim review submission.
* Microsoft signing process.
* GRUB and kernel signing.
* SBAT metadata and revocation planning.
* Boot-asset update mechanism.
* Secure-Boot-enabled hardware testing.
* Optional MOK recovery flow, clearly separated from unattended installation.

**Exit gate:** The complete chain boots on supported Secure Boot systems without asking the user to disable Secure Boot or enroll a MOK.

## Phase 10 — Recovery, cleanup, QA, and release

Implement:

* Temporary boot-entry removal.
* Temporary ESP-directory cleanup.
* Staging-payload cleanup.
* Transaction recovery.
* Resume after Windows-side interruption.
* Failure report import.
* Installation log viewer.
* “Remove installer components” operation.
* A separate and clearly labeled Linux-removal workflow later.

The ordinary cleanup command must never delete Linux partitions.

Test at minimum:

```text
Single disk and multiple disks
NVMe and SATA
512e and 4Kn where available
Small ESP
BitLocker C: plus unencrypted D:
No eligible staging volume
Insufficient shrink range
Power loss after each transaction step
Corrupted rootfs
Changed partition table after planning
Secure Boot enabled and disabled
Firmware that ignores or reorders boot entries
Windows updates after temporary boot setup
```

**Exit gate:** A release candidate passes automated VM tests and destructive physical-lab tests with documented recovery procedures.

---

# Information still needing specification

These decisions remain open and should be written into the project specification before the relevant phase begins:

1. **Product name and stable EFI vendor directory**, such as `\EFI\<VendorName>\`.
2. **Initial supported Windows versions and architectures.**
3. **Whether the first public prototype requires Secure Boot to be disabled.**
4. **The first supported ISO family and exact releases.**
5. **The first automated distro and rootfs format.**
6. **The mini-installer build system: dracut, initramfs-tools, Buildroot, or another approach.**
7. **Final Linux boot policy:** leave Windows first, make Linux first, or ask the user.
8. **Automatic allocation defaults and minimum sizes.**
9. **Whether `/home` is supported in the first manual release.**
10. **Initial-user handling:** pre-created account or first-boot setup.
11. **Password-hash format and secret-handling policy.**
12. **Rootfs update and retention policy.**
13. **Licensing and redistribution rights for shim, GRUB, MokManager, kernels, firmware, and rootfs assets.**
14. **Failure recovery after partitions have been created but rootfs extraction fails.**
15. **Whether the app may use a small elevated helper process instead of running the whole UI as Administrator.**
16. **Whether telemetry exists; diagnostics should be local and redacted by default.**

The strongest first milestone is **a read-only system analyzer followed by a harmless one-time boot proof**. That validates the two most hardware-sensitive layers—Windows storage discovery and firmware handoff—before any partition is resized or formatted.

[1]: https://learn.microsoft.com/en-us/windows/win32/vds/virtual-disk-service-portal?utm_source=chatgpt.com "Virtual Disk Service - Win32 apps - Microsoft Learn"
[2]: https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/bcd-system-store-settings-for-uefi?view=windows-11&utm_source=chatgpt.com "BCD System Store Settings for UEFI"
[3]: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setfirmwareenvironmentvariableexa?utm_source=chatgpt.com "SetFirmwareEnvironmentVariabl..."
[4]: https://documentation.ubuntu.com/security/security-features/platform-protections/secure-boot/?utm_source=chatgpt.com "UEFI Secure Boot"
[5]: https://learn.microsoft.com/en-us/windows/win32/secprov/getprotectionstatus-win32-encryptablevolume?utm_source=chatgpt.com "GetProtectionStatus method of the ..."
[6]: https://www.gnu.org/software/grub/manual/grub/html_node/loopback.html "loopback (GNU GRUB Manual 2.14)"
[7]: https://blog.google/innovation-and-ai/models-and-research/google-labs/stitch-ai-ui-design/?utm_source=chatgpt.com "Introducing “vibe design” with Stitch"
[8]: https://docs.avaloniaui.net/docs/styling/themes?utm_source=chatgpt.com "Themes | Avalonia Docs"

